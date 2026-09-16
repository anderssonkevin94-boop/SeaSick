using UnityEngine;
using SeaSick.Ship;
using SeaSick.Ocean;
public static class AdventureBrigCheck
{
 public static string Main()
 {
  var yard=Object.FindAnyObjectByType<Shipyard>();var art=yard.GetComponentInChildren<AdventureBrigVisual>();
  if(!art)throw new System.Exception("Approved brig absent");
  var probes=yard.GetComponent<BuoyancyProbeSet>();if(probes.Count!=14)throw new System.Exception("Buoyancy rig changed");
  var rig=yard.GetComponent<SailRig>();if(rig.SailCount!=4)throw new System.Exception("Sail assemblies missing "+rig.SailCount);
  var lids=yard.GetComponent<PortLids>();if(lids.LidCount!=14)throw new System.Exception("Gunport lids missing "+lids.LidCount);
  var camera=new GameObject("Brig review camera").AddComponent<Camera>();camera.CopyFrom(Camera.main);camera.enabled=false;camera.fieldOfView=48;
  camera.transform.position=yard.transform.TransformPoint(new Vector3(-32,20,38));camera.transform.LookAt(yard.transform.TransformPoint(new Vector3(0,7,0)));
  var rt=new RenderTexture(1600,1000,24);var tex=new Texture2D(1600,1000,TextureFormat.RGB24,false);var prev=RenderTexture.active;
  try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1600,1000),0,0);tex.Apply();System.IO.File.WriteAllBytes("docs/art-direction/ship-concept/brig-unity.png",tex.EncodeToPNG());}
  finally{RenderTexture.active=prev;camera.targetTexture=null;rt.Release();Object.Destroy(rt);Object.Destroy(tex);Object.Destroy(camera.gameObject);}
  return "Brig present; 14 buoyancy probes, 4 rotating sail assemblies, 14 hinged ports. Mass "+yard.GetComponent<Rigidbody>().mass+". Crew "+yard.Crew+"; guns per side "+yard.Guns;
 }
}
