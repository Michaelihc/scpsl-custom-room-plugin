# WarmupScpSelector — Warmup Activity Suite Specification
### "The Seam": one instrument, four lanes

> Grounded against the shipped code: `WarmupText.cs` palette constants, `HsmHintDisplayProvider` (stable-ID hints, `ForceFastUpdates=false`, ~0.1 s Fast coalescing), the `_lastStatusText` change-skip cache and `HintLoop` in `SelectorController.cs`, the `OnBeforeVanillaRoleAssignment` synchronous teardown, the `MusicFadeOutBeforeStartSeconds` countdown-threshold hook, `SelectorRoom`'s primitive/light/text pipeline with a few very bright ordinary non-HDR point lights, and `Config.Language` (single server-wide `cn`/`en` flag).

---

## 1. Shared vision + the ONE HUD language

**Vision.** The 莺歌傲然 SCP-draft gallery is the hub; four lanes open off it — **Aim Range (Ghost Rail)**, **SCP-018 Dodgeball (Ricochet Ring)**, **Parkour Speedrun (Pulse Line)**, and **Jailbird Duel (Break Bar)**. Each turns 15–75 s of waiting into a self-contained, honest, replayable loop where **you race your own personal best first.** A global leaderboard is planned as a later phase, but is explicitly outside the initial implementation scope. Every lane is good solo first, deepens with a second player, degrades cleanly to 3–4, resets instantly, survives a player leaving, and — the load-bearing rule — treats the real round starting as a **clean handoff, never a failure.** Rewards are cosmetic and confined to warmup; nothing a player earns changes role odds, HP, speed, weapons, spawn, or any real-round stat. The whole suite is one centrally-owned subsystem (an `ActivityManager` on `SelectorController`), not four plugins: one scheduler, one hint service, one exclusive per-player state, one idempotent stop path that disarms every hazard *before* the Tutorial→None handoff.

**The one HUD language.** Every lane's hero is a single full-width **horizontal segmented STATE LINE** — Ghost Rail, Ricochet Halo, Split Spine are the same object in three dialects. The line encodes **position + magnitude + verdict** in one glance and is mirrored into a world light strip wherever a wall or rim exists. Everything else — score, clock, streak, accuracy — is demoted to quiet instrument data around it. If a player remembers one thing from a lane, it is that line and its one green pulse.

### 1.1 Palette-state law (exact brand hex — codify as extended `WarmupText` constants)

| Hex | Name | The one job it owns |
|---|---|---|
| `#0A0D12` | navy base | conceptual room backdrop (shipped room floor `#161B26`, unlit cells) |
| `#232A37` | panel / divider | the 15-em-dash divider `———————————————`; **empty / future** line cells |
| `#5B6270` | dim | denominators, unselected chips, secondary separators |
| `#8B95A6` | muted | labels, coaching, **behind-PB** ("▼ to beat") — never red |
| `#A8B0BC` | muted-2 | secondary coaching / instruction |
| `#E7ECF3` | white | scores, values, rival `◇`, "clean & saved" neutral |
| `#66CCFF` | teal — selectable | menu / tier chip you *could* pick |
| `#4FCBFF` | teal — **live/you** | current node, your marker, "GO", live accent, title |
| `#46D7F3` `#3CE2E7` `#33EEDA` | teal — **world only** | floor seam + rim gradients; **never used in hint text** |
| `#FFD24D` | gold — **time** | clocks, countdowns, the fixed **PB gate**, the gold "missed gate" marker |
| `#FFC94D` `#FFE08A` | gold soft | heat / welcome gradients |
| `#5BFF80` | green — success | ahead / cleared / first-record / round-handoff / one world pulse |
| `#66FF66` | green — **PB hero** | the hero **number** on a PB — used only when the hero is a *score* |
| `#FF5555` | red — **imminent only** | final ~5 s timer digits + one world lamp, or a top-band danger cell; **always paired with a label** |

**Red is law:** it never marks a miss, a hit, a loss, a decoy, or trailing your PB. Scarcity is what makes final-seconds red mean "urgent." Behind-PB is muted `#8B95A6` with `▼ … to beat`.

### 1.2 Type scale (percent of the HSM base `FontSize`, which ships at 20; extends the panel's 165/92/80/70 grammar)

- **Eyebrow / contract** 72–78 % muted — mode · card · duration; smallest because it rarely changes.
- **Signature line** 86–92 % — the state line; mid-size because fill + color, not size, carry the fast read.
- **Data strip** 80–88 % — muted-label + white-value pairs (ACC / PACE / FLOW, PERFECT / lives).
- **Hero** 165–195 % bold — a **score** in white, or the **clock** in gold; largest = emotional anchor.
- **Coaching footer** 70–80 % muted — exactly one instruction, active voice, from the player's side; changes only at phase boundaries.
- **Rule:** exactly one hero on screen per lane at a time. **Nested `<size>` is banned** (it multiplies unpredictably through the hint transport) — author size spans as siblings inside one `<align=center>` block.

### 1.3 Timer / score / streak / accuracy treatment (uniform)

- **Timer:** gold `#FFD24D`, tenths while live, **exact centiseconds frozen only on the recap/split/finish card**. Never drive a live centisecond clock through HSM. Optional `<mspace=0.62em>` for non-jittering digits — validate in the font first.
- **Score / count:** white `#E7ECF3` hero; turns `#66FF66` for one frame only on a PB and only when the hero is a number (not a clock).
- **Streak / flow:** a data-strip integer; optionally a block-glyph meter `██████░░░░` climbing the teal→green ladder (`#5B6270→#66CCFF→#46D7F3→#5BFF80`). Never `<mark>` (its highlight box spans the whole line height — a shipped, removed bug).
- **Accuracy:** white `%` in the data strip, exact on the recap card.
- **PB verdict ("ledger"):** the one shared verdict chip, identical whether the metric is points, seconds, or survival: `▲ +N` green ahead / `▼ N to beat` muted behind / `◆ NEW BEST` gold→green.

### 1.4 Signature element family + shared devices

- **State line** is the signature. **"You are here" = the bracket `[X]`** (proven in `WarmupText` today). **Reserve filled/hollow `◆`/`◇` strictly for you-vs-rival races.**
- Shared house devices, kept verbatim so the suite reads as one product with the draft panel: the **15-em-dash `#232A37` divider**; the **`·`-separated muted eyebrow read as a path**; the **single muted coaching line**; the **`[X]` chip idiom**.

### 1.5 Zones, transport, cadence (one HUD service)

Extend `HsmHintDisplayProvider`. Each active lane owns **three stable HSM IDs** at fixed Y — `warmupscp.<lane>.hero`, `.flash`, `.footer` — alongside the shipped `warmupscp.status` panel.

- **Hero + footer** reuse the shipped `_lastStatusText` change-skip cache, one composite submission per player at **4–5 Hz default** (raise to 10 Hz only after live profiling; **never** set `ForceFastUpdates` globally).
- **Flash** needs a **new force-show path** that bypasses the change-skip cache and clears by token (~700 ms) — so an identical repeat judgment ("PERFECT", "HIT") genuinely reappears.
- **Y-map:** while a player is in a lane, collapse the draft panel (`StatusHintY=840`, ~250 px) to a **one-line bottom strip** so they keep their SCP pick without colliding with the lane footer; restore it on return. A player is only ever in one lane, so per-player hint count is bounded to draft + 3.

### 1.6 World vs text; light budget

Persistent lane/danger/phase state lives in **world objects** (`PrimitiveObjectToy` + `LightSourceToy` + `TextToy`, pooled and despawned exactly like `SelectorRoom`); hints carry only live numbers and event verdicts. Deferred lit-cell designs may use HDR-emissive albedo with a shared bloom light rather than one real point light per cell — literal per-cell lights across simultaneous lanes will exceed SCP:SL's real-time light limit. The current combined Aim/selector hall uses only a few very bright ordinary point lights. The hub logo retains its HDR albedo boost and shares the selector center light for bloom, so it does not consume another real-time light. Cap real point lights per lane. The hub logo + QQ `860705092` stay central and are never repeated inside a lane.

### 1.7 State machine every lane implements identically

`idle/empty` (invitation, not status) → `select` → `countdown` (gold digits, teal GO) → `active` → `success micro-flash` → `fail micro-flash` (never red) → `recap` (~2 s, retry live) → `first-record` → `personal-best-beat` (green heading + line saturates green + one green world pulse) → `round-handoff` (green, "does not count as a failure") → `dropout` (neutral, no forfeit).

### 1.8 Localization + handoff timing

Single server-wide `Config.Language` (`cn`/`en` → `useChineseLocalization`), per-string bilingual pairs, `　` for CJK spacing, ASCII node numbers kept in both languages, **never stacked EN+CN.** The **ROUND STARTING** card is best-effort, driven off the countdown crossing a threshold *during warmup* (as the music fade already is at `MusicFadeOutBeforeStartSeconds`) — **not** inside `OnBeforeVanillaRoleAssignment`, which clears hints and despawns synchronously. Gameplay correctness wins: never delay cleanup or role assignment to guarantee a cosmetic card.

### 1.9 Glyph reality gate (do once, before any lane ships)

Only `● ▶ | · — 　` are proven in shipped C#. **Every signature glyph — `◆ ◇ │ ━ ╌ ▲ ▼` — is unvalidated.** Validate one shared set through `tools/preview/banner.html` and register ASCII fallbacks before any lane's C# ships:

| Glyph | Use | Fallback |
|---|---|---|
| `━` | line track | `--` |
| `╌` | severed track | `··` |
| `│` | PB gate / separator | `|` |
| `◆` | you (race) | `#` or `●` |
| `◇` | rival (race) | `o` or `○` |
| `▲` `▼` | ahead / behind | `+` / `-` |

---

## 2. SPEC A — AIM RANGE · "Ghost Rail" / 瞄准训练场·幽轨

### 2.1 Core loop

