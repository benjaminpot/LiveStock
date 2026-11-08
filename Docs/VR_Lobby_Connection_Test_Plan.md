# VR Menu → Lobby → MainGame 測試計畫

2026-11-09 修正 VR 入口：回退前次將 VR 轉換為桌面 Menu 的行為，工具直接載入原本 Menu_VRClient 場景。原場景 VR／Desktop 兩種啟動順序及完整本機 Rig 混合驗收共 40 項通過；實體頭盔尚待裝置測試。最新操作與證據見 [VR 混合連線說明](VR_Mixed_Connection_Guide.md)。

2026-11-08 工具改版已改為獨立開啟未連線 Client、獨立啟停 Server、F2 切換視角，並加入可同步的 Desktop 企鵝 Prefab。新操作以 [測試工具說明](VR_Lobby_Test_Guide.md) 為準。下列階段紀錄保留改版前的交付歷史。

日期：2026-11-08。狀態：本次範圍的實作與桌面驗收已完成。Multi-Peer 線上驗收 15 項、載入期間離線等 4 項邊界檢查、PC 操作 12 項及獨立 Server＋兩個 PC Client 的 10 項跨程序驗收皆通過。Windows 建置與 Editor PC 畫面檢查完成。實體頭盔仍待裝置驗收。完整結果見 [測試結果](VR_Lobby_Test_Results.md)。

## 目標與分工

Server 先建立 Fusion 房間並進入 Lobby；VR 從現有 LogInMenu 加入；Server 顯示玩家人數，操作者手動按 Start Game，所有已連線玩家留在同一房間並進入 MainGame。

- Codex：建立必要場景物件、Prefab、腳本、Inspector 引用及按鈕事件，整理 Build 場景設定與操作說明。程式使用英文 XML 文件、註解和 Inspector 提示。
- 使用者原先保留手動測試；後續已明確授權 Codex 接續完成檢查、Play Mode、連線測試與 Build。Codex 依此執行並保存報告。
- 實體頭盔畫面與控制器操作，無法以桌面測試代替，仍由持有裝置的操作者驗收。
- 不修改或重新依賴 SessionLauncher.cs、SessionPanel.cs，不改動 GDC_METAVERSE 插件原始碼。
- 按階段交付，每階段提供「開哪個場景、按哪個按鈕、預期顯示什麼」；前一階段失敗先修正，不把問題帶到下一階段。

## 已確認的四項決策

1. Server 的 Start Game 放在桌面 Canvas，後續獨立伺服器程式沿用。
2. 本次完成連線、人數、一起換場並保留本機 VR 視角；網路玩家模型與頭手同步延後，不納入本次驗收。
3. 平常由 Editor 一次啟動 Server＋一個 VR Client 的選單；Server 自動先開房，VR 仍由使用者按 Connect。再加入第二位無 VR 測試 Client，最後驗證獨立程序。
4. 共用 Lobby／MainGame，只分開 Server 與 VR 的啟動場景。使用 NetworkSceneManagerDefault，不建立角色場景映射系統。

無其他架構問題需要先確認。既有場景內容的整理以最小改動、可恢復為原則；若實作中發現會影響既有功能的新問題，再提出具體問題。

## 定案場景配置

所有新流程場景位於 `Assets/_Project/Scenes/_flow test/`。

| 場景 | 內容與用途 |
|---|---|
| ServerBootstrap.unity（新增） | ServerConnectionManager、持續保留的桌面管理 Canvas；無 VR Rig |
| Menu_VRClient.unity（沿用） | ConnectionManager、現有 LogInMenu、本機 VR Rig |
| Lobby.unity（建立共用版） | 等待室與玩家狀態；不內建第二套 VR Rig，不自動開房 |
| MainGame.unity（建立共用版） | 共用測試世界與本機出生點；先停用會依賴舊流程的系統 |

Editor 測試入口載入 ServerBootstrap 與 VR Menu，開房時保留尚未加入的選單；加入後 Server／Client 各自擁有同一 Lobby 資產的 Runner 場景副本。Server 桌面 Canvas 在換場期間保留，顯示當前房間、玩家數及載入狀態。

