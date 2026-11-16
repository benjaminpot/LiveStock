# VR Player_UI 與蘋果 Ray 互動排查、修正規劃

日期：2026-11-16。工作區分支：single-to-multi。使用方式：Unity Editor＋頭盔 Link。

本輪為唯讀排查與規劃，僅新增本文件，未修改遊戲程式、Prefab 或場景。排查時編輯器未處於 Play Mode，未取得這次失敗的即時 VR 連線資料；因此以下區分「已確認的設定／程式行為」與「尚待重現的故障原因」。排查期間使用者的工作區與開啟場景仍有變動，本文件不是一個固定 commit 的完整驗收。

## 保留的需求

- VR 羊使用既有 Locomotor、模型與姿態同步；網頁獵人與羊共享任務結果，桌面玩家只供測試。
- Ray 負責選目標，食指扳機確認；按下當下仍使用原蘋果大 Trigger 與身體膠囊的接近判斷。
- 全部羊合計收集 3 顆；本階段只廣播完成提示，不製作出口。
- 狀態文字使用既有 Player_UI/state，不重建一套 UI。

## 目前確認的事實

### 1. Editor 的視角切換會停用 VR UI 與眼睛相機

ConnectionTestViews.Apply 對未被選中的 Client 呼叫 SetViewSelected(false)。ConnectionManager.SetViewSelected 會關閉該 Client 下所有 Canvas、Camera、EventSystem，並隱藏 Renderer。LocalConnectionTestTools.Perform("Server") 啟動 Server 後會自動選到 Server。

因此，同一個 Editor 同時測試 Server＋VR Client，若留在 Server 視角，Player_UI 和眼睛相機被關閉是目前程式的確定行為。尚未確認使用者遇到問題當下的 Selected 與 ViewSelected，不能直接認定這就是本次根因。此時姿態採樣仍可能持續，別人看得到模型不能證明本機 UI 正在渲染。

來源：Assets/_Project/Scripts/Multiplayer/ConnectionTestViews.cs；ConnectionManager.cs；Editor/LocalConnectionTestTools.cs。

### 2. Editor 啟動順序會影響 XR 是否初始化

LocalConnectionTestTools.Execute 只在動作為 "VR" 時設定 InitManagerOnStart=true；由 Server、Desktop 或 VR Preview 進入 Play 時不啟動 XR。已在 Play 中再開 VR Client 時，Perform 只檢查 activeLoader 並警告，沒有補啟 XR。FlowLaunchOptions 的手動補啟 Loader 僅編譯進非 Editor 的 Player。

下一次應先用 LiveStock > Connection Test > Open VR Client Menu（不要選 no headset），待 Link 與手把可用後再啟動 Server。這是現有測試工具的限制，不代表必須更換 XR 套件。排查時 DesktopClient=false、VRPreview=false、InitManagerOnStart=true，但未在 Play，無法據此確認 Loader 或實際追蹤。

### 3. FruitRayInteractor 有跨幀漏掉按壓的時序風險

ControllerSelector 在 Update 發出 WhenSelected；SDK Interactor 將選取事件加入佇列，在驅動流程中處理。FruitRayInteractor 在 LateUpdate 每幀清除 pressed，且只有 wasPressed=true 與 RayInteractor.State=Select 同時成立才送請求。

實際引用中的 ControllerSelector、BestSelectInteractorGroup 與 FruitRayInteractor 執行順序均為 0。若 SDK 驅動先於 Selector 更新，Select 可能到下一幀才成立，此時橋接器已丟掉按下紀錄。程式存在此風險已確認，但尚未以本次頭盔操作紀錄證明它發生。

來源：Assets/_Project/Scripts/VR/FruitRayInteractor.cs:39；Library/PackageCache/com.meta.xr.sdk.interaction@0eb6eb677898/Runtime/Scripts/Selection/Controllers/ControllerSelector.cs；Interaction/Core/Interactor.cs 與 Base/InteractorGroup.cs。

### 4. Player_UI 的靜態引用、字型、圖層正常，但目前是世界空間面板

在 Menu_VRClient 預覽場景讀到：

- PlayerGameplay.stateText 指向 [BuildingBlock] Camera Rig Variant/Player_UI/state。
- Canvas 與文字的 activeInHierarchy、enabled 均為 true；文字為「等待遊戲開始」，字型 GameplayStatus，白色且 alpha=1。
- Canvas 為 WorldSpace，直屬 Rig，沒有跟隨頭部的元件。它會隨 Rig 移動／轉向，但不會隨實際頭部轉向。
- Canvas 為 UI layer 5；CenterEyeAnchor 的 cullingMask=55 包含該圖層，Near Clip=0.1。靜態狀態沒有被相機圖層排除。
- Canvas 尺寸約 136.4×80.13、世界縮放約 0.03，整個面板約 4.09×2.40 公尺；在未套入實際追蹤的編輯姿態中，相對 HeadAnchor 為 (0,1.4,2.73)。此座標不是佩戴頭盔後的位置驗收。
- Canvas.worldCamera 為空不能單獨當成 WorldSpace UI 不顯示的原因。

因此現在沒有依據改字型或重建 UI。應先分清楚是整個面板不可見、離開視野、被場景遮住，還是只有 state 文字異常，再調整其顯示位置。MainGame 內另有停用的 Rig/Player_UI；正式本機顯示應追查從 Menu 持續存在、且綁到 ConnectionManager 的那一份。

### 5. 射線與身體引用有效；模型同步成功不等於收集條件成立

