# WarmupScpSelector — Agent Notes

## Project Snapshot

- LabAPI `net48` plugin named `WarmupScpSelector` (was the EXILED `ScpslCustomRoomPlugin`).
- Purpose: a **warmup SCP draft**. During waiting-for-players, players are moved into one floating room
  showing a model of each offered SCP with a big coin; grabbing a coin picks that SCP. At round start
  vanilla still chooses the SCP role multiset, but the plugin remaps the pending SCP recipients before
  role initialization/networking. It never creates extra SCPs — it only rearranges vanilla's assignment.
- The room + models **despawn on round start**.

## Architecture

- `src/WarmupScpSelector/Plugin.cs` — entry point; wires LabAPI events + the core `RoleAssigner`/pending-role hooks.
- `Warmup/SelectorController.cs` — small event-driven orchestrator (build room, record picks, hand off, atomic remap).
- `Warmup/SelectorRoom.cs` — builds/despawns the floating room: floor + walls + per-SCP pedestal, model, label, coin.
  Themed to the server brand **莺歌傲然**: a near-black navy gallery, a glowing teal floor seam, near-white lights
  (so SCP models stay true), and the cream/gold primitive server logo centered on the back wall above a gold
  welcome/QQ line. SCP displays are split into left/right banks so the logo stays unobstructed. The brand palette
  is the source of truth in `scpsl-plugins-metarepo/.server/server-identity-preview.html`.
- `Models/MerModelLoader.cs` — vendored reader: parses embedded `.mer.json` primitives and one-level
  ProjectMER empty-parent hierarchies into `PrimitiveObjectToy` data (+ MiniJson). `Models/MerWorldSpawner.cs`
  composes those transforms at arbitrary world roots, returns named markers without spawning marker geometry,
  and owns idempotent per-instance/all-instance teardown for runtime props.
- `Selection/SelectionSwapPlanner.cs`, `Selection/VanillaRoleAssignmentResolver.cs` — pure, unit-tested swap logic.
- `Activities/` — shared **warmup activity suite** (default-off, gated on `Config.ActivitiesEnabled`;
  spec/plan in `docs/warmup-activity-suite-*`). `ActivityManager` holds exclusive per-player sessions + generation
  tokens and the idempotent, non-throwing `StopForRoundStart`/`StopAll`/`OnPlayerLeft` teardown (delegated to
  registered `IActivityLane`s; switching lanes releases the prior lane first; no Harmony). `SelectorController`
  runs `StopForRoundStart` **before** the Tutorial→None flip (the hazard-cleanup ordering invariant).
  `LaneHintIds`/`LaneFlashTracker` back the provider's per-lane `warmupscp.<lane>.hero/.flash/.footer` IDs +
  token-guarded flash. `Activities/AimRange/AimRangeActivityLane` is the default-off Aim Range orchestrator: it
  opens a fail-closed full-width seam only after its collidable world, six persistent shooting-counter weapon dispensers,
  two symmetric native attachment workstations,
  deterministic target deck, event routes, and one shared scheduler start successfully. The selector gallery and training
  area then read and behave as one uninterrupted full-width rectangular hall; full-hall occupancy owns exclusive sessions.
  Replacement/drop/disconnect/disable/round-start destroys an issued gun and clears its reserve, removes lane hints,
  invalidates callbacks, closes the temporary seam gate, and despawns the training world before Tutorial→None.
- `Activities/AimRange/RangeBotController.cs` owns up to two native RA dummies by authored slot plus live
  hub/network/player identity and spawn generation (never shared dummy `UserId`). Authored slots stay dormant until
  humans connect and reconcile every scheduler tick. A solo human is supported under a controller-owned temporary
  lobby lock (released on the second human or teardown) so counted dummies cannot falsely start the round; zero humans
  spawn none and one public slot always remains spare. Each spawn gets a Crossvec.
  Locomotion follows the `toy-tricks-demo` native-motor pattern directly, with no waypoint binding: small world-space
  `FpcMotor.ReceivedPosition` steps drive uninterrupted lane-1 patrol and combat strafing through clear authored
  corridors, with bounded native jumps and a stall watchdog. A genuine tracked-firearm hit locks the first attacker's
  validated live hub after a 0.5–0.6 second reaction delay for a fixed 12-second
  lease that repeat hits cannot refresh. Bots keep strafing with tighter combat steps while aiming, hold native ADS
  throughout retaliation, jump more often
  inside 6 m, aim within the upper portion of a non-head body collider, and use native `Shoot->Hold`/`Release`; initial
  fire still requires `ShotWeapon` plus ammo consumption.
  At low ammo they release fire/ADS, move behind slot-owned 2.2 m cover, invoke native `Reload->Click`, and resume.
  Every held input is released on LOS/session/lease loss, death, respawn, disable, and teardown. Lane events and bot
  advancement share the scheduler clock. `RangeBotLogic`, `RangeBotRegistry`, `RangeBotValidatedSettings`, and
  `RangeBotNative` keep state/ownership/config/native operations separated. Capability failures disable only that bot;
  no navmesh, repeated teleport, direct-damage, or synthetic-fire fallback exists.
