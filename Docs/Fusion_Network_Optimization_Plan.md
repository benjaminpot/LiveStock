# Fusion 網路同步優化計畫

更新日期為 2026-11-09。P0 至 P5 的主要功能已實作，P6 部分完成。自動驗收統一使用 [Test-Fusion.ps1](../Tools/ConnectionFlow/README.md)，手動及實體裝置操作見 [同步驗收指南](Fusion_Synchronization_Acceptance_Guide.md)。目前功能證據以 Fusion_Map_ 報告為準，Fusion_P1_ 與 Fusion_Sync_ 報告保留各自版本的歷史結果。

初次資產收納時已驗證 10 項資產 GUID、9 個 Prefab 與新版 Menu 的 Missing Script，並通過 VR 先加入 16 項、Desktop 先加入 15 項既有回歸。完整結果位於 [收納與回歸報告](Legacy_Connection_Archive_Report.json)。

## P1 已完成的實作

Client 和 Server 都透過 `NetworkSessionController.Create()` 建立各自的 Runner，再呼叫同一份 `StartAsync()`。每個 Runner 物件上可以直接查看 Controller、NetworkRunner、NetworkEvents 和 NetworkSceneManagerDefault。Controller 接收玩家離線與場景開始載入的回呼，交給所屬 NetworkWorld 處理。

`LobbySessionState.prefab` 掛載 `LobbySessionState` 和 `NetworkWorld` 兩個 NetworkBehaviour。Desktop 與 VR 的 Prefab 欄位位於 NetworkWorld 的 Inspector，沿用既有資產引用。LobbySessionState 保存房間階段、人數和載入門檻。NetworkWorld 集中讀取 Fusion 識別來源的 PlayerNetworkInput，角色登記與分身生成不依賴追蹤資料。

玩家完成載入回報後，NetworkWorld 登記該連線固定的 Desktop／VR 角色，建立分身。Desktop 和 VR 分身都可以在姿態來源停用時先建立。生成中的請求保存 Runner、場景代次及玩家登記引用，完成後再核對有效性。離線時集中移除登記與分身。

學習時可先沿這條路徑閱讀。

```text
ConnectionManager.ConnectAsync / ServerConnectionManager.StartServerAsync
→ NetworkSessionController.Create / StartAsync
→ Fusion 載入 Lobby
→ LobbySessionState.RPC_ReportLoaded
→ NetworkWorld.RegisterPlayer / SpawnAvatar
→ NetworkInputSource / NetworkSessionController.OnInput
→ NetworkWorld.FixedUpdateNetwork / TryGetInputForPlayer
→ VRNetworkAvatar.SetPose 或 Desktop 分身 root
→ 分身與 NetworkTransform 顯示結果
```

Server 程式可以呼叫 `NetworkWorld.For(runner).TryGetPlayer(player, out avatar)` 取得該房間玩家的分身。玩家登記表由 Server 持有，Client 端以 Fusion 複寫出的分身和 Networked 狀態呈現。

P1 的歷史結果位於 [P1 驗證摘要](Fusion_P1_Implementation_Report.json)。該輪後續報告使用 `Fusion_Sync_` 前綴，包含輸入、三 target、完整骨架、生命狀態、教學物件、晚加入與持續測試。

## P2 至 P6 的實作狀態

- 玩家採樣集中於每個 Client Runner 的 NetworkInputSource，連續姿態經 OnInput 送出。Server NetworkWorld 依 PlayerRef、固定角色、場景階段及數值有效性套用輸入。
- VR 羊與 NPC 共用 NetworkTargetPose，使用 root-relative 的頭、左前肢、右前肢位置與旋轉。遠端讀取 snapshot 插值，換場重設插值版本。下行 root 交給 NetworkTransform。
- NPC 的 AI、導航與錄製來源只在 Server 執行。遠端保留 IK、伸縮與蒙皮，以三 target 驅動。已比對固定姿態下的 target 與完整骨架。
- MainGame 的 Network NPCs 設為 60 隻，每幀分批生成，使用既有 NavMeshSurface 烘焙資料及各 Runner 場景內的 waypoint。重複生成請求保持數量。
- NetworkMovingBox 示範 root，NetworkSwitch 示範 Networked 持續狀態與集中 RequestToggle。Prefab 現在設定於 MainGame 的 MapNetworkSetup > Scene Objects，作為場景 override。
- 上一輪同步測試的 60 隻持續 600.4 秒，100 隻持續 60.6 秒，Server tick 均約 64/s。三端共處 Editor，平均影格間隔分別為 79.5 ms 與 119.3 ms。流量採 Fusion 統計取樣，完整數據與量測範圍見 soak 報告。