- 左右 FruitRayInteractor 都有效引用同物件的 RayInteractor、TriggerSelector；SDK 與橋接器使用同一 Selector，按鍵為 TriggerButton；兩支 Ray 的 Filter 列表都包含橋接器。
- PlayerGameplay.body 綁到既有 Locomotor/PlayerController 的 CapsuleCollider，靜態 radius=0.25、height=1.4、縮放=1，轉成 PlayerBodyPose.IsValid=true。
- Locomotor._playerOrigin 與 VRClientPoseSource.RigRoot 是同一個 Rig，沒有靜態根節點綁錯的證據。
- 收集仍需要 Running、正確 Runner、有效網路蘋果、本機身體接近與射線通暢。
- 伺服器另外需要有效追蹤、尚未過期的輸入與有效 Body。VRClientPoseSource.TrackingAvailable 要求 HMD 與左右手把的位置／旋轉都在追蹤；NetworkWorld 的輸入有效期限為 0.5 秒。Transform 姿態可同步時，TrackingValid 仍可能為 false。
- 目前 MainGame 的靜態場景沒有 FruitPickup，三顆蘋果由 MapNetworkSetup 在 Running 後生成。工作區已把生成改為世界座標；這是使用者現況，沒有將其改回出生點偏移。測試應確認看到的蘋果 Object.IsValid、Runner 正確且數量為 3。

來源：PlayerGameplay.cs；VRClientPoseSource.cs；NetworkInputSource.cs；NetworkWorld.cs:227；MapNetworkSetup.cs。

## 建議執行順序與驗收

| 順序 | 工作 | 驗收方式 |
| --- | --- | --- |
| 1 | 用正確入口重現：先開 VR Client Menu，再啟動 Server；進入遊戲後切回 VR Client 視角 | activeLoader 非空、角色為 VR、IsConnected=true、Phase=Running、ViewSelected=true；HMD 與兩手 TrackingValid=true |
| 2 | 讀取實際存活 UI，而非 MainGame 中停用的副本 | stateText 的物件路徑與 Canvas、父物件、眼睛相機均有效；文字持續更新；在頭盔觀察其位置、遮擋與轉頭後表現 |
| 3 | 針對互動鏈加入可關閉的診斷紀錄，或在執行時讀取現有欄位 | 同一次操作能串起 Selector 按下幀、Ray 的 Hover/Select 與目標、CanInteract、Body、IsNearby、HasClearRay、RPC 送出及伺服器回覆；只記錄狀態變化／按壓，避免逐幀洗版 |
| 4 | 修正 FruitRayInteractor 的選取觸發方式 | 以 SDK 完成選取的事件（例如 Interactor 的選取事件／WhenSelect）作觸發，保留「一次按下最多一次請求、鬆開才重新允許」；不要依賴 pressed 與 Select 必須同幀，也不要直接呼叫 SDK Drive 繞過管線 |
| 5 | 根據第 2 步結果修正現有 Player_UI 顯示 | 若需求是隨視角可讀的狀態 HUD，建議把現有 Player_UI 掛到 CenterEyeAnchor 下的專用 UI 掛點並調整位置／大小；若它是刻意保留的世界面板，則只修其位置與朝向。不因尚未確認的症狀先改成桌面 Overlay |
| 6 | 處理診斷中實際失敗的接近、遮擋或伺服器條件 | 遠處瞄準可得到靠近提示；身體靠近且射線通暢才收集；被牆擋住不能收集；不要用放寬距離或略過追蹤檢查掩蓋問題 |
| 7 | 實機與多人回歸 | 左右手各測按下／長按／鬆開再按、站立／蹲下／移動；多人同搶一顆僅計一次；所有羊及網頁獵人看到共同 3/3 與完成提示 |

第 3 步的重點是先定位中斷位置：沒有 Hover 查 Ray 啟用、Filter 和目標；有 Hover 無 Select 查按鍵與 SDK；有 Select 無請求查橋接器；已送請求但被拒絕查伺服器回覆。UI 無法閱讀時也應在 Console 保留收集結果，不靠頭盔文字作唯一診斷來源。

Editor 視角切換如需長期支援「操作 Server 同時維持頭盔畫面」，應另行調整測試工具對 VR 相機／Canvas 的控制範圍；先用切回 VR 視角確認症狀，不直接改動多人姿態同步或 Runner 可見性，以免影響現有多視角與 CCTV 行為。

## 驗證邊界與歷史紀錄

本輪確認 Unity 6000.3.25f1、CLI 1.0.0，並讀取正式 Menu/MainGame 預覽、元件引用、SDK 原始碼與歷史 Console。沒有啟動 Play、重建 Player 或執行新的頭盔測試。既有多人／瀏覽器報告不包含本次真實手把選取與頭盔 UI 驗收。

歷史 Console 含 2026-11-16 14:10 UTC 的 [MetaXRFeature] ErrorFormFactorUnavailable，以及 14:08 UTC 的 Fusion bake hash != 0。它們是過去事件，當前沒有編譯錯誤；未把它們認定為這次故障原因。下一輪應按新測試時間篩選紀錄，若再次出現才追查。不要清除既有紀錄後聲稱問題已解決。

## 官方依據

- [Unity 6.3：Event function execution order](https://docs.unity3d.com/6000.3/Documentation/Manual/execution-order.html)：不同物件間事件順序不能依未指定的更新順序假設。
- [Meta：Interaction SDK Selector Interface](https://developers.meta.com/vr/documentation/unity/unity-isdk-selector/)：Selector 與 Interactor 分工，Selector 提供按下／放開事件，由 Interactor 處理選取。
- [Meta：InteractorUnityEventWrapper](https://developers.meta.com/vr/reference/interaction/v203/class_oculus_interaction_interactor_unity_event_wrapper/)：提供 WhenSelect 等互動事件。專案實際可用 API 仍以已安裝 SDK 原始碼為準，本輪已比對本機實作。
