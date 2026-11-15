using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>Reads authored CCTV viewpoints without enabling cameras or saving the map.</summary>
public static class AuditCctvSources
{
    /// <summary>Reports authored viewpoints and renders the first source into a temporary diagnostic image.</summary>
    public static object Run()
    {
        var scene=EditorSceneManager.OpenPreviewScene(NetworkSceneSettings.Current.MainGameScene);
        try
        {
            var switcher=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<CCTVViewSwitcher>(true)).Single();
            var views=new SerializedObject(switcher).FindProperty("views");
            var result=Enumerable.Range(0,views.arraySize).Select(i=>{
                var camera=(Camera)views.GetArrayElementAtIndex(i).FindPropertyRelative("streamCamera").objectReferenceValue;
                var data=camera.GetComponent<UniversalAdditionalCameraData>();
                return new{index=i,camera=camera.name,mask=camera.cullingMask,position=camera.transform.position.ToString("F3"),
                    forward=camera.transform.forward.ToString("F3"),rect=camera.rect.ToString(),camera.nearClipPlane,camera.farClipPlane,
                    renderType=data!=null?data.renderType.ToString():"Default"};
            }).ToArray();
            System.IO.File.WriteAllText(".tmp/FusionAcceptance/CctvSources.json",Newtonsoft.Json.JsonConvert.SerializeObject(result,Newtonsoft.Json.Formatting.Indented));
            var camera0=(Camera)views.GetArrayElementAtIndex(0).FindPropertyRelative("streamCamera").objectReferenceValue;
            var target=new RenderTexture(512,288,24);
            var pixels=new Texture2D(512,288,TextureFormat.RGB24,false);
            var previous=RenderTexture.active;
            try
            {
                camera0.scene=scene;
                camera0.targetTexture=target;
                camera0.Render();
                RenderTexture.active=target;
                pixels.ReadPixels(new Rect(0,0,512,288),0,0);
                pixels.Apply();
                System.IO.File.WriteAllBytes(".tmp/FusionAcceptance/CctvSourcePreview.png",pixels.EncodeToPNG());
            }
            finally
            {
                camera0.targetTexture=null;
                RenderTexture.active=previous;
                Object.DestroyImmediate(pixels);
                Object.DestroyImmediate(target);
            }
            return result;
        }
        finally{EditorSceneManager.ClosePreviewScene(scene);}
    }
}
