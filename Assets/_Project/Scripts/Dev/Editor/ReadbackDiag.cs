using System.Collections;
using System.Reflection;
using System.Text;
using UnityEngine;
using SeaSick.Ocean;

/// Why is the readback stamp not following OceanTime? Prints the clock, the
/// sampler's stamp and every slot's state per frame, across a deliberate
/// BACKWARD scrub -- the case that wedges a ring ranked by timestamp.
/// Play mode, OceanLab. Writes /tmp/seasick-readbackdiag.txt.
public class ReadbackDiag : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("ReadbackDiag: not in play mode"); return; }
        new GameObject("ReadbackDiag").AddComponent<ReadbackDiag>();
    }

    IEnumerator Start()
    {
        var sb = new StringBuilder();

        // Is the fix even in the loaded assembly?
        FieldInfo seq = typeof(DisplacementReadback.Slot).GetField("seq");
        sb.AppendLine("DisplacementReadback.Slot.seq present in loaded assembly: "
            + (seq != null));
        sb.AppendLine("OceanTime.Now at entry: " + OceanTime.Now.ToString("F3")
            + "   Paused " + OceanTime.Paused + "   Scale " + OceanTime.Scale.ToString("F2"));

        var ocean = OceanRenderer.Instance;
        sb.AppendLine("OceanRenderer.Instance: " + (ocean != null));
        if (ocean == null) { System.IO.File.WriteAllText("/tmp/seasick-readbackdiag.txt", sb.ToString()); yield break; }
        sb.AppendLine("renderer enabled " + ocean.enabled + " active " + ocean.gameObject.activeInHierarchy);
        sb.AppendLine();

        sb.AppendLine("free-running:");
        for (int f = 0; f < 8; f++)
        {
            sb.AppendLine(string.Format("  frame {0,2}  OceanTime {1,9:F3}   SurfaceTime {2,9:F3}   Ready {3}",
                f, OceanTime.Now, OceanSampler.SurfaceTime, OceanSampler.Ready));
            yield return null;
        }

        sb.AppendLine();
        sb.AppendLine("scrub BACKWARD to 41 and pause:");
        OceanTime.Scrub(41.0);
        OceanTime.Paused = true;
        for (int f = 0; f < 20; f++)
        {
            sb.AppendLine(string.Format("  frame {0,2}  OceanTime {1,9:F3}   SurfaceTime {2,9:F3}   Ready {3}",
                f, OceanTime.Now, OceanSampler.SurfaceTime, OceanSampler.Ready));
            yield return null;
        }

        sb.AppendLine();
        sb.AppendLine("scrub FORWARD to 900 and pause:");
        OceanTime.Scrub(900.0);
        for (int f = 0; f < 12; f++)
        {
            sb.AppendLine(string.Format("  frame {0,2}  OceanTime {1,9:F3}   SurfaceTime {2,9:F3}   Ready {3}",
                f, OceanTime.Now, OceanSampler.SurfaceTime, OceanSampler.Ready));
            yield return null;
        }

        OceanTime.Paused = false;
        System.IO.File.WriteAllText("/tmp/seasick-readbackdiag.txt", sb.ToString());
        Debug.Log("ReadbackDiag:\n" + sb);
        Destroy(gameObject);
    }
}
