# Connection Test 操作說明

目前工具使用單一動作選單，每個 Client 都從未連線的 Menu 開始。測試入口位於 Unity 上方的 **LiveStock > Connection Test**。

## 選單功能

| 選單 | 動作 |
|---|---|
| Open VR Client Menu (no headset) | 載入原本 Menu_VRClient 場景，以相機觀看 3D Menu，透過 Context Menu Connect 連線 |
| Open VR Client Menu | 載入原本 Menu_VRClient 場景、保留 VR UI 與 Rig，一個 Editor 支援一套本機頭盔 |
| Open Desktop Client Menu | 開啟一個未連線的 Desktop Client，可重複建立多位 |
| Start Server | 建立 Server 並開啟 Lobby |
| Stop Server | 關閉目前房間，已連線玩家會收到斷線狀態 |
| Next View (F2) | 輪流選取 Server 與各個 Client |
| Disconnect Selected Client | 目前 Client 離開房間並返回自己的 Menu |
| Close Selected Client | 中斷目前 Client 並移除它的物件 |
| End Test | 結束 Play Mode，還原測試前的 Peer Mode、XR 與 Display 設定 |
| Build Windows Connection Test | 建置 Single Peer Windows 測試程式 |

停止 Play Mode 時選 VR 入口，工具會載入原本 Menu_VRClient 場景。Desktop／Server 入口使用 ConnectionTestBootstrap。Play Mode 中可以逐一新增其他端。F2 切換同一個 Editor 裡的端，Game View 需取得鍵盤焦點。畫面會顯示目前 Client 名稱與 F2 提示，桌面鍵盤滑鼠只控制目前選取的 Client。

## 建議測試順序

1. 儲存場景，選 Open Desktop Client Menu。
2. 保持房名 LIVESTOCK-DEV、地區 asia，按 CONNECT。沒有 Server 時應顯示 GameNotFound，可在同一個 Menu 重試。
3. 選 Start Server，等 Server 的 Lobby 開啟。
4. 按 F2 切回 Desktop Client，再按 CONNECT，等待人數 1/1。
5. 再選 Open Desktop Client Menu，第二位先維持未連線，確認 Server 人數仍為一位。
6. 第二位手動按 CONNECT，等待 Server 顯示 Players 2、Loaded 2/2。
7. 用 F2 在 Server、Desktop Client 1、Desktop Client 2 間切換。
8. 在 Server 畫面按 Start Game，兩位玩家一起進入 MainGame。
9. 在各個 Desktop 畫面使用 WASD、右鍵觀看、Q／E 升降、Shift 加速。其他已連線端會看到對應企鵝移動。
10. 測試 Disconnect Selected Client、Stop Server 與 Close Selected Client，完成後選 End Test。

VR 測試時改用 Open VR Client Menu，再自行選擇是否開 Server、何時按 CONNECT。實體頭盔與 VR／Desktop 混合追蹤仍須使用裝置驗收。

## Prefab 與修改位置

所有專用 Prefab 位於 `Assets/_Project/Prefabs/Multiplayer/`。

| Prefab | 內容與修改方式 |
|---|---|
| DesktopTestPlayer.prefab | 本機玩家根物件、DesktopTestPlayer 操作元件、第三人稱相機、AudioListener 與企鵝。可調速度、靈敏度、出生偏移、玩家間距及子相機位置 |
| DesktopPenguinVisual.prefab | 共用企鵝外觀，內含原始 Pinguin_001.prefab 的巢狀實例，統一調整本機與網路外觀。此資產覆寫停用原模型的 CreatureMover、MovePlayerInput、CharacterController 及動畫 Root Motion，讓玩家根物件控制位置 |
| NetworkDesktopPenguin.prefab | NetworkObject、NetworkTransform、DesktopNetworkAvatar 與企鵝外觀，由 Server 按玩家身分生成並同步 |
| ConnectionTestClient.prefab | 完整未連線 Client 選單與 VR 引用，根物件在資產中保持停用，工具設定端別後啟用 |
| LobbySessionState.prefab | 人數、載入回報、階段及 NetworkDesktopPenguin 的 Inspector 引用 |

ConnectionManager 的 Desktop Player Prefab 欄位指向專屬玩家資產，每次建立 Desktop Client 都會實例化該資產。Server 按發送 RPC 的 PlayerRef 生成網路企鵝，玩家離開後移除。模型、相機與玩家控制元件都保存在 Prefab 中。

桌面移動維持自由飛行，方便檢查場景。位姿以每秒 20 次送至 Server，再透過 NetworkTransform 同步。這是目前連線測試用的移動方式。

工具選單與生命週期位於 `Editor/LocalConnectionTestTools.cs`。F2 與輸入路由位於 `ConnectionTestViews.cs`。連線流程位於 `ConnectionManager.cs`，Server 與企鵝生成同步位於 `ServerConnectionManager.cs`、`LobbySessionState.cs`。

## 驗收記錄

新流程的 14 項自動驗收全部通過，記錄位於 [Connection_Test_Independent_Report.json](Connection_Test_Independent_Report.json)。先前 VR_Lobby 報告保留為改版前的歷史結果。Windows 建置完成狀態以 [建置報告](VR_Lobby_Build_Report.json) 為準。

獨立 Windows Server 與兩個 Desktop Client 的 11 項驗收通過，詳見 [跨程序報告](VR_Lobby_PC_Multiplayer_Report.json)。Server 單獨啟動、未連線 Menu 與關閉 Client 的三項驗證詳見 [獨立入口報告](Connection_Test_Server_Entry_Report.json)。最新 Windows 建置為 0 errors、189 warnings。

[兩位企鵝的 Editor 實測畫面](ConnectionFlowEvidence/penguin_peers.png)

VR 與 Desktop 混合同步已加入現有羊模型的頭手 IK，無頭盔時的 Context Menu 連線步驟與驗收請見 [VR 混合連線說明](VR_Mixed_Connection_Guide.md)。
