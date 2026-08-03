# WarmupScpSelector — Agent Notes

## Project Snapshot

- LabAPI `net48` plugin named `WarmupScpSelector` (was the EXILED `ScpslCustomRoomPlugin`).
- Purpose: a **warmup SCP draft**. During waiting-for-players, players are moved into one floating room
  showing a model of each offered SCP with a big coin; grabbing a coin picks that SCP. At round start
  the plugin lets vanilla assign roles, then swaps the selected SCP slots to the pickers. It never
  creates extra SCPs — it only rearranges vanilla's assignment.
- The room + models **despawn on round start**.

## Architecture

- `src/WarmupScpSelector/Plugin.cs` — entry point; wires LabAPI events + the core `RoleAssigner.OnBeforePlayersSpawned` hook.
- `Warmup/SelectorController.cs` — small event-driven orchestrator (build room, record picks, hand off, swap).
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
  opens a fail-closed rear extension only after its collidable world, physical weapon shelves, deterministic native
  target deck, event routes, and one shared scheduler start successfully. Verified-bounds occupancy owns exclusive
  sessions; leaving/disconnect/disable/round-start destroys held or dropped range guns, clears their reserve, removes
  lane hints, invalidates callbacks, closes the doorway, and despawns the range before Tutorial→None.
- `Activities/AimRange/RangeBotController.cs` owns up to two native RA dummies by authored slot plus live
  hub/network/player identity and spawn generation (never shared dummy `UserId`). Authored slots stay dormant until
  humans connect and reconcile every scheduler tick. A solo human is supported under a controller-owned temporary
  lobby lock (released on the second human or teardown) so counted dummies cannot falsely start the round; zero humans
  spawn none and one public slot always remains spare. Each spawn gets a deterministic conventional gun preset.
  Locomotion follows the `toy-tricks-demo` native-motor pattern directly, with no waypoint binding: small world-space
  `FpcMotor.ReceivedPosition` steps drive uninterrupted lane-1 patrol and combat strafing through clear authored
  corridors, with bounded native jumps and a stall watchdog. Each spawn deterministically selects only E11-SR,
  Logicer, or AK. A genuine tracked-firearm hit locks the first attacker's validated live hub/head for a fixed 12-second
  lease that repeat hits cannot refresh. Bots keep strafing while aiming, hold native ADS at 10 m+, jump more often
  inside 6 m, and use native `Shoot->Hold`/`Release`; initial fire still requires `ShotWeapon` plus ammo consumption.
  At low ammo they release fire/ADS, move behind slot-owned 2.2 m cover, invoke native `Reload->Click`, and resume.
  Every held input is released on LOS/session/lease loss, death, respawn, disable, and teardown. Lane events and bot
  advancement share the scheduler clock. `RangeBotLogic`, `RangeBotRegistry`, `RangeBotValidatedSettings`, and
  `RangeBotNative` keep state/ownership/config/native operations separated. Capability failures disable only that bot;
  no navmesh, repeated teleport, direct-damage, or synthetic-fire fallback exists.
