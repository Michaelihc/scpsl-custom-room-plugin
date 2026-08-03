# Full Aim Range automatic live verification transcript

## Scope

- Product: `WarmupScpSelector`
- Harness: `WarmupRangeVerifier` (dev-only, excluded from the product solution)
- Required execution environment: isolated local SCP:SL LabAPI port, no human client
- Stable log prefix: `[AimRangeLiveTest]`
- Production code changes: none
- Product lifecycle route used for teardown: reflected call to the existing lane `StopForRoundStart()`
- Post-test state: reflected call to existing lane `Start(room)` when
  `restart_range_after_verification: true`

## API/source evidence used

The harness implementation was checked against the local official/decompiled references before use:

- Native dummy creation: `.references/LabAPI/LabApi/Features/Wrappers/Players/Player.cs` and native
  `NetworkManagerUtils.Dummies.DummyUtils.SpawnDummy` usage in `.tests/Behavioral/Harness/DummyRegistry.cs`.
- Exact wrapper inventories: `.references/LabAPI/LabApi/Features/Wrappers/Pickups/Pickup.cs`
  (`Pickup.List`, `TryGet`, `Rigidbody`, `Serial`, `Type`) and
  `.references/LabAPI/LabApi/Features/Wrappers/AdminToys/AdminToy.cs` plus `ShootingTargetToy.cs` and
  `WaypointToy.cs`.
- Native lobby threshold: `.references/Decompiled/DedicatedServer/Assembly-CSharp/CharacterClassManager.cs`, where
  `ReadyClient`, `Host`, and `Dummy` are counted and the countdown advances only above one counted connection.
- Product geometry and ownership: `src/WarmupScpSelector/Warmup/AimRangeLayout.cs`,
  `Warmup/AimRangeWorld.cs`, `Activities/AimRange/WeaponShelfController.cs`,
  `Activities/AimRange/AimRangeActivityLane.cs`, and `Activities/AimRange/RangeBotController.cs`.

## Build transcript

### Commands

```powershell
dotnet build -c Release .\tests\WarmupRangeVerifier\WarmupRangeVerifier.csproj
```

### Expected

- Product project builds as the verifier's private-false project reference.
- Verifier builds to `tests/WarmupRangeVerifier/bin/Release/net48/WarmupRangeVerifier.dll`.
- Zero warnings and zero errors.
- `WarmupRangeVerifier` remains absent from `WarmupScpSelector.sln`.

### Observed (2026-07-24)

```text
WarmupScpSelector -> src\WarmupScpSelector\bin\Release\net48\WarmupScpSelector.dll
WarmupRangeVerifier -> tests\WarmupRangeVerifier\bin\Release\net48\WarmupRangeVerifier.dll
Build succeeded.
0 Warning(s)
0 Error(s)
```

The full solution/product regression result is recorded separately below after the final validation pass.

## Local port setup

### Preconditions

1. Choose and recheck an unused isolated port; `7999` is the documented example, not a reservation.
2. The port contains only the LabAPI loader/runtime, `HintServiceMeow.dll`, `WarmupScpSelector.dll`, and
   `WarmupRangeVerifier.dll` needed for this run.
3. Remove `WarmupDummyTester.dll` and any plugin that force-reassigns/repositions dummies.
4. No human client connects while the automatic run is active.
5. Product config keeps all six default weapon presets and sets:

```yaml
is_enabled: true
activities_enabled: true
activities:
  aim:
    enabled: true
    bot_count: 2
```

`bot_count: 2` is intentional. With zero canonical humans, the product must still create zero range bots.

### Deploy commands

