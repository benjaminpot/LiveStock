using System; // 提供 Predicate 等基本型別。
using System.Collections.Generic; // 提供 List、Queue、HashSet 等集合。
using UnityEngine; // 提供 MonoBehaviour、Time、Mathf、Random、Debug 等 Unity 功能。
using UnityEngine.AI; // 提供 NavMeshAgent、NavMeshPath 與 NavMesh 靜態尋路功能。

/// <summary>目前遊戲裡支援的五種動物。</summary>
public enum FarmAnimalType
{
    Chicken, // 雞：吃穀物。
    Horse,   // 馬：吃草。
    Sheep,   // 羊：吃草。
    Dog,     // 狗：吃一般食物。
    Cow      // 牛：吃草。
}

/// <summary>
/// LIVEStock 的生理需求式 NPC。
/// 它只管理飢餓、口渴、尋找資源與閒晃，不包含巡邏任務、任務完成度或任務 UI。
/// 需要同一個 GameObject 上有 NavMeshAgent，並且場景內有可達的 AnimalWaypoint。
/// </summary>
[RequireComponent(typeof(NavMeshAgent))] // Unity 會要求這個物件必須有 NavMeshAgent 元件。
public class AnimalNPC : MonoBehaviour
{
    [Header("動物設定")]
    public FarmAnimalType animalType = FarmAnimalType.Sheep; // 指定這隻 NPC 的種類，決定食物偏好。
    public FarmZone homeZone = FarmZone.Grassland; // 平常閒晃時偏好的生活區域。
    public NavMeshAgent agent; // 負責根據 NavMesh 尋路並移動的元件。
    public AnimalWaypoint[] waypoints; // 場景裡可供這隻 NPC 使用的目標點；留空會自動搜尋。

    [Header("生理需求（0 = 不餓／不渴，100 = 非常餓／渴）")]
    [Range(0f, 100f)] public float hunger = 20f; // 飢餓值；越高越需要找食物。
    [Range(0f, 100f)] public float thirst = 20f; // 口渴值；越高越需要找水。
    [Min(0f)] public float hungerIncreasePerSecond = 0.28f; // 每秒增加的飢餓值。
    [Min(0f)] public float thirstIncreasePerSecond = 0.42f; // 每秒增加的口渴值。
    [Range(1f, 100f)] public float hungryThreshold = 60f; // 飢餓值到達這裡就嘗試尋找食物。
    [Range(1f, 100f)] public float thirstyThreshold = 60f; // 口渴值到達這裡就優先嘗試尋找水源。
    [Min(1f)] public float hungerRelief = 55f; // 抵達食物點並完成停留後，降低多少飢餓值。
    [Min(1f)] public float thirstRelief = 70f; // 抵達水源點並完成停留後，降低多少口渴值。

    [Header("思考與停留")]
    [Min(0f)] public float actionDuration = 2f; // 到達食物／水源後停留幾秒，模擬進食或喝水。
    [Min(0.1f)] public float thinkInterval = 1f; // 每隔幾秒檢查是否有更急迫的需求，不必每一幀重新判斷。
    [Min(0.1f)] public float retryInterval = 3f; // 找不到可達目標時，等待幾秒後重試。

    [Header("路徑變化與避免重複")]
    [Min(0)] public int recentWaypointMemory = 4; // 記住最近造訪的幾個點；若有其他候選，會優先避開這些點。
    [Range(0f, 1f)] public float intermediateRouteChance = 0.65f; // 這次移動改經過 Route 中途點的機率；0=不繞路，1=有合適路線就繞。
    [Min(1f)] public float maxRouteDetourMultiplier = 1.8f; // 經過中途點的總路程不可超過直接路徑的幾倍，避免繞太遠。
    [Min(0.1f)] public float minimumRouteLegDistance = 2f; // 起點到中途點、以及中途點到終點，至少要有這段距離才算有意義。
    [Min(0f)] public float distancePenalty = 0.025f; // 距離權重：數字越高越偏好近處，但仍保留隨機性。

    [Header("除錯資訊（只讀）")]
    [SerializeField] private string currentBehavior = "待命"; // 在 Inspector 顯示目前行為，方便測試。
    public string CurrentBehavior => currentBehavior; // 其他腳本可以讀取行為名稱，但不能任意改寫它。

