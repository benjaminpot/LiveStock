using System;
using System.IO;
using System.Linq;
using Fusion;
using Fusion.Editor;
using GDC.Metaverse.Editor;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine.XR.Management;

/// <summary>Builds the same Windows Development Player for local Server/Client acceptance.</summary>
public static class BuildFusionValidation
{
    const string ReloadPending = "LiveStock.ValidationBuildReloadPending";
    const string ReloadMarker = ".tmp/FusionAcceptance/BuildDomainReload.txt";

    /// <summary>Requests a clean domain without loading a temporary eval assembly before SDK build callbacks.</summary>
    [MenuItem("LiveStock/Connection Test/Prepare Fusion Validation Build")]
    public static void PrepareBuild()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play Mode before building.");
        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Save modified scenes before building.");
        SessionState.SetBool(ReloadPending, true);
        UnityEngine.Debug.Log("[FusionValidationBuild] Requesting clean domain reload.");
        EditorUtility.RequestScriptReload();
    }

    /// <summary>Confirms the requested domain reload actually reached initialization before a build starts.</summary>
    [InitializeOnLoadMethod]
    static void RecordBuildReload()
    {
        if (!SessionState.GetBool(ReloadPending, false)) return;
        SessionState.EraseBool(ReloadPending);
        Directory.CreateDirectory(Path.GetDirectoryName(ReloadMarker));
        File.WriteAllText(ReloadMarker, DateTime.UtcNow.ToString("O"));
        UnityEngine.Debug.Log("[FusionValidationBuild] Clean domain initialized.");
    }

    [MenuItem("LiveStock/Connection Test/Build Fusion Validation Player")]
    public static void BuildFromMenu() => Run();

    public static object Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play Mode before building.");
        var settings = XRGeneralSettings.Instance;
        bool previousXR = settings != null && settings.InitManagerOnStart;
        var previousMode = PeerModeTools.Current;
        try
        {
            if (settings != null)
            {
                settings.InitManagerOnStart = false;
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssetIfDirty(settings);
            }
            PeerModeTools.SetPeerMode(NetworkProjectConfig.PeerModes.Single);
            const string output = "Builds/FusionValidation/LiveStockFusion.exe";
            Directory.CreateDirectory("Builds/FusionValidation");
            const string bindings = "Builds/FusionValidation/RuntimeActionBindings.json";
            if (File.Exists(bindings)) File.Delete(bindings);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development,
                extraScriptingDefines = new[] {"FUSION_ACCEPTANCE"}
            });
            var summary = report.summary;
            var result = new
            {
                result = summary.result.ToString(), summary.totalErrors, summary.totalWarnings,
                size = summary.totalSize, seconds = summary.totalTime.TotalSeconds, output,
                errors = report.steps.SelectMany(s => s.messages)
                    .Where(m => m.type == UnityEngine.LogType.Error || m.type == UnityEngine.LogType.Exception)
                    .Select(m => m.content).ToArray()
            };
            File.WriteAllText("Docs/Fusion_Sync_Build_Report.json",
                Newtonsoft.Json.JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented));
            if (summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Validation build failed. Read the report.");
            return result;
        }
        finally
        {
            PeerModeTools.SetPeerMode(previousMode);
            if (settings != null)
            {
                settings.InitManagerOnStart = previousXR;
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssetIfDirty(settings);
            }
        }
    }
}
