# VR 與 Desktop 混合連線測試

VR 入口直接載入 `Assets/_Project/Scenes/_flow test/Menu_VRClient.unity`。保留該場景的環境、VR Rig 與 World Space LogInMenu，按 Connect 後才建立 Client Runner、加入 Server 的 Lobby。

## 本次回退與修正

- 移除「XR loader 不存在就自動切成預覽」的行為。一般 VR 入口維持 VR 模式。
- VR 不再使用 ConnectionTestClient prefab，也不再把 Menu 改成桌面 Overlay。該 prefab 繼續供 Desktop 測試使用。
- 無頭盔選項也載入原本的 Menu_VRClient 場景，只暫停本機 XR Rig，以觀察相機觀看原有 3D Menu。
- F2 保存並還原各相機原本的 enabled 狀態，避免每幀強制開啟所有 VR 眼睛與備用相機。
- VR 玩家載入完成後便由 Server 生成羊模型，沒有追蹤封包也會存在。頭手姿態就緒後再更新 IK。
- 離線環境隨所屬 ConnectionManager 保存，返回 Menu 時恢復該環境與觀察相機。

這些問題可由原有程式與測試確認。使用者先前遇到的斷線事件沒有留下足以確認單一根因的紀錄，因此應以這次重測結果判斷修正範圍。

## 沒有頭盔時

1. 儲存場景、停止 Play Mode。
2. 選 `LiveStock > Connection Test > Open VR Client Menu (no headset)`。會開啟原本的 Menu_VRClient 場景，先保持未連線。
3. 在 Hierarchy 選 `ConnectionManager`，右鍵 ConnectionManager 元件標題，選 `Connect to server`。沒有 Server 時應顯示 `GameNotFound`。
4. 選 `Start Server`，等待 Lobby 開好，再對同一個 ConnectionManager 執行 `Connect to server`。
5. 選 `Open Desktop Client Menu`，在 Desktop Menu 按 CONNECT。
6. 用 F2 切換 Server、VR、Desktop。Server 顯示 Players 2、Loaded 2/2，其他端可看到羊與企鵝。
7. 在 Server 畫面按 Start Game，一起進入 MainGame。
8. `Disconnect Selected Client` 返回該玩家的離線 Menu。`End Test` 結束並恢復測試設定。

也可先連好 Desktop，再開 VR Menu。重複選 VR 入口會選回既有 VR Client，一個 Editor 保留一套本機 VR Rig。

無頭盔時使用 Context Menu 連線。原有 VR 射線 UI 沒有改成滑鼠 UI。若要測試頭手傳輸，可在 Play Mode 修改 `VRClientPoseSource` 所引用的 Head Anchor、Left Hand Anchor、Right Hand Anchor 的局部 Transform。這是模擬追蹤資料，與實體裝置驗收分開。

## 使用頭盔時

先連接 Quest Link／頭盔，停止目前測試，再以 `Open VR Client Menu` 作為第一個入口，讓 XR 在建立任何 Runner 前初始化。之後再選 Start Server、開 Desktop Client、手動連線。

若先以 Desktop／Server 入口開啟測試，該次 Play Mode 的 XR 自動初始化已關閉。此時再選一般 VR 入口會保留原有 VR Menu 並提示 loader 未啟動，須 End Test 後從 VR 入口重新開始。工具不會在已有連線時同步啟動 XR loader。

## 實作位置

| 檔案 | 用途 |
|---|---|
| `Editor/LocalConnectionTestTools.cs` | VR 載入 Menu 場景、Desktop 建立 prefab、Server 與測試設定 |
| `ConnectionManager.cs` | Menu 輸入、Connect、Rig／Menu 保存、F2 相機狀態 |
| `LobbySessionState.cs` | Server 人數／載入狀態、羊與企鵝生成、姿態接收 |
| `VRClientPoseSource.cs` | 本機頭手 IK Target 取樣 |
| `VRNetworkAvatar.cs` | 遠端羊的根節點與頭手姿態 |
| `NetworkVRSheep.prefab` | 現有羊模型與 IK 的網路 prefab |

`SessionLauncher.cs`、`SessionPanel.cs`、GDC_METAVERSE 及原有模型未因這次修正變更。

## 本次驗收

| 測試 | 結果 |
|---|---|
| 原場景 VR 先開、沒有 Server 失敗、重試、混合連線、姿態及返回 Menu | [16 項通過](VR_Menu_Scene_VRFirst_Report.json) |
| Desktop 先連線，再載入原場景 VR，保持其他玩家連線與同步 | [15 項通過](VR_Menu_Scene_DesktopFirst_Report.json) |
| 完整本機 VR Rig 啟用、兩位 PC 先連、VR 後加、反覆切換、三人換場 | [9 項通過](VR_Menu_Scene_Native_Report.json) |
| 純 Desktop 回歸 | [14 項通過](Connection_Test_Independent_Report.json) |
| 新版獨立 Windows Server＋Editor VR／Desktop | [連線與姿態](VR_Menu_Scene_External_Join_Report.json)、[Server 實收資料](VR_Menu_Scene_External_Server_Report.json)、[換場](VR_Menu_Scene_External_Game_Report.json)、[關服清理](VR_Menu_Scene_External_Stop_Report.json) 通過 |

跨程序測試的 Server 頭手資料與來源數值差異為 0，企鵝位置誤差小於 0.001 公尺，兩位 Client 載入狀態 2/2。本次 Windows Development Build 成功，0 errors、189 warnings，詳見 [建置報告](VR_Lobby_Build_Report.json)。

[原有 3D Menu 的實測畫面](ConnectionFlowEvidence/vr_menu_scene.png)。前次 `VR_Mixed_*` 與 `VR_External_*` 報告保留為歷史結果，不能代替本版驗證。

完整 Rig 測試執行於沒有頭盔與 active XR loader 的 Editor，確認的是連線、玩家與場景生命週期。實體頭盔畫面、追蹤、控制器、VR UI 射線、身高與 IK 校正仍需裝置驗收。

Windows 程式有 `-flow-server`、`-flow-desktop`、`-flow-vr-preview` 三種測試參數。這次調整了 Fusion RPC，Server 與 Client 必須使用同一版重新建置的程式。
