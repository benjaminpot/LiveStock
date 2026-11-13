# Recycle Bin

這裡收納目前新版連線流程已取代的資產。2026-11-09 先收納 LegacyMultiplayer。

資產透過 Unity AssetDatabase.MoveAsset 搬移，保留原 GUID。Scripts 仍參與 Unity 編譯，Scripts/Editor 中的工具仍可在 Editor 使用。舊場景與 ServerOnly 等元件仍有依賴，後續會隨 Fusion 重構逐步解除。

LegacyMultiplayer 包含六支舊 Runtime 腳本、四支 Editor 腳本，以及六個舊連線與顯示 Prefab。

Menu_VRClient 場景及 ConnectionTestClient Prefab 各自有一個停用的 Recycle Bin 群組，收納原先已停用的 SessionLauncher、SessionPanel、MultiplayerDemo 物件。

MainGame 的停用 Recycle Bin 群組收納原有的牛、羊本機 NPC Preview。正式測試由 Network NPCs 在 Server 分批生成 60 隻，方便核對各端數量。

完整搬移對照與 GUID 位於專案根目錄的 `Docs/Legacy_Connection_Archive_Report.json` 與 `Docs/Legacy_Pose_Archive_Report.json`。同步架構位於 `Docs/Fusion_Network_Optimization_Plan.md`，操作驗收位於 `Docs/Fusion_Synchronization_Acceptance_Guide.md`。

恢復資產時，在 Unity Project 視窗搬回原路徑，並同步調整 MultiplayerSetup 中兩個 Prefab 的字串路徑。恢復舊場景入口時，依原用途檢查物件和元件啟用狀態。
