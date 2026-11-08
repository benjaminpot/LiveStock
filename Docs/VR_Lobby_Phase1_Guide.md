# 第一階段：場景整理交付與手動檢查

此文件保留第一階段的交付紀錄。後續功能已接續開發，現在的操作方式請看 [VR_Lobby_Test_Guide.md](VR_Lobby_Test_Guide.md)，不要把下列當時尚未實作的項目視為目前狀態。

本階段只整理場景、UI 與設定。尚未實作 Server 開房、玩家人數同步、跨場景保留 VR Rig 或 Start Game 換場功能。不要以目前不能連線判定第一階段失敗。

## 已建立的場景

路徑皆在 `Assets/_Project/Scenes/_flow test/`。

| 場景 | 內容 |
|---|---|
| ServerBootstrap | ServerRoot、空的 ServerConnectionManager 掛載物件、ServerDesktopCamera、ServerCanvas、ServerEventSystem |
| Menu_VRClient | 沿用你的 VR 登入選單，ConnectionManager 指向共用 Lobby |
| Lobby | 由原 Lobby_VRClient 複製，保留等待室，新增 VRSpawnPoint |
| MainGame | 由原 MainGame_VRClient 複製，保留世界內容，新增 VRSpawnPoint |

原 Lobby_Server／Lobby_VRClient／MainGame_Server／MainGame_VRClient 保留，SHA-256 比對確認本階段沒有修改。舊 Session 元件沒有刪除或修改腳本，新入口中的元件保持停用。

Server UI 的 Start Game 目前停用且沒有 On Click 事件，因為開房與開始邏輯是後續交付。UI 顯示的房名與地區目前是提示文字，不是運行中的伺服器狀態。

Lobby／MainGame 的原預覽相機與 AudioListener 停用，避免未來載入時與保留的 VR Rig 重複。直接獨立 Play 這兩個場景時，可能沒有遊戲相機；現階段請用 Scene View 檢查內容。

VRSpawnPoint 是預留給第二階段後的 Rig 定位標記，尚未有腳本使用。初始位置：Lobby `(0, 0.1, 0)`，MainGame `(0, 1.5, 0)`；請在 Scene View 確認地面高度與站位，正式使用前需要頭盔驗收。

## 你現在可以執行的手動檢查

1. 等 Unity 自動編譯／匯入結束，確認 Console 沒有新增紅色錯誤。本階段沒有新增 runtime C# 腳本。
2. 開啟 `ServerBootstrap.unity`，展開 ServerRoot。確認有 ServerCanvas 和 StartGameButton；按鈕的 Interactable 為關閉，這是預期設定。若要看桌面 UI，可由你進入 Play Mode，查看 Game View；它不會開房。結束後離開 Play Mode。
3. 開啟 `Menu_VRClient.unity`，選取 ConnectionManager。確認 Room Name Input、Region Input、Connect Button、Status Text 四欄都有引用，Network Scene Path 是 `Assets/_Project/Scenes/_flow test/Lobby.unity`。
4. 檢查 LogInMenu 的 ConnectButton，On Click 保持綁定 `ConnectionManager.Connect()`。Server 尚未實作，這階段先不用按 Connect 測試。
5. 分別開啟共用 Lobby／MainGame，在 Scene View 檢查等待室與地圖內容，確認各有一個 VRSpawnPoint。若位置需要調整可以直接移動並保存，後續會沿用。
6. 在 Build Profiles 的 Scene List 確認下列 index：0 Menu_VRClient、1 ServerBootstrap、2 Lobby、3 MainGame。其他舊場景保留但不勾選。
7. 最後回到 Menu_VRClient。若有錯誤，提供第一個紅色錯誤與堆疊，先修正再做第二階段。

## GDC_METAVERSE 需要額外腳本或物件嗎？

| 要做的事 | 需要新增嗎？ |
|---|---|
| 切換 Single／Multiple | 不需要。直接使用 GDC_METAVERSE > Peer Mode 選單 |
| 用 Single Peer 建置 | 不需要專用場景物件。使用插件 Build 選單，確認平台及場景清單後再建置 |
| 真正建立 Server 房間 | 需要 Server 啟動腳本；這不是插件內建功能，排在第二階段 |
| 一次啟動 Server＋VR Menu | 需要專案自己的 Editor 測試入口；排在第二階段 |
| 第二位不使用 VR 的測試玩家 | 需要測試客戶端入口；排在第五階段 |
| 生成共享箱子／道具 | 需要 NetworkSpawner 和對應回呼；本次第一階段不用 |

第二階段預計新增 ServerConnectionManager.cs 與 Editor/LocalConnectionTestTools.cs。前者掛在已建立的 ServerConnectionManager 物件，後者提供 Editor 選單，不需在場景再掛一個測試元件。它會使用 GDC 的 PeerModeTools 設定 Multiple，並處理測試結束後還原，插件原始碼不必修改。

目前 Peer Mode 仍保持 Single；現在手動切成 Multiple 也不會自動出現 Server 或第二位玩家。

## Build 與復原注意事項

- 目前預設第一個 Build 場景是 Menu_VRClient，所以直接使用插件 Build 功能會以 VR Menu 為啟動入口，不會自動變成 Server 程式。Server 啟動路由及跨程序建置留到後續階段。
- 原 Build 清單位於 `VR_Lobby_Phase1_PreviousBuildScenes.json`，可依該表在 Build Scene List 恢復勾選。
- 四份原角色場景可繼續開啟；共用版是新增資產，沒有覆蓋原版本。
- 結構檢查報告在 `VR_Lobby_Phase1_EditMode_Report.json`。這只證明場景引用、Missing Script、啟用狀態與 Build 清單等 Edit Mode 結構，不能證明連線、VR 互動或換場已成功。