- Aim lethal human damage is cancelled first in `AimRangeActivityLane.OnDying`, then synchronously reinitializes the
  same player as Tutorial with only `UseSpawnpoint`; the nested spawn routes to the range entrance, `LifeId` must
  change, and the tracked range gun/current reserve are restored. Bot aggro is cleared before reset, a secondary
  `Died -> Spectator` guard blocks death leakage, and an emergency positive-health/effects/position/gun restore covers
  co-plugin interference. Owned bot death stays native: its generation is invalidated immediately, ragdoll is
  cancelled, only its owned hub/gun are destroyed after the callback, and the slot respawns after its delay.
  The collision-free bilingual HSM range HUD is implemented (see `Text/AimRangeText.cs`): per player, three lane
  IDs — `warmupscp.aim.flash` (Y592, force-shown event verdict), `warmupscp.aim.hero` (Y700, a three-line card =
  muted eyebrow + short 5-cell state rail + value-first hits/accuracy strip), `warmupscp.aim.footer` (Y805, one
  short active-voice instruction) — plus the shared `warmupscp.status` strip, which collapses to a one-line
  pick/count/countdown at Y900 while inside the range and restores the full SCP draft panel on exit. All four
  in-range zones render on one configurable HSM center-X `Activities.Aim.HudX` (default -1077): center-X is
  ~0.556 px/unit with X=0 at ~px956, so -1077 lands every rendered box in the narrow ~px216..496 corridor between
  the native inventory list (x0..214) and the inventory wheel (x498..1404) at 1920x1080 — clear of both while TAB
  is held. Outside the range the full draft panel keeps the centered default X. Text was compressed to fit that
  corridor (short rail/footers/status, size-88% collapsed strip); bands + lane X are configurable and
  non-overlapping, no nested `<size>` spans, glyph fallback preserved. Verified with real 1920x1080 renders against
  `inventory-highlighted` (0 overlap) plus announcement/spawn-flash/waiting-highlighted regressions.
  `HsmHintDisplayProvider` resolves a per-hint X (override else global `DefaultX`) and `Services/HintChangeCache`
  backs a provider-level change-skip cache for every non-flash stable ID (player + normalized id + X/Y/text, so a
  horizontal move re-pushes); flash bypasses it and keeps token expiry.
  `Warmup/AimRangeWorld.cs` keeps all collision in explicit AdminToy boxes while spawning two validated embedded
  weapon-rack visuals. Rack markers are transformed and checked against all six `AimRangeLayout` pickup anchors at
  1 cm tolerance; a bad visual is removed while its safe colliders and layout anchors remain. The shell is now
  19.2 m × 22 m, giving three 6.4 m lanes: lane 1 has cover-backed walking/peeking bots; lane 2 owns three simultaneous
  persistent native `ShootingTargetToy`s on parallel absolute-time tracks at different depths and deterministic
  speeds (pre-damage is cancelled so they never lower/die); lane 3 owns visible collidable Aim-Lab spheres whose
  native hitscan obstacle callback pops each generation once and respawns it at another deterministic authored point.
  `SlidingTargetController` and `SphereTargetController` share the one Aim scheduler and invalidate all generations
  before synchronous handoff teardown. Persistence/global scoring remains deferred.
- `Text/WarmupText.cs` — bilingual draft hint/countdown strings + the collapsed in-range status strip; `Text/AimRangeText.cs`
  — pure `AimRangeViewState` snapshot + the range hero card / footer / flash builders (one language per call);
  `Text/ActivityGlyphs.cs` — signature glyphs (`━ ╌ │ ◆ ◇ ▲ ▼`) with ASCII fallbacks (glyph reality gate; preview in `tools/preview/banner.html`).
- `generated/models/*.mer.json` — embedded SCP models, server logo, Aim weapon rack, and the retained legacy
  moving-target carrier asset (no longer spawned by the three-lane runtime; WithCulture=false + LogicalName in the csproj).
- `tools/` — Python model pipeline: `scp_builder.py` (Builder API), `build_scp_*_asset.py` (per-SCP), `render_model_preview.py` (offline renderer, including parented/sheared logo quads; `--exposure N` brightens dark room previews to approximate in-scene point lights), `build_room_preview.py` (emits the themed room + real models/logo to render/inspect offline). `tools/preview/banner.html` previews the welcome line + status-panel TMP markup in a browser (serve over localhost; `file://` is blocked).
- `tests/models/` — `scp_model_contract.py` + `test_scp_*_model.py` geometry contracts.
- `tests/WarmupScpSelector.Tests/` — headless C# planner/activity tests (57 tests currently, including bot lifecycle,
  exact automatic-rifle presets, fixed aggro lock, continuous-path/tall-cover contracts, stale generations, damage policy,
  config validation, lethal reset state, pure MER
  root/one-level-parent transform composition, rack mismatch fail-closed behavior, all-six-slot layout alignment,
  widened three-lane bounds, deterministic absolute-time sliding motion, one-credit sphere pop/respawn state, and the
  Aim HSM UI/cache contracts).

## Key mechanic (why it is simple now)

- Tutorial warmup players are "alive", so vanilla `RoleAssigner.CheckPlayer` would skip them. The plugin
  hooks `RoleAssigner.OnBeforePlayersSpawned` (fires inside round-start, just before the eligibility
  count) and flips those Tutorial players to `RoleTypeId.None` (dead, not spectator) so the assigner
  includes them. This single hook replaced the old timer-watching / watchdog / force-start / lobby-lock
  machinery — do **not** reintroduce that complexity.
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
