using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using SeaSick.Steamer;
namespace SeaSick.Ship.Modular
{
    public static class CoasterVerification
    {
        public static string Check()
        {
            var lib=ModuleLibrary.LoadFromResources();var log=new StringBuilder();int count=0,fail=0;
            var reference=SteamerBootstrap.ReferenceData();var baseline=ShipyardPlanner.PlanFor(ShipConfiguration.Long(),lib,reference,null,out _);
            for(int n=0;n<=3;n++)for(int mask=0;mask<(1<<(n+2));mask++)
            {
                var c=new ShipConfiguration{sternId=CoasterFamily.Hull("stern",(mask&1)!=0),bowId=CoasterFamily.Hull("bow",(mask&(1<<(n+1)))!=0)};
                for(int j=0;j<n;j++)c.middleIds.Add(CoasterFamily.Hull("middle",(mask&(1<<(j+1)))!=0));CoasterFamily.Wheel(c);
                var bare=ShipAssembler.Assemble(c,lib);
                foreach(var slot in bare.slots)if(slot.role==SocketRole.DeckSlot)c.equipment.Add(new EquipmentChoice{slotId=slot.qualifiedId,moduleId=ShipConfiguration.EquipmentCannon});
                var plan=ShipyardPlanner.PlanFor(c,lib,reference,baseline,out var a);
                bool ok=a.ok&&plan!=null&&plan.hydro.Ok&&plan.massMissing==null&&plan.capacityMissing==null&&plan.capacity.gunSlots==c.equipment.Count;
                var decoded=ModularSave.Decode(c.ToJson(),lib,out bool has,out string warning);
                ok&=has&&warning==null&&c.ValueEquals(decoded);
                if(!ok){fail++;log.AppendLine($"FAIL n={n} mask={mask}: {a.Summary()}, hydro={plan?.hydro.missing}, mass={plan?.massMissing}, capacity={plan?.capacityMissing}, save={warning}");}
                count++;
            }
            log.AppendLine($"F coaster: {count-fail}/{count} assembly + full battery + save round trips passed.");
            Directory.CreateDirectory("art-staging/f-coaster-runtime");File.WriteAllText("art-staging/f-coaster-runtime/verification.txt",log.ToString());return log.ToString();
        }
        public static string Routes()
        {
            var lib=ModuleLibrary.LoadFromResources();var reference=SteamerBootstrap.ReferenceData();
            var baseline=ShipyardPlanner.PlanFor(ShipConfiguration.Long(),lib,reference,null,out _);
            var log=new StringBuilder();int total=0,failed=0;
            for(int n=0;n<=3;n++)for(int mask=0;mask<(1<<(n+2));mask++)
            {
                var c=new ShipConfiguration{sternId=CoasterFamily.Hull("stern",(mask&1)!=0),bowId=CoasterFamily.Hull("bow",(mask&(1<<(n+1)))!=0)};
                for(int j=0;j<n;j++)c.middleIds.Add(CoasterFamily.Hull("middle",(mask&(1<<(j+1)))!=0));CoasterFamily.Wheel(c);
                foreach(var slot in ShipAssembler.Assemble(c,lib).slots)if(slot.role==SocketRole.DeckSlot)c.equipment.Add(new EquipmentChoice{slotId=slot.qualifiedId,moduleId=ShipConfiguration.EquipmentCannon});
                var plan=ShipyardPlanner.PlanFor(c,lib,reference,baseline,out var a);
                var ship=new GameObject("Route verification");var drawing=new GameObject("Drawing");drawing.transform.SetParent(ship.transform,false);drawing.transform.localPosition=plan.viewOffset;
                var view=drawing.AddComponent<ModularShipView>();view.Build(a);
                var guns=new System.Collections.Generic.List<Cannon>();
                foreach(var station in plan.fittedGuns){var g=new GameObject("Test gun");g.transform.SetParent(ship.transform,false);g.transform.localPosition=station.positionM;guns.Add(g.AddComponent<Cannon>());}
                var nav=drawing.AddComponent<CoasterNavigation>();nav.Build(ship.transform,plan,guns.ToArray());
                foreach(var gun in guns)
                {
                    var at=gun.transform.localPosition;at.x-=Mathf.Sign(at.x)*.95f;
                    total++;if(!nav.HasRoute(plan.data.helm,at)){failed++;log.AppendLine($"NO ROUTE n={n} mask={mask} helm={plan.data.helm} gunner={at}");}
                }
                if(view.missingParts.Count>0){failed++;log.AppendLine("Missing geometry");}
                UnityEngine.Object.DestroyImmediate(ship);
            }
            log.AppendLine($"Gun station routes: {total-failed}/{total} passed.");
            File.WriteAllText("art-staging/f-coaster-runtime/routes.txt",log.ToString());return log.ToString();
        }
        /// **Walk-route cache check** (2026-10-01): a walker sent to the same target
        /// twice, the second time from somewhere else, must follow the deck graph
        /// (not a straight line through a reserved cargo box), while repeated calls
        /// of one walk reuse the cached route. Builds the default coaster with a
        /// reserved box dead centre between two deck points and logs both walks.
        public static string WalkCache()
        {
            var lib=ModuleLibrary.LoadFromResources();var reference=SteamerBootstrap.ReferenceData();
            var baseline=ShipyardPlanner.PlanFor(ShipConfiguration.Long(),lib,reference,null,out _);
            var c=CoasterFamily.Default();var plan=ShipyardPlanner.PlanFor(c,lib,reference,baseline,out var a);
            var ship=new GameObject("WalkCache ship");var drawing=new GameObject("Drawing");drawing.transform.SetParent(ship.transform,false);drawing.transform.localPosition=plan.viewOffset;
            drawing.AddComponent<ModularShipView>().Build(a);
            var nav=drawing.AddComponent<CoasterNavigation>();
            // Probe two deck points on the centre line, then reserve a box between them.
            float z0=plan.viewOffset.z+3.5f,z1=plan.viewOffset.z+11f;
            var start=nav.ClosestWalkable(new Vector3(0,.9f,z0));var goal=nav.ClosestWalkable(new Vector3(0,.9f,z1));
            nav.Build(ship.transform,plan,new Cannon[0]);start=nav.ClosestWalkable(new Vector3(0,.9f,z0));goal=nav.ClosestWalkable(new Vector3(0,.9f,z1));
            var mid=(start+goal)*.5f;var box=new Bounds(mid+new Vector3(0,.4f,0),new Vector3(1.0f,.8f,1.4f));
            nav.Build(ship.transform,plan,new Cannon[0],new[]{box});
            start=nav.ClosestWalkable(start);goal=nav.ClosestWalkable(goal);
            var exp=box;exp.Expand(new Vector3(.3f,0,.3f));
            var log=new StringBuilder($"nodes={nav.NodeCount} start={start} goal={goal} box={box.center}/{box.size} hasRoute={nav.HasRoute(start,goal)}\n");
            var w=new GameObject("Walker").transform;w.SetParent(ship.transform,false);
            int Walk(Vector3 from,string label,out int inside,out float longest)
            {
                w.localPosition=from;inside=0;longest=0;int n=0;var prev=from;
                while(n<800&&!nav.Move(w,goal,.1f)){n++;if(exp.Contains(new Vector3(w.localPosition.x,box.center.y,w.localPosition.z)))inside++;}
                log.AppendLine($"{label}: {n} steps, end={w.localPosition}, steps inside reserved box={inside}");return n;
            }
            int s1=Walk(start,"walk 1 (fresh)",out int in1,out _);
            // Same target again from the far side of the box, as after being moved / interrupted.
            var other=nav.ClosestWalkable(new Vector3(0,.9f,z0-.3f));
            // Straight line from `other` to `goal` crosses the box?
            bool straightCrosses=false;for(int i=0;i<=100;i++){var p=Vector3.Lerp(other,goal,i/100f);if(exp.Contains(new Vector3(p.x,box.center.y,p.z)))straightCrosses=true;}
            int s2=Walk(other,"walk 2 (same target, moved start)",out int in2,out _);
            // Same walker, same target, still progressing: no rebuild (route object kept).
            w.localPosition=other;nav.Move(w,goal,.1f);var before=typeof(CoasterNavigation).GetField("routes",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(nav);
            var d=(System.Collections.IDictionary)before;var r1=d[w.GetInstanceID()];nav.Move(w,goal,.1f);nav.Move(w,goal,.1f);bool kept=ReferenceEquals(r1,d[w.GetInstanceID()]);
            log.AppendLine($"straight line from moved start crosses box: {straightCrosses}; route object reused across progressing calls: {kept}");
            bool ok=in1==0&&in2==0&&kept&&s2>0;
            log.AppendLine(ok?"PASS":"FAIL");
            UnityEngine.Object.DestroyImmediate(ship);
            File.WriteAllText("art-staging/f-coaster-runtime/walkcache.txt",log.ToString());return log.ToString();
        }
        public static string Render(bool allRaised=false)
        {
            var c=CoasterFamily.Default();if(allRaised){c.middleIds[0]=CoasterFamily.Hull("middle",true);c.bowId=CoasterFamily.Hull("bow",true);}
            var lib=ModuleLibrary.LoadFromResources();var a=ShipAssembler.Assemble(c,lib);
            var go=new GameObject("Coaster verification preview");go.transform.position=new Vector3(0,1000,0);var view=go.AddComponent<ModularShipView>();view.Build(a);
            foreach(var t in go.GetComponentsInChildren<Transform>())t.gameObject.layer=31;
            var camGo=new GameObject("Coaster camera");var cam=camGo.AddComponent<Camera>();cam.enabled=false;cam.cullingMask=1<<31;cam.orthographic=true;cam.orthographicSize=7.8f;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.28f,.32f,.33f);cam.farClipPlane=100;
            var target=go.transform.position+new Vector3(0,1.1f,5);cam.transform.position=target+new Vector3(11,10,-15);cam.transform.LookAt(target);
            var lightGo=new GameObject("Coaster soft key");var light=lightGo.AddComponent<Light>();light.type=LightType.Directional;light.color=new Color(1,.86f,.69f);light.intensity=1.25f;light.shadows=LightShadows.Soft;light.shadowStrength=.75f;light.cullingMask=1<<31;lightGo.transform.rotation=Quaternion.Euler(48,-30,0);
            var rt=new RenderTexture(1600,1200,24){antiAliasing=4};cam.allowMSAA=true;cam.targetTexture=rt;cam.Render();var previous=RenderTexture.active;RenderTexture.active=rt;var tex=new Texture2D(1600,1200,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1600,1200),0,0);tex.Apply();
            string file="art-staging/f-coaster-runtime/unity-"+(allRaised?"raised":"base")+".png";File.WriteAllBytes(file,tex.EncodeToPNG());RenderTexture.active=previous;cam.targetTexture=null;
            UnityEngine.Object.DestroyImmediate(tex);UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(camGo);UnityEngine.Object.DestroyImmediate(lightGo);UnityEngine.Object.DestroyImmediate(go);return Path.GetFullPath(file);
        }
        public static string PreparePlay()
        {
            SeaSick.Save.SaveSlots.EditorTestDirectory="/tmp/seasick-coaster-playtest";Directory.CreateDirectory(SeaSick.Save.SaveSlots.EditorTestDirectory);
            PlayerPrefs.SetInt(SteamerBootstrap.PrefKey,1);return "Scratch saves configured; steamer selected.";
        }
        public static string PlayReport()
        {
            var yard=ShipyardService.Player;if(yard==null)return "FAIL no live shipyard";
            var nav=yard.GetComponentInChildren<CoasterNavigation>();var rb=yard.GetComponent<Rigidbody>();var data=yard.ActiveData;
            var log=$"Coaster={CoasterFamily.Is(yard.Current)} modular={yard.ModularActive} guns={yard.GetComponent<CannonBattery>()?.TotalGuns} navNodes={nav?.NodeCount} ladders={nav?.LadderLinks} position={yard.transform.position} speed={rb.linearVelocity.magnitude:F2} wheelRadius={data.wheelRadius} helm={data.helm} missingParts={yard.GetComponentInChildren<ModularShipView>()?.missingParts.Count}";
            File.WriteAllText("art-staging/f-coaster-runtime/play-report.txt",log);return log;
        }
    }
}