    private AnimalWaypoint currentTarget; // 目前這一段正在前往的目標，可能是中途點或真正終點。
    private AnimalWaypoint finalTarget; // 這次移動真正想抵達的目標，例如水源或食物點。
    private bool isTravellingViaRoute; // true 表示 currentTarget 是 Route 中途點，抵達後還要繼續前往 finalTarget。
    private float actionTimer; // 抵達後的停留倒數計時器。
    private float nextThinkTime; // 下次可以重新評估需求的時間。
    private float retryAfter; // 下次可以重新尋找目標的時間。

    // Queue 保存造訪順序；HashSet 讓程式可以快速判斷某個點是否在最近造訪清單內。
    private readonly Queue<int> recentWaypointQueue = new Queue<int>();
    private readonly HashSet<int> recentWaypointIds = new HashSet<int>();

    /// <summary>儲存候選 Waypoint 與從目前位置走到它的路徑長度。</summary>
    private sealed class WaypointCandidate
    {
        public AnimalWaypoint waypoint; // 這個候選目標本身。
        public float pathLength; // NavMesh 計算出的路徑長度，用來偏好較近的點。

        public WaypointCandidate(AnimalWaypoint waypoint, float pathLength)
        {
            this.waypoint = waypoint; // 保存傳入的目標點。
            this.pathLength = pathLength; // 保存對應路徑距離。
        }
    }

    private void Awake()
    {
        if (agent == null) // 若沒有在 Inspector 指定 NavMeshAgent……
            agent = GetComponent<NavMeshAgent>(); // ……就從同一個 GameObject 取得。

        RefreshWaypointsIfNeeded(); // 若目標點清單尚未設定，就搜尋場景中的 AnimalWaypoint。
    }

    private void Start()
    {
        if (agent == null || !agent.isOnNavMesh) // 檢查 NPC 是否有尋路元件，且起始位置位於已烘焙的 NavMesh 上。
        {
            Debug.LogWarning($"[{name}] 沒有可用的 NavMeshAgent，或 NPC 起始位置不在 NavMesh 上。", this); // 在 Console 提醒需要修正的設定。
            enabled = false; // 關閉此腳本，避免一直執行無效的 AI。
            return; // 結束初始化。
        }

        nextThinkTime = Time.time; // 允許 NPC 一開始就判斷需求並選擇目的地。
        ChooseAndMove(); // 依目前飢餓／口渴程度，選擇第一個可達目標。
    }

    private void Update()
    {
        if (agent == null || !agent.isOnNavMesh) // 若 Agent 不存在或暫時離開 NavMesh……
            return; // ……這一幀就不處理移動邏輯。

        // 需求按照經過的時間增加，不會因為電腦 FPS 不同而直接改變增加速度。
        hunger = Mathf.Min(100f, hunger + hungerIncreasePerSecond * Time.deltaTime); // 增加飢餓值，最高不超過 100。
        thirst = Mathf.Min(100f, thirst + thirstIncreasePerSecond * Time.deltaTime); // 增加口渴值，最高不超過 100。

        if (actionTimer > 0f) // 如果 NPC 還在進食／喝水的停留時間內……
        {
            actionTimer -= Time.deltaTime; // 依時間減少剩餘停留秒數。
            agent.isStopped = true; // 停在原地，模擬進食或喝水。
            if (actionTimer > 0f) // 如果停留時間還沒結束……
                return; // ……暫時不重新選目的地。
        }

        agent.isStopped = false; // 停留結束後，允許 Agent 移動。

        if (currentTarget != null && Time.time >= nextThinkTime) // NPC 正在移動，而且到了重新思考的時間……
        {
            nextThinkTime = Time.time + thinkInterval; // 排定下一次思考時間。
            if (ShouldInterruptForNeeds()) // 檢查是否出現比目前目標更急迫的生理需求。
            {
                ClearCurrentPlan(); // 清除舊的目標和路徑。
                ChooseAndMove(); // 重新依照需求優先順序選擇目標。
                return; // 這一幀不再檢查舊目標。
            }
        }

        if (currentTarget != null && !agent.pathPending) // 有目標，而且 Agent 已完成目前的路徑計算……
        {
            if (agent.pathStatus != NavMeshPathStatus.PathComplete && agent.hasPath) // 如果已有路徑卻不是完整路徑……
            {
                Debug.LogWarning($"[{name}] 前往 {currentTarget.name} 的路徑不完整；請檢查 NavMesh 或橋樑連接。", currentTarget); // 提醒路徑可能被河流或障礙切斷。
                ClearCurrentPlan(); // 放棄這次路徑。
                retryAfter = Time.time + retryInterval; // 等待一段時間，避免重複刷錯誤。
            }
            else if (HasReachedCurrentTarget()) // 若已到達本段目標……
            {
                HandleArrival(); // 判斷這是中途點還是真正終點，執行相應處理。
            }
        }

        if (currentTarget == null && actionTimer <= 0f && Time.time >= retryAfter) // 沒有正在前往的目標、沒有停留動作，而且重試時間已到……
            ChooseAndMove(); // ……就重新決定目的地。
    }