From the gallery the player steps onto a lit lane plate; the lane seam brightens teal. Three physical target plates pick **drill · tier · duration**. A 3-2-1 gold countdown runs, targets execute a fixed **seeded balanced deck**, a ~2 s recap appears while the start coin is already live, and the player re-fires or steps out. **Entry = step onto a lit plate; retry = grab the RESET coin** (both proven idioms — never "shoot to select"). Shooting is reserved for the target content only. Leaving destroys only that lane's active state and never touches the real player count.

### 2.2 Solo / duo / 3–4

- **Solo (baseline):** one lane, your deck, the Ghost Rail racing your interpolated PB pace. First run sets the baseline against the tier target.
- **Duo race:** the paired lane shoots the RACE coin at staging; both lanes get the **same deck, seed, timestamps, stat buckets, weapon preset**, with **mirrored horizontal coordinates** so angles are equal. Targets are lane-owned — no stealing, blocking, or shared-dummy latency race. A white `◇` rival joins your teal `◆` and gold `│` gate.
- **3–4:** independent same-seed lanes (a heat). Each HUD shows personal score, place `2/4`, nearest-rival delta, PB rail — never four dense stat blocks. Late arrival takes a free lane solo or reserves the next heat; a departure just removes that marker.

### 2.3 Deterministic target catalog + difficulty scaling

**Motion engine:** one central scheduler updates only active moving targets, position evaluated from **absolute elapsed run time** (never accumulated deltas). Every archetype = a 100–220 ms **cue**, a committed segment, an 80–180 ms **settle/dwell**. Seed phase/direction/speed-bucket/scale/dwell **once at spawn**; no random calls during movement. No navmesh, no perception, no combat AI, no per-target coroutine, no per-frame allocation.

**Catalog (author-facing indices; never shown to the player):**

| Family | Cards | What it isolates |
|---|---|---|
| **FIRST SHOT / 第一枪** — static/click | Still Point, Grid Pop, Wide Swing, Micro Correct, Vertical Ladder, Shrink Window, Double Gate, Burst Fan | acquisition, micro-correction, decisive arm movement, measured switch |
| **STRAFE LINE / 横移线** — controlled motion | Bobber, Pendulum, Stop-Go, Change-Up, Depth Glide, Figure-Eight, Orbit Arc, Zig-Zag | predictable movement, reacquisition, honest angular-size change |
| **TRACK HOLD / 持续跟枪** — track/switch | Long Glide, Reactive Track, Track Shrink, Priority Pair, Relay Three, Orbit Swap | smooth contact, authored reversals, priority swaps |
| **PEEK CLOCK / 探头时机** — cover/discipline | Pop-Up, Shoulder Peek, Cross-Window, Late Commit, Hold-Fire Decoy, Decoy Cross | timing, exposure discipline, shape-coded no-shoot |
| **SWITCHBOARD / 转火板** — chains | Double Gate, Burst Fan, Priority Pair, Relay Three | 2–3 target acquisition chains, switch latency |
| **MIXED SIGNAL / 混合信号** — assessment | 10–15 s blocks sampling every family (75 s benchmark) | the assessment, not the tutorial |

**Difficulty (visible physical demands only, never hidden target intelligence):**

| Tier | Scale | Speed | Dwell | Active | Decoys (PEEK/MIX) |
|---|---|---|---|---|---|
| **OPEN / 入门** | 0.95–1.20 | 1.2–2.4 m/s | 0.9–1.5 s | 1 | teaching card only |
| **FOCUS / 专注** | 0.80–1.05 | 2.2–4.0 m/s | 0.55–1.05 s | ≤2 | 8–10 % |
| **EDGE / 锐化** | 0.68–0.95 | 3.6–5.5 m/s | 0.35–0.8 s | ≤3 | 12–15 % |

Ranked runs **never adapt mid-run.** An **UNRANKED / 不计排名 PRACTICE** flag may change one dimension per 5-target wave. **Balanced seeded deck:** exact quotas of left/right, near/mid/far, fast/standard/slow, small/standard/large, short/standard/long dwell, and decoys; seeded shuffle under adjacency rules (no anchor twice, no >2 same-side, never a decoy or tiny EDGE target first). Store the seed + 5-second checkpoints with the result. **Do not visually scale a dummy unless the authoritative hitbox scales with it** — vary apparent difficulty through distance, exposed region, and honest cover instead.

### 2.4 Scoring & personal-best

**Click/peek/switch:** valid hit **+100**; reaction bonus **+0..30** from remaining dwell; miss **−25** (breaks flow, never below 0); expiry **0** (breaks flow, no subtraction); HOLD-FIRE hit **−150**; flow **+5/hit after 5 consecutive, capped +25**. **Track:** fixed low-recoil auto, hit **+5**, sustained-contact stability **+10 per 250 ms cap +40/target**, switch bonus **+0..40** by latency; report accuracy + longest contact separately; **no per-frame crosshair raycasts** — score off weapon/damage events only. **Head/center +20 only after LabAPI hit-location is verified** for the chosen dummy+weapon; otherwise flat valid-hit points. No shotguns/multi-pellet in ranked.

**PB key:** player ID + drill + tier + duration + weapon preset + seed policy + scoring version. Only a complete valid run replaces the PB; a truncated run never updates the ghost. Result order: **PB delta first**, then score, ACC, median acquisition/switch, clears, longest streak, decoy hits. A standard timed run **never fails** — only opt-in CLEAN RUN has a success/fail threshold.

### 2.5 Spatial layout

Four parallel forward-facing lanes sharing the navy shell (`#161B26` floor, `#33EEDA` seam), `#232A37` cover/dividers, near-white key lighting so dummies read true. Per lane: low cover rail, two side peek walls, three depth bands, a 120° target arc, an overhead state ring, a start/RESET coin, and drill/tier/duration plates. **Independent sightlines — never one shared shooting volume** (crossfire, body-block, target ambiguity).

### 2.6 Exact tech (lightweight targets, perf)

> **Current implementation override (2026-07-26):** the shipped/default-off warmup range intentionally supersedes
> the original four-lane/no-dummy concept. The selector gallery and training area form one uninterrupted, full-gallery-width
> rectangular hall with no doorway or choke point. Its centered 19.2 m × 22 m zone has three 6.4 m lanes: tactical native
> cover bots, three persistent parallel sliding `ShootingTargetToy`s, and deterministic pop/respawn primitive spheres.
> The deeper drill catalog, PB/race system, and four independent player lanes below remain deferred design material.
> Its six weapon presets are persistent dispenser pickups on top of the shooting counter, with symmetric native attachment workstations at both side walls; interacting grants a separately
> owned inventory copy without consuming the displayed pickup. The hall contains no rack or shelf geometry, and its
> activity/session bounds cover the whole rectangle rather than a former rear-range subdivision. The shooting-counter
> wall spans `ShellWidth - 0.6 m`. The training half uses exactly three intensity-24/range-16 point lights and the
> selector half uses exactly three intensity-24/range-18 point lights, all with ordinary non-HDR light colors. The logo
> retains its HDR albedo boost under the shared center selector light without adding a fourth point light. The former
> seam is UI-only: the selector side keeps only the original SCP panel, the training side shows only Aim UI, and
> shooting/damage/bot mechanics remain active throughout the full hall.

- **Native RA dummies are allowed only in the isolated bot lane** — canonical-human filtering, owned live identity,
  temporary solo lobby locking, native motor steps, native firearm actions, and synchronous teardown keep them out of
  draft counts and round assignment. Each spawn uses Crossvec only. Bots patrol and combat-strafe without
  authored dwell, jump on bounded native-input cooldowns, hold ADS throughout retaliation, and lock the first genuine tracked-firearm
  attacker after a 0.5–0.6 second reaction delay for one non-refreshing 12-second lease. Retaliation aims inside the upper portion of a non-head
  body collider so the low counter does not suppress fire. Automatic retaliation uses native `Shoot->Hold` /
  `Release` and requires `ShotWeapon` plus ammo consumption; low ammo routes behind slot-owned 2.2 m cover before a
  native reload. Primitive sphere hits use the native hitscan obstacle result; sliding targets use cancellable native
  shooting-target pre-damage so they remain persistent.
- **Every event guarded by lane token + run ID + target generation ID + active window** — a shot arriving during expiry/recycle can never score against the next pooled target.
- **Budget:** normally 1 shoot target + ≤1 HOLD target per occupied lane; ≤4 lanes; reuse objects across spawns. One scheduler, not one coroutine per target/player. Motion 15 Hz (configurable 10–20 after live test); repaint the rail only when the lit-cell count changes, ≤5–10 Hz through the change-skip cache; ◆ steps in quantized pace bands while exact PACE prints beneath.
- **World glow (deferred lane designs only)** via HDR-emissive cells + shared bloom light; ≤ a small cap of real point
  lights per lane. The current combined hall uses only three very bright point lights per half; only the logo keeps an
  HDR albedo boost, sharing the selector center light rather than owning an extra one.
- **Weapon isolation (P0):** grant an activity-owned preset (semi-auto pistol for click/peek/switch; low-recoil auto for track), infinite reserve + auto-refill, **cancel all PvP damage in warmup**, and contain shots to the hall. The current counter-armoury override keeps an issued copy throughout the full-room activity and destroys it on replacement/drop, disconnect, disable, and handoff. Valid issued-gun hits always damage owned dummies; retaliation setup is best-effort and cannot make a dummy immune. A later role change is not sufficient cleanup for networked pickups.

### 2.7 Interruption / reset

Each lane is a state machine: `Empty → Selecting → Countdown → Active → Recap → Empty`. Reset cancels the lane token, invalidates all target generation IDs, hides pooled dummies, clears the three HSM IDs, restores inventory, returns the seam to dim. Player leave cleans only their lane. **Round start / plugin disable cancels all lanes in one pass.** At the pre-assignment hook: freeze scoring, classify the run **INTERRUPTED_BY_ROUND (not failed)**, suppress CLEAN-RUN penalties, clear weapons/effects/HSM, hide toys, then let the existing Tutorial→None handoff proceed. A partial run may appear in session stats but never replaces a full-duration PB.

### 2.8 HUD — every state (TMP + EN/CN; `\n` = newline)

**Signature Ghost Rail:** 16-cell line, gold gate `│` fixed at cell 10 (9 behind / 6 ahead — falling behind is easier than leaping past, so trailing markers stay on the rail), teal fill left→`◆`; ahead = gate swallowed by fill.

