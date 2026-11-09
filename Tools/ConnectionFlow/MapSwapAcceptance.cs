using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Temporarily redirects gameplay to a copied map to verify scene configuration portability.</summary>
public static class MapSwapAcceptance
{
    const string Temporary="Assets/_Project/Tests/MapSwapAcceptance.unity";
    const string Snapshot=".tmp/MapSwapSettings.json";
    public static object Prepare()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stop Play Mode first.");
        if(File.Exists(Snapshot))throw new InvalidOperationException("Restore previous map test first.");
        var settings=NetworkSceneSettings.Current;
        File.WriteAllText(Snapshot,Newtonsoft.Json.JsonConvert.SerializeObject(new{original=settings.MainGameScene,scenes=EditorBuildSettings.scenes.Select(s=>new{path=s.path,enabled=s.enabled}).ToArray()}));
        if(!AssetDatabase.CopyAsset(settings.MainGameScene,Temporary))throw new InvalidOperationException("Copy map failed.");
        EditorBuildSettings.scenes=EditorBuildSettings.scenes.Concat(new[]{new EditorBuildSettingsScene(Temporary,true)}).ToArray();
        settings.MainGameScene=Temporary;EditorUtility.SetDirty(settings);AssetDatabase.SaveAssetIfDirty(settings);
        return new{temporaryMap=Temporary,configuredScene=settings.MainGameScene};
    }
    public static object Restore()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stop Play Mode first.");
        var snapshot=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Snapshot));
        var settings=NetworkSceneSettings.Current;settings.MainGameScene=(string)snapshot["original"];
        EditorUtility.SetDirty(settings);AssetDatabase.SaveAssetIfDirty(settings);
        EditorBuildSettings.scenes=snapshot["scenes"].Select(s=>new EditorBuildSettingsScene((string)s["path"],(bool)s["enabled"])).ToArray();
        AssetDatabase.DeleteAsset(Temporary);File.Delete(Snapshot);
        return new{restored=settings.MainGameScene,temporaryRemoved=!File.Exists(Temporary)};
    }
}