    /// <summary>在移動途中檢查是否要因為口渴／飢餓而改道。</summary>
    private bool ShouldInterruptForNeeds()
    {
        if (finalTarget == null) // 沒有真正終點，代表目前沒有有效計畫。
            return true; // 允許重選目標。

        if (thirst >= thirstyThreshold && finalTarget.waypointType != AnimalWaypointType.Water) // 口渴已達門檻，但原目標不是水源……
            return true; // ……就改去喝水。

        if (thirst < thirstyThreshold && hunger >= hungryThreshold && !IsFoodTargetForThisAnimal(finalTarget.waypointType)) // 口渴不急迫、但很餓，而且原目標不是適合自己的食物……
            return true; // ……就改去找食物。

        return false; // 沒有更急迫的需求，不打斷目前移動。
    }

    /// <summary>選擇下一個目的地。優先順序：口渴 > 飢餓 > 閒晃。</summary>
    private void ChooseAndMove()
    {
        RefreshWaypointsIfNeeded(); // 若清單為空，再次嘗試尋找場景中的目標點。
        if (waypoints == null || waypoints.Length == 0) // 如果場景裡沒有任何 AnimalWaypoint……
        {
            SetNoTarget("找不到 Waypoint，稍後重試"); // 顯示原因並設定重試時間。
            return; // 沒有目標可選，結束。
        }

        AnimalWaypoint goal = null; // 暫存最終目的地。

        if (thirst >= thirstyThreshold) // 口渴達門檻時先找水源。
        {
            goal = ChooseWaterTarget(); // 從所有可達且可用的水源點中隨機加權選擇。
            if (goal == null && hunger >= hungryThreshold) // 如果沒有可達水源，而且也很餓……
                goal = ChooseFoodTarget(); // ……允許先找食物作為退路。

            if (goal == null) // 若最急迫的需求沒有找到任何可達目標……
            {
                SetNoTarget("找不到可達水源，稍後重試"); // 不亂走到錯誤的資源點，稍後再試。
                return; // 結束這次選擇。
            }
        }
        else if (hunger >= hungryThreshold) // 沒有急迫口渴需求，但飢餓達門檻……
        {
            goal = ChooseFoodTarget(); // ……尋找符合動物食物偏好的可達食物點。
            if (goal == null) // 如果沒有任何可達食物……
            {
                SetNoTarget("找不到可達食物，稍後重試"); // 顯示狀態並延後重試。
                return; // 結束這次選擇。
            }
        }
        else // 飢餓與口渴都不急迫……
        {
            goal = ChooseWanderTarget(); // ……從生活區附近的閒晃點中隨機選擇。
            if (goal == null) // 如果完全沒有可達閒晃點……
            {
                SetNoTarget("找不到可達閒晃點，稍後重試"); // 顯示問題，方便在 Editor 中補 waypoint。
                return; // 結束這次選擇。
            }
        }

        StartMovingTo(goal); // 把最終目標設定好，並視情況在中途加入一個隨機 Route 點。
    }

    /// <summary>選擇一個可達水源；多個水源間加權隨機，並盡量避免最近走過的點。</summary>
    private AnimalWaypoint ChooseWaterTarget()
    {
        List<WaypointCandidate> candidates = BuildCandidates(point => point.waypointType == AnimalWaypointType.Water); // 找出所有可用且路徑完整的 Water 點。
        return SelectWeightedRandomCandidate(candidates); // 回傳加權隨機選中的水源，沒有候選點則回傳 null。
    }

    /// <summary>選擇一個符合動物種類的可達食物點。</summary>
    private AnimalWaypoint ChooseFoodTarget()
    {
        List<WaypointCandidate> candidates = BuildCandidates(point => IsFoodTargetForThisAnimal(point.waypointType)); // 根據動物種類篩出適合的食物。
        return SelectWeightedRandomCandidate(candidates); // 用權重與距離做隨機選擇。
    }

