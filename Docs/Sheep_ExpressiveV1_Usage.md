# V1 Animation Rigging 三點操偶

Prefab：Assets/_Project/model/SK_Sheep_ExpressiveV1.prefab
主要程式：Assets/_Project/Scripts/MotionRecording/SheepExpressivePlayer.cs

## 控制方式

錄製的 VR Anchor 資料 → 羊根物件局部座標映射 → 三個 Target → Animation Rigging。
即時 VR 與錄製播放共用相同映射，不直接複製 VR 的世界座標。

- Head：頭盔對應的目標位置與完整旋轉。
- Left Front Foot / Right Front Foot：手柄對應的腳掌目標位置。
- Body：由三點相對校正姿勢的平均位移與左右手連線的水平朝向推算。
- Left Hind Foot / Right Hind Foot：固定於羊根物件局部空間的初始參考落點。

只有前三個 Target 是 VR 輸入。身體和後腳是推估／支撐規則，不是額外錄製資料。
不從手柄旋轉推算蹄子旋轉；蹄子方向跟隨身體水平朝向。
手柄連線接近垂直或重合時，維持上一個有效水平朝向；交叉雙手仍可能改變推估方向。

## 使用的 Animation Rigging 功能

同一個 RigBuilder 只有一個 Rig，依 Hierarchy 順序執行：

1. Multi-Parent × 1：Pelvis_joint 跟隨 Body。
2. Chain IK × 1：Neck_M_joint → Neck1_M_joint → Head_M_joint，追蹤 Head。
3. Two Bone IK × 4：左右前／後腿的 Hip → Knee → Ankle。

Head 的 Chain Rotation Weight / Tip Rotation Weight 均為 1，不使用 Multi-Aim，
也不另外疊 Multi-Rotation。頭部目標旋轉已包含模型初始朝向校正。

各腳掌 Target 下的 Ankle 子物件保存腳掌至腳踝的局部座標轉換，
讓 Two Bone IK 的末端是腳踝，而輸入對應腳掌；此偏移隨 Target 旋轉與等比縮放。
四個 Hint 放在 Body 下，只決定膝蓋彎曲方向，不是另外四個 VR 控制點。
四個腳掌 Target 加 Rig Transform，將父物件的移動同步至 IK 使用的 Ankle 子物件。
Constraint 的 Maintain Target Offset 關閉，避免與已做的座標校正重複補償。

## 固定骨長的限制

已移除自寫的 Solve、骨頭位置覆寫與骨頭伸縮。執行時腳本只寫 Target。
超出可及範圍，或三點無法同時由目前骨架到達時，IK 盡量靠近，但不保證端點重合。
不會把頭或蹄子強制搬到 Target，也不會偷偷截斷 Target 軌跡。

Chain IK 不提供羊的解剖關節限制；只有三個追蹤點也不能還原真實身體／後腳步態。
後腳只是固定參考支撐點，不包含地形偵測、踏步或程序化 walk。
這版以正值等比 Root 縮放為前提，不支援非等比或負值縮放。

## 播放操作

進入 Play Mode，選取 V1 羊，按 Play idle / eat / walk。
Pause 後拖 Time 滑桿；Stop / Restore 釋放 Rig 並切回基準姿勢。

Recordings 中可以拖曳指定：
- Custom Recording：MotionRecordingAsset，優先使用。
- 或 Custom Metadata / Custom Binary Frames：同一次錄製的 metadata.json / frames.bytes。
- Play Dragged Recording：播放指定資料。
- Use Custom On Start 配合 Play On Start：開始時播放指定資料。

Use Live Vr 使用 TrackingSpace / CenterEyeAnchor / LeftHandAnchor / RightHandAnchor。
Calibrate Live VR 以當下三點姿勢重新校正，並停止先前檔案播放。

Use Base Animation 預設關閉：不播放既有 idle/eat/walk Clip，但 Animator 仍必須啟用才能執行 Rig。
關閉時用一個固定基準姿勢 Clip 提供解算輸入；不是停用 Animator。
啟用時，先播放既有 Clip，再由 Rig 控制上述部位。
原有 SheepVrMotion.controller 不修改；V1 使用獨立 SheepExpressiveV1.controller。

## 座標校正

Source Scale：三點共用位移比例，預設沿用原設定。
Source To Sheep Euler：三點共用軸向轉換。
Calibration Recording：沿用 Idle.asset 第一幀作為三份錄製的共同基準。

沿用共同頭部原點：校正時頭盔對齊羊頭初始位置，雙手保留相對頭盔的空間關係。
沒有新增每隻腳的獨立位置補償或動作放大參數。
因此原始 VR 姿勢與羊的身形差異，仍可能造成初始手柄目標無法由前腳到達。
Rig Targets 展開欄可檢查三點與 Body 的實際引用。

## 重建與驗證

Tools > LiveStock > Rebuild Three-Point Sheep V1：
更新既有 V1 Prefab，保留其播放／校正設定，不修改 SK_Sheep_white.prefab。
重建控制物件會更換其引用，請勿將自訂物件放進生成的 VR Targets 或 Motion Rig 中。
場景的 VR 引用會更新並標記待儲存，確認後自行 Ctrl+S。

Tools > LiveStock > Validate Animation Rigging Sheep V1：
在獨立 Preview Scene 測試，不修改使用者場景。
檢查 Constraint 配置、三份錄製各 61 點的映射、頭部旋轉、固定骨長，
以及可達／不可達目標、自訂資產／原始檔播放、動畫切換與 Stop。
端點差距會如實回報，不再把「強制放到端點」當作骨架效果驗證。
實際頭盔與動作視覺效果仍需另外確認。

## 本次驗證結果（2026-10-15）

- Unity 6000.3.25f1／Animation Rigging 1.4.1，C# 編譯通過。
- 三份錄製各 61 點，測試 0°／原尺寸、90°／原尺寸、180°／1.5 倍尺寸，共 549 點。
- 骨段長度最大差約 0.0000004 m；頭部目標旋轉差為 0°。
- 原尺寸 183 點中，173 點至少有一個端點差超過 1 mm。
- 原尺寸最大差距：頭部約 1.049 m、前腳約 0.874 m、後腳約 0.051 m。
  這是目前輸入映射／骨長／身體跟隨規則的限制，不代表已重現原始表演。
- 真正 Play Mode 已測試 walk 暫停、Seek、基礎動畫開關；關閉動畫時 Rig 仍運作。
- 真正 Play Mode 手動移動可達的左右前腳 Target，腳掌位置差小於 0.001 mm。
- 模擬 TrackingSpace 的平移與旋轉、不同來源軸向下，即時 Target 位移對應誤差小於 0.001 mm。
- 已檢視吃草取樣的 Scene 畫面；沒有把這項視覺檢查等同於原始動作復現。
- 本次 OpenXR 啟動回報 XR_ERROR_FORM_FACTOR_UNAVAILABLE，因此沒有完成實際頭盔操偶測試。
