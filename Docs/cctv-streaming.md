# CCTV 串流、獨立選台與共享過熱

更新日期：2026-10-23。

目前支援每人最多同時觀看兩台、獨立選台、每人共用 3 秒冷卻，以及共享過熱／恢復。規則、網頁邏輯、MID 影像路由、Chromium 版面與真正的 Unity／WebRTC 雙畫面整合測試均已通過。手機 Safari 的實際播放與出聲仍待使用者確認；Loopback 本機音訊仍延後處理。

## 接續開發先看這裡

- 場景：[nyan test renderStream.unity](../Assets/_Project/Scenes/nyan%20test%20renderStream.unity)。
- 連線、輸入路由、鏡頭輸出：[CCTVViewSwitcher.cs](../Assets/_Project/Scripts/CCTVViewSwitcher.cs)。
- 個別選台／冷卻與共享過熱：[CCTVMonitorState.cs](../Assets/_Project/Scripts/CCTVMonitorState.cs)。
- 操作綁定：[InputSystem_Actions.inputactions](../Assets/_Project/InputSystem_Actions.inputactions)。
- 接收頁：[index.html](../WebApp/client/public/receiver/index.html)、[main.js](../WebApp/client/public/receiver/js/main.js)、[cctv-panel.js](../WebApp/client/public/receiver/js/cctv-panel.js)。
- 雙影像路由：[cctv-video.js](../WebApp/client/public/receiver/js/cctv-video.js)，依 videoMids 對應到各自的 video 元素，音訊只加入主播放器。
- 串流設定：[LiveStockRenderStreamingSettings.asset](../Assets/_Project/Settings/LiveStockRenderStreamingSettings.asset)，Automatic Streaming 關閉。
- 修改前核對程式及儲存場景；行為改變後同步更新本文與相關驗證結果。

**每條瀏覽器連線自動顯示最多兩台不同的監視器，不需要第二畫面開關。兩格共用該玩家的 3 秒冷卻，與其他玩家獨立；各台監視器的過熱值共用。場景只有一台時只顯示一格。**

## 本批已確認的行為與交付狀態

2026-10-22 使用者確認：本批先驗收單畫面；過熱後黑畫面並等待恢復，保留手動切走及原台自動恢復；重新整理視為新連線，選台及冷卻重新開始。這些是原型階段接受的行為，不需要加入玩家身分或重連還原。

雙畫面的最終操作：連線後自動出現主畫面與第二畫面，移除使用者認為多餘的開關。同一人兩格選不同鏡頭，共用該玩家的切換冷卻，初次連線不需要額外操作或冷卻。選入另一格已在看的鏡頭時，兩格交換位置，不會重複累計觀看人數。

本批在 `feature/cctv-monitor-review` 開發，基於 `0ab53a89` 的 main 還原 stash。原 stash `0496b5460a79bbdb71597b3c58390b8abb31c22f` 保留備份；排除了 VersionControlSettings 的模式切換，以及 RenderStreamingProjectSettings 的精靈顯示旗標變更。沿用既有 `Docs/` 目錄，避免另建只差大小寫的 docs 目錄。

程式審查已核對連線各自的兩組輸出、輸入／冷卻、按監視器聚合熱量、斷線釋放及各格過熱畫面還原。規則、UI 與雙 Chromium 接收頁各開兩格的 Unity 串流整合檢查通過。2026-10-23 使用者指定先將本批 P0 提交並合併到本機 main，取代先前等待 Safari 驗收才合併的安排。**實體手機 Safari 的畫面、聲音與操作驗收仍待完成；手機尺寸 Chromium 的結果不等於 Safari 實機結果。**

## 為什麼手機切換時電腦也會切換

先前的原型只有一個 Video Stream Sender、一個目前選台索引與一份冷卻。修改 Sender.sourceCamera，會同時改變它送給所有瀏覽器的影像。

修正後，每條 connectionId 都有自己的 Session、輸出 Camera、Video Stream Sender、Input Receiver、選台與冷卻。選台只調整該 Session 的輸出 Camera，保留該連線的 RenderTexture 和影音連線。

電腦與手機仍開同一個接收頁網址即可，不需要不同 URL。頁面網址相同不代表遊戲狀態必須共用。

## 場景與 Inspector 如何設定

儲存的場景維持兩組實體視角；不需要為兩位使用者再手動複製整組管理元件。