**Idle / empty**
```
<align=center><size=75%><color=#8B95A6>AIM RANGE · LANE OPEN</color></size>
<size=135%><b><color=#4FCBFF>STEP INTO THE LANE</color></b></size>
<size=80%><color=#A8B0BC>Shoot a cyan plate to choose your drill</color></size></align>
```
EN: `AIM RANGE · LANE OPEN / Step into the lane / Shoot a cyan plate to choose your drill`
CN: `瞄准训练场 · 训练道空闲 / 进入训练道 / 射击青色靶牌选择训练`

**Selecting** — one bright chip, rest dim
```
<align=center><size=75%><color=#8B95A6>CHOOSE A DRILL</color></size>
<size=88%><b><color=#66CCFF>[FIRST]</color></b>  <color=#5B6270>STRAFE</color>  <color=#5B6270>TRACK</color>  <color=#5B6270>PEEK</color>  <color=#5B6270>SWITCH</color>  <color=#5B6270>MIX</color></size>
<size=78%><color=#A8B0BC>Shoot again to confirm · step out to leave</color></size></align>
```
CN: `选择训练 / [第一枪] 横移 跟枪 探头 转火 混合 / 再次射击确认 · 走出训练道退出`

**Countdown** (GO frame swaps the digit to teal `<size=180%><b><color=#4FCBFF>GO</color></b>`)
```
<align=center><size=75%><color=#8B95A6>FIRST SHOT · FOCUS · 20s</color></size>
<size=180%><b><color=#FFD24D>3</color></b></size>
<size=78%><color=#A8B0BC>Settle your crosshair</color></size></align>
```
CN: `第一枪 · 专注 · 20秒 / 3·2·1 / 稳住准星 → 开始`

**Active** (hero score + gold timer; footer = rail + ACC/PACE/FLOW). Behind-PB variant recolors fill + PACE to muted and reads `▼`. Final 5 s: only the timer digits + world lamp go `#FF5555`.
```
<align=center><size=75%><color=#8B95A6>STRAFE LINE · FOCUS</color></size>
<size=165%><b><color=#E7ECF3>2,480</color></b></size>  <size=112%><b><color=#FFD24D>12.4s</color></b></size>
<color=#4FCBFF>━━━━━━━━━</color><color=#FFD24D>│</color><color=#4FCBFF>━◆</color><color=#232A37>━━━━</color>
<size=88%><color=#8B95A6>ACC</color> <b><color=#E7ECF3>91%</color></b>   <color=#8B95A6>PACE</color> <b><color=#4FCBFF>+120</color></b>   <color=#8B95A6>FLOW</color> <b><color=#E7ECF3>8</color></b></size></align>
```
CN eyebrow `横移线 · 专注`; labels `命中率 / 节奏 / 连中`; numbers + rail identical.

**Hit micro-flash** (`.flash`, force-shown, clears ~700 ms)
```
<align=center><size=78%><b><color=#5BFF80>+128 · CLEAN HIT</color></b> <color=#8B95A6>184ms</color></size></align>
```
CN: `+128 · 精准命中 · 184毫秒`

**Miss / expiry / HOLD** (`.flash`, muted; HOLD is white bold; never red)
`MISS · FLOW RESET` / `TARGET GONE · FIND THE NEXT` / `HOLD FIRE · −150`
CN: `未命中 · 连中已重置` / `靶标已消失 · 寻找下一个` / `应当停火 · −150`

**Drill complete (recap, ~2 s, retry live)**
```
<align=center><size=78%><b><color=#5BFF80>DRILL COMPLETE</color></b></size>
<size=165%><b><color=#E7ECF3>4,860</color></b></size>
<size=88%><color=#8B95A6>ACC</color> <color=#E7ECF3>92%</color>   <color=#8B95A6>MEDIAN</color> <color=#E7ECF3>218ms</color>   <color=#8B95A6>BEST FLOW</color> <color=#E7ECF3>14</color></size>
<size=76%><color=#A8B0BC>Shoot the start plate to run it again</color></size></align>
```
CN: `训练完成 / 4,860 / 命中率 92% · 中位反应 218毫秒 · 最佳连中 14 / 射击开始靶牌即可再来一局`

**First record**
```
<align=center><size=78%><b><color=#5BFF80>FIRST RECORD SET</color></b></size>
<size=165%><b><color=#E7ECF3>4,860</color></b></size>
<size=76%><color=#A8B0BC>Your next run will race this pace</color></size></align>
```
CN: `已建立首个纪录 / 4,860 / 下一局将与这次节奏竞速`

**Personal-best beat** (the only `#66FF66` hero; +delta leads; one green world-ring pulse)
```
<align=center><size=78%><b><color=#5BFF80>PERSONAL BEST · +340</color></b></size>
<size=175%><b><color=#66FF66>5,200</color></b></size>
<size=84%><color=#A8B0BC>Previous best 4,860</color></size></align>
```
CN: `刷新个人最佳 · +340 / 5,200 / 原纪录 4,860`

**CLEAN RUN fail (the only explicit fail; white, names the fix)**
```
<align=center><size=78%><b><color=#E7ECF3>CHALLENGE INCOMPLETE</color></b></size>
<size=105%><color=#A8B0BC>ACCURACY</color> <b><color=#E7ECF3>54%</color></b> <color=#5B6270>· need 60%</color></size>
<size=76%><color=#A8B0BC>Slow down, then shoot the start plate to retry</color></size></align>
```
CN: `挑战未完成 / 命中率 54% · 需要60% / 放慢节奏，然后射击开始靶牌重试`

**Round handoff** (green, off the countdown threshold)
```
<align=center><size=82%><b><color=#5BFF80>ROUND STARTING</color></b></size>
<size=92%><color=#E7ECF3>Warmup closed cleanly</color></size>
<size=76%><color=#A8B0BC>This partial run does not count as a failure</color></size></align>
```
CN: `回合即将开始 / 热身已正常结束 / 本次未完成训练不会记为失败`

**Insufficient time (ranked duration gated)** — gold header, offers the next fitting duration
```
<align=center><size=80%><b><color=#FFD24D>75s BENCHMARK UNAVAILABLE</color></b></size>
<size=78%><color=#E7ECF3>20s SNAP is ready</color></size>
<size=72%><color=#8B95A6>Long runs close before the round starts</color></size></align>
```
CN: `75秒基准暂不可用 / 20秒快速训练可开始 / 长训练会在回合开始前关闭`

**Race staging / legend / dropout**
```
<align=center><size=76%><color=#8B95A6>RACE READY · SAME SEED</color></size>
<size=120%><b><color=#4FCBFF>YOU</color> <color=#5B6270>vs</color> <color=#E7ECF3>RIVAL</color></b></size>
<size=78%><color=#4FCBFF>◆ YOU</color>  <color=#E7ECF3>◇ RIVAL</color>  <color=#FFD24D>│ PB</color></size></align>
```
Dropout: `<b><color=#E7ECF3>RIVAL LEFT</color></b>` + `Your run continues and remains PB-eligible`.
CN: `竞速准备 · 相同序列 / 你 vs 对手 / ◆ 你 · ◇ 对手 · │ 最佳` — dropout `对手已离开 · 你的训练继续，仍可刷新个人最佳`

---

## 3. SPEC B — SCP-018 DODGEBALL · "Ricochet Ring" / 折射环

### 3.1 Core loop

Step into the ring; occupancy picks the mode (1 = Solo, 2 = Duel, 3–4 = FFA). One real base-game SCP-018 is served from a telegraphed aperture; danger escalates with measured speed; a contact ends solo / removes one FFA player / scores a duel point; ~1.5 s drain + compressed 3-2-1 → next serve. **A hit ends a run and immediately offers another — no penalty ceremony.**

### 3.2 Arena + ball behavior

**Arena:** ~12 × 10 m **chamfered rectangle**, 4.5–5 m high, flat floor, solid primitive walls, 0.35–0.5 m sloped skirts sealing floor/wall seams, **no glass/doors/concave recesses**, a high-contrast `#3CE2E7` boundary seam, launchers flush behind protected apertures, a protected rim gallery **outside the ball volume** for joiners/eliminated players. **Ball (P0 safety):** exactly one live SCP-018, tagged with arena + serve generation IDs; **cancel all real health/status damage**, resolve one logical activity hit, immediately disarm + destroy, ignore late callbacks. A watchdog resets on hit, rally timeout, out-of-bounds, low-speed/stuck (~1.0–1.5 s), duplicate-ball assertion, leave/disable/round-start; plus a hard lifetime **12–15 s, far below the native 80 s exit.** Telegraph the aperture for 700–900 ms; the angle is fixed after the tell — **the copy promises the launch origin, not an exact post-bounce route** (bounces are honest physics). Cap measured velocity to preserve a minimum reaction window; the harmless launch/reset window is enforced by **code-level hit suppression**, not copy alone.

### 3.3 Solo / 1v1 / FFA

- **Solo A — Pattern Run:** 24–30 s of 5–7 deterministic serves; clear by surviving; records clean-clear + perfect dodges (not shaved ms). **Solo B — Dodge Streak:** endless short rallies, headline replay loop; a dodge scores when the ball enters a ~2–2.5 m threat radius and exits clean; a **perfect dodge** ≈ 0.6–0.8 m closest approach.
- **1v1 Duel:** opposite halves split by a teal seam; server gets an inert 018 in a recessed cradle and 4 s to throw (else the cradle auto-launches the telegraphed neutral serve); it arms after the center seam / first bounce, then **either player can be hit by the rebound** — reckless power punishes the thrower. First to 2, 30 s set cap; loser serves next; both get a 1.0 s spawn shield (shown as an unfilled Halo).
- **FFA (3–4):** neutral launcher (no focus-fire/kingmaking), one ball, first contact eliminates, last standing wins; eliminated → rim gallery, rejoin next set (10–20 s), **no revives.** Population changes collapse gracefully (4→2 becomes Duel, →1 becomes Solo, never called a loss).

### 3.4 Scoring & PB