真實 HMD、CPU／GPU 與 IK 分項耗時、延遲／丟包完整矩陣仍待量測。現有 Editor 數據尚不足以判定 VR 刷新率或獨立 Server 的 tick 耗時預算。

上一輪同步測試包含三個獨立 Windows 程序的 10 項驗收，以及三組代表性 SDK 弱網測試。新版兩階段流程另有 Fusion_Map_Standalone_Report 的 11 項驗收。跨程序驗證 root、三 target、各端 60 隻與關服清理。弱網設定涵蓋每封包 50／100 ms 延遲、2%／5% 丟包與 jitter，保留實測 RTT。Windows Development 建置成功，套件及 Shader 警告仍列於驗收說明。

## 已確認的需求

| 項目 | 決定 |
| --- | --- |
| 現有基準 | Desktop 與 VR Client 已能互相看到，沿用目前新版連線流程 |
| 玩家移動 | Client 決定自己的 root 移動，Server 接收後同步給其他玩家 |
| NPC 控制 | Server 執行 AI、導航、錄製動作來源與遊戲狀態修改 |
| 羊 NPC 姿態 | 同步 root 位置與旋轉，加上頭、左前肢、右前肢三個 target 的位置與旋轉 |
| 規模 | 第一輪完整驗收以 60 隻羊為目標，100 隻作延伸壓測 |
| 中心化 | 集中連線生命週期、生成管理、操作請求與觀測入口，各網路物件保存自己的狀態 |
| 學習與擴充 | 用少量腳本、清楚的 Inspector 綁定與三種實例，說明新增物件的完整流程 |
| 舊資產 | 搬入 Assets 內的 Recycle Bin，保留 GUID 與依賴，再逐階段解除依賴 |
| 工作範圍 | 本輪聚焦 Fusion，CCTV 對接另案安排 |

60 與 100 是本計畫選定的測試數量。效能達標需在指定設備上實測，第一階段先記錄 Server 硬體、Client 平台與頭盔刷新率。

## P1 開始前的程式基準

| 腳本或資產 | 現況 | 後續處理 |
| --- | --- | --- |
| `ConnectionManager` | Client 連線、Rig 與 UI 保存、返回選單、視角切換 | 保留入口，將共用 Runner 操作抽到集中元件 |
| `ServerConnectionManager` | 專用 Server 開房、建立房間狀態、切換主場景 | 保留入口，與 Client 共用 Runner 生命週期 |
| `LobbySessionState` | 房間階段、人數、載入回報，也包含分身生成與姿態 RPC | 把生成與高頻姿態處理移出，留下房間狀態及載入協定 |
| `VRClientPoseSource` | 約 20 Hz 採集 root 與已校正的三 target，呼叫 `RPC_VRPose` | 保留採樣與校正，改由集中 Input 回呼送出 |
| `DesktopTestPlayer` | 本機操作與移動，約 20 Hz 呼叫 `RPC_DesktopPose` | 保留 Client 決定移動，將傳輸移到同一 Input 路徑 |
| `VRNetworkAvatar` | Server 寫 root 與 `[Networked] Pose`，遠端在 Render 套用 target | 使用共同 target 同步元件，補上 snapshot 插值 |
| `DesktopNetworkAvatar` | 隱藏擁有者自己的遠端分身，root 由 NetworkTransform 同步 | 保留相同顯示語意 |
| `NetworkVRSheep.prefab` | NetworkObject、NetworkTransform、VRNetworkAvatar、RemoteVRPoseTargets、既有 IK | 作為 VR 玩家遷移基準 |
| `NPC_sheep.prefab` | NetworkTransform、NetworkNpcAuthorityGate、錄製播放、Rigging 與 Final IK | 掛上共同 target 同步元件，切分 Server 與遠端的 target 寫入者 |
| `NetworkNpcAuthorityGate` | Server 跑 AI，動作編號與血量等狀態同步，目前 Client 也播放錄製動作 | 遷移後 Server 產生 target，遠端以網路 target 驅動 IK |
| `RandomNPCSpawner` | 現有數量上限預設 12，與舊 Launcher 的場景回呼連結 | 接入新集中生成入口，分批生成並驗收 60 隻 |
| `NetworkSheepRig` | 逐骨骼同步位置、旋轉與縮放，掛在舊網路羊 Prefab | 完成三 target 與外觀對照後收納 |

