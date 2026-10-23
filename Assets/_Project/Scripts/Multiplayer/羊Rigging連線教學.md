# 羊模型外觀與後腳 IK 同步

## 目前版本：同步完成 IK 後的骨架

`NetworkSheepRig` 已改成同步模型骨架，不再由 Client 自己解算三個 Target。
Server 播放錄製動作，執行 Animator、Animation Rigging 與 Final IK；在順序 30000 的 LateUpdate 取得完成姿態，再於下一個 Fusion Tick 發布。
Client 關閉這隻 NPC 的播放器、Animator、RigBuilder 和 Final IK，在 LateUpdate 套用骨頭相對父節點的位置、旋轉與縮放，蒙皮模型呈現 Server 的姿態。

網路羊 Prefab 目前有 34 個同步骨架節點，NetworkArray 容量為 64。骨架從 SkinnedMeshRenderer.bones 和祖先節點收集，再依階層排序；兩端必須使用相同版本的 Prefab。根節點移動仍由 NetworkTransform 同步。
這裡的「外觀」是蒙皮骨架變形；模型與材質資產由相同 Prefab 提供，選取變色仍由 NetworkSheepSelectable 處理。沒有加入任意貼圖、BlendShape 或換裝同步。

## 後腳跑掉的原因與修正

原始羊 BipedIK 的 leftFoot / rightFoot 沒有指定 Target Transform，卻保存了舊場景的 IKPosition 世界座標，例如左腳 (3.49, 0.15, 2.49)。隨機生成位置及旋轉改變後，這個固定世界位置仍然有效，後腳就被拉向舊位置。

同步元件在 Awake、Final IK Start 初始化之前，以後腳骨頭當前姿態建立 `Rear Foot L Target`、`Rear Foot R Target`，放在本隻羊根節點之下並指定給 solver.target。已有 Target 的設定保留。新目標隨本隻羊移動和旋轉，不再沿用舊世界座標。
此修正以生成時的後腳姿態作為站姿，不是地形腳掌貼地或行走跨步系統；凹凸地形仍需要另外設計 Grounder／腳步控制。

## 使用

1. 生成 `Assets/Prefabs/Networked/NetworkSK_Sheep_white_final.prefab`，Network Spawner 與 RandomNPCSpawner 都可使用。
2. 啟動 Server，加入 PC；網路羊自動套用新同步方式。
3. Play Mode 選 Server 那份羊的 NetworkSheepRig 元件，在元件選單使用 `Motion > Idle / Walk / Eat (Server)` 切換。
4. 後續 AI 可在 Server 呼叫 `PlayMotion(index)`，索引依 VRRecordingTargetPlayer.motions 清單。Walk 只有骨架動作，根節點移動要另外實作。

只在 Server 編輯姿態；Client 骨頭會被收到的姿態覆寫。修改腳本或 Prefab 後要停止 Play Mode 再重開，確保 Awake 與 Fusion 物件資料重新建立。

## 程式學習

- `Awake`：建立穩定的骨架列表，補上缺少的後腳目標。
- `Spawned`：依 StateAuthority 決定哪些動畫元件需要執行。
- `LateUpdate`：Server 取樣解算後的骨架，Client 套用骨架。
- `FixedUpdateNetwork`：把已取樣的資料寫入 NetworkArray，交給 Fusion 同步。
- `PoseReady`：首份骨架尚未發布時不套用全零姿態。
- `Despawned`：停止對已離開網路的物件取樣。

## 已完成驗證（2026-10-23）

編譯通過並重新烘焙網路羊 Prefab。同一 Editor 的 Dedicated Server + 兩位 PC（含晚加入）共有六隻不同位置的羊。切換吃草後暫停播放，逐隻比較全部 34 個骨架節點：旋轉與縮放差為 0，位置最大誤差小於 0.000001 公尺，Client 的 Final IK 已停用。
不同生成位置的左後腳目標具有相同的根節點相對位置；Server solver 的 IKPosition 與本隻羊後腳 Target 相符。
測試完已退出 Play Mode。未測 VR 頭盔或高延遲網路；目前直接套用收到的骨架，沒有插值。

完整骨架比三個 Target 使用更多網路資料，容量 64 也有狀態成本。大量 NPC 前需要量測頻寬，後續可採動作狀態＋時間或骨架壓縮優化。

官方參考：https://doc.photonengine.com/fusion/v2/manual/network-behaviour
