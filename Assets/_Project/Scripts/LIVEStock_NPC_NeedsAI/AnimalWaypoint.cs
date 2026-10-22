using UnityEngine; // 引入 Unity 基本型別、元件、Gizmos 與 Inspector 屬性。

/// <summary>
/// 定義地圖上每一個 Waypoint 的用途。
/// 注意：保留舊版 HorseRun / SheepGrass / DogPatrol 的位置，避免既有場景序列化的 enum 數值錯位。
/// 新版不再把它們當作「任務」，而是把它們當成對應動物的閒晃／食物點。
/// </summary>
public enum AnimalWaypointType
{
    Wander,       // 一般閒晃點：需求不急迫時，動物會從多個可達點中隨機選擇。
    Food,         // 通用食物點：目前主要供狗使用。
    Grass,        // 一般草地／吃草點：供馬、羊、牛覓食。
    Water,        // 飲水點：請放在河岸或池塘邊可站立的位置，不要放深水中央。
    ChickenFeed,  // 雞的穀物食物點。
    HorseRun,     // 舊版相容：新版會把它視為馬可用的閒晃點，不再累積跑訪任務。
    SheepGrass,   // 舊版相容：新版視為羊可吃的草地點，不再累積任務進度。
    DogPatrol,    // 舊版相容：新版會把它視為狗可用的閒晃點，不再執行巡邏任務。
    Route         // 隨機中途路線點：NPC 前往食物／水源／閒晃目標時，可選它作為中途站。
}

/// <summary>地圖區域分類，用來限制或偏好動物平常閒晃的地方。</summary>
public enum FarmZone
{
    Grassland, // 草原區：馬、羊、牛等主要活動區域。
    Forest,    // 森林區：樹木、灌木與遮蔽物較多的區域。
    Farm,      // 農場區：穀倉、作物、雞的覓食點等。
    Central,   // 中央中控台附近。
    Riverbank  // 河岸區：飲水點通常設在這裡。
}

/// <summary>
/// 加在場景中的空物件上，標示某個可前往的位置以及它的用途。
/// 多個 Water / Grass / Food 點代表同一類資源的不同接近位置。
/// </summary>
public class AnimalWaypoint : MonoBehaviour
{
    [Header("目標設定")]
    public AnimalWaypointType waypointType = AnimalWaypointType.Wander; // 在 Inspector 指定這個點是閒晃、食物、水源或路線點。
    public FarmZone zone = FarmZone.Grassland; // 指定這個點屬於哪個區域；閒晃時會優先選生活區內的點。
    public bool available = true; // 取消勾選後，NPC 會忽略這個點；可用來暫時停用某個資源點。

    [Min(0.1f)]
    [Tooltip("被選中的相對權重。數字越大，其他條件相近時越容易被選到；一般先用 1。")]
    public float selectionWeight = 1f; // 控制被選中的機率；也可用來偏好某個水源、草地或中途路線。

    private void OnDrawGizmos()
    {
        // 依類型設定 Scene 視窗中目標點的小球顏色，方便在地圖上辨識。
        Gizmos.color = waypointType switch
        {
            AnimalWaypointType.Water => new Color(0.1f, 0.55f, 1f),        // 水源：藍色。
            AnimalWaypointType.ChickenFeed => new Color(1f, 0.75f, 0.15f),  // 雞食：黃色。
            AnimalWaypointType.Grass => new Color(0.3f, 0.85f, 0.25f),      // 草地：綠色。
            AnimalWaypointType.SheepGrass => new Color(0.75f, 1f, 0.25f),   // 舊版羊草地：黃綠色。
            AnimalWaypointType.Food => new Color(1f, 0.4f, 0.65f),          // 通用食物：粉紅色。
            AnimalWaypointType.Route => new Color(0.7f, 0.4f, 1f),           // 中途路線：紫色。
            AnimalWaypointType.HorseRun => new Color(1f, 0.45f, 0.1f),      // 舊版馬閒晃點：橘色。
            AnimalWaypointType.DogPatrol => new Color(0.2f, 1f, 0.45f),     // 舊版狗閒晃點：亮綠色。
            _ => Color.white                                                // 一般閒晃點：白色。
        };

        Gizmos.DrawSphere(transform.position, 0.22f); // 在目標點位置畫球，方便在 Scene 視窗找到。

#if UNITY_EDITOR
        // 只在 Unity Editor 顯示標籤；正式 Build 不會編譯這段 Editor 專用程式。
        UnityEditor.Handles.Label(transform.position + Vector3.up * 0.3f, waypointType.ToString());
#endif
    }
}
