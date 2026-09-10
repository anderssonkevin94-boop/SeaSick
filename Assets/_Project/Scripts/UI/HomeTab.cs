using SeaSick.Ship;
using UnityEngine;

namespace SeaSick.UI
{
    /// A small tab on the left edge that puts her back on her home berth.
    ///
    /// A TAB and not a panel, on purpose: the HUD is small plates at the
    /// screen edges and nothing across the world, and this is a rare
    /// deliberate command rather than an instrument. It sits directly under
    /// the Yard tab, in the same idiom and on the same edge, because that is
    /// already where a thumb goes for "things I do about the ship" as opposed
    /// to "things I do with the ship".
    ///
    /// **It arms on the first press and only acts on the second.** One tap
    /// that teleports a loaded ship off a wave face and across the world is
    /// the kind of mis-tap that ends a session, and this button lives a
    /// thumb's width from a control the player uses constantly. The armed
    /// state lapses on its own so it can never sit hot.
    ///
    /// Hidden while she is already lying at her own pier — a control with
    /// nothing to do should not be on screen — and it refuses while the crew
    /// are ashore, saying so on itself rather than in a banner.
    ///
    /// What it does NOT do is cost anything: her cargo comes home with her and
    /// the voyage closes exactly as if she had sailed back, because
    /// `VoyageManager` completes on `AtHomeDock` and this genuinely puts her
    /// there. That is Kevin's call, made 2026-09-10 — a free tow home. If it
    /// ever needs a price, the price belongs in `VoyageManager`, not here.
    public class HomeTab : MonoBehaviour
    {
        [Tooltip("Seconds the confirm stays armed before it lapses.")]
        [SerializeField] float armSeconds = 3.5f;
        [Tooltip("Seconds a refusal stays on screen.")]
        [SerializeField] float refusalSeconds = 3f;

        AnchorController anchor;
        float armedUntil = -99f;
        string refusal;
        float refusalUntil = -99f;

        void Start() => anchor = FindFirstObjectByType<AnchorController>();

        void OnGUI()
        {
            // Built on demand rather than trusted from Start: script order is
            // not guaranteed and the ship is not always the first thing up.
            if (anchor == null)
            {
                if (Event.current.type != EventType.Layout) return;
                anchor = FindFirstObjectByType<AnchorController>();
                if (anchor == null) return;
            }
            // Nothing to do at her own pier.
            if (anchor.AtHomeDock) { armedUntil = -99f; return; }

            int u = UITheme.Unit;
            float pad = u * 0.7f;
            // Under the Yard tab (which sits at 0.30 of screen height and is
            // 2.0 units tall), clear of it by half a pad.
            var tab = new Rect(pad, Screen.height * 0.30f + u * 2.0f + pad * 0.5f,
                               u * 5.4f, u * 2.0f);
            bool armed = Time.time < armedUntil;

            var prev = GUI.contentColor;
            if (armed) GUI.contentColor = UITheme.Warn;
            if (GUI.Button(tab, armed ? "sure?" : "⌂ Home", UITheme.Button))
            {
                if (!armed) armedUntil = Time.time + armSeconds;
                else
                {
                    armedUntil = -99f;
                    if (!anchor.BerthAtHome(out string why))
                    {
                        refusal = why;
                        refusalUntil = Time.time + refusalSeconds;
                    }
                }
            }
            GUI.contentColor = prev;
            // Same as the Yard tab: register the rect so a tap on it can
            // never also be read as a grab at the helm's steering zone.
            UIBlocker.Block(tab);

            // The reason lives ON the control that refused. Never a banner.
            if (Time.time < refusalUntil && !string.IsNullOrEmpty(refusal))
            {
                GUI.contentColor = UITheme.Bad;
                GUI.Label(new Rect(tab.xMax + pad * 0.5f, tab.y, u * 11f, tab.height),
                          refusal, UITheme.Small);
                GUI.contentColor = prev;
            }
        }
    }
}
