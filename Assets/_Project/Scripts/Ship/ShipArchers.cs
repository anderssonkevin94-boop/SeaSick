using System.Collections.Generic;
using UnityEngine;
using SeaSick.Combat;

namespace SeaSick.Ship
{
    /// <summary>
    /// **Bows at sea (2026-09-30, docs/GDD.md "Bows").** Kevin: bows are used
    /// "by sailors to attack" from the ship, alongside the cannons.
    ///
    /// The rule, in one sentence: *every volley, each able hand with a bow in
    /// the hold looses one arrow from the hold at the nearest enemy hull in
    /// range.* Archers = min(bows aboard, able crew); arrows come off the
    /// voyage cargo (`VoyageManager.RemoveLoot`), one per archer per volley,
    /// and with none aboard nobody shoots. No crew assignment of its own:
    /// the gun crews still work the guns -- a bow is what the rest of the
    /// deck does with its hands.
    ///
    /// **Targets** are raider hulls (`EnemyShip`) within
    /// `RaidFightTuning.ShipBowRange`; the gun lock (`CannonBattery.
    /// AutoFireTarget`) wins when it is one, same as it does for the guns.
    /// There are no boarders in the game yet -- when there are, they are the
    /// next thing this shoots at.
    ///
    /// **Damage** is small and adds up: one arrow hit is
    /// `RaidFightTuning.ShipArrowHullDamage` of a round shot, banked per hull
    /// and landed through the ordinary `IHittable.TakeHit` one whole shot at
    /// a time -- so the hull, the raid and the sinking all run exactly as a
    /// cannonball's would. Seen on the phone as a volley of arrows arcing
    /// from along the deck, splinters where they bite and splashes where they
    /// miss. Added to the player's hull by `PlayerHull.Awake`.
    /// </summary>
    [DisallowMultipleComponent]
    public class ShipArchers : MonoBehaviour
    {
        /// Arrows at sea are drawn bigger than on land: the camera is far.
        const float SeaArrowScale = 2.2f;
        /// Height of the bows above the hull's hit centre, metres.
        const float DeckRise = 1.2f;
        /// A miss drops this far off the mark, metres (min, max).
        const float MissNear = 2.5f, MissFar = 7f;
        /// Share of each hull's half-length the archers stand along / the
        /// arrows land along.
        const float AlongDeck = 0.6f, AlongTarget = 0.7f;

        static ShipArchers live;

        Crew.CrewRoster roster;
        CannonBattery battery;
        PlayerHull self;
        SeaSick.Voyage.VoyageManager voyage;
        ShipHold hold;
        float clock;
        readonly Dictionary<EnemyShip, float> owed = new Dictionary<EnemyShip, float>();

        /// Archers who would shoot now (for a readout): bows aboard, capped by
        /// able hands, zero with no arrows.
        public int Archers
        {
            get
            {
                var v = Voyage;
                if (v == null || v.HeldOf(World.Res.Arrows) <= 0) return 0;
                return Mathf.Min(v.HeldOf(World.Res.Bow), roster != null ? roster.AbleCount : 0);
            }
        }

        SeaSick.Voyage.VoyageManager Voyage =>
            voyage != null ? voyage : (voyage = FindFirstObjectByType<SeaSick.Voyage.VoyageManager>());

        void Awake()
        {
            roster = GetComponent<Crew.CrewRoster>();
            battery = GetComponent<CannonBattery>();
            self = GetComponent<PlayerHull>();
        }

        void OnEnable() { if (live == null && GetComponent<ShipMotor>() != null) live = this; }
        void OnDisable() { if (live == this) live = null; }

