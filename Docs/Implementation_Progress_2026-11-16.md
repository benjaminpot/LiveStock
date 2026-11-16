# 階段實作與決策紀錄

基準：`1c1ff4b6`，分支 `single-to-multi`。使用者授權依規劃持續實作、驗證並分階段 commit，無須等待確認。

## 原則與待驗收邊界

- 保留既有 VR rig、Locomotor、UI 階層、Fusion 連線與兩階段開始流程。
- 每輪記錄實際修改、代做決定、驗證結果與下一步；保存點若仍有驗收缺口，明確標示 WIP 與失敗項目，不宣告整體完成。
- 實體頭盔與手把、頭盔內可讀性、裝置效能均不能由桌面／預覽代替。
- 本輪範圍為既有收集功能與其驗收鏈路，不新增出口、勝負規則或全面重寫移動。

## 第 1 輪：解除通用資產驗收的舊場景依賴

修改：`ValidateFusionAssets` 允許地圖沒有 `Recycle Bin` 根節點，仍完整檢查所有場景階層的遺失腳本及已封存資產 GUID。

代做決定：封存根節點是選用的收納結構；存在時必須唯一且停用。保留重複／啟用節點的失敗判定，不在新地圖補造空節點。

驗證：四項根節點契約檢查通過；完整 Assets 套件通過。已將契約檢查接入 `-Suite Assets`，後續會一起執行。報告為 `Fusion_Asset_Contract_Report.json` 與 `Fusion_Map_Asset_Report.json`。本輪未更動任何場景／Prefab。

下一步：修正無效／缺失輸入後保留舊身體資料的問題，補上伺服器拒絕與恢復測試。

## 第 2 輪：輸入失效與追蹤判斷

重現：新增真實 Fusion 負向案例後，原版本在明確失去姿態後仍接受收集請求，測試失敗。失敗詳細結果保存在 `.tmp/FusionAcceptance/FruitGameplayAcceptance.pose-loss-before-fix.json`。

修改：伺服器遇到錯誤角色、場景、非有限姿態或明確無姿態時立即撤銷身體資料與 VR 追蹤；缺少新輸入時用伺服器 tick 計時到期，RPC 亦獨立檢查有效期限。頭盔存在之外，正式 VR 輸入還要求頭部位置／方向及兩支控制器均受追蹤。

代做決定：短暫缺包容許 0.5 秒，明確無效資料不享有緩衝。沿用完整頭手姿態格式，因此一支手把失去追蹤時暫停完整姿態，不引入部分手部狀態協定；設備恢復後自動接受新樣本。明確的無頭盔 preview 仍保留原用途。

驗證：Unity 編譯通過，0 error；擴充的真實雙 Client 收集回歸 31 項通過，包含姿態失去、錯誤角色／場景、非有限姿態、無效膠囊、停止輸入後到期、恢復輸入後正常收集。既有連線驗收待下一輪整合檢查；硬體追蹤切換仍需頭盔驗收。

工具提示：CLI 已是 1.0.0 最新版；編譯回覆提示本專案 Pipeline 使用舊命令名稱，但相容操作成功。為避免無關變更，本輪不升級 Pipeline 套件。

下一步：依回歸結果修正整合問題，再重建獨立 Player。

## 第 3 輪：依現有地圖驗收 NPC

發現：Editor 前四組連線／混合角色／載入邊界回歸通過，第五組在「三端各 60 隻」逾時。執行日誌顯示目前新地圖實際生成 30 隻，舊工具仍硬編碼要求 60；不是本輪輸入修正使生成失敗。

修改：一般地圖回歸依現有生成配置的最小／最大數與伺服器完成數檢查每端一致、晚加入與重複生成。容量測試則檢查實際已生成數，必要時於執行期明確調為所選容量。驗收入口新增 frame 前進檢查，並將失敗訊息收斂為具體例外與完整日誌位置。

代做決定：保留組員地圖目前的 30 隻配置，不為了通過舊測試改成 60。60／100 隻是獨立容量案例，測試修改不寫回地圖。沒有新增對局規則。

驗證：重新執行完整 Editor 五組回歸全部通過；每次進入 Play Mode 均確認 frame 有前進。主地圖實測每端 30 隻，包含 AI 權限、導航、目標／骨架同步、晚加入與重複生成。瀏覽器與原生操作工具目前逾時，沒有取得完整串流證據。

