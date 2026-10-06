using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// A heading tape with home pinned to its geographic bearing. Showing
    /// the full circle keeps home on the tape even when it lies astern.
    public sealed class SailingCompass : VisualElement
    {
        readonly Label distance;
        readonly Label[] cardinals = new Label[4];
        float heading, homeBearing;
        bool hasHome;
        static readonly Color Pearl = new Color32(239,243,233,255);
        static readonly Color Gold = new Color32(247,203,93,255);
        public SailingCompass()
        {
            pickingMode=PickingMode.Ignore;
            style.position=Position.Absolute; style.left=style.right=6; style.top=style.bottom=0;
            distance=new Label(); distance.pickingMode=PickingMode.Ignore;
            distance.style.position=Position.Absolute; distance.style.top=2; distance.style.height=15;
            distance.style.marginTop=distance.style.marginBottom=distance.style.marginLeft=distance.style.marginRight=0;
            distance.style.width=Length.Percent(100); distance.style.unityTextAlign=TextAnchor.MiddleCenter;
            distance.style.fontSize=10; distance.style.color=Pearl; Add(distance);
            string[] names={"N","E","S","W"};
            for(int i=0;i<4;i++)
            {
                var label=new Label(names[i]) { pickingMode=PickingMode.Ignore };
                label.style.position=Position.Absolute;label.style.top=29;label.style.width=18;label.style.height=14;
                label.style.marginTop=label.style.marginBottom=label.style.marginLeft=label.style.marginRight=0;
                label.style.unityTextAlign=TextAnchor.MiddleCenter;label.style.fontSize=11;label.style.color=Pearl;
                cardinals[i]=label;Add(label);
            }
            generateVisualContent+=Draw;
        }
        /// The same projection is used for ticks, letters and the home marker.
        public static float BearingX(float bearing,float heading,float width)
            => width*.5f + Mathf.DeltaAngle(heading,bearing)/360f*(width-12f);
        public void Refresh()
        {
            heading=ChartData.ShipHeadingDeg;
            var v=SheetBits.Voyage;var m=SheetBits.Motor;
            hasHome=v!=null && v.HomePoint!=null && m!=null;
            if(hasHome)
            {
                var d=v.HomePoint.position-m.transform.position; d.y=0;
                homeBearing=Mathf.Repeat(Mathf.Atan2(d.x,d.z)*Mathf.Rad2Deg,360);
                distance.text="Home · "+Mathf.RoundToInt(d.magnitude)+" m";
            }
            else distance.text="Sea chart";
            float width=contentRect.width;
            for(int i=0;i<4;i++)cardinals[i].style.left=Mathf.Clamp(BearingX(i*90,heading,width)-9,0,Mathf.Max(0,width-18));
            MarkDirtyRepaint();
        }
        void Draw(MeshGenerationContext ctx)
        {
            float width=contentRect.width; if(width<1)return;
            var p=ctx.painter2D;
            p.strokeColor=new Color(Pearl.r,Pearl.g,Pearl.b,.4f);p.lineWidth=1;
            p.BeginPath();
            for(int i=0;i<24;i++)
            {
                float x=BearingX(i*15,heading,width);
                p.MoveTo(new Vector2(x, i%6==0?19:22));p.LineTo(new Vector2(x,27));
            }
            p.Stroke();
            // White centre tick is the bow heading; gold is exclusively home.
            p.strokeColor=Pearl;p.lineWidth=1.5f;p.BeginPath();p.MoveTo(new Vector2(width/2,17));p.LineTo(new Vector2(width/2,27));p.Stroke();
            if(!hasHome)return;
            float homeX=BearingX(homeBearing,heading,width);
            p.fillColor=Gold;p.BeginPath();p.MoveTo(new Vector2(homeX,29));p.LineTo(new Vector2(homeX-4,20));p.LineTo(new Vector2(homeX+4,20));p.ClosePath();p.Fill();
        }
    }
}