```text
CCTV Points                         ← CCTVViewSwitcher
├─ 01 CCTV Group
│  ├─ 01 CCTV                       ← 視角 Camera、Sender 設定範本、Manager／Broadcast／Input 設定
│  └─ 01 Camera                     ← Unity 本機預覽
└─ 02 CCTV Group
   ├─ 02 CCTV                       ← 第二個視角 Camera
   └─ 02 Camera                     ← 第二個 Unity 本機預覽
```

| 欄位／元件 | 保留的設定 | Play 時用途 |
|---|---|---|
| CCTVViewSwitcher / Video Sender | 01 CCTV 的既有 Sender | 作為每條連線 Sender 的設定範本 |
| Views / Element 0 | 01 CCTV Camera、01 Camera | 第一個視角與本機預覽 |
| Views / Element 1 | 02 CCTV Camera、02 Camera | 第二個視角與本機預覽 |
| Views / Display Name | 可留空 | 預設顯示 CCTV 01、CCTV 02 |
| 01 Signaling Manager | 啟用、Run On Awake 開啟 | 只有這一套管理連線 |
| 01 Broadcast / Streams | 保留原有 Video、Audio、Input 登記 | Awake 時程式暫時移出 Video／Input，Broadcast 繼續負責音訊與回覆 Offer |
| 01 Input Receiver | Local 關閉、Label input、Actions 原資產、Default Map Player | 提供每條連線的輸入設定範本 |
| 01 Input Receiver / CCTV Switch 事件 | CCTV Points → Dynamic OnSwitch | 供程式找出要綁定的 Action；執行時建立帶 connectionId 的事件 |
| 02 Signaling Manager | 停用且 Run On Awake 關閉 | 不啟動第二套連線 |
| 02 Broadcast | 停用 | 不廣播第二組重複串流 |
| AudioListener | 只有 01 啟用，Preview 不掛 | 保持現有音訊收集方式 |

程式會在最初的 Awake 自動登記 CCTVViewSwitcher 為 Signaling Handler，並排在 Broadcast 前面，讓連線建立自己的 Video Sender 後再由 Broadcast 回覆 Offer。範本 Video Sender 與 Input Receiver 在 Play 時停用，避免再共用來源或重複處理輸入。

實體 CCTV Camera 在此版本作為視角設定來源，Play 時停用渲染。連線成立後會在 CCTV Points 底下看到程式產生的「CCTV Viewer …」子物件：根物件只有該連線的 Input Receiver，View 1／View 2 各自包含 Camera、Video Sender。不會複製 Signaling Manager、Broadcast 或 AudioListener。

兩條影像軌在初始 Answer 一起建立，預設選兩台不同且可用的鏡頭；若只有過熱中的第二台，保留該台恢復畫面。只有一台實體 CCTV 時，第二格索引為 -1，停用其 Camera／Track 並隱藏網頁第二 video。兩個玩家最多有四組影像輸出，音訊每人一份，不新增畫面開關或重新協商流程。

本機 Preview Camera 的 Output／Target Texture 留空，輸出 Display 1。Game 視窗預覽跟隨最後成功操作的視角，不會改變其他瀏覽器的選台。這個本機預覽不算觀看者。

## 連線與切換如何運作

1. 網頁的 RenderStreaming 為自己建立一條 connectionId。
2. CCTVViewSwitcher.OnOffer 建立 Session、兩組 Camera／Video Sender 與一個 Input Receiver，並透過 AddSender 綁到該 connectionId。Camera 與 URP 設定、Sender 參數從原有範本複製。
3. 原有 Broadcast 繼續提供 Audio Sender 並回覆 Offer。它已暫時移出共用 Video／Input，所以不會再把兩人綁到同一個輸入入口。
4. 網頁建立 Label 為 input 的通道。OnAddChannel 依 connectionId 與 Label，交給對應 Session 的 Input Receiver。
5. Input Receiver 使用各自複製的 Actions 與配對裝置。輕點主影片映射到 Player / CCTV Switch，事件呼叫 SwitchNextFor(connectionId, 0)；第二格點擊使用 cctv-next、slot 1，不建立第二套鍵盤／觸控裝置。
6. SwitchFor(connectionId, index, slot) 交給 state.TrySwitch(connectionId, index, slot)，檢查該玩家共用冷卻、slot 必須為 0／1、第二格是否已開啟與目標過熱狀態。
7. 成功後只調整該玩家兩格的拍攝設定、位置與旋轉，保留各自 RenderTexture 與 Track。因此另一位玩家的影像不變，也不需要重連。
8. 連線斷開時只移除對應 Session、輸入裝置及輸出，其他使用者的選台與冷卻不受影響。