Hierarchy: personal goal (streak / pattern progress / survival) → PB delta → perfect-dodge style → duel/FFA placement → optional session leader. PB keys, **completed-solo-only, versioned by pattern:** `DodgeStreakMax`, `PerfectDodgesInRun`, `PatternCleanClear`; duel/FFA outcomes stay ephemeral (opponent-dependent). Perfect dodge = a brief green Halo tick + small `PERFECT` line, cosmetic only.

### 3.5 Tech / perf

One physics ball, O(≤4) squared-distance samples for near-miss scoring at 10–20 Hz (one judgment window per serve, never per-contact per-player). **Ricochet Halo world ring = 8 primitive tiles + one shared unshadowed bloom light** (not 8 point lights), repainted only when the integer danger band changes. Reset must confirm the old ball destroyed/disarmed before serving the next. The full-ring green PB burst is one property transition + one delayed restore, generation-guarded.

### 3.6 Interruption / reset

At round start: freeze scoring, despawn ball/launchers/halo/pads, save any earned PB, show the handoff card off the countdown threshold, return control. **Never recorded as a hit, loss, streak break, or DNF.**

### 3.7 HUD — every state

**Signature Ricochet Halo:** 8 cells mirrored world-ring ↔ TMP strip. Fill **count** = danger (primary, colorblind-safe); bands teal 1–4 / gold 5–7 / red cell 8 (redundant); **hollow dim outline `[◇◇◇◇◇◇◇◇]` = safe/not-yet-armed**, ignite-from-left = live.

**Idle** — dormant outline shown so the glyph is learned at rest
```
<align=center><size=75%><color=#8B95A6>SCP-018 · RICOCHET RING</color></size>
<size=165%><b><color=#4FCBFF>DODGEBALL</color></b></size>
<size=90%><color=#232A37>[◇◇◇◇◇◇◇◇]</color></size>
<color=#232A37>———————————————</color>
<size=82%><color=#E7ECF3>Step into the ring</color></size>
<size=72%><color=#8B95A6>Solo pattern · Duel at 2 · FFA at 3–4</color></size></align>
```
CN: `SCP-018 · 折射环 / 躲避球 / 踏入场地开始 / 单人路线 · 2人对决 · 3–4人乱斗`

**Countdown** — gold 3-2-1, two teal cells preview the starting band (ball not yet armed)
```
<align=center><size=78%><color=#8B95A6>LAUNCHING</color></size>
<size=250%><b><color=#FFD24D>3</color></b></size>
<size=90%><color=#4FCBFF>[◆◆</color><color=#232A37>◇◇◇◇◇◇]</color></size>
<size=72%><color=#A8B0BC>Watch the lit aperture</color></size></align>
```
CN: `即将发射 / 3 / 注意亮起的发射口`

**Active — Pattern** (hero fraction: white numerator = serves cleared, dim `/6` = card remaining)
```
<align=center><size=78%><color=#8B95A6>PATTERN · BANK SHOT</color></size>
<size=235%><b><color=#E7ECF3>4</color><color=#5B6270>/6</color></b></size>
<size=92%><color=#4FCBFF>[◆◆◆◆</color><color=#FFD24D>◆◆</color><color=#232A37>◇◇]</color></size>
<size=82%><color=#8B95A6>BEST</color> <b><color=#E7ECF3>620</color></b>　<color=#5BFF80>▲ +25</color>　<color=#8B95A6>PERFECT</color> <b><color=#E7ECF3>2</color></b></size></align>
```
CN labels: `路线 · 折射 / 最佳 / 完美`

**Active — Streak** (raw count, `▼ N to beat` muted)
```
<align=center><size=78%><color=#8B95A6>DODGE STREAK</color></size>
<size=245%><b><color=#E7ECF3>12</color></b></size>
<size=92%><color=#4FCBFF>[◆◆◆◆</color><color=#FFD24D>◆◆</color><color=#232A37>◇◇]</color></size>
<size=82%><color=#8B95A6>BEST</color> <b><color=#E7ECF3>17</color></b>　<color=#8B95A6>▼ 5 to beat</color>　<color=#8B95A6>PERFECT</color> <b><color=#E7ECF3>4</color></b></size></align>
```
CN: `连续闪避 / 最佳 17 · 还差 5 · 完美 4`

**Flash — judgments** (`.flash`, force-shown; contact muted white, never red)
`PERFECT DODGE` `#5BFF80` / `CLEAN DODGE` `#4FCBFF` / `HIT` muted `#8B95A6`
CN: `完美闪避` / `成功闪避` / `被击中`

**Success — pattern cleared** (recap; precise values live here)
```
<align=center><size=170%><b><color=#5BFF80>PATTERN CLEARED</color></b></size>
<size=220%><b><color=#E7ECF3>800</color></b></size>
<size=84%><color=#8B95A6>PERFECT</color> <b><color=#E7ECF3>4</color></b>　<color=#5B6270>│</color>　<color=#8B95A6>CLEAN</color> <b><color=#E7ECF3>6 / 6</color></b></size>
<size=74%><color=#4FCBFF>Next pattern in 2s</color></size></align>
```
CN: `路线完成 / 800 / 完美 4 │ 全净 6/6 / 2秒后进入下一路线`

**Fail — run ended** (non-red, forward-facing)
```
<align=center><size=160%><b><color=#E7ECF3>RUN ENDED</color></b></size>
<size=215%><b><color=#FFD24D>12</color></b></size>
<size=84%><color=#8B95A6>BEST</color> <b><color=#E7ECF3>17</color></b>　<color=#5B6270>│</color>　<color=#8B95A6>PERFECT</color> <b><color=#E7ECF3>4</color></b></size>
<size=74%><color=#4FCBFF>Retrying in 2s</color></size></align>
```
CN: `本次结束 / 12 / 最佳 17 │ 完美 4 / 2秒后重试`

**Personal-best beat** (`#66FF66` hero + one green full-ring burst)
```
<align=center><size=170%><b><color=#5BFF80>NEW PERSONAL BEST</color></b></size>
<size=235%><b><color=#66FF66>18</color></b></size>
<size=82%><b><color=#FFD24D>◆</color> <color=#5BFF80>+1 over your best</color></b></size>
<size=72%><color=#A8B0BC>Ricochet Halo updated</color></size></align>
```
CN: `刷新个人最佳 / 18 / ◆ 比原纪录多 1 / 折射光环已更新`

**Duel active** (your side teal, opponent white; serve timer gold; PB stays visible)
```
<align=center><size=78%><color=#8B95A6>DUEL · FIRST TO 2</color></size>
<size=205%><b><color=#4FCBFF>1</color> <color=#5B6270>—</color> <color=#E7ECF3>1</color></b></size>
<size=92%><color=#4FCBFF>[◆◆◆◆</color><color=#FFD24D>◆◆</color><color=#232A37>◇◇]</color></size>
<size=80%><color=#FFD24D>Your serve · 3s</color>　<color=#5B6270>│</color>　<color=#8B95A6>PB survival</color> <color=#E7ECF3>24.6s</color></size></align>
```
CN: `对决 · 先得2分 / 轮到你发球 · 3秒 · 最长存活 24.6s` (waiting → `对手发球`)

**FFA active** (hero = players live + your PB survival; no dense name rows)
```
<align=center><size=78%><color=#8B95A6>LAST ONE STANDING</color></size>
<size=220%><b><color=#E7ECF3>3</color></b></size>
<size=82%><color=#8B95A6>PLAYERS LIVE</color></size>
<size=92%><color=#4FCBFF>[◆◆◆◆</color><color=#FFD24D>◆◆◆</color><color=#232A37>◇]</color></size>
<size=76%><color=#8B95A6>PB survival</color> <b><color=#E7ECF3>24.6s</color></b></size></align>
```
CN: `坚持到最后 / 3 / 仍在场 / 最长存活 24.6s`

**Dropout** (neutral)
```
<align=center><size=145%><b><color=#4FCBFF>DUEL ENDED</color></b></size>
<size=82%><color=#8B95A6>Opponent left · no result recorded</color></size>
<size=74%><color=#A8B0BC>Solo ready</color></size></align>
```
CN: `对决结束 / 对手已离开 · 本局不计结果 / 单人模式已就绪`

**Round handoff** (adopt this wording house-wide)
```
<align=center><size=155%><b><color=#5BFF80>ROUND STARTING</color></b></size>
<color=#232A37>———————————————</color>
<size=88%><color=#E7ECF3>Warmup closed cleanly</color></size>
<size=74%><color=#8B95A6>Round start does not count as a hit</color></size></align>
```
CN: `回合即将开始 / 热身已正常结束 / 回合开始不计为被击中`

**HOT — top-band overlay** (the only red; layered onto the running state; labeled)
```
<align=center><size=78%><color=#8B95A6>DODGE STREAK</color>　<b><color=#FF5555>HOT</color></b></size>
<size=245%><b><color=#E7ECF3>15</color></b></size>
<size=92%><color=#4FCBFF>[◆◆◆◆</color><color=#FFD24D>◆◆◆</color><color=#FF5555>◆]</color></size>
<size=82%><color=#8B95A6>BEST</color> <b><color=#E7ECF3>17</color></b>　<color=#8B95A6>▼ 2 to beat</color>　<color=#8B95A6>PERFECT</color> <b><color=#E7ECF3>5</color></b></size></align>
```
CN label: `高危`

---

## 4. SPEC C — PARKOUR SPEEDRUN · "Pulse Line" / 脉冲路线

> **Implemented compact pass (2026-08-12).** The live product currently uses one folded 17-landing route in the
> otherwise-empty far-left wing of the widened Aim hall, isolated by a full-height divider. It implements the start
> hold/countdown, ordered swept-segment gates, continuous timer, four-sector spine, floor recovery to the last landing,
> in-warmup PB, reusable RESET coin, exclusive ActivityManager session, and synchronous teardown described below.
> The two mirrored lanes, tier selection, persistent PB ghost, and 3–4-player heat queue remain deferred.

### 4.1 Core loop