下一步：回歸通過後補獨立 Player 的蘋果 RPC／進度探針、重建並驗收。

## 第 4 輪：拒絕網頁非法任務旗標

重現：新增五項負向案例，原 formatter 將字串 `"false"`、數字 `1` 當成真值，並接受進度未滿卻宣告完成的快照；五項全部失敗。

修改：`formatMissionStatus` 檢查提供的旗標必須是 boolean，並拒絕不一致的完成狀態。兩個頁面收到非法快照時保留上一筆有效畫面。

代做決定：為相容既有快照測試，未提供的選用旗標仍以原本的預設語意處理；只拒絕有提供卻型別錯誤的旗標，以及明確矛盾的完成狀態。不改訊息格式或 UI 版型。

驗證：任務狀態與既有 inventory-panel 共 26 項 Jest 測試通過，JSON 結果在 `.tmp/FusionAcceptance/mission-status-jest.json`。WebApp signaling 服務的 TypeScript 建置通過。

下一步：獨立 Player 的實際收集、兩個瀏覽器的持續解碼與完成／重連快照。

## Commit 索引

- `1c1ff4b6`：工作保存點，測試未完成。
- `a0034359`：第 1 輪資產驗收修正。
- `e3f6e738`：第 2 輪無效／過期輸入與追蹤檢查。
- `1fe99a0b`：第 3 輪地圖 NPC 與容量驗收分離。
- `f5132fae`：第 4 輪網頁非法任務旗標。
- `eae662af`：第 5 輪獨立程序與嚴格影片驗收保存點；影片內容仍失敗。
- `67eef490`：第 6 輪三組弱網互動與每端容量驗收。
- `027188ee`：60 隻／10 分鐘通過報告與統計限制。
- `e1f783f6`：100 隻／10 分鐘通過報告。
- `7f5153f9`：第 7 輪停用輸入來源時清理快取。
- `a57adcca`：第 8 輪載入完成啟動 ServerOnly，兩位瀏覽器影像／逐顆進度／重連通過。
- `cf338aa1`：第 9 輪最新 Player 的 21 項獨立程序回歸；影片失敗另存。
- `81e547b5`：第 10 輪獨立 Server 保持世界可見；hidden／batch 影片仍失敗。
- `63e9d623`：第 11 輪實際相機繪製診斷，撤回無重現依據的可見性變更；測試未完成。
- `603a26b9`：已編譯的建置準備選單與真正重載完成標記，Windows 建置通過。

## 第 5 輪：獨立 Player 與瀏覽器整合驗收

修改：驗收專用 probe 增加版本、三顆蘋果／共享進度／完成快照，使用原桌面 actor、相機與 PlayerGameplay 請求完成靠近與收集；追加重送 RPC 去重測試。獨立程序測試改依地圖配置核對 NPC 數量，並可選兩個獨立瀏覽器 context 驗證實際影像解碼前進、完成廣播與重新連線。

代做決定：新增控制入口只存在 `FUSION_ACCEPTANCE && DEVELOPMENT_BUILD && !UNITY_EDITOR` 的驗收建置。一般正式 Player 不提供檔案控制。瀏覽器／原生工具均逾時，改用隔離安裝於 `.tmp/FusionBrowser` 的 Playwright 與既有 Chrome；未修改正式 npm 依賴。沿用可渲染的 D3D11 Server，未切換會移除渲染的專用無頭建置。

驗證：TypeScript 服務建置、JS／PowerShell 語法與 Windows Player 建置通過。初次完整編譯為 0 error／190 warning，最新增量建置為 0 error／3 warning；不代表全部 warning 已修正。新版三個獨立程序的 19 項收集／同步／30 隻 NPC／30 秒持續檢查全部通過；報告為 `Fusion_Map_Standalone_Report.json`。影片內容另行記錄失敗，沒有納入這 19 項的通過宣告。

實測後加嚴：第一次程序／瀏覽器資料驗收 21 項通過，但截圖發現解碼中的影片仍全黑。已保留該輪證據於 `.tmp/FusionAcceptance/Standalone-before-video-content-check.json`，不能把那 21 項當成影片內容通過。新增影片像素範圍／非黑像素檢查，`Docs/Fusion_CCTV_Browser_Report.json` 明確記錄 `pass: false`。

