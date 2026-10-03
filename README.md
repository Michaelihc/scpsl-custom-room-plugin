# WarmupScpSelector

[中文](#中文说明) | [English](#english)

A LabAPI plugin for SCP: Secret Laboratory. During waiting-for-players it builds a floating **space
station** whose gallery holds a model of every offered SCP, each with a big coin. Players walk up and
grab a coin to pick the SCP they want to play. When the round starts, the plugin lets the game assign
roles normally, then swaps the selected SCP slots over to the players who picked them. During the early
round it can also refill a healthy SCP slot whose player disconnects.

Two optional activity compartments open off the station's hub: an **Aim Bay** and a **parkour shaft**.

The latest authored room is `warmup_station_stars` (installation: [star station](generated/schematics/warmup_station_stars/README.md)). Its sunset emblem also replaces the generated fallback gallery's old logo; fallback welcome text uses 星河梦影 and QQ 1109846287.

最新图纸为 `warmup_station_stars`（[安装说明](generated/schematics/warmup_station_stars/README.md)）。生成式备用展厅同样使用落日星球图案，欢迎文字为星河梦影，QQ 群为 1109846287。

Aim Bay bots have native spawn protection removed on every spawn/respawn, so they can take damage immediately. This applies only to owned range bots. Native evidence: `.references/Decompiled/DedicatedServer/Assembly-CSharp/CustomPlayerEffects/SpawnProtected.cs`, `OnRoleChanged` / `TryGiveProtection` (local dedicated-server decompile).

瞄准区机器人每次生成／重生时都会移除原生出生保护，可立即受到伤害；仅影响本插件拥有的靶场机器人。原生行为依据见上方源码路径。

> Renamed from the old "scpsl-custom-room-plugin" / `ScpslCustomRoomPlugin`. It never built custom
> rooms in the SCP-002 sense — it is a warmup SCP draft — so the name was changed to match what it does.

## English

### The station

Players spawn in the SCP gallery, looking down the aisle at the exhibits. Everything else is optional
and reached on foot.

The station defaults to 30 metres above Surface to clear the native collision box in front of SCP-939.
Existing installations should set `surface_clearance: 30`; this moves the entire station together.

```
                                      +Z
         z=71    +---------+          ^
                 | PARKOUR |          |   9 m wide, 13.5 m tall shaft
                 |  SHAFT  |
       z=17.5    +--+   +--+
 +---------------+  |   |  +--------------------------------+
 |  OBSERVATION  |    HUB    |          AIM BAY             |   z = +/-9.5
 |     DECK      |  |   |  |                                |
 +---------------+  |   |  +--------------------------------+
 x=-22        x=-11 +--+---+ x=11                       x=36
                 |CONNECTOR|
       z=-25.5   +--+   +--+
                 |         |
                 | GALLERY |   SCP draft, 7.5 m ceiling
         z=-46   +---------+
```

The plan comes from a room authored in ProjectMER; the numbers were regularised into named constants so
the station is extensible in code. Adding a compartment means adding a zone and the hatch that connects
it — the shell builder walls, seals, and lights it automatically.

Each activity compartment is **fail-closed**: its hatch is a solid bulkhead panel during setup, removed
only once that activity's world, props, and scheduler have all started successfully.

### What it does

- While the server is **waiting for players**, everyone is moved into the station as `Tutorial` and
  spawned in the SCP gallery, facing a rank of SCP models (049, 079, 096, 106, 173, 939, 3114), each on
  a low stand with a big coin in front of it. The gallery's centre aisle is left clear so the server logo
  reads straight down it.
- **Grabbing a coin** selects that SCP (the pickup is cancelled, so the coin stays put). A hint shows
  the lobby countdown and your current pick, with a small badge for how many players picked the same SCP
  (e.g. `SCP-096　·　5 picks`) and a tiny `·N` tally on each chip in the offered-role row. You can change
  your pick any time before the round starts. (The panel is only re-sent when its text changes, so it does
  not spam the network.) The countdown is the largest element after the title. Inside the Aim Range or the
  parkour shaft the full panel gives way to that activity's HUD, but the countdown stays on screen as a smaller,
  quieter strip so nobody loses track of round start.
- A **recent-updates board** hangs on the gallery's north wall beside the exit hatch, behind the spawn. It lists
  the newest entries of the Star River wiki's update log (date, title and a one- or two-line summary) and the
  address of the full log. The text is not part of the plugin: every station build shows the cached copy, then
  fetches the wiki's `updates.json` off the main thread and swaps the text when it arrives. Publishing the wiki
  is enough to change the board at the next lobby. Until a feed has been read once, the board stays hidden.
- The plugin never forces the native lobby countdown. The only exception is an ownership-safe temporary lobby lock
  while one human is using configured native bots, preventing counted dummies from falsely starting the round; it is
  released synchronously when another human joins, the last human leaves, or Aim stops.
- The default-off **Aim Range** occupies the east Aim Bay, firing 21.5 m down the arm. Six persistent
  firearms sit visibly on the shooting counter, with two native attachment workstations placed
  symmetrically against the side walls. Three lanes run the length of the bay: cover-backed live bots,
  three parallel persistent sliding native targets at different distances and speeds, and Aim-Lab spheres
  that send a hitmarker/score on a valid hit and immediately teleport the same always-hittable toy to the
  next authored point. Grabbing a counter gun grants a separate owned inventory copy, so the displayed gun
  remains visible and immediately reusable. Both normal targets and spheres send the shooter a native
  hitmarker when a valid range hit is credited; sliding targets use client interpolation between 15 Hz
  network keyframes instead of snapping between scheduler updates.
- Up to two explicitly enabled native RA bots continuously strafe through cover using small native FPC motor steps
  with no waypoint binding. Both slots use Crossvec. Bots keep moving and
  jump on bounded native-input cooldowns; a close attacker makes them jump more often. During retaliation they always
  hold native ADS and use tighter combat steps for accurate return fire. A genuine hit from a participant's owned range gun locks that first attacker after a 0.5–0.6 second reaction delay for
  12 seconds: the bot tracks an upper-chest point inside a non-head hitbox while strafing and holds native automatic fire until the lease or line of sight ends. At
  low ammo it releases fire/ADS, moves behind slot-owned 2.2 m cover, reloads there, then resumes combat. Bots die
  normally and respawn with a new owned identity and Crossvec.
- Bot shots deal real damage. A lethal hit on a range participant is cancelled before vanilla death, synchronously
  reinitializes the same player as `Tutorial`, and returns them to the range entrance without a spectator frame.
  Leaving the bay or starting the round destroys every range-owned gun, pickup, target, carrier, bot, and hint.
- The HUD switches at the bay threshold. In the hub and gallery you see only the SCP draft panel; inside
  the bay the draft panel is hidden and only the bilingual Aim flash, hero, and footer are shown. Gun
  ownership, damage routing, and bot provocation still reach back through the hatch, so a shooter standing
  in the doorway behaves normally. The Aim HUD renders on a narrow left lane (`Activities.Aim.HudX`,
  default -1077) between the native inventory list and the inventory wheel.
- The default-off **Pulse Line parkour** owns the north shaft — 9 m wide, 53.5 m long, 13.5 m tall. Step
  on START for 0.6 seconds, run the 3-second countdown, then climb an ordered chain of lit landings to the
  finish pad at the far end. Missing a landing returns you to the last one you cleared, caught early in the
  fall so it never costs health, while the authoritative timer keeps running. The reusable RESET coin
  restarts in place, and the compact bilingual HSM card shows timer, sector spine, progress, and the best
  completed time for the current warmup.
- **The parkour route is generated from the game's own physics, not hand-placed.** At build time the
  plugin reads the live Tutorial role's jump speed, walk speed, and sprint speed, and lays out every hop
  so it uses a target fraction of the distance actually reachable at that jump — ramping from an easy
  opener to a closing stretch that demands a committed sprint. If the result would be impossible, trivial,
  out of the shaft, or short on ceiling clearance, the lane refuses to open rather than shipping a broken
  course. This matters more than it sounds: SCP:SL's real jump apex is about **0.61 m**, far lower than a
  hand-authored course would assume.

- When the round starts, the plugin hands players back so the game assigns vanilla roles, then **swaps**
  the selected SCP slots to the pickers:
  - If vanilla spawned an SCP that someone picked, one picker from that pool takes the slot, and the
    displaced vanilla holder inherits the picker's original (human) role.
  - For the regular SCP slots, a pick is skipped if vanilla did not spawn that SCP this round.
    Those slots only rearrange vanilla assignments.
  - **SCP-3114 is the one exception.** Vanilla only spawns SCP-3114 on holidays
    (see `../.references/Decompiled/DedicatedServer/Assembly-CSharp/PlayerRoles/PlayableScps/Scp3114/Scp3114Role.cs`,
    `EnableSpawning`).
    When at least `Scp3114MinPlayers` players (default 26, i.e. more than 25) are counted for round-start
    role assignment and someone picked SCP-3114, one random picker becomes SCP-3114 **instead of the human
    role vanilla was about to give them**: the round has one more SCP and one fewer human, and nobody else's
    role changes. Only a player who picked SCP-3114 can receive it. The picker's status panel shows the
    player threshold while SCP-3114 is selected. If vanilla did spawn SCP-3114 this round, the normal swap
    above owns it. `Scp3114DraftEnabled: false` turns this off.
  - The SCP-3114 assignment happens inside the native role-assignment pass. The shared round role draft
    runs afterward, claims the winner as `warmup.scp`, and excludes them from the Facility Manager,
    GOC spy and SCP-999 candidate pools.
- The station and all models **despawn when the round starts**.
- If a main SCP disconnects during the configured early-round window while still above the configured
  health threshold, that exact role becomes available through `.volunteer <number>` (alias `.v`).
  **Both spectators and living non-SCP players may enter by default.** The first volunteer starts a
  short lottery; only connected, still-eligible UserIds are considered when it resolves.
- Replacement is vacancy-safe: if another plugin or an administrator has already restored that SCP role,
  the lottery cancels instead of creating a duplicate. Round end, restart, disable, and disconnect remove
  all pending callbacks/candidates. `MaxReplacementsPerRound` also counts pending reservations.
- An SCP may optionally use `.human` (alias `.no`) to offer a **role swap** before 30 seconds into the round.
  Acceptance must also occur before 30 seconds; this fixed cutoff is separate from disconnect replacement timing.
  The first living human without a round role claim to answer with `.volunteer <number>` trades roles with
  it: the human becomes the SCP where it stands, keeping the SCP's health, Hume Shield and ScpTiers
  progression; the SCP takes the human's role, position, health, items and ammo. An unanswered offer
  lapses and the SCP stays an SCP.
- Players holding a round role claim (SCP-999, the Facility Manager, the GOC spy, reinforcements, ...)
  cannot volunteer or accept a swap.

### Round role draft

The plugin hosts a shared **round role draft** for special roles other plugins own
(`src/WarmupScpSelector/Roles/RoundRoles.cs`). It runs whether or not the station is enabled.

- Plugins register a `RoleSlot` (id, priority, eligibility, apply, optional count, activity and weight)
  with `RoundRoles.Register`. Registration is static, so it may happen before this plugin is enabled.
- Once per round, one frame after the native role assignment and the SCP coin draft (or right after the
  compatibility swap when that path runs), every SCP is claimed as `warmup.scp` and the slots are filled in
  ascending priority from living players nobody has claimed. Each chosen player is claimed with the slot's
  id and the slot's `Apply` runs as the claim owner.
- A claim ends on disconnect and on any role change its owner did not make inside `RoundRoles.RunAsOwner`
  (an SCP claim survives SCP-to-SCP changes). Other role changers ask `RoundRoles.IsClaimed` first.
- Current slots: `rs.facility_manager` (100) and `rs.goc_spy` (200) from ReinforcementsSystem, `scp999`
  (300) from SCP999. Consumers compile against this project and keep their own round-start pick only when
  this plugin is not loaded.

### Admin force selection

Set `admin_force_selection_enabled: true` in the LabAPI configuration (default `false`).
Remote Admin and the server console provide:

```text
warmupforce <playerId> <scp>    # e.g. warmupforce 4 173 or warmupforce 4 SCP-3114
warmupforce list
warmupforce clear <playerId>
warmupforce clear all
```

Requires the native `ForceclassWithoutRestrictions` permission. Queue or cancel reservations during
waiting-for-players. Each player has one reservation; another command replaces it. The chosen SCP is
guaranteed against the coin lottery, including roles vanilla did not select and SCP-3114 below its
normal threshold. Multiple players may be forced into the same SCP. Forced recipients are claimed
as SCPs before other plugins' round role draft runs.

The draft reuses an existing SCP slot where possible, changing its role if needed. If all slots belong
to other forced recipients, additional reservations turn humans into extra SCPs. Ordinary coin picks
retain their normal slot-preserving behavior. Disconnect, round start, restart and unload clear
reservations. A disconnected player's reservation does not transfer to their next connection.
Other plugins can still veto role changes; failures are logged and the compatibility path rolls back
an incomplete swap.

Native reference (local dedicated-server decompile):
`.references/Decompiled/DedicatedServer/Assembly-CSharp/CommandSystem/Commands/RemoteAdmin/ForceRoleCommand.cs`,
`HasPerms`; round assignment uses `PlayerRoles/RoleAssign/RoleAssigner.cs`, `OnRoundStarted`, and
`PlayerRoles/RoleAssign/HumanSpawner.cs`, `SpawnHumans`, under that same source root.

### Replacement commands

- `.volunteer` / `.v` — list SCP roles currently awaiting replacement.
- `.volunteer 079` / `.v 079` — enter that role's replacement lottery. `SCP-079` and `79` are also accepted.
- `.human` / `.no` — if enabled, offer an eligible healthy SCP role for a swap before 30 seconds into the round.

### Build

References SCP:SL managed assemblies from a dedicated-server install. If yours is elsewhere, pass
`ServerManagedPath`:

```powershell
dotnet build -c Release
# or:
dotnet build -c Release -p:ServerManagedPath="C:\path\to\SCPSL_Data\Managed"
# in a standalone checkout, also pass -p:HsmAdapterProject="C:\path\to\HsmAdapter\HsmAdapter.csproj"
```

Output: `src/WarmupScpSelector/bin/Release/net48/WarmupScpSelector.dll`. The 7 SCP models are embedded
in the DLL. Install `HsmAdapter.dll` as a LabAPI plugin and `HintServiceMeow.dll` for the HUD.

### Install

Copy `WarmupScpSelector.dll` and `HsmAdapter.dll` into the LabAPI plugins folder for your port, e.g.:

```text
%AppData%\SCP Secret Laboratory\LabAPI\plugins\<port>\
```

### Configuration

LabAPI generates the config at:

```text
%AppData%\SCP Secret Laboratory\LabAPI\configs\<port>\WarmupScpSelector\config.yml
```

Key options:

- `Language` — `"en"` for English, `"cn"` for Simplified Chinese player-facing text.
- `ScpReplacement.IsEnabled` — enables the post-start disconnect replacement system (default `true`).
- `ScpReplacement.AllowAliveVolunteers` — allows living non-SCP players in addition to spectators (default `true`).
- `ScpReplacement.DepartureCutoffSeconds`, `VolunteerCutoffSeconds`, `RequiredHealthPercentage`, and
  `LotterySeconds` — departure/entry windows, departure health gate, and lottery duration.
- `ScpReplacement.AllowHumanCommand` — enables the `.human` swap offer.
- `ScpReplacement.MaxReplacementsPerRound`, `IgnoredRoles`, and `CommandCooldownSeconds` — capacity,
  exclusions, and shared `.volunteer`/`.human` rate limiting.
- `RoomOrigin` — world position of the floating selector room (high Y keeps it clear of the live map).
- `PedestalSpacing`, `ModelScale`, `SelectorCoinScale` — layout/sizing.
- `SelectorItem` — the pickup used as the selection coin (default `Coin`).
- `RoleSwapDelaySeconds` — delay after round start before swapping (lets vanilla roles settle).
- `Scp3114DraftEnabled` and `Scp3114MinPlayers` — give one random SCP-3114 picker SCP-3114 in place of their
  vanilla human role once the lobby has at least this many players counted for role assignment (default `true`, `26`).
- `MusicEnabled`, `MusicFilePath` — optional lobby music. `MusicFilePath` must point to preconverted
  `48000 Hz` mono raw float32 little-endian PCM (`.f32le`). Relative paths resolve under the plugin
  config folder above. Convert once offline, then the plugin loads the samples directly:

```powershell
ffmpeg -i lobby.mp3 -ac 1 -ar 48000 -f f32le lobby.f32le
```

- `MusicFadeInSeconds` — per-player fade-in when someone enters the selector.
- `MusicFadeOutBeforeStartSeconds`, `MusicFadeOutSeconds` — starts fading before the native countdown
  reaches round start, with a final fallback fade during the role-assignment handoff.
- `ScpOptions` — the SCPs offered, each with a role, label, and embedded model name.
- `ActivitiesEnabled` and `Activities.Aim.Enabled` — both must be `true` to open the Aim Range.
- `Activities.Parkour.Enabled` — additionally set this to `true` to open Pulse Line in the north shaft.
  It is default-off and **independent of the Aim Bay**: the shaft is its own compartment.
- `Activities.Parkour.SchedulerRateHz`, `StartHoldSeconds`, `CountdownSeconds`, and `RecoveryGraceSeconds` — parkour
  tick and run/recovery timing. `HudX`, `FlashY`, `HeroY`, and `FooterY` position its three stable HSM zones.
- `Activities.Aim.WeaponPresets` — the six persistent shooting-counter presets, attachment codes, and tracked reserve ammo. Two native attachment workstations sit symmetrically at the side walls.
- `Activities.Aim.BotCount` — owned native bot slots (`0..2`). It defaults to `0` and must be explicitly enabled.
  A solo human can use configured bots: the range temporarily owns the native lobby lock so dummy connections cannot
  start the round, then releases it when a second human joins or Aim stops. Bots always leave one public slot free.
- `Activities.Aim.BotWeaponPresets`, `BotRespawnSeconds`, `BotAggroLeaseSeconds`, and related bot settings —
  both bots use Crossvec only, react in 0.5–0.6 seconds, and keep the first-attacker lock for 12 seconds.
- `Activities.Aim.SphereActiveCount` and `SphereDiameter` — lane-3 Aim-Lab population and size. Every credited hit
  immediately teleports the same sphere to the next position in a fixed, irregular 50-point 3D deck.
- `Activities.Aim.FlashY`, `HeroY`, and `FooterY` — the three non-overlapping Aim HSM bands.
  `Activities.Aim.CollapsedStatusY` and `Activities.Parkour.CollapsedStatusY` (default `900`) place the small
  round-countdown strip that replaces the full SCP panel while you are inside that activity, so the lobby countdown
  is always visible; it is deliberately smaller and quieter than the full panel's countdown.
- `Activities.Aim.HudX` — HSM center-X for the in-bay Aim HUD lane (default `-1077`). It keeps the flash,
  hero, footer, and compact countdown in the narrow left corridor between the native inventory list and wheel so the
  HUD never overlaps them while TAB is held. Outside the bay only the original centered SCP panel is shown.
- `PedestalSpacing` — spacing of the gallery's back rank of stands (default `3.7`). Wider spacing fits fewer
  stands in the back rank and pushes the remainder onto the side walls; the gallery holds ten in total.
  With an **authored station** the stands come from the schematic, so this setting no longer decides where
  they are: each coin is anchored to the exhibit whose label names that SCP, and only an exhibit the plugin
  cannot match by label falls back to this grid. Keep the `SCP-xxx` labels in the schematic and the coins
  follow the models wherever you move them.
- `SurfaceClearance` and `RoomOrigin` — where the station floats. The origin is the hub's deck centre.
- `NewsBoard.Enabled` and `NewsBoard.FeedUrl` — the recent-updates board and where its entries come from
  (default `https://scpslservers.com/sr/wiki/updates.json`; how entries are written is in the wiki's
  `AUTHORING.md`). A non-http value is read as a file path, relative to the plugin config folder, with the same
  format. The last good http copy is kept in `news-board-cache.json` in that folder and shown whenever the site
  cannot be reached; a failed fetch logs one warning per lobby.
- `NewsBoard.FetchTimeoutSeconds` and `NewsBoard.MaxEntries` — fetch deadline (default `8`) and the most
  entries shown (default `4`). Long summaries wrap to two lines, and the board drops older entries rather than
  overflow.
- `NewsBoard.Position` and `NewsBoard.FacingYaw` — the board's centre on its wall in station-local metres and the
  direction it faces. The default sits on the gallery's north wall east of the hatch, facing into the gallery
  (`180`). Move it here if a re-authored station puts something on that wall.

### Handing the room to someone else

The station can be exported as a **ProjectMER schematic** so an artist or mapper can open it in the
in-game map editor, rearrange it, and hand the file back.

```text
warmupexport                 # RA / server console, during waiting-for-players
warmupexport my_layout       # or under a chosen name
```

Or set `ExportSchematicName` in the config to write a fresh snapshot every time the station is built.
The file lands in ProjectMER's own `Schematics/<name>/<name>.json` when that plugin is installed on the
port (so it is immediately loadable with `mp spawn <name>`), otherwise in this plugin's config folder.
A checked-in copy of the current room lives at `generated/schematics/warmup_station/`.

Avoid `-` in the name: ProjectMER's schematic lister filters those out.

What the export contains, and why:

- **Everything actually spawned** — deck, bulkheads, overhead, hatch frames, the SCP models, the brand
  logo, the parkour landings, the range furniture, lights, and text. It reads the live world rather than
  re-describing the geometry from the layout code, because a second description would drift from the
  builders and the drift would only surface when someone spawned the export and found it different.
  The recent-updates board is the exception: it is live content placed by config, so it is left out
  rather than baked in as stale text.
- **The logo's parenting.** Its ~200 quads get their shear from a non-uniformly scaled invisible parent
  times a rotated child. Those are exported with `ParentId` and local transforms; flattened to world
  space the emblem comes back subtly wrong rather than obviously broken.
- **`marker_*` anchors** for everything that is not geometry: the spawn point, each compartment centre,
  each hatch, the firing line and gun positions, and every parkour landing. Coins and native
  workstations are geometry-less too and appear as markers.
- Coordinates are **station-local**, so the file does not bake in the height the station happened to
  float at.

Merging an edited file back is not automatic yet — see the note in `AGENTS.md`. Move the `marker_*`
blocks deliberately when you move the things they describe; they are the contract the merge will read.

### Models

Each SCP is a stylized primitive model authored offline as `generated/models/scp-*.mer.json` and
embedded in the DLL. The back-wall server logo is embedded the same way; its ProjectMER empty-parent
hierarchy is retained so the sheared quads render faithfully without a ProjectMER dependency. The Aim
Legacy Aim weapon-rack and moving-target-carrier MER assets remain embedded for compatibility, but the widened
three-lane runtime spawns neither. Runtime spawning preserves one-level parent transforms and never spawns marker
primitives. All active range collision remains explicit AdminToy box geometry. The
authoring/verification pipeline lives in `tools/` and `tests/models/`:

```powershell
pip install -r requirements.txt
python tools/build_scp_173_asset.py                              # author -> generated/models/scp-173.mer.json
python tools/render_model_preview.py --input generated/models/scp-173.mer.json `
  --output generated/previews/scp-173.png --sheet --view front --view threequarter --view side --view top
python tests/models/test_scp_173_model.py                        # geometry contract check
```

### Tests

- **Headless logic** (`dotnet build tests/WarmupScpSelector.Tests`, then run `WarmupScpSelector.Tests.exe`):
  `86/86`. Covers SCP replacement policy/state/text, bot lifecycle and tactical contracts, lethal reset
  state, MER transforms, deterministic sliding motion, one-credit sphere relocation, bilingual Aim text,
  HSM cache behaviour, and — new with the station — compartment tiling, **wall-panel sealing** (every
  compartment face is sampled on a grid and must be either solid panel or inside a hatch), gallery stand
  placement, the Aim Bay's three lanes, and the parkour jump model and generated route.
- **Live dummy playtest** (`tests/WarmupPlaytestScenarios`, run through the shared `.tests/Playtest`
  harness): `ptest run warmup standard`.
  - `warmup-station` raycast-walks every compartment for deck and walls, checks each hatch is open and
    the hub's side walls beside it are not, and settles a dummy at the arrival point.
  - `warmup-pulse-line` sweeps the shaft with raycasts to discover the landings from the world (so a pad
    that spawned without a collider fails discovery rather than passing on paper), then drives three real
    dummies with native movement and the native jump action to bracket the difficulty.

  Transcript from `2026-08-28`, port 7930, standard fidelity:

  ```text
  [Station] shaft sweep: 806 surface hits -> 21 landings from y=0.5 to y=7.24
  [PulseLine] route: 21 landings, climb 6.74m over 47m
  [JumpCourse] runner jumpSpeed=4.9 walk=3.9 sprint=5.4 gravity=19.6 apex=0.61m
  [PulseLine] jump+sprint: cleared 20/20, completed
  [PulseLine] jump+walk:   cleared 19/20, stopped at landing 20 (fell 2.12m below the route)
  [PulseLine] no-jump:     cleared  0/20, stopped at landing 1
  RESULT scenario=warmup-pulse-line outcome=PASS duration=35.57s

  [Station] hub: deck confirmed across 22x35m      [Station] aim: deck confirmed across 25x19m
  [Station] observation: 11x19m   connector: 9x8m   gallery: 22x20.5m   shaft: 9x53.5m
  [Station] aim bay / observation deck / gallery access hatches are clear
  [Station] arrival settled at (0, 320.96, -31.5) (dropped -0.46m)
  RESULT scenario=warmup-station outcome=PASS duration=4.93s
  ```

  Those runs established the real Tutorial movement constants (`jump 4.9 / walk 3.9 / sprint 5.4`,
  apex **0.61 m** — far below what a hand-authored course would assume) and caught five defects worth
  recording, four of which only a live dummy could find:

  1. The route's step-rise cap was applied to the gap arithmetic but not to the landing placement.
  2. The harness's course verb teleported without the ordered-move mark, tripping the TeleportMonitor.
  3. Take-off timing was computed against the pad centre instead of the acceptance point, which made a
     jump onto the deep finish pad fire past the take-off edge and read as impossible.
  4. Difficulty measured against a walk/sprint blend left the closing hops within 2% of walk reach, and a
     walking dummy completed the whole course. Difficulty is now a fraction of a **sprinter's** reach.
  5. Deep closing pads hand a walker both a longer run-up and a bigger landing area; the closing pads are
     now deliberately shallow so the final hop genuinely needs the sprint.

- Model geometry contracts: run each `tests/models/test_*_model.py` script (`10/10` current model contracts).
- Shared HSM renderer: `node ../.tests/UI/smoke-test.js`; all 22 WarmupScp draft/Aim EN+CN fixtures parse with
  zero static issues against the shared native-background collision harness.
- Dev-only isolated live harness: `tests/WarmupRangeVerifier`. **Not yet updated for the station rework** —
  its 253 checks assert the old single-hall seam geometry and will fail until migrated to the new layout.
  It must never ship to production.

### Known limits / conflicts

- The selector/activity HUD requires HintServiceMeow and uses stable HSM hint IDs. Replacement notices use
  global broadcasts plus optional client-console copies.
- Do not install the standalone SCPReplacer beside this port; both would observe the same departure and announce
  competing lotteries. ScpSwap may coexist: the replacement lottery rechecks that the SCP role is still vacant
  immediately before promotion.
- Lobby music uses one filtered audio transmitter per player so join fade-in is per-player. Keep tracks
  reasonably short; `MusicMaxSeconds` caps accidental huge files.
- The isolated live harness verifies AdminToy collision, widened lane geometry, smoothed persistent-target
  replication, sphere spawning, real product-bot patrol/native retaliation ammo consumption, dummy floor behavior, and
  handoff leak cleanup. Authenticated-client pickup/fire sequencing, first-person presentation, repeated bot death/respawn,
  actual hitmarker and sphere-pop presentation, lethal-reset presentation, and final bilingual visual inspection still
  require visible local QA with real clients. A solo human is supported under the range-owned temporary lobby lock;
  additional humans release that lock so the native countdown proceeds.

## 中文说明

[English version](#english)

适用于 SCP: Secret Laboratory 的 LabAPI 插件。在等待玩家阶段，插件会生成一个房间，里面陈列每个可选 SCP 的模型，
每个模型前放一枚大硬币。玩家走过去捡起硬币即可选择想玩的 SCP。回合开始后，插件让游戏正常分配职业，然后把被选中的
SCP 名额交换给选择它的玩家。

> 本插件由旧的 “scpsl-custom-room-plugin” / `ScpslCustomRoomPlugin` 改名而来。它并不是 SCP-002 那种自定义房间，
> 而是一个暖场 SCP 选择器，所以改成了能体现功能的名字。

### 空间站

玩家出生在 SCP 展厅，正对展品通道。其余舱室都是可选的，需要步行前往。

空间站默认位于地表上方 30 米，避开 SCP-939 前方的原生碰撞箱。现有配置应设置 `surface_clearance: 30`，空间站将整体上移。

```
                                      +Z
         z=71    +---------+          ^
                 |  跑酷竖井  |          |   宽 9 m，高 13.5 m
                 +--+   +--+
       z=17.5
 +---------------+  |   |  +--------------------------------+
 |     观景舱     |    中枢    |          瞄准训练舱            |   z = +/-9.5
 +---------------+  |   |  +--------------------------------+
 x=-22        x=-11 +--+---+ x=11                       x=36
                 |  通道   |
       z=-25.5   +--+   +--+
                 | SCP 展厅 |   天花板 7.5 m
         z=-46   +---------+
```

平面布局来自服务器用 ProjectMER 搭建的房间，数值被整理成具名常量，因此可以直接在代码里扩展：新增一个舱室
只需要添加一个 zone 和连接它的舱门，外壳构建器会自动补齐墙体、密封和照明。

每个活动舱都是**失败即关闭**的：布置期间舱门是实心隔板，只有该活动的世界、道具和调度器全部启动成功后才会移除。

### 功能

- 服务器**等待玩家**时，所有人会以 `Tutorial` 身份被移入空间站，出生在 SCP 展厅，正面朝向一排 SCP 模型
  （049、079、096、106、173、939、3114），每个模型立在矮台上，前方悬浮一枚大硬币。展厅中央通道保持空置，
  以便正对看到服务器 Logo。
- **拿取硬币**即选择该 SCP（拾取被取消，硬币保持原位）。提示面板显示大厅倒计时和你当前的选择，并标注有多少玩家
  选了同一个 SCP（例如 `SCP-096　·　5 人选择`），职业行的每个标签上还有 `·N` 计数。回合开始前可随时改选。
  （面板只在文本变化时重新发送，不会刷屏。）倒计时是标题之下最醒目的元素。进入射击训练场或跑酷竖井后，
  完整面板让位给该活动的 HUD，但倒计时仍以更小、更低调的形式留在屏幕上，确保不会错过开局。
- 插件不会强制原版大厅倒计时。唯一例外是单人使用原生机器人时的临时大厅锁，防止被计数的假人误启动回合；
  当第二名真实玩家加入、最后一名真实玩家离开或训练场停止时会同步释放。
- 默认关闭的**瞄准训练场**位于东侧训练舱，沿舱室方向 21.5 m 射击。射击柜台上永久摆放六把枪械，两侧墙边对称放置
  两个原生改装台。舱内有三条训练道：带掩体的实战机器人、三个不同距离与速度的永久平移靶、以及命中后立即把同一个
  靶体传送到下一个点位的 Aim-Lab 球形靶。拿取柜台枪械会获得一把独立的背包副本，展示用的枪械仍然可见可再次取用。
- 最多两名显式启用的原生 RA 机器人使用原生 FPC 位移持续走位，两个槽位都使用 Crossvec。命中它们会触发
  0.5–0.6 秒反应延迟后的 12 秒锁定还击：机器人会持续开镜、瞄准非头部躯干并自动射击；弹药不足时后撤到 2.2 m
  掩体后换弹再返回。机器人正常死亡并以新身份重生。
- 机器人的子弹造成真实伤害。对训练参与者的致命命中会在原版死亡前被取消，同一名玩家被同步重新初始化为
  `Tutorial` 并送回训练场入口，不会出现旁观画面。离开训练舱或回合开始会销毁所有训练场拥有的枪械、掉落物、
  靶体、机器人和提示。
- HUD 在训练舱门口切换。在中枢和展厅只显示 SCP 选择面板；进入训练舱后隐藏选择面板，只显示中英文训练提示。
  枪械归属、伤害路由和机器人激怒仍然覆盖舱门内侧，站在门口射击行为正常。训练 HUD 使用左侧窄通道
  （`Activities.Aim.HudX`，默认 -1077），位于原生物品栏列表和物品轮盘之间。
- 默认关闭的**脉冲路线跑酷**独占北侧竖井（宽 9 m、长 53.5 m、高 13.5 m）。在 START 台上站立 0.6 秒，
  踩上起点台即刻开始计时（默认无倒计时），依次跳过一串发光落点直到尽头的终点台。踩空会被送回上一个已通过的落点——在下坠早期就会
  触发，因此不会扣血——计时器继续走。可重复使用的 RESET 硬币可原地重开，中英文 HSM 卡片显示计时、分段、
  进度和本次热身的最佳成绩。
- **跑酷路线由游戏自身的物理生成，而非手工摆放。** 构建时插件会读取当前 Tutorial 职业真实的跳跃速度、
  行走速度和冲刺速度，让每一跳都占用该跳跃实际可达距离的目标比例——从轻松的开局逐步过渡到必须冲刺的收尾。
  如果生成结果不可能完成、过于简单、超出竖井范围或顶部空间不足，该玩法会拒绝开放，而不是上线一条坏掉的路线。
  这一点比听起来更重要：SCP:SL 真实的跳跃高度约为 **0.61 m**，远低于手工设计时会假设的数值。

- 回合开始时，插件把玩家交还给游戏进行原版职业分配，然后**交换**被选中的 SCP 名额：
  - 如果原版生成了某人选择的 SCP，则从该选择池里选一名玩家获得该名额，被替换的原 SCP 玩家获得选择者原本的（人类）职业。
  - 如果原版本回合没有生成该 SCP，则跳过该选择。插件**不会额外创建 SCP**，也不会随机生成回退职业，只会重排原版已分配的职业。
- 空间站和所有模型在**回合开始时销毁**。
- 若主要 SCP 在配置的回合早期窗口内、且生命值仍高于阈值时断线，其原职业会开放替补。玩家可输入
  `.volunteer <编号>`（别名 `.v`）参加抽选；**默认同时允许旁观者和仍存活的非 SCP 玩家参加**。
  第一名志愿者会启动短暂抽选，结算时只保留仍在线、仍符合条件的 UserId。
- 结算前会再次确认该 SCP 职业确实空缺；若管理员或其他插件已经补位，就取消抽选，不会生成重复 SCP。
  回合结束、重启、插件禁用和玩家断线都会清理候选人与延迟回调。
- 可选的 `.human`（别名 `.no`）允许健康的 SCP 在回合早期发起身份交换；第一个用 `.volunteer <编号>` 接受的无特殊身份存活人类与其交换，血量、休谟护盾与 ScpTiers 等级随 SCP 身份转移。

### 替补命令

- `.volunteer` / `.v`——列出当前可替补的 SCP。
- `.volunteer 079` / `.v 079`——参加该 SCP 的替补抽选，也接受 `SCP-079` 或 `79`。
- `.human` / `.no`——若配置启用，在回合早期发起身份交换。

### 构建

```powershell
dotnet build -c Release
# 或指定服务端托管程序集目录：
dotnet build -c Release -p:ServerManagedPath="C:\path\to\SCPSL_Data\Managed"
```

Build output: `src/WarmupScpSelector/bin/Release/net48/WarmupScpSelector.dll`. Install `HsmAdapter.dll` as a LabAPI plugin and `HintServiceMeow.dll` for the HUD.

### 安装

Copy `WarmupScpSelector.dll` and `HsmAdapter.dll` to the port's LabAPI plugins folder:

```text
%AppData%\SCP Secret Laboratory\LabAPI\plugins\<端口>\
```

### 配置

```text
%AppData%\SCP Secret Laboratory\LabAPI\configs\<端口>\WarmupScpSelector\config.yml
```

常用选项：

- `Language`——`"en"` 英文，`"cn"` 简体中文。
- `ScpReplacement.IsEnabled`——启用回合开始后的断线 SCP 替补（默认 `true`）。
- `ScpReplacement.AllowAliveVolunteers`——除旁观者外，也允许存活的非 SCP 玩家参加（默认 `true`）。
- `ScpReplacement.DepartureCutoffSeconds`、`VolunteerCutoffSeconds`、`RequiredHealthPercentage`、
  `LotterySeconds`——断线/报名窗口、生命值阈值和抽选时长。
- `ScpReplacement.AllowHumanCommand`——`.human` 身份交换的开关。
- `ScpReplacement.MaxReplacementsPerRound`、`IgnoredRoles`、`CommandCooldownSeconds`——每回合上限、
  排除职业和两个命令共用的冷却。
- `RoomOrigin`——悬空选择房间的世界坐标（较高的 Y 可避免与正式地图冲突）。
- `PedestalSpacing`、`ModelScale`、`SelectorCoinScale`——布局/尺寸。使用**自定义站点图纸**时展台来自图纸，
  该间距不再决定展台位置：每枚硬币会锚定到标签写着该 SCP 的展品上，只有无法按标签匹配的展品才回退到生成的
  网格。保留图纸中的 `SCP-xxx` 标签，硬币就会跟着模型走。
- `SelectorItem`——作为选择硬币的物品（默认 `Coin`）。
- `RoleSwapDelaySeconds`——回合开始后多久执行交换（等待原版职业稳定）。
- `MusicEnabled`、`MusicFilePath`——可选大厅音乐。`MusicFilePath` 需要指向预转换的 `48000 Hz`
  单声道 raw float32 little-endian PCM（`.f32le`）。相对路径会从上面的插件配置目录解析。离线转换一次即可，
  插件运行时直接读取采样：

```powershell
ffmpeg -i lobby.mp3 -ac 1 -ar 48000 -f f32le lobby.f32le
```

- `MusicFadeInSeconds`——玩家进入选择房间时的个人淡入时长。
- `MusicFadeOutBeforeStartSeconds`、`MusicFadeOutSeconds`——原版倒计时接近回合开始时淡出；如果倒计时直接结束，
  职业分配交接时也会兜底淡出。
- `ScpOptions`——可选 SCP 列表，每项包含职业、标签和嵌入模型名。
- `ActivitiesEnabled` 与 `Activities.Aim.Enabled`——两项都必须为 `true` 才会开放训练场。
- `Activities.Parkour.Enabled`——再将此项设为 `true`，即可在训练场最左侧原本空置的区域生成“脉冲路线”。
  跑酷默认关闭，并复用 Aim 训练场的外壳、地板与照明。
- `Activities.Parkour.SchedulerRateHz`、`StartHoldSeconds`、`CountdownSeconds`、`RecoveryGraceSeconds`——跑酷更新、
  起点、倒计时与跌落恢复时序；`HudX`、`FlashY`、`HeroY`、`FooterY` 控制三个稳定 HSM 区域。
- `Activities.Aim.WeaponPresets`——六个实体枪架槽位的枪械、配件代码和受控备用弹药。
- `Activities.Aim.BotCount`——原生机器人槽位数（`0..2`），默认 `0`，必须显式启用。单人也可使用机器人：
  训练场会临时持有原生大厅锁，防止 dummy 连接触发开局；第二名真实玩家加入或训练场停止时自动释放，并始终保留一个公共空位。
- `Activities.Aim.BotWeaponPresets`、`BotRespawnSeconds`、`BotAggroLeaseSeconds` 等——机器人确定性枪械和还击时序。
- `Activities.Aim.SphereActiveCount` 与 `SphereDiameter`——第三训练道球形靶的数量和大小。每次有效命中
  都会立即计分并把同一球体传送到固定 50 点不规则三维位置表中的下一点。
- `Activities.Aim.FlashY`、`HeroY`、`FooterY`——三个互不重叠的 Aim HSM 显示区域。
  `Activities.Aim.CollapsedStatusY` 与 `Activities.Parkour.CollapsedStatusY`（默认 `900`）决定在活动区内
  替代完整 SCP 面板的小型开局倒计时条的位置，保证大厅倒计时始终可见；它比完整面板的倒计时更小、更低调。
- `Activities.Aim.HudX`——训练侧 Aim HUD 通道的 HSM 中心 X（默认 `-1077`）。选择侧只显示原始居中的 SCP
  选择面板；越过地面分界线后 SCP 面板隐藏，只显示位于原生物品栏列表与物品转盘之间的 Aim HUD 和小型倒计时条。

### 把房间交给其他人编辑

空间站可以导出为 **ProjectMER 图纸**，方便美术或地图作者在游戏内编辑器中打开、调整后再交回。

```text
warmupexport                 # 等待玩家阶段，在 RA / 服务器控制台执行
warmupexport my_layout       # 或指定名称
```

也可以在配置里设置 `ExportSchematicName`，让每次搭建空间站时自动导出一份快照。若该端口安装了
ProjectMER，文件会写入其 `Schematics/<name>/<name>.json`（可直接 `mp spawn <name>`）；否则写入本插件的
配置目录。仓库内已附带一份当前房间：`generated/schematics/warmup_station/`。

名称请勿使用 `-`：ProjectMER 的图纸列表会过滤掉带 `-` 的名称。

导出内容及原因：

- **实际生成的全部对象**——地板、舱壁、天花板、舱门框、SCP 模型、品牌 Logo、跑酷落点、靶场设施、灯光和文字。
  它读取的是运行中的世界，而不是照着布局代码重新描述一遍；后者迟早会与实际构建产生偏差，而且只有等到有人
  生成导出文件、发现它和游戏里不一样时才会暴露。
- **Logo 的父子结构。** 它约 200 个面片的倾斜来自"非等比缩放的隐形父物体 × 旋转的子物体"。导出时保留
  `ParentId` 和局部坐标；若压平为世界坐标，Logo 会变得略微错误而不是明显损坏。
- **`marker_*` 锚点**——所有非几何信息：出生点、各舱室中心、各舱门、射击线与枪位、每个跑酷落点。硬币和原生
  改装台同样没有几何体，也以锚点形式导出。
- 坐标为**空间站局部坐标**，不会把空间站当时悬浮的高度写死进文件。

目前尚不支持自动合并回来，详见 `AGENTS.md`。移动某个对象时请一并移动对应的 `marker_*`，合并时会以它们为准。

### 测试

- C# 纯逻辑与运行时邻接测试：`86/86` 通过（新增舱室拼接、墙体密封、展厅站位、训练舱三道、跑酷路线契约，以及自定义图纸的展品硬币锚点）。
- 实机假人验证：`ptest run warmup standard`（`.tests/Playtest` 共享 harness）。`warmup-pulse-line` 用射线
  扫描竖井发现落点，再驱动三个真实假人：冲刺+跳跃必须全程通过，仅行走+跳跃必须在收尾停下，完全不跳必须
  第一跳就失败。2026-08-28 实测：22 个落点、爬升 7.07 m、跳跃+冲刺 21/21 通过。
- SCP、Logo、枪架和保留的移动靶载具模型契约：`10/10` 通过。
- WarmupScp 选择/训练场中英文 HSM fixture：22 个全部为零静态问题。
- 隔离本地端口的 dev-only 实机验证：`253/253` 通过，覆盖全宽场景、全宽柜台、每半区三盏超亮非 HDR 点光源、
  dummy 落地、两名产品机器人持续行走/跳跃、非头部上胸跟踪、从柜台玩家侧真实命中后立即还击、战斗横移、原生持续开火、全程开镜、静止攻击者约 2 秒内触发致命重置、UI 分界、2.2 米
  掩体后装弹、固定 12 秒释放、六把枪、三条平滑移动靶、20 个球形靶、同一网络球体的池化换位、精确清理与训练场重启。

### 已知限制 / 冲突

- 选择器/训练场 HUD 依赖 HintServiceMeow，并使用稳定 HSM hint ID；替补通知使用全局广播，且可选同步到客户端控制台。
- 不要同时安装独立版 SCPReplacer，否则两个插件会同时处理同一次断线并发出竞争抽选。ScpSwap 可以共存：
  本插件会在抽选结算前再次确认对应 SCP 职业仍为空缺。
- `tests/WarmupRangeVerifier` 尚未适配空间站改版，其断言仍基于旧的单一大厅接缝，迁移前会失败。
- 大厅音乐为每名玩家使用一个过滤后的音频发送器，因此加入时淡入是按玩家独立生效的。音频文件不要太长；
  `MusicMaxSeconds` 会限制误放入的超大文件。
- 隔离实机 harness 已验证 AdminToy 碰撞、扩宽训练道、枪架位置、平滑永久移动靶同步、球形靶生成、两名产品
  机器人真实巡逻与原生还击弹药消耗、dummy 落地和交接零泄漏清理。真实客户端的拾取/射击顺序、第一人称表现、
  机器人反复死亡重生、命中标记与球形靶消失重生画面、致命重置画面，以及中英文最终视觉检查仍需在可见本地
  服务器上完成。单人会使用训练场临时持有的大厅锁；第二名真实玩家加入后自动释放，让原生倒计时继续。

## Attribution

The replacement mechanic is adapted from Jon M's SCPReplacer and Augaton's modern rewrite. See [NOTICE.md](NOTICE.md).

## License

MIT. See `LICENSE`.