    /// <summary>閒晃時先找生活區內可達的點；如果沒有，才擴大到全地圖。</summary>
    private AnimalWaypoint ChooseWanderTarget()
    {
        List<WaypointCandidate> candidates = BuildCandidates(point => IsWanderPointForThisAnimal(point) && point.zone == homeZone); // 先選本區的 Wander 或舊版相容點。
        if (candidates.Count == 0) // 如果生活區沒有可用的候選點……
            candidates = BuildCandidates(IsWanderPointForThisAnimal); // ……再搜尋其他區域可達的閒晃點。

        return SelectWeightedRandomCandidate(candidates); // 從候選點中隨機選擇，並避免短時間重複造訪。
    }

    /// <summary>判斷這種 waypoint 能不能當作當前動物的閒晃位置。</summary>
    private bool IsWanderPointForThisAnimal(AnimalWaypoint point)
    {
        if (point == null) return false; // 空點不可用。
        if (point.waypointType == AnimalWaypointType.Wander) return true; // 一般閒晃點給所有動物使用。
        if (animalType == FarmAnimalType.Horse && point.waypointType == AnimalWaypointType.HorseRun) return true; // 舊版馬跑訪點改成馬的閒晃點。
        if (animalType == FarmAnimalType.Dog && point.waypointType == AnimalWaypointType.DogPatrol) return true; // 舊版狗巡邏點改成狗的閒晃點。
        return false; // 其他舊版任務類型不作為一般閒晃點。
    }

    /// <summary>判斷指定類型是否為這種動物可以吃的食物。</summary>
    private bool IsFoodTargetForThisAnimal(AnimalWaypointType type)
    {
        switch (animalType) // 依動物種類套用食物偏好。
        {
            case FarmAnimalType.Chicken:
                return type == AnimalWaypointType.ChickenFeed; // 雞優先吃穀物。
            case FarmAnimalType.Horse:
                return type == AnimalWaypointType.Grass; // 馬吃草。
            case FarmAnimalType.Sheep:
                return type == AnimalWaypointType.Grass || type == AnimalWaypointType.SheepGrass; // 羊吃草，也相容舊版 SheepGrass 點。
            case FarmAnimalType.Cow:
                return type == AnimalWaypointType.Grass; // 牛吃草。
            case FarmAnimalType.Dog:
                return type == AnimalWaypointType.Food; // 狗使用一般 Food 點。
            default:
                return false; // 未知類型不會找到食物。
        }
    }

    /// <summary>
    /// 從符合條件的 waypoint 建立候選清單。
    /// 每個候選都先計算一次完整 NavMesh 路徑，所以不會正常選到明顯不可達的點。
    /// </summary>
    private List<WaypointCandidate> BuildCandidates(Predicate<AnimalWaypoint> filter)
    {
        List<WaypointCandidate> candidates = new List<WaypointCandidate>(); // 建立空清單存放合格目標。
        if (waypoints == null) return candidates; // 沒有 waypoint 清單時回傳空清單。

        foreach (AnimalWaypoint point in waypoints) // 檢查每一個已註冊的目標點。
        {
            if (point == null || !point.available) continue; // 忽略已刪除或被停用的目標。
            if (!filter(point)) continue; // 忽略不符合本次需求的類型／區域。

            if (TryGetPath(point.transform.position, out NavMeshPath path, out float pathLength)) // 計算 NPC 到這個目標的路徑。
                candidates.Add(new WaypointCandidate(point, pathLength)); // 只有路徑完整可達才加入候選清單。
        }

        return candidates; // 回傳所有可達候選。
    }

