using System;
using System.Linq;
using Fusion;
using Fusion.Editor;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using TMPro;
using LiveStock.Multiplayer;

/// <summary>Migrates current map bindings into a reusable prefab and splits Server buttons.</summary>
public static class ConfigureMapNetworkSetup
{
    public static object Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        for (int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++)
            if(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save modified scenes first.");
        const string settingsPath="Assets/Resources/NetworkSceneSettings.asset";
        var settings=AssetDatabase.LoadAssetAtPath<NetworkSceneSettings>(settingsPath);
        if(settings==null)
        {
            settings=ScriptableObject.CreateInstance<NetworkSceneSettings>();
            settings.LobbyScene=ServerConnectionManager.SceneFolder+"Lobby.unity";
            settings.MainGameScene=ServerConnectionManager.SceneFolder+"MainGame.unity";
            settings.MenuScene=ServerConnectionManager.SceneFolder+"Menu_VRClient.unity";
            settings.ServerScene=ServerConnectionManager.SceneFolder+"ServerBootstrap.unity";
            AssetDatabase.CreateAsset(settings,settingsPath);
        }
        var scene=EditorSceneManager.OpenScene(settings.MainGameScene);
        var marker=UnityEngine.Object.FindObjectsByType<FlowSceneMarker>(FindObjectsSortMode.None).Single();
        if(marker.GetComponent<MapNetworkSetup>()==null)
        {
            var root=new GameObject("MapNetworkSetup");
            var setup=root.AddComponent<MapNetworkSetup>();
            var newMarker=root.GetComponent<FlowSceneMarker>();
            newMarker.Stage=2; newMarker.VRSpawnPoint=marker.VRSpawnPoint;
            marker.VRSpawnPoint.SetParent(root.transform,true);
            UnityEngine.Object.DestroyImmediate(marker);
            var spawner=UnityEngine.Object.FindObjectsByType<RandomNPCSpawner>(FindObjectsSortMode.None).Single();
            spawner.transform.SetParent(root.transform,true);
            PrefabUtility.SaveAsPrefabAssetAndConnect(root,"Assets/_Project/Prefabs/Multiplayer/MapNetworkSetup.prefab",InteractionMode.AutomatedAction);
            var so=new SerializedObject(setup);
            var list=so.FindProperty("sceneObjects"); list.arraySize=2;
            for(int i=0;i<2;i++)
            {
                var entry=list.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("Prefab").objectReferenceValue=AssetDatabase.LoadAssetAtPath<NetworkObject>("Assets/_Project/Prefabs/Multiplayer/Examples/"+(i==0?"NetworkMovingBox":"NetworkSwitch")+".prefab");
                entry.FindPropertyRelative("Offset").vector3Value=new Vector3(i==0?-1.5f:1f,.75f,2f);
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.RecordPrefabInstancePropertyModifications(setup);
        }
        EditorSceneManager.SaveScene(scene);
        scene=EditorSceneManager.OpenScene(settings.ServerScene);
        var server=UnityEngine.Object.FindFirstObjectByType<ServerConnectionManager>();
        var fields=new SerializedObject(server);
        var start=(UnityEngine.UI.Button)fields.FindProperty("startGameButton").objectReferenceValue;
        var load=(UnityEngine.UI.Button)fields.FindProperty("loadMapButton").objectReferenceValue;
        if(load==null)
        {
            load=UnityEngine.Object.Instantiate(start,start.transform.parent);
            load.name="LoadMapButton";
            load.onClick=new UnityEngine.UI.Button.ButtonClickedEvent();
            UnityEventTools.AddPersistentListener(load.onClick,server.LoadMainGame);
            load.GetComponentInChildren<TMP_Text>().text="Load MainGame";
            ((RectTransform)load.transform).anchoredPosition=new Vector2(-165,-120);
            ((RectTransform)start.transform).anchoredPosition=new Vector2(165,-120);
            fields.FindProperty("loadMapButton").objectReferenceValue=load;
            fields.ApplyModifiedPropertiesWithoutUndo();
        }
        ((RectTransform)load.transform).anchoredPosition=new Vector2(0,-105);
        ((RectTransform)start.transform).anchoredPosition=new Vector2(0,-145);
        ((RectTransform)load.transform).sizeDelta=new Vector2(240,32);
        ((RectTransform)start.transform).sizeDelta=new Vector2(240,32);
        EditorSceneManager.SaveScene(scene);
        const string roomPath="Assets/_Project/Prefabs/Multiplayer/LobbySessionState.prefab";
        var room=PrefabUtility.LoadPrefabContents(roomPath);
        try {new NetworkObjectBakerEditTime().Bake(room);PrefabUtility.SaveAsPrefabAsset(room,roomPath);}
        finally {PrefabUtility.UnloadPrefabContents(room);}
        NetworkProjectConfigUtilities.RebuildPrefabTable();
        AssetDatabase.SaveAssets();
        return new{settings=settingsPath,mapPrefab="MapNetworkSetup.prefab",buttons=new[]{load.name,start.name}};
    }
}

