using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// A north-up paper chart over live sailing, not a modal sheet.
    public sealed class PaperSeaMap : VisualElement
    {
        readonly VisualElement drawing;
        readonly Label footer;
        public PaperSeaMap(Action close)
        {
            name = "live-paper-map"; style.position = Position.Absolute;
            style.backgroundColor = (Color)new Color32(235,224,191,255);
            style.borderLeftWidth=style.borderRightWidth=style.borderTopWidth=style.borderBottomWidth=3;
            style.borderLeftColor=style.borderRightColor=style.borderTopColor=style.borderBottomColor=(Color)new Color32(193,170,124,255);
            var title = new Label("SEA CHART     N ↑"); title.style.color=(Color)new Color32(57,72,69,255);
            title.style.position=Position.Absolute; title.style.left=14; title.style.top=12; title.style.fontSize=13; Add(title);
            var x=new Button(close){text="×",tooltip="Close chart"}; x.style.position=Position.Absolute;
            x.style.right=5; x.style.top=4; x.style.width=x.style.height=44; x.style.fontSize=25;
            x.style.backgroundColor=Color.clear; x.style.borderLeftWidth=x.style.borderRightWidth=x.style.borderTopWidth=x.style.borderBottomWidth=0; Add(x);
            drawing=new VisualElement { pickingMode=PickingMode.Ignore }; drawing.style.position=Position.Absolute;
            drawing.style.left=drawing.style.right=14; drawing.style.top=54; drawing.style.bottom=40; drawing.style.overflow=Overflow.Hidden;
            drawing.generateVisualContent+=Draw; Add(drawing);
            footer=new Label("Sailing continues"); footer.style.position=Position.Absolute; footer.style.left=14; footer.style.bottom=12;
            footer.style.fontSize=12; footer.style.color=(Color)new Color32(57,72,69,255); Add(footer);
        }
        public void Refresh()
        {
            var v=SheetBits.Voyage; var m=SheetBits.Motor;
            footer.text=v!=null && v.HomePoint!=null && m!=null
                ? "Home · " + Mathf.RoundToInt(Vector3.Distance(v.HomePoint.position,m.transform.position)) + " m    ·    Sailing continues"
                : "Sailing continues";
            drawing.MarkDirtyRepaint();
        }
        void Draw(MeshGenerationContext ctx)
        {
            Rect r=drawing.contentRect; if(r.width<1) return;
            var p=ctx.painter2D; Vector2 ship=ChartData.ShipPos;
            float span=1200f; var v=SheetBits.Voyage;
            Vector2 home=ship;
            bool hasHome=v!=null && v.HomePoint!=null;
            if(hasHome) { var h=v.HomePoint.position; home=new Vector2(h.x,h.z); span=Mathf.Max(span,Vector2.Distance(ship,home)*2.3f); }
            float scale=Mathf.Min(r.width,r.height)/span;
            Vector2 Map(Vector2 w) { var d=(w-ship)*scale; return r.center+new Vector2(d.x,-d.y); }
            p.strokeColor=new Color(0.3f,0.38f,0.34f,.16f); p.lineWidth=.7f;
            for(int i=0;i<=6;i++) { float t=i/6f; p.BeginPath(); p.MoveTo(new Vector2(r.width*t,0)); p.LineTo(new Vector2(r.width*t,r.height)); p.MoveTo(new Vector2(0,r.height*t)); p.LineTo(new Vector2(r.width,r.height*t)); p.Stroke(); }
            foreach(var island in ChartData.Islands())
            {
                var outline=ChartData.OutlineOf(island.island);
                p.fillColor=(Color)new Color32(152,169,132,255); p.strokeColor=(Color)new Color32(83,111,91,255); p.lineWidth=1.2f; p.BeginPath();
                for(int i=0;i<48;i++) { float a=i*360f/48f; float rad=a*Mathf.Deg2Rad; float radius=outline!=null?outline(a):island.meanRadius;
                    Vector2 at=Map(island.centre+new Vector2(Mathf.Sin(rad),Mathf.Cos(rad))*radius); if(i==0)p.MoveTo(at);else p.LineTo(at); }
                p.ClosePath();p.Fill();p.Stroke();
            }
            if(hasHome) Triangle(p,Map(home),0,new Color32(218,166,48,255),6);
            foreach(var enemy in ChartData.Raiders())
                if(Vector2.Distance(enemy.pos,ship)<400f) Triangle(p,Map(enemy.pos),enemy.headingDeg,new Color32(176,82,64,255),3);
            Triangle(p,r.center,ChartData.ShipHeadingDeg,new Color32(31,75,95,255),7);
        }
        static void Triangle(Painter2D p,Vector2 c,float angle,Color color,float size)
        {
            float a=angle*Mathf.Deg2Rad; Vector2 f=new Vector2(Mathf.Sin(a),-Mathf.Cos(a)),r=new Vector2(-f.y,f.x);
            p.fillColor=color;p.BeginPath();p.MoveTo(c+f*size);p.LineTo(c-f*size*.6f+r*size*.6f);p.LineTo(c-f*size*.6f-r*size*.6f);p.ClosePath();p.Fill();
        }
    }
}