影像排查：原始 CCTV 相機在唯讀 preview scene 可正常渲染農場，未修改其位置。實際 hidden Player 的兩個瀏覽器在尚未過熱時就持續解碼黑影像；非 batch 圖形程序也仍會重現。實際 sender 貼圖與相機診斷留在 `.tmp/FusionStandalone-20261116-052824`，原始來源預覽在 `.tmp/FusionAcceptance/CctvSourcePreview.png`。遮罩診斷未證明應修改正式相機；根因仍待定位，不能宣告完整 WebRTC 影像驗收完成。

修正：深色 live 頁面的任務文字原本受通用段落樣式影響而對比不足，已在既有 control-deck 範圍內指定亮色文字。未更動頁面階層。

建置排查：確認 `PipelineEval_*`／`PipelineRunScript_*` 暫時組件的 `Assembly.GetName()` 會拋出字串轉換例外。新增 `Build-Fusion.ps1`，等待編譯／domain 穩定、排程真正的 reload，再呼叫已編譯選單；已成功建置，並還原已知建置快取與暫存檔。曾發現 Camera 的內部 sceneCullingMask 不可用於 Player，診斷已改用公開的 overrideSceneCullingMask，後續 Player 編譯通過。

下一步：依實測排查整合問題，再進行 60／100 隻的容量與弱網測試。

## 第 6 輪：弱網互動與每端容量驗收（進行中）

修改：弱網套件依現有地圖 NPC 配置驗收，並在每種延遲／丟包／抖動條件下，實際確認輸入停止後不能收集、恢復後三端共享 1/3。容量測試除了伺服器 AliveCount，還會等待與持續核對每個 Runner 的副本數；出現例外仍輸出失敗報告，避免留下前次成功結果。

代做決定：保持地圖 30 隻不變；60／100 隻只在測試執行期設定。共享 Editor 的 frame time／記憶體包含三端與 Editor，不能當成 Quest 效能或獨立伺服器成本。

驗證：三組弱網全部通過（基線、50 ms／2% loss／10 ms jitter、100 ms／5% loss／20 ms jitter），每組每端 30 隻 NPC、過期輸入拒絕、恢復後三端共享 1/3。最高設定的觀察平均 RTT 約 225 ms。報告為 `Fusion_Map_WeakNetwork_Report.json`。60／100 隻持續測試接續執行；實體頭盔與正常可視圖形環境中的串流仍未驗收。

下一步：先各執行 60／100 隻的 10 分鐘持續測試。另已發現本機輸入元件停用時缺少快取清理，後續補負向重現並修正；不在持續測試中途修改會觸發 domain reload 的正式程式。

### 60 隻持續測試結果

通過 600.7 秒，三個 Runner 的最低副本數皆為 60，0 error，觀察伺服器速率約 64.01 tick/s。共享 Editor 平均畫面間隔 75.62 ms、P95 93.62 ms，配置記憶體差約 +15.7 MB；不能解讀成 Quest／單獨伺服器效能。報告為 `Fusion_Map_Soak_60_Report.json`。

