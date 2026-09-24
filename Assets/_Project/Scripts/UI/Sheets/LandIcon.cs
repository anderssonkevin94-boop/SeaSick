using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    // Small game-specific pictograms, rendered as UI meshes without font glyph dependencies.
    public sealed class LandIcon : VisualElement
    {
        readonly string kind;
        public LandIcon(string kind)
        {
            this.kind = kind;
            AddToClassList("land-icon"); pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        void Draw(MeshGenerationContext context)
        {
            var p = context.painter2D;
            float s = Mathf.Min(contentRect.width, contentRect.height) / 32f;
            Vector2 V(float x, float y) => new Vector2(x * s, y * s);
            void Poly(Color color, params Vector2[] points)
            {
                p.fillColor = color; p.BeginPath(); p.MoveTo(points[0] * s);
                for (int i = 1; i < points.Length; i++) p.LineTo(points[i] * s);
                p.ClosePath(); p.Fill();
            }
            void Line(float x, float y, float a, float b)
            { p.BeginPath(); p.MoveTo(V(x,y)); p.LineTo(V(a,b)); p.Stroke(); }
            void Circle(float x, float y, float r, Color color)
            { p.fillColor = color; p.BeginPath(); p.Arc(V(x,y), r*s, 0, 360); p.Fill(); }
            var pearl = MidnightLandHud.Pearl;
            var wood = new Color32(212,139,72,255);
            var end = new Color32(247,193,120,255);
            p.strokeColor = pearl; p.lineWidth = 1.6f*s;
            switch (kind)
            {
                case "food":
                    Poly(pearl,new Vector2(3,16),new Vector2(29,16),new Vector2(25,26),new Vector2(7,26));
                    Line(10,29,22,29); Line(10,6,10,12); Line(16,3,16,11); Line(22,6,22,12); break;
                case "logs":
                    for (int i=0; i<3; i++)
                    {
                        float y=8+i*8;
                        Poly(wood,new Vector2(5,y),new Vector2(23,y-5),new Vector2(28,y),new Vector2(9,y+7));
                        Circle(7,y+3,4,end); Circle(7,y+3,2,wood);
                    }
                    break;
                case "planks":
                    for(int i=2;i>=0;i--)
                        Poly(i==0?end:wood,new Vector2(3,17+i*4),new Vector2(23,3+i*4),new Vector2(30,7+i*4),new Vector2(10,23+i*4));
                    break;
                case "saw":
                    var teeth = new Vector2[48];
                    for(int i=0;i<teeth.Length;i++)
                    { float a=i*Mathf.PI*2/teeth.Length; float r=i%4<2?14:11; teeth[i]=new Vector2(16+Mathf.Cos(a)*r,16+Mathf.Sin(a)*r); }
                    Poly(pearl,teeth); Circle(16,16,3,new Color32(24,43,57,255)); break;
                case "crew":
                    Circle(16,8,4,pearl); Circle(6,12,3,pearl); Circle(26,12,3,pearl);
                    Poly(pearl,new Vector2(10,29),new Vector2(10,20),new Vector2(13,15),new Vector2(19,15),new Vector2(22,20),new Vector2(22,29));
                    Poly(pearl,new Vector2(1,29),new Vector2(1,21),new Vector2(5,17),new Vector2(8,18),new Vector2(8,29));
                    Poly(pearl,new Vector2(24,29),new Vector2(24,18),new Vector2(27,17),new Vector2(31,21),new Vector2(31,29)); break;
                case "build":
                    Poly(pearl,new Vector2(2,15),new Vector2(16,3),new Vector2(30,15),new Vector2(26,15),new Vector2(26,29),new Vector2(19,29),new Vector2(19,20),new Vector2(13,20),new Vector2(13,29),new Vector2(6,29),new Vector2(6,15)); break;
                case "ship":
                    Poly(pearl,new Vector2(3,24),new Vector2(29,24),new Vector2(24,30),new Vector2(8,30));
                    Poly(pearl,new Vector2(15,3),new Vector2(15,21),new Vector2(3,21));
                    Poly(pearl,new Vector2(18,8),new Vector2(28,21),new Vector2(18,21)); break;
                case "stores":
                    p.BeginPath(); p.MoveTo(V(4,8)); p.LineTo(V(16,3)); p.LineTo(V(28,8)); p.LineTo(V(28,25)); p.LineTo(V(16,30)); p.LineTo(V(4,25)); p.ClosePath(); p.Stroke();
                    Line(4,8,16,13); Line(28,8,16,13); Line(16,13,16,30); Line(4,8,16,30); Line(28,8,16,30); break;
                case "close": Line(8,8,24,24); Line(24,8,8,24); break;
                default: Line(5,16,27,16); Line(20,9,27,16); Line(20,23,27,16); break;
            }
        }
    }
}