選台不會修改原始範本的 sourceCamera。原始 OnSwitch／SwitchNext／SwitchToView 這些沒有 connectionId 的入口，只供本機預覽使用；真正的網頁操作均走有 connectionId 的路徑。

InputReceiver 的儲存事件字串可能仍保留舊名稱 Player/Attack，但 Action ID `6c2ab1b8-8984-453a-af3d-a3c78ae1679a` 對應目前的 CCTV Switch。核對時需同時看 GUID 與 Inspector。

## 網頁 UI 與狀態資料

UI 是接收頁的 HTML／CSS，影片由原有 VideoPlayer 的 video 元素播放。之後設計版面時修改 receiver 的 HTML、CSS 與 cctv-panel.js。

主播放器維持原有 VideoPlayer；第二格用獨立的無聲 video。CCTVVideo 先保存 ontrack 收到的影像，收到 Unity 的 videoMids 後依協商 MID 分配，避免把兩條 video Track 塞到同一個 video 或依不穩定的事件抵達順序分配。狀態或 Track 先到都可處理。網頁「選台目標」可選主畫面／第二畫面，按鈕操作對應 slot；輕點影片則操作該影片。

除了 input 二進位操作通道，網頁在 input 開啟後會建立獨立的 cctv JSON 通道。OnAddChannel 依 Label 路由，不依抵達順序選擇第一個空的 Input Receiver。

| 方向 | 訊息 | 用途 |
|---|---|---|
| 網頁 → Unity | `{"type":"cctv-ready"}` | 請求目前狀態 |
| 網頁 → Unity | `{"type":"cctv-select","slot":1,"index":1}` | 第二格選索引 1，slot 與 index 都從 0 開始；省略 slot 預設主畫面 |
| 網頁 → Unity | `{"type":"cctv-next","slot":1}` | 該連線第二格找下一台可用監視器 |
| Unity → 各網頁 | cctv-state，約每 0.2 秒 | selectedIndices、videoMids 各兩格，cooldownRemaining 共用，搭配共用 monitors |

selectedIndices 例如 `[0,1]` 為兩格鏡頭索引；場景只有一台時為 `[0,-1]`。selectedIndex 保留為主畫面索引以相容原訊息。videoMids 是每格 Video Sender 對應的協商 MID，只用來對應影像，不當作玩家身分。原先開關用的 cctv-secondary 指令已移除。

共用 monitors 包含 name、status、heat、recoveryRemaining、viewers。狀態為 ready（可使用）、watching（觀看中）、overheated（過熱停用）。網頁面板顯示個別選台與冷卻、共用過熱與恢復倒數。切換限制由 Unity 檢查，不能只靠網頁停用按鈕。

## 過熱與冷卻預設

| Inspector 欄位 | 預設 | 規則 |
|---|---|---|
| Heat Limit | 100 | 達到上限停用該監視器 |
| Heat Per Viewer | 5／秒 | 同台一人 +5／秒，兩人 +10／秒 |
| Cooling Rate | 10／秒 | 沒人觀看該台時下降，最低 0 |
| Recovery Duration | 10 秒 | 過熱期間不能選入，時間到歸零恢復 |
| Switch Cooldown | 3 秒 | 每位使用者的兩格共用，其他使用者不受影響；首次連線不用等待 |

觀看者按實際已連線的 Session 計數，同一 connectionId 不會重複累加；尚未連上的 Offer 不算觀看。不同分頁會建立不同 connectionId，未來加入登入或玩家身分後需再改成按玩家 ID 去重。

過熱時，只將正在看該台的 Session Camera 暫時改為黑色輸出，網頁顯示倒數。同一台兩人一起看時，兩人都會看到它過熱；另一台不受影響。保持選中者在恢復後自動重新看到畫面並繼續升溫。

## 如何驗收

