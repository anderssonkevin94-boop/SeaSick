# Tap Sailing Prototype

Experimental, 2026-09-24. This is a control-feel trial, not the final nautical HUD.
The existing ten-tree/palm asset work and unrelated scene/render settings are untouched.

## Enable and Compare

HelmInput installs SailingPilot at runtime on the same ship. The prototype is on
by default for this playtest; no scene migration is required. Press Classic to
return to the existing floating stick, or Try tap sailing to switch back. A
switch stops propulsion and clears the old order. The choice is session-local.
Disable HelmInput to relinquish all player steering for existing physics probes.

## Controls

- Sailing now uses a higher, approximately 45-degree viewpoint, blended in as
  the prototype takes control. Classic framing is retained in Classic mode.
- Tap an enemy hull within 150 m to acquire its combat lock and cancel the old
  course/propulsion order. Tap that enemy again to release. This frames the fight;
  it does not fire guns or automatically chase. Helm and water orders remain available.
  Out-of-range enemy taps report the range restriction instead of sailing there.
- Tap clear water to set or replace a destination. The dotted indicator is a
  straight course suggestion, not a promise of an obstacle-free turn.
- Drag the world to pan the camera, bounded to 150 m from the follow view.
- Pinch to zoom through the existing camera. A multi-touch gesture suppresses
  single-finger taps until all fingers lift.
- Center returns to the ship; selecting a destination also recenters.
- Stop clears the destination, rowing, and engine order. Momentum and waves
  remain physical, so this is not an instant brake or an anchor.
- Start a drag on Helm to take manual control. Horizontal displacement sets
  rudder; vertical displacement changes throttle, including reverse. Releasing
  holds the current heading and selected throttle, never resumes the old route.
- WASD/arrows remain a desktop override. Releasing drive keys orders zero
  throttle; Escape stops. R keeps the existing rowing toggle.
- Selectable objects open their existing sheets. Dismissing a sheet does not
  also issue a course. Anchoring, island mode, pause and application focus loss
  clear the order. Anchored object selection remains with WorldPicker.

## Boundaries

SailingCourse validates straight courses up to 800 m against the terrain height
field at 2 m intervals, checking the center and both corridor edges. Width and
depth margins scale conservatively with hull length. Unknown terrain rejects
the order. This is sampled terrain clearance, not continuous collision proof:
very small obstacles between samples, props and moving ships are NOT routed
around. No automatic island routing, collision avoidance or docking is supplied.
Near shallow docks a course can be refused even if the target is deep; use manual
helm to leave the shallows. Existing docking rules are unchanged.

The controller slows for turns and arrival, coasts early, and stops commanding
propulsion within a hull-scaled arrival radius. After arrival it does not chase
wave drift back to the marker. The helm control remains available throughout.
An underway terrain safety check looks ahead along current velocity and orders
a stop if that corridor is unsafe; it cannot guarantee arrest before collision.

The existing ship motor, paddle forces, wave physics, loading and crew-dependent
engine response are unchanged. Autopilot uses rudder/throttle orders, not
transform movement. The existing destination hook is deliberately cleared while
this mode owns input so it cannot bypass the heading controller's damping.

## Files and Ownership

- Ship/SailingPilot.cs: gestures, input arbitration, session toggle, commands,
  temporary controls and marker. Called once by HelmInput.Update.
- Ship/SailingCourse.cs: deterministic terrain corridor and speed policy.
- Ship/HelmInput.cs: runtime installation and exclusive policy dispatch.
- CameraRig/ChaseCamera.cs: bounded world offset and manual-helm pinch guard.
- UI/Sheets/WorldPicker.cs: shared selection resolver; yields underway input to
  the prototype while retaining anchored and island selection.
- Dev/Editor/SailingPilotReview.cs: editor rule checks and save-suppressed trial.

All script paths above are relative to Assets/_Project/Scripts.

## Validation and Phone Checklist

Run SeaSick > Sailing Prototype > Validate Rules for terrain/arrival policy
checks. In Play mode, Check Runtime suppresses saving via GameBoot.Skip and tests
attachment, mode switching and stopping. Run Save-Safe Water Trial relocates the
ship in PLAY MODE ONLY into a deep-water corridor and orders an approximately
100 m turning approach; it logs to /tmp/seasick-sailing-trial.txt and stops after
65 seconds. Do not run it during ordinary gameplay or edit Assets while it runs.
Capture Game View writes /tmp/seasick-sailing-portrait.png.

Actual iPhone multi-touch, combat maneuvering, different hull/load combinations,
and subjective feel still need device playtests. The acceptance sequence is:
tap and arrive; replace route; pan without ordering; pinch without steering;
take manual control; stop; dock/cast off without stale commands; switch modes.
Check both 1080x2340 portrait and desktop landscape layouts.

No iOS binary is built by this change. Follow the project's normal iOS build
workflow after reviewing and compiling these sources. UI art-direction polish
and full obstacle routing are separate from this first experiment.

## Recorded Results

- Both runtime and editor assemblies compile with tools/compilecheck.sh.
- 13 deterministic course-policy checks passed in the Unity editor.
- Runtime attachment, clearing orders and switching modes passed.
- Actual paddle-drive trials reached straight and 45-degree destinations about
  100 m away. In the recorded turning run, range fell from about 98 m to 2 m,
  speed dropped below 1 m/s, and propulsion stayed at zero after arrival.
- Portrait 1080x2340 was rendered and inspected; prototype control labels fit.
- The final turning trace, rule result, runtime result and portrait capture are
  in validation/sailing-prototype/. Trials suppressed autosaving and the editor
  was left out of Play mode. No existing scene was saved by these tests.
- These checks do not certify device multitouch, desktop layout, combat,
  all hulls, or on-device performance. Those remain explicit playtest work.
