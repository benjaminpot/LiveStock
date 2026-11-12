# VR Lobby 第一階段測試結果（歷史）

目前驗收入口見 [驗收索引](../Tools/ConnectionFlow/README.md)。以下內容為當時版本的結果。

本次依使用者回報回退 VR 桌面介面替換，改為直接載入原本 Menu_VRClient。最新原場景混合驗收共 40 項通過，包含完整本機 VR Rig 啟用後加入兩位已連線 Desktop 的測試。結果與裝置驗收限制見 [最新 VR 操作說明](VR_Mixed_Connection_Guide.md)。下列前次報告保留為歷史記錄。

VR 混合同步改版已完成Desktop 先加入 14 項、VR 先加入 15 項驗收，以及純 Desktop 14 項回歸。頭手 Anchor 經真實 Fusion 連線驅動現有羊模型 IK。操作與裝置驗收範圍請見 [VR 混合連線說明](VR_Mixed_Connection_Guide.md)。

2026-11-08 工具改版的 14 項驗收已通過，涵蓋獨立未連線 Menu、無 Server 連線失敗、F2 視角與輸入隔離、企鵝模型位置及同步、換場與退出。詳見 [本次驗收](Connection_Test_Independent_Report.json) 與 [操作說明](VR_Lobby_Test_Guide.md)。以下為先前版本的測試記錄。

日期：2026-11-08。已完成 Server 先開房、VR 選單加入、玩家／載入人數、桌面手動開始及共用 MainGame 換場。本機 Rig 跨場景保留；網路角色與頭手同步留待下一階段。

## 已實際驗證

| 測試 | 結果與證據 |
|---|---|
| PC 鍵盤滑鼠操作 | [12/12 通過](VR_Lobby_PC_Controls_Report.json)，涵蓋 WASD、加速、升降、右鍵視角、換場後持續操作與斷線停用 |
| 獨立 Windows Server＋兩個 PC Client | [10/10 通過](VR_Lobby_PC_Multiplayer_Report.json)，涵蓋三個不同 PID、不同 PlayerRef、同房狀態／Server 權威、2/2 載入、各自輸入、共同換場、返回選單、晚加入拒絕與關服斷線 |
| Editor PC 畫面 | [通過](VR_Lobby_PC_Visual_Report.json)，相機有效、操作提示未溢出、共用 MainGame 啟用的 NavMeshAgent 為零。[實際畫面](ConnectionFlowEvidence/pc_main_game.png) |
| 真實 Photon Server＋兩個 Client，Editor Multiple | [15/15 通過](VR_Lobby_Acceptance_Report.json)：無人禁止開始、輸入驗證、重複連線、兩人載入等待、離開人數、同步換場、相同網路狀態 ID、重複開始、晚加入拒絕、關服斷線與返回選單 |
| 載入期間離開與權威檢查 | [4/4 通過](VR_Lobby_LoadingEdge_Report.json)：Lobby 載入阻擋、Client 不可開始、Lobby／MainGame 載入中的離線者移出等待名單 |
| 桌面實際指標點擊 | [通過](VR_Lobby_Pointer_Report.json)：CONNECT、Server Start Game，兩端進入 MainGame |
| Windows Development Build | [成功](VR_Lobby_Build_Report.json)：0 errors、192 warnings |
| 獨立 Windows Server＋Editor Single Client | [Lobby](VR_Lobby_Standalone_Join_Report.json) → [MainGame](VR_Lobby_Standalone_Game_Report.json) 通過：Editor 沒有本機 Server、Client 沒有 State Authority；人數及載入均 1/1，換場後狀態 ID 為 1024 |
| Single Client 返回選單 | [通過](VR_Lobby_Standalone_Return_Report.json)：只保留一個 ConnectionManager、一個桌面 AudioListener，顯示 Ready to connect |
| 獨立 Server 的 XR 狀態 | [通過](VR_Lobby_Server_Player_Report.json)：xrAuto=false、xrLoader=null、activeRigs=0 |
| 最終場景結構及測試設定還原 | [通過](VR_Lobby_Final_EditMode_Report.json)：四場景無 Missing Script、必要引用及按鈕事件完整；Single、XR 自動啟動開啟、Play Mode 停止、Runner=0、專案診斷設定已還原 |