共用場景從現有 Server／VRClient 版本比對後建立；先保留原本四份場景作為回復來源，不直接刪除。第一版的測試 Build 清單使用相同順序及 index，ServerBootstrap 與 Menu_VRClient 以啟動入口區分，不使用重新排序的角色專屬清單。

## 實作前檔案狀態（歷史基準）

`Assets/_Project/Scenes/_flow test/` 已有：

| 場景 | 計畫前的既有內容 |
|---|---|
| Menu_VRClient.unity | 客戶端啟動、現有 VR Rig 與 LogInMenu |
| Lobby_Server.unity | Server 等待室／管理畫面 |
| Lobby_VRClient.unity | VR 等待室 |
| MainGame_Server.unity | Server 遊戲場景 |
| MainGame_VRClient.unity | VR 遊戲場景 |

已知落差：

- ConnectionManager 僅支援 GameMode.Client，缺少獨立 Server 啟動入口。
- 腳本預設路徑仍是不存在的 `_flow test/Lobby.unity`；Menu_VRClient 中序列化欄位仍指向 `_ALPHA_DEMO/MainGame.unity`。只改腳本預設值不會修正已保存的 Inspector 值。
- EditorBuildSettings 仍啟用 `_ALPHA_DEMO` 的 Menu／Lobby／MainGame；需檢查 Build Profile 是否另有覆寫。
- Menu_VRClient 仍保留舊 Session 物件；已確認 SessionLauncher 物件停用，實作時再逐項確認其他舊元件的啟用狀態，不刪除它們。
- 舊 NetworkPlayer.Spawned 直接呼叫 SessionLauncher.CreateLocalRole，不能直接當作新流程的獨立玩家方案。
- 目前尚無新流程的 Server 啟動與共用 Lobby 狀態管理腳本，需獨立建立。
- MainGame 場景包含現有 NPC／ServerOnly 等系統；需要確認它們的啟動與權限依賴，先避免它們干擾連線驗收。

## 架構與行為約定

### 連線與開始

- Server 使用 GameMode.Server；客戶端維持 GameMode.Client，禁止 Client 建立房間。
- 預設測試房名 LIVESTOCK-DEV、region asia；兩端 App ID、App Version、region 和房名一致。
- Server 本身不算一位玩家；UI 的 Connected 人數取自伺服器的玩家集合，不使用本機 Runner 數量代替玩家人數。
- 第一版至少一位玩家已完成 Lobby 載入，Server 才可手動開始。不自動開始、不倒數、不設 Ready 按鈕。
- Server 在開始時關閉房間加入入口；第一版不處理中途加入、Host migration、掉線後自動重連或返回 Lobby 再開下一局。
- UI 顯示區分 Connecting、Connected／Loading Lobby、Lobby、Loading MainGame、In MainGame、Failed／Disconnected。

### 場景管理

- 開始只能由 Server 發起，經 Fusion 的場景管理流程切換，客戶端不單獨呼叫 SceneManager.LoadScene。
- 使用 NetworkSceneManagerDefault 載入共用 Lobby／MainGame，依明確綁定的 Runner 身分控制 Server UI／VR 表現。
- 共通網路狀態使用同一個註冊 Prefab，由 Server 生成並以 Fusion 支援的方式保留至換場完成；不生成舊 NetworkPlayer。
- 進入 MainGame 後須收到各客戶端載入完成回報，Server 才顯示全員到達；Server 自己載完不能視為全員載完。
- 本機 VR Rig 在 Menu、Lobby、MainGame 間明確轉移或持續保留，只存在一套頭盔追蹤與有效 AudioListener。

## 階段 1：場景與設定整理

交付狀態：已完成物件／設定整理與 Edit Mode 結構檢查。下列為第一階段當時的交付紀錄；後續功能和操作以 `Docs/VR_Lobby_Test_Guide.md` 為準。

