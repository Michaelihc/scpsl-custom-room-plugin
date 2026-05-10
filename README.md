# SCP:SL Custom Room Plugin

EXILED plugin for SCP: Secret Laboratory warmup SCP class selection.

During warmup the plugin locks the lobby, suppresses the vanilla waiting/start timer update, shows its own countdown as hints, moves players into a custom Tutorial selector room, and lets them choose an SCP role by interacting with persistent coin pickups. After the vanilla round role assignment runs, the plugin swaps selected players into matching SCP roles from their selected pools.

## Build

This project references SCP:SL managed assemblies for `Assembly-CSharp-firstpass`, `Mirror`, and Unity types. If your server files are somewhere else, pass `ScpSlManagedPath` to the build:

```powershell
dotnet restore
dotnet build -c Release -p:ScpSlManagedPath="C:\path\to\SCPSL_Data\Managed"
```

The plugin DLL is emitted under:

```text
src/ScpslCustomRoomPlugin/bin/Release/net48/ScpslCustomRoomPlugin.dll
```

Copy that DLL to the server's EXILED plugins folder for the target port.

## Project Layout

- `src/ScpslCustomRoomPlugin/Plugin.cs` - plugin entrypoint and event registration.
- `src/ScpslCustomRoomPlugin/Config.cs` - EXILED config object.
- `src/ScpslCustomRoomPlugin/WarmupSelectionController.cs` - warmup room, selector coins, countdown, and post-start role reconciliation.
- `src/ScpslCustomRoomPlugin/Patches/WaitingForPlayersUiPatch.cs` - Harmony patch suppressing the vanilla waiting/start timer update.
