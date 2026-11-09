# Fusion 同步與驗收說明

本版使用集中 Network Input 傳送玩家姿態，共用 NetworkTargetPose 同步 VR 羊與 NPC 的頭、左前肢、右前肢，root 由 NetworkTransform 同步。玩家 Client 決定移動，NPC 的 AI、導航、動作來源和生命狀態由 Server 控制。

本輪地圖接入與兩階段開始結果彙整於 [實作測試摘要](Fusion_Map_Implementation_Report.json)。先前同步與壓測結果保留於 Fusion_Sync_Implementation_Report.json。

## 先確認資產

| 位置 | 應有內容 |
| --- | --- |
| `Assets/_Project/Prefabs/Multiplayer/LobbySessionState.prefab` | LobbySessionState 與 NetworkWorld。NetworkWorld 保存企鵝與 VR 羊的 Player Prefabs，地圖物件清單由 MapNetworkSetup 保存 |
| `NetworkVRSheep.prefab` | NetworkObject、NetworkTransform、VRNetworkAvatar、NetworkTargetPose。Capture On Server 關閉，三 target 都有引用 |
| `NPC_sheep.prefab` | NetworkObject、NetworkTransform、NetworkNpcAuthorityGate、NetworkTargetPose。Capture On Server 開啟，三 target 都有引用 |
| `_flow test/MainGame.unity` | Navigation 已綁定烘焙資料，Network NPCs 的數量與上限皆為 60 |
| `Prefabs/Multiplayer/Examples/` | NetworkMovingBox 與 NetworkSwitch |
| `Assets/_Project/Recycle Bin/` | 舊連線資產。MainGame 的停用 Recycle Bin 收納兩個原本的本機 NPC Preview |

`NetworkTargetPose` 中的位置相對於同一個網路 root。Inspector 的三個引用指向 IK 的控制 target。骨架和伸縮由各端既有 IK 計算。

## 無頭盔的 Editor 驗收

1. 儲存場景並停止 Play Mode。
2. 選 `LiveStock > Connection Test > Open VR Client Menu (no headset)`。
3. 選 `Start Server`，等待 Lobby 開啟。
4. 在 VR 的 ConnectionManager 元件標題按右鍵，執行 `Connect to server`。
5. 選 `Open Desktop Client Menu`，在 PC 選單按 CONNECT。
6. 用 F2 切到 Server，應看到 Players 2、Loaded 2/2。Load MainGame 可按，Start Game 停用，此時網路 NPC 為 0。
7. 按 Load MainGame。三端進入 MainGame 並重新定位玩家，階段停在 AwaitingStart，NPC 與教學 Cube 都為 0。等待 Loaded 2/2，Start Game 才可按。此時玩家走動和姿態仍同步。按 Start Game 後階段為 Running，各端才收到 60 隻 NPC 及兩個教學 Cube。生成分批執行，等待數量穩定。
8. 在 Desktop 畫面使用 WASD、右鍵、Q／E、Shift，切到其他端確認企鵝位置與朝向跟隨。
9. 選 VR 的 ConnectionManager 上 `VRClientPoseSource`。透過 Head Anchor、Left Hand Anchor、Right Hand Anchor 引用選取對應物件，調整局部位置或旋轉。其他端的羊頭與前肢應跟隨。
10. 選 Rig Root 引用的本機物件，調整其世界位置與旋轉。其他端的整隻 VR 羊應移動，頭手仍保持正確的相對位置。
11. 暫停 `VRClientPoseSource` 元件。已建立的羊保持最後有效姿態，Server 的 TrackingValid 應轉為 false，PC 與 NPC 繼續同步。重新啟用後追蹤恢復。
12. 選 `Disconnect Selected Client` 返回該玩家的 Menu。剩餘端應持續運作，離開者的分身被移除。
13. 選 `Stop Server`，確認其他 Client 都顯示斷線。
14. 選 `End Test`，工具還原 Peer Mode、XR 與 Display 設定。

完成一輪後，改以 Desktop 先加入，再開 VR Menu，重做連線、姿態、MainGame 與斷線檢查。

### 檢查資料方向

```text
本機 DesktopTestPlayer / VRClientPoseSource
→ 該 Client Runner 上的 NetworkInputSource
→ Fusion OnInput / PlayerNetworkInput
→ Server NetworkWorld.FixedUpdateNetwork
→ 該玩家的分身 root 與三 target 狀態
→ NetworkTransform / NetworkTargetPose
→ 遠端 IK 與模型
```

