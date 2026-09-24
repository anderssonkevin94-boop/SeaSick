using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using SeaSick.Ship.Modular;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.ModularYard.EditorTools
{
    // Batch-only visual QA. Run on a disposable project copy, never the main scene.
    public static class ShipyardUiReview
    {
        static readonly Vector2Int[] sizes = { new Vector2Int(320,568), new Vector2Int(390,844), new Vector2Int(430,932), new Vector2Int(844,390) };
        static readonly StringBuilder report = new StringBuilder();
        static UIDocument document;
        static ShipyardScreen screen;
        static ShipyardDraft draft;
        static PanelSettings settings;
        static RenderTexture target;
        static int index, frames;
        static double deadline;
        static string output;
        static int failures;
        static bool connected;
        static ShipyardService service;

        public static void Run()
        {
            if(!Application.isBatchMode || !Directory.GetParent(Application.dataPath).Name.StartsWith("seasick-yard-ui-review",StringComparison.Ordinal))
                throw new InvalidOperationException("Run this review only in a disposable seasick-yard-ui-review project in batch mode.");
            output=Environment.GetEnvironmentVariable("SHIPYARD_REVIEW_OUTPUT") ?? "Logs/shipyard-ui-review";
            Directory.CreateDirectory(output);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            connected = Environment.GetEnvironmentVariable("SHIPYARD_REVIEW_LIVE") == "1";
            if (connected)
            {
                service = new GameObject("Isolated backend fixture").AddComponent<ShipyardService>();
                service.Bind(SeaSick.Steamer.SteamerBootstrap.ReferenceData(), null, null);
            }
            RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight=new Color(.6f,.62f,.65f);
            var light=new GameObject("Review sun").AddComponent<Light>();
            light.type=LightType.Directional; light.intensity=1.3f;
            light.transform.rotation=Quaternion.Euler(40,-35,0);
            deadline=EditorApplication.timeSinceStartup+90;
            Application.logMessageReceived+=Log;
            EditorApplication.update+=Tick;
            try { Next(); } catch(Exception e) { Finish(e); }
        }
        static void Log(string text,string stack,LogType type)
        {
            if(type==LogType.Error || type==LogType.Exception || text.Contains("USS") || text.Contains("Unknown pseudo"))
            { report.AppendLine(type+": "+text); failures++; }
        }
        static void Next()
        {
            Cleanup(); frames=0;
            var size=sizes[index];
            target=new RenderTexture(size.x,size.y,24); target.Create();
            settings=UnityEngine.Object.Instantiate(Resources.Load<PanelSettings>("UI/SheetPanel"));
            settings.targetTexture=target; settings.scaleMode=PanelScaleMode.ConstantPixelSize; settings.scale=1;
            document=new GameObject("Review document").AddComponent<UIDocument>(); document.panelSettings=settings;
            var root=document.rootVisualElement;
            root.style.backgroundColor=(Color)new Color32(22,43,57,255);
            root.style.paddingTop=index==0?20:index==3?0:44;
            root.style.paddingBottom=index==0 || index==3?0:34;
            root.style.paddingLeft=root.style.paddingRight=index==3?47:0;
            var bridge = connected ? new ShipyardLiveBridge() : null;
            draft=new ShipyardDraft(ModuleLibrary.LoadFromResources(),ShipConfiguration.Long(), bridge,
                bridge == null ? null : bridge.RemovalBlocker, bridge == null ? null : bridge.Allowed);
            screen=new ShipyardScreen(draft,()=>{},bridge); root.Add(screen);
            draft.AddMiddle();
            if(index==2) { draft.AddMiddle(); draft.ChooseWheel(ShipConfiguration.TimberRotor); }
        }
        static void Tick()
        {
            try
            {
                if(EditorApplication.timeSinceStartup>deadline) throw new Exception("UI review timed out");
                var utility=typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.UIElementsRuntimeUtility");
                utility?.GetMethod("UpdateRuntimePanels",BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public)?.Invoke(null,null);
                var panel=document.rootVisualElement.panel;
                if(panel==null || ++frames<20) return;
                var repaint=panel.GetType().GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
                    .FirstOrDefault(m=>m.Name=="Repaint" && m.GetParameters().Length==1 && m.GetParameters()[0].ParameterType==typeof(Event));
                if(repaint==null) throw new Exception("Runtime panel repaint method unavailable");
                repaint.Invoke(panel,new object[]{new Event { type=EventType.Repaint }});
                if(frames<25) return;
                Capture();
                if(index==0) CheckActions();
                if(++index==sizes.Length) Finish(null); else Next();
            }
            catch(Exception e) { Finish(e); }
        }
        static void Capture()
        {
            var size=sizes[index];
            report.AppendLine("VIEW "+size+" root="+screen.worldBound);
            foreach(var scroll in screen.Query<ScrollView>().ToList())
                foreach(var button in scroll.Query<Button>().ToList())
                {
                    if(button.text!="Timber" && button.text!="Reinforced") continue;
                    var br=button.worldBound;var clip=scroll.contentViewport.worldBound;
                    if(br.yMin<clip.yMin-.5f || br.yMax>clip.yMax+.5f)
                        throw new Exception("Wheel selector clipped by scroll viewport: "+br+" clip "+clip);
                }
            foreach(var b in screen.Query<Button>().ToList())
            {
                var r=b.worldBound;
                if(r.width<43.9f || r.height<43.9f) throw new Exception("Small touch target: "+b.text+" "+b.tooltip+" "+r);
                if(r.xMin<-.1f || r.xMax>size.x+.1f || r.yMin<-.1f || r.yMax>size.y+.1f)
                    throw new Exception("Button off screen: "+b.text+" "+b.tooltip+" "+r);
                for(var ancestor=b.parent;ancestor!=null;ancestor=ancestor.parent)
                    if((ancestor.ClassListContains("yard-viewport") || ancestor.ClassListContains("unity-scroll-view__content-viewport")) &&
                       (r.xMin<ancestor.worldBound.xMin-.5f || r.xMax>ancestor.worldBound.xMax+.5f ||
                        r.yMin<ancestor.worldBound.yMin-.5f || r.yMax>ancestor.worldBound.yMax+.5f))
                        throw new Exception("Clipped button: "+b.text+" "+b.tooltip+" by "+ancestor.name);
            }
            foreach(var l in screen.Query<Label>().ToList())
            {
                if(string.IsNullOrEmpty(l.text) || l.resolvedStyle.display==DisplayStyle.None) continue;
                if(l.resolvedStyle.whiteSpace!=WhiteSpace.NoWrap) continue;
                var measured=l.MeasureTextSize(l.text,0,VisualElement.MeasureMode.Undefined,0,VisualElement.MeasureMode.Undefined);
                if(measured.x>l.contentRect.width+2) throw new Exception("Clipped label: "+l.text+" needs "+measured.x+" has "+l.contentRect.width);
            }
            if(screen.Query<Button>().ToList().Single(b=>b.text=="Confirm refit").enabledSelf != connected)
                throw new Exception("Unexpected confirmation availability");
            if (connected)
            {
                var figure = service.Report(draft.Snapshot()).Figure("overallLength");
                if(!screen.Query<Label>().ToList().Any(l => l.text != null && l.text.Contains(figure.Format(figure.proposed))))
                    throw new Exception("Backend length missing from screen");
                if(!service.Current.ValueEquals(ShipConfiguration.Long())) throw new Exception("Preview changed the live baseline");
            }
            var old=RenderTexture.active; RenderTexture.active=target;
            var tex=new Texture2D(size.x,size.y,TextureFormat.RGB24,false);
            tex.ReadPixels(new Rect(0,0,size.x,size.y),0,0);tex.Apply();RenderTexture.active=old;
            var pixels=tex.GetPixels32();
            int distinct=pixels.Where((p,i)=>i%17==0).Select(p=>((int)p.r<<16)|((int)p.g<<8)|p.b).Distinct().Count();
            if(distinct<100) throw new Exception("Blank/flat UI capture: "+distinct+" colors");
            File.WriteAllBytes(Path.Combine(output,"shipyard-"+size.x+"x"+size.y+".png"),tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            report.AppendLine("PASS touch bounds, single-line labels, confirmation availability, nonblank pixels ("+distinct+") connected="+connected);
        }
        static void Cleanup()
        {
            screen?.Dispose();screen=null;
            if(document!=null) UnityEngine.Object.DestroyImmediate(document.gameObject);
            if(settings!=null) UnityEngine.Object.DestroyImmediate(settings);
            if(target!=null) {target.Release();UnityEngine.Object.DestroyImmediate(target);}
        }
        static void Submit(Button button)
        {
            // Exercise the actual Button Clickable binding without requiring an
            // OS input device/focused Game view in the batch editor.
            var simulate=typeof(Clickable).GetMethod("SimulateSingleClick",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
            if(simulate==null) throw new Exception("Clickable simulator unavailable");
            simulate.Invoke(button.clickable,new object[]{null,0});
        }
        static void CheckActions()
        {
            Submit(screen.Query<Button>().ToList().Single(b=>b.tooltip=="Undo last change"));
            if(draft.Count!=1) throw new Exception("UI undo action failed");
            Submit(screen.Query<Button>().ToList().Single(b=>b.tooltip=="Add middle section before bow"));
            if(draft.Count!=2) throw new Exception("UI add action failed");
            Submit(screen.Query<Button>().ToList().Single(b=>b.text=="Timber"));
            if(draft.Rotor!=ShipConfiguration.TimberRotor) throw new Exception("UI wheel action failed");
            Submit(screen.Query<Button>().ToList().Single(b=>b.text=="Cancel"));
            if(screen.parent!=null) throw new Exception("UI cancel did not close");
            if(Resources.FindObjectsOfTypeAll<Camera>().Any(c=>c.name=="Preview camera"))
                throw new Exception("Preview camera leaked after cancel");
            if(Resources.FindObjectsOfTypeAll<RenderTexture>().Any(t=>t.name=="Shipyard preview"))
                throw new Exception("Preview texture leaked after cancel");
            report.AppendLine("PASS simulated Button add/undo/wheel/cancel actions; preview camera and texture released");
        }
        static void Finish(Exception error)
        {
            EditorApplication.update-=Tick;Application.logMessageReceived-=Log;
            if(error!=null) { report.AppendLine(error.ToString());failures++; }
            Cleanup();report.AppendLine("Failures: "+failures);
            File.WriteAllText(Path.Combine(output,"review.txt"),report.ToString());
            Debug.Log(report.ToString());EditorApplication.Exit(failures==0?0:1);
        }
    }
}
