# WarmupScpSelector

[中文](#中文说明) | [English](#english)

A LabAPI plugin for SCP: Secret Laboratory. During waiting-for-players it builds one room holding a
model of every offered SCP, each with a big coin. Players walk up and grab a coin to pick the SCP they
want to play. When the round starts, the plugin lets the game assign roles normally, then swaps the
selected SCP slots over to the players who picked them.

> Renamed from the old "scpsl-custom-room-plugin" / `ScpslCustomRoomPlugin`. It never built custom
> rooms in the SCP-002 sense — it is a warmup SCP draft — so the name was changed to match what it does.

## English

### What it does

- While the server is **waiting for players**, everyone is moved into a floating selector room as
  `Tutorial` and shown a row of SCP models (049, 079, 096, 106, 173, 939, 3114), each with a big coin.
- **Grabbing a coin** selects that SCP (the pickup is cancelled, so the coin stays put). A hint shows
  the lobby countdown and your current pick, with a small badge for how many players picked the same SCP
  (e.g. `SCP-096　·　5 picks`) and a tiny `·N` tally on each chip in the offered-role row. You can change
  your pick any time before the round starts. (The panel is only re-sent when its text changes, so it does
  not spam the network.)
- The plugin never forces the native lobby countdown. The only exception is an ownership-safe temporary lobby lock
  while one human is using configured native bots, preventing counted dummies from falsely starting the round; it is
  released synchronously when another human joins, the last human leaves, or Aim stops.
- The default-off **Aim Range** turns the entire selector and training area into one uninterrupted full-width rectangular
  hall—there is no doorway, choke point, or second room. Six persistent firearms sit visibly on the shooting counter
  with two native attachment workstations placed symmetrically against the side walls;
  the counter wall spans nearly the entire hall. Exactly three very bright point lights illuminate each half of the hall
  (six total), using ordinary non-HDR light colors; the branded back-wall logo keeps its HDR albedo boost
  and shares the selector's center light for bloom without adding another real-time light;
  the centered 19.2 m × 22 m training area has three 6.4 m lanes: cover-backed live bots, three parallel persistent sliding
  native targets at different distances/speeds, and Aim-Lab spheres that send a hitmarker/score on a valid hit and
  immediately teleport the same always-hittable toy to the next authored point.
  Grabbing a counter gun grants a separate owned inventory copy, so the displayed gun remains visible and immediately reusable.
  Both normal targets and spheres send the shooter a native hitmarker when a valid range hit is credited; sliding
  targets use client interpolation between 15 Hz network keyframes instead of snapping between scheduler updates.
- Up to two explicitly enabled native RA bots continuously strafe through cover using small native FPC motor steps
  with no waypoint binding. Both slots use Crossvec. Bots keep moving and
  jump on bounded native-input cooldowns; a close attacker makes them jump more often. During retaliation they always
  hold native ADS and use tighter combat steps for accurate return fire. A genuine hit from a participant's owned range gun locks that first attacker after a 0.5–0.6 second reaction delay for
  12 seconds: the bot tracks an upper-chest point inside a non-head hitbox while strafing and holds native automatic fire until the lease or line of sight ends. At
  low ammo it releases fire/ADS, moves behind slot-owned 2.2 m cover, reloads there, then resumes combat. Bots die
  normally and respawn with a new owned identity and Crossvec.
- Bot shots deal real damage. A lethal hit on a range participant is cancelled before vanilla death, synchronously
  reinitializes the same player as `Tutorial`, and returns them to the range entrance without a spectator frame.
  Leaving the range or starting the round destroys every range-owned gun, pickup, target, carrier, bot, and hint.
- The former room seam is now UI-only. On the selector side, only the original SCP selection panel is shown; no Aim UI
  appears. On the training side, the SCP panel is hidden completely and only the bilingual Aim flash, hero, and footer
  are shown. Shooting, damage routing, gun ownership, and bot provocation remain active across the entire hall. The Aim HUD renders on a narrow left lane
  (`Activities.Aim.HudX`, default -1077) that sits between the native inventory list and the inventory wheel, so it
  never overlaps them while TAB is held; the original selector-side SCP panel keeps its centered layout.
- The default-off **Pulse Line parkour** uses the previously empty far-left wing behind the shooting counter. A
  full-height divider isolates it from bot fire. Step on START for 0.6 seconds, run the 3-second countdown, then clear
  17 ordered cream platforms around four folded sectors and return to FIN beside START. Thin cyan/gold route strips
  make the next line readable; swept-segment gate checks prevent fast crossings from being missed. Falling to the
  hall floor immediately returns the runner to the last completed landing while the authoritative timer keeps running.
  The reusable RESET coin restarts in place, and the compact bilingual HSM card shows timer, sector spine, progress,
  and the best completed time for the current warmup. The course uses only static toys and one bounded scheduler loop.
- When the round starts, the plugin hands players back so the game assigns vanilla roles, then **swaps**
  the selected SCP slots to the pickers:
  - If vanilla spawned an SCP that someone picked, one picker from that pool takes the slot, and the
    displaced vanilla holder inherits the picker's original (human) role.
  - If vanilla did **not** spawn that SCP this round, the pick is skipped. The plugin **never creates
    extra SCPs** or invents fallback roles — it only rearranges what vanilla already assigned.
- The selector room and all models **despawn when the round starts**.

### Build

References SCP:SL managed assemblies from a dedicated-server install. If yours is elsewhere, pass
`ServerManagedPath`:

```powershell
dotnet build -c Release
# or:
dotnet build -c Release -p:ServerManagedPath="C:\path\to\SCPSL_Data\Managed"
```

Output: `src/WarmupScpSelector/bin/Release/net48/WarmupScpSelector.dll`. The 7 SCP models are embedded
in the DLL. HintServiceMeow must also be installed for the plugin to load.

### Install

Copy `WarmupScpSelector.dll` into the LabAPI plugins folder for your port, e.g.:

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
- `RoomOrigin` — world position of the floating selector room (high Y keeps it clear of the live map).
- `PedestalSpacing`, `ModelScale`, `SelectorCoinScale` — layout/sizing.
- `SelectorItem` — the pickup used as the selection coin (default `Coin`).
- `RoleSwapDelaySeconds` — delay after round start before swapping (lets vanilla roles settle).
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
- `Activities.Parkour.Enabled` — additionally set this to `true` to build Pulse Line in the Aim hall's far-left bay.
  Parkour remains default-off and requires the Aim hall because it reuses that shell and floor.
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
- `Activities.Aim.FlashY`, `HeroY`, and `FooterY` — the three non-overlapping Aim HSM bands. The retained
  `CollapsedStatusY` setting is legacy compatibility and is not rendered.
- `Activities.Aim.HudX` — HSM center-X for the training-side Aim HUD lane (default `-1077`). It keeps the flash,
  hero, and footer in the narrow left corridor between the native inventory list and wheel so the HUD never overlaps
  them while TAB is held. The selector side shows only the original centered SCP panel.

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

- C# planner/activity/runtime-adjacent logic (headless): `dotnet build tests/WarmupScpSelector.Tests` then run
  `WarmupScpSelector.Tests.exe` (`61/61` tests, including bot lifecycle/tactical contracts, lethal reset state, parkour route/gates/HUD, MER transforms,
  widened lane bounds, deterministic sliding motion, one-credit immediate sphere relocation, bilingual Aim text, and HSM cache behavior).
- Model geometry contracts: run each `tests/models/test_*_model.py` script (`10/10` current model contracts).
- Shared HSM renderer: `node ../.tests/UI/smoke-test.js`; all 22 WarmupScp draft/Aim EN+CN fixtures parse with
  zero static issues against the shared native-background collision harness.
- Dev-only isolated live harness: `tests/WarmupRangeVerifier`. Its current 253-check run verifies the full-width shell,
  nearly hall-wide counter, two symmetric native attachment workstations, three bright non-HDR point lights per hall half, six persistent dispensers, dummy settling,
  two real product bots continuously walking/jumping, non-head upper-chest tracking, a genuine participant firearm hit
  from the real player side of the counter, a measured 0.5–0.6-second response and native held-fire ammo consumption,
  combat strafing, multi-round native held fire, retaliation ADS, a stationary-attacker lethal reset within 2.5 seconds,
  the UI-only seam, 2.2 m reload-cover traversal, exact 12-second input release,
  all six weapon pickups, three smoothed sliding targets, 20 authored spheres, pooled same-toy sphere relocation, exact teardown, ambient isolation,
  and successful lane restart. It must never ship to production.

### Known limits / conflicts

- Player-facing text requires HintServiceMeow and uses stable HSM hint IDs.
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

### 功能

- 服务器**等待玩家**时，所有人被设为 `Tutorial` 并传送到悬空选择房间，房间里排列着 SCP 模型
  （049、079、096、106、173、939、3114），每个模型前有一枚大硬币。
- **捡起硬币**即选择该 SCP（拾取会被取消，硬币留在原处）。提示会显示倒计时和当前选择，并在所选 SCP 旁标注
  有多少玩家选了同一个（例如 `SCP-096　·　5 人`），下方每个候选 SCP 也会带一个小小的 `·N` 计数。回合开始前
  可随时更改。（提示只在文本变化时才重新发送，不会刷屏占用网络。）
- 插件不会强制原版大厅倒计时。唯一例外是单人使用已启用的原生机器人时，训练场会临时持有大厅锁，
  防止计入人数的 dummy 错误触发开局；第二名真实玩家加入、最后一名玩家离开或训练场停止时会同步释放。
- 默认关闭的**瞄准训练场**会把 SCP 展厅和训练区合并成一个等宽、完整的长方形大厅，没有门洞、瓶颈或第二个房间。
  六把永久保留的真实枪械清晰摆在射击台上；中央 19.2 米 × 22 米区域分成三条 6.4 米宽训练道：带掩体的实战机器人、不同距离与速度的三条平行永久移动靶，以及命中后
  立即消失并在其他位置重生的 Aim-Lab 球形反应靶。有效命中普通靶或球形靶时会向射手发送原生命中标记；
  移动靶在 15 Hz 网络关键帧之间使用客户端插值，不再按调度更新逐格跳动。
- 可显式启用最多两名原生 RA 机器人。它们不绑定路点，而是通过原生 FPC 马达持续横向移动；两个槽位都
  只装备 Crossvec。机器人移动时会按受限冷却跳跃，近距离会更频繁跳跃，远距离则
  按住原生开镜。只有训练玩家使用其受控枪械真正命中后，机器人才能锁定第一名攻击者，并持续瞄准躯干、
  横移和按住原生全自动开火；锁定固定 12 秒且不会被重复命中延长。弹匣将空时会停止射击/开镜，移动到
  对应槽位的 2.2 米高掩体后装弹，再恢复战斗。机器人可正常死亡，并以新的受控身份和随机步枪重生。
- 机器人子弹造成真实伤害。对训练玩家的致命一击会在原版死亡前取消，同步把同一玩家重新初始化为
  `Tutorial` 并送回入口，不经过旁观者状态。离开训练场或回合开始时，所有训练场拥有的枪械、拾取物、靶子、
  载具、机器人和提示都会销毁。
- 玩家进入训练场后，完整 SCP 选择面板会折叠为一行状态；独立的中英双语 HSM 训练卡使用互不重叠的
  事件、主卡、指引和状态区域。
- 回合开始时，插件把玩家交还给游戏进行原版职业分配，然后**交换**被选中的 SCP 名额：
  - 如果原版生成了某人选择的 SCP，则从该选择池里选一名玩家获得该名额，被替换的原 SCP 玩家获得选择者原本的（人类）职业。
  - 如果原版本回合没有生成该 SCP，则跳过该选择。插件**不会额外创建 SCP**，也不会随机生成回退职业，只会重排原版已分配的职业。
- 选择房间和所有模型在**回合开始时销毁**。

### 构建

```powershell
dotnet build -c Release
# 或指定服务端托管程序集目录：
dotnet build -c Release -p:ServerManagedPath="C:\path\to\SCPSL_Data\Managed"
```

输出：`src/WarmupScpSelector/bin/Release/net48/WarmupScpSelector.dll`。7 个 SCP 模型已嵌入 DLL。服务器也必须安装 HintServiceMeow。

### 安装

把 `WarmupScpSelector.dll` 复制到对应端口的 LabAPI 插件目录：

```text
%AppData%\SCP Secret Laboratory\LabAPI\plugins\<端口>\
```

### 配置

```text
%AppData%\SCP Secret Laboratory\LabAPI\configs\<端口>\WarmupScpSelector\config.yml
```

常用选项：

- `Language`——`"en"` 英文，`"cn"` 简体中文。
- `RoomOrigin`——悬空选择房间的世界坐标（较高的 Y 可避免与正式地图冲突）。
- `PedestalSpacing`、`ModelScale`、`SelectorCoinScale`——布局/尺寸。
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
- `Activities.Aim.FlashY`、`HeroY`、`FooterY`——三个互不重叠的 Aim HSM 显示区域；保留的
  `CollapsedStatusY` 仅用于配置兼容，不再渲染。
- `Activities.Aim.HudX`——训练侧 Aim HUD 通道的 HSM 中心 X（默认 `-1077`）。选择侧只显示原始居中的 SCP
  选择面板；越过地面分界线后 SCP 面板完全隐藏，只显示位于原生物品栏列表与物品转盘之间的 Aim HUD。

### 测试

- C# 纯逻辑与运行时邻接测试：`61/61` 通过。
- SCP、Logo、枪架和保留的移动靶载具模型契约：`10/10` 通过。
- WarmupScp 选择/训练场中英文 HSM fixture：22 个全部为零静态问题。
- 隔离本地端口的 dev-only 实机验证：`253/253` 通过，覆盖全宽场景、全宽柜台、每半区三盏超亮非 HDR 点光源、
  dummy 落地、两名产品机器人持续行走/跳跃、非头部上胸跟踪、从柜台玩家侧真实命中后立即还击、战斗横移、原生持续开火、全程开镜、静止攻击者约 2 秒内触发致命重置、UI 分界、2.2 米
  掩体后装弹、固定 12 秒释放、六把枪、三条平滑移动靶、20 个球形靶、同一网络球体的池化换位、精确清理与训练场重启。

### 已知限制 / 冲突

- 玩家提示依赖 HintServiceMeow，并使用稳定的 HSM hint ID。
- 大厅音乐为每名玩家使用一个过滤后的音频发送器，因此加入时淡入是按玩家独立生效的。音频文件不要太长；
  `MusicMaxSeconds` 会限制误放入的超大文件。
- 隔离实机 harness 已验证 AdminToy 碰撞、扩宽训练道、枪架位置、平滑永久移动靶同步、球形靶生成、两名产品
  机器人真实巡逻与原生还击弹药消耗、dummy 落地和交接零泄漏清理。真实客户端的拾取/射击顺序、第一人称表现、
  机器人反复死亡重生、命中标记与球形靶消失重生画面、致命重置画面，以及中英文最终视觉检查仍需在可见本地
  服务器上完成。单人会使用训练场临时持有的大厅锁；第二名真实玩家加入后自动释放，让原生倒计时继续。

## License

MIT. See `LICENSE`.