每個 Client Runner 的 Provide Input 應啟用，Server 的 Provide Input 應關閉。Server NetworkWorld 的 AcceptedInputCount 應持續增加。

RPC 用於載入完成回報與離散互動。玩家連續姿態已改走 Network Input。

## NPC 驗收

在 Server 所屬 MainGame 中選 `Network NPCs`，確認生成數為 60。Client 的複本由 Fusion 接收，Client 的 AnimalNPC 與 NavMeshAgent 應停用，錄製播放器也應停用。Server 的這些來源保持運作，NetworkTargetPose 的 HasPose 為 true。

選一隻 Server NPC，觀察它移動與切換動作，切到其他 Client 檢查同一 NetworkId 的模型。頭與前肢應跟隨，身體、後腳、尾巴和伸縮也應合理。

在該 Server NPC 的 NetworkNpcAuthorityGate 元件執行 `Network/Test damage (Server)`。重複操作至 Health 為 0，所有端應一致進入死亡狀態，計時結束後一起恢復。Client 無權直接修改血量。

要做固定姿態對照，先暫停該 NPC 的 AnimalNPC、AnimalMotionController 與錄製播放器。只暫停播放器時，動作控制器仍可能開始另一段動作。等網路狀態收斂後，再比對三 target 與完整模型。完成後恢復三個來源。

對 Network NPCs 再執行生成請求，數量應維持 60。`NPC/Respawn Generated (Server)` 會先清掉舊的一批，再依目前 Groups 與上限分批重建。

100 隻壓測時，在 Play Mode 的 Server 生成器將 Groups 的 Min Count、Max Count 與 Max Networked NPCs 都改為 100，再執行 Respawn Generated。結束 Play Mode 後，場景資產維持原定的 60 隻設定。

## 互動開關與 root 範例

MainGame 載入完成並按下正式 Start Game 後，玩家出生點附近會出現兩個方塊。NetworkMovingBox 由 Server 上下移動，用於示範 root 同步。

NetworkSwitch 用於示範持續狀態。讓 Desktop 企鵝移到距離開關 3 公尺內，在該 Client 的開關物件上對 NetworkSwitch 元件執行 `Network/Request toggle`。所有端應看到同一顏色，IsOn 保存在 Networked 狀態中。超出距離或未完成 MainGame 載入的請求會被 Server 拒絕。

此範例的驗收入口是 Inspector Context Menu。之後的 VR 按鈕、射線或其他輸入可呼叫相同 RequestToggle，沿用集中入口與 Server 檢查。

## 新物件加入同步

### 只有位置與旋轉

1. 複製 NetworkMovingBox Prefab 作為範例。
2. 保留 NetworkObject 和 NetworkTransform，替換外觀與實際移動來源。
3. 在 Server 的 FixedUpdateNetwork 更新 root。
4. 將 Prefab 加入 Fusion Prefab 表，在目前地圖的 MapNetworkSetup > Scene Objects 加入該 Prefab 和出生偏移。
5. 啟動兩個 Client，驗證生成、移動、換場與清理。

### 有可保存的狀態

參考 NetworkSwitch。狀態以 Networked 屬性保存，Spawned 套用初始外觀，OnChangedRender 更新外觀。操作經由 NetworkWorld 的 RPC 入口識別來源，檢查條件後才修改目標狀態。

### 有三個控制 target

參考 NPC_sheep。保留 NetworkObject、NetworkTransform、NetworkTargetPose，拖入同一 root 下的三個 IK target。Server 生成 target 時啟用 Capture On Server，遠端由元件套用插值姿態後再解 IK。沿用一致的模型、校正與伸縮設定。

動態生成可在 Server 呼叫下列入口。傳入的 Prefab 須已登記於 Fusion。

```csharp
var world = NetworkWorld.For(runner);
world.SpawnObject(prefab, position, rotation);
```

場景切換時，NetworkWorld 清理它建立的場景物件。玩家分身則依目前跨場景策略保存和重新定位。

## 實體頭盔驗收

1. 先連接 Quest Link 或目前使用的頭盔，結束其他測試。
2. 第一個入口選 `Open VR Client Menu`，讓 XR 在建立 Runner 前初始化。
3. 戴上頭盔，檢查原本 3D Menu、VR 射線與 CONNECT 按鈕。
4. 啟動 Server、連接 VR，再加入 Desktop Client。
5. 由 Desktop 觀察 VR 玩家轉頭、抬手、蹲下與移動，檢查身高、左右前肢與模型伸縮。
6. 先 Load MainGame，確認等待狀態，再 Start Game，靠近 NPC，觀察姿態平順程度與實際 CPU／GPU frame time。
7. 測試追蹤暫失、恢復、返回選單與關服。

