using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
public static class FlowSingleReturn
{
 public static async Task<object> Run(){
 var before=UnityEngine.Object.FindFirstObjectByType<ConnectionManager>();before.ReturnToMenu();
 var end=DateTime.UtcNow.AddSeconds(30);ConnectionManager after=null;
 do{await Task.Delay(100);after=UnityEngine.Object.FindFirstObjectByType<ConnectionManager>();if(DateTime.UtcNow>end)throw new Exception("Standalone return did not reload menu");}while(after==null||after==before||!after.GetComponentsInChildren<Canvas>(true).Any(c=>c.name=="LogInMenu"&&c.gameObject.activeInHierarchy));
 await Task.Delay(250);
 var report=new{managers=UnityEngine.Object.FindObjectsByType<ConnectionManager>(FindObjectsSortMode.None).Length,activeRigs=UnityEngine.Object.FindObjectsByType<OVRCameraRig>(FindObjectsSortMode.None).Count(r=>r.gameObject.activeInHierarchy),audioListeners=UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Count(a=>a.isActiveAndEnabled),status=after.Status};
 System.IO.File.WriteAllText("Docs/VR_Lobby_Standalone_Return_Report.json",Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented));
 if(report.managers!=1 || report.activeRigs!=(FlowLaunchOptions.DesktopClient?0:1) || report.audioListeners!=1)throw new Exception("Return-to-menu lifecycle left unexpected local objects");
 return report;
 }
}
