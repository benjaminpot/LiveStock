using System;
using System.Linq;
using Fusion;
using Fusion.Editor;
using Oculus.Interaction;
using Oculus.Interaction.Locomotion;
using Oculus.Interaction.Surfaces;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;

/// <summary>Wires existing apples, local rigs and Player_UI without rebuilding their hierarchy.</summary>
public static class ConfigureFruitGameplay
{
    const string Apple = "Assets/_Project/Prefabs/Game Objects/NetworkApple.prefab";
    const string Rig = "Assets/_Project/Prefabs/Multiplayer/[BuildingBlock] Camera Rig Variant.prefab";
    const string Client = "Assets/_Project/Prefabs/Multiplayer/ConnectionTestClient.prefab";

    static void Set(UnityEngine.Object target, string name, UnityEngine.Object value)
    {
        var fields = new SerializedObject(target);
        fields.FindProperty(name).objectReferenceValue = value;
        fields.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.RecordPrefabInstancePropertyModifications(target);
    }

    static T Add<T>(GameObject go) where T : Component => go.GetComponent<T>() ?? go.AddComponent<T>();

    static void EditPrefab(string path, Action<GameObject> edit)
    {
        var root = PrefabUtility.LoadPrefabContents(path);
        try { edit(root); PrefabUtility.SaveAsPrefabAsset(root, path); }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    static void WireRig(GameObject root)
    {
        var locomotor = root.GetComponentsInChildren<FirstPersonLocomotor>(true).Single();
        var bridge = Add<VRLocomotionActions>(locomotor.gameObject);
        Set(bridge, "locomotor", locomotor);
        foreach (var wrapper in root.GetComponentsInChildren<ActiveStateUnityEventWrapper>(true))
        {
            bool crouch = wrapper.name == "Crouch";
            if (!crouch && wrapper.name != "Run") continue;
            UnityAction<bool> action = crouch ? bridge.SetCrouching : bridge.SetRunning;
            foreach (var e in new[] { wrapper.WhenActivated, wrapper.WhenDeactivated })
                for (int i = e.GetPersistentEventCount() - 1; i >= 0; i--)
                    if (new[] { "Run", "Walk", "ToggleCrouch", "SetRunning", "SetCrouching" }.Contains(e.GetPersistentMethodName(i)))
                        UnityEventTools.RemovePersistentListener(e, i);
            UnityEventTools.AddBoolPersistentListener(wrapper.WhenActivated, action, true);
            UnityEventTools.AddBoolPersistentListener(wrapper.WhenDeactivated, action, false);
            EditorUtility.SetDirty(wrapper);
            PrefabUtility.RecordPrefabInstancePropertyModifications(wrapper);
        }
        int count = 0;
        foreach (var ray in root.GetComponentsInChildren<RayInteractor>(true))
        {
            var fields = new SerializedObject(ray);
            var selector = fields.FindProperty("_selector").objectReferenceValue as ControllerSelector;
            if (selector == null) continue;
            var interaction = Add<FruitRayInteractor>(ray.gameObject);
            Set(interaction, "rayInteractor", ray);
            Set(interaction, "selector", selector);
            var filters = fields.FindProperty("_interactableFilters");
            bool exists = Enumerable.Range(0, filters.arraySize).Any(i => filters.GetArrayElementAtIndex(i).objectReferenceValue == interaction);
            if (!exists) filters.GetArrayElementAtIndex(filters.arraySize++).objectReferenceValue = interaction;
            fields.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.RecordPrefabInstancePropertyModifications(ray);
            count++;
        }
        if (count == 0) throw new InvalidOperationException("No controller RayInteractor was found on the authored rig.");
        foreach (var graphic in root.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
            if (graphic.GetComponentsInParent<Transform>(true).Any(t => t.name == "Player_UI"))
            {
                graphic.raycastTarget = false;
                EditorUtility.SetDirty(graphic);
                PrefabUtility.RecordPrefabInstancePropertyModifications(graphic);
            }
    }

    static void WireClient(ConnectionManager owner)
    {
        var ownerFields = new SerializedObject(owner);
        var rig = (GameObject)ownerFields.FindProperty("localVRRig").objectReferenceValue;
        var gameplay = Add<PlayerGameplay>(owner.gameObject);
        var state = rig.GetComponentsInChildren<TMP_Text>(true).Single(t => t.name == "state");
        var locomotor = rig.GetComponentsInChildren<FirstPersonLocomotor>(true).Single();
        var character = new SerializedObject(locomotor).FindProperty("_characterController").objectReferenceValue;
        var capsule = new SerializedObject(character).FindProperty("_capsule").objectReferenceValue;
        Set(gameplay, "stateText", state);
        Set(gameplay, "body", capsule);
        var statusFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project/Fonts/GameplayStatus.asset");
        var desktopStatus = ownerFields.FindProperty("sessionStatusText").objectReferenceValue as TMP_Text;
        if (statusFont != null && desktopStatus != null)
        {
            desktopStatus.font = statusFont;
            desktopStatus.fontSharedMaterial = statusFont.material;
            PrefabUtility.RecordPrefabInstancePropertyModifications(desktopStatus);
        }
        var fields = new SerializedObject(gameplay);
        var icons = rig.GetComponentsInChildren<UnityEngine.UI.Image>(true).Where(i => i.name.StartsWith("Apple", StringComparison.Ordinal)).OrderBy(i => i.name).ToArray();
        var array = fields.FindProperty("appleIcons");
        array.arraySize = icons.Length;
        for (int i = 0; i < icons.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = icons[i];
        fields.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.RecordPrefabInstancePropertyModifications(gameplay);
        state.text = "等待遊戲開始";
        PrefabUtility.RecordPrefabInstancePropertyModifications(state);
    }

    public static object Inspect()
    {
        var stage = PrefabStageUtility.GetCurrentPrefabStage();
        return new { scenes = Enumerable.Range(0, UnityEngine.SceneManagement.SceneManager.sceneCount).Select(i => {
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i); return new { scene.path, scene.isDirty }; }).ToArray(),
            prefabStage = stage != null ? stage.assetPath : null };
    }

    /// <summary>Bakes only the gameplay message characters; the CJK source stays editor-only.</summary>
    public static object ConfigureFont()
    {
        AssetDatabase.Refresh();
        const string path = "Assets/_Project/Fonts/GameplayStatus.asset";
        string messages = new string(Enumerable.Range(32, 95).Select(i => (char)i).ToArray())
            + "等待遊戲開始玩家尚未準備完成追蹤資料請靠近蘋果這顆已被收集重新瞄準伺服器確認按扳機…！　";
        var source = AssetDatabase.LoadAssetAtPath<Font>("Assets/_Project/Fonts/Editor/NotoSansCJKtc-Regular.otf");
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (font == null)
        {
            font = TMP_FontAsset.CreateFontAsset(source, 45, 5, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,
                1024, 1024, AtlasPopulationMode.Dynamic, false);
            font.name = "GameplayStatus";
            if (!font.TryAddCharacters(messages, out string missing)) throw new InvalidOperationException("Missing glyphs: " + missing);
            font.atlasPopulationMode = AtlasPopulationMode.Static;
            AssetDatabase.CreateAsset(font, path);
            AssetDatabase.AddObjectToAsset(font.material, font);
            foreach (var atlas in font.atlasTextures) AssetDatabase.AddObjectToAsset(atlas, font);
        }
        else
        {
            font.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            Set(font, "m_SourceFontFile", source);
            if (!font.TryAddCharacters(messages, out string missing)) throw new InvalidOperationException("Missing glyphs: " + missing);
            font.atlasPopulationMode = AtlasPopulationMode.Static;
            EditorUtility.SetDirty(font);
            foreach (var atlas in font.atlasTextures) EditorUtility.SetDirty(atlas);
        }
        EditPrefab(Rig, root => {
            var text = root.GetComponentsInChildren<TMP_Text>(true).Single(t => t.name == "state");
            text.font = font;
            text.fontSharedMaterial = font.material;
            PrefabUtility.RecordPrefabInstancePropertyModifications(text);
        });
        EditPrefab(Client, root => WireClient(root.GetComponent<ConnectionManager>()));
        var setup = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            var scene = EditorSceneManager.OpenScene(NetworkSceneSettings.Current.MenuScene);
            foreach (var owner in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<ConnectionManager>(true))) WireClient(owner);
            EditorSceneManager.SaveScene(scene);
        }
        finally { EditorSceneManager.RestoreSceneManagerSetup(setup); }
        AssetDatabase.SaveAssets();
        uint[] missingGlyphs;
        bool coversMessages = font.HasCharacters(messages, out missingGlyphs, false, false);
        if (!coversMessages) throw new InvalidOperationException("Static atlas has missing glyphs.");
        return new { path, coversMessages, glyphs = font.characterTable.Count, atlas = "1024x1024 static", sourceIncluded = font.sourceFontFile != null };
    }