1. 停止 Unity Play，等待程式編譯完成。保持原本 Inspector 接線，不要在 Play 時手動重啟另一套 Manager。
2. 啟動 WebApp 與 Unity Play。電腦和手機都重新整理並開啟同一個 receiver/index.html，再各自連線。
3. 場景兩台 CCTV 時，起始每人主畫面為 01、第二畫面為 02。手機主畫面選 02，該玩家兩格交換；電腦瀏覽器應保持主 01、第二格 02。
4. 手機的冷卻為 3 秒時，電腦仍可立即切台；兩人的按鈕與目前鏡頭名稱應各自正確。
5. 分別測試輕點影片與網頁按鈕；兩種操作都應只影響發出操作的連線。
6. 各台按實際觀看人數升溫：一位玩家看該台每秒 +5，兩位一起看該台每秒 +10。只有兩台且兩人都連線時，兩台都各有兩位觀看。
7. 持續看至 100，該台黑畫面停用 10 秒後恢復。其他鏡頭仍能正常觀看。
8. 關閉手機頁面，電腦保持原選台，觀看人數降為 1。無人觀看的鏡頭應降溫。
9. Unity Game 視窗只代表本機預覽，可跟著最後操作變化；請用電腦瀏覽器檢查「另一位玩家」是否被切台。
10. 連線即應看見兩台真實影片，不需要第二個開關；音訊維持一份。
11. 改變選台目標或輕點第二影片，只影響該玩家；選到另一格的鏡頭會交換兩格。任一格操作後，兩格都受 3 秒冷卻限制。
12. 場景增加第三台以上鏡頭時，可在兩格中選任意兩台；無人觀看的鏡頭應降溫。同一人不能用第三格或重複觀看同台。
13. 過熱遮罩要分別對應主／第二格；關閉接收頁後，兩台都應釋放該玩家的觀看計數。只有兩台且始終同時觀看時，熱量會一起累積，可能同時過熱。

自動測試：

```powershell
./tests/cctv/run.ps1
node --test tests/cctv/panel.test.mjs tests/cctv/video.test.mjs
```

第一個編譯實際的 CCTVMonitorState，驗證個別選台、個別冷卻、不同鏡頭分別升溫、同台共享熱量、斷線／重連及過熱恢復。測試使用本機 Unity 6000.3.25f1 的 .NET 工具，不同安裝位置可傳入 -EditorData。

Node 命令驗證面板狀態、雙格控制、共用冷卻、各格過熱遮罩、斷線清除、兩個接收頁獨立資料，以及 Track 抵達順序不同時依 MID 正確分配影像且不重複音訊。這些測試不取代 WebRTC、Unity 渲染與 Safari 播放的實機驗收。

另有真實 Chromium 的面板測試，使用專案實際 HTML／CSS／CCTVPanel，送入固定測試狀態封包，驗證兩個獨立瀏覽器 context、手機觸控、冷卻按鈕、過熱遮罩與無水平溢出的版面。它不啟動 Unity，也不驗證影音串流。

Playwright 可安裝到暫存目錄，不修改專案套件依賴：

```powershell
$cctvBrowserTools = Join-Path $env:TEMP 'LiveStock-CCTVBrowserTools'
npm.cmd install --prefix $cctvBrowserTools --no-save --ignore-scripts playwright
node (Join-Path $cctvBrowserTools 'node_modules/playwright/cli.js') install chromium
$env:CCTV_PLAYWRIGHT_MODULE = Join-Path $cctvBrowserTools 'node_modules/playwright/index.mjs'
node tests/cctv/browser-smoke.mjs
```

2026-10-23 自動驗證紀錄：移除第二畫面開關後，C# 規則測試、9 項網頁邏輯／路由測試、Chromium 面板測試均通過（桌面 1280×900、手機尺寸 390×844）。面板截圖產生於被 Git 忽略的 `Library/CCTVReview/desktop-dual.png`、`mobile-dual.png` 等檔案，可在本機重跑取得。

真正的 Unity 串流整合測試：[stream-smoke.mjs](../tests/cctv/stream-smoke.mjs)。先在 Unity 開啟上述 CCTV 場景並按 Play、保持兩台監視器與本文預設參數，啟動 WebApp，關閉其他接收頁，再執行：

```powershell
$env:CCTV_PLAYWRIGHT_MODULE = Join-Path $env:TEMP 'LiveStock-CCTVBrowserTools/node_modules/playwright/index.mjs'
node tests/cctv/stream-smoke.mjs
```

預設連到 `http://127.0.0.1/receiver/index.html`；其他位置可用 `CCTV_RECEIVER_URL` 環境變數指定。測試會建立桌面與手機尺寸的兩個獨立 Chromium context，讀取真正的 cctv 狀態封包，透過網頁按鈕與官方 input 通道操作 Unity，最後關閉測試瀏覽器。測試過程會改變共享熱量與本機預覽。