- 新增 ServerBootstrap，包含空的 ServerConnectionManager 掛載物件、桌面 Canvas、停用的 Start Game、獨立桌面相機與 EventSystem；尚未掛上 Server 開房腳本。
- 共用 Lobby 由 Lobby_VRClient 複製，共用 MainGame 由 MainGame_VRClient 複製；原四份角色場景未修改，已比對 SHA-256。
- 共用場景各有 VRSpawnPoint 初始標記，位置尚待使用者場景／頭盔驗收；場景預覽相機及 AudioListener 停用，避免未來與保留的 VR Rig 重複。
- Menu_VRClient 的 ConnectionManager 已保存正確共用 Lobby 路徑，四個 UI 引用完整，舊 Session 元件停用。
- Build 清單啟用順序：Menu_VRClient、ServerBootstrap、Lobby、MainGame；舊項目保留但停用。未發現自訂 Build Profile；原清單保存於 `Docs/VR_Lobby_Phase1_PreviousBuildScenes.json`。
- GDC Peer Mode 保持 Single，沒有開始多人測試。第二階段的 Editor 測試入口才負責切換及恢復模式。

Codex 工作：

1. 比對既有兩端場景，建立共用 Lobby／MainGame，保留原版本與現有未完成 UI。
2. 新增 ServerBootstrap，放置 Server 啟動物件與獨立桌面管理 UI；Menu_VRClient 保留 VR 入口。
3. 整理 ConnectionManager 的場景參照，修正 Inspector 已保存的舊路徑。
4. 整理測試 Build Scene List／Profile，兩端共用一致的四場景清單及 index，明確區分啟動入口。
5. 確認舊 Session 元件在測試入口不執行；不改它們的腳本。

交付：場景角色表、有效場景清單、已接好的入口物件。

使用者檢查：等 Unity 編譯，確認 Console 無紅色錯誤、可開啟入口場景，現有 VR Menu 可見。

## 階段 2：Server 開 Lobby，VR 從 Menu 加入

交付狀態：已實作；真實 Photon Server／Client 連線、空白房名、重複點擊與不存在房間驗收通過。

Codex 工作：

1. 新增最小 ServerConnectionManager：開房、顯示房名／region／連線狀態、Shutdown 清理；尚未完成開房時不能接受 Start Game 操作。
2. 調整 ConnectionManager：加入 Server 現有房間、跟隨 Lobby 場景、失敗後可重試，斷線清楚顯示原因。
3. 建立持續保留的 Server 桌面 Canvas；VR 保留目前 LogInMenu 欄位與 Connect 事件。
4. 建立 Lobby 最小顯示：Server 看玩家數；VR 顯示已加入與等待 Server 開始。
5. 新增 Multi-Peer Editor 專用測試入口，先啟動 Server Runner 再保留 VR 選單操作；不能由 Server 載入場景意外卸掉未加入的 Menu。
6. 所有跨 Runner 引用以明確 Runner 綁定；不透過全域 FindFirstObjectByType 誤取別人的 UI、狀態或場景。

使用者測試 T1：先 Server、後按 VR Connect；預期 Server 開房成功，VR 到達 Lobby，Server 顯示 1 位玩家。

使用者測試 T2：未開 Server／輸入不存在的房名；預期連線失敗，無誤顯示 Connected，可修正後重試。

使用者測試 T3：房名空白、重複點 Connect；預期有提示，且只產生一個客戶端 Runner。

## 階段 3：人數同步與手動 Start Game

交付狀態：已實作；零人不可開始、延遲玩家阻擋開始、人數加入／離開及桌面按鈕驗收通過。

Codex 工作：

1. 新增最小 LobbySessionState 網路狀態，保存階段與玩家數，權威在 Server。
2. 玩家加入／離開時更新人數與 UI；Server 不列入玩家數。
3. Start Game 條件：Server 已開房、至少一人、所有已連線玩家已完成 Lobby 載入，且沒有正在進行的換場。
4. Client 不顯示可操作的 Start Game；程式本身也檢查 Server 權限，不能只靠隱藏按鈕。
5. 按一次開始便立即進入 LoadingMainGame，停用按鈕並關閉房間，避免連點或換場期間加入的競態。