    /// <summary>
    /// 加權隨機選擇候選點。
    /// 如果有未在最近記憶清單裡的點，就先排除最近造訪過的點；只有沒有其他選擇時才重複使用。
    /// </summary>
    private AnimalWaypoint SelectWeightedRandomCandidate(List<WaypointCandidate> candidates)
    {
        if (candidates == null || candidates.Count == 0) return null; // 沒有候選點時回傳 null。

        List<WaypointCandidate> freshCandidates = new List<WaypointCandidate>(); // 建立「最近沒去過」的候選清單。
        foreach (WaypointCandidate candidate in candidates) // 逐一檢查候選點。
        {
            if (!recentWaypointIds.Contains(candidate.waypoint.GetInstanceID())) // 如果它不在最近造訪的點裡……
                freshCandidates.Add(candidate); // ……就優先保留。
        }

        List<WaypointCandidate> pool = freshCandidates.Count > 0 ? freshCandidates : candidates; // 有新點就用新點；若全部都去過，才允許重複。
        float totalWeight = 0f; // 計算全部候選的總權重。

        foreach (WaypointCandidate candidate in pool) // 逐一計算每個候選的有效權重。
            totalWeight += GetEffectiveWeight(candidate); // 距離越近通常越容易中，但隨機性仍然存在。

        if (totalWeight <= 0f) return pool[UnityEngine.Random.Range(0, pool.Count)].waypoint; // 防止權重異常時仍能隨機選一點。

        float roll = UnityEngine.Random.value * totalWeight; // 在總權重範圍內抽一個隨機數。
        foreach (WaypointCandidate candidate in pool) // 依序扣除每個候選的權重。
        {
            roll -= GetEffectiveWeight(candidate); // 抽籤數落入此候選的權重範圍時，就選它。
            if (roll <= 0f) return candidate.waypoint; // 回傳本次抽中的目標點。
        }

        return pool[pool.Count - 1].waypoint; // 因浮點數誤差未提早回傳時，回傳最後一個候選作為保底。
    }

    /// <summary>把 Inspector 設定的權重和距離整合成最終抽選權重。</summary>
    private float GetEffectiveWeight(WaypointCandidate candidate)
    {
        float authoredWeight = Mathf.Max(0.1f, candidate.waypoint.selectionWeight); // 確保基本權重至少為 0.1，避免候選永遠無法抽中。
        float distanceFactor = 1f + candidate.pathLength * distancePenalty; // 路徑越長，分母越大，抽選權重越低。
        return authoredWeight / distanceFactor; // 回傳兼顧手動權重與距離偏好的有效權重。
    }

    /// <summary>開始前往真正目標，並以機率決定是否加一個隨機 Route 中途點。</summary>
    private void StartMovingTo(AnimalWaypoint goal)
    {
        finalTarget = goal; // 記住這趟旅程真正想去的地方，例如喝水或吃草的位置。
        currentTarget = goal; // 預設直接走向真正目標。
        isTravellingViaRoute = false; // 預設沒有中途站。

        if (goal.waypointType != AnimalWaypointType.Route && TryChooseIntermediateRoute(goal, out AnimalWaypoint routePoint)) // 若是一般目的地，且找到合理的中途路線……
        {
            currentTarget = routePoint; // 先把目前目標改成中途點。
            isTravellingViaRoute = true; // 標記抵達後還要前往真正終點。
        }

        agent.isStopped = false; // 確保動物可以開始移動。
        if (!agent.SetDestination(currentTarget.transform.position)) // 要求 NavMeshAgent 前往本段目標；若設定失敗……
        {
            Debug.LogWarning($"[{name}] 無法設定目標 {currentTarget.name}。", currentTarget); // 在 Console 提示可能的尋路問題。
            ClearCurrentPlan(); // 清除失敗的路徑計畫。
            retryAfter = Time.time + retryInterval; // 延遲重試。
            currentBehavior = "設定路徑失敗，稍後重試"; // 更新 Inspector 顯示的狀態。
            return; // 結束本次行動。
        }

        currentBehavior = isTravellingViaRoute ? "沿隨機中途路線移動" : GetTravelBehavior(goal); // 顯示目前是繞行中或直接前往目標。
        nextThinkTime = Time.time + thinkInterval; // 安排下一次需求檢查。
    }

