# 網路 NPC 隨機生成器

完成項目 1：RandomNPCSpawner（獨立腳本與可重用 Prefab）。

## 使用方式

1. 把 `Assets/_Project/Prefabs/Multiplayer/RandomNPCSpawner.prefab` 拖到場景。
2. 移動生成器，讓它位於想生成 NPC 的區域；選取後 Scene 視窗會顯示青色範圍。
3. 在 Groups 新增元素，把已完成的「網路 NPC Prefab」拖進 Prefab 欄位，例如羊、馬、狗、雞；每種設定 Min Count 與 Max Count。
4. 用 Area Size 設定 X/Z 範圍，Minimum Spacing 設定 NPC 中心的最低水平距離。
5. 開啟 Snap To Ground，Ground Layers 只勾選地面、Terrain 所在 Layer。地面必須有 Collider。Height Offset 用於修正 NPC 根節點和腳底的高度差。
6. 由現有 SessionLauncher 啟動伺服器並加入 PC；只有伺服器生成，其他玩家接收相同 NPC。
7. 在別的場景使用時，該場景也必須由 Fusion 載入，並使用現有 SessionLauncher（或在 OnSceneLoadDone 呼叫 RandomNPCSpawner.SpawnFor）。離開場景前呼叫 DespawnFor。

預設 Prefab 已填入互動羊，每次生成 3～5 隻。生成器本身是場景設定，不需要 NetworkObject。

## 參數

| 欄位 | 說明 |
|---|---|
| Groups | 每種 NPC 的網路 Prefab 與隨機數量（含上下界） |
| Area Size | 公尺為單位的矩形範圍，跟隨生成器 Y 軸旋轉，忽略縮放與傾斜 |
| Minimum Spacing | 本生成器 NPC 中心間距；跨生成器、牆壁重疊不包含在內 |
| Attempts Per NPC | 每隻尋找有效位置的次數；範圍太小就少生成並顯示警告 |
| Seed | 0 每次隨機，其他整數重現候選排列；地形、數量和參數也須相同 |
| Ground Ray Height | 從生成器高度上方開始向下找地面，射線長度是此數值兩倍 |
| Ground Layers | 只放地面，避免射線打到其他動物或屋頂 |
| Height Offset | 地面高度之外的根節點偏移 |

每種數量上限 200，尋找次數上限 100，避免誤填極大數字讓 Editor 卡住。實際請先使用少量 NPC。

## 程式學習

- `SpawnFor`：在場景就緒後，找出該 Runner 場景內啟用的生成器；Server 才執行。
- `Spawn`：每種抽取數量，再替每隻抽位置和 Y 軸角度，透過 `runner.Spawn` 建立網路物件。
- `TryPosition`：射線找地面，確認與已生成 NPC 的中心間距；超過嘗試次數就跳過。
- `DespawnFor`：離開場景前清除這個生成器建立的物件。
- `OnDrawGizmosSelected`：只畫編輯器提示，不參與網路同步。

隨機只在 Server 算一次，Client 不需要相同 Seed；位置與物件由 Fusion 同步。

## NPC Prefab 前置條件

NPC 根節點需有 NetworkObject，移動需要 NetworkTransform，並註冊到 Fusion Prefab Table。可使用既有 GDC_METAVERSE > Multiplayer > Make Selected Networked 工具建立。
AI、移動、傷害等遊戲規則也要由 Server 執行；生成器不會自動把原本的單機 AI 改成網路 AI。
隨機生成沿用 Prefab 原始尺寸。互動羊會保留 NetworkSheepSelectable 與 Collider，可繼續點擊變色。

## 測試

已確認：Unity C# 編譯通過、Prefab 已保存且網路羊參照有效；固定種子產生 20 個位置，全數符合 20×20 範圍與 1 公尺最小間距。

2026-10-23 執行驗證：同一個 Editor 內啟動 Dedicated Server 與兩位 PC（Multi-Peer）。在 Server 的臨時平面測試區生成三隻羊，根節點高度正確落在地面頂面 Y=50.5；兩位 PC（含晚加入）都收到相同三隻 NPC。NetworkId 一致，位置符合網路壓縮精度。再改成四隻並重新生成，Server 與兩位 PC 皆為四隻；清除後三邊總計為零。測試區僅存在 Play Mode，沒有存入使用者場景。這項測試不代表所有 Terrain 坡度、任意模型的腳底高度都已驗證。

開一個 Server 和兩個 PC，確認三邊 NPC 數量與位置一致。第一位點羊後第二位應看到變色；晚加入也應收到同一批 NPC。
若生成 0 隻，先檢查 Groups、地面 Collider、Ground Layers、射線高度，再把 Minimum Spacing 調小。
本功能不包含重生計時器、NavMesh、完整物理避障或行為 AI。

## 功能 2：執行中清除與重新生成（已存檔）

用途：在 Play Mode 改數量、範圍或 Seed 後，直接重新生成，不必重新加入房間。

1. 在 Hierarchy 找到 **Server 那份場景** 裡的 RandomNPCSpawner；Multi-Peer 會有多份場景，Client 的生成器不具伺服器權限。
2. 修改 Groups、Area Size 等欄位。
3. 從 RandomNPCSpawner 元件右上角選單（或右鍵元件標題）選 `NPC > Respawn Generated (Server)`。
4. 要清除則選 `NPC > Clear Generated (Server)`；清除後仍可執行重新生成。

`ClearGenerated()` 透過 Fusion `Despawn` 刪除本生成器建立的 NPC，同步至所有 Client；其他生成器的 NPC 不受影響。
`RespawnGenerated()` 先清除，再依目前設定呼叫原本的 Spawn。這是重新建立物件，舊 NPC 的選取、生命值等狀態也會重設。
`AliveCount` 是唯讀 C# 屬性，可供之後的工具讀取仍有效的 NPC 數量；不是同步欄位，也不會自動顯示在預設 Inspector。

這兩個操作僅在 Server 執行；編輯模式或 Client 呼叫會直接返回。它們沒有對一般玩家開放 RPC 權限。
Play Mode 修改參數用於測試，停止後 Unity 會還原；正式設定請退出 Play Mode 後修改場景或 Prefab 並儲存。

## 保存與接續

本次功能檔案為 RandomNPCSpawner.cs、RandomNPCSpawnerSetup.cs、RandomNPCSpawner.prefab 與本教學；SessionLauncher 僅新增場景生成與清除的兩個呼叫。
未自動加入你目前的 test Network 場景，請依上面步驟拖入 Prefab 後儲存場景。
重新匯入時工具只建立缺少的 Prefab，已有設定不會被重設；功能 1 可獨立使用，不依賴後續功能。

官方參考：https://doc.photonengine.com/fusion/v2/manual/spawning
