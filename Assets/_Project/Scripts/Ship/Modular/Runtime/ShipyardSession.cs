using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.Ship.Modular
{
    /// **The shipyard modal's hold on the world** (A8, gameplay half).
    /// Astra's modal calls `SetWorldInputBlocked(true)` when it opens and
    /// `false` when it closes. While blocked: the helm (stick, telegraph
    /// keys, row), the chase camera's pinch/wheel zoom, the island camera's
    /// gestures, the combat lock key and the broadside keys all do nothing.
    /// Time is NOT scaled. UI-side world pickers (WorldPicker, Hand,
    /// CampSiting, WallSiting) should also check `WorldInputBlocked`; they
    /// are Astra's files and were not edited (see docs/SHIPYARD-API.md).
    public static class ShipyardSession
    {
        public static bool WorldInputBlocked { get; private set; }

        public static void SetWorldInputBlocked(bool blocked) { WorldInputBlocked = blocked; }

        // ---- Live-ship visibility while she is "in" the dry dock --------
        //
        // **The double-ship bug** (2026-09-26, fix/yard-hide): since
        // `ShipyardPreview` started staging the DRAFT ship inside the world
        // dry dock slip and widening its camera's culling mask so the dock
        // and island render behind it, the LIVE ship -- still moored at her
        // home-pier berth -- renders too. `ShipyardPreview` "never accesses
        // the live ship/camera" on purpose, so this lives here instead,
        // called by `ShipyardLiveBridge.Open` alongside `SetWorldInputBlocked`
        // (see there for why it is a separate call, not folded into that
        // one).
        //
        // What gets hidden: every `Renderer` under the live ship's own
        // transform -- hull, sails, `CrewAgent`s parented onto her deck
        // (`CrewAgent.cs`: `transform.SetParent(ship, true)`), and the
        // foam/spray emitters `SpeedJuice` parents under her (`FoamEmitters`)
        // -- all found and disabled by `enabled = false`, never by disabling
        // the ship's own GameObject or touching a Collider/Rigidbody:
        // buoyancy, anchoring and saves all keep reading a live, physically
        // simulated ship throughout the refit. Only renderers that were
        // actually ON get remembered, so restoring never turns on a part
        // that was legitimately hidden for some other reason.
        //
        // `SurfaceWake` is the one exception `GetComponentsInChildren`
        // cannot reach: her wake mesh lives on a WORLD-SPACE sibling
        // GameObject the component creates itself, not a child of the ship
        // (`SurfaceWake.Awake`, "Foam stays in world space") -- so it is
        // hidden by disabling the `SurfaceWake` component instead, whose own
        // `OnDisable` already does `surface.SetActive(false)`.
        static readonly List<Renderer> hiddenRenderers = new List<Renderer>();
        static SeaSick.Ship.SurfaceWake hiddenWake;
        static bool shipHidden;

        /// `true` hides the live ship, `false` restores exactly what was
        /// hidden. Idempotent -- a second call with the same value is a
        /// no-op, so callers do not need to track whether they already
        /// asked.
        public static void SetLiveShipHidden(bool hidden)
        {
            if (hidden == shipHidden) return;
            shipHidden = hidden;
            if (hidden)
            {
                // A refit REBUILDS the ship (today in place, `old == new` --
                // see `ShipyardService.Raise` -- but this does not trust
                // that staying true): re-evaluate from the event's own
                // GameObject rather than caching a renderer list that could
                // outlive the parts it points at.
                ShipyardService.PlayerShipReplaced += OnPlayerShipReplaced;
                HideShip(ShipyardService.Player != null ? ShipyardService.Player.gameObject : null);
            }
            else
            {
                ShipyardService.PlayerShipReplaced -= OnPlayerShipReplaced;
                RestoreShip();
            }
        }

        static void OnPlayerShipReplaced(GameObject oldShip, GameObject newShip)
        {
            RestoreShip();
            HideShip(newShip);
        }

        static void HideShip(GameObject ship)
        {
            hiddenRenderers.Clear();
            hiddenWake = null;
            if (ship == null) return;
            foreach (var r in ship.GetComponentsInChildren<Renderer>(true))
            {
                if (!r.enabled) continue;
                r.enabled = false;
                hiddenRenderers.Add(r);
            }
            var wake = ship.GetComponent<SeaSick.Ship.SurfaceWake>();
            if (wake != null && wake.enabled) { wake.enabled = false; hiddenWake = wake; }
        }

        static void RestoreShip()
        {
            // Reload/refit can outrun this: a cached Renderer may already be
            // destroyed (a Unity "fake null"), so guard every one rather than
            // assuming the list is still live.
            foreach (var r in hiddenRenderers) if (r != null) r.enabled = true;
            hiddenRenderers.Clear();
            if (hiddenWake != null) hiddenWake.enabled = true;
            hiddenWake = null;
        }

        /// Statics outlive play mode here (domain reload is off).
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            WorldInputBlocked = false;
            ShipyardService.PlayerShipReplaced -= OnPlayerShipReplaced;
            hiddenRenderers.Clear();
            hiddenWake = null;
            shipHidden = false;
        }
    }
}
