# WarmupScpSelector architecture

Source paths below `Warmup/`, `Selection/`, `Replacement/`, `Activities/`, `Models/`,
`Import/`, `Export/`, `Text/` and `Services/` are relative to `src/WarmupScpSelector/`.

## Assignment and replacement

`Plugin.cs` wires native LabAPI events. `Warmup/SelectorController.cs` builds the room,
records picks, stops activities and performs the round-start handoff.
`Selection/SelectionSwapPlanner.cs` and `Selection/VanillaRoleAssignmentResolver.cs` own pure swap logic.

Tutorial warmup players are alive, so `RoleAssigner.CheckPlayer` would skip them.
The native `RoleAssigner.OnBeforePlayersSpawned` hook changes them to `RoleTypeId.None` before
eligibility counting. Cancellable round-start SCP role events are buffered and the final
slot-preserving permutation is applied before human spawning. Preserve the native SCP multiset
and the single human assignment for displaced players; do not reintroduce timer-based force-start
or watchdog machinery. Activities stop before this handoff.

`Replacement/` snapshots healthy main-SCP disconnects inside the early cutoff, stores volunteers
by authenticated UserId and rechecks vacancy before promotion. Spectators and living non-SCP
humans are eligible by default. Round end/restart/disable invalidates every callback.
Optional human-role opt-out also checks eligibility and capacity before opening a slot.

## Station geometry and presentation

`Warmup/WarmupHallLayout.cs` owns compartment geometry, hatches, gallery stands, spawn and logo
anchors. Compartments are a central hub, east Aim Bay, west observation deck, south gallery and
north parkour shaft. `BuildWallSegments` cuts openings without duplicating shared faces.
`StationShellBuilder.cs` owns deck, bulkheads, overhead, hatch signs and general lights.
`SelectorRoom.cs` owns exhibits, coins, server logo and teardown. An activity hatch opens only
after that lane starts successfully.

Keep same-facing surfaces separated enough to avoid depth fighting, including same-color pairs.
Signs need dark backing against pale bulkheads. `StationPalette.cs` supplies graphite structure,
near-white lighting, teal/cyan wayfinding, amber caution and gold branding. Keep general light colors
near white so they do not wash out model colors.

`Models/MerModelLoader.cs` reads embedded primitives and parent hierarchies.
`MerWorldSpawner.cs` composes world transforms, returns markers without visible marker geometry,
and owns idempotent instance cleanup.

## Activities and ownership

Activities are default-off through `Config.ActivitiesEnabled`.
`ActivityManager` owns exclusive per-player sessions, generation tokens and non-throwing lane
teardown. Lane changes release the preceding lane first. `LaneHintIds` and `LaneFlashTracker`
own stable hero/flash/footer identities and token-bound flash expiry.

The Aim Bay fires along +X. `Warmup/AimRangeLayout.cs` uses a shared lateral/up/downrange frame
and explicit authored rotation. Its three lanes contain cover-backed bots, sliding native
shooting targets and collidable spheres. `AimRangeWorld.cs` owns furniture; the station shell
owns deck/walls/overhead. Six persistent gun dispensers and two native attachment workstations
remain behind the firing line. Dispensing cancels native pickup and grants a separately owned gun.

One scheduler drives target motion and lifecycle. Sliding targets use absolute-time tracks and
cancel native lowering/death. Sphere hits credit each generation once and immediately reposition
the existing visible, collidable, synchronized toy. Avoid repeated despawn/respawn or flag toggles.
Dropping/replacing guns, disconnecting, disabling and round handoff clear owned guns/reserves,
hints, callbacks and furniture.

Lethal human damage is cancelled before synchronously reinitializing the same player as Tutorial
with `UseSpawnpoint`. The player's LifeId changes, the range gun/reserve is restored and bot aggro
is cleared. Keep the secondary spectator guard and recovery path for co-plugin interference.
Owned bot deaths remain native; invalidate their generation before delayed owned-hub cleanup/respawn.

## Parkour

`Activities/Parkour/ParkourActivityLane.cs` owns the independent northern Pulse Line.
One 20 Hz loop handles occupancy, start hold, optional countdown, swept gates, timer/HSM, fall
recovery, finish/personal best and the reset coin.

`ParkourLayout` and `ParkourJumpModel` generate the route from the live Tutorial prefab's jump,
walk and sprint speeds. Use the descending branch of the jump arc; budget gaps from takeoff edge
to landing center, anchor difficulty to sprint speed, and take lateral swing from the same reach
budget. Cap step rise on placement and reject impossible, trivial, out-of-bounds or low-clearance
layouts. Fall recovery catches misses below the last cleared landing before impact damage.

## Hints and text

`Text/WarmupText.cs`, `Text/AimRangeText.cs` and `Text/ActivityGlyphs.cs` own bilingual text
and glyph fallbacks. `Services/HintChangeCache` keys stable hints by player, ID, position and text;
flash hints bypass the change-skip cache. HSM provider X overrides must participate in that key.

Inside Aim UI bounds, remove the draft panel and show only the Aim hero/flash/footer.
Combat bounds are larger than UI bounds so shooting from the doorway remains consistent.
Use the shared Center-plus-X rules in `../../.tests/AGENTS.md`; the remaining in-game placement
check is recorded in [development](development.md).

## Authored station round trip

`Export/StationSchematicExporter.cs` reads the live world, not a duplicate geometry description.
`StationSchematic` writes a flat parent-linked list, local transforms, RRGGBBAA colors and
DisplaySize divided by the loader's scale factor. Write parents before children to preserve shear.
Spawn, hatch, firing-line, parkour, coin and workstation anchors are `marker_*` empties.

`Config.AuthoredStationAsset` replaces the generated shell/decor/exhibits when its asset loads;
missing, unreadable or failed assets fall back to generation.
The Aim Bay shell/furniture and functional pickups remain code-owned to prevent duplicate collision
and unbound coins/guns. The authored parkour route supplies both geometry and gates.

`Import/AuthoredGalleryAnchors.cs` matches coins to the exhibit label naming the SCP.
Do not reposition coins from a regenerated slot grid when the authored exhibit remains elsewhere.
Unmatched options use the slot fallback and log the mismatch.

ProjectMER block IDs are opaque and may all be negative. Only a missing ID uses
`StationAssetBlock.NoId`. Resolve existing-parent transforms and use
`ApproximateRootPosition` when a known parent was not spawned. Preserve exported markers.
