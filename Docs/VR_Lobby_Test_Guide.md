# Server／VR Lobby 測試操作說明

## 功能範圍

Server 先開房並進入 Lobby；玩家從現有 LogInMenu 連線，Server 顯示已加入／已載入人數，操作者手動按 Start Game，一起進入共用 MainGame。房間、Runner 連線及 LobbySessionState 的網路身分保留，不在換場時重新連線。

本次不生成網路玩家模型、不傳送頭手姿態、不啟動 NPC／CCTV／語音；本機 VR 視角仍使用現有 Rig。加入 Fusion 房間並不是帳號登入。

## 一台電腦、沒有頭盔的測試

1. 保存正在編輯的場景，離開 Play Mode。
2. 選單選擇 **LiveStock > Connection Test > Start Server + Desktop Menu (no headset)**。
3. 工具透過 GDC 的 PeerModeTools 暫時切成 Multiple，開啟 Menu_VRClient，再於 Play Mode 加入 ServerBootstrap。Server 自動開 Lobby，但玩家不會自動加入。
4. Game View 使用 Display 1；左上是 Server 管理區，右側是登入選單。等待 Server 顯示 Lobby，確認 Players 為 0 且 Start Game 停用。
5. 保留房名 `LIVESTOCK-DEV`、region `asia`，按 **CONNECT**。
6. 預期進入等待室：Server 顯示 Players 1、Loaded 1/1；客戶端顯示等待 Server 開始。
7. 選單選擇 **Add Test Client**，預期 Players 2、Loaded 2/2。這位測試玩家是真實線上 Client，不會建立第二個 VR Rig、相機或 AudioListener。
8. 點左上 **Start Game**，兩位玩家一起進入 MainGame，Loaded 最後為 2/2。開始時房間會關閉，不接受新玩家。
9. 可使用 **Leave Last Test Client** 測試玩家離開。
10. 測試結束選 **End Test**，或停止 Play Mode；工具還原原本 Peer Mode、XR 自動啟動與 Game View Display 設定。

若 Unity 場景尚未保存，入口會提示先保存，不會自行彈出阻塞自動化的儲存視窗。

## 有 VR 頭盔的操作

1. 先確保 Quest Link／實際 XR 裝置已能在目前專案中運作。
2. 選 **Start Server + VR Menu**。此模式不全域關閉 XR，只有 Server 不建立 VR Rig。
3. 在頭盔中使用原 LogInMenu 的 CONNECT。LogInMenu 和換場後的 SessionCanvas 都已配置 Meta PointableCanvas、RayInteractable 與裁切平面；PCM 的 Canvas Module 與同一個本機 Rig 會隨 ConnectionManager 保留。
4. 由電腦操作者在桌面按 Server 的 Start Game。頭盔顯示共用 MainGame。
5. **Return to Menu** 會退出本次客戶端連線並恢復入口。遊戲開始後舊房間已關閉，若要開新一輪，先 End Test 再重新啟動測試。

目前沒有加入虛擬鍵盤；可先在 Inspector 預填房名／地區。實體頭盔畫面、控制器按鍵與射線點擊仍須由有裝置的操作者驗收，桌面滑鼠測試不能代替這些結果。

## 測試載入等待

選 **Add Delayed Test Client (5 seconds)**。新 Client 的載入完成回報會延遲五秒；這段時間 Server 應顯示 Loaded 小於 Players，Start Game 停用。等回報後才可開始。

Server 不計入玩家人數；開始條件是至少一位玩家且所有已連線玩家完成 Lobby 載入。不會自動開始，不需要 Ready 按鈕。

## 獨立 Windows 程式

一般測試建置入口：**LiveStock > Connection Test > Build Windows Connection Test**，使用 GDC Single Peer Build。輸出到 `Builds/ConnectionTest/LiveStockFlow.exe`。

同一個二進位檔使用不同啟動參數，保持四個場景的 build index 完全一致：

```powershell
# 有桌面管理 UI 的 Server，不啟動 XR。
& '.\Builds\ConnectionTest\LiveStockFlow.exe' -flow-server

# 無頭盔的桌面 Client，仍由使用者按 CONNECT。
& '.\Builds\ConnectionTest\LiveStockFlow.exe' -flow-desktop

# VR Client：正常啟動，不加上述參數。
& '.\Builds\ConnectionTest\LiveStockFlow.exe'
```

Server 要保留手動按鈕，所以使用有畫面的 Windows 程式，不加 `-nographics`。同一台電腦只讓 VR Client 使用頭盔。

自動驗證另外使用 Development Build，暫時將專案既有 Unity Pipeline 的 Player 診斷烘焙到測試程式，以便讀取獨立 Server 狀態並觸發同一個 StartGame 方法；建置結束還原專案設定。這份診斷測試二進位檔不是正式發佈版。

目前已產生的 EXE 就是這份 Development 診斷測試版。重現自動化建置可在專案根目錄執行 `& './Tools/ConnectionFlow/Build-Test.ps1' -WithDiagnostics`；此工具會等待乾淨的腳本重載，避免 Unity Pipeline 暫時執行組件干擾 Input System 建置，並在完成或失敗時還原診斷與 XR 設定。一般手動測試可直接使用上述 Unity 選單建置。

## 場景與腳本對照

| 項目 | 作用 |
|---|---|
| ServerBootstrap／ServerConnectionManager | 開房、Server UI、關房與手動換場 |
| Menu_VRClient／ConnectionManager | 讀取輸入、連線、保存本機 Rig／UI、退出與返回選單 |
| Lobby／MainGame | 共用場景，每個 Runner 有自己的場景副本 |
| FlowSceneMarker | 標記 Lobby 或 MainGame，指定本機 VRSpawnPoint |
| LobbySessionState Prefab | Server 權威的階段、人數與載入回報，跨場景保留 |
| FlowLaunchOptions | 獨立程序的 Server／桌面啟動選擇與 XR 分流 |
| Editor/LocalConnectionTestTools | GDC 模式切換、一次啟動兩端、增減測試玩家與測試結束還原 |

沒有修改 SessionLauncher.cs、SessionPanel.cs 或 GDC_METAVERSE 插件原始碼。新的等待狀態顯示整合在 ConnectionManager 的 SessionCanvas，沒有另建不必要的 UI 框架。

## 結果與限制

- [完整測試結果與報告連結](VR_Lobby_Test_Results.md)：線上驗收 15/15、載入邊界 4/4、滑鼠點擊及獨立 Server＋Editor Client 跨程序測試通過。
- Windows 建置成功，0 errors、192 warnings；警告主要來自既有 Sentis shader 與 Meta 套件，未以此推論實體 VR 已驗收。
- 頭盔追蹤、控制器射線及遠端角色同步，各自獨立驗收；本次沒有實作遠端角色同步。

若遇到問題，請提供 Server 狀態、Players／Loaded、客戶端狀態，以及 Console 第一個紅色錯誤及堆疊。完整驗收工具位於 `Tools/ConnectionFlow/`，不在 Assets，不會自動成為遊戲腳本。
