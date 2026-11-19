# 手機商店：怎麼接、怎麼改價格

玩家按購買後，買成功才扣錢、增加一份道具；錢不夠就兩邊都不變。

## 要拖什麼

先停止 Play，確認場景裡已經有 [MoneyManager](MoneyManager.md)。

1. 把 `Assets/_Project/Prefabs/Economy/ShopManager.prefab` 拖到 **ServerOnly** 底下。已經有就不用再放。
2. 點 Hierarchy 裡的 **ShopManager**，把同場景的 **MoneyManager** 拖進 **Money Manager** 欄位。
3. 找到 **CCTV View Switcher** 元件，把剛放好的 **ShopManager** 拖進它的 **Shop Manager** 欄位。
4. 確認 CCTV View Switcher 的 **Money Manager** 也填了同一個錢包。
5. 按 **Ctrl＋S 儲存場景**。不要再掛第二份 ShopManager，也不要把場景引用 Apply 回共用 CCTV Prefab。

這樣可以購買和看庫存。要讓買到的道具能發射，還要接 [ItemUseManager](ItemUse.md)。整套接法看 [手機模組交接](MobileModule-handover.md)。

## 價格在哪裡改

在 Hierarchy 點 **ShopManager**，展開 Inspector 的 **Products**，修改各商品的 **Price**。

價格必須大於 0。**先停止 Play，改好存場景，再啟動新的主機。**

| 商品 | ID（不要改） | 新拖進來的 Prefab 原價 | Haohao 測試場景價格 |
| --- | --- | ---: | ---: |
| 寶貝球 | capture-ball | 100 | 10,000 |
| 弓箭 | bow | 100 | 10,000 |
| 狙擊槍 | sniper | 599 | 59,900 |
| 炸彈 | bomb | 599 | 59,900 |

測試場景的價格有乘 100，沒有改來源 Prefab。如果要改回原價，只改各項 **Price**；不要按 Revert All，否則可能連剛接好的 MoneyManager 也一起清掉。

## 怎麼測試購買

1. 照 [啟動步驟](MobileModule-handover.md#3-怎麼測試) 開測試主機與手機頁面，按 **Start Streaming**。
2. 往下捲到商店，等商品資料出現。尚未連上時，按鈕會是 COMING SOON、數量是 `—`，這時還不能買。
3. 等錢夠，買一件弓箭。用 Haohao 測試場景時應扣 10,000，弓箭增加一份；測試時別忘了餘額也正在每秒增加。
4. 再買一件，確認再扣一次商品價格、再增加一份。
5. 錢不夠時，購買應失敗，不扣錢也不增加道具。
6. 另開 <http://127.0.0.1/receiver/index.html> 並連線。第二頁應從零庫存開始，不會拿到第一頁的道具。
7. 關閉第一頁再重連，它的庫存應清空，第二頁不受影響。

物品欄選取或取消都不扣錢、不消耗道具。實際發射時才會少一份；射空也不退還。

瘋狂模式與本局結束後不能購買，也不能使用一般道具；已買的庫存會保留。瘋狂模式的槍不在商品清單裡，不用新增第五個商品。

如果一直停在 COMING SOON，先檢查網頁有沒有連上 Unity，再檢查 CCTV View Switcher 的 Shop Manager 和 Money Manager 是否都接好。

[手機商店](ShopManager-mobile.webp)／[桌機商店](ShopManager-desktop.webp)／[舊版接收頁商店](ShopManager-legacy-mobile.webp)。這些是先前測試的截圖。

## 庫存會不會保留

每個手機連線各有自己的庫存，數量存在 Unity 主機，不是網頁自己記。

- 同一連線重複送準備訊息，不會清空庫存。
- 斷線或換連線後會清除，重新連線從頭開始。
- 停止主機、離開場景也會清除。
- 目前沒有帳號或存檔功能。

## 要從程式購買、消耗時再看這段

ShopManager 負責購買和庫存，MoneyManager 負責金錢，不要再建立第二套資料。

- `Purchase`：購買判斷，結果是 `ShopState.PurchaseResult`。
- `TryGetQuantity`：查詢數量。
- `TryConsumeItem`：庫存足夠才減少數量，不會再扣錢。
- `RegisterPlayer`、`RemovePlayer`、`Clear`：管理連線資料。
- `TryGetLastRequestId`：取得最近一次購買要求的編號。

**手機使用道具要經過 ItemUseManager.Use，不能直接呼叫 TryConsumeItem 就當成發射成功。**

網頁只送商品 ID 和要求編號，每次買一件；價格、金額、數量和玩家身分都由主機決定。相同要求重送不會再扣錢或多發道具，舊編號也不會重新購買。失敗的購買要求仍會用掉該編號，下一次要用新編號，現有網頁已處理。

先前的一般模式測試紀錄見 [道具文件](ItemUse.md#前一次的測試紀錄)；瘋狂模式的手動操作見 [交接文件](MobileModule-handover.md#4-瘋狂模式怎麼接怎麼測)。
