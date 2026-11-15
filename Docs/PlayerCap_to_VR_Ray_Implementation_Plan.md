# Player_cap 功能移植至 VR：Locomotor 與蘋果 Ray 互動規劃

日期：2026-11-16。分支：`single-to-multi`。本文件只規劃，沒有實作遊戲功能或修改使用者的 Prefab／場景。

## 已確認需求

- VR 玩家已加上 Locomotor，本階段沿用它，檢查設定與網路接合，不重建移動系統。
- 將 Player_cap 的移動、觀看／轉向、跳躍、奔跑、蹲下與收集功能接到正式 VR 玩家。
- 手把以 Ray 指向 3D 蘋果，按下食指扳機，像按按鈕一樣發出收集請求。
- **Ray 命中與「玩家是否靠近」獨立。按下時仍檢查原本的接近條件，不能遠距離直接收集。**
- 接近沿用蘋果 Trigger 與玩家身體 Collider 的判斷，不另訂 3 公尺或以 Ray 長度取代。
- 所有羊共享 3 顆進度；收集完成提示廣播給羊與網頁獵人。本階段不做出口、逃脫或勝負結算。
- 桌面玩家維持測試用途。既有鍵盤入口與 VR 入口呼叫相同的遊戲請求，不建立第二份任務狀態。

## 目前專案狀態

1. `PlayerControl.cs` 仍包含原單機控制；MainGame 的 Player_cap 目前已停用。保留這項設定，避免與 VR 重複控制。
2. 使用者已啟用／加入 VR Locomotor；目前 Prefab 亦有使用者調整。本文件不覆寫這些修改。
3. `NetworkApple.prefab` 已存在於 `Assets/_Project/Prefabs/Game Objects/`，含 Rigidbody、NetworkObject、NetworkTransform 與仍為單機邏輯的 FruitPickup。
4. NetworkApple 的 apple 子物件已有兩個 SphereCollider：較大的 Trigger 用來判斷接近，較小的實體 Collider 貼近蘋果模型。保留接近 Trigger，Ray 瞄準使用較小 Collider。
5. MainGame 的 MapNetworkSetup 已安排生成三個 NetworkApple，另保留兩個示範物件。沿用目前生成入口，不再放第二組蘋果；確認 Offset 是相對出生點座標，不能當成世界座標直接使用。
6. Build Settings 已啟用新 MainGame，之前文件的建置清單阻擋已修正。
7. VRClientPoseSource 會擷取 rigRoot 與校正後的頭手 IK 目標；NetworkInputSource 在 LateUpdate 快取姿態。Locomotor 的實際移動根節點、完成時機與這些取樣是否一致，仍需執行驗收。

以上是檔案檢查與使用者確認；尚未證明實體頭盔中的操作已通過。

## Player_cap 功能對照

| 原功能 | VR 實作方式 | 應保留的行為與驗收 |
| --- | --- | --- |
| Move：WASD，通常速度 5 | 沿用 Locomotor 左搖桿 Slide | 可前後左右移動，受地形碰撞與重力影響；不再寫另一份 Rigidbody 位移 |
| Look：滑鼠旋轉玩家與 Camera | 頭盔自然觀看＋Locomotor 右搖桿轉向 | 不以程式額外旋轉追蹤中的相機俯仰；先沿用現有轉向模式 |
| Jump：空白鍵、落地才跳 | 沿用 Locomotor Jump，保留／核對原 A 鍵配置 | 空中不能連跳；不把原 Rigidbody 的 y 速度直接套到 VR rig |
| Sprint：按住 Shift 加速、鬆開恢復 | 沿用 FirstPersonLocomotor.Run(bool)，用現有 Run 按鍵接合 | 保留按住／鬆開語意；若現有設定是切換式，需改為按住式。原速度倍率約 2 倍作為調整參考 |
| Crouch：按住、降低高度與速度 | 沿用 FirstPersonLocomotor.Crouch(bool)，核對現有蹲下按鍵 | 保留按住／鬆開語意，利用膠囊與原 SDK 行為；不縮放整個 VR rig。實際蹲下的追蹤也需驗收 |
| Interact：範圍內按 E | Controller Ray 指定蘋果＋按食指扳機＋檢查該顆接近狀態 | 沒瞄準不收集、沒靠近不收集、只收射線指向的那顆 |
| AppleUI | 綁定本機 VR 可見的進度顯示 | 所有羊看到同一份共享進度；沿用目前已放的 UI，不重複生成 |