Step onto the start plate (0.6 s arms the lane, quiet 3-2-1, timer starts on the authoritative GO timestamp). Run one folded four-sector loop with finish beside start for instant rematches. Any real mistake = grab the recessed **RESET coin** and re-run. First clear ≈ 35–45 s, optimized ≈ 25–32 s.

### 4.2 Course / sections / tiers

Four sectors around a central timing mast, finish beside start:
1. **TAKEOFF / 起跳** — two broad offset landings teach that the lit edge is the takeoff cue and air-steering is allowed; first checkpoint inside ~10 s.
2. **SWITCHBACK / 折返** — an S-turn: commit a direction, steer the landing without stopping.
3. **COMMIT / 衔接** — two linked jumps reward carried speed; broad safe outer line, inner gold cut via alignment not extra distance.
4. **HOME / 收线** — a descending momentum chain into a wide finish plane, so the last emotion is flow, not a precision cliff.

**Tiers on one route:** **LEARN / 熟悉** (all-safe teal line) · **RACE / 竞速** (mix cuts) · **MASTER / 精通** (all gold cuts clean). All are valid PB routes, so a new player finishes immediately and replaces one safe segment at a time.

### 4.3 No-crouch-jump air-control movement

Depth comes from **normal jump timing, momentum preservation, and mid-air strafe/air-steering — all rewarded.** **Crouch-jump / crouch-boost is explicitly excluded and never required.** Enforce by **geometry + tuning** (no detection, no record split): safe gaps use ~65–75 % of a live-measured reliable jump; flow gaps 75–85 %; gold cuts stay inside the standard envelope but demand a sharper line, a shorter setup, or an air-steered landing. **No head-height windows, mantle checks, maximum-range make-or-die gaps, sub-footprint pads, or any cut where crouch-boost yields a materially faster record.** Acceptance test: every required line and every MASTER cut must be reproducible with ordinary Tutorial movement + normal jump + air steering only.

### 4.4 Timer + splits + personal-best ghost

Monotonic server time, tenths live, exact ms frozen at each ordered gate and finish (~1.2 s freeze). One ordered checkpoint per sector; **recovery keeps the clock running, adds no penalty, and a fall-recovered run may still PB** because its real time already includes the loss. Crossing finish without all gates in order is invalid. **PB ghost = one colliderless, lightless teal pace coin** replaying the stored PB path (~8 Hz interpolated), fading across recovery discontinuities, hidden in synchronized duo heats. Compare against the **exact stored PB run's interpolated checkpoints**, never sum-of-best.

### 4.5 Spatial layout

