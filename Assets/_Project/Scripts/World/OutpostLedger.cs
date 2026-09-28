using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// What one hand at an outpost has been told to do.
    ///
    /// **The order carries a TARGET now**, because "cut wood" was the only job
    /// there was and is not any more. Gathering needs to know what; working
    /// needs to know where.
    public enum OutpostOrder
    {
        /// Standing about. What a hand does when nobody has told it anything,
        /// and what it falls back to when its pile is full or its stock is
        /// gone.
        Idle,
        /// Taking something out of the ground: `target` is the resource.
        /// This was `Cut`, back when timber was all there was.
        Gather,
        /// Cutting and carrying wood into whatever is sited here but not yet
        /// built. **The same work at the same rate as gathering timber** --
        /// the only difference is where the logs land, which is what makes a
        /// half-built camp cost exactly what it looks like it costs.
        Build,
        /// Assigned to a building: `target` is the plan id. What they produce
        /// is the building's business, not theirs.
        Work,
    }

    /// **How much of a day's ration this camp actually issues, 2026-09-22.**
    /// Kevin's knob: a camp you can run lean on purpose, not just one that
    /// runs out by accident. `OutpostLedger.EatMultiplier` reads it; nothing
    /// else needs to know the camp is short-rationing on purpose versus
    /// simply out of food.
    public enum Rations
    {
        Full,
        Half,
        None,
    }

    /// **Which of food or timber this camp leans on, 2026-09-22.** Kevin's
    /// second knob: "a camp you can point." Even is the old, unweighted
    /// arithmetic; the other two trade a fifth of one rate for a quarter of
    /// the other, cheap enough that a player can flip it and watch the
    /// numbers on the sheet move without the camp's shape changing.
    public enum WorkPriority
    {
        Even,
        FoodFirst,
        TimberFirst,
    }

    /// One hand left at an outpost.
    ///
    /// Keyed by `name`, which is `CrewMemberDef.displayName`. They are a cast
    /// of twenty with names, not a pool, so the name IS the identity and it
    /// survives a save without an id table nobody would maintain.
    [System.Serializable]
    public class OutpostHand
    {
        public string name;
        public OutpostOrder order = OutpostOrder.Idle;
        /// **Still walking up from the ship, 2026-09-23** -- not saved.
        /// Kevin: *"i placed the fire down and its halfway done before
        /// anyone has cut down any wood or even reached it."* The ledger
        /// pays work by the clock and the bodies act it out, which is right
        /// for a camp nobody is watching; but a hand the player has just
        /// watched step off the gangway has done nothing yet. Set by
        /// `Outpost.Station` for a watched landing, cleared by the hand's
        /// `CampWorker` the moment the body starts its first piece of work
        /// (or after `CampWorker.WalkInLimit` seconds, so a body that cannot
        /// path does not freeze the camp), and by `Outpost.CatchUp` whenever
        /// nobody is watching. `WorkFactor` is zero while it is set.
        [System.NonSerialized] public bool walkingIn;
        /// **What the BODY cannot do**, written by `CampWorker` and read by
        /// `StallReason` only (never by the books): today just "walled off",
        /// when the walk to his errand has no route and the straight line is
        /// through a palisade. Null while he can move. 2026-09-24.
        [System.NonSerialized] public string bodyBlocked;
        /// Hunting because the camp went hungry and this hand took it on
        /// itself (`OutpostLedger.FeedFirst`), not because the player said
        /// so. Only these are sent back once the camp is fed again.
        public bool autoFood;

        /// What they are gathering, or which building they are assigned to.
        /// Empty for Idle and for Build, which has only ever one thing to
        /// work on.
        public string target = "";

        /// **Carried, unused, on purpose.** Kevin's call 2026-09-13: a hand can
        /// eventually refuse or leave when starving or badly treated, but for
        /// this pass they only get angry. Nothing reads this yet; it is here
        /// from the start so the save format does not have to change when
        /// something does.
        public float mood = 1f;

        /// **Recruited, not shipped, 2026-09-21.** True for a hand
        /// `OutpostLedger.Step` grew from a full pile and an empty bed --
        /// they have a name and a row but no crew body yet. `Outpost` reads
        /// this to know which rows still need one raised for them; it
        /// defaults false so an existing save (every hand in it came off
        /// the ship) does not suddenly read as newborn.
        public bool born = false;

        // --- the haul in their arms (2026-09-23) ---------------------------
        //
        // One trip at a time: goods leave the source at pickup, ride here, and
        // land at the destination when `haulLeft` (game-days of the hand's
        // effective work) runs out. Saved, so a trip split across quanta,
        // ticks or a save lands exactly where one long tick would (D2). Read
        // through `OutpostLedger.HaulOf`. All default to "carrying nothing",
        // which is what an old save reads as.
        public string haulRes = "";
        public int haulCount;
        public HaulPlace haulFrom = HaulPlace.None;
        public HaulPlace haulTo = HaulPlace.None;
        public int haulFromStation = -1;
        public int haulToStation = -1;
        /// The load came out of a station's input BAY (not its rack): where
        /// it goes back to if the store turns out to be full on arrival.
        public bool haulFromBay;
        public float haulLeft;
        public float haulDays;
        /// **The trip's route, booked at its start (2026-09-23).** Where the
        /// load is picked up and put down (world x,z), whether both were
        /// known, and the game-days of one walked leg and of the work at the
        /// pickup (cut + handle). Read through `HaulOf`; an old save's trip
        /// reads as unplaced.
        public bool haulPlaced;
        public float haulFromX, haulFromZ, haulToX, haulToZ;
        public float haulWalkDays, haulWorkDays;

        // --- the walker (2026-09-27, docs/DELIVERY-ON-ARRIVAL.md) ----------
        //
        // Kevin: *"A villager walking with that resource has that resource on
        // him and it gets where it gets when it gets there."* A trip is a
        // walk, not a timer: `tripLeg` says which part of it he is in, and the
        // books change only at the pickup (end of `AtPickup`) and the drop-off
        // (arrival at the end of `ToDrop`). The legs are walked by the body
        // while the camp is watched (`driven`) and by the ledger's own
        // invisible walker otherwise. All saved; all default to "standing at
        // the camp centre, carrying nothing", which is what an old save reads as.
        /// `TripLeg` as an int (JsonUtility-safe). 0 = no trip / an old save's.
        public int tripLeg;
        /// The load has left its source and is on him. False while he is
        /// still walking out to fetch it (the load is only PLANNED then).
        public bool haulPicked;
        /// Metres left on the current leg (headless walker only).
        public float legLeft;
        /// Seconds of stationary work left at the pickup (full strength).
        public float workLeft;
        /// Where he is (world x,z). `wHas` false = never placed: the centre.
        public bool wHas;
        public float wx, wz;
        /// A farmhand's harvest not yet carried to the store (whole units
        /// are carried; the fraction waits here).
        public float basket;

        // --- eating by fill (food rework, 2026-09-27; OutpostLedger.Food.cs) ---
        /// How full he is, 0..1; drains `EatPerHandPerDay` x rations a game
        /// day. Below `EconomyTuning.HungryBelow` he walks to the store and
        /// eats. Old saves: the initialiser (full).
        public float full = 1f;
        /// The last thing he ate: its mood/work bonus holds while he is fed.
        public string lastMeal = "";
        /// The load in his arms is his meal: he eats it at the store instead
        /// of putting it down.
        public bool eating;

        /// A body is walking this hand right now (`CampWorker`): the ledger
        /// does not advance his legs. Not saved.
        [System.NonSerialized] public bool driven;

        // --- downed (death/rescue phase 1, 2026-09-27) ----------------------
        //
        // Always downed before dead (docs/PLAN-DEATH-RESCUE.md, "Deaths").
        // While `downed` is true: `WorkFactor` is 0 (no productive output,
        // no new trips/orders -- see `OutpostLedger.WorkFactor`), and
        // `downedLeft` only ticks in real, unscaled-by-pause time while the
        // camp is WATCHED and running (`Outpost.Update` -> `TickDowned`) --
        // never in `Step`'s game-day quanta, so nobody dies while the
        // player is away or the game is paused. His load stays on him in
        // phase 1 (phase 2 drops it where he fell). Default false/0/"" so
        // an old save reads as "nobody is down".
        public bool downed;
        public float downedLeft;
        public string downedCause = "";
        /// **A rescuer has reached him (phase 2).** The timer is stopped
        /// for good the moment his rescuer arrives -- set once, never
        /// cleared except by `Revive`/`Die`. Default false: an old save's
        /// downed hand (there were none before phase 2) reads as
        /// "not reached yet", which just means a rescuer is re-dispatched.
        public bool reached;

        // --- rescue / drag (death/rescue phase 2, 2026-09-27) ---------------
        //
        // docs/PLAN-DEATH-RESCUE.md, "Deaths": a nearby free hand runs over
        // and drags a downed hand back to a hut. `rescuing` lives on the
        // RESCUER (the downed hand's own name, or "" when he is not one);
        // `dragged` lives on the DOWNED hand (his rescuer has him and they
        // are walking together); `recovering`/`recoverLeft` also live on the
        // downed hand, once he is laid down safe. All watched-only to move
        // (`OutpostLedger.DispatchRescuers`, `CampWorker`'s own tick) except
        // recovery, which ticks in `Step`'s ordinary game-day quanta so it
        // keeps going while nobody is looking (docs: "recovery... MAY
        // advance in game time, unwatched too -- that only helps").
        /// The name of the downed hand THIS hand is walking to / dragging.
        /// "" = not rescuing anybody.
        public string rescuing = "";
        /// This (downed) hand's rescuer has him and they are walking
        /// together to a hut. False until `reached`, and again once he is
        /// laid down to recover.
        public bool dragged;
        /// Laid down safe (a hut, or the fire with no hut), healing.
        /// `WorkFactor` is 0 the same way `downed`'s is.
        public bool recovering;
        /// Game-days of recovery left. Ticks in `Step` (unwatched included).
        public float recoverLeft;
        /// Laid down in a hut (true) or by the fire, no hut standing
        /// (false) -- set once, when the drag finishes, purely for the row
        /// text (`Doing`).
        public bool recoverAtHut;
        /// **The rescuer has reached him and they are walking together**
        /// (mirrors the downed hand's own `reached`, kept on the rescuer
        /// too so `Doing` can tell "running to" from "dragging" without a
        /// ledger reference). Not saved: a reload mid-drag just reads as
        /// "running to" for one frame until `CampWorker` re-derives it from
        /// the downed hand's own (saved) `reached`.
        [System.NonSerialized] public bool draggingNow;

        // --- pout + floor (death/rescue phase 4, 2026-09-28) ----------------
        //
        // docs/PLAN-DEATH-RESCUE.md, "Neglect": an angry hand does not
        // desert, he walks to the fire and sulks for a while, then goes
        // back to whatever he was doing. `order`/`target` are left alone
        // while he pouts, on purpose -- there is nothing to "resume", the
        // ordinary dispatch just starts paying him again once `pouting`
        // clears. Saved so a reload mid-pout (or mid-cooldown) picks up
        // exactly where it left off; all default false/0, which is what an
        // old save (nobody in it ever pouted) reads as.

        /// Standing at the fire, sulking. `WorkFactor` is 0 the same way
        /// `downed`'s is (via `Busy`); see `OutpostLedger.PoutTick`/
        /// `StartPout`.
        public bool pouting;
        /// Real seconds left of this pout. Counted down only while the
        /// camp is watched and running (`OutpostLedger.PoutTick`, called
        /// from `Outpost.Update` beside `TickDowned`).
        public float poutLeft;
        /// Real seconds before this hand may pout again, even if he is
        /// still angry. Ticks down the same watched-and-running way
        /// `poutLeft` does.
        public float poutCooldown;

        /// **Out of the ordinary dispatch**: downed, recovering, being
        /// dragged, off rescuing somebody (phase 2), or pouting at the fire
        /// (phase 4) -- no new orders, no productive work, same as `downed`
        /// alone was in phase 1.
        public bool Busy => downed || recovering || dragged || pouting || defending || alarmed || returningSpear || !string.IsNullOrEmpty(rescuing);

        // --- village defence (death/rescue phase 9, 2026-09-28) -------------
        //
        // docs/PLAN-DEATH-RESCUE.md, "Village defence in raids": an armed
        // hand near the camp during a live raid drops what he's carrying
        // and fights. Not saved -- a raid never resumes mid-fight on load
        // (`RaidParty` itself is not saved either), so a reload always
        // reads as "nobody is defending yet", which is exactly right.

        /// **True while this hand is fighting** (walking to a raider or
        /// jabbing one). Counted in `Busy` so the ordinary dispatch and
        /// `DispatchRescuers` both leave him alone. Set/cleared by
        /// `CampWorker.TickDefend`.
        [System.NonSerialized] public bool defending;
        /// The spear kind he picked up to fight with, snapshotted the
        /// moment he starts defending -- for the row text (`Doing`) only.
        [System.NonSerialized] public string defendSpear;
        /// **Dev-only arm flag** (`LifeDevPanel`'s "Arm" button): makes a
        /// hand an eligible defender without a real hunt or the phase-10
        /// alarm/arming flow. A hunter already out (`huntArmed`) is
        /// eligible the same way -- see `CampWorker.TickDefend`.
        [System.NonSerialized] public bool armedDefender;
        /// Hits taken THIS raid, reset by `Down` and at the next raid's
        /// start (`RaidParty.Begin`) -- never saved, a raid is over the
        /// moment nobody is watching.
        [System.NonSerialized] public int raidHitsTaken;

        // --- the alarm (death/rescue phase 10, 2026-09-28) ------------------
        //
        // docs/PLAN-DEATH-RESCUE.md, "Village defence in raids": the moment a
        // raid party lands, every hand not already busy (downed, rescuing,
        // pouting) and not a posted tower lookout either arms up from the
        // store or runs to hide. Never saved -- like `defending`, a reload
        // always reads as "nobody is under an alarm", which is exactly right
        // since a raid never resumes mid-fight on load.

        /// True while this hand's ordinary work is displaced by a live raid's
        /// alarm (fetching a spear, or hiding) -- set by `Combat.RaidAlarm` at
        /// the moment a party lands, cleared when it clears the alarm.
        /// Counted in `Busy` the same way `defending` already is.
        [System.NonSerialized] public bool alarmed;
        /// Walking to the store for a spear -- `CampWorker.TickFetchSpear`.
        [System.NonSerialized] public bool fetchingSpear;
        /// Gone into a hut: body hidden, waiting it out.
        [System.NonSerialized] public bool hidingHut;
        /// **Actually inside now** (as opposed to still walking to the
        /// door): set by `CampWorker.HideBody`, cleared by `RevealBody` --
        /// `HutHidingLabels` counts this, not `hidingHut` alone, so a hut's
        /// "N hiding" only counts hands who have really disappeared into it.
        [System.NonSerialized] public bool hiddenInHut;
        /// No hut stands: crouched on the far side of the camp instead.
        [System.NonSerialized] public bool hidingCrouch;
        /// **The spear THIS hand is fighting with** -- a store unit he took
        /// out (`Res.Spear`/`Res.IronSpear`), or the one a hunter already had
        /// in hand. Read by `CampWorker.TickDefend` for the jab damage and by
        /// `Doing` for the row text INSTEAD OF re-asking the camp-wide
        /// `OutpostLedger.SpearInHand` snapshot -- a hand armed from an
        /// iron-empty store keeps fighting with his stone spear even if iron
        /// turns up in the pile a moment later. Null/empty for an unarmed
        /// hand, and for one whose `defending` comes from the phase 9 dev
        /// "Arm" flag rather than a real store spear.
        [System.NonSerialized] public string raidSpear;
        /// The camp's posted lookout, while a raid is live -- set/cleared by
        /// `Combat.RaidAlarm` so `Doing` can say "on the tower" rather than
        /// the ordinary "lookout" position label. Never changes what the
        /// body does (`CampWorker.TickTower` already keeps him there); this
        /// is display only.
        [System.NonSerialized] public bool raidLookout;

        // --- all clear (death/rescue phase 12, 2026-09-28) -------------------
        //
        // docs/PLAN-DEATH-RESCUE.md, "All clear": a hand still holding a
        // STORE spear when the raid ends walks it back rather than it
        // teleporting into the pile. Never saved -- same reasoning as
        // `alarmed`/`defending`: a reload never resumes a raid mid-anything.

        /// True while this hand is walking `raidSpear` back to the store --
        /// set by `Combat.RaidAlarm.End`, cleared by `Combat.RaidAlarm.
        /// SettleReturn` on arrival (`World.CampWorker.TickReturnSpear`) or
        /// the moment his body goes away unwatched. Counted in `Busy` the
        /// same way `alarmed` already is.
        [System.NonSerialized] public bool returningSpear;
        /// **Wear on THIS hand's `raidSpear`, from raid kills only** (hunting
        /// wear is a separate, existing path -- `OutpostLedger.HuntKill`).
        /// Zeroed the moment a fresh spear is fetched
        /// (`CampWorker.TickFetchSpear`); accrued by `Combat.RaidAlarm.
        /// WearOnKill` per raider killed; carried into the store as a
        /// fractional return (`OutpostLedger.ReturnWornSpear`) once he is
        /// back, or lost outright the instant it reaches 1 (the spear
        /// BREAKS -- `Combat.RaidAlarm.WearOnKill`).
        [System.NonSerialized] public float raidSpearWear;

        public TripLeg Leg => (TripLeg)tripLeg;

        public bool Hauling => haulCount > 0 && !string.IsNullOrEmpty(haulRes);

        /// **Out after an animal (2026-09-27).** A hunt is a trip on the
        /// same haul fields (`haulRes == Res.Game`, one carcass, Field ->
        /// Store): walk out + stalk, the kill, the carry, the deposit
        /// (`OutpostLedger.Hunting`). `huntKilled` flips at the kill; the
        /// trip went out with the bow when `huntArmed`. Saved with the rest.
        public bool HuntTrip => Hauling && haulRes == Res.Game;
        public bool huntKilled;
        public bool huntArmed;
        /// **A hunting accident was rolled at this kill** (death/rescue
        /// phase 2), consumed right after `FinishPickup` -- not acted on
        /// inside `HuntKill` itself, which runs before the pickup has
        /// finished updating the trip's own fields (`haulPicked` in
        /// particular): downing him there would stamp on state `PickUp` is
        /// still writing. Not saved -- it never survives past the same
        /// frame it is set on.
        [System.NonSerialized] public bool huntAccidentPending;
        /// Kills and carcass deposits booked, this session only (probes).
        [System.NonSerialized] public int huntKills, huntDeposits;

        /// Bumped by every trip start, never saved: lets the body
        /// (`CampWorker`) mime each ledger trip exactly once.
        [System.NonSerialized] public int haulSerial;

        /// What to call what they are doing, for the list on the right.
        public string Doing
        {
            get
            {
                // **Death/rescue phase 2 states outrank the ordinary
                // order** -- a downed/dragged/recovering hand, or one off
                // rescuing somebody, is not doing his job right now.
                if (downed)
                    return reached ? "down" : "down · " + Mmss(downedLeft);
                if (recovering) return recoverAtHut ? "recovering in the hut" : "recovering by the fire";
                if (dragged) return "being carried home";
                if (!string.IsNullOrEmpty(rescuing))
                    return (draggingNow ? "dragging " : "running to ") + rescuing;
                if (pouting) return "pouting at the fire · " + Mmss(poutLeft);
                // **Death/rescue phase 10/11** -- the alarm's own states, same
                // priority band as `defending` just below. A hand hiding with
                // a spear still in hand (the Hide-all switch caught a hunter
                // with his own) reads as ORDERED there, not unarmed -- the
                // one thing the row has to never claim is that he has
                // nothing when he does.
                if (fetchingSpear) return "fetching a spear";
                if (returningSpear) return "returning a spear";
                if (hidingHut || hidingCrouch)
                {
                    if (!string.IsNullOrEmpty(raidSpear)) return "hiding (ordered)";
                    return hidingHut ? "hiding, no spear" : "crouching, no spear";
                }
                if (defending)
                    return "defending, " + (defendSpear == Res.IronSpear ? "iron spear" : "stone spear");
                if (raidLookout) return "on the tower";
                switch (order)
                {
                    case OutpostOrder.Gather:
                        // Nobody "gathers game". He is out after the goats.
                        if (target == Res.Game) return "hunting";
                        return string.IsNullOrEmpty(target)
                            ? "gathering" : "gathering " + target.ToLowerInvariant();
                    case OutpostOrder.Build: return "building";
                    case OutpostOrder.Work:
                        string post = BuildPlans.PositionAt(target);
                        return string.IsNullOrEmpty(post) ? "working" : post;
                    default: return "idle";
                }
            }
        }

        /// "2:31" from a real-seconds count, for the downed row (`Doing`).
        static string Mmss(float seconds)
        {
            int s = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return (s / 60) + ":" + (s % 60).ToString("00");
        }

        /// Furious, not just short-tempered. `OutpostLedger.AngryCount` counts
        /// this; nothing else reads it yet.
        public bool Angry => mood < 0.5f;

        /// One word for the row, or none. "angry" once `mood` has crossed
        /// the line `WorkFactor` starts docking labour at; "hungry" a while
        /// before that, when they are still pulling full weight but it has
        /// been going short; "" for a hand nobody has starved.
        public string MoodWord
        {
            get
            {
                if (Angry) return "angry";
                if (mood < 0.95f) return "hungry";
                return "";
            }
        }
    }

    /// Something the player has SITED here but nobody has finished building.
    ///
    /// **This is ledger state, not a scene object, and that is the whole
    /// point.** A blueprint you placed and then sailed away from has to still
    /// be there -- half built, with the logs that went into it -- when you come
    /// back three islands later and its terrain has streamed in and out twice.
    /// The ghost standing on the ground is DRAWN from this, the same way the
    /// crew bodies are drawn from the hand rows.
    ///
    /// Position is carried here rather than taken from the outpost's surveyed
    /// clearing because the player chose it. The survey says where a camp COULD
    /// go; this says where it is going.
    [System.Serializable]
    public class PendingBuild
    {
        public string planId;
        /// Where the player put it, world metres.
        public float x, z;
        /// Logs to finish it.
        public int needed;
        /// Logs in it. Whole logs only -- a half-carried log is not a thing
        /// anyone can see, so the fraction lives beside it.
        public int done;
        public float donePart;

        /// **The stone part, 2026-09-21.** Kevin: *"the buildings require
        /// wood and stone, so stone needs to be minable."* Same three fields
        /// as the timber part and paid by the same shape of arithmetic (haul
        /// from the pile, else quarry what stands), kept SEPARATE rather than
        /// folded into one number because the sheet has to be able to say
        /// which of the two a stalled blueprint is waiting on.
        ///
        /// **A save written before this existed restores all three as 0**,
        /// which reads exactly as "this one wanted no stone" -- so an old
        /// half-built hut finishes on the timber it was already owed.
        public int stoneNeeded;
        public int stoneDone;
        public float stoneDonePart;

        /// **The brick part, 2026-09-22.** Same three fields again, and
        /// zero on every plan there is -- see `BuildPlan.baseBrickCost` for
        /// why it exists before anything charges it. A save written before
        /// this existed restores all three as 0, which reads exactly as
        /// "this one wanted no brick", the same way the stone part did.
        ///
        /// **The one that cannot be paid out of the ground.** Timber is cut
        /// and stone is quarried where the blueprint stands; a brick was
        /// made at a quarry by somebody and is either on the pile or it is
        /// not. `OutpostLedger.PayBrick` has no seam half at all.
        public int brickNeeded;
        public int brickDone;
        public float brickDonePart;

        /// Which way it faces, world degrees. **Carried here rather than
        /// recomputed**, because since 2026-09-19 the player turns it by hand
        /// in 45-degree steps, and a blueprint that came back from a save
        /// facing a direction nobody chose would be a different building.
        public float yaw;
        /// Metres along the ridge, for a plan whose length the ground chose
        /// (a pier). 0 means the plan's own footprint. See `BuildPlan.WithLength`.
        public float length;

        /// **Hand-days of LABOUR spent standing it up, 2026-09-23.**
        ///
        /// Kevin: *"first the villagers should gather all the resources
        /// necessary to build the building, THEN they start actually
        /// building it which shouldn't take super long."* So a site has two
        /// phases and this is the second one's clock. It accrues ONLY once
        /// `Stocked` is true (`OutpostLedger.PayBuild`), which is the whole
        /// of the change: before today the build-time half was folded into
        /// the haul and a site was "done" the instant the last log landed.
        ///
        /// Measured in hand-days, so three builders spend it three times as
        /// fast without this field knowing how many there are.
        public float built;

        /// **One-shot migration marker.** False in every save written before
        /// the two phases existed; `OutpostLedger.MigratePending` reads it
        /// once, decides what `built` should be for that old row, and sets
        /// it. See the note there.
        public bool phased;

        // --- the CLEAR phase (2026-09-23) --------------------------------------
        //
        // Kevin: *"any blueprint can be placed over any trees or small rocks
        // etc and the area will be cleared by the villagers before buildings
        // begin construction."* So a site now has THREE clocks: clearing,
        // stocking, building. Clearing and stocking can overlap (the haulers
        // go on filling the stack while the plot is cut), building waits on
        // both.
        //
        // **What is saved is COUNTS, never tree indices** -- the same trick
        // the camp's wood plays with `treesFelled`. Which trees and rocks
        // those counts describe is re-derived from the row's footprint and
        // the island's geometry on every visit (`Outpost.SyncClearing`), in
        // a fixed order (nearest the camp first), so the first
        // `floor(clearDone)` of them are down on any load, however the
        // terrain streamed in.
        //
        // A save written before this restores all of them as 0/false, which
        // reads as "nothing to clear" -- `Cleared` is true and an old row
        // builds exactly as it did yesterday.

        /// Standing trees / stone-or-ore rocks inside the footprint when the
        /// row was sited. Cleared in that order: every tree, then every rock
        /// (stone rocks before ore rocks, see `clearOre`).
        public int clearTrees, clearRocks;
        /// How many of `clearRocks` are ORE rather than stone -- the last
        /// ones cleared, so the ledger can book the right resource for a
        /// rock without a scene to look at. Additive to the contract; 0 on
        /// every old save.
        public int clearOre;
        /// Obstructions removed so far, fractional. The integer part is how
        /// many are DOWN; the fraction is the one being worked.
        public float clearDone;
        /// **This row owns its footprint's trees and rocks.** Set only by a
        /// fresh siting (`Outpost.SiteFresh`, `Outpost.SiteWall`) -- never
        /// by a gate or a repair on a line that already stands, and never by
        /// an old save -- so the clearing registry only claims ground for
        /// rows that were counted. Without it an old save's queued row
        /// would drop every tree in its footprint on first load.
        public bool clearSited;

        /// Obstructions this site had to clear, in all.
        public int ClearTotal => clearTrees + clearRocks;
        /// Obstructions still standing. Never negative.
        public int ClearLeft => Mathf.Max(0, ClearTotal - Mathf.FloorToInt(clearDone));
        /// Nothing left standing on the plot: building may begin.
        public bool Cleared => ClearLeft == 0;
        /// Trees still standing -- trees go first, so this is the whole of
        /// `ClearLeft` until the last tree is down.
        public int TreesLeft => Mathf.Max(0, clearTrees - Mathf.FloorToInt(clearDone));
        /// Rocks still standing.
        public int RocksLeft => Mathf.Max(0, ClearLeft - TreesLeft);

        // --- the wall half (Phase 1, 2026-09-23) -----------------------------

        /// **This row is a WALL SEGMENT, not a building on a plot.**
        ///
        /// A wall site goes through the whole of the existing pipeline --
        /// it is queued in `sites`, stocked by the haulers, raised by
        /// `Outpost.FinishReady` -- and the only thing that differs is
        /// that its ground is a LINE rather than a rectangle. So rather
        /// than a second queue with a second set of rules (and a second
        /// set of bugs), it is the same row with three more fields on it,
        /// and `x`/`z` are the segment's MIDPOINT so every reader that
        /// already knows where a site is (the hauler's destination, the
        /// sheet's anchor, the blueprint's transform) goes on being right
        /// without knowing walls exist.
        ///
        /// A save written before this reads all three back as their
        /// constructed values -- `false` and two zero vectors -- which is
        /// exactly "this one was not a wall".
        public bool isWall;
        /// The two posts, world metres, ground-snapped. For a GATE row,
        /// the posts of the segment it replaces.
        public Vector3 postA, postB;

        /// **This row REPLACES a gap, not a whole run (2026-09-26).**
        /// Kevin: "place the blueprint only for the section being
        /// replaced" -- set by `Outpost.QueueRepair`, never by a fresh
        /// `SiteWall`, and read by `Outpost.EnsureBlueprints` to draw the
        /// partial ghost instead of the full one. A save written before
        /// this restores it as `false`; `EnsureBlueprints` also asks
        /// `WallOn` whether a segment already stands on these posts, so an
        /// old save's already-queued repair still draws the partial ghost
        /// even though this flag came back empty.
        public bool isRepair;

        public float WallLength => (postB - postA).magnitude;

        public Vector3 At => new Vector3(x, 0f, z);

        /// **Every material is in.** What `Complete` used to mean, and what
        /// the STOCKING phase ends on. A plan with no stone price is stocked
        /// on its timber exactly as it always was.
        public bool Stocked => done >= needed && stoneDone >= stoneNeeded
            && brickDone >= brickNeeded;

        /// Hand-days of labour this plan's size asks for once it is stocked.
        public float LabourNeeded => OutpostLedger.LabourFor(this);

        /// How far through the BUILDING phase, 0..1. **Zero until every
        /// material is in and the plot is clear** (Kevin, phone playtest
        /// 2026-09-23: *"get the resources (5/5 wood, 3/3 stone) then they
        /// start hammering"*) -- `built` only accrues then anyway, but an
        /// old save's row can carry a `built` that must not show early.
        public float Build01 => !(Stocked && Cleared) ? 0f
            : LabourNeeded <= 0f ? 1f : Mathf.Clamp01(built / LabourNeeded);

        /// Stocked AND stood up: the row may leave the queue and become a
        /// building. Two phases, so this is no longer the same question as
        /// "has everything been delivered" -- that is `Stocked`.
        ///
        /// **And cleared, since 2026-09-23**: a building does not go up on
        /// ground with trees still standing on it. `Cleared` is true on every
        /// row from an old save, so this is the old answer for those.
        public bool Complete => Stocked && Cleared && built >= LabourNeeded;

        /// **What has been delivered against what it wants, 0..1, WHOLE
        /// units only** -- timber, stone and brick summed. A picture of the
        /// stack (the log pile, the ghost's tint), never a percentage: the
        /// percentage is `Progress01`, which is the labour alone.
        /// (2026-09-23 phone playtest: the fractional `*DonePart` fields used
        /// to count here, so the bar crept up with nothing delivered.)
        public float Fill01
        {
            get
            {
                float want = needed + stoneNeeded + brickNeeded;
                if (want <= 0f) return 1f;
                return Mathf.Clamp01((Mathf.Min(done, needed) + Mathf.Min(stoneDone, stoneNeeded)
                     + Mathf.Min(brickDone, brickNeeded)) / want);
            }
        }

        /// **The site's percentage, 0..1: the BUILDING phase alone.** 0 %
        /// while it is being cleared and stocked -- the stocking is shown as
        /// counts ("3/5 timber"), not as a creeping percentage -- then 0 to
        /// 100 over the hammering. Kevin, 2026-09-23: *"the percentages also
        /// start going up before resource quota is met."*
        public float Progress01 => Build01;

        /// **What the site is doing, in the words the sheets print.**
        /// "stocking 3/6 logs, 2/2 stone" then "building 40%".
        public string PhaseLine
        {
            get
            {
                // The clear phase leads the line while it lasts: it is the
                // thing the camp is visibly doing on the plot.
                string clearing = "";
                if (!Cleared)
                {
                    int t = TreesLeft, r = RocksLeft;
                    var bits = new System.Collections.Generic.List<string>(2);
                    if (t > 0) bits.Add(t == 1 ? "1 tree" : $"{t} trees");
                    if (r > 0) bits.Add(r == 1 ? "1 rock" : $"{r} rocks");
                    clearing = "clearing " + string.Join(", ", bits);
                    if (Stocked) return clearing;
                    clearing += "; ";
                }
                if (Stocked)
                    return built >= LabourNeeded
                        ? "going up"
                        : $"hammering {Mathf.RoundToInt(Build01 * 100f)}%";
                var parts = new System.Collections.Generic.List<string>(3);
                if (needed > 0) parts.Add($"{Mathf.Min(done, needed)}/{needed} logs");
                if (stoneNeeded > 0) parts.Add($"{Mathf.Min(stoneDone, stoneNeeded)}/{stoneNeeded} stone");
                if (brickNeeded > 0) parts.Add($"{Mathf.Min(brickDone, brickNeeded)}/{brickNeeded} brick");
                return clearing + (parts.Count == 0 ? "stocking" : "stocking " + string.Join(", ", parts));
            }
        }

        /// The timber part, on its own -- what the log stack beside the
        /// blueprint is drawn from.
        public bool TimberPaid => done >= needed;
        public bool StonePaid => stoneDone >= stoneNeeded;
        public bool BrickPaid => brickDone >= brickNeeded;
    }

    /// **A building that stands here, and WHERE.**
    ///
    /// `OutpostLedger.built` is the list of plan ids and it is what every
    /// count reads; this is the row a save restores the object from. It is a
    /// separate list rather than a change to `built` because the probes write
    /// `built` by hand (`l.built.Add(id)`) and a schema that broke them all
    /// on the day the save arrived would be the save system's first bug.
    /// `Outpost.Raise` records one of these for everything it stands up, and
    /// `Outpost.Adopt` re-raises from it -- at the spot, not from the spiral,
    /// which would move every hut on load.
    [System.Serializable]
    public class BuiltBuilding
    {
        public string planId;
        /// World metres. Height is re-read from the field on load.
        public float x, z;
        /// World degrees, the way `PendingBuild.yaw` is.
        public float yaw;
        /// Metres along the ridge, the way `PendingBuild.length` is: 0 for
        /// a plan of its own size, the chosen length for a pier.
        public float length;

        /// **This building's own level, 2026-09-27** (Kevin: "they have
        /// their own levels, always"). 0 = a row from a save written before
        /// levels were per building: it reads the plan's old shared level
        /// (`OutpostLedger.levels`), so nothing loads downgraded. Every row
        /// recorded since is born 1, and `Upgrade` writes only its own row.
        /// It lives on the row so `Outpost.Demolish` taking a row out takes
        /// that building's level with it and no other copy's shifts.
        public int level;

        public Vector3 At => new Vector3(x, 0f, z);
    }

    /// **A wall segment that STANDS, in the record (2026-09-23).**
    ///
    /// `BuiltBuilding` cannot carry one: a building is a point and a yaw,
    /// a segment is two posts, and a segment also has a state a building
    /// does not (how much of it is left). `Outpost.Adopt` re-creates one
    /// `WallSegment` per row here and re-marks the pathing grid from them,
    /// so a camp loaded mid-raid comes back with its breach still in it.
    [System.Serializable]
    public class BuiltWall
    {
        /// World metres. Heights are re-read from the field on load, the
        /// way `BuiltBuilding` re-reads its own.
        public float ax, az, bx, bz;
        public bool isGate;
        /// What is left of it. 0 means breached; a save written with a
        /// breached segment brings the breach back.
        public float hp;
        public float maxHp;

        public Vector3 A => new Vector3(ax, 0f, az);
        public Vector3 B => new Vector3(bx, 0f, bz);
    }

    /// **Names for a hand nobody shipped, 2026-09-21.**
    ///
    /// A cast of twenty came off the manifest with names already; a hand
    /// recruited on the beach has to get one from somewhere. Picked
    /// deterministically off a hash of the camp's key and its roster size
    /// rather than `Random`, so the SAME camp reaching the SAME headcount
    /// twice -- once live, once replayed from a save -- names its newcomer
    /// the same both times.
    public static class VillagerNames
    {
        /// ~24 short storybook names, the register the manifest's own crew
        /// names are already in. They live in `Crew.CrewNames` since
        /// 2026-09-22: the ship's yard names her new berths out of the same
        /// hat, and two hats meant two people called Bo.
        static string[] Names => SeaSick.Crew.CrewNames.Pool;

        /// The name for the next hand this ledger recruits. Skips anybody
        /// already on this roster AND anybody already answering to that name
        /// anywhere else -- aboard, or at another camp -- so a hand carried
        /// onto the ship never meets his own name there.
        public static string NextFor(OutpostLedger ledger)
        {
            int seed = ledger.keyX * 73856093 ^ ledger.keyZ * 19349663
                ^ ledger.hands.Count * 83492791;
            uint h = unchecked((uint)seed);
            var used = SeaSick.Crew.CrewNames.InUse();
            for (int i = 0; i < Names.Length; i++)
            {
                string candidate = Names[(int)((h + (uint)i) % (uint)Names.Length)];
                if (ledger.Hand(candidate) == null && !used.Contains(candidate)
                    && !SeaSick.World.Life.Lives.IsTaken(candidate))
                    return candidate;
            }
            // All 24 spoken for: keep recruiting rather than stall on a
            // naming collision nobody designed for.
            for (int i = 1; i < 999; i++)
            {
                string candidate = "Hand " + i;
                if (ledger.Hand(candidate) == null && !used.Contains(candidate)
                    && !SeaSick.World.Life.Lives.IsTaken(candidate))
                    return candidate;
            }
            return "Hand " + (ledger.hands.Count + 1);
        }
    }

    /// **The outpost IS this object. The crew you can see are a rendering of
    /// it.**
    ///
    /// The whole design turns on that inversion. If the walking, chopping
    /// agents were what produced timber, then an island would only pay while
    /// the player stood and watched it — which is the exact opposite of a loop
    /// built around sailing away. So production is arithmetic over elapsed
    /// game time, and a crewman carrying a log is the animation of an
    /// increment that already happened.
    ///
    /// Plain serialisable data with no MonoBehaviour and no scene reference:
    /// an outpost has to keep working while its island is three kilometres
    /// astern and its terrain has streamed out, and it has to survive a save.
    /// It was built to be savable before there was a writer for it (D4),
    /// because retro-fitting serialisation onto live component state is the
    /// expensive version of this job; since 2026-09-21 `Save/SaveGame`
    /// writes it into the save file exactly as it is.
    [System.Serializable]
    public partial class OutpostLedger
    {
        // --- identity --------------------------------------------------------

        /// Rounded world position of the camp, in metres.
        ///
        /// **Not an island index.** Islands are discovered by flood-fill in
        /// whatever order the streamer found them, so an index is not an
        /// identity and a camp keyed to one would silently move house after any
        /// change to the streamer. The seed is stable; the ordering is not.
        public int keyX, keyZ;

        public static int KeyOf(float v) => Mathf.RoundToInt(v);
        public void SetKey(Vector3 at) { keyX = KeyOf(at.x); keyZ = KeyOf(at.z); }
        public bool Matches(Vector3 at) => keyX == KeyOf(at.x) && keyZ == KeyOf(at.z);

        // --- who is here -----------------------------------------------------

        public List<OutpostHand> hands = new List<OutpostHand>();

        public int HandsOn(OutpostOrder order)
        {
            int n = 0;
            foreach (var h in hands) if (h != null && h.order == order) n++;
            return n;
        }

        public int HandsOn(OutpostOrder order, string target)
        {
            int n = 0;
            foreach (var h in hands)
                if (h != null && h.order == order && h.target == target) n++;
            return n;
        }

        public OutpostHand Hand(string who)
        {
            foreach (var h in hands) if (h != null && h.name == who) return h;
            return null;
        }

        // --- what it holds ---------------------------------------------------

        /// **What is on the ground here, per resource.**
        ///
        /// It used to be one integer called `timber`, because timber was the
        /// only thing an island had. Kevin, 2026-09-19: *"crew on the island
        /// can gather resources up to 10 of each without a storage unit."*
        /// So the ceiling is PER RESOURCE and the pile is a list.
        public List<OutpostStore> stores = new List<OutpostStore>();

        /// What is left in the ground, per resource. Seeded from the survey.
        public List<OutpostStock> stocks = new List<OutpostStock>();

        /// **What this place can keep OF EACH THING.** A campfire watches over
        /// ten of anything; a storehouse is how you raise it. This ceiling is
        /// the whole reason the loop does not become an idle game: hands fill
        /// it and stop, so the only way to get more out of an island is to
        /// invest in it.
        public int ceilingPer = CampfireCeiling;

        public OutpostStore Store(string resource, bool create = false)
        {
            foreach (var s in stores) if (s != null && s.resource == resource) return s;
            if (!create) return null;
            var made = new OutpostStore { resource = resource };
            stores.Add(made);
            return made;
        }

        public OutpostStock Stock(string resource, bool create = false)
        {
            foreach (var s in stocks) if (s != null && s.resource == resource) return s;
            if (!create) return null;
            var made = new OutpostStock
            {
                resource = resource,
                regrowPerDay = Res.RegrowPerDay(resource),
            };
            stocks.Add(made);
            return made;
        }

        /// **Everything the camp holds of this, 2026-09-23** -- the DISPLAYED
        /// camp total: the store PLUS every station's bay, finished bench and
        /// output rack. Loads in hands' arms are not in it (`CarriedOf`). The
        /// store alone is `StoreCountOf`. Not what a cost may spend -- a bay
        /// is a station's queued input -- that is `SpendableOf`.
        public int CountOf(string resource)
        {
            var s = Store(resource);
            return (s != null ? s.whole : 0) + StationCountOf(resource);
        }

        /// **What a cost may spend** (2026-09-23): the store, station racks
        /// and finished benches -- exactly where `Take` draws, never bays.
        /// Every affordability gate reads this (fire raise, upgrades,
        /// recruiting, the lookout's volley).
        public int SpendableOf(string resource)
        {
            var s = Store(resource);
            return (s != null ? s.whole : 0) + StationSpendableOf(resource);
        }

        /// Room left in the STORE for this resource, in whole units, net of
        /// loads already walking there (a haul reserves its room at pickup).
        /// The ceiling is the store's; station stock does not use it up.
        public int RoomFor(string resource) =>
            Mathf.Max(0, ceilingPer - StoreCountOf(resource) - InFlightTo(HaulPlace.Store, -1, resource));

        /// Whole and part together -- what a tool check or a recipe's "have"
        /// arithmetic wants, since a saw blade at 0.95 is still a saw blade.
        /// Store and station RACKS (never bays), the places `DrawHeld`
        /// wears from, so a check never passes on stock the wear cannot take.
        float HeldOf(string resource)
        {
            var s = Store(resource);
            return (s != null ? s.whole + s.part : 0f) + StationHeldOf(resource);
        }

        /// Put whole units in, refusing what will not fit. Returns what was
        /// taken.
        public int Add(string resource, int n)
        {
            if (n <= 0) return 0;
            int took = Mathf.Min(RoomFor(resource), n);
            if (took <= 0) return 0;
            Store(resource, true).whole += took;
            return took;
        }

        /// Take whole units out: the store first, then station racks and
        /// finished benches (`SpendableOf`). **Never a bay** -- a station's
        /// queued input is not the camp's to spend. Returns what was
        /// actually there.
        public int Take(string resource, int n)
        {
            if (n <= 0) return 0;
            var s = Store(resource);
            int got = s != null ? Mathf.Min(s.whole, n) : 0;
            if (s != null) s.whole -= got;
            if (got < n) got += TakeFromStations(resource, n - got);
            return got;
        }

        /// **The store's own units only, never a station's rack or bay**
        /// (death/rescue phase 10): a hand arming up from the pile takes a
        /// physical spear out of the STORE, same as `StoreCountOf` counts --
        /// a fletcher's queued input is not the camp's armoury. Returns what
        /// was actually taken (0 or 1 in practice, but not assumed).
        public int TakeFromStore(string resource, int n)
        {
            if (n <= 0) return 0;
            var s = Store(resource);
            int got = s != null ? Mathf.Min(s.whole, n) : 0;
            if (s != null) s.whole -= got;
            return got;
        }

        /// Everything on the ground, all kinds together — what a hold has to
        /// have room for.
        public int Total
        {
            get
            {
                int n = 0;
                foreach (var s in stores) if (s != null) n += s.whole;
                return n + StationTotal();
            }
        }

        /// Timber, by name, because half the game still asks for it directly.
        public int Timber => CountOf(Res.Timber);

        /// The sub-log accrual on the timber pile. Only the probes care, and
        /// they care a great deal: it is what makes ticking often and ticking
        /// rarely agree.
        public float TimberPart()
        {
            var s = Store(Res.Timber);
            return s != null ? s.part : 0f;
        }

        /// **Timber taken out of the GROUND here, ever.** Not what is in the
        /// pile — what has been cut, including everything carried off by the
        /// ship and everything burnt into a building.
        ///
        /// This is the number the wood is drawn from. Kevin, playing it:
        /// *"the trees never disappear. i assume they would since they're cut
        /// down."* They did not, because gathering only ever decremented an
        /// abstract stock. The plan settled this on 2026-09-13 and it was
        /// never built: **fell deterministically, nearest the camp outward,
        /// and store only a COUNT** — then the whole visible state reproduces
        /// from one integer on any visit, whatever the terrain did in between.
        public float timberTaken;

        /// How many trees have been felled to represent that. Whole trees, so
        /// a visit that arrives to find the mesh untouched knows exactly how
        /// many to take down.
        public int treesFelled;

        /// **How much of that wood has grown back**, in trees, fractions and
        /// all. Kevin, 2026-09-22: *"they should re-grow further away from
        /// camp, to help the camp not get overgrown."*
        ///
        /// A second count rather than a smaller `treesFelled`, because
        /// `timberTaken` is what has been cut EVER and the felling is driven
        /// off it: walking `treesFelled` backwards would only make the next
        /// tick take the same trees down again. So the two are kept apart --
        /// what was cut, and what has come back -- and the picture is the
        /// difference. Which trees come back is geometry and lives in
        /// `Outpost.DrawWood`: the far ones first, never the camp's own
        /// clearing.
        public float treesRegrown;

        /// **Declared now, consumed in a later pass.** Food is settled as
        /// local — berries and wheat off the island itself, no supply run —
        /// and over-capacity hands eat stores and can starve. None of that is
        /// wired: a farm now MAKES `Res.Food`, but nothing eats it yet.
        public float foodEaten;

        // --- upkeep: eating and recruiting, 2026-09-21 ------------------------
        //
        // GDD 6, Upkeep: "huts cap supported hands ... neglected hands get
        // angry." This is the first half of that -- feeding what is
        // already here, and growing the roster to fill the beds a hut
        // buys. The anger is still parked on `OutpostHand.mood`.

        /// Food one hand ashore eats per game day. Distinct from
        /// `FoodPerHandPerDay` above, which is what one FARMHAND produces --
        /// this is what every hand, farmhand or not, consumes. **A
        /// placeholder, never played.**
        public const float EatPerHandPerDay = 1f;

        /// **The ration the player has set, 2026-09-22.** Plain serialised
        /// field, not a property, so `JsonUtility` saves it the same free way
        /// it already saves `OutpostHand.order` -- an enum round-trips as its
        /// underlying int with no extra plumbing.
        public Rations rations = Rations.Full;

        /// What `rations` actually pays out, against `EatPerHandPerDay`.
        public float EatMultiplier => rations switch
        {
            Rations.Full => 1f,
            Rations.Half => 0.5f,
            _ => 0f,
        };

        /// **The work priority the player has set, 2026-09-22 -- a camp you
        /// can point.** Plain serialised field for the same JsonUtility
        /// reason as `rations`.
        public WorkPriority priority = WorkPriority.Even;

        /// What `priority` does to one resource's rate: Food and Timber trade
        /// a fifth for a quarter against each other; everything else, and
        /// `Even`, is untouched. Read by both the arithmetic (`Step`) and the
        /// readouts (`RatePerDay`/`MakeRatePerDay`) so the sheet never prints
        /// a number the tick would not pay.
        public float PriorityMultiplier(string resource)
        {
            if (priority == WorkPriority.Even) return 1f;
            bool boostFood = priority == WorkPriority.FoodFirst;
            if (Economy.FoodBook.IsFoodish(resource)) return boostFood ? 1.25f : 0.8f;
            if (resource == Res.Timber) return boostFood ? 0.8f : 1.25f;
            return 1f;
        }

        // --- upkeep: mood, 2026-09-22 ------------------------------------
        //
        // Kevin's design, settled: the campfire IS the provisions gauge and
        // failure is gradual -- stores run out, hands stop pulling full
        // weight and forage for themselves instead, and they get ANGRY.
        // That is the whole punishment; nobody leaves.

        /// How much a day wholly unfed knocks a hand's mood down. Two
        /// unfed days take a content hand (mood 1) to furious (mood 0).
        /// **A guess, never played.**
        public const float MoodDropPerHungryDay = 0.5f;

        /// How much a day fully fed brings mood back up. Four fed days
        /// walk a furious hand back to content. **A guess, never played.**
        public const float MoodRecoverPerFedDay = 0.25f;

        /// Days of food in the pile, per hand, that reads as a bright
        /// fire on `Health01`. **A guess, never played.**
        public const float DaysOfFoodForBrightFire = 3f;

        /// **How much of a day's work this hand actually does, applied to
        /// PRODUCTION only -- never to eating.** A hand at mood 0.5 or
        /// better works flat out; below that they spend the rest of the
        /// day foraging for themselves instead of the camp, scaling to
        /// nothing at mood 0. So a starving camp does not stop dead, it
        /// just gets slower, which is what makes the decline something the
        /// player can see coming and catch.
        // (`walkingIn` no longer zeroes the factor, 2026-09-27: every trip
        // is walked from where the hand really is -- a man still coming up
        // from the gangway starts his first trip from the gangway -- and
        // bench/site work needs him standing there. See
        // docs/DELIVERY-ON-ARRIVAL.md.)
        public static float WorkFactor(OutpostHand h) =>
            h == null || h.Busy ? 0f
                : Mathf.Max(StarvingWorkFloor, Mathf.Clamp01(h.mood / 0.5f)) * (1f + MealWorkBonus(h));

        /// **Decision for Kevin to review (phase 4, 2026-09-28):** a
        /// pouting hand is zeroed here through `Busy`, same as `downed`.
        /// The OLDER below-half-mood slow-down below (`StarvingWorkFloor`,
        /// the 35% floor a merely-angry-but-not-yet-pouting hand works at)
        /// is left exactly as it was, not replaced -- so mood still costs a
        /// camp output before anybody ever reaches the fire, and pouting is
        /// the harder stop on top of that once the cooldown lets it fire.
        /// Whether the two together read as "enough consequence" or as one
        /// system doing the other's job twice is a play call, not a code
        /// one.
        ///
        /// **Hunger slows a hand; it does not stop one, 2026-09-23.** Kevin
        /// chose it after the stuck-buildings repro: at mood 0 the old
        /// factor was exactly zero, so a hungry camp froze with everybody
        /// "assigned". Mood still falls, the camp still suffers for it, and
        /// the work still moves -- at about a third of the pace.
        public const float StarvingWorkFloor = 0.35f;

        /// Days of rations each hand brings ashore from the ship
        /// (`Outpost.Station`), so a fresh camp gets its first buildings up
        /// before it has to feed itself. Kevin, 2026-09-23.
        public const float ProvisionDays = 3f;

        /// `WorkFactor`, except that **a hand bringing in food is never
        /// docked** -- foraging IS gathering food, so a starving camp told
        /// to gather berries or work its farm can still eat its way back.
        /// Without this a camp that ran out once could never recover: the
        /// hungrier they got the less food they brought in.
        public static float WorkFactorOn(OutpostHand h, string produces) =>
            h != null && h.Busy ? 0f
                : Economy.FoodBook.IsFoodish(produces) ? (h == null ? 0f : 1f) : WorkFactor(h);

        /// **The pace this hand's CURRENT job is paid at, 0..1** -- the
        /// factor `Step` actually scales his day by, dispatched the way
        /// `Step` dispatches it: a gatherer (trips or hunt) and a farmhand
        /// at `WorkFactorOn` of what they bring in (food is never docked);
        /// a builder, a hauler and every stationed worker (the kitchen
        /// included -- `StepStations` pays the bench at plain `WorkFactor`)
        /// at `WorkFactor`. Hunger/mood only; the camp's `priority` is a
        /// choice, not a slowdown, and is left out. 2026-09-24, so the
        /// sheets can say why a villager is slow (`StallReason`).
        public float WorkFactorOf(OutpostHand h)
        {
            if (h == null || h.walkingIn) return 0f;
            if (h.order == OutpostOrder.Gather && !string.IsNullOrEmpty(h.target))
                return WorkFactorOn(h, h.target == Res.Game ? Res.Food : h.target);
            if (h.order == OutpostOrder.Work && !string.IsNullOrEmpty(h.target) && !IsStation(h.target)
                && Conversion(h.target, out string makes, out _, out _, out _, out _, out _))
                return WorkFactorOn(h, makes);
            return WorkFactor(h);
        }

        /// Below this pace the hand's line says he is working slowly, and why.
        public const float SlowWorkShown = 0.9f;

        /// Is anybody here going hungry right now -- the pile has nothing
        /// in it and there is somebody to feed. What `Step`'s eating block
        /// is about to find, a step early, for anything that wants to warn
        /// ahead of the tick rather than after it.
        public bool Hungry
        {
            get
            {
                if (hands.Count == 0) return false;
                return BestMeal() == null && FoodFill() <= 0f;
            }
        }

        /// **What `Outpost` pushes to `Campfire.health01`.** Three days of
        /// food banked, per hand, reads as a bright fire; an outpost with
        /// nobody home reads as full so an empty camp does not look like a
        /// dying one. **Rations-honest, 2026-09-22**: the bar wants fewer
        /// days' worth of food when a half ration means fewer days' worth is
        /// actually spent, and a camp on no rations at all never reads
        /// bright no matter how the pile is stacked -- nobody there is being
        /// fed, whatever is sitting beside the fire.
        public float Health01
        {
            get
            {
                if (hands.Count == 0) return 1f;
                if (rations == Rations.None) return 0f;
                return Mathf.Clamp01(FoodFill()
                    / (hands.Count * EatPerHandPerDay * EatMultiplier * DaysOfFoodForBrightFire));
            }
        }

        /// How many hands here are furious. What the sheet counts against
        /// the roster.
        public int AngryCount
        {
            get
            {
                int n = 0;
                foreach (var h in hands) if (h != null && h.Angry) n++;
                return n;
            }
        }

        /// Days of accumulated progress toward the next recruit spends.
        public const float DaysPerRecruit = 3f;

        /// Food the pile must hold before a recruit will start accruing --
        /// and what recruiting the hand actually spends.
        public const int RecruitFoodCost = 3;

        /// Days accrued toward the next hand. Reset (less `DaysPerRecruit`)
        /// each time a hand is born. Does not accrue without a free bed and
        /// food in the pile -- see `Step`.
        public float recruitProgress;

        /// **Shortfall, in days, that has gone unfed.** Nobody starves or
        /// leaves on this yet -- that is the parked "neglect/anger" feature
        /// GDD 6 names -- but the debt is counted from the day it is first
        /// owed, so the feature has something true to read when it is built.
        public float hungerDays;

        /// Beds this camp has, summed over every plan raised here.
        /// `built` and `raised` grow one entry per building together (see
        /// `Outpost.Raise`), so `built` alone is enough to count from.
        public int HousingCapacity
        {
            get
            {
                int n = 0;
                // Per building (2026-09-27): the k-th `built` id of a plan
                // is its k-th copy, each at its own level.
                for (int i = 0; i < built.Count; i++)
                {
                    string id = built[i];
                    int k = 0;
                    for (int j = 0; j < i; j++) if (built[j] == id) k++;
                    n += BuildPlans.Named(id).houses + Economy.Techs.HousesBonus(id, LevelOf(id, k));
                }
                return n;
            }
        }

        /// Hands living here, housed or not -- `hands.Count` by another
        /// name, for the sheet.
        public int Housed => hands.Count;

        /// How far along the next recruit is, 0..1.
        public float RecruitProgress01 => DaysPerRecruit > 0f
            ? Mathf.Clamp01(recruitProgress / DaysPerRecruit) : 0f;

        /// One line for the sheet: what stands between this camp and its
        /// next hand.
        public string RecruitLine
        {
            get
            {
                int cap = HousingCapacity;
                if (cap <= 0) return "no beds";
                if (Housed >= cap) return $"{Housed} of {cap} beds";
                if (FoodFill() < RecruitFoodCost) return "no food to feed a newcomer";
                float daysLeft = Mathf.Max(0f, DaysPerRecruit - recruitProgress);
                return $"{Housed} of {cap} beds · a new hand in {daysLeft:0.#} days";
            }
        }

        // --- warmth: a hut near the fire, 2026-09-27 --------------------------
        //
        // Kevin's item "1" alongside item 12's item "4" (the walk label):
        // GDD's item 12 shipped the label and left "an aura or morale
        // incentive for building close" as his call, separately. This is
        // that call. No per-hand bed assignment exists anywhere in this file
        // -- `Housed` is just `hands.Count` against `HousingCapacity` -- so
        // warmth is handed out the same way beds always implicitly have
        // been: first come, first served, by a hand's own place in `hands`,
        // against however many warm beds stand right now.

        /// Metres a Hut can stand from the fire (`keyX`/`keyZ`) and still
        /// keep its residents warm. `keyX`/`keyZ` are `SetKey`'s own
        /// rounding of the fire's position to the metre -- plenty precise
        /// against a 30 m line.
        public static float WarmHutRadius => Economy.EconomyTuning.WarmHutRadius;   // tuning asset, 2026-09-27

        /// Provisional, unplayed, 2026-09-27: mood a warm hand gains per
        /// day, on top of the ordinary hunger arithmetic below, climbing
        /// toward the same cap (`mood`'s own ceiling of 1) that recovery
        /// already climbs toward -- no second cap to invent. Mood over 0.5
        /// buys nothing more from `WorkFactor` (it already saturates
        /// there), so in practice this speeds a hungry hand's recovery
        /// rather than helping a content one.
        public static float WarmMoodBonusPerDay => Economy.EconomyTuning.WarmMoodBonusPerDay;   // tuning asset

        /// Is the `raised` row at `i` a Hut standing within `WarmHutRadius`
        /// of the fire? Straight-line distance -- the same measure a Hut's
        /// siting ghost judges itself by (`CampSiting.WarmthLabel`), since
        /// neither has a ground route worth asking `CampPath` for over a
        /// line this short.
        bool IsWarmRow(int i)
        {
            var b = raised[i];
            if (b == null || b.planId != BuildPlans.Hut.id) return false;
            float dx = b.x - keyX, dz = b.z - keyZ;
            return dx * dx + dz * dz <= WarmHutRadius * WarmHutRadius;
        }

        /// Warm beds this camp has right now: `houses` (plus any tech
        /// bonus, at that building's own level) summed over every Hut
        /// standing within `WarmHutRadius`, upgraded or not -- an upgraded
        /// hut is still warm if it is still close. Order matches `raised`,
        /// which is the order `IsHandWarm` fills from.
        public int WarmBedCapacity
        {
            get
            {
                int n = 0;
                for (int i = 0; i < raised.Count; i++)
                    if (IsWarmRow(i))
                        n += BuildPlans.Named(raised[i].planId).houses
                            + Economy.Techs.HousesBonus(raised[i].planId, LevelAtRaised(i));
                return n;
            }
        }

        /// Huts standing inside `WarmHutRadius` of the fire -- the Campfire
        /// card's "warm ring" count (2026-09-27).
        public int WarmHutCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < raised.Count; i++) if (IsWarmRow(i)) n++;
                return n;
            }
        }

        /// Is `hands[handIndex]` one of the warm ones? Deterministic: warm
        /// beds fill first, by hand order, the same "no assignment table"
        /// shortcut `HousingCapacity`/`Housed` already take for beds in
        /// general.
        public bool IsHandWarm(int handIndex) => handIndex >= 0 && handIndex < WarmBedCapacity;

        /// Is `h` one of the warm ones? Reads its place in `hands`; -1
        /// (not on this roster) always reads cold.
        public bool IsHandWarm(OutpostHand h) => IsHandWarm(hands.IndexOf(h));

        // --- the fire: tech tree, building levels, recipes, 2026-09-23 -------
        //
        // Kevin: *"to hunt, you need a spear."* The tree hangs off the fire,
        // not off any building raised or ship sailed, so it reads the same
        // whether a camp is visited once or ten times a session. Backed by
        // the data in `SeaSick.World.Economy` (`Techs`, `Recipes`, `Cost`);
        // this is only the SAVED state and the arithmetic that spends it.

        /// Saved level. 0 (a fresh ledger, or a save from before the fire had
        /// levels) reads as 1 through `CampfireLevel`; nothing but
        /// `RaiseCampfire` ever writes this field.
        public int campfireLevel;

        /// 1 for a camp that has never raised its fire.
        public int CampfireLevel => Mathf.Max(1, campfireLevel);

        /// One recipe remembered per station plan. `JsonUtility` cannot
        /// serialise a Dictionary, so this is a `List<T>` of `[Serializable]`
        /// rows, same shape as every other saved table here.
        [System.Serializable]
        public class RecipeChoice { public string planId; public string recipeId; }
        public List<RecipeChoice> choices = new List<RecipeChoice>();

        /// **LEGACY since 2026-09-27**: one level per plan, from before each
        /// building had its own (`BuiltBuilding.level`). Kept and still read
        /// for a `raised` row whose `level` is 0 (an old save), and written
        /// only for a building with no `raised` row at all (a probe that
        /// wrote `built` by hand). Nothing else writes it.
        [System.Serializable]
        public class PlanLevel { public string planId; public int level; }
        public List<PlanLevel> levels = new List<PlanLevel>();

        /// "needs 3 more hide" -- the shortfall, `ShipPrices.CannotAfford`
        /// style: lower-case, no full stop, names every line short.
        static string DescribeShortfall(List<Economy.Ingredient> missing)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < missing.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(missing[i].n).Append(" more ").Append(Economy.ResDefs.Label(missing[i].res));
            }
            return sb.ToString();
        }

        // --- the fire itself ---

        public Economy.CampfireLevel NextCampfire => Economy.Techs.NextCampfire(CampfireLevel);

        public bool CanRaiseCampfire(out string why)
        {
            var next = NextCampfire;
            if (next == null) { why = "the fire is already at its top"; return false; }
            var missing = Economy.Cost.Missing(next.cost, SpendableOf);
            if (missing.Count > 0) { why = "needs " + DescribeShortfall(missing); return false; }
            why = null;
            return true;
        }

        public bool RaiseCampfire()
        {
            if (!CanRaiseCampfire(out _)) return false;
            var next = NextCampfire;
            foreach (var line in next.cost) Take(line.res, line.n);
            campfireLevel = next.level;
            return true;
        }

        public bool PlanUnlocked(string planId) => Economy.Techs.PlanLevel(planId) <= CampfireLevel;

        public string PlanLockReason(string planId)
        {
            int need = Economy.Techs.PlanLevel(planId);
            return need <= CampfireLevel ? null : $"needs the fire at {Economy.RecipeGraph.Roman(need)}";
        }

        // --- building levels (per building since 2026-09-27) ---
        //
        // Kevin: "they have their own levels, always." A building's level is
        // on its `raised` row; its ORDINAL is its place among its plan's
        // rows, the same key `StationStock.ordinal` uses, so a station and
        // its level always name the same building.

        /// The plan-wide level an old save kept, 1 if none.
        public int LegacyLevelOf(string planId)
        {
            if (levels != null)
                foreach (var l in levels)
                    if (l != null && l.planId == planId) return Mathf.Max(1, l.level);
            return 1;
        }

        /// The `raised` row of the `ordinal`-th building of `planId`, or -1.
        public int RaisedIndexOf(string planId, int ordinal)
        {
            if (raised == null || ordinal < 0) return -1;
            int k = 0;
            for (int i = 0; i < raised.Count; i++)
            {
                if (raised[i] == null || raised[i].planId != planId) continue;
                if (k == ordinal) return i;
                k++;
            }
            return -1;
        }

        /// Level of the building on `raised[raisedIndex]`. When the row is
        /// missing or is another plan's, `planId`'s legacy level.
        public int LevelAtRaised(int raisedIndex, string planId = null)
        {
            if (raised != null && raisedIndex >= 0 && raisedIndex < raised.Count)
            {
                var r = raised[raisedIndex];
                if (r != null && (planId == null || r.planId == planId))
                    return r.level > 0 ? r.level : LegacyLevelOf(r.planId);
            }
            return string.IsNullOrEmpty(planId) ? 1 : LegacyLevelOf(planId);
        }

        /// Level of the `ordinal`-th building of `planId` -- a station's own
        /// level is `LevelOf(s.planId, s.ordinal)`.
        public int LevelOf(string planId, int ordinal)
        {
            int i = RaisedIndexOf(planId, ordinal);
            return i >= 0 ? LevelAtRaised(i) : LegacyLevelOf(planId);
        }

        /// **The highest level any standing copy of `planId` has reached**
        /// -- the plan-wide question (a recipe's station-level gate, a sheet
        /// with no one building in hand). 1 when never upgraded. A single
        /// building's number is `LevelOf(planId, ordinal)` / `LevelAtRaised`.
        public int LevelOf(string planId)
        {
            int best = 0;
            if (raised != null)
                for (int i = 0; i < raised.Count; i++)
                    if (raised[i] != null && raised[i].planId == planId)
                        best = Mathf.Max(best, LevelAtRaised(i));
            return best > 0 ? best : LegacyLevelOf(planId);
        }

        /// The next step for the building on `raised[raisedIndex]`.
        public Economy.UpgradeStep NextUpgradeAt(int raisedIndex, string planId)
            => Economy.Techs.Upgrade(planId, LevelAtRaised(raisedIndex, planId) + 1);

        /// The lowest-level copy of `planId` (its `raised` index), or -1 --
        /// what the plan-wide `Upgrade(planId)` takes up.
        int LowestRaised(string planId)
        {
            int pick = -1, lo = int.MaxValue;
            if (raised != null)
                for (int i = 0; i < raised.Count; i++)
                    if (raised[i] != null && raised[i].planId == planId)
                    {
                        int lv = LevelAtRaised(i);
                        if (lv < lo) { lo = lv; pick = i; }
                    }
            return pick;
        }

        /// Plan-wide shims: the lowest copy is the one that goes up next.
        public Economy.UpgradeStep NextUpgrade(string planId) => NextUpgradeAt(LowestRaised(planId), planId);
        public bool CanUpgrade(string planId, out string why) => CanUpgradeAt(LowestRaised(planId), planId, out why);
        public bool Upgrade(string planId) => UpgradeAt(LowestRaised(planId), planId);

        /// Can THIS building (`raised[raisedIndex]`, a `planId`) go up a
        /// level? `raisedIndex` -1 is a building with no row (legacy).
        public bool CanUpgradeAt(int raisedIndex, string planId, out string why)
        {
            if (CountBuilt(planId) <= 0) { why = $"no {BuildPlans.Named(planId).label} stands here"; return false; }
            var next = NextUpgradeAt(raisedIndex, planId);
            if (next == null) { why = "already at its top"; return false; }
            if (CampfireLevel < next.campfireLevel)
            { why = $"needs the fire at {Economy.RecipeGraph.Roman(next.campfireLevel)}"; return false; }
            var missing = Economy.Cost.Missing(next.cost, SpendableOf);
            if (missing.Count > 0) { why = "needs " + DescribeShortfall(missing); return false; }
            why = null;
            return true;
        }

        /// Pay for and take THIS building up one level. Only its own row
        /// changes; its twins keep theirs.
        public bool UpgradeAt(int raisedIndex, string planId)
        {
            if (!CanUpgradeAt(raisedIndex, planId, out _)) return false;
            var next = NextUpgradeAt(raisedIndex, planId);
            foreach (var line in next.cost) Take(line.res, line.n);
            var row = raised != null && raisedIndex >= 0 && raisedIndex < raised.Count
                      && raised[raisedIndex] != null && raised[raisedIndex].planId == planId
                ? raised[raisedIndex] : null;
            if (row != null) row.level = next.toLevel;
            else
            {
                // No row to carry it (a hand-written `built`): the legacy
                // plan-wide row, as before 2026-09-27.
                if (levels == null) levels = new List<PlanLevel>();
                bool found = false;
                foreach (var l in levels)
                    if (l != null && l.planId == planId) { l.level = next.toLevel; found = true; break; }
                if (!found) levels.Add(new PlanLevel { planId = planId, level = next.toLevel });
            }
            // The ceiling is pushed in from the buildings on the ground
            // every `Outpost.CatchUp` (same as a fresh raise); nothing here
            // owns a scene reference to force that early, so a level-up's
            // extra store room shows on the very next tick, not this line.
            return true;
        }

        // --- recipes ---

        /// The chosen recipe, or the station's default; null for a station
        /// with no recipe table at all (a farm, a watchtower).
        ///
        /// **Since the orders (2026-09-23)** a station's ACTIVE order wins:
        /// what it is being told to make is what it makes. Without one, the
        /// remembered choice or the default -- what the sheet highlights.
        public Economy.Recipe RecipeAt(string planId)
        {
            var ordered = OrderedRecipe(planId);
            if (ordered != null) return ordered;
            return LegacyRecipeAt(planId);
        }

        /// The pre-order answer: remembered choice, else the default.
        Economy.Recipe LegacyRecipeAt(string planId)
        {
            if (!Economy.Recipes.StationHasRecipes(planId)) return null;
            string chosenId = null;
            if (choices != null)
                foreach (var c in choices)
                    if (c != null && c.planId == planId) { chosenId = c.recipeId; break; }
            var chosen = chosenId != null ? Economy.Recipes.Named(chosenId) : null;
            if (chosen != null && chosen.station == planId && RecipeAvailable(chosen, out _)) return chosen;
            return Economy.Recipes.Default(planId);
        }

        public bool ChooseRecipe(string planId, string recipeId)
        {
            var r = Economy.Recipes.Named(recipeId);
            if (r == null || r.station != planId || !RecipeAvailable(r, out _)) return false;
            if (choices == null) choices = new List<RecipeChoice>();
            bool found = false;
            foreach (var c in choices)
                if (c != null && c.planId == planId) { c.recipeId = recipeId; found = true; break; }
            if (!found) choices.Add(new RecipeChoice { planId = planId, recipeId = recipeId });
            // **Choosing IS ordering, 2026-09-23**: nothing is worked without
            // an order, and the station sheet's one tap is a player order --
            // so a chosen recipe is a REPEAT order on every station of this
            // plan. `PlaceOrder` is the full API (counts, per instance).
            EnsureStations();
            for (int i = 0; i < stations.Count; i++)
                if (stations[i] != null && stations[i].planId == planId)
                    PlaceOrder(i, recipeId, RepeatOrder);
            return true;
        }

        /// Fire level, station level, tool -- NOT missing inputs, which a
        /// chosen recipe simply waits on, same as the sawmill waits for
        /// timber.
        public bool RecipeAvailable(Economy.Recipe r, out string why)
        {
            if (r == null) { why = "no such recipe"; return false; }
            if (CampfireLevel < r.campfireLevel)
            { why = $"needs the fire at {Economy.RecipeGraph.Roman(r.campfireLevel)}"; return false; }
            if (LevelOf(r.station) < r.stationLevel)
            { why = $"needs the {BuildPlans.Named(r.station).label} at level {r.stationLevel}"; return false; }
            if (r.tool != null && HeldOf(r.tool) <= 0f)
            { why = $"needs a {Economy.ResDefs.Label(r.tool)} in the pile"; return false; }
            why = null;
            return true;
        }

        /// **One conversion, whatever the station.** A recipe station reads
        /// its chosen (or default) `Recipe`; anything else synthesises the
        /// old one-input plan fields into the same shape, so the Work loop,
        /// `Stalled` and the forecast functions only ever read this. Legacy
        /// `takes`/`Yield` stay bit-identical: one line, `{plan.takes, 1}`,
        /// priced per `plan.Yield` outputs -- exactly what the old inline
        /// arithmetic spent, to the bit.
        bool Conversion(string planId, out string makes, out Economy.Ingredient[] takes,
            out float yield, out float ratePerDay, out string tool, out float toolWear, int ordinal = -1)
        {
            // **The building's own level, 2026-09-27**: a hand's copy
            // (`OrdinalOfHand`) when one is named, else the plan's best.
            int lv = ordinal >= 0 ? LevelOf(planId, ordinal) : LevelOf(planId);
            if (Economy.Recipes.StationHasRecipes(planId))
            {
                var r = RecipeAt(planId);
                if (r == null) { makes = null; takes = Economy.Cost.None; yield = 1f; ratePerDay = 0f; tool = null; toolWear = 0f; return false; }
                makes = r.makes;
                takes = r.takes;
                yield = Mathf.Max(1, r.yield);
                ratePerDay = r.ratePerDay * Economy.Techs.RateMul(planId, lv);
                tool = r.tool;
                toolWear = r.toolWear;
                return true;
            }
            var plan = BuildPlans.Named(planId);
            makes = plan.makes;
            takes = string.IsNullOrEmpty(plan.takes) ? Economy.Cost.None : new[] { new Economy.Ingredient(plan.takes, 1) };
            yield = plan.Yield;
            ratePerDay = plan.rate * Economy.Techs.RateMul(planId, lv);
            tool = null;
            toolWear = 0f;
            return !string.IsNullOrEmpty(makes);
        }

        // --- hunting ---

        /// What a hunter needs in hand, best spear first (`Techs.HuntingSpears`).
        public string SpearInHand()
        {
            foreach (var spear in Economy.Techs.HuntingSpears)
                if (HeldOf(spear) > 0f) return spear;
            return null;
        }

        /// Null when a spear is in the pile; else the reason a hunter is not
        /// out on the island. **Hard gate**, Kevin 2026-09-23: no spear, no
        /// kills.
        public string HunterBlocker() => SpearInHand() == null ? "needs a spear" : null;

        // --- what is built ---------------------------------------------------

        /// Plan ids raised here. `Outpost` owns the objects on the ground; this
        /// is what a save would restore them from.
        public List<string> built = new List<string>();

        public int CountBuilt(string planId)
        {
            int n = 0;
            foreach (var b in built) if (b == planId) n++;
            return n;
        }

        /// Where each building was stood up. See `BuiltBuilding`: `built` is
        /// the count, this is the spot. Written by `Outpost.Raise`, read by
        /// `Outpost.Adopt`, and by nothing else.
        public List<BuiltBuilding> raised = new List<BuiltBuilding>();

        /// `level` 1 for a new building; `Outpost.Adopt` passes the saved
        /// row's own (0 = legacy, read through the plan's old level).
        public void RecordRaised(string planId, Vector3 at, float yaw, float length = 0f, int level = 1)
        {
            raised.Add(new BuiltBuilding
                { planId = planId, x = at.x, z = at.z, yaw = yaw, length = length, level = Mathf.Max(0, level) });
        }

        /// **Every segment standing on this island, breached or not.**
        /// Written by `Outpost.RaiseWall` and by `WallSegment.Damage`, read
        /// by `Outpost.Adopt`. The objects on the ground are the live copy;
        /// this is what a save restores them from.
        public List<BuiltWall> builtWalls = new List<BuiltWall>();

        public int CountRaised(string planId)
        {
            int n = 0;
            foreach (var b in raised) if (b != null && b.planId == planId) n++;
            return n;
        }

        /// **The build QUEUE, oldest first (2026-09-22).**
        ///
        /// Kevin, on the phone: *"I want to be able to place more blueprints
        /// at once."* It used to be one row and a refusal ("something is
        /// already going up"), which made the camp a one-decision-at-a-time
        /// place and put the player on a boat waiting for a shed.
        ///
        /// Order is the whole design: hands serve `sites[0]` until it is
        /// stocked, then `sites[1]`. A site leaves this list the moment it is
        /// RAISED (`Outpost.FinishReady`) or cancelled, never before -- the
        /// reservations and `CanPlace` read it to keep two drawings off the
        /// same ground.
        public List<PendingBuild> sites = new List<PendingBuild>();

        /// **The single-row save slot this queue replaced. Migration only.**
        ///
        /// `JsonUtility` cannot rename a key, so a save written before the
        /// queue carries its one blueprint here. `MigratePending` lifts it
        /// into `sites` and nulls this, and `Outpost.Adopt` calls that before
        /// anything reads the ledger. NOTHING ELSE MAY READ OR WRITE IT --
        /// the live answer is `Pending` / `Focus` / `sites`. It is still
        /// written by every save (as an empty object, exactly as it always
        /// was: `JsonUtility` cannot write null either), which is why an old
        /// build can still read a new save's camps.
        public PendingBuild pending;

        /// **Old save into new list.** Idempotent, and cheap enough to call
        /// from anywhere that is about to look at the queue.
        public void MigratePending()
        {
            if (sites == null) sites = new List<PendingBuild>();
            if (pending == null) return;
            // An empty row is what `JsonUtility` writes for "there was
            // nothing sited" -- see `Outpost.Adopt`, which has always had to
            // re-null it.
            if (!string.IsNullOrEmpty(pending.planId)) sites.Insert(0, pending);
            pending = null;
            PhaseOldRows();
        }

        /// **An old save into the two phases, 2026-09-23.**
        ///
        /// Before today "everything delivered" WAS "finished", so a saved
        /// row that is `Stocked` was, in that save's own terms, done -- it
        /// was sitting there waiting for `Outpost.FinishReady` to find a
        /// scene to stand it in. Giving it its labour outright keeps that
        /// promise: it raises on the next `CatchUp`, exactly as it would
        /// have. A row that was NOT stocked had no build progress to carry
        /// (the old `donePart` is a fraction of a LOG, not a fraction of a
        /// day's work), so it starts the building phase at zero.
        ///
        /// One shot per row, marked by `PendingBuild.phased`, so calling
        /// this from `MigratePending` -- which is called from everywhere --
        /// costs one bool test per site after the first time.
        void PhaseOldRows()
        {
            if (sites == null) return;
            for (int i = 0; i < sites.Count; i++)
            {
                var s = sites[i];
                if (s == null || s.phased) continue;
                s.phased = true;
                if (s.Stocked) s.built = LabourFor(s);
                else s.built = 0f;
            }
        }

        /// **The site the camp is working on**: the oldest one that is not
        /// yet stocked. Null when every queued site has all its materials in
        /// (they are then only waiting to be stood up). This is what
        /// `BuilderWants`, the haul target and the starvation lines are all
        /// about -- one answer, so the arithmetic and the bodies agree.
        public PendingBuild Focus
        {
            get
            {
                if (sites == null) return null;
                for (int i = 0; i < sites.Count; i++)
                    if (sites[i] != null && !sites[i].Complete) return sites[i];
                return null;
            }
        }

        /// **The oldest site still short of materials, 2026-09-23.**
        ///
        /// `Focus` used to answer this, back when a site stopped wanting
        /// anything the moment it was paid for. With a building phase a
        /// site can be the focus (it is being stood up) and want nothing,
        /// and a hauler asking `Focus` where to put a log would walk it to
        /// a finished stack. This is the haul question; `Focus` is the
        /// "which site is the camp on" question.
        public PendingBuild StockingFocus
        {
            get
            {
                if (sites == null) return null;
                for (int i = 0; i < sites.Count; i++)
                    if (sites[i] != null && !sites[i].Stocked) return sites[i];
                return null;
            }
        }

        /// **The oldest site that still wants THIS material, or null.**
        ///
        /// Kevin, 2026-09-23: *"if the building needed 1 more wood and all 4
        /// villagers were carrying wood ... they deposited the wood even
        /// though the amount was already reached."* This is the answer a
        /// hauler re-asks every step of the walk home: the moment the need
        /// is met it returns the NEXT site that wants it, or null, and null
        /// means the camp pile. Nothing is ever carried into a site that is
        /// not short of it.
        public PendingBuild SiteWanting(string resource)
        {
            if (sites == null || string.IsNullOrEmpty(resource)) return null;
            for (int i = 0; i < sites.Count; i++)
                if (RemainingOf(sites[i], resource) > 0) return sites[i];
            return null;
        }

        /// Whole units of `resource` this site is still short of. 0 for a
        /// material it never wanted, and never negative.
        public static int RemainingOf(PendingBuild p, string resource)
        {
            if (p == null || string.IsNullOrEmpty(resource)) return 0;
            if (resource == Res.Timber) return Mathf.Max(0, p.needed - p.done);
            if (resource == Res.Stone) return Mathf.Max(0, p.stoneNeeded - p.stoneDone);
            if (resource == Res.Brick) return Mathf.Max(0, p.brickNeeded - p.brickDone);
            return 0;
        }

        /// **Put whole delivered units where they belong.**
        ///
        /// The one door a BODY walking a load in should use: units go into
        /// the oldest site short of that material, up to what it is short
        /// of, and the surplus lands on the camp pile instead of pushing a
        /// counter past its need. Nothing is dropped -- the pile takes the
        /// remainder whether or not it is over the fire's ceiling, because
        /// a log that has been carried here exists.
        public int DeliverToSite(string resource, int n)
        {
            if (string.IsNullOrEmpty(resource) || n <= 0) return 0;
            MigratePending();
            int left = n;
            while (left > 0)
            {
                var site = SiteWanting(resource);
                if (site == null) break;
                int room = RemainingOf(site, resource);
                int take = Mathf.Min(room, left);
                if (resource == Res.Timber) site.done += take;
                else if (resource == Res.Stone) site.stoneDone += take;
                else if (resource == Res.Brick) site.brickDone += take;
                left -= take;
            }
            if (left > 0) Store(resource, true).whole += left;
            return n;
        }

        /// The row a sheet means when it says "the blueprint" without naming
        /// one: the one being worked, else the oldest queued.
        public PendingBuild Pending
        {
            get
            {
                var f = Focus;
                if (f != null) return f;
                return sites != null && sites.Count > 0 ? sites[0] : null;
            }
        }

        /// How many drawings stand here.
        public int SiteCount => sites != null ? sites.Count : 0;

        /// Is there a blueprint here waiting on wood?
        public bool Building => Focus != null;

        /// Sited, paid for, and waiting for somebody to stand it up. The
        /// arithmetic can finish a building while its island is unloaded, so
        /// the raise happens when the scene next has somewhere to put it —
        /// see `Outpost.CatchUp`.
        public bool ReadyToRaise
        {
            get
            {
                if (sites == null) return false;
                for (int i = 0; i < sites.Count; i++)
                    if (sites[i] != null && sites[i].Complete) return true;
                return false;
            }
        }

        /// The oldest site with everything in it, or null. `Outpost` raises
        /// these one per call until there are none left.
        public PendingBuild FirstStocked
        {
            get
            {
                if (sites == null) return null;
                for (int i = 0; i < sites.Count; i++)
                    if (sites[i] != null && sites[i].Complete) return sites[i];
                return null;
            }
        }

        /// Is this plan already queued here? A camp keeps one of each, and
        /// that rule has to cover the drawings as well as the buildings or
        /// the queue is how you get two sawmills.
        public bool Queued(string planId)
        {
            if (sites == null || string.IsNullOrEmpty(planId)) return false;
            for (int i = 0; i < sites.Count; i++)
                if (sites[i] != null && sites[i].planId == planId) return true;
            return false;
        }

        /// How many drawings of `planId` are in the queue right now. `Queued`
        /// only asks "any at all" (the one-of-each gate); a plan with
        /// `BuildPlan.allowMultiple` set needs the actual count, so its Nth
        /// copy can be priced through `BuildPlans.PriceForCopy`.
        public int QueuedCount(string planId)
        {
            if (sites == null || string.IsNullOrEmpty(planId)) return 0;
            int n = 0;
            for (int i = 0; i < sites.Count; i++)
                if (sites[i] != null && sites[i].planId == planId) n++;
            return n;
        }

        // --- raiders, 2026-09-22 -----------------------------------------------
        //
        // Phase 3 "teeth": a camp on an island with raiders offshore, nobody
        // watching it, with something piled, gets raided on a clock the sheet
        // can print in days. A manned watchtower stops the clock; an unmanned
        // one halves it. It is a mistake the player can see coming, not a
        // dice roll -- see `RaidLine`.

        /// Raiders patrolling this island right now. **Pushed in by
        /// `Outpost.CatchUp` before every tick**, like `ceilingPer` -- the
        /// ledger never computes this, it only reacts to it.
        public int raiders;

        /// Days of unwatched exposure banked toward the next raid.
        public float threat;

        /// Lifetime raid count.
        public int raids;

        /// Days of exposure a fresh camp can bank before a raid lands.
        /// **A placeholder, never played.**
        public const float DaysToRaid = 4f;

        /// Share of each pile's whole units a raid takes, at least one unit
        /// when the pile has any. **A placeholder, never played.**
        public const float RaidShare = 0.4f;

        /// Mood every hand loses when the camp is raided. **A placeholder,
        /// never played.**
        public const float RaidMoodHit = 0.25f;

        // --- what arrows buy, 2026-09-22 ---------------------------------------
        //
        // Kevin: *"build a fletcher's building as well for bow and arrow."*
        // Two effects, and both of them SPEND the arrows, because a good that
        // only accumulates is a number and not a decision.

        /// What a quiver is worth to a hunter: half again as many animals a
        /// day, at one arrow an animal. **A guess, never played.**
        public const float BowKillBonus = 1.5f;

        /// Arrows a posted lookout will loose at one raid.
        public const int VolleyArrows = 5;

        /// What each arrow loosed takes off the raid's share, as a fraction
        /// of the pile. Five arrows at a tenth each turns `RaidShare` from
        /// 0.4 into 0.2 -- a full volley halves what a raid carries off, and
        /// no volley leaves the old number untouched to the bit.
        public const float VolleyShareOff = 0.1f;

        /// **The lookout looses, and the raid carries less off.**
        ///
        /// The rule, in one sentence: *a posted lookout with arrows spends up
        /// to five of them, and every arrow spent takes a tenth off the share
        /// a raid takes.* No lookout, no watchtower or no arrows and nothing
        /// happens at all -- `RaidShare` stands, exactly as it did before the
        /// fletcher existed.
        ///
        /// Returns the arrows actually loosed, so the caller can say so.
        ///
        /// A posted lookout with arrows looses up to five. The away-clock
        /// raid spends them in `Raid()` against its share; the live raid
        /// spends them in `Combat.RaidParty.Begin`, two arrows a raider.
        ///
        /// **Every manned tower looses its own, 2026-09-27** (several towers
        /// per camp): with no `maxArrows` given the volley is `VolleyArrows`
        /// per tower with a hand on it, still paid from one quiver and still
        /// clamped by `RaidShare` at the raid.
        public int LookoutVolley(int maxArrows = -1)
        {
            if (!LookoutPosted) return 0;
            if (maxArrows < 0) maxArrows = VolleyArrows * Mathf.Max(1, MannedCopies(WatchtowerId));
            // Store and the fletcher's rack alike (`Take` draws both).
            int held = SpendableOf(Res.Arrows);
            if (held <= 0 || maxArrows <= 0) return 0;
            return Take(Res.Arrows, Mathf.Min(maxArrows, held));
        }

        /// Matches the id `BuildPlans.Watchtower` is being wired up with
        /// elsewhere -- kept as a string here rather than a reference to
        /// that plan, which may not exist yet.
        public const string WatchtowerId = "Watchtower";

        /// Has a watchtower been raised here at all -- built, not manned.
        /// ANY of them, since a camp may raise several (2026-09-27); the
        /// raid clock halves on one, not per tower.
        public bool HasWatchtower => built.Contains(WatchtowerId);

        /// Labour standing lookout right now, clamped to one -- a single
        /// hand at full mood is all the guard a camp needs.
        public float Guard
        {
            get
            {
                float g = 0f;
                foreach (var h in hands)
                    if (h != null && h.order == OutpostOrder.Work && h.target == WatchtowerId)
                        g += WorkFactor(h);
                return Mathf.Clamp01(g);
            }
        }

        /// Days of exposure this camp banks per day, at its current orders.
        /// Zero with nobody offshore, nothing to take, or a manned lookout --
        /// a raid is never a clock running on a camp that cannot be raided.
        public float ThreatRatePerDay
        {
            get
            {
                if (raiders <= 0 || Total <= 0 || Guard >= 1f) return 0f;
                return (1f - Guard) * (HasWatchtower ? 0.5f : 1f);
            }
        }

        /// Days until the next raid at the current rate, or -1 when none is
        /// coming.
        public float DaysUntilRaid
        {
            get
            {
                float rate = ThreatRatePerDay;
                if (rate <= 0f) return -1f;
                return (DaysToRaid - threat) / rate;
            }
        }

        /// Is somebody standing lookout right now -- `Guard` at full, spelled
        /// out for a UI that wants a bool rather than the float it is graded
        /// from.
        public bool LookoutPosted => Guard >= 1f;

        /// **The clock's cadence with `Guard` forced to one value, 2026-09-22**
        /// -- not "until the next raid from here," which `DaysUntilRaid`
        /// already answers, but "how far apart raids land at this setting,"
        /// for a sheet that wants to show the player both ends of the choice
        /// at once. Mirrors `ThreatRatePerDay` term for term with `Guard`
        /// substituted, so the two can never disagree about what a manned
        /// lookout is worth.
        float ThreatRateAt(bool guarded)
        {
            if (raiders <= 0 || Total <= 0 || guarded) return 0f;
            return HasWatchtower ? 0.5f : 1f;
        }

        /// Days between raids as if nobody were watching at all.
        public float RaidDaysUnwatched
        {
            get
            {
                float rate = ThreatRateAt(false);
                return rate <= 0f ? float.PositiveInfinity : DaysToRaid / rate;
            }
        }

        /// Days between raids as if a lookout were manning the tower --
        /// which is to say never: a manned watch halts the clock outright,
        /// the same way `Guard >= 1f` already zeroes `ThreatRatePerDay`.
        public float RaidDaysIfWatched => float.PositiveInfinity;

        /// **What the sheet prints for this camp's raid risk**, or null when
        /// there is nobody offshore to make it a risk at all.
        public string RaidLine
        {
            get
            {
                if (raiders <= 0) return null;
                int n = raiders;
                string who = n == 1 ? "raider" : "raiders";
                if (Guard >= 1f)
                    return $"{n} {who} offshore   ·   the lookout keeps them off";
                if (Total <= 0)
                    return $"{n} {who} offshore   ·   nothing here to take";
                float d = DaysUntilRaid;
                string fix = HasWatchtower
                    ? "post a lookout"
                    : "a watchtower and a lookout stop it";
                return $"{n} {who} offshore   ·   a raid {d:0.#} days after you sail   ·   " + fix;
            }
        }

        /// **The raid itself.** Takes a share of every pile that has
        /// anything in it, records what was lost against the open absence,
        /// and knocks every hand's mood down -- the cost of nobody watching.
        void Raid()
        {
            // **The volley first, Kevin 2026-09-22.** The lookout is already
            // standing there -- with a quiver she does something about it.
            // Loosed BEFORE a single pile is touched, so the arrows spent are
            // not themselves part of what the raid takes.
            int loosed = LookoutVolley();
            float share = Mathf.Clamp(RaidShare - loosed * VolleyShareOff, 0f, RaidShare);

            foreach (var s in stores)
            {
                if (s == null || s.whole <= 0) continue;
                // Still at least one unit off any pile that has anything:
                // a volley blunts a raid, it does not turn one away.
                int took = Mathf.Max(1, Mathf.FloorToInt(s.whole * share));
                took = Take(s.resource, took);
                if (took > 0) away.AddRaided(s.resource, took);
            }
            foreach (var h in hands)
                if (h != null) h.mood = Mathf.Max(0f, h.mood - RaidMoodHit);
            raids++;
            away.raids++;
        }

        // --- the clock -------------------------------------------------------

        /// `TimeOfDay.Seconds` this ledger has been advanced to. Double for the
        /// same reason TimeOfDay is: a float loses resolution over a session.
        ///
        /// **Only ever advanced in whole quanta** — that is what makes the
        /// arithmetic path-independent.
        public double lastTicked;

        /// **What happened while nobody was standing here.** Opened when the
        /// ship sails (`Outpost.ShowHands(false)`) and closed when she
        /// returns, so the game can say what the camp did in between. Never
        /// null: JsonUtility restores a reference-type field as a fresh
        /// default object on an old save, and a fresh `Absence` has
        /// `sinceSeconds == 0`, which `Open` already reads as "nothing open".
        public Absence away = new Absence();

        /// One open-ended record of an absence: what was gathered, made,
        /// eaten, raised and recruited between a departure and the next
        /// arrival. `[System.Serializable]` so it rides along inside the
        /// ledger's own JsonUtility save.
        [System.Serializable]
        public class Absence
        {
            /// `TimeOfDay.Seconds` the ship left. **0 means no absence is
            /// open** -- an old save, or a camp nobody has left yet.
            public double sinceSeconds;

            // Parallel lists rather than a dictionary: JsonUtility cannot
            // serialize one, and this is small enough that a linear find on
            // arrival costs nothing.
            public List<string> res = new List<string>();
            public List<float> got = new List<float>();

            public float eaten;
            public float hungryDays;

            /// Blueprint plan ids that went from building to `Complete`
            /// while away.
            public List<string> raised = new List<string>();
            /// Names of hands recruited while away.
            public List<string> born = new List<string>();

            /// **Raiders, 2026-09-22.** Parallel lists like `res`/`got`, for
            /// the same JsonUtility reason -- what a raid took, per resource,
            /// while nobody was standing here to stop it.
            public List<string> raidRes = new List<string>();
            public List<int> raidGot = new List<int>();
            /// How many times this camp was raided during the absence.
            public int raids;

            /// Is there an absence in progress?
            public bool Open => sinceSeconds > 0.0;

            /// Worth showing the player at all, or just a quiet return.
            public bool Anything
            {
                get
                {
                    if (raised.Count > 0 || born.Count > 0) return true;
                    if (eaten > 0f || hungryDays > 0f) return true;
                    if (raids > 0) return true;
                    for (int i = 0; i < got.Count; i++) if (got[i] >= 1f) return true;
                    return false;
                }
            }

            /// Find or create this resource's row and add to it.
            public void Add(string resource, float amount)
            {
                if (string.IsNullOrEmpty(resource) || amount == 0f) return;
                for (int i = 0; i < res.Count; i++)
                {
                    if (res[i] != resource) continue;
                    got[i] += amount;
                    return;
                }
                res.Add(resource);
                got.Add(amount);
            }

            /// Find or create this resource's raided row and add to it.
            public void AddRaided(string resource, int n)
            {
                if (string.IsNullOrEmpty(resource) || n == 0) return;
                for (int i = 0; i < raidRes.Count; i++)
                {
                    if (raidRes[i] != resource) continue;
                    raidGot[i] += n;
                    return;
                }
                raidRes.Add(resource);
                raidGot.Add(n);
            }

            /// How many whole days this absence has run, as of `nowSeconds`.
            public float DaysAway(double nowSeconds)
            {
                if (!Open || TimeOfDay.DayLength <= 0f) return 0f;
                double elapsed = nowSeconds - sinceSeconds;
                return elapsed <= 0.0 ? 0f : (float)(elapsed / TimeOfDay.DayLength);
            }
        }

        /// Open a fresh absence record. The caller ticks the ledger up to
        /// `nowSeconds` FIRST (`Outpost.CatchUp` does), so nothing that
        /// happened before departure leaks into the record.
        public void BeginAbsence(double nowSeconds)
        {
            away = new Absence { sinceSeconds = nowSeconds };
        }

        /// Close the open absence and hand back what it holds. The caller
        /// ticks the ledger up to now FIRST, so the record covers the whole
        /// time she was gone, right up to this return. Returns null if
        /// there was nothing open (an old save, or two arrivals in a row).
        public Absence EndAbsence()
        {
            if (!away.Open) return null;
            var closed = away;
            away = new Absence();
            return closed;
        }

        // --- the numbers, none of which have been played ---------------------

        /// Game-days in one step. A day is `TimeOfDay.DayLength` (180 s while
        /// testing), so a quantum is 3.6 seconds of real time at the current
        /// setting.
        ///
        /// Everything advances in whole quanta and the remainder is carried, so
        /// **one call covering ten days and ten calls covering one day each
        /// produce bit-identical state.** That property is what lets the game
        /// tick a camp whenever it feels like — on arrival, on a map query, on
        /// save — without the answer depending on how often it asked.
        ///
        /// **0.1 -> 0.02, 2026-09-24** (Kevin, phone playtest: fetching a log
        /// at the store "seems like a minute"). Every decision the books take
        /// -- start the next trip, load the bench, carry the rack home -- is
        /// taken inside a step, but the body mimes the books on the step
        /// grid (`CampWorker.SecondsUntilSpent`), so an 18 s grid read as up
        /// to 18 s of standing about per hand-off. 3.6 s reads as real time.
        ///
        /// **The quantum is FIXED, watched or not**, which keeps D2 bit-exact
        /// (`StationStockSelfTest` `d2-30-days-busy-camp`). The step SIZE is
        /// not neutral (the same self-test's `mixed-step-30d` diagnostic):
        /// hands take turns inside a step, mood/`WorkFactor` is read once at
        /// the step's start and eating settles once at its end, and the
        /// "haul or help build" choice is taken per step. So there is no
        /// coarse catch-up path for unwatched camps; a 30-day absence is
        /// 1500 steps (timed in `d2-30-days-busy-camp`), and a coarse path
        /// would also put `CampWorker.SecondsUntilSpent` off the grid.
        public const float QuantumDays = 0.02f;

        /// Logs a hand fells in a day. **A guess, never played.** Still the
        /// unit everything else is priced against — see `Res.GatherRate`,
        /// which sets the other resources relative to it.
        public const float TimberPerHandPerDay = 4f;
        /// **Stone a day one builder quarries out of standing rock**, when
        /// there is none piled to carry. Under the felling rate (4) because
        /// a boulder is four strikes where a tree is three -- the same
        /// relation `Res.GatherRate` already prices Stone at against Timber,
        /// rounded up to a whole number a player can count in days. **A
        /// guess, never played**, 2026-09-21.
        public const float StonePerHandPerDay = 3f;

        /// **How long one pair of hands takes to stand up a hut once every
        /// stick of it is on the ground, in game-days, 2026-09-23.**
        ///
        /// Kevin: the building phase *"shouldn't take super long"*. Half a
        /// day for a plan of the reference size, scaled by how much stuff
        /// the plan is made of and clamped either side so the cheapest thing
        /// on the list is not instant and the most expensive is not a week.
        /// Three builders spend three hand-days a day, so three hands do it
        /// in a third of the time -- that falls out of the unit, not out of
        /// a special case.
        public const float BuildDaysPerHand = 0.5f;

        /// **Hand-days to take ONE tree off a building plot.** Kevin,
        /// 2026-09-23: 5 SECONDS of builder time a tree
        /// (`Playtest.ClearSecondsPerTree`), turned into days at the current
        /// `TimeOfDay.DayLength`. The log is booked on the pile like any
        /// other. The dial for "clearing takes too long".
        public static float ClearTreeHandDays => SecondsToDays(Playtest.ClearSecondsPerTree);
        /// **Hand-days to break ONE rock off a plot**: `Playtest.ClearSecondsPerRock`
        /// (8 s, the stone cut ratio -- a guess, Kevin set only the tree),
        /// broken and rolled aside with `ClearStonePerRock` worth keeping.
        public static float ClearRockHandDays => SecondsToDays(Playtest.ClearSecondsPerRock);
        /// Units a cleared rock books on the pile (Stone, or Ore for the
        /// `clearOre` tail). Half a prop's worth.
        public const int ClearStonePerRock = 2;

        /// Materials a plan of the reference size is made of. A hut is 6
        /// logs + 2 stone = 8, so a hut is a shade over the reference and
        /// costs about 0.67 hand-days to raise.
        public const float ReferenceMaterials = 6f;

        /// Hand-days of labour a site's BUILDING phase costs.
        /// `Mathf.Clamp` either side: nothing under a quarter-day (a
        /// campfire is four logs and should still be a job), nothing over
        /// one and a half (a 24-log sawmill).
        ///
        /// **2026-09-27: hammer SECONDS per plan** (Kevin: "we are just
        /// talking about building time here"). One builder's seconds come
        /// from the tuning asset (`BuildPlans.HammerSeconds`: shelter 30 s,
        /// sawmill 60 s, forge 75 s ...), times FEEL's build-time
        /// multiplier, in hand-days at the current day length. A line row
        /// (palisade, gate, ladder) has no plan row and pays
        /// `EconomyTuning.HammerSecondsPerLineLog` per unit it is made of.
        /// Crew speed (diminishing) is applied where it is spent, `PayBuild`.
        public static float LabourFor(PendingBuild p)
        {
            if (p == null) return 0f;
            float sec = BuildPlans.HammerSeconds(p.planId);
            if (sec <= 0f)
            {
                float stuff = Mathf.Max(0, p.needed) + Mathf.Max(0, p.stoneNeeded)
                    + Mathf.Max(0, p.brickNeeded);
                if (stuff <= 0f) return 0f;          // a free plan is free to raise
                sec = stuff * Economy.EconomyTuning.HammerSecondsPerLineLog;
            }
            return sec * Economy.EconomyFeel.BuildTimeMul / Mathf.Max(0.0001f, TimeOfDay.DayLength);
        }
        /// Food a day one farmhand brings in off a farm's field
        /// (`BuildPlans.Farm.rate`). Half again the felling rate: the wheat
        /// is planted in rows beside the camp, not found. **A guess, never
        /// played**, 2026-09-21.
        public const float FoodPerHandPerDay = 6f;

        /// **A build that cannot finish by itself.** Nothing in the pile and
        /// nothing left standing to cut: the drawing will wait for the wood to
        /// regrow, which is days per log. The sheet says so, because a stalled
        /// blueprint is otherwise indistinguishable from a slow one.
        public bool BuildStarved => TimberStarved || StoneStarved;

        /// The blueprint still wants logs and there are none to be had.
        public bool TimberStarved
        {
            get
            {
                var f = StockingFocus;
                return f != null && !f.TimberPaid
                    && StoreCountOf(Res.Timber) <= 0 && Wood.standing < 1f;
            }
        }

        /// **The same, for the stone part.** An island whose seam is worked
        /// out and whose pile is empty cannot finish a building however much
        /// wood is standing -- and a sheet that said "NO TIMBER LEFT" at it
        /// would be sending the player to cut trees they do not need.
        public bool StoneStarved
        {
            get
            {
                var f = StockingFocus;
                if (f == null || f.StonePaid) return false;
                // The store: builders pay stone from it (and the seam) only.
                if (StoreCountOf(Res.Stone) > 0) return false;
                var seam = Stock(Res.Stone);
                return seam == null || seam.standing < 1f;
            }
        }

        /// What a campfire watches over, of each thing. Settled at ten.
        public const int CampfireCeiling = 10;

        /// Timber-grade logs per hectare of the ground the camp works.
        /// **A guess, never played**, and deliberately far under the ~230
        /// trees a hectare the scenery actually draws: most of a wood is not
        /// worth felling, and a stock nobody can exhaust is not a stock.
        public const float StandingPerHectare = 40f;

        /// Share of the timber stock that comes back in a day. **A guess.**
        public const float RegrowthPerDay = 0.02f;

        /// Seed a fresh ledger for a camp on this ground.
        ///
        /// Timber comes from the surveyed area, because how much wood stands
        /// within reach is a property of the place. Anything else the island
        /// offers is added by `Outpost` once it knows what the populator put
        /// there — the ledger must not go looking at the scene.
        public static OutpostLedger For(Vector3 at, float workedHectares)
        {
            var l = new OutpostLedger();
            l.SetKey(at);
            l.ceilingPer = CampfireCeiling;
            l.SeedStock(Res.Timber, workedHectares);
            l.lastTicked = TimeOfDay.Seconds;
            return l;
        }

        /// Put a resource's stock on the ground here, sized by the worked area.
        public void SeedStock(string resource, float hectares)
        {
            var s = Stock(resource, true);
            s.standingMax = Mathf.Max(1f, hectares * Res.PerHectare(resource));
            s.standing = s.standingMax;
            s.regrowPerDay = Res.RegrowPerDay(resource);
        }

        /// **Put more of a resource in the ground here.** A farm's field:
        /// raising one adds `beds * unitsPerBed` of standing Food, planted
        /// and ready, and lifts the ceiling it regrows to by the same. Called
        /// from the raise hook, never from `Step`, so the ledger still learns
        /// about the scene only at the moments the scene tells it.
        ///
        /// Merges into a stock that already exists -- wild wheat gathered by
        /// hand and a farm's rows are one Food stock -- and the regrowth
        /// becomes the faster of the two, because a field that has been
        /// planted does not come back slower for having wild wheat beside it.
        /// `regrowPerDay` below zero leaves the stock's own rate alone.
        public OutpostStock AddStanding(string resource, float amount, float regrowPerDay = -1f)
        {
            var s = Stock(resource, true);
            if (amount > 0f)
            {
                s.standingMax += amount;
                s.standing = Mathf.Min(s.standingMax, s.standing + amount);
            }
            if (regrowPerDay >= 0f) s.regrowPerDay = Mathf.Max(s.regrowPerDay, regrowPerDay);
            return s;
        }

        /// What a raised plan adds to the ground: a farm's field, or nothing.
        /// One call for the raise hook, so the numbers stay on the plan.
        public OutpostStock AddField(BuildPlan plan)
        {
            if (plan.beds <= 0 || string.IsNullOrEmpty(plan.makes)) return null;
            // The farm grows per plot since the food rework (`FarmPlot`).
            if (plan.id == BuildPlans.Farm.id) return null;
            return AddStanding(plan.makes, plan.FieldStanding, plan.bedRegrowPerDay);
        }

        /// The timber stock, which enough of the game asks for by name that it
        /// is worth not making everybody look it up.
        public OutpostStock Wood => Stock(Res.Timber, true);

        // --- the tick --------------------------------------------------------

        /// Bring this ledger up to `nowSeconds`.
        ///
        /// Safe and free to call as often as you like: it advances in whole
        /// quanta and leaves `lastTicked` on the quantum grid, so a second call
        /// in the same frame does nothing at all, and the state after any
        /// sequence of calls depends only on the elapsed time.
        ///
        /// Nothing here touches the scene, the terrain or a MonoBehaviour, so
        /// it works for an island that is not loaded — which is the point.
        /// Step multiple for `AwayProgress` only; see `Tick`. Never saved.
        public static int CatchUpStride = 1;

        /// Steps run by every ledger this session (the catch-up's cost meter).
        public static long StepsRun;

        public void Tick(double nowSeconds)
        {
            float dayLength = Mathf.Max(0.0001f, TimeOfDay.DayLength);
            // **Time away (2026-09-27)**: `AwayProgress` may widen the step to
            // a whole multiple of the quantum ONLY when a long catch-up would
            // blow its wall-clock budget. Whole multiples keep `lastTicked` on
            // the quantum grid; 1 (the default) is the game's only step.
            int stride = CatchUpStride < 1 ? 1 : CatchUpStride;
            double quantum = QuantumDays * stride * dayLength;
            if (quantum <= 0.0) return;

            double elapsed = nowSeconds - lastTicked;
            if (elapsed <= 0.0)
            {
                // Time can run backwards when a dev tool scrubs the clock.
                // Re-anchor rather than bank a negative debt that would later
                // be paid out as a burst of free timber.
                if (elapsed < 0.0) lastTicked = nowSeconds;
                return;
            }

            long steps = (long)(elapsed / quantum);
            if (steps <= 0) return;

            // A camp left for a very long time still has to answer in one
            // frame. Fifty thousand quanta is a thousand game days at the
            // 0.02-day quantum (2026-09-24; was 10000 at 0.1), far past any
            // session; beyond it the arithmetic has converged on the ceiling
            // anyway, so the clamp cannot change an outcome anyone will see.
            const long MaxSteps = 50000;
            long run = steps > MaxSteps ? MaxSteps : steps;

            for (long i = 0; i < run; i++) Step(QuantumDays * stride);
            StepsRun += run;

            // Advance the FULL elapsed quanta even when the run was clamped,
            // or the ledger would owe the same debt again on the next call and
            // never catch up.
            lastTicked += steps * quantum;
        }

        // --- stocking a site: whole armfuls, never more than it needs ----------
        //
        // **Phone playtest, 2026-09-23.** Kevin: *"they carry way too much
        // resources to it, and the required x/z resources don't show accurate
        // account for how many they've received ... it should be: get the
        // resources (5/5 wood, 3/3 stone) then they start hammering."*
        //
        // Until today the stocking was a fractional pour (`PayTimber` /
        // `PayStone` / `PayBrick` / `Haul`): hand-days became fractions of a
        // log in `donePart`, the bar read those fractions, and the bodies
        // mimed trips on their own clock with no relation to what landed. Now
        // a builder does what a station hauler does: one TRIP at a time
        // (`OutpostHand.haul*`, `HaulPlace.Site`), carrying a whole armful
        // (`Res.Armful`) capped at what the site is still short of NET of
        // loads already walking to it -- so two haulers never both bring the
        // last three. The load leaves its source at pickup and lands in the
        // site's whole counters on arrival (`DepositHaul`); nothing fractional
        // is ever booked into a site.

        [System.NonSerialized] readonly List<OutpostHand> builderScratch = new List<OutpostHand>();

        /// **Whole units of `res` this site is still short of once every load
        /// already walking to a site has landed.** Loads in arms fill the
        /// queue oldest-first -- the order `DeliverToSite` puts them in -- so
        /// the answer is exactly what will still be missing.
        public int NetShort(PendingBuild site, string res)
        {
            if (site == null || sites == null) return 0;
            int transit = InFlightTo(HaulPlace.Site, -1, res);
            for (int i = 0; i < sites.Count; i++)
            {
                var s = sites[i];
                int r = RemainingOf(s, res);
                if (s == site) return Mathf.Max(0, r - transit);
                transit = Mathf.Max(0, transit - r);
            }
            return 0;
        }

        /// **One builder's share of a quantum**, spent down the queue oldest
        /// first: clear the plot, fetch its materials trip by trip, and --
        /// only once every one is IN -- stand it up. A hand whose site has
        /// every missing unit already in somebody's arms moves on to the next
        /// site, exactly as "the haulers move on to the next one".
        void BuilderDay(OutpostHand h, ref float budget)
        {
            float scale = WorkFactor(h);
            for (int guard = 0; guard < 64 && budget > Eps; guard++)
            {
                if (h.Hauling)
                {
                    // A transfer armful (2026-09-24) is walked by the builder
                    // pass's `TransferDay`, not here.
                    if (IsTransferHaul(h)) return;
                    // **Any load in his arms is walked where it was going**
                    // (2026-09-27) -- a site armful, a cleared log to the
                    // store, a station load from a job he just left.
                    if (!AdvanceHaul(h, ref budget, scale)) return;
                    continue;
                }

                // Kevin's ladder (2026-09-27): fetch from the store, work a
                // plot, cut/quarry, else wait -- OutpostLedger.SiteLadder.cs.
                if (!LadderStep(h, ref budget, scale)) return;
            }
        }

        /// **The second phase: spend hand-days standing it up.**
        ///
        /// Only ever called on a `Stocked` site, and it takes no materials
        /// at all -- everything it is made of is already on the ground.
        /// `labour` is hand-days, so three builders in one tick hand this
        /// three times as much and it finishes in a third of the time
        /// without knowing there are three of them.
        /// **The CLEAR phase: spend hand-days taking the plot's trees and
        /// rocks down, 2026-09-23.** Paid first, out of the same builder
        /// labour `PayBuild` spends, so a camp nobody is watching clears its
        /// plots at exactly the pace a watched one does. Every time
        /// `clearDone` crosses a whole number one obstruction is down, and
        /// what it was made of is booked the way gathering books it (a log,
        /// or `ClearStonePerRock` stone/ore, up to the pile's ceiling). The
        /// SCENE shows it by reading the same integer (`Outpost.SyncClearing`)
        /// -- the ledger never needs to know which tree it was.
        void PayClear(OutpostHand h, PendingBuild pending, ref float labour)
        {
            if (pending == null) return;
            for (int guard = 0; guard < 256 && labour > 0f && !pending.Cleared; guard++)
            {
                int k = Mathf.FloorToInt(pending.clearDone);
                bool tree = k < pending.clearTrees;
                float cost = tree ? ClearTreeHandDays : ClearRockHandDays;
                if (cost <= 0f) { pending.clearDone = k + 1; }
                else
                {
                    float need = (k + 1 - pending.clearDone) * cost;
                    if (labour < need)
                    {
                        pending.clearDone += labour / cost;
                        labour = 0f;
                        // Float drift must never book a tree twice or skip
                        // one: the integer only moves in the branch below.
                        if (pending.clearDone >= k + 1) pending.clearDone = k + 0.9999f;
                        return;
                    }
                    labour -= need;
                    pending.clearDone = k + 1;
                }
                string res;
                int n;
                if (tree) { res = Res.Timber; n = 1; }
                else
                {
                    // Rocks run stone first, ore last (`clearOre`).
                    int rock = k - pending.clearTrees;
                    bool ore = rock >= pending.clearRocks - pending.clearOre;
                    res = ore ? Res.Ore : Res.Stone;
                    n = ClearStonePerRock;
                }
                // **What came off the plot is CARRIED to the store**
                // (2026-09-27): it is in his arms at the site and counts when
                // it lands. What the store has no room for is left lying (as
                // `Add` always clamped it).
                int carry = h != null ? Mathf.Min(n, RoomFor(res)) : 0;
                if (carry > 0)
                {
                    StartTimedTrip(h, res, carry, HaulPlace.Site, -1, HaulPlace.Store, -1, pending, false, true);
                    return;
                }
            }
        }

        void PayBuild(PendingBuild pending, ref float labour)
        {
            if (pending == null || labour <= 0f) return;
            float want = pending.LabourNeeded - pending.built;
            if (want <= 0f) return;
            // **Diminishing returns, 2026-09-27**: N builders at the site
            // hammer N^0.75 as fast as one (`EconomyTuning.CrewSpeed`), so
            // each one's hand-day is worth CrewSpeed(N)/N of a lone man's.
            int crew = Mathf.Max(1, HammerCrew(pending));
            float eff = Economy.EconomyTuning.CrewSpeed(crew) / crew;
            float spend = Mathf.Min(labour, want / eff);
            pending.built = Mathf.Min(pending.LabourNeeded, pending.built + spend * eff);
            labour -= spend;
        }

        /// **Nothing in a site above what the site asked for, 2026-09-23.**
        ///
        /// The arithmetic (`Haul`) has always clamped to `room`, but a BODY
        /// can put a whole unit in from outside the tick -- `CrewAgent` does
        /// exactly that when a ship's hand walks a log into a blueprint. If
        /// one lands on a counter that was already full, the surplus is
        /// moved to the camp pile here, at the top of the tick, before
        /// anything reads a count. Never destroyed: the pile takes it even
        /// past the fire's ceiling, because the log exists.
        void ReconcileSites()
        {
            if (sites == null) return;
            for (int i = 0; i < sites.Count; i++)
            {
                var s = sites[i];
                if (s == null) continue;
                Spill(ref s.done, s.needed, Res.Timber);
                Spill(ref s.stoneDone, s.stoneNeeded, Res.Stone);
                Spill(ref s.brickDone, s.brickNeeded, Res.Brick);
                // **A fraction of a unit is not delivered** (2026-09-23): the
                // old fractional pour left `*DonePart` in saved rows. Nothing
                // accrues there any more, so an old save's fractions go back
                // to the pile they came from -- not lost, not counted.
                SpillPart(ref s.donePart, Res.Timber);
                SpillPart(ref s.stoneDonePart, Res.Stone);
                SpillPart(ref s.brickDonePart, Res.Brick);
                if (s.built > s.LabourNeeded) s.built = s.LabourNeeded;
            }
        }

        void SpillPart(ref float part, string resource)
        {
            if (part <= 0f) { part = 0f; return; }
            var st = Store(resource, true);
            st.part += part;
            part = 0f;
            int whole = Mathf.FloorToInt(st.part + 1e-5f);
            if (whole > 0) { st.whole += whole; st.part = Mathf.Max(0f, st.part - whole); }
        }

        void Spill(ref int done, int need, string resource)
        {
            if (done <= need) return;
            int extra = done - need;
            done = need;
            Store(resource, true).whole += extra;
        }

        /// **What a builder here should be fetching right now**: logs until
        /// the timber part is paid, then stone, then nothing. One answer, so
        /// the arithmetic (`Step`), the body (`CampWorker`) and the mime all
        /// agree about which material a man is carrying.
        public string BuilderWants
        {
            get
            {
                // **The OLDEST unstocked site, and only that one.** The
                // queue's whole rule, in the one place every body reads.
                // **`StockingFocus`, not `Focus`, since 2026-09-23.** A
                // site in its BUILDING phase is the focus and wants
                // nothing; the haulers should be filling the next one.
                var p = StockingFocus;
                if (p == null) return null;
                if (!p.TimberPaid) return Res.Timber;
                if (!p.StonePaid) return Res.Stone;
                return p.BrickPaid ? null : Res.Brick;
            }
        }

        /// **Test seam, not a game path**: one `Step` of any size, bypassing
        /// `Tick`'s fixed quantum, so `StationStockSelfTest` can measure how
        /// far the books drift when the step size changes. The game only
        /// ever steps `QuantumDays`.
        internal void StepForTest(float days) => Step(days);

        /// One quantum of work. The only place the outpost's state changes.
        void Step(float days)
        {
            // Anything a body over-delivered since the last tick goes on the
            // pile before a single count is read. See `ReconcileSites`.
            MigratePending();
            MigrateFood();
            EnsurePlots();
            GrowPlots(days);
            ReconcileSites();
            DecayDelivered(days);
            FeedFirst();
            EnlistFree();
            EnsureStations();
            // She cast off: store -> ship armfuls go home; done orders go.
            SettleTransfers();

            // **A gatherer whose store is full does something else** (Kevin,
            // 2026-09-23): hauls for the stations if there is hauling to do,
            // else lends a hand to the build. Decided once per quantum so the
            // two cannot both take his day.
            bool haulChores = HasHaulChore();
            bool gatherersBuild = !haulChores && Focus != null;

            // Regrowth first, so a camp that stripped its ground last step has
            // something to cut this one rather than the order of operations
            // deciding the answer.
            foreach (var s in stocks)
            {
                if (s == null || s.regrowPerDay <= 0f) continue;
                if (s.standing < s.standingMax)
                    s.standing = Mathf.Min(s.standingMax,
                        s.standing + s.standingMax * s.regrowPerDay * days);
            }

            // **And the stumps close over, from the outside in.** Kevin,
            // 2026-09-22. The same rate the timber stock regrows at, measured
            // against the trees that are down rather than against the stock's
            // ceiling, so a wood that was barely touched comes back slowly and
            // a stripped one comes back at the pace it was stripped. Held here
            // as a number only: `Outpost.DrawWood` is what decides the far
            // trees are the ones that come back, and it clamps this again
            // against the camp's own clearing once the ground can be seen.
            float woodRate = Res.RegrowPerDay(Res.Timber);
            if (treesFelled > 0 && woodRate > 0f && treesRegrown < treesFelled)
                treesRegrown = Mathf.Min(treesFelled,
                    treesRegrown + treesFelled * woodRate * days);
            else if (treesRegrown > treesFelled) treesRegrown = treesFelled;

            // **Building comes before everything, and draws on the same
            // standing timber.** A camp that has not been built yet has
            // nowhere to stockpile TO -- the ceiling is what the fire watches
            // over and there is no fire -- so a hand told to build is not
            // choosing between two piles, they are the reason there will be
            // one.
            // Captured before the building block touches `pending`, so the
            // completion check below can tell "finished just now" from
            // "was already sitting there ready to raise".
            // **One pass down the QUEUE, oldest first (2026-09-22).** The
            // hand-days are spent on `sites[0]` until it is stocked and only
            // then on `sites[1]`, in the same tick if there is a day left
            // over -- which is exactly "the haulers move on to the next one".
            // With one site queued this is the old block to the bit.
            // **Per hand since 2026-09-23** (phone playtest): each builder
            // spends his own share of the quantum in `BuilderDay` -- clear,
            // fetch in whole armfuls, then build. Clearing and building are
            // linear in hand-days, so this is the old pooled arithmetic for
            // those two phases; the stocking is now trips. The builder list
            // is taken once, before anyone touches a store, so a gatherer
            // who starts helping cannot be counted in or out mid-pass.
            var builderHands = builderScratch;
            builderHands.Clear();
            foreach (var h in hands)
            {
                if (h == null || h.downed) continue;
                // (A trip gatherer's help is `GatherDay`'s -- his arms too --
                // so only a blocked HUNTER joins the builders here.)
                if (h.order == OutpostOrder.Build
                    || (gatherersBuild && !TripGatherer(h) && GatherBlocked(h)))
                    builderHands.Add(h);
                // (A load re-ordered mid-trip is WALKED there by whichever
                // pass owns him now -- 2026-09-27; no load lands untouched.)
            }
            foreach (var h in builderHands)
            {
                float budget = days * WorkFactor(h);
                if (sites != null) BuilderDay(h, ref budget);
                // **A builder standing about carries cargo** (2026-09-24):
                // what the site work left of his day goes on the transfer
                // orders, and an armful he is already walking is walked.
                if (budget > Eps) TransferDay(h, ref budget);
            }

            // --- gathering ---------------------------------------------------
            //
            // One pass per HAND rather than per resource, so two hands on the
            // same thing share one stock and one ceiling without this loop
            // having to know that they are two.
            //
            // **Everything but the hunt is TRIPS since 2026-09-23** (Kevin,
            // decision A): store -> source -> cut an armful -> store, each
            // trip's time its walked distance plus the cutting (`GatherDay`,
            // OutpostLedger.Stations.cs). No per-day `Res.GatherRate`
            // accrual any more. **Since 2026-09-27 the hunt is trips too**
            // (`HuntDay`, OutpostLedger.Hunting.cs): stalk, one whole kill,
            // carry, deposit 4 Food + 1 Hide at the store.
            foreach (var h in hands)
            {
                if (h == null || h.downed || h.order != OutpostOrder.Gather) continue;
                if (string.IsNullOrEmpty(h.target)) continue;
                if (TripGatherer(h)) GatherDay(h, days, gatherersBuild);
            }

            // --- working at a building ---------------------------------------
            //
            // **A position turns one thing into another.** A sawyer takes
            // timber and makes boards; a smith takes ore and makes tools; a
            // farmhand takes nothing at all, because the field is the input.
            // The conversion lives on the PLAN, so a building is the whole
            // description of what its job is worth.
            foreach (var h in hands)
            {
                if (h == null || h.downed || h.order != OutpostOrder.Work) continue;
                if (string.IsNullOrEmpty(h.target)) continue;
                // Assigned to something that is not standing here. Can happen
                // to a saved hand whose building was never restored; produce
                // nothing rather than guessing.
                if (!built.Contains(h.target)) continue;
                // **Stations work their own stock, by order** -- see
                // `StepStations` below. This loop is left with the buildings
                // whose input is the ground (the farm).
                if (IsStation(h.target)) continue;
                // **Carrying his basket to the store** (2026-09-27): walked
                // by the station pass; no harvesting on the way.
                if (h.Hauling) continue;
                // **Farm plots (food rework, 2026-09-27)**: crops per plot,
                // plant and harvest, each harvest carried to the store.
                if (h.target == BuildPlans.Farm.id) { FarmDay(h, days); continue; }
                // **At the field to work it.**
                bool hasPost = PlanPlace(h.target, OrdinalOfHand(h), out var postAt);
                float walkBudget = days * WorkFactor(h);
                if (hasPost && !WalkTo(h, postAt, ref walkBudget, WorkFactor(h))) continue;

                // **One conversion, whatever the station.** `Conversion`
                // reads the chosen (or default) recipe for a station with a
                // recipe table, and otherwise synthesises the same one-input
                // shape the old inline arithmetic spent -- see its doc
                // comment. Every read of a plan's takes/makes/rate/Yield in
                // this loop, `Stalled` and the two forecast functions goes
                // through it, so a recipe station and a legacy one share one
                // code path and cannot drift apart.
                if (!Conversion(h.target, out string makes, out Economy.Ingredient[] takes,
                        out float yield, out float ratePerDay, out string tool, out float toolWear,
                        OrdinalOfHand(h)))
                    continue;
                if (string.IsNullOrEmpty(makes) || ratePerDay <= 0f) continue;

                // A tool sits in the pile and is worn, not spent one-for-one:
                // no tool, no work at all -- the saw blade gates fine boards
                // the way a spear gates a hunt.
                if (tool != null && HeldOf(tool) <= 0f) continue;

                var made = Store(makes, true);
                // Room net of what is already in his basket.
                float room = StoreRoomF(makes) - h.basket;
                if (room <= 0f) { CarryBasket(h, makes, hasPost, postAt); continue; }

                float want = Mathf.Min(ratePerDay * days * WorkFactorOn(h, makes)
                    * PriorityMultiplier(makes), room);
                if (want <= 0f) continue;

                // **`want` outputs cost `yield` batches**, and one batch
                // spends every `takes` line at once -- clamp the OUTPUT by
                // the tightest input, not by any one of them: one log left
                // is three arrows, not one; two ore and one stone both have
                // to hold for a recipe that wants both.
                if (takes != null && takes.Length > 0)
                {
                    foreach (var line in takes)
                    {
                        if (line.n <= 0) continue;
                        want = Mathf.Min(want, HeldOf(line.res) * yield / line.n);
                    }
                    if (want <= 0f) continue;
                }
                // **No input means the ground is the input**, and if the
                // ground is tracked it is drawn down exactly as a gatherer
                // draws it: a farmhand harvests the standing Food that
                // raising the farm put there (`AddStanding`), and the field
                // grows back at the top of the next step. A ledger that has no
                // stock for what the building makes -- an older save, a
                // probe's bare farm -- is not bounded at all, as before.
                else
                {
                    var field = Stock(makes);
                    if (field != null)
                    {
                        want = Mathf.Min(want, field.standing);
                        if (want <= 0f) continue;
                    }
                }

                // The tool wears with what is actually made -- clamped the
                // same way an input would be, so `want` cannot outrun it.
                if (tool != null && toolWear > 0f)
                {
                    want = Mathf.Min(want, HeldOf(tool) / toolWear);
                    if (want <= 0f) continue;
                }

                if (takes != null && takes.Length > 0)
                {
                    float batches = want / yield;
                    foreach (var line in takes)
                    {
                        if (line.n <= 0) continue;
                        var from = Store(line.res, true);
                        from.part -= line.n * batches;
                        while (from.part < 0f && from.whole > 0) { from.whole--; from.part += 1f; }
                        if (from.part < 0f) from.part = 0f;
                    }
                }
                else
                {
                    var field = Stock(makes);
                    if (field != null) field.standing -= want;
                }

                if (tool != null && toolWear > 0f) DrawHeld(tool, toolWear * want);

                // **Into his basket, not the store** (2026-09-27): the
                // harvest counts when he carries it in (`CarryBasket`).
                h.basket += want;
                if (h.basket + 1e-4f >= Mathf.Max(1, Res.Armful(makes))
                    || StoreRoomF(makes) - h.basket < 1f)
                    CarryBasket(h, makes, hasPost, postAt);
            }

            // --- stations and hauling, 2026-09-23 ----------------------------
            //
            // Stationed workers work their bench by ORDER and fetch/haul when
            // it cannot go on; idle hands (and gatherers the store has no room
            // for, unless they went to the build above) haul for them.
            StepStations(days, !gatherersBuild);

            // **Recovery** (death/rescue phase 2): a hand laid down safe by
            // his rescuer heals on the ordinary clock, watched or not.
            StepRecovery(days);

            // --- upkeep: eating -----------------------------------------------
            //
            // Since the food rework (2026-09-27) every hand has a fullness
            // that drains, and a hungry hand WALKS to the store for the best
            // dish there (`EatStep`, OutpostLedger.Food.cs). Still every
            // quantum, so D2 holds.
            EatStep(days);

            // --- upkeep: recruiting ---------------------------------------------
            //
            // A free bed and food in the pile are both required before
            // progress accrues at all -- Kevin's call: recruiting should
            // read as something the camp EARNS, not a clock that runs
            // regardless. Checked against the pile AFTER eating, so a camp
            // that just fed its last hand on its last three Food does not
            // also recruit off the same three.
            if (Housed < HousingCapacity && FoodFill() >= RecruitFoodCost)
            {
                recruitProgress += days;
                if (recruitProgress >= DaysPerRecruit)
                {
                    recruitProgress -= DaysPerRecruit;
                    TakeFill(RecruitFoodCost);
                    string name = VillagerNames.NextFor(this);
                    hands.Add(new OutpostHand
                    {
                        name = name,
                        order = OutpostOrder.Idle,
                        born = true,
                    });
                    away.born.Add(name);
                    Life.Lives.Log(name, Life.LifeEvents.Born, CampLabel);
                }
            }

            // --- raiders, 2026-09-22 --------------------------------------------
            //
            // Only banks or bites while the ship is away -- with her there
            // the raiders are ships she can fight, not a clock on the camp.
            // A lookout on watch while you are home lets the camp relax
            // instead of just holding steady, which is why the threat comes
            // back down rather than only ever climbing.
            if (away.Open)
            {
                threat += ThreatRatePerDay * days;
                if (threat >= DaysToRaid)
                {
                    Raid();
                    threat -= DaysToRaid;
                }
            }
            else if (Guard >= 1f)
            {
                threat = Mathf.Max(0f, threat - days);
            }
        }

        /// How full this resource's pile is, for anything drawing a gauge.
        public float Fill01(string resource) => ceilingPer > 0
            ? Mathf.Clamp01(StoreCountOf(resource) / (float)ceilingPer) : 0f;

        /// Nothing more for this hand to do: their pile is full, or their
        /// stock is gone, or the thing they work at has nothing to work on.
        /// Thin wrapper over `StallCause` so the two can never disagree.
        public bool Stalled(OutpostHand h) => StallCause(h) != null;

        /// **Why no builder can lift a finger**: null while any queued site
        /// still has clearing to do, is stocked and waiting to be raised, or
        /// is short of something that exists somewhere to fetch. Otherwise
        /// the oldest unstocked site's shortfall, e.g. "needs 6 stone for
        /// the Hut — none in the store, no rock to quarry here". The same
        /// tests `FetchForSites` makes, read without moving anything.
        public string SiteShortfall()
        {
            if (sites == null) return null;
            PendingBuild stuck = null;
            for (int i = 0; i < sites.Count; i++)
            {
                var s = sites[i];
                if (s == null || s.Complete) continue;
                if (!s.Cleared || s.Stocked) return null;
                if (stuck == null) stuck = s;
            }
            if (stuck == null) return null;
            string list = null, hint = null;
            for (int k = 0; k < 3; k++)
            {
                string res = k == 0 ? Res.Timber : k == 1 ? Res.Stone : Res.Brick;
                int need = NetShort(stuck, res);
                if (need <= 0 || SiteSourceExists(res)) continue;
                string item = $"{need} {Friendly(res)}";
                list = list == null ? item : list + ", " + item;
                if (hint == null)
                    hint = res == Res.Brick ? "the quarry makes brick"
                         : res == Res.Stone ? "no rock to quarry here"
                         : "no trees left to cut";
            }
            if (list == null) return null;   // a trip is about to start
            return $"needs {list} for the {BuildPlans.Named(stuck.planId).label} — none in the store, {hint}";
        }

        /// Is there any of this to fetch for a site: the store, a station's
        /// output rack, or (timber and stone) the ground. Mirrors the order
        /// `FetchForSites` looks in.
        bool SiteSourceExists(string res)
        {
            var pile = Store(res);
            if (pile != null && pile.whole > 0) return true;
            if (stations != null)
                for (int i = 0; i < stations.Count; i++)
                {
                    var row = stations[i]?.Rack(res);
                    if (row != null && row.whole > 0) return true;
                }
            if (res == Res.Brick) return false;
            var stock = Stock(res);
            return stock != null && stock.standing >= 1f - 1e-4f;
        }

        /// Null while producing, else the reason -- lower-case sentence
        /// fragment, for the sheets. **Not the same set as `Stalled`**:
        /// walking up from the landing and working hungry both stop a hand
        /// from earning anything, but neither is what `Stalled` means (a
        /// full pile, an empty stock, nothing to work on), so `Stalled`
        /// stays blind to them and only this reads them, ahead of
        /// `StallCause`.
        public string StallReason(OutpostHand h)
        {
            if (h == null) return null;
            // **Downed/dragging/recovering/pouting (death/rescue): not a
            // stall, never flagged "stuck".** `Doing` already says what he
            // is doing; this must not pile a warning on top of it.
            if (h.downed || h.recovering || h.dragged || h.pouting
                || !string.IsNullOrEmpty(h.rescuing)) return null;
            if (h.walkingIn) return "still on the way up from the ship";
            // The body's own reason first (walled off): the books say he is
            // working, the feet say he cannot get there. Display only.
            if (!string.IsNullOrEmpty(h.bodyBlocked)) return h.bodyBlocked;
            string cause = StallCause(h);
            if (cause != null) return cause;
            // **Slow, and why, 2026-09-24** (Kevin: plank making "far too
            // slow" -- the hand should SAY when he is dragging). Below
            // `SlowWorkShown` the line names the pace his current job is paid
            // at (`WorkFactorOf`), rounded to 5 % so a sheet keyed on this
            // text rebuilds on a real change, not every step. Hungry = the
            // pile is empty or the camp is on short rations right now; low
            // spirits = fed again but mood still climbing back (or a raid).
            float pace = WorkFactorOf(h);
            if (pace < SlowWorkShown)
            {
                int pct = Mathf.Clamp(Mathf.RoundToInt(pace * 20f) * 5, 0, 100);
                string why = Hungry || rations != Rations.Full ? "hungry" : "low spirits";
                return $"working slowly — {why} ({pct}% pace)";
            }
            return null;
        }

        /// The reason `Stalled` says yes, named. Same boolean logic,
        /// term for term -- a `null` here is exactly the `false` `Stalled`
        /// used to return inline.
        string StallCause(OutpostHand h)
        {
            if (h == null) return "gone";
            // Nothing unstocked left in the QUEUE, not "nothing sited": a
            // builder whose site is stocked has the next drawing to serve.
            if (h.order == OutpostOrder.Build)
            {
                if (Focus == null) return "nothing sited to build";
                // **Standing at the fire with a site queued, 2026-09-24**
                // (Kevin: three builders "just go and stand by the camp fire
                // ... it's not clear why they won't work"): every site was
                // cleared but not stocked, and the material it is short of is
                // nowhere -- not in the store, not on a rack, not standing in
                // the ground -- so `BuilderDay` could start no trip. Named
                // here so the sheet says so. A hauler mid-trip is working.
                return h.Hauling ? null : SiteShortfall();
            }
            if (h.order == OutpostOrder.Gather)
            {
                var stock = Stock(h.target);
                // The hunter fills the Food pile, so a full Food pile is what
                // stops him -- and a herd below one animal is a herd he
                // cannot take one out of.
                if (h.target == Res.Game)
                {
                    // Out on a trip is working: a beast down is fetched
                    // whatever the store or the spear says now.
                    if (h.HuntTrip) return null;
                    string blocker = HunterBlocker();
                    if (blocker != null) return blocker;
                    if (GatherBlocked(h)) return GatherFullReason(h);
                    if (stock == null || stock.standing < 1f) return "no game left here";
                    return null;
                }
                if (GatherBlocked(h)) return GatherFullReason(h);
                // Walking home with the last armful is still working.
                if (h.Hauling) return null;
                if (stock == null || stock.standing < 1f) return "nothing left to cut here";
                return null;
            }
            if (h.order == OutpostOrder.Work)
            {
                // A lookout makes nothing and that is the job -- never
                // stalled for having nothing to show for standing watch.
                if (h.target == WatchtowerId) return null;
                if (IsStation(h.target)) return StationStallCause(h);
                if (h.target == BuildPlans.Farm.id) return FarmStallCause(h);
                if (!Conversion(h.target, out string makes, out Economy.Ingredient[] takes,
                        out _, out float ratePerDay, out string tool, out _))
                    return "not set to make anything";
                if (string.IsNullOrEmpty(makes) || ratePerDay <= 0f) return "not set to make anything";
                if (RoomFor(makes) <= 0) return "pile is full";
                if (tool != null && HeldOf(tool) <= 0f) return $"needs a {Friendly(tool)} in the pile";
                if (takes != null && takes.Length > 0)
                {
                    foreach (var line in takes)
                        if (HeldOf(line.res) <= 0f) return $"waiting on {Friendly(line.res)}";
                    return null;
                }
                // The field is the input: stripped bare is stalled, until it
                // grows back.
                var field = Stock(makes);
                return field != null && field.standing <= 0f ? "field is bare" : null;
            }
            // An idle hand carrying for the stations is not stalled.
            if (h.order == OutpostOrder.Idle && h.Hauling) return null;
            return "waiting on orders";
        }

        /// A resource id in the words the sheets show, lower-case. Not
        /// general localisation, just the vocabulary `Res` actually has.
        static string Friendly(string res) => res switch
        {
            Res.Boards => "boards",
            Res.FineBoards => "fine boards",
            Res.SawBlade => "saw blade",
            Res.Tools => "tools",
            Res.Iron => "iron",
            Res.Stone => "stone",
            Res.Timber => "timber",
            Res.Ore => "ore",
            Res.Spice => "spice",
            Res.Brick => "brick",
            Res.Arrows => "arrows",
            Res.Hide => "hide",
            Res.Spear => "spear",
            Res.IronSpear => "iron spear",
            _ => string.IsNullOrEmpty(res) ? "supplies" : res.ToLowerInvariant(),
        };

        /// **Net units per game-day this camp changes `resource` by, at its
        /// CURRENT orders, sign included.** Mirrors `Step` and `Stalled` term
        /// for term so the readout never disagrees with what a quantum
        /// actually pays -- a gatherer stalled on a full pile or a worked-out
        /// stock does not count, same as `Step` would skip them. Read-only,
        /// allocation-free: called once a frame per resource.
        /// (2026-09-27) A FORECAST for the sheets, never booking: trips are
        /// walked and goods count on arrival (docs/DELIVERY-ON-ARRIVAL.md);
        /// the measured figure is `DeliveredPerDay`.
        public float RatePerDay(string resource)
        {
            float rate = 0f;

            foreach (var h in hands)
            {
                if (h == null) continue;

                if (h.order == OutpostOrder.Gather)
                {
                    // **A hunter reads on the Food line, in meat.** His
                    // target is Game and his rate is animals a day, so the
                    // readout would be in the wrong units on the wrong row
                    // if it took him at his word: half an animal a day is
                    // two Food a day, and Game itself never moves in a
                    // pile at all.
                    if (h.target == Res.Game)
                    {
                        if (Stalled(h)) continue;
                        // **The bow shows on BOTH lines, 2026-09-22.** A
                        // hunter with arrows kills half again as many animals
                        // and spends one apiece, so Food reads higher and
                        // Arrows reads as a drain. Mirrors `Step` term for
                        // term, which is the only way a readout stays honest
                        // about a good that is consumed rather than kept.
                        // Trips since 2026-09-27: a carcass per hunt trip.
                        bool armed = HeldOf(Res.Arrows) >= 1f;
                        float kills = HuntTripPerDay(armed)
                                      * WorkFactorOn(h, Res.Food) * PriorityMultiplier(Res.Food);
                        if (resource == Res.Meat) { if (StoreRoomF(Res.Meat) > 0f) rate += kills * Res.MeatPerAnimal; }
                        else if (resource == Res.Arrows && armed) rate -= kills;
                        else foreach (var drop in Economy.Techs.HuntDrops)
                            if (drop.res == resource) rate += kills * drop.n;
                        continue;
                    }
                    if (h.target != resource || Stalled(h)) continue;
                    // Trips since 2026-09-23: an armful per walked trip.
                    rate += GatherTripPerDay(resource) * WorkFactorOn(h, resource)
                        * PriorityMultiplier(resource);
                    continue;
                }

                if (h.order == OutpostOrder.Work)
                {
                    if (string.IsNullOrEmpty(h.target) || !built.Contains(h.target)) continue;
                    if (!Conversion(h.target, out string makes, out Economy.Ingredient[] takes,
                            out float yield, out float ratePerDay, out _, out _, OrdinalOfHand(h)))
                        continue;
                    if (ratePerDay <= 0f || Stalled(h)) continue;
                    if (makes == resource)
                        rate += ratePerDay * WorkFactorOn(h, resource) * PriorityMultiplier(resource);
                    // Consumption scales with the same factor -- an angry
                    // worker draws down the input no faster than they make
                    // the output. Divided by the yield for the same reason
                    // `Step` divides: a fletcher making three arrows a day is
                    // drawing ONE log a day off the pile, and a readout that
                    // said three would have the player cutting twice what
                    // the bench can use.
                    else if (takes != null)
                        foreach (var line in takes)
                            if (line.res == resource)
                                rate -= ratePerDay * WorkFactorOn(h, makes) * PriorityMultiplier(makes)
                                    * line.n / yield;
                }
                // Build hauls from the pile into the blueprint -- a transfer,
                // not production, so it never shows up here.
            }

            // Every quantum eats regardless of whether the pile can pay --
            // an empty pile just means they go hungry, and the drain is the
            // whole point of the readout. Scaled by `EatMultiplier`, 2026-09-22,
            // so the readout agrees with `Step`: a camp on half or no rations
            // does not drain a full ration it was never going to spend.
            // (Eating is in FILL since the food rework: `FillPerDay`, not a
            // per-resource drain -- which dish goes depends on the larder.)

            return rate;
        }

        /// Only the positive terms of `RatePerDay` -- what is being made or
        /// gathered, ignoring what it costs to make it. "How fast is this
        /// being produced," for the target line.
        public float MakeRatePerDay(string resource)
        {
            float rate = 0f;

            foreach (var h in hands)
            {
                if (h == null) continue;

                if (h.order == OutpostOrder.Gather)
                {
                    // **A hunter reads on the Food line, in meat.** His
                    // target is Game and his rate is animals a day, so the
                    // readout would be in the wrong units on the wrong row
                    // if it took him at his word: half an animal a day is
                    // two Food a day, and Game itself never moves in a
                    // pile at all.
                    if (h.target == Res.Game)
                    {
                        if ((resource != Res.Meat && resource != Res.Hide) || Stalled(h)) continue;
                        // The bow, as `Step` and `RatePerDay` have it. This
                        // readout is the POSITIVE terms only, so the arrows
                        // it costs are deliberately not subtracted here --
                        // only the meat (and the hide) they buy is.
                        // Trips since 2026-09-27: a carcass per hunt trip.
                        float kills = HuntTripPerDay(HeldOf(Res.Arrows) >= 1f)
                                      * WorkFactorOn(h, Res.Food) * PriorityMultiplier(Res.Food);
                        if (resource == Res.Meat) { if (StoreRoomF(Res.Meat) > 0f) rate += kills * Res.MeatPerAnimal; }
                        else foreach (var drop in Economy.Techs.HuntDrops)
                            if (drop.res == Res.Hide) rate += kills * drop.n;
                        continue;
                    }
                    if (h.target != resource || Stalled(h)) continue;
                    // Trips since 2026-09-23: an armful per walked trip.
                    rate += GatherTripPerDay(resource) * WorkFactorOn(h, resource)
                        * PriorityMultiplier(resource);
                    continue;
                }

                if (h.order == OutpostOrder.Work)
                {
                    if (string.IsNullOrEmpty(h.target) || !built.Contains(h.target)) continue;
                    if (!Conversion(h.target, out string makes, out _, out _, out float ratePerDay, out _, out _,
                            OrdinalOfHand(h)))
                        continue;
                    if (ratePerDay <= 0f || makes != resource || Stalled(h)) continue;
                    rate += ratePerDay * WorkFactorOn(h, resource) * PriorityMultiplier(resource);
                }
            }

            return rate;
        }

        /// Put every hand here on the same order. Used when a blueprint goes
        /// down (everybody builds it) and when it is finished (everybody goes
        /// back to what an island is for).
        /// **Why this site is not moving, in words, or "" when it is.**
        /// Kevin, 2026-09-23: *"one wall segment was built, but nothing else
        /// is even though it states that people are assigned to it. this has
        /// happened before."* Two true things read as that bug, and the
        /// sheet said neither:
        /// - the builders are the CAMP's, not the site's, and they serve the
        ///   queue oldest first (`Step`), so every later site shows "Bo is on
        ///   it" while getting nothing until the ones ahead are done;
        /// - a hand works at `WorkFactor` = mood / 0.5, so a starving crew is
        ///   assigned and does exactly nothing.
        public string StallReason(PendingBuild p)
        {
            if (p == null || p.Complete || hands == null) return "";
            float strength = 0f;
            int builders = 0, walking = 0;
            foreach (var h in hands)
                if (h != null && h.order == OutpostOrder.Build)
                { builders++; strength += WorkFactor(h); if (h.walkingIn) walking++; }
            if (builders == 0) return SiteIssue(p) ?? "";
            if (walking == builders)
                return "They are still on their way up from the ship.";
            if (strength <= 0.001f)
                return "They are too hungry to work. Feed the camp and they pick the tools back up.";
            if (Hungry && strength <= builders * StarvingWorkFloor + 0.001f)
                return "They are starving and working at a third of the pace. Feed the camp.";
            // Sites are worked side by side now (the ladder), so there is no
            // "waiting its turn"; what can stop one is a material nothing
            // can supply.
            string issue = SiteIssue(p);
            if (issue != null) return issue;
            return "";
        }

        /// **A hungry camp feeds itself first, 2026-09-23.** Found by
        /// reproducing Kevin's "one wall segment was built, but nothing else
        /// is": a fresh camp has no food, mood falls 0.5 a day, `WorkFactor`
        /// is mood / 0.5, so about two days (six minutes) in, every builder
        /// is assigned and doing exactly nothing. Initiative has to include
        /// "we are starving, somebody hunt": when the pile is under a day's
        /// eating and nobody is on a food order, one free or building hand
        /// in four (at least one) goes hunting on its own; they come back to
        /// the queue once there are `FedDays` of food in. Hunting is exempt
        /// from the hunger penalty (`WorkFactorOn`), so a starving camp can
        /// always eat its way back -- if the island has game.
        /// Where the `ordinal`-th raised `planId` stands.
        bool PlanPlace(string planId, int ordinal, out Vector3 at)
        {
            at = default;
            if (raised == null) return false;
            int k = 0;
            foreach (var r in raised)
            {
                if (r == null || r.planId != planId) continue;
                if (k++ == Mathf.Max(0, ordinal)) { at = r.At; return true; }
            }
            return false;
        }

        /// **A farmhand shoulders the whole units in his basket** and walks
        /// them to the store (a picked-up trip: they are already in his arms).
        void CarryBasket(OutpostHand h, string makes, bool hasPost, Vector3 postAt)
        {
            int n = Mathf.FloorToInt(h.basket + 1e-4f);
            if (n <= 0 || h.Hauling) return;
            h.basket = Mathf.Max(0f, h.basket - n);
            if (hasPost) { h.wHas = true; if (!h.driven) { h.wx = postAt.x; h.wz = postAt.z; } }
            StartTimedTrip(h, makes, n, HaulPlace.Field, -1, HaulPlace.Store, -1, null, false, true);
            if (hasPost) { h.haulFromX = postAt.x; h.haulFromZ = postAt.z; }
        }

        public void FeedFirst()
        {
            if (hands == null || hands.Count == 0) return;
            float have = FoodFill();
            float day = hands.Count * EatPerHandPerDay;

            if (have >= day * FedDays)
            {
                foreach (var h in hands)
                    if (h != null && h.autoFood)
                    {
                        h.autoFood = false;
                        // Re-ordered by the player since? Their order stands.
                        if (h.order == OutpostOrder.Gather && h.target == Res.Game)
                        { h.order = OutpostOrder.Idle; h.target = ""; }
                    }
                return;
            }
            if (have >= day) return;

            int feeding = 0;
            foreach (var h in hands)
                if (h != null && h.order == OutpostOrder.Gather && h.target == Res.Game) feeding++;
            int want = Mathf.Max(1, hands.Count / 4);
            if (feeding >= want) return;
            var game = Stock(Res.Game);
            if (game == null || game.standing < 1f) return;   // nothing to hunt here

            // Idle first, then builders -- never a hand the player put on
            // other work.
            for (int pass = 0; pass < 2 && feeding < want; pass++)
                foreach (var h in hands)
                {
                    if (feeding >= want) break;
                    if (h == null || h.downed) continue;
                    var from = pass == 0 ? OutpostOrder.Idle : OutpostOrder.Build;
                    if (h.order != from) continue;
                    h.order = OutpostOrder.Gather;
                    h.target = Res.Game;
                    h.autoFood = true;
                    feeding++;
                }
        }

        /// Days of food in the pile before hunters who went on their own
        /// initiative come back to the build queue.
        public const float FedDays = 3f;

        /// **Free hands take the initiative, 2026-09-23.** Kevin: *"i want
        /// villagers to take initiative. gather and build on my blueprints
        /// without asking."* So a hand nobody has given anything to do
        /// (`Idle`) goes to the queue on its own the moment there is a
        /// drawing in it -- clearing, fetching the logs and stone, standing
        /// it up, exactly what the Build order already does. An order the
        /// player DID give (gather this, work that building) is left alone:
        /// initiative is what a hand does with no orders, not a veto on the
        /// ones it has. The way back is already written: when the queue
        /// empties, `Outpost` sends every builder back to `Idle`.
        ///
        /// Run from `Step`, so it covers every way a hand comes to be idle
        /// -- born, recalled, its building torn down, a save loaded -- off
        /// screen as well as on. Returns how many it enlisted, so a caller
        /// that just sited something can re-arrange the bodies at once.
        public int EnlistFree()
        {
            if (!Building || hands == null) return 0;
            int n = 0;
            foreach (var h in hands)
                if (h != null && !h.downed && h.order == OutpostOrder.Idle)
                { h.order = OutpostOrder.Build; h.target = ""; n++; }
            return n;
        }

        public void OrderAll(OutpostOrder order, string target = "")
        {
            foreach (var h in hands)
            {
                if (h == null) continue;
                h.order = order;
                h.target = target;
            }
        }
    }
}