        void Update()
        {
            // One ship shoots from the one hold: a second hull in the scene
            // (a yard preview) never draws on the same arrows.
            if (live == null && GetComponent<ShipMotor>() != null) live = this;
            if (live != this) return;

            float period = RaidFightTuning.ShipVolleySeconds;
            clock = Mathf.Min(clock + Time.deltaTime, period);
            if (clock < period) return;

            var v = Voyage;
            if (v == null) return;
            if (roster == null) roster = GetComponent<Crew.CrewRoster>();
            int archers = Mathf.Min(v.HeldOf(World.Res.Bow), roster != null ? roster.AbleCount : 0);
            if (archers <= 0) return;

            EnemyShip target = Pick(RaidFightTuning.ShipBowRange);
            if (target == null) return;

            // **One arrow per archer per volley, out of the hold.**
            int shots = v.RemoveLoot(Mathf.Min(archers, v.HeldOf(World.Res.Arrows)), World.Res.Arrows);
            if (shots <= 0) return;
            TrimDeckStack(v);
            clock = 0f;
            for (int i = 0; i < shots; i++) Loose(target);
        }

        /// The locked raider if it is in range, else the nearest one that is.
        EnemyShip Pick(float range)
        {
            Vector3 here = transform.position;
            float bestSq = range * range;
            if (battery == null) battery = GetComponent<CannonBattery>();
            if (battery != null && battery.AutoFireTarget is EnemyShip locked && locked != null && locked.Alive)
            {
                Vector3 d = locked.HitCentre - here;
                d.y = 0f;
                if (d.sqrMagnitude <= bestSq) return locked;
            }
            EnemyShip best = null;
            foreach (var e in EnemyShip.All)
            {
                if (e == null || !e.Alive) continue;
                Vector3 d = e.HitCentre - here;
                d.y = 0f;
                float sq = d.sqrMagnitude;
                if (sq <= bestSq) { bestSq = sq; best = e; }
            }
            return best;
        }

        void Loose(EnemyShip target)
        {
            if (self == null) self = GetComponent<PlayerHull>();
            Vector3 centre = self != null ? self.HitCentre : transform.position + Vector3.up * 2.2f;
            Vector3 axis = self != null ? self.HitAxis : transform.forward * 8f;
            Vector3 from = centre + axis * Random.Range(-AlongDeck, AlongDeck) + Vector3.up * DeckRise;

            bool hit = Random.value < RaidFightTuning.ShipBowHitChance;
            Vector3 aim = target.HitCentre + target.HitAxis * Random.Range(-AlongTarget, AlongTarget)
                          + Vector3.up * Random.Range(0f, 1.2f);
            if (!hit)
            {
                Vector2 off = Random.insideUnitCircle.normalized * Random.Range(MissNear, MissFar);
                aim = new Vector3(aim.x + off.x, target.transform.position.y, aim.z + off.y);
            }
            var t = target;
            ArrowFlight.Loose(from, aim, () =>
            {
                if (!hit) { Ocean.DynamicWaterSim.Splash(aim, 0.6f, 0.25f); return; }
                if (t == null || !t.Alive) return;
                ArrowFlight.Puff(aim, new Color(0.45f, 0.34f, 0.22f), 1.6f);
                Bank(t, aim);
            }, hit ? target.transform : null, SeaArrowScale);
        }

        /// Arrow hits add up; each whole round shot's worth lands as one.
        void Bank(EnemyShip t, Vector3 at)
        {
            owed.TryGetValue(t, out float have);
            have += Mathf.Max(0f, RaidFightTuning.ShipArrowHullDamage);
            if (have >= 1f)
            {
                have -= 1f;
                t.TakeHit(at, 1f);
            }
            if (!t.Alive) owed.Remove(t);
            else owed[t] = have;
        }

        /// The stack on deck comes down with the arrows, as
        /// `World.ShipCargoSide.Take` does for a camp transfer.
        void TrimDeckStack(SeaSick.Voyage.VoyageManager v)
        {
            if (hold == null) hold = FindFirstObjectByType<ShipHold>();
            if (hold == null) return;
            for (int i = 0; i < 64 && hold.VisibleCount > v.TotalHeld; i++)
                if (!hold.RemoveVisual(World.Res.Arrows) && !hold.RemoveVisual()) break;
        }
    }
}
