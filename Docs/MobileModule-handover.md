# 手機商店與道具：隊友怎麼接

這份給同一個 Unity 專案的隊友用。先拿到程式、Prefab 和網頁修改，再照下面操作；不是只傳三個 Prefab 就能放進別的專案。

**Haohao_MainGame 測試場景已經接好，直接跳到「怎麼測試」。要接到另一個場景，才需要重新拖物件。**

## 1. 要拖哪三個物件

先停止 Play。在 Project 找到下面三個 Prefab，全部拖到目標場景的 **ServerOnly** 底下：

```text
Assets/_Project/Prefabs/Economy/MoneyManager.prefab
Assets/_Project/Prefabs/Economy/ShopManager.prefab
Assets/_Project/Prefabs/Items/ItemUseManager.prefab
```

已經有的就沿用，不要再放第二份。四種道具已經在 ItemUseManager 裡配好，不用再把四顆球拖到場上。

## 2. 把五個欄位填好

下面要拖的都是 **Hierarchy 裡剛放好的物件**，不是 Project 裡的 Prefab。

| 點選哪個物件／元件 | Inspector 欄位 | 從 Hierarchy 拖什麼進去 |
| --- | --- | --- |
| ShopManager | Money Manager | MoneyManager |
| ItemUseManager | Shop Manager | ShopManager |
| CCTV View Switcher 元件 | Money Manager | MoneyManager |
| CCTV View Switcher 元件 | Shop Manager | ShopManager |
| CCTV View Switcher 元件 | Item Use Manager | ItemUseManager |

**CCTV View Switcher 是 Inspector 裡的元件名稱。** Hierarchy 物件可能叫 `CCTV Viewer ...`，在原本的 `ServerOnly → CCTV Points` 底下找。不是再新增一個 CCTV 或再掛一份管理器。

填好後按 **Ctrl＋S 儲存場景**。不用按 Apply All，也不要把這些場景物件存回共用 CCTV Prefab。

## 3. 怎麼測試

### 先開網頁服務

如果服務已經在跑，就不用重開。否則在專案根目錄開終端機，執行：

```powershell
cd WebApp
npm start
```

### 再開 Unity 主機

1. 開 **Menu** 場景，按 Play。
2. 在 Hierarchy 找到執行中的 **SessionLauncher**。
3. 啟動主機前，把 Inspector 的 **Network Scene Path** 填成：

   ```text
   Assets/_Project/Scenes/Haohao_test_scene/Haohao_MainGame.unity
   ```

4. 按 **Start Local Dedicated Server**。不要按 Start Farm，它目前仍指向舊場景路徑。

以上是已接好的測試場景入口。測自己接好的其他場景時，改填那個場景的路徑，並確認它已加入 Build Settings。這個欄位只在 Play 中改，不要存回 Menu。

### 最後開手機頁面

電腦瀏覽器開 <http://127.0.0.1/receiver/live.html>，按 **Start Streaming**。

實體手機要跟電腦在同一個網路，把網址中的 `127.0.0.1` 換成電腦的區網 IP；電腦的防火牆也要允許連線。

照這個順序試：

1. 等餘額增加。目前測試設定是每秒加 10,000。
2. 買一件道具。餘額應扣掉商品價格，持有數量加一。
3. 在物品欄選取道具，再點監視器影片中的 NPC。應射出球體、數量減一，命中後顯示結果。
4. 射空也會消耗一份；最後一份用完會自動取消選取。沒有庫存時不能使用。
5. 另開 <http://127.0.0.1/receiver/index.html> 並連線。第二頁應有自己的餘額和庫存，不會拿到第一頁的道具。
6. 關閉第一頁再重連。它應重新開始，第二頁不受影響。現在沒有存檔，所以斷線後不保留庫存。

## 目前做了什麼、還沒做什麼

現在可以買道具、放進物品欄、選取後發射，並對 NPC 造成傷害。

**四種道具目前都用球體測試。寶貝球還不能捕捉、狙擊槍還不是射線、炸彈還沒有範圍爆炸。這個模組也還沒接 VR 玩家受擊與淘汰。**

原 MainGame 沒有一起改成這套接法；請先用上面的測試場景。實體手機和 Safari 尚未完成驗收，先前測的是電腦 Chromium。

## 想調整什麼，就看哪一份

- [改初始金額、每秒收入](MoneyManager.md)
- [改商品價格、測試購買](ShopManager.md)
- [改道具速度、傷害、大小；查看攻擊程式在哪裡](ItemUse.md)
- [前一次的測試紀錄](ItemUse.md#前一次的測試紀錄)

Haohao 測試場景的商品價格目前有乘 100；從 Project 新拖的 ShopManager 仍是原價。這是測試設定，不是正式平衡數值。

## 交給別人時要帶什麼

- Unity 程式、Prefab、場景及對應的 `.meta`，包含 Economy 的 `.asmdef`。
- 網頁端修改，以及 `package.json`、lockfile。不要只傳 Unity 物件，否則對方可能沒有物品欄和使用介面。
- 這份說明及上面的設定文件。

**不要傳或提交 `node_modules`、coverage。** 對方用套件清單安裝依賴。原本的 `WebApp/node_modules` 曾被 Git 追蹤，加入 ignore 不會自動解除追蹤；不要因此整包加入提交。

前一輪實作及這次文件改寫都沒有執行 stage、commit 或 push。若要用 Git 交付，仍需整理並提交實際修改，不能只請隊友 pull 就當作已交付。
