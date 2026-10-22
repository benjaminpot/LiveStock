using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Fusion;
using Fusion.Editor;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace GDC.Metaverse.Editor
{
    /// <summary>
    /// Fusion Peer Mode helpers. Multiple: several runners (players) in one Editor for testing. Single: one runner per
    /// process, for real devices and builds. Peer Mode is local only, so Single and Multiple processes can share a room.
    /// </summary>
    public static class PeerModeTools
    {
        private const string SingleMenu = "GDC_METAVERSE/Peer Mode/Single (devices, builds)";
        private const string MultipleMenu = "GDC_METAVERSE/Peer Mode/Multiple (test players in one Editor)";

        public static NetworkProjectConfig.PeerModes Current => NetworkProjectConfig.Global.PeerMode;

        [MenuItem(SingleMenu, priority = 40)]
        private static void SetSingle() => SetPeerMode(NetworkProjectConfig.PeerModes.Single);

        [MenuItem(MultipleMenu, priority = 41)]
        private static void SetMultiple() => SetPeerMode(NetworkProjectConfig.PeerModes.Multiple);

        [MenuItem(SingleMenu, true)]
        private static bool ValidateSingle() => Validate(NetworkProjectConfig.PeerModes.Single, SingleMenu);

        [MenuItem(MultipleMenu, true)]
        private static bool ValidateMultiple() => Validate(NetworkProjectConfig.PeerModes.Multiple, MultipleMenu);

        private static bool Validate(NetworkProjectConfig.PeerModes mode, string menu)
        {
            Menu.SetChecked(menu, Current == mode);
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        /// <summary>Writes Peer Mode into the global NetworkProjectConfig (.fusion) and reimports it.</summary>
        public static void SetPeerMode(NetworkProjectConfig.PeerModes mode)
        {
            string path = FusionGlobalScriptableObjectUtils.GetGlobalAssetPath<NetworkProjectConfigAsset>();
            if (string.IsNullOrEmpty(path)) throw new InvalidOperationException("No NetworkProjectConfig found. Install Fusion first.");
            string text = File.ReadAllText(path);
            string updated = Regex.Replace(text, "\"PeerMode\"\\s*:\\s*\\d+", $"\"PeerMode\": {(int)mode}");
            if (updated == text) return;
            File.WriteAllText(path, updated);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            Debug.Log($"[GDC_METAVERSE] Fusion Peer Mode = {mode}.");
        }

        [MenuItem("GDC_METAVERSE/Build/Build Active Target (Single Peer)", priority = 60)]
        private static void BuildActiveTarget()
        {
            var target = EditorUserBuildSettings.activeBuildTarget;
            string extension = target is BuildTarget.StandaloneWindows or BuildTarget.StandaloneWindows64 ? ".exe"
                : target == BuildTarget.Android ? ".apk" : "";
            Build($"Builds/{target}/{PlayerSettings.productName}{extension}", target);
        }

        /// <summary>
        /// Builds the enabled Build Settings scenes with Peer Mode temporarily set to Single, then restores the Editor's
        /// Peer Mode. Returns true on success.
        /// </summary>
        public static bool Build(string outputPath, BuildTarget target, string[] scenes = null, BuildOptions options = BuildOptions.None)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before building.");
            scenes ??= EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0) throw new InvalidOperationException("No enabled scenes in Build Settings.");
            var previous = Current;
            try
            {
                SetPeerMode(NetworkProjectConfig.PeerModes.Single);
                var summary = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = outputPath,
                    target = target,
                    targetGroup = BuildPipeline.GetBuildTargetGroup(target),
                    options = options,
                }).summary;
                if (summary.result != BuildResult.Succeeded)
                {
                    Debug.LogError($"[GDC_METAVERSE] Build {summary.result}: {summary.totalErrors} error(s). See the Console.");
                    return false;
                }
                Debug.Log($"[GDC_METAVERSE] Build done: {Path.GetFullPath(outputPath)} ({summary.totalSize / (1024 * 1024)} MB, {summary.totalTime:mm\\:ss}).");
                EditorUtility.RevealInFinder(outputPath);
                return true;
            }
            finally { SetPeerMode(previous); }
        }
    }

    /// <summary>Warns when a build would use Multi-Peer (meant for Editor testing only).</summary>
    internal sealed class MultiPeerBuildCheck : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (PeerModeTools.Current == NetworkProjectConfig.PeerModes.Multiple)
                Debug.LogWarning("[GDC_METAVERSE] Building with Fusion Peer Mode = Multiple. Device builds should use Single " +
                    "(GDC_METAVERSE > Peer Mode, or GDC_METAVERSE > Build > Build Active Target (Single Peer)).");
        }
    }
}