- `Activities/Parkour/ParkourActivityLane.cs` is the default-off Pulse Line game. It requires the Aim hall and owns the
  formerly empty far-left bay behind the counter; a full-height divider isolates it from lane-1 bot fire. One shared
  20 Hz loop handles occupancy, 0.6 s start hold, authoritative three-second countdown, ordered swept-segment gates,
  timer/HSM updates, fall recovery, finish/PB, and the reusable reset coin. The folded single-player route has 17
  static cream landings across four sectors, stays below 2.75 m for ceiling clearance, and returns to FIN beside START.
  Falling to the hall floor teleports to the last completed landing without stopping the clock. `ActivityManager`
  exclusivity removes the Aim gun/session when entering the bay; Aim only carves out the bay after parkour starts.
- Aim lethal human damage is cancelled first in `AimRangeActivityLane.OnDying`, then synchronously reinitializes the
  same player as Tutorial with only `UseSpawnpoint`; the nested spawn routes to the range entrance, `LifeId` must
  change, and the tracked range gun/current reserve are restored. Bot aggro is cleared before reset, a secondary
  `Died -> Spectator` guard blocks death leakage, and an emergency positive-health/effects/position/gun restore covers
  co-plugin interference. Owned bot death stays native: its generation is invalidated immediately, ragdoll is
  cancelled, only its owned hub/gun are destroyed after the callback, and the slot respawns after its delay.
  The collision-free bilingual HSM range HUD is implemented (see `Text/AimRangeText.cs`): per player, three lane
  IDs — `warmupscp.aim.flash` (Y592, force-shown event verdict), `warmupscp.aim.hero` (Y700, a three-line card =
  muted eyebrow + short 5-cell state rail + value-first hits/accuracy strip), `warmupscp.aim.footer` (Y805, one
  short active-voice instruction). The former room seam is UI-only: the selector side shows only the untouched original
  `warmupscp.status` SCP draft panel, while the training side removes that panel and shows only the three Aim IDs.
  Combat, issued guns, damage routing, and bot provocation still use full-hall occupancy. The three Aim zones render
  on one configurable HSM center-X `Activities.Aim.HudX` (default -1077): center-X is
  ~0.556 px/unit with X=0 at ~px956, so -1077 lands every rendered box in the narrow ~px216..496 corridor between
  the native inventory list (x0..214) and the inventory wheel (x498..1404) at 1920x1080 — clear of both while TAB
  is held. Outside the range the full draft panel keeps the centered default X. Text was compressed to fit that
  corridor (short rail/footers); bands + lane X are configurable and
  non-overlapping, no nested `<size>` spans, glyph fallback preserved. Verified with real 1920x1080 renders against
  `inventory-highlighted` (0 overlap) plus announcement/spawn-flash/waiting-highlighted regressions.
  `HsmHintDisplayProvider` resolves a per-hint X (override else global `DefaultX`) and `Services/HintChangeCache`
  backs a provider-level change-skip cache for every non-flash stable ID (player + normalized id + X/Y/text, so a
  horizontal move re-pushes); flash bypasses it and keeps token expiry.
  `Warmup/AimRangeWorld.cs` keeps all collision in explicit AdminToy boxes. The old weapon-rack visual and all
  shelf/cradle collision are no longer spawned; `AimRangeLayout` places six persistent guns on the shooting counter
  and two symmetric native attachment workstations against the side walls.
  Interacting with a dispenser cancels native pickup and grants a separately owned inventory copy, leaving the displayed gun.
  The counter wall spans `ShellWidth - 0.6 m`; exactly three intensity-24/range-16 point lights illuminate the training
  half and exactly three intensity-24/range-18 point lights illuminate the selector half, all with ordinary non-HDR light colors.
  The branded logo retains its HDR albedo boost and shares the selector's center light for bloom; it does not own a fourth light.
  The outer training shell matches the dynamic gallery width, eliminating the former doorway/choke; the centered
  19.2 m × 22 m play zone gives three 6.4 m lanes: lane 1 has cover-backed walking/peeking bots; lane 2 owns three simultaneous
  persistent native `ShootingTargetToy`s on parallel absolute-time tracks at different depths and deterministic
  dynamically varying deterministic speeds (pre-damage is cancelled so they never lower/die); lane 3 owns 20 spread-out,
  visible collidable Aim-Lab spheres whose
  native hitscan obstacle callback credits each generation once and immediately teleports that same always-visible,
  collidable, non-static transform-synchronized toy to another deterministic authored point. There is no sphere
  despawn/respawn delay and flags never toggle, avoiding flashes and destroy/recreate churn.
  `SlidingTargetController` and `SphereTargetController` share the one Aim scheduler and invalidate all generations
  before synchronous handoff teardown. Persistence/global scoring remains deferred.