    public static object Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before configuring assets.");
        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save open scene changes before configuring assets.");
        var setup = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            EditPrefab(Apple, root => {
                var fruit = root.GetComponent<FruitPickup>();
                var spheres = root.GetComponentsInChildren<SphereCollider>(true);
                var trigger = spheres.Single(c => c.isTrigger);
                var solid = spheres.Single(c => !c.isTrigger);
                var surface = Add<ColliderSurface>(solid.gameObject);
                Set(surface, "_collider", solid);
                var ray = Add<RayInteractable>(root);
                Set(ray, "_surface", surface);
                Set(Add<PointableUnityEventWrapper>(root), "_pointable", ray);
                Set(fruit, "proximityTrigger", trigger);
                Set(fruit, "rayCollider", solid);
                Set(fruit, "rayInteractable", ray);
                new NetworkObjectBakerEditTime().Bake(root);
            });
            EditPrefab(Rig, WireRig);
            EditPrefab(Client, root => WireClient(root.GetComponent<ConnectionManager>()));
            EditPrefab("Assets/_Project/Prefabs/Multiplayer/LobbySessionState.prefab", root => new NetworkObjectBakerEditTime().Bake(root));
            var scene = EditorSceneManager.OpenScene(NetworkSceneSettings.Current.MenuScene);
            foreach (var owner in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<ConnectionManager>(true))) WireClient(owner);
            EditorSceneManager.SaveScene(scene);
            NetworkProjectConfigUtilities.RebuildPrefabTable();
            AssetDatabase.SaveAssets();
        }
        finally { EditorSceneManager.RestoreSceneManagerSetup(setup); }
        return new { apple = Apple, rig = Rig, ui = "Player_UI/state", configured = true };
    }
}
