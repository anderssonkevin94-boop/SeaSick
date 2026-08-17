using SeaSick.Ship;
using UnityEngine;

namespace SeaSick.Crew
{
    /// One crew member on deck. Sickness fills from time at sea plus the
    /// ship's roughness (squared — smooth sailing is dramatically kinder).
    /// All acting is whole-body: green tint, sway, and the walk to the rail.
    /// Lives as a child of the ship, so deck motion comes for free.
    public class CrewAgent : MonoBehaviour
    {
        [Header("Who")]
        [SerializeField] CrewMemberDef def;

        [Header("Sickness")]
        [SerializeField] float baseRate = 0.005f;     // per second, just for being at sea
        [SerializeField] float roughnessRate = 0.12f; // per second at roughness = 1 (squared curve)
        [SerializeField] float pukeThreshold = 0.75f;
        [SerializeField] float pukeRelief = 0.25f;
        [SerializeField] float pukeDuration = 3.5f;

        [Header("Deck positions (ship-local)")]
        [SerializeField] Vector3 stationLocal = new Vector3(1.0f, 2.0f, -2.5f);
        [SerializeField] Vector3 railLocal = new Vector3(1.8f, 2.0f, -2.5f);
        [SerializeField] float walkSpeed = 1.4f;

        [Header("Anger (fuel for mutiny)")]
        [SerializeField] float angerRiseRate = 0.035f;   // per s while very sick
        [SerializeField] float angerDecayRate = 0.012f;  // per s once feeling better
        [SerializeField] float verySickThreshold = 0.8f;

        [Header("Acting")]
        [SerializeField] Renderer[] tintRenderers;
        [SerializeField] Color healthyTint = new Color(0.87f, 0.65f, 0.48f);
        [SerializeField] Color sickTint = new Color(0.55f, 0.78f, 0.45f);
        [SerializeField] Color angryTint = new Color(0.9f, 0.38f, 0.3f);
        [SerializeField] float maxSwayDegrees = 9f;

        public float Sickness01 { get; private set; }
        /// Fuel for mutiny: rises while very sick, cools once they feel better.
        public float Anger01 { get; private set; }
        public int PukeCount { get; private set; }
        public string StateName => state.ToString();
        public CrewMemberDef Def => def;

        /// Full recovery — called when the ship docks at home.
        public void Rest() { Sickness01 = 0f; Anger01 = 0f; }

        enum State { Station, ToRail, Puking, Returning }
        State state = State.Station;

        SmoothnessMeter meter;
        MaterialPropertyBlock block;
        float pukeTimer;
        float swayPhase;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        void Start()
        {
            meter = GetComponentInParent<SmoothnessMeter>();
            block = new MaterialPropertyBlock();
            if (tintRenderers == null || tintRenderers.Length == 0)
                tintRenderers = GetComponentsInChildren<Renderer>();
            transform.localPosition = stationLocal;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            AccumulateSickness(dt);
            RunStateMachine(dt);
            ActBody(dt);
        }

        void AccumulateSickness(float dt)
        {
            float rough = meter != null ? meter.Roughness01 : 0f;
            float resistance = def != null ? def.ironStomach : 0f;
            float rate = (baseRate + roughnessRate * rough * rough) * (1f - 0.5f * resistance);
            Sickness01 = Mathf.Clamp01(Sickness01 + rate * dt);

            // Puking drops sickness below the anger threshold, so letting them
            // puke literally buys off the mutiny for a while.
            if (Sickness01 >= verySickThreshold)
                Anger01 = Mathf.Clamp01(Anger01 + angerRiseRate * dt);
            else if (Sickness01 < 0.6f)
                Anger01 = Mathf.Clamp01(Anger01 - angerDecayRate * dt);
        }

        void RunStateMachine(float dt)
        {
            switch (state)
            {
                case State.Station:
                    if (Sickness01 >= pukeThreshold) state = State.ToRail;
                    break;

                case State.ToRail:
                    if (WalkTo(railLocal, dt))
                    {
                        state = State.Puking;
                        pukeTimer = pukeDuration;
                        // Face overboard.
                        float outward = Mathf.Sign(railLocal.x);
                        transform.localRotation = Quaternion.Euler(0f, 90f * outward, 0f);
                    }
                    break;

                case State.Puking:
                    pukeTimer -= dt;
                    if (pukeTimer <= 0f)
                    {
                        Sickness01 = Mathf.Max(0f, Sickness01 - pukeRelief);
                        PukeCount++;
                        state = State.Returning;
                    }
                    break;

                case State.Returning:
                    if (WalkTo(stationLocal, dt))
                    {
                        transform.localRotation = Quaternion.identity;
                        state = State.Station;
                    }
                    break;
            }
        }

        bool WalkTo(Vector3 targetLocal, float dt)
        {
            Vector3 pos = Vector3.MoveTowards(transform.localPosition, targetLocal, walkSpeed * dt);
            transform.localPosition = pos;
            return (pos - targetLocal).sqrMagnitude < 0.001f;
        }

        void ActBody(float dt)
        {
            // Queasy sway: builds with sickness, wobbles faster when sicker.
            swayPhase += dt * Mathf.Lerp(1.2f, 3.2f, Sickness01);
            float sway = Mathf.Sin(swayPhase) * maxSwayDegrees * Sickness01;

            if (state == State.Puking)
            {
                // Lean over the rail, heaving. Big pose: must read from the
                // gameplay camera ~30m back, not just up close.
                float heave = Mathf.Sin(Time.time * 7f) * 8f;
                float outward = Mathf.Sign(railLocal.x);
                transform.localRotation = Quaternion.Euler(0f, 90f * outward, 0f)
                    * Quaternion.Euler(52f + heave, 0f, 0f);
            }
            else if (state == State.Station)
            {
                transform.localRotation = Quaternion.Euler(0f, 0f, sway);
            }

            // Green shift readable well before the puke threshold; anger adds
            // a red flush on top so a brewing mutiny is visible on bodies.
            Color tint = Color.Lerp(healthyTint, sickTint, Mathf.Clamp01(Sickness01 * 1.15f));
            tint = Color.Lerp(tint, angryTint, Anger01 * 0.8f);
            block.SetColor(BaseColorId, tint);
            foreach (var r in tintRenderers)
                if (r != null) r.SetPropertyBlock(block);
        }
    }
}