    /// <summary>
    /// 挑選一個合理的 Route waypoint 作為中途點。
    /// 必須同時能走到中途點和從中途點走到真正終點，且不能比直達路徑繞太遠。
    /// </summary>
    private bool TryChooseIntermediateRoute(AnimalWaypoint goal, out AnimalWaypoint chosenRoute)
    {
        chosenRoute = null; // 預設沒有合適中途點。
        if (intermediateRouteChance <= 0f || UnityEngine.Random.value > intermediateRouteChance) return false; // 依 Inspector 設定的機率決定這趟要不要繞路。
        if (goal == null || agent == null || !agent.isOnNavMesh) return false; // 資料無效時不能規劃路線。

        if (!TryGetPath(goal.transform.position, out NavMeshPath directPath, out float directLength)) return false; // 先確保直達目標本身可達，並取得最短路徑長度。
        if (directLength < minimumRouteLegDistance * 2f) return false; // 目標太近時，中途點沒有意義。

        List<WaypointCandidate> routeCandidates = new List<WaypointCandidate>(); // 收集合適的路線中途點。
        if (waypoints == null) return false; // 沒有 waypoint 清單就不能找中途點。

        foreach (AnimalWaypoint point in waypoints) // 檢查所有 waypoint。
        {
            if (point == null || !point.available || point.waypointType != AnimalWaypointType.Route) continue; // 只允許可用的 Route 類型當中途點。
            if (point == goal) continue; // 中途點不能就是最終目的地。

            if (!TryGetPath(point.transform.position, out NavMeshPath toRoutePath, out float toRouteLength)) continue; // 確認從目前 NPC 位置能完整走到這個中途點。
            if (toRouteLength < minimumRouteLegDistance) continue; // 中途點太靠近目前位置，就不拿來增加路線變化。

            NavMeshPath routeToGoalPath = new NavMeshPath(); // 準備計算從中途點到最終目的地的路徑。
            bool secondPathFound = NavMesh.CalculatePath(point.transform.position, goal.transform.position, agent.areaMask, routeToGoalPath); // 使用 Unity 靜態尋路計算兩個位置之間的路徑。
            if (!secondPathFound || routeToGoalPath.status != NavMeshPathStatus.PathComplete) continue; // 中途點若無法接到終點，就排除。

            float routeToGoalLength = GetPathLength(routeToGoalPath); // 算出中途點到終點的路程。
            if (routeToGoalLength < minimumRouteLegDistance) continue; // 第二段太短就沒有足夠的路線變化。

            float totalDetourLength = toRouteLength + routeToGoalLength; // 估算經由中途點的總距離。
            if (totalDetourLength > directLength * maxRouteDetourMultiplier) continue; // 若繞路超過允許倍率，排除這個中途點。
            if (recentWaypointIds.Contains(point.GetInstanceID())) continue; // 優先不使用剛剛才造訪過的路線點。

            routeCandidates.Add(new WaypointCandidate(point, toRouteLength)); // 把合格路線點加入候選，並使用第一段路長作為距離權重。
        }

        // 如果每個路線點都在最近記憶裡，可以放寬限制再找一次，避免永遠不會使用路線點。
        if (routeCandidates.Count == 0) // 沒有未造訪的中途點……
        {
            foreach (AnimalWaypoint point in waypoints) // ……允許重新檢查全部 Route 點作為保底。
            {
                if (point == null || !point.available || point.waypointType != AnimalWaypointType.Route || point == goal) continue; // 排除無效點與終點。
                if (!TryGetPath(point.transform.position, out NavMeshPath toRoutePath, out float toRouteLength)) continue; // 確認能走到中途點。
                if (toRouteLength < minimumRouteLegDistance) continue; // 排除離起點太近的點。

                NavMeshPath routeToGoalPath = new NavMeshPath(); // 建立第二段路徑。
                if (!NavMesh.CalculatePath(point.transform.position, goal.transform.position, agent.areaMask, routeToGoalPath) || routeToGoalPath.status != NavMeshPathStatus.PathComplete) continue; // 確認從中途點到終點可達。
                float routeToGoalLength = GetPathLength(routeToGoalPath); // 取得第二段距離。
                if (routeToGoalLength < minimumRouteLegDistance) continue; // 排除離終點太近的中途點。
                if (toRouteLength + routeToGoalLength > directLength * maxRouteDetourMultiplier) continue; // 不能繞太遠。

                routeCandidates.Add(new WaypointCandidate(point, toRouteLength)); // 加入保底候選。
            }
        }

        chosenRoute = SelectWeightedRandomCandidate(routeCandidates); // 從有效 Route 點中加權隨機選一個。
        return chosenRoute != null; // 有選到中途點就回傳 true，否則直接前往終點。
    }

