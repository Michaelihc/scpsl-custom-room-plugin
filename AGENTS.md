# WarmupScpSelector — Agent Notes

## Project Snapshot

- LabAPI `net48` plugin named `WarmupScpSelector` (was the EXILED `ScpslCustomRoomPlugin`).
- Purpose: a **warmup SCP draft** plus early-round SCP disconnect replacement. During waiting-for-players, players are moved into one floating
  **space station** whose south gallery shows a model of each offered SCP with a big coin; grabbing a coin picks that SCP. At round start
  vanilla still chooses the SCP role multiset, but the plugin remaps the pending SCP recipients before
  role initialization/networking. It never creates extra SCPs — it only rearranges vanilla's assignment.
  After round start, a healthy early-disconnecting SCP can open that exact vacant role to a short
  UserId-backed `.volunteer` lottery; spectators and living non-SCP humans are both eligible by default.
- The station + models **despawn on round start**.
- The station's plan is derived from the authored ProjectMER room the server owner built (`DT.json`): a
  central hub with an east Aim Bay, a west observation deck, a south SCP gallery down a short connector,
  and a north parkour shaft. The numbers were regularised into named constants rather than kept as an
  exported asset, so the room is code-extensible; the topology is the author's.

## Architecture

- `src/WarmupScpSelector/Plugin.cs` — entry point; wires LabAPI events + the core `RoleAssigner`/pending-role hooks.
- `Warmup/SelectorController.cs` — small event-driven orchestrator (build room, record picks, hand off, atomic remap).
- `Warmup/WarmupHallLayout.cs` — **single source of truth for the whole station**: compartments
  (`StationZone`), hatches (`StationOpening`), the SCP gallery stands, spawn point, and logo anchor. Its
  `BuildWallSegments` is pure geometry that cuts hatches out of compartment faces and is what guarantees a
  sealed shell; a shared face is never built twice because the narrower compartment's face lies entirely
  inside the hatch. Extending the station means adding a zone + an opening — the shell builder walls and
  lights it for free. `ContainsPoint` exists because `Bounds.Contains` is a native ECall and would make
  every occupancy rule untestable headlessly.
- `Warmup/StationShellBuilder.cs` — spawns that geometry: deck, overhead, bulkheads with ribs, hatch
  frames, amber threshold striping, teal wayfinding strips, bilingual hatch signage, deck lighting.
  Z-fighting rule for anything added here: two surfaces flicker when they face the SAME way at the same
  depth, so an abutting pair is fine and a shared plane is not. The three that were real: a hatch gate
  built to exactly fill its opening landed on the frame's own planes (now inset by `GateInset`), the
  panel above a hatch shared its underside with the shorter neighbour's overhead (now dipped by
  `WarmupHallLayout.AboveHatchDip` into the lintel, which is thicker than the wall), and the aim
  backstop plate was sunk into the far bulkhead (now proud of it). Faces at deck level y=0 are buried in
  the 0.4 m deck slab and are not worth chasing.
- `Warmup/StationPalette.cs` — the one **orbital-station** theme (cold graphite structure, near-white deck
  lighting, brand teal/cyan for wayfinding, amber for caution, gold for brand text). Two rules: lights stay
  near-white (a tinted lamp washes out the muted SCP model primitives) and saturation is a signal, never
  decoration. Brand hues come from `scpsl-plugins-metarepo/.server/brand/server-identity-preview.html`.
- `Warmup/SelectorRoom.cs` — builds/despawns the station: shell, SCP gallery (low collidable plinth +
  model + label + frozen coin per offered SCP), the cream/gold primitive server logo high on the gallery
  back wall above the welcome/QQ line, and the observation deck's star-field viewport. The gallery's centre
  aisle is deliberately left empty so the logo reads straight down it from the arrival point. Per-lane
  **hatch gates** replace the old full-width seam: each activity compartment is sealed during setup and its
  panel removed only once that lane started successfully.
- `Models/MerModelLoader.cs` — vendored reader: parses embedded `.mer.json` primitives and one-level
  ProjectMER empty-parent hierarchies into `PrimitiveObjectToy` data (+ MiniJson). `Models/MerWorldSpawner.cs`
  composes those transforms at arbitrary world roots, returns named markers without spawning marker geometry,
  and owns idempotent per-instance/all-instance teardown for runtime props.
- `Selection/SelectionSwapPlanner.cs`, `Selection/VanillaRoleAssignmentResolver.cs` — pure, unit-tested swap logic.
- `Replacement/` — LabAPI port of the Jon M/Augaton SCPReplacer flow. It snapshots a healthy main-SCP
  disconnect inside the early cutoff, stores volunteers only by authenticated UserId, accepts spectators plus
  living non-SCP players by default, starts one generation-guarded lottery on the first volunteer, and rechecks
  that the role is still vacant before promotion. Round end/restart/disable cancels all callbacks. Optional
  `.human`/`.no` opens the SCP's old slot only after eligibility/capacity checks and rolls a configured human role.
