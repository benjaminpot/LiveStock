# Fusion 驗收入口

在專案根目錄執行，先儲存所有場景。Editor 套件會切換 Play Mode，結束時執行 End Test。

```powershell
& ./Tools/ConnectionFlow/Test-Fusion.ps1
```

預設 Editor 套件依序執行 Desktop 先加入、VR 先加入、生命週期、載入離線邊界、完整地圖與 NPC 驗收。每項建立自己的起始 Menu，最後清理 Runner。詳細 CLI 結果放在 `.tmp/FusionAcceptance/`，功能報告使用 `Docs/Fusion_Map_` 前綴。完整錯誤會使指令失敗並保留日誌。

## 選擇驗收範圍

| 指令參數 | 執行內容 |
| --- | --- |
| `-Suite Editor` | 五輪主要 Editor 功能回歸，預設選項 |
| `-Suite Assets` | 收納 GUID、Prefab 與場景腳本引用 |
| `-Suite Fruit` | 不需頭盔的雙 Client 蘋果收集、範圍／遮擋、去重、Player_UI 與新一輪重設驗收 |
| `-Suite Standalone` | 既有同版本建置的 Server、Desktop、VR 預覽，D3D11 模式採樣 30 秒 |
| `-Suite WeakNetwork` | 三組代表性模擬網路條件 |
| `-Suite MapSwap` | 暫時複製地圖並修改場景設定，完成後還原與移除測試場景 |
| `-Suite Soak -SoakSeconds 600 -NpcCount 60` | 建立三端與完整驗收後，進行持續測試 |
| `-Suite Soak -SoakSeconds 600 -NpcCount 100` | 100 隻容量持續測試 |

本機需已安裝 Unity CLI，且本專案 Editor 已開啟。Standalone 使用 `Builds/FusionValidation/LiveStockFusion.exe`，先透過 `LiveStock > Connection Test > Build Fusion Validation Player` 建置。專案保留這個驗收建置選單。

建議使用可重複的建置入口；它先確認沒有未儲存場景、等待編譯及 domain reload，再呼叫已編譯的建置選單，避開暫時 Pipeline 組件造成的 SDK 反射例外。建置回呼產生的已知設定快取與暫存檔會還原成建置前內容。

```powershell
& ./Tools/ConnectionFlow/Build-Fusion.ps1
& ./Tools/ConnectionFlow/TestFusionStandalone.ps1 -UseGraphics -SampleSeconds 30
```

獨立程序驗收會檢查 probe 版本，並以實際桌面相機及共用收集請求驗證三顆蘋果、共享進度與重送 RPC 去重。NPC 數量依目前地圖配置；60／100 隻另由容量套件指定，不能把兩者混為同一測試。

可選兩個隔離瀏覽器 context 的本機 WebRTC 驗收，使用本機已安裝的 Chrome 與 port 80；執行前確認該 port 未被其他服務占用：

```powershell
npm install --prefix .tmp/FusionBrowser --no-save --no-package-lock --ignore-scripts playwright
npm --prefix WebApp run build
& ./Tools/ConnectionFlow/TestFusionStandalone.ps1 -UseGraphics -WithBrowser -SampleSeconds 30
```

此模式以非批次的圖形程序執行，要求兩個瀏覽器都有解碼前進、非黑且非單色的影片內容，以及任務完成／重連快照。圖形裝置初始化、連線成功或解碼幀數增加，均不足以代替影像內容檢查。詳細證據與截圖留在各次 `.tmp/FusionStandalone-*`；失敗也會保留報告。這仍不包含實體頭盔操作。

MapSwap 開始前若已有 `.tmp/MapSwapSettings.json`，會要求先處理前次中斷的地圖測試。手動復原可在 Edit Mode 執行 `MapSwapAcceptance.Restore`。準備及復原的詳細日誌也放在 `.tmp/FusionAcceptance/`。

## 手動與實體頭盔驗收

使用 `LiveStock > Connection Test` 選單啟動 Server、Desktop 或 VR。依序檢查 Load MainGame、AwaitingStart 零 NPC、Start Game 後生成、姿態、離線與清理。

實體頭盔、控制器、XR UI、快速動作外觀及 CPU／GPU frame time 仍需裝置驗收。詳細操作見 [同步驗收指南](../../Docs/Fusion_Synchronization_Acceptance_Guide.md)，狀態見 [網路計畫](../../Docs/Fusion_Network_Optimization_Plan.md)。

## 保留的輔助程式

`Test-Fusion.ps1` 是測試指令入口。它呼叫同目錄的 C# 驗收腳本與 TestFusionStandalone.ps1，這些檔案是目前實作的一部分。

ConfigureFusionSynchronization.cs 與 ConfigureMapNetworkSetup.cs 是會修改資產的配置工具，平常驗收透過 Test-Fusion.ps1 執行。資產配置有變更需求時再使用對應工具。

蘋果功能的配置工具為 `ConfigureFruitGameplay.cs`，會修改既有 Prefab 和 Menu 引用，不屬於純驗收。專用資產檢查使用 `ValidateFruitGameplay.Run`，細節及未驗證範圍見 [實作記錄](../../Docs/VR_Fruit_Gameplay_Implementation_Report.md)。

歷史工具與舊版說明收納於 `Recycle Bin/ConnectionFlow/`。搬移檔案及 SHA256 保存在 `Docs/Fusion_Acceptance_Tool_Archive_Report.json`。舊版程式包含過期的房間階段、建置路徑和操作語意，恢復使用前需配合目前 API 更新。
