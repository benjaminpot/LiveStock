using System;
using System.Collections;
using System.Collections.Generic;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LiveStock.Multiplayer
{
    /// <summary>網路同步邏輯階段；實際場景由角色決定，不依賴兩份 Build 的場景 index。</summary>
    public sealed class RoleNetworkSceneManager : NetworkSceneManagerDefault
    {
        public const string SceneFolder = "Assets/_Project/Scenes/_ALPHA_DEMO/";
        public static SceneRef LobbyRef => SceneRef.FromPath("LiveStock/Stages/Lobby");
        public static SceneRef MainGameRef => SceneRef.FromPath("LiveStock/Stages/MainGame");
        readonly List<SceneRef> loaded = new();
        Scene bootstrap;
        public string LocalSceneName { get; private set; }
        public bool MapLoaded => LocalSceneName == "MainGame_server" || LocalSceneName == "MainGame_VRClient";

        public override void Initialize(NetworkRunner runner)
        {
            bootstrap = SceneManager.GetActiveScene();
            base.Initialize(runner);
        }

        public string LocalPath(SceneRef stage)
        {
            if (stage != LobbyRef && stage != MainGameRef)
                throw new InvalidOperationException("Unknown LiveStock network stage: " + stage);
            return SceneFolder + (stage == LobbyRef ? "Lobby" : "MainGame")
                + (Runner.IsServer ? "_server" : "_VRClient") + ".unity";
        }

        public override SceneRef GetSceneRef(string path)
        {
            if (path == SceneFolder + "Lobby_server.unity" || path == SceneFolder + "Lobby_VRClient.unity") return LobbyRef;
            if (path == SceneFolder + "MainGame_server.unity" || path == SceneFolder + "MainGame_VRClient.unity") return MainGameRef;
            return base.GetSceneRef(path);
        }

        protected override IEnumerator LoadSceneCoroutine(SceneRef stage, NetworkLoadSceneParameters parameters)
        {
            string path = LocalPath(stage);
            if (SceneUtility.GetBuildIndexByScenePath(path) < 0)
                throw new InvalidOperationException("Role scene missing from this build: " + path);
            Runner.InvokeSceneLoadStart(stage);
            Scene scene;
            using (MakeLoadingScope())
            {
                bool multiple = Runner.Config.PeerMode == NetworkProjectConfig.PeerModes.Multiple;
                // 單一進程 server 可直接接管已開啟的 Lobby；多 peer 必須各自載入副本。
                scene = !multiple && loaded.Count == 0 && bootstrap.IsValid() && bootstrap.path == path ? bootstrap : default;
                if (!scene.IsValid())
                {
                    // 先載入新場景，再卸載舊場景，避免最後一個場景被卸載及過渡空畫面。
                    var op = SceneManager.LoadSceneAsync(path, new LoadSceneParameters(LoadSceneMode.Additive));
                    if (op == null) throw new InvalidOperationException("Cannot load " + path);
                    scene = SceneManager.GetSceneAt(SceneManager.sceneCount - 1);
                    while (!op.isDone) { OnLoadSceneProgress(stage, op.progress); yield return null; }
                }
                MarkSceneAsOwned(stage, scene);
                if (parameters.LoadSceneMode == LoadSceneMode.Single)
                {
                    foreach (var previous in loaded.ToArray()) yield return base.UnloadSceneCoroutine(previous);
                    loaded.Clear();
                }
                if (!multiple && bootstrap.IsValid() && bootstrap.isLoaded && bootstrap != scene)
                    yield return SceneManager.UnloadSceneAsync(bootstrap);
                bootstrap = default;
                LocalSceneName = scene.name;
                loaded.Add(stage);
            }
            // 保留 SDK 的物理場景隔離、相同邏輯 SceneRef 登記及載入完成通知。
            yield return base.OnSceneLoaded(stage, scene, parameters);
        }

        protected override IEnumerator UnloadSceneCoroutine(SceneRef stage)
        {
            yield return base.UnloadSceneCoroutine(stage);
            loaded.Remove(stage);
        }
    }
}