```powershell
$port = 7999
$pluginDir = "$env:APPDATA\SCP Secret Laboratory\LabAPI\plugins\$port"
Copy-Item .\src\WarmupScpSelector\bin\Release\net48\WarmupScpSelector.dll $pluginDir -Force
Copy-Item .\tests\WarmupRangeVerifier\bin\Release\net48\WarmupRangeVerifier.dll $pluginDir -Force
Get-FileHash .\src\WarmupScpSelector\bin\Release\net48\WarmupScpSelector.dll
Get-FileHash "$pluginDir\WarmupScpSelector.dll"
Get-FileHash .\tests\WarmupRangeVerifier\bin\Release\net48\WarmupRangeVerifier.dll
Get-FileHash "$pluginDir\WarmupRangeVerifier.dll"
powershell -NoProfile -ExecutionPolicy Bypass -File ..\.references\run-port.ps1 -Port $port
```

### Log extraction

```powershell
Select-String -Path "..\la$port.log" -Pattern '\[AimRangeLiveTest\]'
```

### Observed (final isolated run, 2026-07-24)

- Port: `7998`, headless local automated loop only.
- Game: SCP:SL `14.2.7`; HintServiceMeow `5.5.0`; product and verifier `1.0.0`.
- Product SHA-256, source and deployed: `4d59bb49019c18a3df9be34ea8bf1fafeab8fb7c526c12264dc3aa97a7165ed3`.
- Verifier SHA-256, source and deployed: `26c61fe8d4b399709a775db3ffedc354fc35cf2f8187349835523fa081831735`.
- Automatic checks ran from `22:39:43.689 -04:00` through `22:39:50.048 -04:00`.
- No `[AimRangeLiveTest] FAIL` or server/plugin error occurred. The expected dev-only verifier warning and the expected
  zero-human bot-policy warning were the only relevant warnings.
- Final line: `[AimRangeLiveTest] PASS summary checks=102/102 cleanup=verified manualQaRange=restarted`.

## Automatic check transcript template

Each section below names the stable check IDs and the exact expected/observed evidence produced by the harness.

### 1. Startup and zero-human bot policy

Expected:

- Product, activity suite, and Aim are enabled.
- Selector controller active.
- Selector room spawned; Aim doorway prepared; fail-closed gate removed only after startup.
- `AimRangeWorld.IsSpawned == true`, non-null `AimRangeLayout`, six shelf anchors.
- Six shelf states are `Available`.
- Target deck is non-empty and contains static + moving cards.
- MEC scheduler handle is running.
- Exactly two static MER rack instances exist with visible primitive toys.
- Canonical human count is zero.
- Requested product bot count may be two, but owned registry count and `AIM-RANGE-*` product dummy count are zero.

Expected log IDs:

```text
config
controller-active
door-open
world-layout
shelf-start
target-deck
scheduler-start
rack-visuals
zero-human-bot-policy
```

Observed: all startup checks passed. The live room contained six available shelf slots, a 24-card target deck
(18 static, 6 moving), two rack instances with 46 visible toys, a running scheduler, and zero product bots with zero
canonical humans.

### 2. Dense collision/raycast probe

Downward grid expected:

- More than 60 authored floor rays.
- Ray origin is 3.00 m above the authored floor.
- Nearest non-trigger hit distance is `3.00 +/- 0.16 m`.
- Hit Y is authored gallery/range floor Y `+/- 0.12 m`.
- Regions: gallery-to-doorway seam, entrance, left/right shelf interaction strips, before/after shooting line,
  static bay, moving bay, bot bay, and backstop walkway.

Horizontal/side expected distances:

| Check | Expected nearest distance |
|---|---:|
| Open doorway to backstop | 20.50 m |
| Left/right doorway stub | 0.65 m |
| Door lintel | 0.65 m |
| Left/right side wall | 5.85 m |
| Left/right divider | 1.875 m |
| Left/right shelf collider | 5.105 m |
| Shooting line | 0.875 m |
| Backstop | 1.70 m |

Failures include ray origin, direction, nearest distance, hit point, and collider name.

Expected log IDs:

```text
raycast-down-grid
raycast-horizontal-suite
```

Observed: 62/62 downward rays passed across every authored region, and 12/12 horizontal rays passed for doorway
stubs, lintel/opening, side walls, dividers, shelves, shooting line, and backstop.

### 3. Exactly one harness dummy; Tutorial floor settling/walking; no round start