- `Activities/` — shared **warmup activity suite** (default-off, gated on `Config.ActivitiesEnabled`;
  spec/plan in `docs/warmup-activity-suite-*`). `ActivityManager` holds exclusive per-player sessions + generation
  tokens and the idempotent, non-throwing `StopForRoundStart`/`StopAll`/`OnPlayerLeft` teardown (delegated to
  registered `IActivityLane`s; switching lanes releases the prior lane first; no Harmony). `SelectorController`
  runs `StopForRoundStart` **before** the Tutorial→None flip (the hazard-cleanup ordering invariant).
  `LaneHintIds`/`LaneFlashTracker` back the provider's per-lane `warmupscp.<lane>.hero/.flash/.footer` IDs +
  token-guarded flash. `Activities/AimRange/AimRangeActivityLane` is the default-off Aim Range orchestrator,
  now housed in the station's **east Aim Bay** (x 11..36, z +/-9.5). It opens that bay's hatch only after its
  furniture, six persistent shooting-counter weapon dispensers, two symmetric native attachment workstations,
  deterministic target deck, event routes, and one shared scheduler start successfully.
  Replacement/drop/disconnect/disable/round-start destroys an issued gun and clears its reserve, removes lane
  hints, invalidates callbacks, re-seals the bay hatch, and despawns the range furniture before Tutorial->None.
- `Warmup/AimRangeLayout.cs` — the range fires **along +X** down the arm, with the three lanes stacked across
  Z: lane 1 bots+cover (z -9.5..-3.1), lane 2 sliding targets (-3.1..2.9), lane 3 spheres (2.9..9.5, the widest
  because the cloud needs the most clear width). Everything is authored through one `Range(lateral, up,
  downrange)` frame, and every authored rotation is `RangeRotation` (a -90 degree turn) applied to the old
  -Z-downrange facing — that is why the sliding targets are identity and the sphere lane is Euler(0,-90,0).
  `Warmup/AimRangeWorld.cs` adds only range furniture (counter, lane dividers, cover, rails, workstations,
  counter lighting, signage); the bay's deck/walls/overhead belong to the station shell.
- `Activities/Parkour/ParkourActivityLane.cs` is the default-off **Pulse Line**, now in the station's own
  9 m x 53.5 m x 13.5 m **parkour shaft** north of the hub. It no longer depends on the Aim range at all.
  One shared 20 Hz loop handles occupancy, 0.6 s start hold, authoritative three-second countdown, ordered
  swept-segment gates, timer/HSM updates, fall recovery, finish/PB, and the reusable reset coin.
- **The route is generated, not hand-placed** (`ParkourLayout` + `ParkourJumpModel`). `ParkourJumpModel` is
  the closed form of the native arc (`y = J*t - g*t^2/2`, gravity 19.6 from `FpcGravityController`), caught on
  the DESCENDING branch — the ascending root gives barely a third of the reach. `SelectorController.ResolveJumpModel`
  reads the **live Tutorial prefab's** `JumpSpeed`/`WalkSpeed`/`SprintSpeed`, so the geometry adapts to what the
  game actually ships. Measured 2026-08-28: `jump 4.9 / walk 3.9 / sprint 5.4`, apex **0.61 m** — far below what
  a hand-authored course would assume, which is exactly why the route is generated.
  Each hop's gap comes from a difficulty ramp (0.45 -> 0.86 of the reach a **sprinting** player has), the
  sideways swing is taken out of that same budget, and the step rise is capped on the PLACEMENT, not just in
  the gap arithmetic. The layout **fails closed** if the result would be impossible (>0.92), trivial
  (<0.72 peak), out of the shaft, or short on headroom.
- Three calibration facts were established in game and are easy to get wrong again:
  1. **Gap is edge-to-CENTRE**, not edge-to-edge. A player runs to the take-off pad's far edge and aims for
     the middle of the next pad; crediting the landing's near half as free reach makes the course about a
     third easier than the numbers claim.
  2. **Difficulty is anchored to sprint speed.** A blend between walk and sprint put the closing hops within
     2% of walk reach, and a walking dummy completed the entire course.
  3. A fixed-metre lateral zig-zag silently eats the whole jump budget and makes late hops unclearable —
     the swing must be a fraction of the hop, not a constant.
- Fall recovery catches a miss ~0.2 s into the fall (`HasFallenOffRoute`: 1.75 m below the last cleared
  landing), not on impact, so a slip from the top of the shaft never reaches the fall-damage table.
