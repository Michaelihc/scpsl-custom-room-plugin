# WarmupDummyTester (dev-only, NOT shipped)

A throwaway LabAPI plugin for manually testing the warmup selector UX with bots. **Never ship it** — it is
deliberately excluded from `WarmupScpSelector.sln`, so a normal product build never produces or deploys it.

## Why it exists

The real `WarmupScpSelector` ignores dummies on purpose: dummies aren't in `Player.ReadyList` and have no
client to grab a coin, so they stay spectators. This plugin lets you *simulate* a populated draft:

- Pulls every admin-spawned dummy (`dummy` command) into the selector room (sets `Tutorial`, parks them on
  the floor, re-placing each tick so they don't fall).
- Gives each dummy a **random** pick from the **live** selector's configured SCP list.
- **Broadcasts on screen** which dummy chose what, e.g. `(3) → SCP-173`, refreshing live as dummies appear.
- At round start, releases its dummies back to `Spectator` so they don't litter the live round.

It reads the running product plugin's config (`WarmupScpSelectorPlugin.Instance.Config`) so the SCP pool and
room location always match the real selector.

### What it does NOT do

It does **not** drive the real round-start SCP swap. That path iterates `Player.ReadyList` (no dummies) by
design, and changing that is a shipped behavior change we deliberately avoided. The swap math itself is
covered headlessly by `tests/WarmupScpSelector.Tests`.

## Build & run (local test server only)

```bash
dotnet build -c Release tests/WarmupDummyTester/WarmupDummyTester.csproj
# copy the DLL into a LOCAL test server, alongside the product plugin:
#   <AppData>/SCP Secret Laboratory/LabAPI/plugins/<port>/WarmupDummyTester.dll
```

Then in-game during the lobby: spawn a few bots with the `dummy` command and watch the broadcast.

**Remove `WarmupDummyTester.dll` from the plugins folder before using that server for real play.**