    /// <summary>確認抵達的是中途 Route 點，還是最終資源／閒晃目標。</summary>
    private void HandleArrival()
    {
        if (currentTarget == null) return; // 沒有當前目標時不做事。

        if (isTravellingViaRoute) // 如果目前抵達的是中途點……
        {
            RememberWaypoint(currentTarget); // 記住剛剛經過的 Route 點，避免下一趟馬上重複。
            currentTarget = finalTarget; // 下一段改以真正目的地作為目前目標。
            isTravellingViaRoute = false; // 已經離開中途路線，接下來直接前往終點。

            if (currentTarget != null && agent.SetDestination(currentTarget.transform.position)) // 設定下一段目的地。
            {
                currentBehavior = GetTravelBehavior(currentTarget); // 顯示例如前往水源或尋找食物。
                return; // 不在中途點進食／喝水，繼續走向真正目標。
            }

            ClearCurrentPlan(); // 若無法設定第二段路徑，就放棄此計畫。
            retryAfter = Time.time + retryInterval; // 稍後再重新找目標。
            currentBehavior = "中途路線後無法抵達終點，稍後重試"; // 提示開發者檢查路徑。
            return; // 結束處理。
        }

        AnimalWaypoint reachedPoint = currentTarget; // 暫存真正抵達的終點。
        RememberWaypoint(reachedPoint); // 記錄這次到訪，讓下次選點優先避開近期位置。
        agent.ResetPath(); // 清除已完成的路徑。
        agent.isStopped = true; // 到點後停止，開始模擬進食／喝水／停留。
        currentTarget = null; // 這段移動已完成，清除當前目標。
        finalTarget = null; // 這趟旅程也結束了。

        switch (reachedPoint.waypointType) // 依據到達目標的用途滿足需求。
        {
            case AnimalWaypointType.Water: // 抵達水源。
                thirst = Mathf.Max(0f, thirst - thirstRelief); // 降低口渴值，最低不低於 0。
                currentBehavior = "喝水"; // Inspector 顯示正在喝水。
                break; // 結束此 case。

            case AnimalWaypointType.Food: // 通用食物點。
            case AnimalWaypointType.Grass: // 一般草地點。
            case AnimalWaypointType.ChickenFeed: // 雞的穀物點。
            case AnimalWaypointType.SheepGrass: // 舊版羊草地點。
                hunger = Mathf.Max(0f, hunger - hungerRelief); // 降低飢餓值，最低不低於 0。
                currentBehavior = animalType == FarmAnimalType.Chicken ? "啄食穀物" : "進食"; // 更新行為文字。
                break; // 結束食物處理。

            default: // 閒晃點或舊版馬／狗活動點。
                currentBehavior = "閒晃"; // 不改變需求值，只停留觀察。
                break; // 結束預設處理。
        }

        actionTimer = actionDuration; // 設定停留時間，模擬進食、喝水或短暫停留。
        nextThinkTime = Time.time + thinkInterval; // 避免停留結束後立刻頻繁重新評估。
    }

    /// <summary>確認指定目標有完整 NavMesh 路徑，並回傳路徑和長度。</summary>
    private bool TryGetPath(Vector3 destination, out NavMeshPath path, out float pathLength)
    {
        path = new NavMeshPath(); // 建立用來接收 Unity 計算結果的路徑物件。
        pathLength = 0f; // 先把路徑長度歸零。
        if (agent == null || !agent.isOnNavMesh) return false; // NPC 不在 NavMesh 上時無法計算有效路徑。

        bool pathFound = agent.CalculatePath(destination, path); // 計算從 NPC 目前位置到目的地的路徑。
        if (!pathFound || path.status != NavMeshPathStatus.PathComplete) return false; // 只有完整路徑才算可達。

        pathLength = GetPathLength(path); // 將路徑折線各段距離加總，取得實際路程估計值。
        return true; // 路徑完整，回傳成功。
    }

    /// <summary>把 NavMeshPath 的轉角點串起來，加總整條路徑長度。</summary>
    private float GetPathLength(NavMeshPath path)
    {
        if (path == null || path.corners == null || path.corners.Length < 2) return 0f; // 不足兩個轉角點就沒有可計算的路程。
        float length = 0f; // 路徑長度的累加器。
        for (int i = 1; i < path.corners.Length; i++) // 從第二個轉角開始，逐段計算。
            length += Vector3.Distance(path.corners[i - 1], path.corners[i]); // 加上前一點到目前點的直線距離。
        return length; // 回傳總路程。
    }