2026-10-22 整合驗證結果：兩端接收影片並有實際解碼幀數、音訊接收位元組持續增加；選台與冷卻各自獨立；滑鼠點擊及手機尺寸觸控影片均正確路由；同台到 100 過熱、兩端顯示停用遮罩、約 10 秒後恢復；關閉一端後，另一端觀看人數變 1，影音接收位元組與影片解碼幀數仍持續增加。無介面的 Chromium 使用測試用自動播放旗標，這不證明 Safari 能自動播放或裝置喇叭有聲音。

2026-10-23 真正的雙畫面整合測試已通過：每位自動收到兩個不同 MID 的影像，兩格均含實際場景像素；主播放器只有一條音軌，第二 video 沒有音軌，各台計數為兩人。輕點第二格交換該玩家兩格、共用冷卻且不影響另一位；第三格請求與冷卻期間操作均被拒絕，換鏡頭不觸發重新協商。兩格各自顯示過熱與恢復；斷開一端後，另一端的兩條影像與音訊接收位元組及解碼幀數持續增加。實際 Unity 畫面截圖為 `Library/CCTVReview/live-dual-0.png`（桌面並排）與 `live-dual-1.png`（手機尺寸上下排列）。

若收到的狀態只有 selectedIndex，沒有 selectedIndices／videoMids，代表仍在跑舊組件。停止 Play，Assets → Refresh（Ctrl+R），等編譯完成再 Play；重新整理接收頁以載入新版腳本。本次曾遇到此情形，確認 DLL 更新後才進行上述雙畫面驗證。

在 Play 中重新編譯腳本可能只還原套件的 running 旗標，沒有還原實際連線，且 CCTV 規則 state 為空；此時 Play 按鈕仍藍色，但接收頁停在連線中。2026-10-23 檢查到這種狀態後，完整停止再進入 Play，確認 state 已初始化，才完成上述測試。臨時 Editor 檢查腳本與 meta 已移除；驗收後 Unity 留在停止 Play 的狀態。

Unity 日誌另有遠端 Gamepad 的 Dpad layout 建立失敗訊息；本次滑鼠與觸控測試通過，手把輸入尚未驗收。此批沒有修改套件或 XR 設定來處理該訊息。

## 已知狀態與後續工作

| 項目 | 狀態 |
|---|---|
| 網頁影音與原有單人切台 | 使用者已確認先前版本可用 |
| 需刷新數次才能連上 | 使用者先前確認關閉 Automatic Streaming 後解決 |
| 兩個瀏覽器切台互相影響 | 已改為獨立 Session；兩個 Chromium 接收頁連到 Unity 實測通過，待 Safari 確認 |
| 監視器狀態、共享過熱、個別冷卻 | Unity／WebRTC 與雙 Chromium 整合測試通過，待 Safari 確認 |
| 每人同時看兩台 | 已完成；規則、網頁、路由與 Unity 雙影像整合測試通過，待 Safari 確認 |
| Loopback 本機沒聲音 | 仍延後處理；不要加回已刪除的 AudioStreamLoopbackSync |
| 額外「點擊開啟聲音」按鈕 | 使用者要求移除，勿自行加回 |
| DataStreamingTest | 僅供記錄操作名稱，不是選台功能的依賴 |
| 連線提示與重連 | 已有基本連線狀態；完整錯誤處理與自動重連延後 |
| 瞄準與攻擊 | 尚未實作；後續需依 connectionId 取得對應視角，再處理影片內座標與射線 |

## 核對依據與呼叫路徑

| 依據（Evidence） | 確認內容（Finding） | 修改位置（Path） |
|---|---|---|
| CCTVViewSwitcher.OnOffer、OnAddChannel | 每條連線建立兩組 Sender／Camera 與一個 Receiver，通道按連線與 Label 路由 | 調整連線輸出與輸入管理 |
| SwitchNextFor、SwitchFor、ApplySessionView | 操作只套用到發出請求的 Session，保留個別 RenderTexture | 調整選台與視角 |
| CCTVMonitorState.Viewers、TrySwitch、Tick | 選台／冷卻按連線保存，熱量按實體監視器聚合 | 調整規則或玩家去重 |
| SendStatus 與 cctv-panel.js | 每位收到自己的選台／冷卻與共用過熱資料 | 調整網頁 UI 或協定 |
| SendStatus.videoMids、CCTVVideo.render、stream-smoke.mjs | 真實 WebRTC 兩格依 MID 分配，有場景像素，音訊只播放一份，開關不用重連 | 調整影像接收或雙格版面 |