上表描述遷移前基準。高頻姿態的 Unreliable RPC 已由集中 Network Input 取代。

目前 `VRTrackedPose` 保留上行 root 資料，VRNetworkAvatar 的 Pose 是供既有呼叫者讀取的組合介面。下行 root 統一由 NetworkTransform 保存。

## 集中管理的物件與腳本

下列為已實作的元件配置。

```text
每個本機 Runner 一份
NetworkSessionController                 連線、停止、回呼、場景生命週期
├─ NetworkRunner
├─ NetworkSceneManagerDefault
└─ NetworkInputSource                    收集該 Client 的 PC 或 VR 輸入

每個房間一個網路物件
LobbySessionState.prefab                 跨 Lobby／MainGame 保存
├─ NetworkObject
├─ LobbySessionState                     Phase、人數、載入回報
└─ NetworkWorld                          玩家登記、生成／銷毀、離散操作請求

每張玩法地圖一份 MapNetworkSetup
├─ FlowSceneMarker 與玩家出生點
├─ Network NPCs 生成器
└─ Scene Objects 正式開始時生成清單

每名玩家一個網路分身
NetworkVRSheep / NetworkDesktopPenguin
├─ NetworkObject                        InputAuthority 指向該玩家
├─ NetworkTransform                     root 的下行同步
├─ VRNetworkAvatar / DesktopNetworkAvatar
└─ NetworkTargetPose                     VR 羊使用共同三 target 同步

每隻 NPC 一個網路物件
NPC_sheep
├─ NetworkObject                        StateAuthority 為 Server
├─ NetworkTransform
├─ NetworkNpcAuthorityGate               AI／血量／生死與來源啟停
├─ NetworkTargetPose                     同一套三 target 同步
└─ Visual                               Client 執行本機 IK、伸縮與蒙皮
```

### 腳本責任

| 元件 | 擁有的責任 | 外部常用操作 |
| --- | --- | --- |
| `NetworkSessionController` | 這個 Runner 的啟動、停止、場景回呼、其他元件引用 | Create、StartAsync、ShutdownAsync |
| `NetworkWorld` | Server 生成與移除玩家和動態物件、PlayerRef 對應、操作接收與分派 | SpawnObject、TryGetPlayer、RequestToggle |
| `LobbySessionState` | 已複寫的房間階段、人數、載入門檻 | ReportLoaded、讀取 Phase／CanStart |
| `NetworkInputSource` | 本機採樣快取，在 OnInput 提交一份 PlayerNetworkInput | 綁定目前 Desktop 或 VR 來源 |
| `NetworkTargetPose` | 三 target 狀態、有效性、snapshot 插值、套用時序 | Inspector 指定 head／left／right、來源模式 |
| `NetworkNpcAuthorityGate` | NPC 的權限啟停、AI 與生命狀態 | Server 修改血量、啟停行為 |

`ConnectionManager` 和 `ServerConnectionManager` 繼續對接目前 UI 與測試工具。共用邏輯進入 NetworkSessionController 後，兩個原入口會逐步縮小。

每個 Runner 使用自己的引用。Multi-Peer 的查找透過傳入 Runner 或既有 `For(runner)` 完成。Global singleton 僅用於明確的本機 Editor 視角工具。

## 三條資料路徑

### 玩家連續姿態

```text
Desktop 本機移動／VR 本機追蹤與移動
→ 快取 root 世界座標與三 target 的 root-relative 姿態
→ NetworkInputSource.OnInput
→ PlayerNetworkInput
→ NetworkWorld.FixedUpdateNetwork 以 TryGetInputForPlayer 讀取該玩家輸入
→ Server 接受該 Client 的移動結果，更新分身 root 與三 target
→ NetworkTransform + NetworkTargetPose
→ 其他端依同一 render 時間插值後顯示
```

Fusion Server Mode 下 Server 仍持有 StateAuthority。此處 Client 決定移動表示 Server 信任該玩家上報的 root，而玩家持有該分身的 InputAuthority。這個控制約定可以沿用現有 Server／Client 拓樸。