使用者測試 T4：零人時按鈕不可用；一位已到達 Lobby 時可用；還沒按開始前持續停在 Lobby。

使用者測試 T5：玩家離開後人數下降；Server 不會被計入，也不會自動開始。

## 階段 4：Server 帶所有人進入 MainGame

交付狀態：已實作；三個 Runner（Server＋兩位 Client）同步到 MainGame，網路狀態 ID 保持相同，重複開始不重複生成；晚加入 GameClosed、關服斷線與返回選單驗收通過。

Codex 工作：

1. 實作 Server 發起 Fusion 換場，維持房間和原有連線，不重建 Runner。
2. 切換時顯示載入狀態；各客戶端完成後回報，Server 顯示到達人數。
3. 處理 Menu／Lobby UI 銷毀後的引用重綁定，避免按鈕或 TMP 參照遺失。
4. 保留一套本機 VR Rig；Server 不建立或啟用 XR 相機。Multi-Peer 的 Server 管理 Canvas 不依賴被隱藏的 Server 世界相機。
5. 現有 NPC／Render Streaming／場景生成器按需求啟用；不得在 Lobby 提早生成。第一階段不必為連線驗收加入整套遊戲系統。
6. 若使用 GDC NetworkSpawner，在權威端正確的場景完成／離開回呼接入 SpawnFor／DespawnFor，並確保一次生成；不因換場回呼重複生成。

使用者測試 T6：一位 VR Client 已在 Lobby，Server 按 Start Game；預期雙端的 Runner 都到達共用 MainGame 的各自副本，房名與連線保持，無重複 VR Rig。

使用者測試 T7：快速連點 Start Game；只換場一次，不重複生成狀態或物件。

使用者測試 T8：關閉 Server；VR 顯示斷線且能回到可重新連線的入口。至少提供明確的 Return to Menu 操作，不要求自動重連。

## 階段 5：第二位測試玩家與獨立程序

交付狀態：第二位無 VR 測試玩家已實作並驗證；Windows 建置成功，獨立 Server＋Editor Single Client 已驗證 Lobby、MainGame、權威分工及返回選單。實體頭盔測試仍待使用者執行。

Codex 工作：

1. 提供 Editor 專用 Add Test Client／Leave Test Client，第二位不啟動 VR、不搶相機／AudioListener。
2. 對測試客戶端顯示名稱、Runner、所在階段，便於確認是真的兩位玩家。
3. 提供 Server 獨立程式與 Client 的 Build 設定、啟動順序和輸出路徑；使用 GDC Build Single Peer 時確認實際 Profile／場景清單符合本方案。
4. Server 程式不初始化 XR，使用有視窗的測試伺服器供桌面 Start Game 操作。Editor 同程序中的 VR Client 仍可使用 XR，Server 僅避免建立 XR Rig；不能在共享程序全域關閉 XR 而影響 VR Client。

使用者測試 T9：Server＋VR Client＋無頭盔測試 Client；Server 顯示 2 位，按開始後三個 Runner 到達對應 MainGame。

使用者測試 T10：兩人中一人仍在載入 Lobby 時開始不可用；離開者不應永久阻塞流程。切換期間掉線者應從等待名單移除並更新狀態。

使用者測試 T11：Server Build＋Editor VR Client，重做加入與手動開始，確認不依賴 Editor-only 測試工具。

使用者測試 T12：在 MainGame 後嘗試加入同房；第一版應拒絕加入並提供可理解的失敗提示。

## PC 操作替代測試

使用者於 2026-11-08 授權在離開期間完成自動測試，並先以 PC 操作玩家驗證多人流程。

