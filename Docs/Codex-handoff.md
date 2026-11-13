# Codex 接手：LiveStock

這是從 omp 帶過來的進度交接，不是完整逐字稿，也沒有匯入或改寫 Codex 的原生會話資料庫。請先讀本檔，再依使用者的新指示做事；不要自動重做已完成的功能。

## 目前停在哪裡

- Unity 專案：`C:\Users\User\Unity_Project\LiveStock`。
- 手機金錢、商店、物品欄及參數化球形道具使用，已完成一輪實作與驗收；細節在下方既有交接文件。
- 最近的工作是整理團隊看的 Excel 工作清單，並更新已確認的逃生／勝負規則，已存回原檔。
- 使用者現在要換回 Codex 接續。這次交接不新增遊戲功能、不更動 Excel，也不執行 Git stage／commit／push。
- 此次只整理文件；沒有重跑遊戲測試、移轉 MCP 設定或匯入原生聊天歷史。

## 跟使用者合作的方式

- 使用繁體中文，短而具體。計畫、工作表與交接都要給人看，不要堆內部術語、冗長規劃或過度設計。
- 使用者目前對工作表的要求是「列出有哪些工作」，不是安排人員、工時、優先級或四週排程。
- 能從專案或工具確認的事先查；真正未定的遊戲規則不要猜。
- 不要擅自改名 `CCTVViewSwitcher`。使用者認為它之後可能成為手機模組接口，已說現在先不改名。
- 使用者在意功能完成後有沒有 commit。前一輪助手明確記錄沒有 stage／commit／push；不要聲稱已有提交。後續提交依使用者授權處理，不能順手提交整個工作目錄。
- 不提交或推送 `node_modules`、coverage 或 Unity 產生檔。既有 `WebApp/node_modules/` 原本已被追蹤，ignore 不會自動解除；不要擅自刪資料或改索引。
- 保留使用者原有改動。不強制關閉 Excel／Unity、不丟棄未儲存內容。

## 手機模組：先讀現有文件

- [手機模組交接](MobileModule-handover.md)：整體責任、場景接線、啟動與移植。
- [金錢系統](MoneyManager.md)：錢包與加扣款 API。
- [商店](ShopManager.md)：商品、價格與庫存。
- [道具使用](ItemUse.md)：Prefab、可調參數、物理注意事項、驗收結果及限制。

### 已做好的內容與邊界

- 每個手機連線有獨立錢包及庫存；收入、購買判斷、使用消耗由 Unity 主機決定。
- 已有四項商品購買、物品欄選取，以及「選取 → 點影片發射 → 消耗一份 → 命中 NPC 傷害／回報」流程。
- 庫存只有 ShopManager 那一份；手機端不另外維護權威庫存。
- 資料以 connectionId 區分，只在主機記憶體。重複 ready 不重置；斷線清除，重連從零開始。沒有帳號存檔或瀏覽器本地庫存。
- 四項道具目前都是參數化球形投射物，使用獨立 Prefab、共用腳本。**寶貝球捕捉、射線狙擊、炸彈範圍爆炸尚未完成**，不要把基礎流程誤稱為正式效果全做完。
- NPC 沿用既有血量、傷害、死亡及同物件重生流程。`NetworkNpcAuthorityGate.TryApplyDamage` 回 false 可能是非致命傷，不能直接解讀為沒有扣血。
- 球體是主機端物理，手機透過串流觀看；沒有另做 Fusion 投射物同步。

### 場景與接線

已接好的測試場景：

`Assets/_Project/Scenes/Haohao_test_scene/Haohao_MainGame.unity`

- `ServerOnly` 下各放一份 MoneyManager、ShopManager、ItemUseManager。
- ShopManager 引用同場景 MoneyManager；ItemUseManager 引用同場景 ShopManager。
- `ServerOnly → CCTV Points` 的 CCTV View Switcher 元件引用上述管理器，**不是再新增一份管理器元件**。Hierarchy 物件名稱可能是 `CCTV Viewer ...`，不等於腳本名稱。
- Economy Prefab：`Assets/_Project/Prefabs/Economy/`。
- 道具 Prefab：`Assets/_Project/Prefabs/Items/`，含 CaptureBall、BowProjectile、SniperProjectile、BombProjectile 及 ItemUseManager。
- 移植需包含程式、assembly、Prefab、場景與 `.meta`，不是只拖走 Hierarchy 的物件。
- 不把場景引用 Apply 回共用 CCTV Prefab；原 MainGame 尚未回填新管理器，保留既有行為。

### 目前測試數值

- 測試場景的商品價格有 ×100 override：10,000／10,000／59,900／59,900。來源 Prefab 仍是原價。
- 每秒收入仍是 10,000，屬目前測試設定，不代表正式經濟平衡已定案。
- 道具速度、重力、向上速度、傷害、直徑、壽命可在各道具 Prefab 的 Item Projectile 元件調整。
- 停止 Play 後修改、儲存，再重啟 Server。還原測試價格時只還原 Price，不能 Revert All 丟掉場景引用。