- `Text/WarmupText.cs` — bilingual original draft hint/countdown strings (plus a retained, unused legacy collapsed-strip builder); `Text/AimRangeText.cs`
  — pure `AimRangeViewState` snapshot + the range hero card / footer / flash builders (one language per call);
  `Text/ActivityGlyphs.cs` — signature glyphs (`━ ╌ │ ◆ ◇ ▲ ▼`) with ASCII fallbacks (glyph reality gate; preview in `tools/preview/banner.html`).
- `generated/models/*.mer.json` — embedded SCP models, server logo, and retained legacy Aim rack/moving-target
  assets (no longer spawned by the three-lane runtime; WithCulture=false + LogicalName in the csproj).
- `tools/` — Python model pipeline: `scp_builder.py` (Builder API), `build_scp_*_asset.py` (per-SCP), `render_model_preview.py` (offline renderer, including parented/sheared logo quads; `--exposure N` brightens dark room previews to approximate in-scene point lights), `build_room_preview.py` (emits the themed room + real models/logo to render/inspect offline). `tools/preview/banner.html` previews the welcome line + status-panel TMP markup in a browser (serve over localhost; `file://` is blocked).
- `tests/models/` — `scp_model_contract.py` + `test_scp_*_model.py` geometry contracts.
- `tests/WarmupScpSelector.Tests/` — headless C# planner/activity tests (61 tests currently, including bot lifecycle,
  exact automatic-rifle presets, fixed aggro lock, continuous-path/tall-cover contracts, stale generations, damage policy,
  config validation, lethal reset state, pure MER
  root/one-level-parent transform composition, continuous full-room bounds and persistent counter-armoury layout,
  widened three-lane bounds, deterministic absolute-time sliding motion, one-credit immediate sphere relocation, and the
  Aim HSM UI/cache contracts).
  The dev-only live verifier currently passes `253/253`, including pooled same-toy sphere relocation, a 0.564 s first return shot, and native stationary-attacker lethal reset in 1.022 s,
  all-retaliation ADS, non-head aim, the UI-only seam, and both three-light hall halves.

## Key mechanic (why it is simple now)

- Tutorial warmup players are "alive", so vanilla `RoleAssigner.CheckPlayer` would skip them. The plugin
  hooks `RoleAssigner.OnBeforePlayersSpawned` (fires inside round-start, just before the eligibility
  count) and flips those Tutorial players to `RoleTypeId.None` (dead, not spectator) so the assigner
  includes them. It then buffers cancellable `RoundStart` SCP `ChangingRole` events and applies the final
  slot-preserving permutation on the last SCP callback, before `HumanSpawner` runs. A displaced holder stays
  `None` until vanilla gives it one human role, so clients never see SCP→human or human→SCP. No Harmony.
  This event path replaced timer-watching / watchdog / force-start / lobby-lock machinery — do **not**
  reintroduce that complexity.
- The room is self-contained and floats at `Config.RoomOrigin` (no map/door lookups).

## Build / test

- `dotnet build -c Release` (set `ServerManagedPath` if the dedicated server isn't at the default Steam path).
- Planner tests: build `tests/WarmupScpSelector.Tests` and run the produced `.exe`.
- Models: `pip install -r requirements.txt`; `python tools/build_scp_<id>_asset.py`; render; `python tests/models/test_scp_<id>_model.py`.

## Conventions

- Treat this folder as its own product (vendored loader + pipeline; no cross-repo deps).
- Prefer native LabAPI events/wrappers over Harmony. Keep the plugin small and event-driven.
- Player-facing text is bilingual (EN/CN via `Language`); code, identifiers, and these notes stay English.
- Live behavior needs a running server to verify. Planner/activity state and model geometry are headless-tested; native dummy association, real firearm retaliation, repeated bot death/respawn, and no-spectator lethal reset still require the focused live harness/final QA.
