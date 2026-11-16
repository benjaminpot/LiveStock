using System;
using System.Collections.Generic;
using System.Linq;
using Fusion;
using Oculus.Interaction;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Validates this feature's authored references and renders the existing status UI for inspection.</summary>
public static class ValidateFruitGameplay
{
    public static object Run()
    {
        var checks = new List<object>();
        foreach (string path in new[] { "Assets/_Project/Prefabs/Game Objects/NetworkApple.prefab",
            "Assets/_Project/Prefabs/Multiplayer/ConnectionTestClient.prefab",
            "Assets/_Project/Prefabs/Multiplayer/[BuildingBlock] Camera Rig Variant.prefab" })
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            int missing = root.GetComponentsInChildren<Transform>(true).Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
            if (missing != 0) throw new InvalidOperationException(path + " contains missing scripts.");
            checks.Add(new { path, missing });
        }
        var apple = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Game Objects/NetworkApple.prefab");
        var fruit = apple.GetComponent<FruitPickup>();
        if (!apple.GetComponent<NetworkObject>().NetworkedBehaviours.Contains(fruit)) throw new InvalidOperationException("Fruit behaviour is not baked.");
        var fruitFields = new SerializedObject(fruit);
        var trigger = fruitFields.FindProperty("proximityTrigger").objectReferenceValue as SphereCollider;
        var surface = fruitFields.FindProperty("rayCollider").objectReferenceValue as Collider;
        if (trigger == null || !trigger.isTrigger || surface == null || surface.isTrigger || surface == trigger)
            throw new InvalidOperationException("Apple proximity and ray surfaces must remain separate.");
        var client = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Multiplayer/ConnectionTestClient.prefab");
        var fields = new SerializedObject(client.GetComponent<PlayerGameplay>());
        var status = fields.FindProperty("stateText").objectReferenceValue as TMP_Text;
        if (status == null || status.name != "state" || fields.FindProperty("body").objectReferenceValue == null
            || fields.FindProperty("appleIcons").arraySize != 3) throw new InvalidOperationException("Player UI/body bindings are incomplete.");
        var rig = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Multiplayer/[BuildingBlock] Camera Rig Variant.prefab");
        var rays = rig.GetComponentsInChildren<FruitRayInteractor>(true);
        if (rays.Length != 2) throw new InvalidOperationException("Both controller rays must be connected.");
        foreach (var ray in rays)
        {
            var rayFields = new SerializedObject(ray);
            var interactor = rayFields.FindProperty("rayInteractor").objectReferenceValue;
            var filters = new SerializedObject(interactor).FindProperty("_interactableFilters");
            if (rayFields.FindProperty("selector").objectReferenceValue == null
                || !Enumerable.Range(0, filters.arraySize).Any(i => filters.GetArrayElementAtIndex(i).objectReferenceValue == ray))
                throw new InvalidOperationException("Controller ray filter/selector not connected.");
        }
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project/Fonts/GameplayStatus.asset");
        uint[] missingGlyphs;
        if (!font.HasCharacters("等待遊戲開始請靠近蘋果按扳機收集完成！0/3", out missingGlyphs, false, false)
            || font.atlasPopulationMode != AtlasPopulationMode.Static || font.sourceFontFile != null)
            throw new InvalidOperationException("Status font must contain Chinese with a static atlas.");
        var result = new { pass = true, prefabs = checks, controllerRays = rays.Length, appleIcons = 3,
            separateTrigger = true, baked = true, chineseGlyphs = true, staticFont = true };
        System.IO.File.WriteAllText("Docs/Fruit_Gameplay_Asset_Report.json", Newtonsoft.Json.JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented));
        return result;
    }

    public static object CaptureUI()
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Multiplayer/[BuildingBlock] Camera Rig Variant.prefab");
        var authored = asset.GetComponentsInChildren<Canvas>(true).Single(c => c.name == "Player_UI");
        var scene = EditorSceneManager.NewPreviewScene();
        RenderTexture target = null;
        Texture2D pixels = null;
        var previousTarget = RenderTexture.active;
        try
        {
            var ui = UnityEngine.Object.Instantiate(authored.gameObject);
            SceneManager.MoveGameObjectToScene(ui, scene);
            ui.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            ui.transform.localScale = Vector3.one;
            ui.SetActive(true);
            var text = ui.GetComponentsInChildren<TMP_Text>(true).Single(t => t.name == "state");
            text.text = "按扳機收集蘋果";
            text.ForceMeshUpdate(true, true);
            Canvas.ForceUpdateCanvases();
            var bounds = new Bounds(ui.transform.position, Vector3.zero);
            var corners = new Vector3[4];
            foreach (var graphic in ui.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
            {
                graphic.rectTransform.GetWorldCorners(corners);
                foreach (var point in corners) bounds.Encapsulate(point);
            }
            var cameraRoot = new GameObject("Status UI Preview");
            SceneManager.MoveGameObjectToScene(cameraRoot, scene);
            var camera = cameraRoot.AddComponent<Camera>();
            camera.scene = scene;
            camera.orthographic = true;
            camera.orthographicSize = Mathf.Max(bounds.size.y * .6f, bounds.size.x * .3f);
            camera.transform.position = bounds.center - Vector3.forward * 1000;
            camera.farClipPlane = 2000;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.07f, .08f, .10f);
            target = new RenderTexture(1024, 512, 24);
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            pixels = new Texture2D(1024, 512, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, 1024, 512), 0, 0);
            pixels.Apply();
            const string path = ".tmp/FruitGameplay/Player_UI_preview.png";
            System.IO.File.WriteAllBytes(path, pixels.EncodeToPNG());
            return new { path, text = text.text, font = text.font.name };
        }
        finally
        {
            RenderTexture.active = previousTarget;
            if (target != null) UnityEngine.Object.DestroyImmediate(target);
            if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