頻寬欄位是每個觀察到的 render frame 讀取 SDK 最後一次 Fusion update 位元組統計後加總的估計，可能漏取 update，不是封包擷取。SDK 的單位與取樣時機依本機 `FusionStatisticsNetworkPage.cs`、`FusionStatistics.AfterUpdate` 與 [Photon Statistics 文件](https://doc.photonengine.com/fusion/v2/manual/testing-and-tooling/fusion-statistics-2.1) 核對；本輪只補充報告限制，未改寫原始測量數字。

### 100 隻持續測試結果

通過 600.0 秒，三個 Runner 的最低副本數皆為 100，測量區間 0 error，觀察伺服器速率約 64.04 tick/s。共享 Editor 平均畫面間隔 117.96 ms、P95 148.68 ms，配置記憶體差約 +15.4 MB。報告為 `Fusion_Map_Soak_100_Report.json`。本機依序測試，沒有控制所有系統／溫度變因，不能只用兩組畫面時間判定唯一效能瓶頸或裝置容量上限。

## 第 7 輪：停用本機輸入來源的快取生命週期

發現：`NetworkInputSource` 的 OnInput 讀取的是最後快取；元件停用後仍可能持續送出舊的有效姿態，讓伺服器持續更新有效期限。

重現：真實雙 Client 測試先確認快取有效，停用來源後發出收集 RPC；原版本仍成功收集，負向案例失敗。證據保存在 `.tmp/FusionAcceptance/FruitGameplayAcceptance.disabled-cache-before-fix.json`。

修改：只在 `NetworkInputSource.OnDisable` 呼叫既有 Clear，撤銷最後快取的姿態與身體。保留錯誤角色／場景／有限值等獨立負向注入，另加明確缺少姿態的案例，不用整體停用取代每項輸入驗證。

代做決定：沿用目前輸入格式，不新增 sequence、時間戳或另一套同步協定；這是本機來源的生命週期清理。

驗證：Unity 編譯 0 error；擴充後 33 項真實雙 Client 收集／來源失效／重新開局測試全數通過。此修正發生在前述容量與獨立 Player 測試之後，稍後的最終 Player 須重建，不能用舊執行檔宣告這個保存點的 Standalone 驗收已完成。

## 第 8 輪：場景載入完成後啟動伺服器專屬物件

重現：實際 Server 世界的 CCTV 元件存在，但 ServerOnly.IsActiveFor 為 false、SignalingManager.Running 為 false。新流程只有載入開始事件，漏接 ServerOnly 註解所依賴的載入完成啟動；Multi-Peer 合併場景後，Start 的備援判斷不能保證正確啟動。失敗證據在 `.tmp/FusionAcceptance/Cctv-before-scene-completion-fix.json`。

修改：NetworkSessionController 在 Fusion OnSceneLoadDone 後，對該 Runner 已載入場景套用既有 ServerOnly.ActivateFor。新增 `-Suite Cctv`，使用現有 Game view 將伺服器世界選為可見，讓兩個隔離 Chrome context 分別選擇不同相機，檢查實際影片、每次進度與重連。Editor 使用獨立報告檔，不覆蓋 hidden Standalone 的失敗。

代做決定：沿用 ServerOnly 的權限判斷，Client 不啟動串流；不改 NPC 的 Running 門檻，也不改組員的 CCTV 位置、過熱參數或渲染架構。沒有注入假的畫面。

驗證：Unity 編譯 0 error；12 項 Editor／真實瀏覽器整合檢查全部通過。兩端分別顯示 CCTV 01／02 的非黑農場影像，1920 寬影片解碼持續前進，兩端都觀察到 0/3、1/3、2/3、完成；重連保留完成狀態與正常影片。已目視確認重連截圖，農場與完成文字清楚。報告為 `Fusion_CCTV_Editor_Acceptance_Report.json`、`Fusion_CCTV_Editor_Browser_Report.json`；截圖位於 `.tmp/FusionAcceptance/Cctv-20261116-061932`。hidden Standalone 黑影像與實體 HMD 仍未通過。

下一步：重建最新 Player，補輸入來源停用／恢復的獨立程序檢查，並回歸場景生命週期。

## 第 9 輪：最新 Player 的來源生命週期

修改：驗收 probe 升至版本 3，新增僅供 Development 驗收的輸入來源停用／恢復指令與狀態；在第一次正常收集前，先停用來源並發出真實收集 RPC，要求收到伺服器拒絕且進度維持 0，再恢復輸入完成後續三顆。例外也寫入當次失敗報告，啟動時先清空上次報告。

代做決定：停用來源不等同斷線，因此恢復測試要求同一連線繼續使用；不擴充正式遊戲控制。重新建置後才宣告最新兩個生命週期修正的 Player 結果。Editor 影像通過與 hidden Player 影像分別判定。

驗證：最新 Player 建置通過，0 error／3 warning；三個獨立圖形 Player 的 21 項 RPC／來源停用恢復／30 隻 NPC／30 秒持續／離線清理檢查全數通過。嚴格雙瀏覽器測試仍記錄 `pass: false`，兩個不同相機皆在未過熱時解碼黑影像，證據位於 `.tmp/FusionStandalone-20261116-062738`。這個失敗與獨立 RPC 報告分開保存。

下一步：已找到新伺服器啟動時無條件 SetVisible(false)；驗證它對串流渲染的影響，再做主要 Editor 回歸。

## 第 10 輪：獨立伺服器的世界可見性（進行中）

發現：ServerConnectionManager 在 Multi-Peer 下無條件 SetVisible(false)。本機 Fusion 原始碼確認此 API 直接停用所屬 Renderer／Behaviour，會影響串流相機；Editor 測試因選取伺服器又將它設為 true，才出現兩種環境不同結果。

修改：只有存在既有本機 peer selector 時才在啟動時隱藏伺服器世界；獨立 Server 保持可見。驗收 probe 升至版本 4 並回報 Runner 可見性，獨立程序在影片驗收前明確檢查。未更改相機位置、圖層、URP 或串流編碼。

代做決定：保留 Editor 的單一選取視角行為，避免多個世界同時顯示；獨立伺服器沒有該測試選取器，應繪製自身世界供 CCTV 使用。

驗證：重建 0 error／3 warning，獨立 Server 的可見性檢查通過。然而兩個瀏覽器仍在未過熱時解碼全黑，不能宣告影片問題已修好。再以同一建置對照 D3D11 圖形 batch 模式，仍失敗；證據分別在 `.tmp/FusionStandalone-20261116-063329`、`.tmp/FusionStandalone-20261116-063541`。batch 模式保留為明確選項，不作為影像成功的替代判準。

當時下一步：補實際相機渲染回呼與 Renderer 診斷。後續核對發現驗收建置明確設定 Single Peer，上述 Multi-Peer 分支並未參與這個 Player 案例；第 11 輪撤回無重現依據的正式可見性變更。此輪不能視為黑影像修正。

## 第 11 輪：相機實際繪製的診斷（進行中）

修改：Development probe 版本 5 記錄 SRP endCameraRendering 次數、所屬 Runner 的 Renderer 啟用數與少量內容樣本。一般 capture-cctv 只讀取現有 sender 貼圖；明確的 diagnose-cctv 另將一個原始 CCTV 相機手動渲染到獨立暫存貼圖後還原。

代做決定：診斷只在嚴格影片已失敗後執行，原始失敗判定不變；手動來源圖不代表自然持續串流成功，不放進 ready 判斷，也不把手動 Render 加入正式 LateUpdate。

修正前輪推論：BuildFusionValidation 明確使用 Single Peer，沒有執行 SetVisible(false) 的 Multi-Peer 分支。因此撤回第 10 輪 ServerConnectionManager 的修改，避免保留尚無有效負向重現的正式程式變更；可見性欄位與模式對照仍保留供驗收診斷。

初次診斷：Player 中 4266 個所屬 Renderer、4025 個在階層與元件層級均啟用；明確手動渲染原始來源能取得正常農場圖，已目視確認。證據在 `.tmp/FusionStandalone-20261116-064138/cctv-manual-source.png`。這排除了場景內容完全缺失的推論，仍不代表持續串流通過。

診斷改善：捕捉檔名加入 frame，避免瀏覽器斷線後的樣本覆蓋最初 sender 狀態；同時記錄實際 pipeline／graphics device／peer mode，支援 SRP 與內建 pipeline 的完成回呼，不以單一回呼為零就推定完全未繪製。之後仍需主要 Editor／替換地圖回歸。

後續證據：`.tmp/FusionStandalone-20261116-064515/cctv-1067-diagnostics.txt` 顯示 URP、D3D11、非 batch、Single Peer；兩個實際 sender 相機均 active／enabled、有 RenderTexture，但自然繪製完成數為 0。原始貼圖仍黑，手動來源正常。這把缺口收斂到 Player 的自然相機繪製鏈路，尚不能宣告是單一 Windows 視窗原因；未為繞過失敗而在正式程式加手動每幀 Render。

圖形 batch 的同版診斷也得到兩個 sender 自然繪製完成數 0，證據在 `.tmp/FusionStandalone-20261116-064649`。來源唯讀 audit 確認兩個實際 streamCamera 都指定 targetDisplay=1（第二個顯示器）、CameraType.Game、URP Base；動態輸出透過 CopyFrom 繼承設定。下一步候選是只對串流輸出取消副顯示器綁定，再用相同嚴格案例驗證，不修改原始相機。

額度保存點：最後一批第 11 輪 commit／targetDisplay 候選修正／重建曾因自動核准審查的使用額度錯誤而完全未執行。重新唯讀核對後，最後 commit 仍是 81e547b5，九個檔案留有本輪修改。候選正式修正尚未套用；主要 Editor／換圖最終回歸尚待執行。

後續：重新透過正常審查成功核對並建立 WIP 保存點 63e9d623；工具恢復可用後，依原授權繼續第 12 輪。

## 第 12 輪：排除副顯示器假說與強化建置／證據發布

修改：只在動態輸出相機 CopyFrom 後將 targetDisplay 設為 0；原始 CCTV、預覽相機與位置均保留。probe 版本 6 在診斷回報 display 與 cameraType。

代做決定：串流輸出使用 RenderTexture，不應要求使用者先啟用組員相機原本指向的第二顯示器。不新增手動每幀渲染、不改 pipeline、過熱規則或編碼。

驗證：待重建並執行同一嚴格雙瀏覽器案例。

建置閘門：首次重建遇到 SDK／InputSystem 枚舉暫時組件時的字串轉換例外，保留失敗報告，沒有啟動舊 Player 代替驗收。準備流程改為已編譯選單直接要求 script reload，並由 InitializeOnLoadMethod 寫入完成標記；不再用 eval 建立另一個暫時組件，也不只等待固定秒數。原 delayCall 版本未留下本次要求的完成標記，已改為直接要求；專屬日誌確認目前要求與完成均執行。未修改 SDK 套件。

實測反證：新建置 0 error／190 warning。targetDisplay=0 的兩個 sender 仍有 RenderTexture、active／enabled，但自然繪製完成數仍為 0，見 `.tmp/FusionStandalone-20261116-104624/cctv-1006-diagnostics.txt` 與 `cctv-1238-diagnostics.txt`。已撤回 targetDisplay 正式修改，不把候選保存成已修好；保留 display／cameraType 診斷，probe 升至 7，要求最終 Player 重建。

另一個測試工具缺口：同輪發生 Windows `UNKNOWN` 錯誤，路徑是持續覆寫的公開 `Docs/Fusion_CCTV_Browser_Report.json`，原始錯誤在該次 runRoot 的 browser-evidence.json。改為執行中只更新 runRoot，開始／結束才發布公開報告，對觀察到的 Windows 檔案忙碌類錯誤作最多 10 次、每次 100 ms 的有界重試。失敗前先請 Server 捕捉仍存續的相機，避免斷線清理抹去診斷。這些只改驗收工具，不改遊戲邏輯。

工具回歸：撤回正式候選後 Unity 編譯 0 error；新發布流程的 Editor／真實雙瀏覽器 12 項再次通過，無頁面錯誤。新增的診斷將額外記錄所有相機的總完成數，並在 Editor 套件直接檢查兩個自然繪製回呼，作為 Player 診斷的對照。

繪製對照已通過：擴充後 Cctv 套件 13 項通過，兩個不同 `View 1` 的自然 SRP 完成回呼均被觀察到，且兩個真實瀏覽器仍取得影像、逐顆進度與重連。這確認目前的回呼診斷在正常 Editor 渲染環境可以觀察到實際繪製。

下一步：重跑 Editor 的真實雙瀏覽器套件與主要回歸／換圖，然後在 run_script 暫時組件已存在的情況下，再驗證建置前重載與最新獨立 RPC。

## 第 13 輪：最終整合回歸與交接狀態（進行中）

範圍：依序執行含自然繪製回呼的 Cctv、五組 Editor、33 項 Fruit、Assets 與 MapSwap。測試結束還原 peer mode、場景設定與臨時地圖；最後再建置 probe 7 並驗證獨立程序。

代做決定：不重跑已完成的兩組 10 分鐘容量案例，因其 Editor 生成／輸入測量結果已保存；不把那些共用 Editor 數字當成最新獨立伺服器或 HMD 效能。修正指南中的舊 NPC 數量、必備 Recycle Bin、示範 Cube 與建置報告名稱，歷史量測保留並標明範圍。