- 已新增 DesktopTestPlayer，由桌面模式的 ConnectionManager 自動建立並保留。
- WASD 移動、滑鼠右鍵轉向、Q／E 升降、Shift 加速，採用自由飛行視角。
- PC 控制 12 項驗收通過。選單／斷線時停止操作，Lobby 與 MainGame 可操作。
- 同一 Windows 建置的三份程序完成 Server＋兩位 PC Client 驗收，共 10 項通過。已確認不同程序與 PlayerRef、Server 權威、2/2 載入、同步換場、各自輸入、退出與關服清理。
- 共用 MainGame 的兩個預覽動物 NavMeshAgent 已停用。最終獨立測試記錄中的導航錯誤為零。
- PC／Server 限制為 60 FPS，便於同機多程序測試。
- Editor PC 畫面檢查通過，操作提示沒有文字溢出，Return to Menu 按鈕可見。背景隱藏程序的 ScreenCapture 產生黑畫面，視覺證據採用 Editor PC 模式擷圖。

操作與雙 PC 視窗啟動方式請見 [測試操作說明](VR_Lobby_Test_Guide.md)。

## 本次預計交付檔案與物件

以下為最終實作對照；等待室 UI 與 Rig 保存合併到 ConnectionManager，減少額外腳本。

| 項目 | 變更 |
|---|---|
| ConnectionManager.cs | 修改現有腳本，處理加入 Lobby、UI 狀態、退出及重試 |
| ServerConnectionManager.cs | 新增 Server 開房、玩家集合、手動開始、關房及清理 |
| LobbySessionState.cs＋網路 Prefab | 新增 Server 權威的玩家數、階段及載入回報 |
| ConnectionManager 的 SessionCanvas | 等待室狀態顯示；明確綁定所屬 Runner，未另建 LobbyPanel.cs |
| ConnectionManager／FlowSceneMarker | 本機 Rig 跨場景保留與出生點設定，不傳送網路姿態，未另建 LocalVRRigLifecycle.cs |
| FlowLaunchOptions.cs | Server／桌面／VR 啟動選擇與 XR 啟動分流 |
| Editor/LocalConnectionTestTools.cs | 新增一次啟動兩端、增減無 VR 測試玩家、結束與恢復測試設定 |
| ServerBootstrap／Lobby／MainGame | 新增場景與必要 UI、狀態 Prefab、出生點 |
| Menu_VRClient | 更新引用及事件，保留現有 VR 互動設定 |
| 本計畫及操作說明 | 逐階段更新交付與使用者實測狀態 |

## 明確延後的項目

- 網路玩家模型生成與頭手姿態同步：本次只保留本機自然追蹤，不宣稱其他玩家看得到 VR 動作。
- 不改寫舊 NetworkPlayer 或舊 Session 腳本；本流程不生成依賴它們的玩家物件。
- 不新增 Voice、虛擬鍵盤、Ready 按鈕、自動開始、重連或完整遊戲規則。
- NPC／CCTV 等既有功能是否啟用，待連線換場完成後再獨立安排，不作為本次通過條件。

## 每次手動測試的回報格式

```text
測試編號：T1
模式：同一 Editor Multiple／Server Build + Editor Client
起始場景：
操作步驟：
Server 狀態與人數：
VR 狀態與所在場景：
實際結果：
Console 第一個紅色錯誤（含堆疊）：
```

## 完成標準

- 不依賴舊兩支 Session 腳本即可開房、從現有 LogInMenu 加入 Lobby。
- Server 人數與實際連線一致，只有 Server 可手動開始。
- 已連線玩家在同一房間從 Lobby 到 MainGame；確認載入完成與掉線清理。
- 一台電腦可完成一位 VR 與第二位無 VR 測試客戶端的流程。
- 各項實測結果由使用者確認，未執行的 Build／HMD／跨程序項目明確標記未驗證。

## 技術參考

- 本專案 `Assets/GDC_METAVERSE/README.md`：Peer Mode、Single Peer Build 與 NetworkSpawner 接入。
- Photon Multi-Peer：https://doc.photonengine.com/fusion/v2/manual/testing-and-tooling/multipeer
- Photon Scene Loading：https://doc.photonengine.com/fusion/v2/manual/scene-loading
