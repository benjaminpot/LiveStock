# GDC_METAVERSE 多人開發工具

給 **Unity + Photon Fusion 2** 多人專案用的小工具組。不綁定特定遊戲，匯入 `GDC_METAVERSE.unitypackage` 即可使用，所有功能都在選單 **GDC_METAVERSE** 底下。

## 需求

| 項目 | 版本 |
|---|---|
| Unity | 6000.0 以上 |
| Photon Fusion | 2.1 以上（需已匯入並設定 App ID） |
| Photon Voice for Fusion | 選用，只有「語音」功能需要 |

匯入後會產生兩個組件：`GDC.Metaverse`（執行時）與 `GDC.Metaverse.Editor`（編輯器）。專案程式不需修改即可使用。

---

## 1. Make Networked：把場景物件變成多人物件

把擺在場景裡的物件，改成「由 Host 在進場時生成、所有人看到同一份」的網路物件。

**選單**

- **GDC_METAVERSE → Multiplayer → Make Selected Networked**：只轉換選取的物件。
- **GDC_METAVERSE → Multiplayer → Make All Instances Of Selected Networked**：轉換場景中同一個 Prefab 的全部實例（例如 20 個箱子一次轉完）。

**它會做的事**

1. 在 `Assets/Prefabs/Networked/` 建立 `Network<名稱>` 網路 Prefab：原本是 Prefab 的做成 Variant，一般場景物件則存成新 Prefab。已存在就沿用。
2. 加上 `NetworkObject` 與同步元件（預設 Fusion 的 `NetworkTransform`），並註冊到 Fusion 的 Prefab 清單。
3. 在場景的 **Network Spawner** 新增生成點（位置、方向沿用原物件），然後移除原物件。

可以用 **Undo** 復原；完成後記得**存檔場景**。也可以在 Project 視窗選 Prefab 執行，只會建立網路 Prefab，不動場景。

**自訂（選用）**：在專案的 Editor 程式裡改輸出資料夾，或為特定物件指定同步元件：

```csharp
[UnityEditor.InitializeOnLoad]
static class MyNetworkRules
{
    static MyNetworkRules()
    {
        GDC.Metaverse.Editor.MakeNetworkedTool.OutputFolder = "Assets/Prefabs/Multiplayer";
        GDC.Metaverse.Editor.MakeNetworkedTool.Rules.Add((go => go.GetComponent<MyPickup>() != null, typeof(MyNetworkPickup)));
    }
}
```

## 2. Network Spawner：進場時生成網路物件

場景元件（**Add Component → GDC_METAVERSE → Network Spawner**），列出「哪個網路 Prefab、生成在哪些點」。通常由 Make Networked 自動建立；要調整位置就移動底下的生成點，要多生成一個就複製生成點並加入對應 Group 的 **Points**。

**需要在你的連線程式呼叫兩行**（Host 才會生效，Client 自動收到）：

```csharp
public void OnSceneLoadDone(NetworkRunner runner)  => GDC.Metaverse.NetworkSpawner.SpawnFor(runner);
public void OnSceneLoadStart(NetworkRunner runner) => GDC.Metaverse.NetworkSpawner.DespawnFor(runner);
```

支援 Fusion Multi-Peer（每個 Runner 只生成自己場景裡的那一份）。

## 3. Peer Mode：在同一個 Editor 測試多位玩家

- **GDC_METAVERSE → Peer Mode → Multiple (test players in one Editor)**：同一個 Editor 可同時跑多個 Runner（多位玩家），方便測試。
- **GDC_METAVERSE → Peer Mode → Single (devices, builds)**：一般模式，實機與建置用。

選單會打勾顯示目前模式。Peer Mode 只影響本機，所以 Single 與 Multiple 的程式可以加入同一個房間。Multi-Peer 的注意事項請見 [Photon 官方文件](https://doc.photonengine.com/fusion/v2/manual/testing-and-tooling/multipeer)。

## 4. Build（Single Peer）

- **GDC_METAVERSE → Build → Build Active Target (Single Peer)**：用目前平台與 Build Settings 中啟用的場景建置，輸出到 `Builds/<平台>/`。建置時自動切成 Single，結束後恢復原本的 Peer Mode。
- 若用 Unity 原本的 Build 且 Peer Mode 是 Multiple，Console 會跳出警告。
- 自訂建置可呼叫：`GDC.Metaverse.Editor.PeerModeTools.Build(outputPath, BuildTarget.StandaloneWindows64);`

## 5. Voice：一行設定語音（需要 Photon Voice for Fusion）

1. 在 Photon Dashboard 建立 **Voice** 應用，App ID 填入 Fusion 的 `PhotonAppSettings` → **App Id Voice**。
2. 選取玩家的網路 Prefab，執行 **GDC_METAVERSE → Voice → Add Voice To Selected Network Prefab**。
3. 建立 NetworkRunner 之後、`StartGame` 之前呼叫：

```csharp
var runner = gameObject.AddComponent<NetworkRunner>();
GDC.Metaverse.FusionVoiceSetup.Configure(gameObject, transmit: true, playVoices: true);
```

`transmit`：這位玩家的麥克風要不要送出；`playVoices`：這位玩家聽不聽得到別人。可用來做「有人只能說、有人只能聽」的玩法。不需要在場景放任何語音物件。

---

## 疑難排解

| 狀況 | 處理 |
|---|---|
| 生成的物件只有 Host 看得到 | 確認網路 Prefab 有標籤 `FusionPrefab`（Make Networked 會自動加），並重新匯入 `NetworkProjectConfig` |
| Client 看不到 Network Spawner 生成的物件 | 只有 Host 會生成；確認有在 `OnSceneLoadDone` 呼叫 `SpawnFor` |
| 沒有語音 | 確認 App Id Voice 已填、玩家 Prefab 有 `VoiceNetworkObject`、`Configure` 在 `StartGame` 之前呼叫 |
| 建置後連不上 | 確認建置是 Single Peer，且各裝置的 Fusion App ID、Region、App Version 相同 |
