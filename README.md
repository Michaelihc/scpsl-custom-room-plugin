# SCP:SL Custom Room Plugin

EXILED plugin scaffold for SCP: Secret Laboratory custom room logic.

## Build

```powershell
dotnet restore
dotnet build -c Release
```

The plugin DLL is emitted under:

```text
src/ScpslCustomRoomPlugin/bin/Release/net48/ScpslCustomRoomPlugin.dll
```

Copy that DLL to the server's EXILED plugins folder for the target port.

## Project Layout

- `src/ScpslCustomRoomPlugin/Plugin.cs` - plugin entrypoint and event registration.
- `src/ScpslCustomRoomPlugin/Config.cs` - EXILED config object.
- `src/ScpslCustomRoomPlugin/Handlers/ServerEventHandlers.cs` - server event hooks for room lifecycle logic.
