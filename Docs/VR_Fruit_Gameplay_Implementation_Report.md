# VR 玩家與蘋果互動實作記錄

日期：2026-11-16。分支：`single-to-multi`。

## 已完成

- 沿用使用者加好的 Locomotor 與既有姿態同步。未更換模型、IK 或頭手姿態格式。
- 現有 Controller Ray 經食指扳機 Selector 的按下事件提出收集請求；不另讀一次扳機。按住不會因移向另一顆蘋果而再提出請求。
- NetworkApple 已配置 RayInteractable、ColliderSurface 與 PointableUnityEventWrapper。小實體 Collider 作為射線表面，大 Trigger 保留原本的身體接近規則。
- 按下時重新檢查「射線選中的這顆」與玩家身體膠囊是否重疊。手伸近、Ray 指到、或靠近另一顆，都不取代這個接近條件。
- 膠囊重疊以世界座標幾何查詢，避免本機持續存在的 rig 與 Multi-Peer 地圖分處不同 PhysicsScene 時收不到 Trigger 回呼。
- 本機與伺服器皆檢查射線遮擋；查詢所屬 Runner 的物理場景，排除另一個本機 Runner 的蘋果與互動來源。
- 伺服器驗證發送者、Running 階段、追蹤／身體資料、目標歸屬、接近與遮擋。已收集狀態由伺服器寫入，每顆只成功一次。
- 所有羊共享 3 顆進度，模型／碰撞／互動同步關閉；收滿後顯示「蘋果已收集完成！」。不製作出口或勝負結算。
- 將回饋與圖示接到既有 `Player_UI/state` 和三顆 Apple 圖示，不重建 UI 階層或位置。
- 接上網頁獵人的 mission-status 訊息；新連線握手也取得目前快照，舊通道訊息不會蓋掉新通道的畫面。
- Run／Crouch 接到既有 Locomotor 的按住／鬆開事件，蹲下優先；原有 Jump、搖桿移動與轉向保留。
- 修正 `state` 原字型缺中文字的問題，使用靜態 1024×1024 字型圖集；包含中文回饋與桌面狀態所需 ASCII。來源與 OFL 授權見 `Assets/_Project/Fonts/README.md`，執行期字型不引用完整來源字型。

## 主要接合位置

| 位置 | 職責 |
| --- | --- |
| PlayerGameplay | 綁定本機 Player_UI、擷取身體膠囊、顯示回饋，提供桌面測試入口 |
| FruitRayInteractor | 沿用 ControllerSelector 按下事件、選取來源與 Runner 過濾 |
| PlayerBodyPose | 原 Trigger 與身體膠囊的重疊規則 |
| FruitPickup | 單顆同步收集狀態、射線遮擋、外觀／碰撞更新 |
| NetworkWorld | 驗證 RPC、彙總共同進度、回覆個別玩家；離開場景清除進度 |
| VRLocomotionActions | 既有奔跑／蹲下的按住事件與優先順序 |
| CCTVViewSwitcher／cctv-panel.js | 網頁任務快照與完成提示 |

## 目前可做的桌面驗證

使用 `LiveStock > Connection Test` 的既有 Server／Desktop 入口，連線後依序 Load MainGame、Start Game。

- WASD 移動、右鍵觀看、Q/E 上下移動、Shift 加速維持原測試操作。
- 用畫面中央瞄準蘋果，按滑鼠左鍵收集；需要讓測試玩家身體靠近。
- 桌面 session 狀態區會顯示互動回饋；VR 的相同文字由 Player_UI/state 顯示。
- 已兼容原第三人稱相機位於玩家後方的測試射線。這不放寬蘋果與身體的 Trigger 接近規則。

可重複執行的驗收入口：

```powershell
& ./Tools/ConnectionFlow/Test-Fusion.ps1 -Suite Fruit
```

## 驗證結果

| 驗證 | 結果與範圍 |
| --- | --- |
| Unity 程式編譯 | 通過，無編譯錯誤 |
| 真實 Fusion Server＋兩個 Client | 25 項通過；原範圍、伺服器拒絕遠處／遮擋、去重、共享進度、第三人稱相機請求、UI、離線保留、新房間／新地圖歸零 |
| 本功能資產檢查 | 通過；三個 Prefab 無遺失腳本、兩支控制器 Ray 已接、三顆圖示、身體引用、NetworkObject bake、中文靜態字型正確 |
| 網頁 Jest | mission-status 與既有 inventory-panel 共 21 項通過；涵蓋進度、完成、非法快照、重連與舊通道 |
| UI 渲染 | 已視覺檢查 Player_UI 的隔離靜態預覽，中文字可讀；不是頭盔中的呈現結果 |

多人結果保存在 `Docs/Fruit_Gameplay_Acceptance_Report.json`，資產結果保存在 `Docs/Fruit_Gameplay_Asset_Report.json`。本次多人驗收只在測試期間停用 NPC 生成，以隔離收集功能；未改動地圖的 NPC 生成資產，因此不是 60 隻 NPC 的效能驗收。

既有 `ValidateFusionAssets` 在檢查新 MainGame 時，因其要求必須存在的 `Recycle Bin` 根節點已不存在而中止（Sequence contains no matching element）。未更改此舊工具或重建該物件；不能宣稱通用 Assets 套件全數通過。本功能使用獨立的 ValidateFruitGameplay 檢查實際引用。

## 尚待實體設備或完整串流驗收

- 使用者已確認其他連線玩家能看見 VR 模型動作；這是既有同步的使用者觀察，本次未重新取得頭盔驗證。
- 實際手把扳機選取、雙手切換、Locomotor 的移動／跳躍／奔跑／蹲下，以及 UI 在頭盔中的尺寸與可讀性，仍需設備驗收。
- 網頁已驗證資料訊息的處理，未完成真實 WebRTC 影像、CCTV 多瀏覽器與頭盔同場的端到端測試。
- 本次沿用客戶端送出位移結果的架構；收集有伺服器驗證，不代表已實作完整的伺服器移動模擬或防作弊。
- 未重新建置獨立 Player；後續 Standalone 驗收必須先重建，不能使用舊執行檔驗證新增欄位與 RPC。

## 保留使用者修改

開始配置前，已將 Editor 中 MainGame 的未儲存內容保存副本並儲存；副本與當時儲存後的場景 SHA256 一致。保存位置為 `.tmp/FruitGameplay/MainGame_user_edits.unity`；原磁碟場景副本為 `.tmp/FruitGameplay/MainGame_on_disk_before.unity`。這包含使用者對場景 UI 的移動／停用，不能把整份 MainGame 差異當成這次程式實作的修改。

## 後續保存與測試狀態

已於 `1c1ff4b6` 保存本功能，commit 明確標註測試尚未完成。後續階段的修改、代做決定、commit 與最新驗證結果見 [階段實作紀錄](Implementation_Progress_2026-11-16.md)。上方為初次實作紀錄，不能當成最新狀態；通用 Assets 阻擋已在後續修正，收集回歸已擴充為 33 項並通過，網頁任務／inventory 共 26 項通過。Editor 與兩個真實瀏覽器的不同相機影像、逐顆進度及重連已通過 12 項整合驗收；獨立 Player 亦已重建並取得 RPC／來源停用恢復證據。實體頭盔與其同場串流仍未驗收；獨立影片的後續結果以階段紀錄與各環境報告為準。
