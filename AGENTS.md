# WarmupScpSelector

Read the [shared workspace instructions](../AGENTS.md) before working here, including
language, authorization and verification policy; independent repositories may not inherit them.
Use [plugin conventions](../docs/plugin-conventions.md) for shared settings, audio and UI.

LabAPI `net48` warmup SCP draft, orbital-station activities and early-disconnect SCP replacement.
The room and models despawn at round start. This remains an independent product.

| Task | Reference |
| --- | --- |
| Architecture, station import/export and lifecycle invariants | [Architecture](docs/architecture.md) |
| Build, verification and retained legacy tools | [Development](docs/development.md) |
| Player/admin behavior and configuration | [README](README.md) |
| API lookup | [Metarepo references](../.references/AGENTS.md) |
| Test ports and manual verification | [Local environment](../docs/local-test-environment.md), [tests](../.tests/AGENTS.md) |
| HSM and localization conventions | [Plugin conventions](../docs/plugin-conventions.md) |

- Preserve vanilla's SCP role multiset; remap recipients before role initialization/networking.
- Stop every activity before the Tutorial-to-None role handoff. Invalidate generation-bound callbacks
  and release only owned guns, hints, toys and sessions.
- Station geometry and authored marker anchors must agree with gameplay. Preserve opaque negative
  ProjectMER IDs and hierarchy transforms; never infer that a negative ID means missing.
- Prefer native events/wrappers; do not add Harmony when an available native path suffices.
- HSM uses Center alignment with explicit X. Do not claim HTML previews prove in-game placement.
- Keep this file an index. Update the owning topic docs; do not read or maintain `implementation-notes.*`.