Expected:

- Harness-owned dummy precondition count zero.
- Spawn exactly one native hub named `AIM-RANGE-LIVE-PROBE`.
- Wait one Unity frame before role assignment; poll until role is `Tutorial`.
- Temporarily hold the native lobby lock while the dummy exists; restore the prior lock afterward.
- Settle for six stable frames at the real entrance and five additional authored floor positions: shelf interaction,
  shooting approach, moving bay, bot bay, and backstop walkway.
- Nudge/walk the dummy one meter across the entrance in eight frame-stepped increments.
- At every sample: wrapper/hub alive, sane Y, authored floor beneath the body, no fall below/through floor, no
  penetration indicated by the body-to-floor ray.
- `RoundStarted` event and `Round.IsRoundStarted` remain false.
- Destroy only the captured harness hub and poll to zero harness-owned dummies.

Expected log IDs:

```text
lobby-lock
dummy-precondition
dummy-role
dummy-exactly-one
dummy-settle-entrance
dummy-walk
dummy-settle-authored-1 ... dummy-settle-authored-5
dummy-no-round-start
dummy-floor-suite
dummy-cleanup
```

Observed: the single Tutorial probe settled at the entrance plus five authored points, walked one meter in eight
steps, stayed on solid floor with sane Y and no penetration, never started the round, and was destroyed back to zero
harness dummies.

### 4. Six shelf pickups

Expected per slot `0..5`:

- `Available`, nonzero serial, live `Pickup` wrapper.
- Configured conventional type in order: COM-15, COM-18, FSP-9, Crossvec, AK, E-11-SR (unless the local config
  deliberately changes the six presets, in which case the verifier compares against that live config).
- Standard rigidbody exists and `isKinematic == true`.
- Position is within `0.12 m` of the marker-resolved/authored shelf anchor.
- Serial is distinct across all six slots.
- Serial is absent from `SelectorRoom.CoinRoles`.

Expected log IDs include `shelf-<n>-available`, `-pickup`, `-type`, `-kinematic`, `-anchor`, `-serial`,
`-coin-isolation`, and aggregate `shelf-suite`.

Observed: all six configured firearm types passed at their exact anchors with kinematic rigidbodies, six distinct
serials, and zero contamination of the seven SCP coin serials.

### 5. Static/moving targets and carrier relation

Expected:

- Force each of the three deterministic static cards through the existing private `ShowNextTarget(now)` route and
  compare the native `ShootingTargetToy` position to each authored static anchor (`<= 0.12 m`).
- Force the moving card.
- Moving MER carrier exists and contains visible primitive toys.
- Drive the product's existing absolute-time target tick at four offsets (0.15, 0.55, 1.05, and 1.55 seconds).
- Every sample remains in the authored moving bay bounds.
- Native target position matches `AimRangeDeterminism.EvaluatePath(...)` using absolute elapsed time (`<= 0.22 m`).
- Carrier `marker_target_mount` matches the native target (`<= 0.08 m`).
- Sampled X span is at least 0.60 m, proving nontrivial bounded movement rather than four identical positions.

Expected log IDs:

```text
static-target-0
static-target-1
static-target-2
static-target-suite
moving-carrier-spawn
moving-carrier-visuals
moving-target-bounds
moving-target-determinism
moving-carrier-mount
moving-target-motion
moving-target-suite
```

Observed: all three static anchors matched exactly. The moving target and carrier mount matched the deterministic
path exactly at all four offsets (zero measured drift), stayed inside the authored bay, and traversed `1.75 m` in X.

### 6. Deterministic product cleanup and manual-QA restart

Route:

- Capture exact range-owned shelf serials, target Unity IDs, world/MER/carrier toy Unity IDs, and owned bot hub IDs.
- Invoke the existing lane `StopForRoundStart()` through reflection. This is selected instead of forcing a native
  round start because it tests the same lane teardown without consuming/corrupting the local warmup round.
- Poll until exact ownership disappears; do not destroy global ambient entities.

Expected after stop:

- Lane stopped; range world despawned; layout null.
- Every shelf slot `Disabled` with serial zero; all captured shelf serials absent.
- Captured target IDs absent.
- Captured rack/carrier/world toy IDs absent.
- Captured bot hub IDs absent and product bot registry empty.
- Range session count zero; HSM provider has zero `.aim.` cache entries and zero `|aim` flash timers.
- Selector room's fail-closed range gate restored.
- If configured, invoke existing `Start(room)`, poll until startup is ready again, and leave the range open for
  manual QA.

Expected log IDs:

```text
cleanup-lane-world
cleanup-shelves
cleanup-pickups
cleanup-targets
cleanup-world-toys
cleanup-bots
cleanup-hints
cleanup-door
cleanup-suite
manual-qa-restart
final-round-state
```

Observed: cleanup removed all six owned pickups, the current target, 108 captured range world/rack/carrier toys,
all range hint/cache entries, and the open doorway state while preserving 298 ambient pickups and 337 ambient toys.
The range then restarted successfully with six shelves, 24 cards, and the scheduler running.

## Final observed summary

```text
[AimRangeLiveTest] PASS summary checks=102/102 cleanup=verified manualQaRange=restarted
```

Any `[AimRangeLiveTest] FAIL` line or missing final PASS remains a failed live run.

## Errors encountered during implementation

1. Initial verifier build lacked `CommandSystem.Core.dll`, required transitively by LabAPI player role APIs.
   Resolution: add a non-copying managed reference in the dev-only verifier project.
2. `MerWorldTransform` is intentionally internal to the product. Resolution: keep production unchanged and read the
   moving carrier's out marker value/`Position` property through reflection.
3. Reflection initially treated a null private field as a missing field, which is invalid for the intentionally-null
   open door gate/current target. Resolution: add `NullableField` for read-only nullable inspection.
4. No production snapshot API was added; reflection remains isolated to the never-shipped verifier.
5. The first live collision run used a stale verifier DLL and checked the open doorway before waking the zero-client
   physics scene. Resolution: hash-verify every deployment and spawn/settle the probe dummy before geometry rays.
6. Initial horizontal rays could hit the verifier's own dummy before the authored backstop. Resolution: exclude only
   the captured harness hub from geometry ray results; no ambient or product entity is globally ignored.
7. Initial moving-target verification mixed the lane start clock with the scheduler's elapsed clock, then sampled a
   frozen zero-client idle clock. Resolution: drive the existing private absolute-time target tick at four explicit
   offsets and compare the live native target plus MER mount at each point. Product movement behavior was not changed.

## 2026-07-25 correction-run addendum

After the visible-client correction pass, the isolated verifier was expanded to cover six authored near-white point
lights, all six firearms starting empty then becoming full/ready through the product preload helper, the required
90-degree target yaw plus three broad-face collider rays per static target, and the existing ownership/cleanup suite.
The final run completed with:

```text
[AimRangeLiveTest] PASS summary checks=132/132 cleanup=verified manualQaRange=restarted
```

The dev verifier was removed from the local plugin directory after this run.

## Unverified boundaries

The automatic run intentionally does not claim coverage for:

- A real client rendering the room, racks, targets, HSM hints, or first-person weapon animations.
- Human pickup input and inventory event sequencing from an authenticated client.
- Human firing, hit registration, damage isolation, lethal reset presentation, ragdoll suppression, or ammo UI.
- Bot provocation, first-attacker lease, native camera aim, line-of-sight behavior, `Shoot->Click`, ammo consumption,
  retaliation damage, death, cleanup, respawn, or `BOT BACK` UI.
- Behavior with two or more canonical humans (the threshold that permits range bots).
- Interactions/conflicts with other dummy managers, pickup mutators, admin-toy cleanup plugins, or non-HSM UI systems.
- Production Linux deployment. This harness must never be copied there.

These boundaries require a separate visible local-server/manual QA session with real clients and, for bot retaliation,
at least two canonical humans plus a spare public slot.
