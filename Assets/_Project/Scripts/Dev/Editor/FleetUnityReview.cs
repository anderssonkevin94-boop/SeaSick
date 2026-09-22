using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using SeaSick.Ship;
namespace SeaSick.Dev
{
    public static class FleetUnityReview
    {
        const string Out="docs/art-direction/fleet-v3/unity";
        [MenuItem("SeaSick/Art/Render fleet desktop and portrait review")]
        public static void Execute()
        {
            if(Application.isPlaying)throw new Exception("Render review in edit mode.");
            Directory.CreateDirectory(Out);
            foreach(int stage in new[]{1,4,7,12,13,14,16,20})
            {
                var scene=EditorSceneManager.NewPreviewScene();
                try
                {
                    var prefab=AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Project/Resources/Ships/FleetV3/Ship{stage:00}.prefab");
                    var root=(GameObject)PrefabUtility.InstantiatePrefab(prefab,scene);
                    foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;
                    var renderers=root.GetComponentsInChildren<MeshRenderer>();var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
                    var light=new GameObject("Fleet review sun");SceneManager.MoveGameObjectToScene(light,scene);var sun=light.AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.2f;sun.cullingMask=1<<31;sun.transform.rotation=Quaternion.Euler(35,-35,0);
                    var cg=new GameObject("Fleet review camera");SceneManager.MoveGameObjectToScene(cg,scene);var cam=cg.AddComponent<Camera>();cam.scene=scene;cam.cullingMask=1<<31;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.12f,.27f,.34f);cam.orthographic=true;cam.farClipPlane=300;
                    cam.transform.position=bounds.center+new Vector3(-1,.65f,1).normalized*100;cam.transform.LookAt(bounds.center);
                    foreach(bool portrait in new[]{false,true})
                    {
                        int w=portrait?1080:1920,h=portrait?2340:1080;float ex=0,ey=0;
                        foreach(var r in renderers)for(int k=0;k<8;k++){var b=r.bounds;var p=b.center+Vector3.Scale(b.extents,new Vector3((k&1)==0?-1:1,(k&2)==0?-1:1,(k&4)==0?-1:1));p=cam.transform.InverseTransformPoint(p);ex=Mathf.Max(ex,Mathf.Abs(p.x));ey=Mathf.Max(ey,Mathf.Abs(p.y));}
                        cam.orthographicSize=Mathf.Max(ey,ex*h/w)*1.1f;var rt=new RenderTexture(w,h,24);cam.targetTexture=rt;cam.Render();var before=RenderTexture.active;RenderTexture.active=rt;var tex=new Texture2D(w,h,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,w,h),0,0);tex.Apply();File.WriteAllBytes($"{Out}/{stage:00}-{(portrait?"portrait":"desktop")}.png",tex.EncodeToPNG());RenderTexture.active=before;cam.targetTexture=null;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(tex);
                    }
                }
                finally{EditorSceneManager.ClosePreviewScene(scene);}
            }
            Debug.Log("[FleetV3] Unity review images rendered in desktop and portrait.");
        }
        public static void LiveShot()
        {
            if(!Application.isPlaying)throw new Exception("Live screenshot requires play mode.");
            var ship=GameObject.Find("PlayerShip");var yard=ship.GetComponent<Shipyard>();var cam=Camera.main;
            if(!cam || !ship.GetComponentInChildren<FleetVisual>())throw new Exception("Live fleet/camera missing.");
            Directory.CreateDirectory(Out);
            var report=$"Live stage {yard.NodeIndex+1}; position {ship.transform.position}; rotation {ship.transform.eulerAngles}; mass {yard.Load.TotalKg}; GM {yard.Load.GMm}; load sinkage {yard.Load.SinkageM}; cannons {ship.GetComponentsInChildren<Cannon>().Length}\n";
            var oldTarget=cam.targetTexture;float aspect=cam.aspect;var oldActive=RenderTexture.active;
            foreach(bool portrait in new[]{false,true})
            {
                int w=portrait?1080:1920,h=portrait?2340:1080;
                var rt=new RenderTexture(w,h,24);cam.targetTexture=rt;cam.aspect=(float)w/h;cam.Render();RenderTexture.active=rt;
                var tex=new Texture2D(w,h,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,w,h),0,0);tex.Apply();
                File.WriteAllBytes($"{Out}/live-{(portrait?"portrait":"desktop")}.png",tex.EncodeToPNG());
                cam.targetTexture=oldTarget;RenderTexture.active=oldActive;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(tex);
            }
            cam.aspect=aspect;File.WriteAllText(Out+"/live-scene.txt",report);
        }
        static GameObject test;static int index;static double next;static StringBuilder report;
        [MenuItem("SeaSick/Art/Test all fleet upgrades (Play mode)")]
        public static void PlayTest()
        {
            if(!Application.isPlaying)throw new Exception("Play test requires play mode.");
            if(test)throw new Exception("Fleet test already running.");
            test=new GameObject("Fleet integration validation");test.SetActive(false);test.transform.position=new Vector3(100000,0,100000);
            test.AddComponent<Rigidbody>().isKinematic=true;var yard=test.AddComponent<Shipyard>();
            var so=new SerializedObject(yard);so.FindProperty("applyOnStart").boolValue=false;so.ApplyModifiedPropertiesWithoutUndo();
            test.AddComponent<CannonBattery>().enabled=false;test.SetActive(true);index=0;report=new StringBuilder("Runtime fleet progression, real Shipyard.Apply and Cannon.Fire\n");next=EditorApplication.timeSinceStartup;
            EditorApplication.update+=TestTick;
        }
        static void TestTick()
        {
            if(EditorApplication.timeSinceStartup<next)return;next=EditorApplication.timeSinceStartup+.2;
            try
            {
                if(!Application.isPlaying)throw new Exception("Play mode stopped during fleet test.");
                var yard=test.GetComponent<Shipyard>();
                if(index>0)
                {
                    var guns=test.GetComponentsInChildren<Cannon>();var visual=test.GetComponentInChildren<FleetVisual>();var node=yard.Node;
                    if(!visual || visual.stage!=index || guns.Length!=node.ports_per_side*2)throw new Exception("Runtime gun/model mismatch " + index + " actual " + guns.Length);
                    foreach(var gun in guns){gun.Manned=true;if(!gun.Fire())throw new Exception("Gun could not fire at stage "+index);if(float.IsNaN(gun.MuzzlePoint.y))throw new Exception("Invalid muzzle");}
                    if(test.GetComponent<SailRig>().SailCount!=visual.sailPivots.Length)throw new Exception("Sail count mismatch");
                    report.AppendLine($"PASS {index:00}: {guns.Length} working guns; {visual.sailPivots.Length} sail pivots; collider {test.GetComponent<BoxCollider>().size}");
                }
                if(index==20)
                {
                    var live=GameObject.Find("PlayerShip");if(live && !live.GetComponentInChildren<FleetVisual>())throw new Exception("Live PlayerShip still uses old art");
                    report.AppendLine("PASS live PlayerShip uses approved fleet. Ocean code and active scene were not rewritten.");Finish(null);return;
                }
                yard.Apply(index++);
            }
            catch(Exception e){Finish(e);}
        }
        static void Finish(Exception error)
        {
            EditorApplication.update-=TestTick;if(test)UnityEngine.Object.Destroy(test);test=null;
            if(error!=null){report.AppendLine("FAILED "+error);Debug.LogException(error);}
            Directory.CreateDirectory(Out);File.WriteAllText(Out+"/runtime-validation.txt",report.ToString());Debug.Log("[FleetV3] runtime validation " +(error==null?"PASS":"FAILED"));
        }
    }
}
