# 舊版連線測試工具

本目錄保存歷史工具原文，位於 Assets 外，C# 檔案不參與 Unity 專案編譯。

目前驗收入口為 `Tools/ConnectionFlow/Test-Fusion.ps1`，操作索引為同目錄 README.md。

14 支舊工具包含早期單階段開始、RPC 姿態、診斷建置、重現及一次性遷移工具。LocalConnectionTestTools.BeforeCleanup.cs.txt 保存移除舊建置選單前的 Editor 原文。

Docs 子目錄保存兩份舊操作指南，原 Docs 路徑保留新版入口指引。歷史 JSON 結果仍留在 Docs 供比對，檔名前綴 Fusion_P1、Fusion_Sync、VR_Lobby 等代表各自版本的測試範圍。

工具搬移前後的 SHA256 位於 `Docs/Fusion_Acceptance_Tool_Archive_Report.json`。舊工具與目前 AwaitingStart／Running 流程有差異，復原後需重新檢查 API 與引用路徑。