- Aim lethal human damage is cancelled first in `AimRangeActivityLane.OnDying`, then synchronously reinitializes the
  same player as Tutorial with only `UseSpawnpoint`; the nested spawn routes to the range entrance, `LifeId` must
  change, and the tracked range gun/current reserve are restored. Bot aggro is cleared before reset, a secondary
  `Died -> Spectator` guard blocks death leakage, and an emergency positive-health/effects/position/gun restore covers
  co-plugin interference. Owned bot death stays native: its generation is invalidated immediately, ragdoll is
  cancelled, only its owned hub/gun are destroyed after the callback, and the slot respawns after its delay.
  The collision-free bilingual HSM range HUD is implemented (see `Text/AimRangeText.cs`): per player, three lane
  IDs — `warmupscp.aim.flash` (Y592, force-shown event verdict), `warmupscp.aim.hero` (Y700, a three-line card =
  muted eyebrow + short 5-cell state rail + value-first hits/accuracy strip), `warmupscp.aim.footer` (Y805, one
  short active-voice instruction). The HUD switches at the bay threshold (`ContainsAimUi`): outside it, only the
  untouched original `warmupscp.status` SCP draft panel; inside, that panel is removed and only the three Aim IDs
  show. Combat, issued guns, damage routing, and bot provocation use the larger `ContainsVerified` bounds, which
  reach back through the hatch so a shooter standing in the doorway still behaves normally. The three Aim zones render
  on one configurable HSM center-X `Activities.Aim.HudX` (default -1077, aimed at the narrow corridor between
  the native inventory list (x0..214) and the inventory wheel (x498..1404) at 1920x1080 while TAB is held).
  SUSPECT since the 2026-08-18 in-game HSM recalibration (see `..\.tests\AGENTS.md`): center-X is actually
  0.5 px/unit (not 0.556, so -1077 renders ~px421), and multi-char lines starting left of X≈-800 word-wrap
  after their first glyph — this lane likely renders garbled in game and needs the ghost-tail re-place +
  an in-game recheck. Outside the range the full draft panel keeps the centered default X. Text was compressed to fit that
  corridor (short rail/footers); bands + lane X are configurable and
  non-overlapping, no nested `<size>` spans, glyph fallback preserved. Verified with real 1920x1080 renders against
  `inventory-highlighted` (0 overlap) plus announcement/spawn-flash/waiting-highlighted regressions.
  `HsmHintDisplayProvider` resolves a per-hint X (override else global `DefaultX`) and `Services/HintChangeCache`
  backs a provider-level change-skip cache for every non-flash stable ID (player + normalized id + X/Y/text, so a
  horizontal move re-pushes); flash bypasses it and keeps token expiry.
  `Warmup/AimRangeWorld.cs` keeps all range collision in explicit AdminToy boxes and adds only furniture; the bay's
  deck, bulkheads, overhead, and general lighting come from the station shell. The old weapon-rack visual and all
  shelf/cradle collision are no longer spawned; `AimRangeLayout` places six persistent guns on the shooting counter
  and two symmetric native attachment workstations against the side walls.
  Interacting with a dispenser cancels native pickup and grants a separately owned inventory copy, leaving the displayed gun.
  The counter spans `ShellWidth - 0.6 m` across the bay, and three intensity-24/range-16 point lights sit over the
  firing line, with ordinary non-HDR light colors. The branded logo keeps its HDR albedo boost and shares a gallery
  light for bloom; it does not own a light of its own.
  The 19 m x 21.5 m bay gives three lanes: lane 1 has cover-backed walking/peeking bots; lane 2 owns three simultaneous
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
- `Export/` — ProjectMER schematic export so the room can be handed to someone else and edited in the
  in-game map editor. `StationSchematic` is the format writer (verified against the vendored ProjectMER
  source, NOT inferred from a sample: blocks are a flat list linked by `ParentId`, transforms are LOCAL
  to the parent, colours are 8-char `RRGGBBAA` so alpha survives, and a text block's `DisplaySize` is
  multiplied by 20 on load so it is stored divided by 20). `StationSchematicExporter` reads the LIVE
  world rather than re-describing geometry from the layout - a second description would drift from the
  builders invisibly. It sorts by hierarchy depth so a parent is always written before its children,
  which is what preserves the logo's shear (a flattened child loses it and the emblem comes back subtly
  wrong). Non-geometry - spawn point, hatches, firing line, parkour landings, coins, workstations - is
  emitted as `marker_*` empties. Triggered by the `warmupexport` RA command or `Config.ExportSchematicName`.
  A checked-in export lives in `generated/schematics/warmup_station/`.
