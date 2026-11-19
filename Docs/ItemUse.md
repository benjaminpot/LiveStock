# 手機道具：怎麼接、怎麼用、怎麼調

目前可以「買道具 → 在物品欄選取 → 點影片發射 → 打到 NPC 扣血」。

**四種道具現在都是球體。寶貝球還不能捕捉、狙擊槍還不是射線、炸彈還沒有範圍爆炸；也還沒接 VR 玩家受擊與淘汰。**

## 要拖什麼

如果還沒有金錢和商店，先照 [整套接法](MobileModule-handover.md) 放好三個管理器。已經有商店的場景，只要補下面這幾步：

1. 停止 Play。
2. 把 `Assets/_Project/Prefabs/Items/ItemUseManager.prefab` 拖到 **ServerOnly** 底下。已經有就不用再放。
3. 點 Hierarchy 的 **ItemUseManager**，把同場景的 **ShopManager** 拖進 **Shop Manager** 欄位。
4. 找到 **CCTV View Switcher** 元件，把剛放好的 **ItemUseManager** 拖進 **Item Use Manager** 欄位。
5. 按 **Ctrl＋S 儲存場景**。不要把場景引用 Apply 回共用 CCTV Prefab。

四個道具已經在 ItemUseManager 的 **Projectiles** 裡配好，不用再把四個 Prefab 拖到場上，也不要改它們的商品 ID。

## 玩家怎麼使用

