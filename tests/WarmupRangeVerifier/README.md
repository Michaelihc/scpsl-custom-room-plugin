# WarmupRangeVerifier (DEV ONLY, never ship)

`WarmupRangeVerifier` is a disposable LabAPI plugin that automatically exercises the live Full Aim Range after
`WaitingForPlayers`, without a human game client. It is intentionally excluded from `WarmupScpSelector.sln` and
has no product deploy target.

The verifier does not modify production code. It reads internal lane ownership through reflection, invokes the
lane's existing `StopForRoundStart()` cleanup route, asserts that only the exact product-owned entities vanished,
and invokes the existing `Start(room)` route to leave the range ready for manual QA.

## Safety boundaries

- Run only on an isolated local test port.
- Do not co-load `WarmupDummyTester.dll`; that tool intentionally repositions every native dummy and would fight
  this verifier's authored-position settling checks.
- The harness owns exactly one dummy named `AIM-RANGE-LIVE-PROBE`. Cleanup destroys only its captured
  `ReferenceHub`; it never sweeps ambient/plugin dummies.
- Shelf cleanup assertions use the six exact shelf serials captured from product state. The verifier never destroys
  ambient pickups.
- Toy/target/carrier cleanup assertions use exact Unity instance IDs captured from the lane's own world/MER lists.
  The verifier never destroys ambient admin toys.
- It temporarily sets `Round.IsLobbyLocked = true` while the dummy exists, restores the prior value afterward, and
  fails if `RoundStarted` is observed. This prevents the native host+dummy player-count rule from starting a real
  round during an automatic no-client probe.
- Bot retaliation/fire is not triggered. The no-human bot safety boundary is tested with zero canonical humans;
  manual retaliation, damage, death, respawn, and native shot verification remain separate QA.

## Required local product config

Enable the range in the local port's `WarmupScpSelector` config:

```yaml
is_enabled: true
activities_enabled: true
activities:
  aim:
    enabled: true
    bot_count: 2
```

`bot_count: 2` makes the zero-canonical-human assertion meaningful: the product is configured to request bots,
but its lobby policy must still spawn none. Keep the six default conventional weapon presets for the shelf type
checks.

The verifier config may remain at defaults:

```yaml
is_enabled: true
startup_timeout_seconds: 15
dummy_settle_timeout_seconds: 4
restart_range_after_verification: true
```

Use the generated local config file as the source of truth for serializer key casing.

## Build

From the product root:

```powershell
dotnet build -c Release .\WarmupScpSelector.sln
dotnet build -c Release .\tests\WarmupRangeVerifier\WarmupRangeVerifier.csproj
```

The verifier output is:

```text
tests\WarmupRangeVerifier\bin\Release\net48\WarmupRangeVerifier.dll
```

A normal solution build does not build or deploy it.

## Deploy to an isolated local port

Example using port `7999` (recheck that it is unused before claiming it):

```powershell
$port = 7999
$pluginDir = "$env:APPDATA\SCP Secret Laboratory\LabAPI\plugins\$port"
New-Item -ItemType Directory -Force $pluginDir
Copy-Item .\src\WarmupScpSelector\bin\Release\net48\WarmupScpSelector.dll $pluginDir
Copy-Item .\tests\WarmupRangeVerifier\bin\Release\net48\WarmupRangeVerifier.dll $pluginDir
# Also place the locally verified HintServiceMeow.dll dependency beside the product plugin.
```

For automated local loops only, start the isolated server with the repository helper:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ..\.references\run-port.ps1 -Port 7999
```

Do not use a headless automated loop as release/manual QA. For final visual QA, restart the visible local server on
the selected port after removing the verifier DLL.

## Logs and result

All machine-readable lines use one prefix:

```text
[AimRangeLiveTest] START ...
[AimRangeLiveTest] PASS check=<stable-id> observed=<details>
[AimRangeLiveTest] FAIL check=<stable-id> expected=<...> observed=<...>
[AimRangeLiveTest] PASS summary checks=<n>/<n> cleanup=verified manualQaRange=restarted
```

A run passes only after:

1. Product/controller/door/world/layout/shelves/target deck/scheduler/rack visuals are live and requested bots remain
   absent with zero canonical humans.
2. The dense 60+ downward-ray grid and 12 authored-distance horizontal rays pass.
3. One native Tutorial dummy settles/walks at the entrance and authored floor points without falling or penetrating.
4. All six shelf pickups match configured conventional types, anchors, kinematic rigidbodies, distinct serials, and
   remain absent from `SelectorRoom.CoinRoles`.
5. Every static target anchor and several absolute-time moving target/carrier samples match authored bounds and the
   carrier mount relation.
6. `StopForRoundStart()` removes all exact range-owned shelf pickups, targets, carriers/bots/hints/world toys without
   touching ambient entities, and the range restarts for manual QA.

See `LIVE-VERIFICATION-TRANSCRIPT.md` for the detailed execution record/template.