- `Import/` — merge-back. `Config.AuthoredStationAsset` names a schematic that is spawned INSTEAD of the
  generated shell, decor, and exhibits (looked up in this plugin's `Schematics/` then ProjectMER's). It
  fails soft: not configured, missing, unreadable, or a failed spawn all fall back to generating.
  Two things are never taken from an asset, and both are load-bearing:
  1. **The Aim Bay.** Its dividers and cover are what the bots path around and shoot from, and the lane
     spawns that furniture itself, so taking it from the asset too would spawn every piece twice. The
     compartment is skipped and rebuilt by code, shell INCLUDED - skipping a compartment without
     rebuilding its shell once left the parkour shaft with no floor and dropped players out of the
     station; `warmup-station` caught it.
  2. **Pickups.** Selection coins carry a serial-to-SCP-role binding and counter guns carry owned-weapon
     bookkeeping. A static copy looks right and does nothing, so code spawns them at the anchors.

  The parkour shaft, by contrast, IS spawned from the asset, and `AuthoredParkourRoute` recovers the
  gates from those landings, so geometry and gates stay one thing and an author's route drives the run.
  `Import/AuthoredGalleryAnchors.cs` does the same job for the SCP gallery: a coin is anchored to the
  exhibit whose LABEL names that SCP, not to a freshly computed slot. Anchoring coins on the slot grid
  is only correct while the live config still reproduces the grid the asset was exported from -
  `pedestal_spacing` 3.7 -> 4.0 drops the back rank from six stands to four and left three coins 4.65 m
  from the SCP they draft, with every authored label still in place, so a player read "SCP-096", took the
  nearest coin, and got SCP-173. Unmatched options fall back to the slot and are logged.
- **Block ids are opaque; only a MISSING id means "no id".** ProjectMER writes `ObjectId` from Unity's
  `GetInstanceID()`, and a file saved in the map editor can come back with EVERY id negative (real case:
  `DT (2).json`, -18862..-1678). The reader used to treat negative as absent, which unparented all 574
  child blocks - and an unparented child spawns its LOCAL offset at the station origin, so the author's
  model and the logo's shear quads piled into the middle of the hub (578 blocks within 3 m of the centre,
  measured off a live export). `StationAssetBlock.NoId` is the sentinel; a child whose parent exists but
  was not spawned falls back to `ApproximateRootPosition` rather than to the origin.
- Keep the exported `marker_*` blocks when editing. They are the anchor contract, and without them an
  importer has to infer gameplay positions from geometry - a landing detector run over a real edited file
  picked a ceiling panel as a landing, which would have been a 9.9 m impossible hop.
- `generated/models/*.mer.json` — embedded SCP models, server logo, and retained legacy Aim rack/moving-target
  assets (no longer spawned by the three-lane runtime; WithCulture=false + LogicalName in the csproj).
- `tools/` — Python model pipeline: `scp_builder.py` (Builder API), `build_scp_*_asset.py` (per-SCP), `render_model_preview.py` (offline renderer, including parented/sheared logo quads; `--exposure N` brightens dark room previews to approximate in-scene point lights), `build_room_preview.py` (**STALE** — still emits the pre-station single hall; not updated for the station rework). `tools/preview/banner.html` previews the welcome line + status-panel TMP markup in a browser (serve over localhost; `file://` is blocked).
- `tests/models/` — `scp_model_contract.py` + `test_scp_*_model.py` geometry contracts.
- `tests/WarmupScpSelector.Tests/` — headless C# planner/activity/replacement tests (86 currently, including station
  compartment tiling, wall-panel sealing, gallery stands, the Aim Bay's three lanes, the parkour jump model and
  generated route across a jump-speed sweep, bot lifecycle,
  exact automatic-rifle presets, fixed aggro lock, continuous-path/tall-cover contracts, stale generations, damage policy,
  config validation, lethal reset state, pure MER
  root/one-level-parent transform composition, station compartment tiling and wall-panel sealing, gallery stand
  placement, the Aim Bay's three-lane bounds and persistent counter armoury, deterministic absolute-time sliding motion, one-credit immediate sphere relocation, and the
  Aim HSM UI/cache contracts, authored-schematic hierarchy resolution under negative ObjectIds, and authored gallery coin
  anchors surviving a slot-grid change).
  `tests/WarmupPlaytestScenarios/` ships this plugin's live dummy scenarios for the shared `.tests\Playtest`
  harness: `warmup-station` (raycast-walks every compartment for deck/walls/hatches, settles a dummy at the
  arrival point) and `warmup-pulse-line` (discovers the route by raycast, then brackets it with three real
  dummies — sprint+jump must complete, walk+jump must stop late, no-jump must stop at the first landing).
  Run with `ptest run warmup standard`. The station geometry comes from public API, not reflection.
  `tests/WarmupRangeVerifier` is **NOT updated for the station rework** — its 253 checks assert the old
  single-hall seam and will fail until migrated.

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