VR 預覽測試提供模擬追蹤資料。實體 HMD、控制器、XR UI 與實際刷新率需以這一輪裝置操作驗收。

## 如何閱讀結果

| 報告 | 驗證內容 |
| --- | --- |
| `Fusion_Sync_VRFirst_Report.json`、`Fusion_Sync_DesktopFirst_Report.json` | 兩種加入順序、姿態與換場 |
| `Fusion_Sync_World_Report.json` | 角色、無姿態生成、重連、重複關閉與清理 |
| `Fusion_Sync_LoadingEdge_Report.json` | Lobby 與 MainGame 載入期間離線，剩餘玩家仍能通過門檻 |
| `Fusion_Sync_Acceptance_Report.json` | 三 target、完整骨架、60 NPC、傷害重生、教學物件與晚加入 |
| `Fusion_Sync_Soak_60_Report.json` | 60 隻持續運作與量測 |
| `Fusion_Sync_Soak_100_Report.json` | 100 隻容量壓測 |
| `Fusion_Sync_Build_Report.json` | 同版本 Windows Development Player 的建置結果 |
| `Fusion_Sync_Build_Warnings.json` | 建置警告的去重內容 |
| `Fusion_Sync_Standalone_Report.json` | 三個獨立程序的姿態、60 NPC 與關服清理 |
| `Fusion_Sync_WeakNetwork_Report.json` | 三組 SDK 模擬網路條件下的收斂、NPC 與生命週期 |
| `Fusion_Sync_Asset_Report.json` | 16 項搬移 GUID、16 個 Prefab 與兩個場景的腳本引用 |

Editor 多開時，影格間隔和記憶體包含整個 Editor、所有 Runner 與場景複本。獨立程式的 Server、PC Client 和 VR Client 應分別量測。頭盔效能以實體設備的 CPU／GPU frame time 和刷新率判斷。

Server 與 Client 每次都使用同版本建置，本輪已變更輸入和 Prefab 的網路資料結構。

### 本輪量測範圍

60 隻在 Editor Multi-Peer 持續約 600.4 秒，平均影格間隔 79.5 ms，P95 101.4 ms。100 隻持續約 60.6 秒，平均 119.3 ms，P95 143.9 ms。兩輪 Server tick 約 64/s，NPC 數量穩定。詳細流量取樣、記憶體變化及條件保存在各自 JSON 中。

影格間隔包含所有本機 Runner 與 Editor 工作。tick 推進率、CPU tick 執行時間與 GPU frame time 分別量測。100 隻這輪為短壓測，完整弱網矩陣、IK 分項耗時與實體 HMD 刷新率仍待驗收。

獨立程序測試以同一台 Windows 上的 Server、Desktop 與 VR 預覽執行，60 隻採樣約 30 秒，Server tick 約 64/s。三端使用無顯示卡模式與 60 FPS 上限，平均影格間隔約 17 ms，未包含畫面繪製負載。

弱網測試涵蓋基準，以及每封包 50 ms 延遲／2% 丟包／10 ms jitter 和 100 ms 延遲／5% 丟包／20 ms jitter。模擬套用於三個 Runner，量得 Client 平均 RTT 約 77、229、230 ms。RTT 包含處理時間，並受路由與模擬實際作用方向影響，因此保留設定值和實測值。三組均通過靜止姿態收斂、各端 60 隻、持續輸入及關服重開。完整延遲／丟包矩陣與主觀平順度繼續列為待驗收。

Windows 驗收建置成功，0 errors、190 warnings。警告包括 OpenXR 遷移提示、Sentis Shader，以及 Meta 套件的 `FusionAvatarSdk28Plus.prefab` Missing Script。專案同步與收納 Prefab 的 16 個檢查結果皆為零 Missing Script，套件警告內容保存在 Build Warnings 報告。

### 重跑自動測試

`Tools/ConnectionFlow` 中的驗收腳本由 Unity Pipeline `run_script` 執行，每輪先 End Test，再開一個對應的未連線 Menu。

