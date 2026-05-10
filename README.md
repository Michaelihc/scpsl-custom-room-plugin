# SCP:SL Custom Room Plugin

Draft EXILED plugin for SCP: Secret Laboratory warmup SCP class selection.

Status: draft/private version.

## Overview

During waiting-for-players, the plugin moves verified players into an SCP selector lobby anchored at the SCP-173 gate. Players select a preferred SCP by interacting with a coin. The game still owns the native lobby countdown and the vanilla round role distribution.

After vanilla round start, the plugin only swaps already-assigned vanilla roles:

- If vanilla spawned a selected SCP role, one selected player from that SCP pool can receive that slot.
- The displaced vanilla SCP holder receives the selected player's original vanilla role.
- If vanilla did not spawn that SCP role, the selection is skipped.
- The plugin does not create additional SCP roles or fallback roles.

## Build

This project references SCP:SL managed assemblies from a dedicated server install. If your server files are somewhere else, pass `ScpSlManagedPath` to the build:

```powershell
dotnet restore
dotnet build -c Release -p:ScpSlManagedPath="C:\path\to\SCPSL_Data\Managed"
```

The plugin DLL is emitted under:

```text
src/ScpslCustomRoomPlugin/bin/Release/net48/ScpslCustomRoomPlugin.dll
```

Copy that DLL to the port-specific EXILED plugins folder for the target server.

## Project Layout

- `src/ScpslCustomRoomPlugin/Plugin.cs` - plugin entrypoint and event registration.
- `src/ScpslCustomRoomPlugin/Config.cs` - EXILED config object.
- `src/ScpslCustomRoomPlugin/WarmupSelectionController.cs` - selector lobby, native countdown status, player selections, and post-start swap logic.

## License

MIT. See `LICENSE`.