跨程序測試透過診斷介面呼叫 Server 的同一個 StartGame 方法；桌面按鈕本身另由指標點擊測試驗證。測試有確認 frameCount 前進，並非只進入 Play Mode 或觀看靜態預覽。

新增的雙 PC 跨程序測試會觸發 CONNECT 與 Server Start Game 的既有按鈕事件，再透過 Input System 模擬鍵盤操作。兩個 Client 的位置各自獨立，測試階段採用本機自由飛行視角。三程序的背景隱藏視窗擷圖為黑畫面，已刪除該擷圖，保留的畫面證據來自 Editor PC 模式。最終程序均已結束。

## 交付內容

- [操作說明](VR_Lobby_Test_Guide.md)與[實作計畫](VR_Lobby_Connection_Test_Plan.md)。
- 四個流程場景：`Assets/_Project/Scenes/_flow test/` 下的 Menu_VRClient、ServerBootstrap、Lobby、MainGame。
- 獨立測試程式：`Builds/ConnectionTest/LiveStockFlow.exe`。`-flow-server` 開 Server，`-flow-desktop` 開桌面 Client，無參數為 VR Client。
- GDC 測試支援入口：`LiveStock > Connection Test`。不需要手動額外掛 GDC 測試元件。
- PC 操作元件位於 `Assets/_Project/Scripts/Multiplayer/DesktopTestPlayer.cs`，由桌面模式自動掛到測試相機。
- 驗收工具：`Tools/ConnectionFlow/`，位於 Assets 外，不會成為遊戲腳本。
- [選單畫面](ConnectionFlowEvidence/delivery_menu.png)、[等待室](ConnectionFlowEvidence/lobby_verified.png)、[MainGame 出生點](ConnectionFlowEvidence/main_verified.png)。

沒有修改 SessionLauncher.cs、SessionPanel.cs、GDC_METAVERSE 插件原始碼或原本四個 Server／VRClient 角色場景。Menu 仍保留既有停用的舊 Rig／Session 物件；只有指派給 ConnectionManager 的 Rig 用於新流程。

## 尚待實體裝置驗收

1. 接上 Quest Link 或專案既有 XR 裝置，選 `Start Server + VR Menu`。
2. 確認 LogInMenu 位於視線前方，控制器射線可點 CONNECT；房名先用 LIVESTOCK-DEV、region asia。
3. 等 Server 顯示 Loaded 1/1，從桌面按 Start Game；頭盔應進入 MainGame，頭手追蹤持續運作。
4. 確認出生高度、UI 距離可用，並從頭盔點 Return to Menu，確認沒有重複 Rig、相機或音訊。
5. 再以無參數的 Windows VR Client 重複一次，驗證手動 XR loader 啟動與裝置相容性。

本次沒有實體頭盔，因此沒有宣稱以上項目通過。Windows 建置警告主要來自既有 Sentis shader variant 與 Meta 相容性／棄用項目；無頭盔的 Server 記錄仍有 Meta 原生 OpenXR 裝置不可用訊息，雖然實際管理的 XR loader 為 null。現有測試 EXE 包含本機 Unity Pipeline 診斷功能，只供開發測試，正式發佈前需另做正式建置。

最終獨立程序記錄仍有既有 Fusion Avatar 範例資源的 Missing Script 警告，以及背景視窗的 graphics ring buffer 警告。四個流程場景的 Missing Script 檢查均為零。測試發現的預覽動物 NavMeshAgent 錯誤已在共用 MainGame 停用元件後消除。各類訊息的次數分開保存。[日誌檢查摘要](VR_Lobby_PC_Log_Summary.json)