本機相機與頭盔直接讀取本機追蹤和移動。Owner 的外觀顯示延續現有本機模型設定。上行姿態加入擁有者、資料有效性和場景階段檢查，移動速度與路線由 Client 決定。

Network Input 的使用需成對實作 OnInput 與 GetInput，並確認 ProvideInput、Prefab 的 InputAuthority、回呼註冊與 Input 容量。輸入緩衝處理的是傳輸與 tick 對應，Client 移動結果採樣的方案仍須獨立驗證延遲及本機控制感。相關 API 依 Fusion 2 文件與已安裝 2.1.3 SDK 核對。[Photon Player Input](https://doc.photonengine.com/fusion/v2/manual/input/player-input)

### NPC 連續姿態

```text
Server 的 AnimalNPC／NavMeshAgent 決定 root
Server 的 AnimalMotionController／VRRecordingTargetPlayer 產生三 target
→ Server 採集映射與校正後的 target，快取完整一組
→ FixedUpdateNetwork 將快取寫入 Networked 三 target 狀態
→ root 透過 NetworkTransform 同步
→ 遠端插值 target，先套 target，再執行 IK／伸縮／蒙皮
```

Server 的錄製播放器目前在 Update 推進。第一版在播放與映射完成後的 LateUpdate 快取 target，下一個 network tick 提交快取，接受明確的一幀採樣延遲。Server 使用原始模擬／來源 target，遠端使用插值後的視覺 target。驗收時量測這段延遲。

Client 的錄製播放器和其他 target 來源按角色停用。RigBuilder、Final IK、StretchToTargetConstraint 依實際模型保留並核對順序。後腳、尾巴、身體和伸縮參數會影響完整外觀，三 target 方案須同時比對 SkinnedMesh，而非只比 target 座標。

### 離散操作與持續狀態

```text
本機 UI／互動程式
→ NetworkWorld.RequestToggle(target NetworkObject)
→ RPC 到 StateAuthority
→ 以 RpcInfo.Source 找到該玩家與該 Runner 的目標
→ 檢查操作權限、階段與目標有效性
→ 目標元件在 Server 更新自己的 Networked 狀態
→ 其他端讀取狀態並呈現
```

目前採具名 RequestToggle 與 RPC_RequestToggle，接收端從 PlayerRef 註冊表識別來源，檢查 MainGame 階段、同一 Runner 與 3 公尺距離。新增互動時沿用這個可追蹤的入口。

持續狀態使用 Networked 屬性，短暫回饋按需要使用 RPC。操作造成的持續結果存回物件，讓稍後加入或重新同步的端能取得現況。[Photon RPC](https://doc.photonengine.com/fusion/v2/manual/data-transfer/rpcs)

## 羊的資料規格

| 資料 | 空間／形式 | 產生者 | 傳輸與套用 |
| --- | --- | --- | --- |
| root position／rotation | 世界座標 | 玩家 Client 或 NPC Server | 上行玩家 Input，所有分身下行 NetworkTransform |
| Head position／rotation | 相對同一網路 root | 玩家追蹤映射後或 NPC 播放映射後 | 一份 Networked target 結構 |
| Left position／rotation | 相對同一網路 root | 同上 | 同上 |
| Right position／rotation | 相對同一網路 root | 同上 | 同上 |
| HasPose／TrackingValid | NetworkBool 等狀態 | 對應來源 | 首次姿態、追蹤中斷、恢復時使用 |
| Health／生死／計時器 | Networked 屬性 | Server | 維持目標物件內的狀態 |
| MotionIndex | 動作識別 | NPC Server | 保留診斷或玩法用途，Client 外觀由網路 target 驅動 |

初期 scale 使用 Prefab 設定與既有伸縮計算。若三 target 外觀對照顯示還缺少某個必要參數，再增加有證據需要的欄位。

共同資料結構先採固定三組 Position + Rotation，root 下行只保存在 NetworkTransform。HasPose 與追蹤狀態明確區分，零向量或 identity rotation 可是有效姿態。

遠端 Render 讀取 Fusion 的 from／to snapshot 和 alpha，位置用 Lerp，旋轉用 Slerp。target 的時間軸須與 root 對齊，並驗證 Render、RigBuilder 與 Final IK 的實際執行順序。Fusion 2 提供 TryGetSnapshotsBuffers 與 PropertyReader。[Photon Network Buffers](https://doc.photonengine.com/fusion/v2/manual/advanced/network-buffers)

換場、回到選單和瞬移需要重設視覺插值基準。root 跳轉採用已安裝版本支援的 NetworkTransform.Teleport，並在同一個跳轉版本／tick 重設 target 插值。移動要求會與本機 Rig 協調，避免下一份 Client root 又覆蓋跳轉。[Photon NetworkTransform](https://doc.photonengine.com/fusion/v2/manual/network-transform)

## 分階段實作與驗收

每階段完成後形成一個可回退的變更，保存測試報告與同版本 Server／Client。遇到回歸時先停在該階段修復。

| 階段 | 工作與交付 | 通過條件 |
| --- | --- | --- |
| P0 基準與收納 | 保存混合連線基準，收納舊入口，記錄 GUID 與引用 | 編譯通過、引用有效、兩種加入順序與 Lobby → MainGame 正常 |
| P1 集中生命週期，已完成 | 抽出 NetworkSessionController 和 NetworkWorld，搬出 LobbySessionState 的生成邏輯 | 已通過混合連線、角色登記、重連、換場和清理回歸 |
| P2 玩家傳輸統一，已實作 | PlayerNetworkInput 與 OnInput，NetworkWorld 集中讀取 | Client 決定 root，兩端 root／target 可比對，無追蹤先生成分身 |
| P3 三 target 共用與插值，已實作 | NetworkTargetPose 套用於 VR 和 NPC，移除重複下行 root | 基準 target 與骨架比對通過，網路抖動下的視覺觀察待驗收 |
| P4 NPC 生命週期與規模，已實作 | spawner 接入 MapNetworkSetup，由 NetworkWorld 在 Running 階段分批生成 60 隻 | 60 隻生成、死亡重生、權限與重複請求通過 |
| P5 新物件教學，已實作 | 同步 root、互動開關、三 target 三份實例與 Inspector 欄位說明 | 生成、持續狀態及晚加入通過，使用者依說明實作新物件待驗收 |
| P6 壓測與依賴收斂，部分完成 | 已完成 Editor 60／100 隻測試，舊骨骼同步收納 | 仍待 HMD、完整弱網矩陣及分項效能量測 |

### 狀態判讀

「已實作」表示程式與 Prefab 已接入。「自動測試通過」依具名 JSON 報告判定。「人工／實機待驗收」保留給裝置、外觀及使用者接入操作。舊版本的壓測資料單獨標示。

| 範圍 | 實作 | 自動測試證據 | 人工／實機待驗收 |
| --- | --- | --- | --- |
| P0 收納與 P1 生命週期 | 已完成主要功能 | Fusion_Map_Asset、World、LoadingEdge | 舊 CCTV／ServerOnly 依賴另列 |
| P2 玩家 Input | 已完成主要功能 | 兩種加入順序、World、Acceptance、Standalone | 實體頭盔追蹤與控制器 |
| P3 三 target | 已完成共享元件與 snapshot 插值 | 靜止 target、完整骨架與追蹤恢復通過 | 快速轉身、大幅移動、瞬移外觀及延遲 |
| P4 NPC | 已完成 Server 權限、生命狀態、分批生成 | 60 隻、死亡重生、晚加入及清理通過 | 新兩階段流程長測與裝置容量 |
| P5 教學與地圖接入 | 已完成範例、MapNetworkSetup 與兩階段開始 | 複製地圖切換及兩顆按鈕通過 | 空白地圖接入與使用者自行新增物件 |
| P6 效能與依賴 | 部分完成 | 舊流程 Editor 60 隻 10 分鐘、100 隻 1 分鐘；三組代表性弱網 | 新流程長測、完整弱網矩陣、CPU／GPU／IK 分項及頭盔 |
| 驗收入口整理 | 已移走舊工具，保留單一指令入口 | 本次入口驗證見 Fusion_Acceptance_Entry_Report | 裝置項目沿用下方待辦 |

### P0 和 P1 的生命週期細節

- [x] 每名玩家完成載入回報便登記角色，保留目前載入完成回報與 VR 無追蹤也生成的行為。Desktop 分身也改由明確的生成時點建立。
- [x] 生成中保存 pending 狀態。SpawnAsync 返回時確認原 Runner、玩家和場景代次仍有效，過期結果在原 Runner 清理。
- [x] 將角色在同次連線期間固定，重新加入時再設定。避免封包順序決定玩家分身種類。
- [x] 連線完成、場景完成與玩法可執行分別檢查。Load MainGame 與 Start Game 各有載入門檻，AwaitingStart 保持零 NPC。
- [x] RPC 與 Input 只作用於來源玩家的分身。Server 自己的管理操作使用 Server 入口。
- [x] 場景切換保留玩家分身的既有跨場景策略，清除前一場的 NPC 與場景物件，重新定位後清除舊插值。
- [x] callback 註冊和解除配對，Shutdown 後清掉本 Runner 的生成表與快取。

### P2 和 P3 的資料細節

- [x] PC／VR 來源只負責本機採樣。從 LateUpdate 的硬體 target 得到快取，OnInput 提交最近完整快取。
- [x] 每個 Runner 每次 OnInput 僅提交同一份 PlayerNetworkInput，Owner 與 Server 的角色可由 Inspector／診斷確認。
- [x] 檢查 NaN、Infinity、無效 quaternion 和來源身份。依已確認需求，玩家 root 移動結果由 Client 決定。
- [x] HasPose 輸入為 false 時保留有效姿態或中立姿態，依條件恢復。缺一個封包與實際失去追蹤分開處理。
- [x] 三 target 組成一份完整狀態，預先快取 Transform 和元件引用，查找放在初始化階段。
- [ ] 比對實際 Render tick，測試 root 快速轉向時 target 仍附著於正確的身體位置。
- [x] 記錄首次有效姿態、追蹤中斷與換場的插值規則。人工瞬移協調及外觀測試仍列為待驗收。

### P4 和 P6 的效能量測

原始 20 Hz RPC 路徑缺少同硬體效能基準。新版目前量得約 64/s simulation tick，後續比較 tick 與 send interval 時須明確記錄設定。量測記錄 simulation tick、上行 send、下行 snapshot send 和渲染 FPS 四項實際數字。

| 場景 | 執行方式 | 必留資料 |
| --- | --- | --- |
| 12／30／60 羊，全數可見 | 獨立 Windows Server + Desktop Client + VR Client | Server tick 平均／P95、Client CPU／GPU、IK 耗時、每端收送 bytes/s、GC、姿態更新間隔 |
| 60 羊，10 分鐘 | 所有 NPC 移動與切動作，穿插分身斷線、重連和場景測試 | NPC 數穩定、記憶體變化、例外、重複生成及殘留物件 |
| 100 羊 | 同一硬體與設定，作容量壓測 | 首個瓶頸、可見數、每端流量與穩定 tick 能力 |
| 延遲與丟包 | 基準、100 ms RTT、200 ms RTT，搭配 0%／2%／5% loss 與 jitter | 實際 RTT、target age、連線狀態、停頓與恢復時間 |
| 真實頭盔 | 標明 Quest standalone 或 Link 與刷新率 | 頭手追蹤、本機畫面、IK 外觀、CPU／GPU frame time |

在第一階段固定測試設備後設定 frame budget。Server P95 tick 耗時應小於選定 tick 間隔，VR CPU／GPU frame time 應符合選定頭盔刷新率。每個結果同時附模型可見數、解析度、畫質與網路條件，避免把 Editor 多開的 FPS 當作獨立程式容量。

三 target 若各使用 Vector3 和 Quaternion，另加一組 root，原始浮點資料為 112 bytes。60 隻、每秒 30 份約為 201,600 bytes/s，每份完整傳給 2 位 Client 的概念量級約為 403,200 bytes/s。這是未壓縮的資料量估算，實際 Fusion 封包還受 delta、欄位編碼、協定、冗餘、send interval 與 interest 設定影響，最後以 bytes/s 量測為準。

優化依量測結果進行。先處理每幀配置、全場搜尋和重複採樣，再調整頻率與精度。若骨架／蒙皮或 IK 成為瓶頸，再評估視覺 LOD 與遠端 IK 更新頻率。若流量成為瓶頸，再實測 AOI 或精度壓縮，保留「60 隻全數可見」的對照結果。Server 對不可見 NPC 仍須繼續執行必要的玩法模擬。

## 新物件加入網路同步的教學流程

教學以三個小實例逐步增加需求，每個實例附 Hierarchy、Inspector 欄位、資料方向、呼叫點與雙 Client 驗收方式。

### 實例 A，移動的箱子

1. 建立 Prefab，在 root 掛 NetworkObject 和 NetworkTransform。
2. 將 Prefab 加入已安裝 Fusion 的網路 Prefab 表，透過專案 Editor 工具驗證標記與 bake 結果。
3. 由 Server 的 NetworkWorld 生成，使用同一入口移除。
4. Server 更新 root，兩位 Client 檢查位置與旋轉。
5. 切場景後檢查箱子所屬生命週期與清理結果。

### 實例 B，可互動的開關

1. 延續實例 A，加入一支清楚的 NetworkBehaviour，保存 Networked IsOn。
2. 本機按鈕呼叫 NetworkWorld.RequestToggle，指定 NetworkId 與操作。
3. Server 驗證操作後更新目標 IsOn。
4. Spawned 套用初始狀態，狀態變更回呼更新外觀。
5. 先切開關，再加入第二位 Client，確認新加入者看到目前結果。

### 實例 C，三 target 的動物

1. 延續 NetworkObject 與 NetworkTransform，掛 NetworkTargetPose。
2. Inspector 拖入頭、左前肢、右前肢 target，三者共用同一 root 座標空間。
3. 指定 Server target 來源，Client 接收 target 後執行 IK。
4. 檢查 Prefab 校正、伸縮及後腳等其他外觀設定。
5. 檢查移動、轉向、動作切換、首次生成、換場與重新進入 interest 的結果。

未來物件優先依「root、持續狀態、三 target」選擇需要的元件，新增一個普通同步物件時，主要修改它的 Prefab、狀態腳本及 Server 生成清單。

## Recycle Bin 收納與後續依賴處理

此次放置位置為 `Assets/_Project/Recycle Bin/LegacyMultiplayer`。位於 Assets 內的 C# 繼續編譯，Editor 腳本留在 Editor 子目錄。搬移保留 GUID，舊場景與共用腳本依然能解析類別與資產引用。

| 收納項目 | 位置 |
| --- | --- |
| SessionLauncher、SessionPanel、NetworkPlayer、ObserverCamera | `Scripts/` |
| SessionLauncherEditor、MultiplayerSetup | `Scripts/Editor/` |
| NetworkSheepRig、NetworkSheepSelectable | `Scripts/` |
| NetworkSheepRigSetup、RandomNPCSpawnerSetup | `Scripts/Editor/` |
| NetworkPlayer、MultiplayerDemo、RemoteSheepVisual、RemoteSheepVisual_old | `Prefabs/` |
| NetworkSK_Sheep_white_final、RandomNPCSpawner | `Prefabs/` |
| 新版 Menu 中的 SessionLauncher、SessionPanel、MultiplayerDemo | 該場景停用的 `Recycle Bin` Hierarchy 群組 |
| ConnectionTestClient 中相同三個停用物件 | 該 Prefab 的停用 `Recycle Bin` 子群組 |
| MainGame 中原有的牛與羊 NPC Preview | 該場景停用的 `Recycle Bin` 群組 |

MultiplayerSetup 中指向搬移 Prefab 的字串路徑同步更新。收納清單與原 GUID 記錄於 `Legacy_Connection_Archive_Report.json`。

以下項目在遷移階段再收斂。

| 項目 | 目前依賴與處理門檻 |
| --- | --- |
| ServerOnly → SessionLauncher.RefreshRunnerVisibility | 仍有兩處編譯依賴。待集中 Runner 可見性責任接好後解除，另外驗證既有 Server 呈現 |
| NetworkSheepRig／NetworkSheepRigSetup／舊網路羊 Prefab | 三 target 外觀對照已通過，整組連同 NetworkSheepSelectable 搬入 Recycle Bin，保留 GUID |
| RandomNPCSpawnerSetup／舊 RandomNPCSpawner Prefab | 已搬入 Recycle Bin，字串路徑更新為收納後位置。新版 MainGame 使用場景內 Network NPCs |
| VRLiveTargets | RemoteVRPoseTargets 和 VRRecenterController 仍引用。保留現有校正呼叫鏈，再規劃整合 |
| RandomNPCSpawner | 保留現有生成邏輯，接到新生命週期並擴展數量 |
| GDC_METAVERSE | 保留工具與套件，實作時核對是否與 NetworkWorld 同時生成同一批物件 |
| 舊測試與 Alpha 場景 | 保留場景內容和 GUID。先標明入口，後續逐場移除對舊連線流程的依賴 |

收納完成後的檢查包含 GUID 一致、MonoScript 可載入、新舊 Prefab 引用、Missing Script、Editor 編譯、兩種加入順序與換場回歸。完整移除舊依賴須等 P1 至 P4 的對應責任完成。

## 驗收狀態與剩餘工作

### 已實作並有自動測試證據

- [x] Server 先開，VR／Desktop 任一順序加入，分身與角色正確。
- [x] VR 無追蹤先生成分身，追蹤恢復後更新三 target。
- [x] Client 決定 root，Server 與其他端收到姿態。
- [x] Load MainGame 完成定位後停在 AwaitingStart，Start Game 才進入 Running 並生成 NPC。
- [x] 載入期間有人離線仍能通過門檻，未完成載入時禁止正式開始。
- [x] 每端 60 隻，Server AI、三 target、生命狀態、晚加入與清理通過。
- [x] 同版本 Windows 建置及 D3D11 跨程序功能測試通過。
- [x] 教學物件生成、開關持續狀態、地圖複本路徑切換通過。

### 已有歷史量測

- [x] Fusion_Sync_Soak_60_Report 留有上一輪 Editor 60 隻約 10 分鐘數據。
- [x] Fusion_Sync_Soak_100_Report 留有上一輪 Editor 100 隻約 1 分鐘數據。
- [x] Fusion_Sync_WeakNetwork_Report 留有三組代表條件的收斂結果。

### 待自動量測或依賴整理

- [ ] 新兩階段流程的 60 隻 10 分鐘與更長 100 隻容量測試。
- [ ] 完整延遲／丟包矩陣及姿態更新間隔量測。
- [ ] 分離程序的 CPU／GPU、Server tick 執行時間、IK、GC 與頻寬分項量測。
- [ ] 依量測瓶頸再決定更新頻率、LOD、AOI 或資料精度調整。
- [ ] 解除 ServerOnly 到 SessionLauncher 的兩處依賴，另行驗證 CCTV 與 Server 呈現。
- [ ] 處理建置套件／Shader 警告與 CCTV/WebRTC 無顯示卡例外。

### 待人工／實體裝置驗收

- [ ] 真實頭盔、控制器、XR UI、追蹤中斷恢復與身高校正。
- [ ] 快速 root 旋轉、大幅移動、瞬移時的三 target 與完整模型外觀。
- [ ] 弱網下的主觀平順度與裝置 CPU／GPU frame time。
- [ ] 從空白建立地圖，接入 MapNetworkSetup、waypoint 與 NavMesh。
- [ ] 使用者依教學新增一個 Prefab，驗證生成、同步、互動與清理。

首次生成以 HasPose 控制有效資料，追蹤中斷保留最後有效姿態，換場使用 Teleport 並重設 target epoch。完整人工瞬移協調路徑及其裝置外觀仍列於待驗收，資料收斂測試與裝置觀察各自記錄。

## 舊驗收工具收納

舊版單階段開始、早期姿態、診斷建置、重現及 P1 遷移工具已搬至根目錄 Recycle Bin/ConnectionFlow。舊版 Editor 建置選單與 reload token 回呼已移除，原文保存在回收桶快照。保留 Build Fusion Validation Player 作為目前驗收建置入口。

搬移清單及 SHA256 見 Fusion_Acceptance_Tool_Archive_Report.json。操作索引以 Tools/ConnectionFlow/README.md 為準。歷史報告仍留在 Docs，報告本身代表當時版本的測試條件。
## 地圖接入與兩階段開始更新

已加入 MapNetworkSetup Prefab 和 Resources/NetworkSceneSettings.asset。正式 runtime 的 Lobby、MainGame、Menu 與 Server 場景引用集中於設定資產。Server 的 Load MainGame 負責換場與玩家定位，Start Game 在所有玩家載入完成後才啟動 Running 與 NPC 生成。詳細地圖接入和驗收步驟見 Fusion_Synchronization_Acceptance_Guide.md。本輪報告使用 Fusion_Map_ 前綴。