1. 開啟串流頁面並連線；啟動方式看 [測試步驟](MobileModule-handover.md#3-怎麼測試)。
2. 在商店買道具。
3. 在物品欄點選剛買的道具。
4. 點監視器影片裡要打的位置，就會發射。

選取和取消不消耗。成功發射才消耗一份，射空也不退還；最後一份用完會自動取消選取，仍會顯示這次發射的命中結果。

零庫存、鏡頭無效、監視器過熱或還在冷卻時，使用會被拒絕，不扣道具。目前每次使用至少間隔 0.2 秒。

## 瘋狂模式的槍

由 Unity 的 **CCTV View Switcher** 呼叫 `EnterCrazyMode()` 後，所有手機會自動換成連發槍，不必購買或選取。

- 按住影片連發，放開就停；預設每 0.2 秒一發。
- 每次成功發射扣 100 元，射空也扣。不到 100 元不能射，不消耗原本庫存。
- 收入暫停，商店和一般道具暫時不能用；原本的錢和道具不會清掉。
- 切台、過熱、離開視窗、斷線或結束本局都會停射。
- `EndRound()` 會停止使用並回收還在飛的球，不會恢復一般模式。

完整手動測試看 [交接文件](MobileModule-handover.md#4-瘋狂模式怎麼接怎麼測)。

## 速度、傷害、大小在哪裡改

先停止 Play。在 Project 開啟：

```text
Assets/_Project/Prefabs/Items/
```

選要修改的道具 Prefab，看 Inspector 的 **Item Projectile** 元件：

| 欄位 | 改的是什麼 |
| --- | --- |
| Speed | 往前飛的速度 |
| Use Gravity | 是否受重力影響 |
| Upward Speed | 發射時額外向上的速度，有重力時可用來做拋物線 |
| Damage | 命中 NPC 時的傷害 |
| Diameter | 球的直徑 |
| Lifetime | 最多飛多久，單位是秒 |

改好儲存，再啟動新的主機。改 Project 的道具 Prefab 會影響使用它的場景，不是只改目前這一局。

目前設定如下；速度和大小使用 Unity 世界單位：

| 道具 Prefab | Speed | Use Gravity | Upward Speed | Damage | Diameter | Lifetime |
| --- | ---: | --- | ---: | ---: | ---: | ---: |
| CaptureBall.prefab | 12 | 開 | 3 | 1 | 0.22 | 5 |
| BowProjectile.prefab | 10 | 關 | 0 | 1 | 0.08 | 6 |
| SniperProjectile.prefab | 35 | 關 | 0 | 3 | 0.08 | 3 |
| BombProjectile.prefab | 8 | 開 | 4 | 2 | 0.28 | 5 |
| CrazyGunProjectile.prefab（瘋狂模式專用） | 35 | 關 | 0 | 1 | 0.08 | 3 |

## 攻擊掛在哪裡？有改到原本的 NPC 嗎？

- **CCTVViewSwitcher**：接手機點擊，再交給 ItemUseManager。這個原有腳本有修改，加入道具指令的處理。
- **ServerOnly 下的 ItemUseManager**：一般模式檢查並消耗道具；瘋狂模式改成檢查餘額、每發扣 100 元，再發射專用槍彈。
- **每顆球上的 ItemProjectile**：處理飛行、碰撞和命中傷害。

新增的是 ItemUseManager、ItemProjectile 等道具程式。**沒有修改 NPC 原本的受傷、死亡、重生程式**；球命中時，呼叫既有的 `NetworkNpcAuthorityGate.TryApplyDamage`，沿用原本的血量和重生流程。

如果要改碰撞程式，注意 `TryApplyDamage` 回 false 也可能是已扣血但 NPC 還沒死，不能直接當作沒打到。不要另外做第二套 NPC 血量。

球由 Unity 主機處理，手機透過影片看到結果，目前沒有另外同步成 Fusion 網路物件。沒有接 ItemUseManager 的原場景仍用舊測試射擊；接了之後，不能用舊射擊指令跳過庫存。

## 常見狀況先查哪裡

- **買得到但不能用**：先看 ItemUseManager 的 Shop Manager，以及 CCTV View Switcher 的 Item Use Manager 是否有填。
- **球是粉紅色**：確認使用 `Assets/_Project/Prefabs/Items/ItemProjectile.mat`，這是專案的 URP 材質，不要換成不相容的內建材質。
- **一射出就撞到東西**：看鏡頭是否在監視器外殼或牆裡。球有大小，鏡頭周圍要留空間，不要直接關掉碰撞掩蓋位置問題。
- **改了參數卻沒變**：停止 Play，確認改對道具 Prefab、已儲存，再啟動新的主機。
- **重連後道具沒了**：目前就是這個規則，沒有存檔。各手機的庫存分開，斷線也會清掉它還在飛的球。

如果要改發射程式，球從停用狀態取出後，要先啟用，再設定 Rigidbody 的位置和速度。先設速度再啟用，曾實測出現「回報已發射但球沒飛」；這個問題已修正，別把順序改回去。球的存活時間跟遊戲時間走，使用冷卻則不受 timeScale 影響。

## 本次瘋狂模式的測試紀錄

- Unity 金錢與道具規則 117 項、實體球 1 項通過；前端相關測試 114 項通過。
- 用真實 Unity 主機與兩個 390px Chromium 網頁測試，不是只放假資料看畫面。
- 測試時暫把第一頁餘額設成 599 元。長按成功發射五發後剩 99 元，不能再射；原本的一份弓箭仍在。放開後不補射，也沒有再自動加錢。
- 第二頁在瘋狂模式中才加入，自動換槍，餘額維持 0，不會開始收入。
- 結束本局時，實際飛行中的球從一顆變成零顆；新的使用要求被拒絕、不再扣款，兩頁都顯示「本局已結束」。
- 測完已停止 Play、關閉測試頁與本次啟動的網頁服務。回到原本已儲存的 Haohao 測試場景，沒有保存臨時餘額或測試物件。

這次使用 Fusion 單機主機驗證手機串流與操作；沒有宣稱完成多人 VR、實體手機或 Safari 驗收。正式勝負流程仍需隊友呼叫 `EnterCrazyMode()`／`EndRound()`。

## 同步遠端後的驗證

- 保留遠端的蘋果收集進度與手機瘋狂模式，兩種資訊可在同一頁正常顯示；沒有把任務完成自行改接成勝負或模式觸發。
- Unity 金錢與道具規則 117 項、實體球 1 項通過；前端連同蘋果進度共 6 組、122 項通過。
- 真實 Unity 主機與兩個 390px Chromium 頁面驗證：過熱鏡頭不發射、不扣款；切到可用鏡頭後，長按成功發射兩發，餘額從 599 降到 399，原本一份弓箭不變。
- 晚加入的頁面同時收到瘋狂模式與蘋果進度，餘額維持 0；結束本局後兩頁停止操作，飛行球回收，新的發射被拒絕。
- 測完已停止 Play、關閉本次測試瀏覽器，回到原本已儲存的場景。本機 XR、Android、畫質等既有設定保持不變；原先已在跑的網頁服務沒有關閉。

## 前一次的測試紀錄

以下保留先前一般道具的驗收結果，不代表瘋狂模式已完成實體手機或 Safari 驗收。

- Unity `LiveStock.Economy.Tests`：98 個案例通過，0 失敗、0 跳過，包含錢包 19、商店 35、使用規則 44 個案例。
- Unity `LiveStock.ItemPhysics.Tests`：1 個案例通過，實際模擬球第一次發射及回收重用後是否移動。
- 前端：5 suites、82 tests 通過。
- 在真實 Unity 主機和兩個 375px Chromium 頁面，測過四種道具的購買、選取、發射、消耗及結果回報。
- 兩頁的飛行球、庫存和結果不混在一起。關閉一頁只清掉那一頁的資料與球，另一頁繼續運作；重連從零開始。
- 同一個使用要求重送不會多扣或多生球；舊編號、換商品重用編號、無效鏡頭和越界座標都會被拒絕。
- 測試時暫把一個 NPC 血量設成 3：弓箭命中後變成 2，狙擊球再命中後變成 0；約 3.348 秒後同一個 NPC 恢復血量、碰撞與動作。這些臨時測試設定沒有保存，也沒有修改 NPC 資產。
- 測試後重新開啟場景，確認三個管理器、欄位、道具和材質都還在，測試價格仍是 10,000／10,000／59,900／59,900。沒有把這套設定回填原 MainGame 或共用 CCTV Prefab。

要重跑前端測試，在專案根目錄開終端機執行：

```powershell
cd WebApp/client
npm test -- --runInBand test/item-use-controller.test.js test/cctv-aim.test.js test/inventory-panel.test.js test/shop-panel.test.js test/money-panel.test.js
```

[新版接收頁截圖](ItemUse-mobile.png)／[舊版接收頁截圖](ItemUse-legacy-mobile.webp)。

Safari 和實體手機尚未測過。舊版接收頁的 codec 選單仍可能造成橫向捲動；既有 Meta XR／Gamepad remoting 錯誤也沒有在這個模組裡修正。前一次驗收最後已停止 Play，臨時 NPC 和鏡頭設定沒有保存；當時網頁服務保留執行，現在是否仍在跑要另看。