Two mirrored non-overlapping lanes (finish beside start), the four-node timing mast mirroring the HUD spine, the teal floor seam tracing the route (gold only on genuine cuts), cream landing faces visible before every takeoff, recessed RESET alcoves (can't be a shortcut), two NEXT HEAT rim pads for 3–4 players.

### 4.6 Tech / perf

Cheapest lane — static primitives + gate triggers + one optional pace coin, no AI. **Gate validation via swept movement segments** (store previous/current server position, intersect the segment with the next ordered gate) so a fast air-strafe can't slip a thin checkpoint. **Per-activity safe/recovery volumes, not the shipped 30 m `MaintainPlayers` gallery radius** (which would snap runners home). Recovery teleport is authoritative, generation-guarded, resets the position sampler, and grants a short no-trigger grace so it can't immediately bank/invalidate a gate. RESET coin has ownership + debounce. Spine repaint is event-driven (splits/finish/PB); hero at ~5 Hz tenths with diff-skip; mast lights via the HDR-emissive trick; **the pace coin is optional and off by default until profiling** justifies the replicated transform.

### 4.7 Interruption / reset

Round start freezes timestamps, blocks record writes, clears the three HSM IDs, hides the coin, disables triggers — idempotent across round start, disable, map reset, repeated leaves. **Round start is a graceful CLOSE, never a DNF;** completed PBs stay saved; a partial attempt never replaces a full-course PB.

### 4.8 HUD — every state

**Signature Split Spine:** `01━━[02]━━03━━04━━FIN` — future track `#232A37` ghosted, live node teal bracket `[02]`, completed-ahead `#5BFF80`, completed-behind muted `#8B95A6`, first-run/no-PB completed white `#E7ECF3`. Fill length = progress, fill color = pace-vs-self, bracket = position.

**Idle** — teaches the teal/gold color language
```
<align=center><size=165%><b><color=#4FCBFF>PULSE LINE</color></b></size>
<size=76%><color=#8B95A6>PARKOUR · LANE OPEN</color></size>
<color=#232A37>———————————————</color>
<size=92%><color=#E7ECF3>Step onto the start plate</color></size>
<size=72%><color=#4FCBFF>teal</color><color=#A8B0BC> runs the route · </color><color=#FFD24D>gold</color><color=#A8B0BC> is a faster cut</color></size></align>
```
CN: `脉冲路线 / 跑酷竞速 · 赛道空闲 / 踏上起点板即可开始 / 青色是路线 · 金色是更快的捷径`

**Tier select**
```
<align=center><size=76%><color=#8B95A6>CHOOSE YOUR LINE</color></size>
<size=96%><b><color=#66CCFF>[LEARN]</color></b><color=#5B6270>　·　</color><color=#4FCBFF>RACE</color><color=#5B6270>　·　</color><color=#FFD24D>MASTER</color></size>
<size=72%><color=#A8B0BC>Safe route · mix cuts freely · all cuts, no falls</color></size></align>
```
CN: `选择你的路线 / [熟悉] · 竞速 · 精通 / 安全路线 · 自由混用捷径 · 全捷径不失误`

**Countdown → GO** (gold digits flip to teal GO)
```
<align=center><size=76%><color=#8B95A6>RACE · LINE ARMED</color></size>
<size=195%><b><color=#FFD24D>3</color></b></size>
<size=76%><color=#E7ECF3>Gate opens on GO</color></size></align>
```
CN: `竞速 · 赛道已就绪 / 3 / 喊"开始"时闸门开启 → 开始 · 计时开始`

**Active — with PB** (clock tenths; behind-PB variant recolors fill + chip muted, `▼ 0.31s to beat`)
```
<align=center><size=72%><color=#8B95A6>SECTOR 2 / 4 · SWITCHBACK</color></size>
<size=190%><b><color=#FFD24D>12.6</color></b></size>
<size=86%><color=#5BFF80>01━━</color><b><color=#4FCBFF>[02]</color></b><color=#232A37>━━03━━04━━FIN</color></size>
<size=80%><color=#8B95A6>PB 31.904</color><color=#5B6270>　│　</color><b><color=#5BFF80>▲ 0.31s</color></b></size>
<size=70%><color=#A8B0BC>Air-steer to the lit edge · grab RESET to restart</color></size></align>
```
CN: `第 2 / 4 段 · 折返 / 12.6 / … / 最佳 31.904 │ ▲ 0.31秒 / 空中转向亮起的边缘 · 抓取重置硬币重跑`

**Active — first run (no PB)** — completed fill is WHITE (nothing to be ahead/behind of), compares to a labeled benchmark
```
… <size=86%><color=#E7ECF3>01━━</color><b><color=#4FCBFF>[02]</color></b><color=#232A37>━━03━━04━━FIN</color></size>
<size=80%><color=#8B95A6>NO PB YET</color><color=#5B6270>　│　</color><color=#A8B0BC>benchmark 34.8</color></size>
<size=70%><color=#A8B0BC>First run sets your record · just reach FIN</color></size>
```
CN: `暂无最佳 │ 基准 34.8 / 首跑即建立记录 · 到达终点即可`

**Valid checkpoint split** (~1.2 s; node turns green or muted `▼`)
```
<align=center><size=120%><b><color=#4FCBFF>CHECKPOINT 2</color></b></size>
<size=86%><color=#5BFF80>01━━02</color><color=#232A37>━━03━━04━━FIN</color></size>
<size=84%><b><color=#5BFF80>▲ 0.31s ahead</color></b></size></align>
```
CN: `检查点 2 / ▲ 领先 0.31秒` (behind → `▼ 落后 0.31秒`)

**Recoverable fall** (not a fail; white, section-specific coaching)
```
<align=center><size=120%><b><color=#4FCBFF>CHECKPOINT 2</color></b></size>
<size=84%><color=#E7ECF3>Recovered · the clock is still running</color></size>
<size=72%><color=#A8B0BC>Turn after takeoff</color></size></align>
```
CN: `检查点 2 / 已返回 · 计时仍在继续 / 起跳后再转向`

**Success — first record**
```
<align=center><size=80%><b><color=#5BFF80>FIRST RECORD SAVED</color></b></size>
<size=190%><b><color=#FFD24D>34.821</color></b></size>
<size=88%><color=#5BFF80>01━━02━━03━━04━━FIN</color></size>
<size=72%><color=#A8B0BC>Your pace coin is ready · step in to race it</color></size></align>
```
CN: `首个记录已保存 / 34.821 / 你的节奏球已就绪 · 踏入与它竞速`

**Success — finish, no PB** — spine WHITE (valid & saved, not a beat); gray `▼ to beat`
```
<align=center><size=80%><b><color=#4FCBFF>FINISH</color></b></size>
<size=190%><b><color=#FFD24D>34.821</color></b></size>
<size=88%><color=#E7ECF3>01━━02━━03━━04━━FIN</color></size>
<color=#232A37>———————————————</color>
<size=82%><b><color=#E7ECF3>RUN SAVED</color></b><color=#5B6270>　·　</color><color=#8B95A6>▼ 1.204s to beat</color></size></align>
```
CN: `到达终点 / 34.821 / 成绩已保存 · ▼ 还差 1.204秒`

**Personal-best beat** — the one card where green saturates (heading + full spine + delta) + one green world pulse; hero stays gold (clock rule)
```
<align=center><size=105%><b><color=#5BFF80>NEW PERSONAL BEST</color></b></size>
<size=195%><b><color=#FFD24D>31.904</color></b></size>
<size=88%><color=#5BFF80>01━━02━━03━━04━━FIN</color></size>
<size=88%><b><color=#5BFF80>▲ 1.713s faster</color></b></size>
<size=70%><color=#A8B0BC>Pace coin updated · run it back</color></size></align>
```
CN: `刷新个人最佳 / 31.904 / ▲ 快 1.713秒 / 节奏球已更新 · 再跑一次`

**Fail / invalid** — the broken-spine diagnostic (green to last banked gate, gold missed gate, `╌╌` severed track); white heading, never red
```
<align=center><size=130%><b><color=#E7ECF3>RUN NOT SAVED</color></b></size>
<size=86%><color=#5BFF80>01━━02━━</color><color=#FFD24D>03</color><color=#232A37>╌╌04╌╌FIN</color></size>
<color=#232A37>———————————————</color>
<size=90%><color=#FFD24D>Checkpoint 3 was missed</color></size>
<size=72%><color=#A8B0BC>Back to start · cross the gates in order</color></size></align>
```
CN: `本次未保存 / 错过检查点 3 / 返回起点 · 请按顺序通过闸门` (variants: `离开赛道` / `已达 90 秒上限`)

**Duo staging** (YOU teal, RIVAL white; opt-in only when both plates armed)
```
<align=center><size=76%><color=#8B95A6>RACE READY · MIRRORED LANES</color></size>
<size=120%><b><color=#4FCBFF>YOU</color><color=#5B6270>　vs　</color><color=#E7ECF3>RIVAL</color></b></size>
<size=72%><color=#A8B0BC>Both plates armed · countdown starts together</color></size></align>
```
CN: `竞速就绪 · 镜像赛道 / 你 对 对手 / 两块起点板已就绪 · 一同倒计时`

**Duo active split** (PB stays LEFT/primary; rival gap is a right-side add at split events; pace ghosts hidden)
```
<size=80%><color=#8B95A6>PB </color><b><color=#5BFF80>▲ 0.31s</color></b><color=#5B6270>　│　</color><color=#8B95A6>RIVAL </color><b><color=#5BFF80>▲ 0.24s</color></b></size>
```
CN: `最佳 ▲ 0.31秒 │ 对手 ▲ 0.24秒`

**Duo dropout** — only the rivalry layer drops; no forfeit
```
<align=center><size=118%><b><color=#E7ECF3>RIVAL LEFT</color></b></size>
<size=76%><color=#A8B0BC>Your run continues · still PB-eligible</color></size></align>
```
CN: `对手已离开 / 你的挑战继续 · 仍可刷新最佳`

**Queue — next heat** (states the escape hatch: queueing never traps you)
```
<align=center><size=88%><b><color=#4FCBFF>NEXT HEAT · 1 / 2</color></b></size>
<size=72%><color=#A8B0BC>A free lane can still be run solo now</color></size></align>
```
CN: `下一组 · 1 / 2 / 有空闲赛道时仍可立即单人跑`

**Round handoff** — spine freezes green through wherever you reached, dims the rest
```
<align=center><size=135%><b><color=#5BFF80>ROUND STARTING</color></b></size>
<size=86%><color=#5BFF80>01━━02━━</color><color=#232A37>03━━04━━FIN</color></size>
<color=#232A37>———————————————</color>
<size=90%><color=#E7ECF3>Run closed at checkpoint 2</color></size>
<size=72%><color=#A8B0BC>No loss recorded · joining the round</color></size></align>
```
CN: `回合即将开始 / 挑战已在检查点 2 结束 / 本次不记失败 · 正在进入回合`

---

## 5. DECISIONS FOR YOU

*(Every open choice, deduped, grouped. Recommended default marked ✅.)*

### 5.1 Shared / suite-wide

**S1 — Suite signature identity.** ✅ One horizontal STATE LINE per lane (Rail/Halo/Spine as one family) · (b) each activity picks its own hero · (c) shared big-number+gradient. *Why: the segmented line emerged independently in all three and is the memorable, non-templated element.*

**S2 — "You are here" device.** ✅ Reuse the shipped `[X]` bracket everywhere; reserve `◆`/`◇` for you-vs-rival only · (b) diamonds everywhere · (c) per-lane invention. *Why: the bracket already ships and needs no validation.*

**S3 — Glyph validation.** ✅ One shared validation gate + registered ASCII fallbacks before any build · (b) validate per-activity · (c) trust the "proven" claims. *Why: `◆ ◇ │ ━ ╌ ▲ ▼` are absent from shipped code; one gate stops three tofu bugs.*

**S4 — HUD architecture.** ✅ Three stable HSM IDs/lane, hero+footer on change-skip, flash on a new force-show path · (b) one combined block re-sent each tick · (c) add a RueI corner dashboard. *Why: reuses the shipped provider + `_lastStatusText`; flash force-show is the one new piece.*

**S5 — Live cadence.** ✅ 4–5 Hz composite default, 10 Hz only after profiling, never global `ForceFastUpdates` · (b) 10 Hz always · (c) 2 Hz. *Why: best feel-per-byte across simultaneous lanes.*

**S6 — Draft-panel coexistence.** ✅ Collapse the draft panel to a one-line bottom strip while in a lane · (b) keep full panel at Y=840, squeeze the lane HUD above · (c) hide the panel entirely. *Why: players keep picking their SCP without the ~250 px panel colliding with the footer.*

**S7 — Persistent-state medium.** ✅ World objects carry state, hints carry only numbers/verdicts · (b) all state in text · (c) all state in world. *Why: matches the shipped split and is what makes the dual-surface Halo work.*

**S8 — World glow / light budget.** ✅ HDR-emissive cells + a few shared bloom lights (the shipped logo trick), hard cap on real point lights/lane · (b) one real light per cell · (c) HUD-only, no glow. *Why: literal per-cell lights blow SCP:SL's real-time light limit.*

**S9 — Red-usage law.** ✅ Red only for imminent urgency (final ~5 s timer + one lamp, or top danger cell), always labeled, never failure; parkour may use zero · (b) red = any bad event · (c) no red anywhere. *Why: scarcity is what makes red mean "urgent."*

**S10 — PB-beat vs gold=time.** ✅ PB = green heading + line saturates green + one world pulse, always; hero number turns `#66FF66` only when the hero is a score · (b) always `#66FF66` even on the clock · (c) green heading only. *Why: keeps both inviolable rules (gold=time, `#66FF66`=PB) from colliding on the parkour clock.*

**S11 — Pace/PB source.** ✅ Interpolated PB checkpoints, benchmark as pre-PB fallback · (b) final-score projection · (c) always the fixed tier target. *Why: a projection can show "ahead" while you pace behind, then snap — the exact failure the line exists to prevent.*

**S12 — Entry / retry idiom.** ✅ Step onto a lit plate to enter, grab a RESET coin to retry · (b) shoot a plate to select/retry · (c) mixed per activity. *Why: both are proven idioms; "shoot to select" needs an unbuilt firearm-hit path.*

**S13 — Localization.** ✅ Single server-wide `Config.Language` + per-string pairs · (b) per-player locale detection · (c) stacked EN+CN. *Why: matches the shipped convention; stacking doubles hint height.*

**S14 — Round-handoff visibility.** ✅ Best-effort pre-handoff card off the countdown threshold; never delay cleanup/assignment · (b) delay hint removal to guarantee it · (c) no card. *Why: the real hook clears hints synchronously; correctness must win.*

**S15 — Seed determinism vs freshness.** ✅ Rotating pool of ~5 authored seeds per tier, PB stored per-seed · (b) fixed seed per tier · (c) fully random each run. *Why: fresh at rep 40 *and* comparable PBs; matters most for Aim (memorization bites hardest).*

**S16 — PB persistence.** ✅ Persistent SteamID-keyed, versioned, completed-runs-only, fail-safe to session-only if storage is missing · (b) session-only · (c) external global DB. *Why: "beat your best from last week" is the strongest hook; every HUD is built around a durable ghost.*

**S17 — Cosmetic reward scope.** ✅ In-warmup-only cosmetics (seam tint, name chip, halo/coin skin, plaque) with zero real-round effect · (b) session-only pulse/title · (c) warmup-wide gallery title. *Why: lasting reward with guaranteed zero real-round information or advantage.*

**S18 — Run-length weighting.** ✅ Make the 15–25 s loop the star; long ranked runs are a bonus gated by remaining time · (b) 75 s / MASTER as marquee · (c) uniform ~45 s. *Why: the short loop is what players actually get most sessions and maximizes reps.*

**S19 — Subsystem shape.** ✅ One `ActivityManager` on `SelectorController`: exclusive per-player state, generation tokens everywhere, one non-throwing `StopForRoundStart` · (b) three independent event subscribers · (c) three separate plugins. *Why: one critical-path teardown before Tutorial→None is far safer than three.*

**S20 — Simultaneous capacity.** ✅ 4 lean Aim lanes / 1 shared 018 arena (≤4) / 2 mirrored Parkour lanes + queue · (b) 2 per activity · (c) 4 full instances each. *Why: supports natural 3–4 play without multiplying the expensive arena/course worlds.*

**S21 — Failure feedback strength.** ✅ Keep zero-red but give every fail/hit a strong non-red punch (size spike + one world flash + audio) · (b) strict muted-gray purity · (c) reintroduce red for hits. *Why: a repeat player must never wonder whether the run ended, without spending reserved red.*

### 5.2 Aim Range

**A1 — Default heat length.** ✅ SNAP 20 s default, STANDARD 45 s / BENCHMARK 75 s when time allows · (b) 45 s default · (c) one dynamic always-unranked duration. *Why: 20 s reliably fits SCP:SL's uncertain wait and drives retries.*
**A2 — Drill roster.** ✅ FIRST SHOT, STRAFE LINE, TRACK HOLD, PEEK CLOCK, SWITCHBOARD + MIXED SIGNAL · (b) three drills + MIX · (c) rotating daily three. *Why: covers the skill families without exposing the 32-pattern catalog.*
**A3 — Difficulty model.** ✅ Three fixed tiers OPEN/FOCUS/EDGE + separate adaptive PRACTICE flag · (b) one universal tier · (c) fully adaptive. *Why: fixed tiers keep PBs/races fair; adaptive practice stays uncontaminating.*
**A4 — Weapon presets.** ✅ Discipline-specific fixed presets · (b) one universal auto · (c) player-chosen with per-weapon PBs. *Why: preserves the intended motor skill without fragmenting records.*
**A5 — Score model.** ✅ Transparent event points (+100/reaction/−25/−150; track = hit+continuity) · (b) clears→accuracy→time with no formula · (c) 0–100 composite. *Why: explicit points read during a race and still expose diagnostics on recap.*
**A6 — Race format.** ✅ Mirrored lanes, same seed, synced timestamps · (b) shared target field · (c) alternating relay. *Why: eliminates target stealing and makes dropout/scoring trivial to reason about.*
**A7 — Late-join.** ✅ Start solo immediately or reserve the next 20 s race · (b) join current heat unranked · (c) wait for the heat. *Why: drop-in safe without corrupting an active comparison.*
**A8 — Target construction (superseded by the approved full-range implementation).** ✅ Three persistent native `ShootingTargetToy` targets move directly on parallel absolute-time tracks, remain alive, send native hitmarkers on credited hits, and expose raw client smoothing `60` with 15 Hz network keyframes. The separate Aim-Lab lane uses collidable spheres that pop once per generation, send the same hitmarker feedback, and respawn deterministically. Native RA dummies are intentional in the bot bay, owned by slot + live identity + generation, walk through direct nearby `FpcMotor.ReceivedPosition` steps with no waypoint binding, pause patrol while aiming, and retaliate only through verified native firearm actions plus observed ammo consumption; there is no synthetic damage/fire fallback. *Why: current native capability checks, one clock domain, and generation-owned teardown provide reliable attribution without leaking dummies or range hazards into the live round.*
**A9 — Precision bonus.** ✅ +20 head/center only after verifying hit-location for the dummy+weapon; else flat · (b) reward by damage amount · (c) never distinguish. *Why: a trustworthy simple score beats a rich one on ambiguous data.*
**A10 — Rail marker glyphs.** ✅ `◆` you / `◇` rival / `│` PB, validated + fallbacks · (b) `●`/`○`/`|` · (c) `▲`/`△`/`│`. *Why: filled-vs-hollow diamonds survive distance/colorblindness; dots read as room lights.*

### 5.3 SCP-018 Dodgeball

**D1 — Signature surface.** ✅ Mirror the Halo on both world rim ring + TMP strip · (b) world ring only · (c) TMP only. *Why: peripheral read while dodging + central read on recap; the world ring holds state so the hint stays numbers-only.*
**D2 — Halo encoding.** ✅ Fill count primary + 3-band color redundant · (b) per-cell gradient · (c) single-color count only. *Why: count survives colorblindness/peripheral vision.*
**D3 — Default solo route.** ✅ Pattern Run as the large entrance pad **with** Endless Streak beside it as the headline replay loop · (b) Streak only · (c) auto-alternate. *Why: a finite pattern teaches the room; endless Streak is the "one more go" engine.*
**D4 — Multiplayer interaction depth.** ✅ Shared-ball-volume Duel (positioning matters, rebounds punish the thrower) + neutral-launcher FFA · (b) fully neutral all modes · (c) fully interactive FFA. *Why: gives the 2-player case real tension while keeping 3–4 fair and drop-in safe.*
**D5 — Arena plan.** ✅ 12×10 m chamfered rectangle · (b) regular octagon · (c) long split court. *Why: learnable banks + a clear duel axis, no ball-trapping 90° corners.*
**D6 — Danger cap.** ✅ Measured-velocity cap preserving a reaction window + 12–15 s rally cap · (b) fixed six-bounce cap · (c) lifetime only. *Why: measured velocity encodes the true threat regardless of collision.*
**D7 — Active ball count.** ✅ Exactly one · (b) add a second in FFA sudden death · (c) one per two players. *Why: one accelerating ball is tenser and far easier to attribute and clean up.*
**D8 — Comeback rule.** ✅ Loser serves next (1v1); all return next FFA set · (b) one protected respawn · (c) catch-revive · (d) none. *Why: visible initiative restoration that never secretly changes stats.*
**D9 — Duel length.** ✅ First to 2, 30 s cap · (b) single-hit · (c) first to 3 · (d) continuous. *Why: allows comeback but resolves before anyone waits.*
**D10 — FFA elimination.** ✅ No revives, rejoin next set (10–20 s) · (b) one auto second life · (c) catch-revive · (d) lives stock. *Why: clean single elimination keeps placement legible.*
**D11 — Perfect-dodge threshold.** ✅ ~0.7 m closest approach, telemetry-tuned · (b) fixed 0.5 m · (c) velocity-scaled · (d) omit. *Why: a generous but unsafe-looking near miss is a second mastery goal without affecting survival.*
**D12 — World ring implementation.** ✅ 8 tiles + one shared light · (b) 8 tiles, no light · (c) 8 independent lights. *Why: the fill-count signature survives without 8 dynamic lights.*
**D13 — Contact resolution.** ✅ Own + tag + disarm + destroy the ball, cancel real damage, resolve one logical hit, ignore late callbacks · (b) rely on native damage. *Why: native grace/velocity/armor make outcomes inconsistent and can leak into the round.*

### 5.4 Parkour Speedrun

**P1 — Course packaging.** ✅ One four-sector folded loop with safe teal lines + optional gold cuts · (b) three separate courses · (c) rotating short courses. *Why: visible progression, compact footprint, one PB ecosystem, no randomization.*
**P2 — Recovery / record validity.** ✅ Checkpoint recovery keeps the clock; finish may still PB · (b) any fall → practice-only · (c) fixed time penalty. *Why: elapsed time already prices the mistake, preserving flow.*
**P3 — Crouch-tech exclusion.** ✅ Geometry + tuning so it gives no advantage · (b) detect and invalidate · (c) separate record categories. *Why: honors the movement kit without policing an input or splitting records.*
**P4 — Personal ghost form.** ✅ One colliderless teal pace coin (~8 Hz), gated behind a config flag, off by default until profiling; Spine + mast are the primary ghost · (b) split lights only · (c) sampled marker ribbon. *Why: communicates line + pace far more cheaply than a dummy/trail, without paying replicated-transform cost unproven.*
**P5 — Duel capacity.** ✅ Two mirrored lanes + a NEXT HEAT queue for 3–4 · (b) four always-live lanes · (c) one shared non-collision lane. *Why: a true 2-player race that handles 3–4 while bounding primitives.*
**P6 — Ghosts during head-to-head.** ✅ Hide PB ghosts in synced races, keep PB deltas in HUD · (b) show each own ghost · (c) show only the faster ghost. *Why: the real opponent supplies motion; four moving figures would clutter two lanes.*
**P7 — Difficulty labels.** ✅ LEARN / RACE / MASTER on one route · (b) Bronze/Silver/Gold · (c) PB only. *Why: labels describe player behavior and route mastery, PB stays first.*
**P8 — Benchmark tuning.** ✅ Live human percentiles + course-version bump · (b) tool-assisted ideal × multipliers · (c) hand-authored by feel. *Why: real runs include SCP:SL acceleration, air control, tick, and latency.*
**P9 — Fail-state spine.** ✅ Broken-spine diagnostic (green to last gate, gold missed gate, `╌╌`) · (b) divider + text cause · (c) full gray spine. *Why: turns the signature into the in-place explanation, with gold not red.*
**P10 — Live precision.** ✅ Tenths live, exact ms frozen on events · (b) hundredths live · (c) whole seconds. *Why: responsive without flooding HSM; the precision jump itself signals "authoritative."*
**P11 — Gate validation.** ✅ Swept movement-segment intersection · (b) point-in-trigger only. *Why: a fast air-strafe can cross a thin checkpoint between samples and be falsely invalidated.*
**P12 — Safe-volume model.** ✅ Per-activity safe/recovery volumes · (b) keep the 30 m gallery-radius `MaintainPlayers` rule. *Why: the shipped rule snaps runners back to the gallery if the course extends past 30 m.*
**P13 — Terminus localization.** ✅ Localize `FIN → 终点`, keep ASCII node numbers `01–04` both languages · (b) keep "FIN" literally · (c) localize node numbers. *Why: numbers keep the spine width identical; `终点` reads naturally.*

---

## 6. Build order + the one thing to validate

**Prototype first: SCP-018 Dodgeball (Solo Dodge Streak).** It has the best moment-to-moment feel, is the hardest to memorize (real physics self-refreshes), uses a genuine base-game item (no custom projectile, no firearm-hit path, no navmesh), and exercises the two riskiest shared systems at once — the **dual-surface world-ring ↔ TMP-strip mirror** (proves the "state in world, numbers in text" architecture and the HDR-emissive light trick) and the **generation-token teardown** (proves a lethal base-game hazard can be owned, disarmed, and destroyed before the round-start handoff).

Then **Parkour** (deepest longevity via cuts/tiers/splits, lowest native-fit risk — only movement, no kit granting or hit-detection), then **Aim Range** last (needs the most content plus the seed-rotation fix and the target-construction spike to survive 50 reps).

Before any of it, land two one-time shared prerequisites in parallel: extend `HsmHintDisplayProvider` to the three-ID + flash-force-show model, and clear the **glyph reality gate** in `tools/preview/banner.html`.

**The single thing to validate before building anything on top of it:** that **round start reliably and idempotently destroys/disarms every hazard — the SCP-018, any granted weapon and dropped pickup, all activity HSM IDs, and every pooled toy/light — inside the pre-assignment path, before Tutorial players are flipped to `None`, from any activity phase, with zero leak into the live round and zero exception escaping the core hook.** Everything else is cosmetic; this is the one correctness invariant the whole suite rests on. Validate it on a live server with the detailed gallery enabled and ~10 concurrent participants (measure server frame time, outbound traffic, HSM updates/s, spawned `NetworkIdentity`/`AdminToy` and active `LightSourceToy` counts, and cleanup duration inside the handoff) — headless planner/model tests cannot cover these runtime costs.
---

## 7. Owner decisions (locked 2026-07-23)

These override the recommended defaults where they differ.

1. **Build order → AIM RANGE FIRST** (overrides §6, which recommended Dodgeball-first for tech de-risking). Rationale: Aim is the primary activity. Implication — the shared prerequisites move to the very front: extend `HsmHintDisplayProvider` to the three-ID + flash-force-show model, clear the glyph reality gate in `tools/preview/banner.html`, then run the **target-construction spike** (native `ShootingTarget` first; pooled-primitive + authoritative-ray fallback) and the **seed-rotation** design before scoring is built. The one must-validate invariant (idempotent hazard teardown before the Tutorial→None handoff) still applies — here the "hazard" to prove cleanup on is the **granted weapon + dropped ammo/pickups**, not the SCP-018.
2. **PB storage → persistent per-SteamID** (versioned, completed-runs-only, fail-safe to session-only if storage is missing).
3. **Dodgeball duel → shared-ball duel** (positioning matters; a reckless throw can rebound onto the thrower).
4. **Rewards → in-warmup cosmetics** (seam tint, name chip, halo/coin skin, plaque; zero real-round effect).

---

## 8. SPEC D — JAILBIRD DUEL · "Break Bar" / 囚鸟对决  *(added by owner request 2026-07-23)*

The dedicated **1v1 lane** — the suite's only explicitly-two-player activity. Everything else is solo-first; this is where a second player becomes a *rival*, not just a co-occupant. Ships **Jailbird-first**, with the weapon slot designed as a rotating duel loadout later.

### 8.1 Core loop
Two players step onto the two facing duel plates; when both are down, a mutual ready pulse and a gold 3-2-1. Best-of-3 melee bout: identical activity-owned Jailbirds, full HP, opposite corners, spawn-shielded until GO. A round ends on a downing blow; ~1.5 s reset heals both, refills charges, repositions; match ends at 2 round-wins with an instant **REMATCH** offer. A lone player sees a "waiting for a challenger" invitation and may warm up on a **Break Dummy** (a swinging practice pell that teaches jailbird spacing and charge timing) until a second player arrives.

### 8.2 The weapon (Jailbird-first)
Both duelists get an **activity-owned Jailbird**. **The charged (sprint) swing is disabled** — no charge burst and no forward flash — so the duel is **light-swing only**: a pure contest of spacing, timing, and footwork with no charge-spam and no blind. (Durability is a non-issue with charges off; a swing never breaks the weapon.) All PvP damage is intercepted (see 8.6): a lethal blow **resolves the round** instead of killing — the loser is left at 1 HP, "downed," then both are fully healed on reset. No real death, no spectator, nobody leaves warmup. The weapon slot is a config so future loadouts (e.g. a disarmed-melee set) can rotate without redesign.

### 8.3 Solo / 2 / 3–4
- **Solo:** honest "waiting for a challenger" invite + optional Break Dummy practice (swing timing and spacing, cosmetic hit-feel only, no record).
- **2 (the mode):** the full best-of-3 duel.
- **3–4:** a **NEXT UP** rim queue — winner stays (capped, e.g. 2 defenses then rotate to keep it fair), losers re-queue; or opt into a fresh bout. Nobody is trapped: a waiting player can always leave to another lane.

### 8.4 Scoring & records
Duel outcomes are opponent-dependent, so — consistent with the dodgeball duel/FFA rule — they stay **ephemeral** (session win/loss, current streak), never a persistent SteamID PB (which would be unfair to seed against). Soft cosmetic ticks only: a session **win streak** and an optional **clean sweep** (2–0, no HP lost). Losing is never punished beyond the bout and never touches the real round.

### 8.5 Arena
Small symmetric ring (~8 × 8 m), low navy walls with a `#3CE2E7` center seam, two cream spawn pads at opposite ends, spawn-shield posts that drop on GO, a rim gallery outside the fight volume for the queue. Fully contained; near-white key light so both fighters read clearly. No cover clutter — this is a clean dueling floor, not an obstacle arena.

### 8.6 Tech / cleanup (P0 — it grants a real weapon)
Grant the Jailbird activity-owned with the **charged/sprint attack disabled**; **cancel all real damage/effects outside the resolve path**; intercept lethal to resolve rounds; **own + destroy both Jailbirds and clear any applied effects** on every exit, leave, reset, disable, and round-start — the same weapon-teardown invariant as the Aim Range (a later role change is not sufficient cleanup for a networked weapon/pickup). Everything generation-guarded so a late hit cannot score into the next round or leak into the live round.

### 8.7 Interruption / reset
Round start freezes the bout, resolves it as **INTERRUPTED (not a loss)**, heals both, destroys weapons, clears the three HSM IDs, despawns the arena, then lets the Tutorial→None handoff proceed. Handoff card is green, "does not count as a loss."

### 8.8 HUD — signature "Break Bar"
A center-anchored horizontal state line: round pips flank a live HP-tug bar that meets wherever momentum currently sits — `◆` you (teal) pushing from the left, `◇` rival (white) from the right. Round score is the hero; timer gold; a round win is green, a round loss is **muted/white, never red** (red stays reserved for the final-seconds timer only).

**Waiting for challenger (solo)**
```
<align=center><size=75%><color=#8B95A6>JAILBIRD DUEL · RING OPEN</color></size>
<size=150%><b><color=#4FCBFF>WAITING FOR A CHALLENGER</color></b></size>
<size=78%><color=#A8B0BC>Step on to warm up on the dummy · a rival can join anytime</color></size></align>
```
CN: `囚鸟对决 · 擂台空闲 / 等待挑战者 / 踏上练习桩热身 · 对手可随时加入`

**Staging / ready (both present)**
```
<align=center><size=76%><color=#8B95A6>BEST OF 3 · JAILBIRD</color></size>
<size=120%><b><color=#4FCBFF>YOU</color> <color=#5B6270>vs</color> <color=#E7ECF3>RIVAL</color></b></size>
<size=78%><color=#A8B0BC>Both on the pads · duel begins on GO</color></size></align>
```
CN: `三局两胜 · 囚鸟 / 你 vs 对手 / 双方就位 · 喊开始即对决`

**Active bout** (hero round score; Break Bar shows live HP tug; gold timer)
```
<align=center><size=76%><color=#8B95A6>ROUND 2 · BEST OF 3</color></size>
<size=200%><b><color=#4FCBFF>1</color> <color=#5B6270>—</color> <color=#E7ECF3>0</color></b></size>
<size=88%><color=#4FCBFF>◆●●●●●</color><color=#232A37>▐</color><color=#E7ECF3>●●●◇</color></size>
<size=80%><color=#8B95A6>Control the spacing · time your swing</color></size></align>
```
CN labels: `第 2 局 · 三局两胜` / `控制距离 · 把握出手时机`

**Round won / lost flash** (`.flash`, force-shown; win green, loss muted white — never red)
`ROUND WON` `#5BFF80` / `ROUND LOST` muted `#8B95A6` / `DOWNED` white
CN: `本局胜` / `本局负` / `被击倒`

**Match win**
```
<align=center><size=170%><b><color=#5BFF80>DUEL WON</color></b></size>
<size=210%><b><color=#E7ECF3>2</color> <color=#5B6270>—</color> <color=#E7ECF3>0</color></b></size>
<size=82%><color=#8B95A6>WIN STREAK</color> <b><color=#E7ECF3>3</color></b>　<color=#5B6270>│</color>　<color=#FFD24D>CLEAN SWEEP</color></size>
<size=74%><color=#4FCBFF>Step on to rematch</color></size></align>
```
CN: `对决胜利 / 2 — 0 / 连胜 3 │ 完胜 / 踏上擂台再战`

**Match loss** (forward-facing, non-red)
```
<align=center><size=160%><b><color=#E7ECF3>DUEL OVER</color></b></size>
<size=205%><b><color=#E7ECF3>1</color> <color=#5B6270>—</color> <color=#E7ECF3>2</color></b></size>
<size=76%><color=#A8B0BC>Good bout · step on to run it back</color></size></align>
```
CN: `对决结束 / 1 — 2 / 打得漂亮 · 踏上擂台再来一场`

**Dropout** (rival left mid-duel; no forfeit recorded)
```
<align=center><size=145%><b><color=#4FCBFF>RIVAL LEFT</color></b></size>
<size=78%><color=#8B95A6>No result recorded · dummy practice ready</color></size></align>
```
CN: `对手已离开 / 本局不计结果 · 练习桩已就绪`

**Round handoff** (green, house wording)
```
<align=center><size=155%><b><color=#5BFF80>ROUND STARTING</color></b></size>
<color=#232A37>———————————————</color>
<size=88%><color=#E7ECF3>Duel closed cleanly</color></size>
<size=74%><color=#8B95A6>Round start does not count as a loss</color></size></align>
```
CN: `回合即将开始 / 对决已正常结束 / 回合开始不计为落败`

### 8.9 Decisions for you
- **DD1 — Downing / death handling.** ✅ Intercept the lethal blow → resolve the round + heal both · (b) let HP deplete to a low "downed" threshold that ends the round · (c) real damage with a revive. *Why: guarantees no real death/spectator leak while keeping hits meaningful.*
- **DD2 — Match length.** ✅ Best of 3 · (b) first to 1 (fast, high-variance) · (c) best of 5 (can outlast the wait). *Why: allows a comeback and still resolves before anyone is left waiting.*
- **DD3 — Solo-while-waiting.** ✅ Break Dummy practice pell · (b) pure waiting invitation · (c) auto-offer a bot sparring partner. *Why: keeps a lone player busy without faking an opponent; (c) reintroduces the heavy combat-bot you are avoiding here.*
- **DD4 — Weapon scope.** ✅ Jailbird-first, weapon slot designed for a rotating duel loadout later · (b) Jailbird permanent · (c) player-chosen weapon. *Why: matches "start with jailbird" and leaves room to grow without fragmenting records (which stay ephemeral anyway).*
- **DD5 — Charged/sprint swing.** ⛔ **DISABLED (owner decision).** Duel is light-swing only — spacing, timing, footwork; no charge burst, no blind flash. *Effect: more readable and fair, lower skill-ceiling, no flash to clear on teardown.*
- **DD6 — 3–4 handling.** ✅ Winner-stays queue, capped defenses · (b) fresh bout each time · (c) 2v2. *Why: keeps the ring flowing for a small crowd without anyone stuck spectating long.*