### 執行與驗證紀錄

- 由 Menu 進 Play，在執行中的 SessionLauncher 設定上述測試場景完整路徑，再按 Start Local Dedicated Server。
- 不使用既有 Start Farm 作本次驗收：先前確認它指向未註冊舊路徑，本輪沒有順手修改。
- WebApp signaling server 能沿用就沿用；否則在 `WebApp` 執行 `npm start`。
- 手機頁面：`/receiver/live.html`、`/receiver/index.html`。實體手機需使用電腦區網 IP。
- 快測：連線看餘額 → 購買扣款及庫存 → 選取並點影片使用 → 消耗一份、收到命中回報 → 第二頁確認隔離 → 斷線重連確認清除。
- **上次驗收紀錄**：Unity Economy 98 案例、原生物理 1 案例、前端 5 suites／82 tests 通過；另有實際 Unity 主機及兩個 375px Chromium 接收頁驗收。細節及指令見 ItemUse.md。
- 這些是先前結果，不是本次交接重新執行。Safari／實體手機尚未驗收；Meta XR／Gamepad remoting 既有錯誤不在這個模組的修正範圍。
- 上次驗收結束時已停止 Play、回到測試場景。接手時查當下執行狀態，不假設舊瀏覽器、MCP 連線或 signaling 服務仍存在。
- omp 曾允許的 MCP 不等於 Codex 已完成相同設定；需要操作 Unity 時先確認 Codex 可用的 MCP，不直接重設或複製憑證。

## Excel：已存回原檔

原檔：

`C:\Users\User\Unity_Project\for-material\live-stock\LiveStock_四週任務計畫.xlsx`

- 檔名與工作表名仍保留「四週任務計畫」，表內標題已改為「LiveStock 工作清單」。
- 已整理成 35 項工作。保留原欄位、配色與狀態選單；已完成、白盒、整合中等既有狀態保留。
- Priority、Estimated Effort、Owner、W1–W4 暫時留白；新增工作未經確認的狀態也留白。
- 表格 `LivestockTasks` 範圍為 A4:I39，凍結在 A5。右側 K 欄是一般說明及已確認規則。
- 不自動把舊企畫的天氣、投票、更多動物等提案列入本輪工作。
- 同目錄保留 `LiveStock_四週任務計畫_修改前備份_20261112_152322.xlsx` 與 `LiveStock_四週任務計畫_逃生規則修改前備份_20261112_155242.xlsx`。
- 上次寫入曾被 Excel 開啟鎖住；使用者儲存並關閉後才成功保存。再次修改時保留格式，不強制終止 Excel。

### 已確認的逃生與勝負規則

1. 收集滿 5 顆果實：進入瘋狂模式，開啟逃生門。
2. 任一名 VR 玩家成功逃出：VR 隊伍獲勝。
3. 所有 VR 玩家被淘汰：手機隊伍獲勝。
4. 兩個條件都未成立：繼續逃生，不立即判輸。

原 Excel 已同步果實收集、瘋狂模式、逃生門、勝負判定、VR 任務進度及出局等待／逃生提示，共六項工作名稱，並在右側加上上述規則。**這是工作清單與規則更新，不代表這些玩法已在程式中全部實作。**

沒有在本次確認的細節，例如果實是否全隊共享、時間耗盡如何判定，不要自行補成既定規則。

## 企畫與流程圖在哪裡

- 功能樹：`C:\Users\User\Unity_Project\for-material\live-stock\livestock-功能拆解樹.png`。
- 核心玩法投影片圖片：`C:\Users\User\Unity_Project\for-material\live-stock\核心玩法\`。
- 使用者另補的 Figma 匯出圖在 `C:\Users\User\Downloads\live-stock\`：
  - `livestock-flowchart.png`：企畫、討論與提案。
  - `livestock-flowchart1.png`：整體流程。
  - `livestock-flowchart4.png`：手機流程。
  - `livestock-flowchart444.png`：VR 流程。
  - `livestock-功能拆解樹.png`：功能樹。
- 沒有找到獨立的完整 PDF／PPT／Word 企畫書，不要說已讀過不存在的完整文件。
- 已討論的衝突是 VR 圖最右菱形「是否超過 n 人被抓到」，是 → VR 贏、否 → VR 輸；總流程卻是淘汰達門檻 → 手機贏。
- 使用者接受上面的逃生／全員淘汰規則，已改 Excel；**原 PNG 尚未修改**。不能把原圖舊箭頭當成仍有效的新規則。

## 下一步怎麼接

目前沒有獲准的新遊戲實作工作。先等使用者指定要改程式、工作清單還是流程圖，不要自行展開新功能。

若要動程式，先看手機模組既有交接並確認目前專案狀態；若要動 Excel，直接對原檔修改、保留格式、先備份。Unity 場景／Prefab 與 Excel 都可能已被使用者繼續編輯，不能拿先前副本覆蓋當下版本。