| 腳本與入口 | 起始狀態 |
| --- | --- |
| `FusionSyncMixedAcceptance.VRFirst` | 先開無頭盔 VR Menu |
| `FusionSyncMixedAcceptance.DesktopFirst` | 先開 Desktop Menu |
| `NetworkWorldAcceptance.Run` | 先開 Desktop Menu |
| `FusionSyncLoadingEdges.Run` | 先開 Desktop Menu |
| `FusionSynchronizationAcceptance.Run` | 先開 Desktop Menu |
| `FusionSynchronizationAcceptance.Soak(600, 60)` | 上一項成功後，保留 Server、Desktop 與 VR 在 MainGame |
| `FusionWeakNetworkAcceptance.Run` | 先開 Desktop Menu，三組結束後還原記憶體中的模擬設定 |
| `ValidateFusionAssets.Run` | Edit Mode |

以 DesktopFirst 為例，在專案根目錄執行以下指令。

```powershell
unity command run_script --caller plugin --skill unity-cli --timeout 240 --format json -- --file Tools/ConnectionFlow/FusionSyncMixedAcceptance.cs --entry FusionSyncMixedAcceptance.DesktopFirst --timeout_ms 220000
```

獨立執行檔可透過 `LiveStock > Connection Test > Build Fusion Validation Player` 重建。輸出位於 `Builds/FusionValidation/LiveStockFusion.exe`，包含明確啟用的 Development 驗收 probe。平常建置不會包含該 probe。Server 使用 `-flow-server`，PC 使用 `-flow-desktop`，無頭盔 VR 使用 `-flow-vr-preview`。實體 VR 啟動時使用一般入口。

重跑跨程序自動驗收時，在專案根目錄執行以下命令。腳本會啟動並結束它建立的三個隱藏程序，輸出 Standalone 報告。

```powershell
& ./Tools/ConnectionFlow/TestFusionStandalone.ps1 -SampleSeconds 30 -UseGraphics
```


## 新地圖接入流程

1. 將新場景加入 Build Scene List，Server 與 Client 使用同版本及順序。
2. 在場景拖入 `Assets/_Project/Prefabs/Multiplayer/MapNetworkSetup.prefab`，每張玩法地圖一份。這是一般場景 Prefab，Runner 與房間狀態由既有連線流程建立。
3. 調整子物件 VRSpawnPoint 的位置與朝向。根物件 FlowSceneMarker 保持 Stage 2，VR Spawn Point 指向該子物件。
4. 調整子物件 Network NPCs 的種類、數量、生成範圍與上限。新地圖建立 AnimalWaypoint 和符合 NPC Agent Type 的 NavMeshSurface，烘焙自己的導航資料。生成器會取得所屬 Runner 地圖內的 waypoint。
5. MapNetworkSetup 的 Scene Objects 設定正式開始時生成的物件。共用 Prefab 的預設清單為空，目前 MainGame 的兩個 Cube 是該場景的 Prefab override。可清空清單停用示範。
6. 在 `Assets/Resources/NetworkSceneSettings.asset` 修改 Main Game Scene 路徑。Lobby、Menu 與 Server 入口也集中在這份資產。保留單一 Resources 設定資產，名稱為 NetworkSceneSettings。
7. 重跑兩種加入順序、Load MainGame、等待零 NPC、Start Game、生成數量、姿態與離線清理驗收。

每個 Runner 的 FlowSceneMarker 和地圖設定依所屬場景查找。地形、美術、waypoint 與 NavMesh 資料留在各地圖，地圖接入 Prefab 保存出生點、生成器與開始時生成清單。Editor 的 Connection Test 選單仍指向專案內的測試入口場景。

房間階段依序為 Lobby、LoadingMainGame、AwaitingStart、Running。Server 的 CanLoadMap 只在 Lobby 且所有玩家載入完成時成立。CanStart 只在 AwaitingStart 且所有玩家載入完成時成立。NPC、教學物件與開關互動在 Running 執行。等待階段仍允許玩家移動與追蹤同步。

本輪新報告使用 `Fusion_Map_` 前綴。先前 `Fusion_Sync_` 報告保留為上一輪歷史結果。

本輪驗證包含替代地圖完整 28 項、生命週期 15 項、兩種加入順序 15／16 項、載入邊界 6 項與跨程序 11 項。新版 Windows 建置成功，0 errors、193 warnings，去重警告保存在 Fusion_Map_Build_Warnings.json。

跨程序預設無顯示卡模式曾在 Server 的 CCTV/WebRTC 收到影像串流 offer 時出現 Graphics device type Null not supported。該輪 Fusion 功能檢查已通過，日誌檢查判失敗，保存在 Fusion_Map_Headless_Report.json。以 -UseGraphics 使用 D3D11 圖形裝置重跑後 11 項全部通過，結果在 Fusion_Map_Standalone_Report.json。這輪測試沒有量測 GPU frame time 或實體頭盔。