原程式的通常／奔跑／蹲下速度為 5／10／2；Locomotor 的 SpeedFactor 與輸入速度、阻尼共同作用，不能把這三個數字直接當成其等值參數。保留快慢關係，再用頭盔調整。原程式同時按奔跑與蹲下時會互相覆寫速度；建議 VR 採「蹲下優先，放開後依奔跑鍵是否仍按住恢復」並測試組合。

官方提供 FirstPersonLocomotor 的跳躍、奔跑、蹲下入口，現有安裝版本也已確認有 Jump()、Run(bool)、Crouch(bool)。[Meta API](https://developers.meta.com/vr/reference/interaction/v207/class_oculus_interaction_locomotion_first_person_locomotor/)

## 蘋果互動行為

| 條件 | 畫面／處理 |
| --- | --- |
| Ray 未命中蘋果 | 不發送收集請求 |
| 命中但玩家尚未靠近 | 顯示「請靠近蘋果」；按下不收集 |
| 命中且已進入該顆 Trigger 範圍 | 顯示可收集的瞄準／高亮回饋 |
| 按下扳機且以上條件成立 | 發出一次請求，等待伺服器結果 |
| 玩家走出範圍／射線改指另一顆 | 以當下目標及當下接近條件重判，不沿用舊 nearbyFruit |
| 伺服器接受 | 同步已收集狀態、隱藏外觀與碰撞／互動、更新共同進度 |
| 伺服器拒絕 | 保留蘋果，解除本機等待狀態；依目前條件繼續顯示 |

建議以「按下」的 Select 事件觸發一次，不同時把 Release／Unselect 也接到收集。持續按住不連續送請求；按住後掃過多顆也不得自動連吃，實作時需驗收 Selector 的重新選取行為，必要時加上必須鬆開才能再觸發的本機限制。

## Ray 與接近判斷的元件接法

### 本機玩家

沿用 rig 裡的 ControllerRayInteractor 與 ControllerSelector，核對食指扳機選取、指向來源、射線視覺與互動群組。不要另加 OVRInput／InputAction 再讀一次相同扳機。左右手都可走相同的收集入口；驗收先完成單手，再檢查另一手與雙手競爭。

只有本機 rig 可讀手把／發請求，遠端羊模型不啟動 Locomotor 或本機輸入。選單、UI 與遊戲蘋果共用同一套射線選擇機制，選到 UI 時不額外觸發背後蘋果。

### NetworkApple

```text
NetworkApple（沿用現有 NetworkObject 與 Rigidbody）
├─ FruitPickup：改成權威收集入口與同步狀態
├─ RayInteractable：代表整顆蘋果的互動目標
├─ ColliderSurface：指定較小的實體 SphereCollider
├─ PointableUnityEventWrapper：Hover／Unhover／Select 回饋與來源事件
└─ apple
   ├─ Renderer
   ├─ 較大 SphereCollider，Is Trigger = true：接近判斷
   └─ 較小 SphereCollider：實體／射線命中表面
```

這是待接合的邏輯配置，不表示這些元件已加入。RayInteractable 的 Surface 指向對應 ColliderSurface；蘋果是普通 3D 互動物件，不必包一個透明 Canvas Button，也不需加入新的 XR Interaction Toolkit。

Meta 的射線由 RayInteractor 與 Selector 配合，再由 RayInteractable 接收。ColliderSurface 可作為目標表面。[Ray 原理](https://developers.meta.com/vr/documentation/unity/unity-isdk-ray-interaction/)、[官方元件設定教學](https://developers.meta.com/vr/documentation/unity/unity-isdk-create-ray-interactions/)

PointableUnityEventWrapper 的 Select 帶有 PointerEvent，可保留來源識別；正式邏輯需將該來源綁回所屬本機玩家／Runner，不能在場景中 FindFirstObject 或依賴全域 FruitManager.Instance。Hover／Unhover 僅更新本機回饋，不改共同任務狀態。[Event Wrappers](https://developers.meta.com/vr/documentation/unity/unity-isdk-event-wrappers/)

### 保留原本的「靠近」規則

- 接近來源是 VR 玩家身體，不能用射線碰到 Trigger、或手伸到蘋果附近就代替玩家靠近。
- 沿用蘋果現有較大 Trigger，改掉 FruitPickup 對 PlayerControl 的硬綁定，改成識別屬於哪個本機玩家的身體 Collider。
- 先使用 Locomotor 已有的 CharacterController 膠囊；確認碰撞 Layer 與 Trigger 回呼可用。蘋果本身已有 kinematic Rigidbody，不需再把 Player_cap 的動態 Rigidbody 複製到 VR rig。
- 對每顆蘋果保存各玩家的接近關係；多個身體 Collider 時需正確處理一個離開但另一個仍重疊的狀況，不能任一 Exit 就清空所有接近狀態。
- 按下時再確認目前仍重疊；物件停用、場景離開或玩家斷線時清理接近關係，避免殘留的 Enter 記錄造成誤收集。
- 先把角色膠囊對齊 VR 身體並驗收站立／蹲下範圍，保留原本「身體進入收集區」的語意；不強迫不同身高的 VR 玩家使用原 Player_cap 固定 2 公尺膠囊。
- 檢查持續存在的本機 rig 與該 Runner 的蘋果是否處於同一 PhysicsScene。若 Multi-Peer 將兩者分開，不能期待跨物理場景的 OnTriggerEnter 自動發生；改以該 Runner 中的蘋果 Trigger 幾何與本機身體體積做查詢，保留相同重疊規則，不改成任意距離門檻。

目前 SDK RayInteractor 的候選選取會遍歷可互動目標，不能假設所有普通牆壁都會自動擋射線。需檢查／補上本機遮擋過濾，並由伺服器在自己的物理場景確認目標可見；一般牆壁、UI 與蘋果的 Layer／Collider 都要納入驗收。大接近 Trigger 不應變成很容易點到的瞄準表面。

## 多人接合

1. Ray Select 找到所屬本機玩家、所選 NetworkApple 與按下時的指向資料。
2. 本機檢查該顆蘋果的接近條件；不符合只顯示「請靠近」。
3. 透過目前房間的網路物件送出目標 NetworkId 與必要的射線來源資料；Fusion 識別真實發送者，不讓客戶端自報任意 PlayerRef。
4. 伺服器檢查 Running、玩家身分／追蹤、物件仍存在且屬於本輪任務，並確認伺服器端玩家身體與原蘋果 Trigger 的接近條件。可用伺服器端重疊檢查，避免依賴遠端羊目前沒有的 Collider；站立／蹲下的幾何資料須一致。
5. 指向與遮擋用同一個 Runner 的 PhysicsScene 檢查。若需接收客戶端射線，檢查起點合理性與有限值；目前網路手部欄位是校正後 IK target，不等於 ControllerPointerPose，不能直接當成精確瞄準方向。
6. 驗證與設定已收集狀態由伺服器一次完成，同顆雙手／雙玩家競爭只成功一次。
7. 建議 FruitPickup 利用現有 NetworkObject 改為 NetworkBehaviour，以 Networked IsCollected 保存每顆狀態；共享任務進度由伺服器從本輪三顆狀態推導，再同步給 UI，不另外保留一份收集遮罩。
8. 收集後停用模型與互動／碰撞元件，保留必要的網路根物件。新增 NetworkBehaviour 後重新檢查 NetworkObject 的 bake／prefab table。
9. 羊端依網路狀態更新；完成達 3/3 時向羊與網頁獵人提示。斷線／重連快照與離開地圖清理沿用多人化主計畫。

目前玩家位置仍由客戶端送結果，這次不重寫完整伺服器移動預測。Locomotor 與 VRClientPoseSource 的根節點要對齊，並檢查 end-of-frame 移動與 LateUpdate 取樣的時序；不能宣稱更換按鍵後已自動完成網路同步。

奔跑與跳躍可先由位移結果呈現；若伺服器端收集體積／碰撞與遠端身高需要蹲下資料，再在既有玩家輸入及狀態中同步實際需要的 Crouching／高度。避免另同步重複的原始按鍵或整套骨架。

## 實作順序

| 階段 | 待做 | 完成標準 |
| --- | --- | --- |
| 1. Locomotor 接合 | 保留使用者已加的元件；核對移動根、頭部、膠囊、地面 Layer、Move／Turn／Jump／Run／Crouch 配置 | 真頭盔能走、轉、跳、按住奔跑／蹲下與恢復；遠端看得到位移 |
| 2. 單顆 Ray 回饋 | 一顆 NetworkApple 接好 Surface、Selector、Hover／Select 來源；只記錄事件 | 準確瞄準小蘋果、UI／牆遮擋正常，按一次只觸發一次 |
| 3. 接近限制 | 解開 PlayerControl 綁定，將身體接近關係與射線選取目標分開 | 遠看可提示但不能收；近 A 指 B 不能收 B；走出範圍不能沿用舊狀態 |
| 4. 權威收集與進度 | 沿用三顆 NetworkApple 生成配置，加入同步收集狀態、來源驗證與共享任務 UI | 同顆競爭只算一次；所有羊與 CCTV 看到相同結果 |
| 5. 廣播與回歸 | 接上網頁完成提示、清理與新一輪重設；執行原連線測試 | 羊與獵人收到完成提示；返回選單再進入恢復 0/3 |

## 驗收清單

- [ ] Player_cap 保持停用；沒有第二份 Rigidbody 移動、重複相機或重複扳機讀取。
- [ ] Move／Turn／Jump／Sprint／Crouch 全部在頭盔可操作，奔跑／蹲下鬆開能恢復；兩鍵同按的優先順序一致。
- [ ] Locomotor 不因錯誤地面 Layer 卡住，不穿牆，不用縮放整個 rig 模擬蹲下。
- [ ] 只 Hover 不收集；按下 Select 才請求；長按、掃過多顆、雙手同按不連吃。
- [ ] Ray 指到較小模型表面才算命中，不因碰到大 Trigger 就選中。
- [ ] Ray 命中但身體未進 Trigger 時拒絕；只伸手靠近也不能繞過玩家接近條件。
- [ ] 身體同時靠近 A、B 時只收射線選中的一顆；只靠近 A 卻指 B 時不收 B。
- [ ] 身體已靠近但射線沒命中、或牆／UI 擋住時不收集。
- [ ] 剛進入／離開 Trigger、轉身、跳躍、蹲下、傳送與多 Collider 邊界不造成殘留。
- [ ] AwaitingStart、載圖、離線／追蹤無效時不能收集；遠端羊不讀本機手把。
- [ ] 兩位羊各收一顆都看到 2/3；同時搶第三顆只到 3/3，完成提示只出現一次。
- [ ] 使用相同新建置執行獨立程序測試；網頁獵人重連能取得目前進度。
- [ ] Multi-Peer 不混用另一個 Runner 的玩家、Ray、物理場景或任務 UI。
- [ ] 離開 MainGame 解除事件與 proximity 記錄，新一輪回到 0/3。

這輪只產出規劃；以上勾選項均尚待實作／驗收。既有主計畫的任務規則仍適用，接近與 Ray 接法以本文件及使用者最新決定為準。