    /// <summary>把剛剛到訪過的 Waypoint 加入最近記憶佇列。</summary>
    private void RememberWaypoint(AnimalWaypoint point)
    {
        if (point == null || recentWaypointMemory <= 0) return; // 沒有目標，或使用者將記憶長度設成 0，就不記錄。
        int id = point.GetInstanceID(); // 取得這個場景物件在目前執行階段的識別值。
        if (!recentWaypointIds.Add(id)) return; // 如果已存在最近記憶中，不重複加入佇列。
        recentWaypointQueue.Enqueue(id); // 將這次造訪放在佇列尾端，表示最新造訪。

        while (recentWaypointQueue.Count > recentWaypointMemory) // 最近記憶數量超過 Inspector 指定上限時……
        {
            int oldId = recentWaypointQueue.Dequeue(); // 移除最早造訪的識別值。
            recentWaypointIds.Remove(oldId); // 從 HashSet 同時移除，讓這個舊點未來可以再次被選擇。
        }
    }

    /// <summary>試算並設定失敗或無目標時的狀態，稍後再重試。</summary>
    private void SetNoTarget(string message)
    {
        currentBehavior = message; // 讓 Inspector 顯示目前找不到目標的原因。
        agent.isStopped = true; // 先停在原地，不要朝錯誤方向亂走。
        retryAfter = Time.time + retryInterval; // 等待指定秒數後才再嘗試尋路。
    }

    /// <summary>清掉目前路徑和目標參考。</summary>
    private void ClearCurrentPlan()
    {
        currentTarget = null; // 清除目前這一段的目的地。
        finalTarget = null; // 清除真正終點。
        isTravellingViaRoute = false; // 清除中途路線狀態。
        if (agent != null && agent.isOnNavMesh) agent.ResetPath(); // 若 Agent 還在 NavMesh 上，清除它的舊路徑。
    }

    /// <summary>更新 waypoint 清單；第一次空白時會自動搜尋整個場景。</summary>
    private void RefreshWaypointsIfNeeded()
    {
        if (waypoints == null || waypoints.Length == 0) // 只有沒有手動指定清單時才自動搜尋。
            waypoints = FindObjectsByType<AnimalWaypoint>(FindObjectsSortMode.None); // 找出場景中的所有 AnimalWaypoint 元件。
    }

    /// <summary>將 Waypoint 類型轉成除錯用的行為文字。</summary>
    private string GetTravelBehavior(AnimalWaypoint goal)
    {
        if (goal == null) return "移動中"; // 空目標使用一般描述。
        if (goal.waypointType == AnimalWaypointType.Water) return "前往水源"; // 前往水源點。
        if (IsFoodTargetForThisAnimal(goal.waypointType)) return "尋找食物"; // 前往符合動物食性的食物點。
        return "閒晃"; // 其他目標都是一般閒晃。
    }

    /// <summary>確認目前這段路是否已抵達。</summary>
    private bool HasReachedCurrentTarget()
    {
        if (currentTarget == null || agent.pathPending) return false; // 沒有目標或仍在計算路徑時，不視為抵達。
        float remainingDistance = agent.remainingDistance; // 取得 Agent 到目標的剩餘路徑長度。
        float arrivalDistance = Mathf.Max(agent.stoppingDistance + 0.15f, 0.3f); // 加上容許誤差，避免浮點數使 NPC 永遠差一點點。

        if (agent.hasPath && !float.IsInfinity(remainingDistance) && remainingDistance <= arrivalDistance) // 有路徑且剩餘距離足夠小……
            return true; // ……視為抵達。

        if (!agent.hasPath && Vector3.Distance(transform.position, currentTarget.transform.position) <= arrivalDistance) // 沒有路徑時，只有真的貼近目標才算抵達。
            return true; // 視為抵達目標。

        return false; // 還沒到達。
    }

    private void OnDrawGizmosSelected()
    {
        if (currentTarget != null) // 選取 NPC 時，如果它有目前目標……
        {
            Gizmos.color = Color.magenta; // 中途點／目前目標用洋紅色表示。
            Gizmos.DrawLine(transform.position, currentTarget.transform.position); // 畫線表示 NPC 正在走向的當前目標。
            Gizmos.DrawWireSphere(currentTarget.transform.position, 0.35f); // 用線框球圈出當前目標。
        }

        if (isTravellingViaRoute && finalTarget != null) // 如果正在中途點和真正終點之間移動……
        {
            Gizmos.color = Color.cyan; // 真正終點用青色表示。
            Gizmos.DrawLine(currentTarget.transform.position, finalTarget.transform.position); // 畫出中途點到真正終點的意圖線，方便在 Scene 視窗除錯。
            Gizmos.DrawWireSphere(finalTarget.transform.position, 0.4f); // 圈出最後要抵達的資源點。
        }
    }
}
