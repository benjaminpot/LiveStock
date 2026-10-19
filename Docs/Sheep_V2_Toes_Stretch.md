# V2 前腳 Toes 優先對齊

Prefab：`Assets/_Project/model/SK_Sheep_white_V2.prefab`

左右 `ToesFront` 對齊現有播放器的 Left / Right Hand Target，包含位置與旋轉。
錄製的座標校正、動作清單、播放操作與 Apply Hand Rotation 設定保持原樣。
這裡的對齊是對映射後的 Target，不是直接複製 VR 的世界座標。

## 每隻前腿的解算順序

1. **Stretch To Target Constraint**：在 Animation Rigging 的 animation stream 中，
   依當幀髖部與目標距離調整 Knee / Ankle 的骨段間距。不自行計算 IK 旋轉。
2. **Two Bone IK**：標準 HipFront → KneeFront → AnkleFront，負責關節旋轉。
3. **Multi-Parent / Exact Ankle Pose**：將腳踝精確放到對應目標，
   修正完全伸直、接近髖部等退化姿勢的末端偏差。

原本的手柄 Target 代表 Toes。其下的 `Ankle IK` 子物件保存腳掌到腳踝的
局部位置／旋轉換算；Target 上的 Rig Transform 負責同步進解算流程。
沒有另外覆寫 Toes 的位置，沒有在 LateUpdate 用腳本搬動骨頭。

距離正常時沿用原骨段長度；太遠時兩段等比例延長；太近而無法折回時，
只延長較短的一段，使兩段可以折回。每次都從保存的原始長度計算，不累積伸長。
為優先滿足端點，沒有最大伸長上限，也不縮放手柄 Target 或整隻羊。

## 模型表面延展

原始模型的前腿零件是單骨剛性權重，只改骨節間距會露縫。
V2 使用專用的四份大腿／小腿 Mesh 副本，將權重沿骨段分配到相鄰兩根骨頭，
讓腿部表面隨髖－膝、膝－踝間距延展，而不是只把零件拉開。

副本集中在 `Assets/_Project/model/SK_Sheep_white_V2_FrontLegSkin.asset`。
原始 FBX、頭部、後腳、腳掌 Mesh、原本 Animator Controller 與播放器程式未修改。
長距離或大幅度折腿仍可能有誇張拉伸、扭曲或接縫；端點對齊不等於自然動作。

## 使用與重建

照常使用 VR Recording Target Player 的動作按鈕。
修改頭或後腳不會由此工具自動調整；這次只處理前腳。
支援不同位置／朝向及啟動前設定的正值等比尺寸。
不支援非等比／負值縮放，也不建議在執行中改變骨架 Scale。

重設連接：`Tools > LiveStock > Configure V2 Stretchable Front Legs`。
工具會更新 V2 Prefab 及場景內同名 V2，包括已 Unpack 的 V2 副本，
不刪除或替換場景物件；場景保留待使用者確認、Ctrl+S。
不要刪掉 Ankle IK、Stretch 或 Exact Ankle Pose 中任一環節後仍期待端點保證。

## 驗證

`Tools/ValidateSheepToes.cs` 以獨立 Preview Scene 執行，不改使用者場景。
測試三份錄製的全部幀，另含接近髖部、3 m 遠目標與大角度旋轉。
測試朝向／尺寸：0°／1 倍、90°／1 倍、180°／1.5 倍。
合計 11,226 個姿態檢查；Toes 最大位置誤差約 0.0042 mm，旋轉差 0°。
已在真正 Play Mode 確認 idle、walk、eat 的 Toes 對齊，並檢視 walk 的表面延展。
這些是播放／模擬目標測試，不是實際頭盔操偶測試。

修改前的 V2 Prefab、播放器與當時尚未儲存的場景副本已封存於
`Docs/Backups/SheepV2_before_toes_stretch_20261015_224547.zip`。
