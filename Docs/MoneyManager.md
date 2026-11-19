# 手機金錢：怎麼接、怎麼調

每支手機有自己的餘額。現在初始是 0，連線準備好後每秒加 10,000；斷線重連會重新開始，不會保留上次的錢。

進入瘋狂模式或結束本局後，所有手機的收入都會停止，原有餘額保留。瘋狂模式每次成功發射另扣 100 元。

## 要拖什麼

先停止 Play。

1. 把 `Assets/_Project/Prefabs/Economy/MoneyManager.prefab` 拖到場景的 **ServerOnly** 底下。已經有就不用再放。
2. 找到 Inspector 裡有 **CCTV View Switcher** 元件的物件。
3. 把 Hierarchy 裡的 **MoneyManager** 拖進它的 **Money Manager** 欄位。
4. 按 **Ctrl＋S 儲存場景**。不要把場景引用 Apply 回共用 CCTV Prefab。

這樣只有金錢功能。要讓玩家買東西，還要接 [ShopManager](ShopManager.md)；要整套一起接，照 [手機模組交接](MobileModule-handover.md) 做。

## 初始金額和收入在哪裡改

在 Hierarchy 點 **MoneyManager**，看 Inspector：

| 欄位 | 用途 | 目前設定 |
| --- | --- | ---: |
| Initial Balance | 手機剛連進來有多少錢 | 0 |
| Income Per Second | 每秒增加多少錢 | 10,000 |

兩個欄位都不能是負數。

**先停止 Play，改好並存場景，再啟動新的主機。** 不要在 Play 中改完就期待目前這一局立刻換數值。

## 怎麼確認有接好

先照 [啟動步驟](MobileModule-handover.md#3-怎麼測試) 開啟測試主機和網頁。

1. 手機連線後，應看得到餘額。
2. 目前設定應從 0 開始，每秒增加 10,000。
3. 另開一個接收頁並連線。它應從自己的初始金額開始，不影響第一頁。
4. 關閉第一頁再重連，它應重新開始；第二頁繼續累積。
5. 停止 Unity 主機後，網頁不應繼續自己加錢。

如果完全看不到餘額，先檢查：影片是否已連線、CCTV View Switcher 的 Money Manager 欄位是否有填，以及填的是不是同場景的物件。

[手機畫面](MoneyManager-mobile.webp)／[桌機畫面](MoneyManager-desktop.webp)。這些是先前測試的截圖。

## 寫程式加錢、扣錢時再看這段

使用 `LiveStock.Economy` 裡的 MoneyManager。需要哪個場景的錢包，就把那個 MoneyManager 接到欄位，不要另做一份錢包。

玩家 ID 要用手機串流連線的 **connectionId**，不是 VR 的 Fusion PlayerRef。金額由 Unity 主機處理，不能讓網頁傳一個金額就直接加款。

| 呼叫 | 作用 |
| --- | --- |
| `RegisterPlayer(id)` | 建立錢包，但還不開始收入；已存在就保留原資料 |
| `TryGetBalance(id, out balance)` | 讀取餘額；找不到玩家時回 false，balance 為 0 |
| `AddMoney(id, amount)` | 加錢；amount 必須是正整數 |
| `TrySpendMoney(id, amount)` | 扣錢；餘額不足就回 false，不改餘額 |
| `StartIncome(id)` | 開始收入；重複呼叫不重算起點，瘋狂模式或結束後也不會重新開啟收入 |
| `StopIncome(id)` | 停止收入，保留餘額；未滿一秒的累計時間會清掉 |
| `SuspendIncome()` | 停止所有玩家的收入，保留餘額；這局後加入的手機也不會開始收入 |
| `RemovePlayer(id)` | 移除這個玩家的錢包 |
| `Clear()` | 清除這個 MoneyManager 裡所有玩家的錢包 |
| `BalanceChanged` | 餘額改變時通知，帶回玩家 ID 和新餘額 |

空白或不存在的玩家 ID 會被拒絕；加扣款金額不接受 0 或負數。金額超過 int 上限時，該次加款或收入不會發放，也不會變成負數。

只呼叫加扣錢，不等於已經發放道具。購買請走 ShopManager，使用道具請走 ItemUseManager。

一般模式在手機準備好後開始收入。遊戲流程要透過 CCTV View Switcher 的 `EnterCrazyMode()` 和 `EndRound()` 停止收入；目前沒有自行判斷勝負。資料沒有存進帳號、PlayerPrefs 或瀏覽器。

先前的一般模式測試紀錄見 [道具文件](ItemUse.md#前一次的測試紀錄)；瘋狂模式的手動操作見 [交接文件](MobileModule-handover.md#4-瘋狂模式怎麼接怎麼測)。
