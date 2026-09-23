using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SeaSick.Dev
{
    public static class AstraPlaytestImport
    {
        const string Art = "Assets/_Project/Art/AstraPlaytest";
        const string Runtime = "Assets/_Project/Resources/AstraPlaytest";
        const string Request = "art-staging/astra-playtest-import.request";
        const string Report = "art-staging/astra-playtest-import-report.txt";
        static readonly List<string> log = new List<string>();

        [InitializeOnLoadMethod]
        static void Schedule() { EditorApplication.delayCall += RunRequested; }

        static void RunRequested()
        {
            if (!File.Exists(Request)) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
            { EditorApplication.delayCall += RunRequested; return; }
            File.Delete(Request);
            Execute();
        }

        [MenuItem("SeaSick/Art/Import latest Astra playtest assets")]
        public static void Execute()
        {
            log.Clear();
            try
            {
                Directory.CreateDirectory(Runtime);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                var cloth = MaterialAt("Astra_Cloth", "SeaSick/Crew Vertex Color", Color.white);
                var skin = MaterialAt("Astra_Skin", "SeaSick/Crew Vertex Color", new Color(.851f,.573f,.349f));
                var wood = MaterialAt("Astra_Building", "SeaSick/Environment Toon", Color.white);
                var hull = MaterialAt("Astra_Ship", "SeaSick/Fleet Vertex Color", Color.white);
                foreach (var f in Directory.GetFiles(Art, "*.fbx", SearchOption.AllDirectories))
                    Import(f.Replace('\\','/'), f.Contains("/Crew/") ? cloth : f.Contains("/Ship/") ? hull : wood, skin);
                BuildCrew();
                BuildSawmill();
                BuildShip();
                AssetDatabase.SaveAssets();
                Validate();
                RenderReviews();
                log.Add("SUCCESS: prefab and runtime imports complete; no scene file was saved or replaced.");
                Debug.Log("[AstraPlaytest] " + string.Join("\n",log));
            }
            catch (Exception e)
            {
                log.Add("FAILED: " + e);
                Debug.LogException(e);
            }
            File.WriteAllLines(Report,log);
        }

        static Material MaterialAt(string name, string shaderName, Color color)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null) throw new Exception("Missing shader " + shaderName);
            var path = Art + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m,path); }
            m.shader = shader; m.SetColor("_BaseColor",color);m.enableInstancing=true;
            EditorUtility.SetDirty(m);return m;
        }

        static void Import(string path, Material material, Material skin)
        {
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) throw new Exception("No model importer " + path);
            importer.globalScale=1;importer.useFileScale=true;
            importer.importNormals=ModelImporterNormals.Import;
            importer.importCameras=false;importer.importLights=false;importer.importAnimation=false;
            importer.isReadable=true;importer.optimizeGameObjects=false;
            importer.animationType=path.Contains("/Crew/") ? ModelImporterAnimationType.Generic : ModelImporterAnimationType.None;
            importer.materialImportMode=ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation=ModelImporterMaterialLocation.InPrefab;
            importer.SaveAndReimport();
            var model=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            foreach(var r in model.GetComponentsInChildren<Renderer>(true))
                foreach(var m in r.sharedMaterials)
                    if(m != null)
                        importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material),m.name),
                            path.Contains("/Crew/") && (r.name.Contains("Skin") || m.name.IndexOf("skin",StringComparison.OrdinalIgnoreCase)>=0) ? skin : material);
            importer.SaveAndReimport();
        }

        static GameObject Model(string path, Transform parent)
        {
            var asset=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if(asset == null)throw new Exception("Missing " + path);
            var go=(GameObject)PrefabUtility.InstantiatePrefab(asset);
            go.transform.SetParent(parent,false);
            PrefabUtility.UnpackPrefabInstance(go,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
            return go;
        }

        static Bounds BoundsOf(GameObject go)
        {
            var renderers=go.GetComponentsInChildren<Renderer>(true);
            if(renderers.Length==0)throw new Exception("No renderers " + go.name);
            var b=renderers[0].bounds;foreach(var r in renderers)b.Encapsulate(r.bounds);return b;
        }

        static void BuildCrew()
        {
            const string path="Assets/_Project/Prefabs/CrewMember.prefab";
            Backup(path);
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var old=root.transform.Find("Visual");if(old != null)Object.DestroyImmediate(old.gameObject);
                var vis=Model(Art+"/Crew/Deckhand.fbx",root.transform);vis.name="Visual";
                var size=BoundsOf(vis);
                if(size.size.y < 1 || size.size.y > 3)throw new Exception("Crew import units wrong: " + size);
                vis.transform.localScale=Vector3.one*(1.7f/size.size.y);
                vis.transform.localPosition=Vector3.up*(-size.min.y*vis.transform.localScale.y);
                var anim=vis.GetComponent<Animator>();if(anim==null)anim=vis.AddComponent<Animator>();
                var idle=MakeClip(vis,"Idle",false);var walk=MakeClip(vis,"Walk",true);
                string controllerPath=Runtime+"/CrewAnimator.controller";
                var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
                if(controller==null)controller=AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
                foreach(var layer in controller.layers)
                    foreach(var state in layer.stateMachine.states)layer.stateMachine.RemoveState(state.state);
                controller.parameters=new[]{new AnimatorControllerParameter{name="Speed",type=AnimatorControllerParameterType.Float}};
                var sm=controller.layers[0].stateMachine;
                var a=sm.AddState("Idle");a.motion=idle;var b=sm.AddState("Walk");b.motion=walk;sm.defaultState=a;
                var to=b.AddTransition(a);to.hasExitTime=false;to.duration=.15f;to.AddCondition(AnimatorConditionMode.Less,.18f,"Speed");
                to=a.AddTransition(b);to.hasExitTime=false;to.duration=.1f;to.AddCondition(AnimatorConditionMode.Greater,.30f,"Speed");
                anim.runtimeAnimatorController=controller;anim.applyRootMotion=false;
                anim.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                var agent=root.GetComponent<Crew.CrewAgent>();var so=new SerializedObject(agent);
                so.FindProperty("animator").objectReferenceValue=anim;
                var skin=vis.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r=>r.name.Contains("Skin")).ToArray();
                if(skin.Length!=1)throw new Exception("Crew skin renderer contract failed");
                var tr=so.FindProperty("tintRenderers");tr.arraySize=skin.Length;
                for(int i=0;i<skin.Length;i++)tr.GetArrayElementAtIndex(i).objectReferenceValue=skin[i];
                so.FindProperty("healthyTint").colorValue=new Color(.851f,.573f,.349f);
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(vis,Runtime+"/DeckhandVisual.prefab");
                PrefabUtility.SaveAsPrefabAsset(root,path);
                log.Add("Crew: latest hatless mesh, 1.7 m, skin-only tint, new skeleton-bound idle/walk clips.");
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
        }

        static AnimationClip MakeClip(GameObject vis,string name,bool walking)
        {
            string path=Runtime+"/Crew_"+name+".anim";
            var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if(clip==null){clip=new AnimationClip();AssetDatabase.CreateAsset(clip,path);}
            clip.ClearCurves();clip.frameRate=30;
            var bones=vis.GetComponentsInChildren<SkinnedMeshRenderer>().SelectMany(r=>r.bones).Distinct().ToArray();
            if(!bones.Any(t=>t.name=="thigh.L"))throw new Exception("Expected new crew skeleton was not imported");
            foreach(var bone in bones)
            {
                string binding=AnimationUtility.CalculateTransformPath(bone,vis.transform);
                var qcurves=Enumerable.Range(0,4).Select(_=>new AnimationCurve()).ToArray();
                var pcurves=Enumerable.Range(0,3).Select(_=>new AnimationCurve()).ToArray();
                var rest=bone.localRotation;var parent=Quaternion.Inverse(vis.transform.rotation)*bone.parent.rotation;
                for(int i=0;i<=32;i++)
                {
                    float t=i/32f,sign=bone.name.EndsWith(".L")?1f:-1f;
                    float wave=Mathf.Sin(t*Mathf.PI*2)*sign,pitch=0,roll=0;
                    if(bone.name.StartsWith("upper_arm"))
                    {roll=-Mathf.Sign(vis.transform.InverseTransformPoint(bone.position).x)*24;pitch=walking?-wave*16:wave*1.2f;}
                    if(bone.name.StartsWith("forearm"))pitch=-8;
                    if(walking && bone.name.StartsWith("thigh"))pitch=wave*23;
                    if(walking && bone.name.StartsWith("shin"))pitch=-Mathf.Max(0,-wave)*28;
                    if(walking && bone.name.StartsWith("foot"))pitch=-wave*10+Mathf.Max(0,-wave)*14;
                    var q=Quaternion.Inverse(parent)*Quaternion.Euler(pitch,0,roll)*parent*rest;
                    var p=bone.localPosition;
                    for(int c=0;c<4;c++)qcurves[c].AddKey(t,q[c]);
                    for(int c=0;c<3;c++)pcurves[c].AddKey(t,p[c]);
                }
                for(int c=0;c<4;c++)clip.SetCurve(binding,typeof(Transform),"m_LocalRotation."+"xyzw"[c],qcurves[c]);
                for(int c=0;c<3;c++)clip.SetCurve(binding,typeof(Transform),"m_LocalPosition."+"xyz"[c],pcurves[c]);
            }
            clip.EnsureQuaternionContinuity();
            var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=true;
            AnimationUtility.SetAnimationClipSettings(clip,settings);EditorUtility.SetDirty(clip);return clip;
        }

        static void BuildSawmill()
        {
            var go=new GameObject("sawmill");
            try
            {
                var model=Model(Art+"/Sawmill/Sawmill.fbx",go.transform);
                foreach(var t in model.GetComponentsInChildren<Transform>(true))
                    if(t.name.StartsWith("Input_Log_") || t.name.StartsWith("Output_Plank_") ||
                        t.name.StartsWith("Bench_") && t.name!="Bench_Anchor" || t.name=="Saw_Tool")t.gameObject.SetActive(false);
                var b=BoundsOf(go);
                if(b.size.x>7.56f || b.size.z>5.85f || b.size.y>3.84f)throw new Exception("Sawmill footprint mismatch " + b);
                Backup("Assets/_Project/Resources/Settlement/sawmill.prefab");
                PrefabUtility.SaveAsPrefabAsset(go,"Assets/_Project/Resources/Settlement/sawmill.prefab");
                log.Add("Sawmill: latest tarp only; hidden stock/work variants preserved for future per-building inventory integration.");
            }
            finally{Object.DestroyImmediate(go);}
        }

        static void BuildShip()
        {
            var root=new GameObject("AstraSteamerV8");
            try
            {
                var fleet=root.AddComponent<Ship.FleetVisual>();fleet.gunTemplates=new Transform[0];fleet.sailPivots=new Transform[0];
                var adapter=root.AddComponent<Ship.AstraSteamerVisual>();
                var body=new GameObject("Geometry").transform;body.SetParent(root.transform,false);adapter.geometry=body;
                var socket=new GameObject("WheelModuleSocket").transform;socket.SetParent(body,false);socket.localPosition=new Vector3(0,.35f,-10.83f);
                foreach(var path in Directory.GetFiles(Art+"/Ship","*.fbx"))
                {
                    string name=Path.GetFileNameWithoutExtension(path);
                    var model=Model(path,name.StartsWith("Paddle_")?socket:body);model.name=name;
                    if(name=="Paddle_Rotor")adapter.paddle=model.transform;
                }
                PrefabUtility.SaveAsPrefabAsset(root,Runtime+"/Steamer.prefab");
                log.Add("Ship: V8 geometry, independent paddle module; opt-in player visual adapter preserves ladder handling and existing guns.");
            }
            finally{Object.DestroyImmediate(root);}
        }

        static void Validate()
        {
            foreach(string path in new[]{Runtime+"/Steamer.prefab","Assets/_Project/Resources/Settlement/sawmill.prefab","Assets/_Project/Prefabs/CrewMember.prefab"})
            {
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if(prefab==null)throw new Exception("Prefab did not save: "+path);
                foreach(var r in prefab.GetComponentsInChildren<Renderer>(true))
                    if(r.sharedMaterials.Any(m=>m==null || m.shader==null || !m.shader.isSupported))throw new Exception("Invalid material: "+r.name);
            }
            var crew=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/CrewMember.prefab"));
            try
            {
                var visual=crew.transform.Find("Visual").gameObject;
                var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(Runtime+"/Crew_Walk.anim");
                var shin=visual.GetComponentsInChildren<Transform>().First(t=>t.name=="shin.L");
                clip.SampleAnimation(visual,0);var before=shin.position;
                clip.SampleAnimation(visual,.25f);float movement=Vector3.Distance(before,shin.position);
                if(movement<.01f)throw new Exception("Walk clip did not reach the new bones");
                log.Add("Verified walk binding; knee displacement "+movement.ToString("F3")+" m.");
            }
            finally{Object.DestroyImmediate(crew);}
        }

        static void Backup(string path)
        {
            string folder="art-staging/astra-playtest-backup";Directory.CreateDirectory(folder);
            string file=folder+"/"+Path.GetFileName(path);
            if(!File.Exists(file))File.Copy(path,file);
        }

        static void RenderReviews()
        {
            var original=SceneManager.GetActiveScene();
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight=new Color(.55f,.58f,.62f);RenderSettings.fog=false;
                var sun=new GameObject("Review Sun").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.2f;
                sun.transform.rotation=Quaternion.Euler(48,-35,0);
                var camera=new GameObject("Review Camera").AddComponent<Camera>();camera.scene=scene;
                camera.orthographic=true;camera.clearFlags=CameraClearFlags.SolidColor;
                camera.backgroundColor=new Color(.37f,.49f,.55f);camera.nearClipPlane=.01f;camera.farClipPlane=300;
                foreach(string kind in new[]{"crew","sawmill","ship"})
                {
                    GameObject go;
                    if(kind=="ship")
                    {
                        go=new GameObject("Ship Review");go.AddComponent<Rigidbody>().isKinematic=true;
                        var existing=Resources.Load<Ship.FleetVisual>("Ships/FleetV3/Ship13");
                        if(existing==null)throw new Exception("Current fleet rung did not load");
                        var visual=Ship.AstraSteamerVisual.Build(go.transform,12,existing);
                        if(visual==null || visual.gunTemplates.Length!=existing.gunTemplates.Length)throw new Exception("Ship adapter failed");
                        foreach(var t in visual.gunTemplates)t.gameObject.SetActive(true);
                        log.Add("Ship runtime adapter: gun count preserved, deck at center "+visual.DeckHeight(0).ToString("F2")+" m.");
                    }
                    else
                    {
                        string path=kind=="crew"?"Assets/_Project/Prefabs/CrewMember.prefab":"Assets/_Project/Resources/Settlement/sawmill.prefab";
                        go=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                        if(kind=="crew")AssetDatabase.LoadAssetAtPath<AnimationClip>(Runtime+"/Crew_Idle.anim").SampleAnimation(go.transform.Find("Visual").gameObject,0);
                    }
                    go.transform.position=new Vector3(10000,0,10000);
                    var rs=go.GetComponentsInChildren<Renderer>();var bounds=rs[0].bounds;
                    foreach(var r in rs)bounds.Encapsulate(r.bounds);
                    camera.transform.position=bounds.center+new Vector3(1,1.0f,1.5f).normalized*Mathf.Max(5,bounds.size.magnitude*1.5f);
                    camera.transform.LookAt(bounds.center);
                    camera.orthographicSize=Mathf.Max(bounds.size.y*.65f,bounds.size.magnitude*.45f);
                    var rt=new RenderTexture(1200,900,24);var old=RenderTexture.active;
                    var tex=new Texture2D(1200,900,TextureFormat.RGB24,false);
                    try
                    {
                        camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
                        tex.ReadPixels(new Rect(0,0,1200,900),0,0);tex.Apply();
                        File.WriteAllBytes("art-staging/astra-playtest-"+kind+".png",tex.EncodeToPNG());
                        log.Add(kind+" Unity bounds "+bounds.size.ToString("F3"));
                    }
                    finally{camera.targetTexture=null;RenderTexture.active=old;Object.DestroyImmediate(tex);Object.DestroyImmediate(rt);Object.DestroyImmediate(go);}
                }
            }
            finally{SceneManager.SetActiveScene(original);EditorSceneManager.CloseScene(scene,true);}
        }
    }
}
