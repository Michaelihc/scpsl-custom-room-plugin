# Development and verification

This file records current verification boundaries, not a running-server inventory.
Select ports using `../../docs/local-test-environment.md` and inspect processes before launch.

## Build and focused checks

- Build the root solution with `dotnet build -c Release -p:DeployToLocalServer=false`.
  Override `ServerManagedPath` when the dedicated server is elsewhere.
- Build `tests/WarmupScpSelector.Tests` and run its produced executable for planner/activity policies.
- Model generation uses `requirements.txt`, `tools/build_scp_<id>_asset.py` and
  `tests/models/test_scp_<id>_model.py`; use the current renderer for visual inspection.
- `tests/WarmupPlaytestScenarios/` is the current station/parkour live harness.
  It observes public geometry and native dummy behavior; it must not reflect into plugin internals.
  Relevant scenarios include `warmup-station` and `warmup-pulse-line`, through `ptest run warmup standard`.

## Remaining checks

- Recheck the Aim HUD in-game. The documented default X=-1077 lies outside the shared conservative
  caret-space wrap range. An HTML preview alone does not establish correct native HSM wrapping.
  Confirm current config/source values before tuning; use the shared ghost-padding/Center-X recipe.
- Final gameplay verification still needs native dummy association, firearm retaliation, repeated
  bot death/respawn and the human lethal-reset path without spectator leakage.
- Retained Aim rack/moving-target model assets are still embedded and have geometry tests.
  Remove them only together with their catalog/build/test references; this documentation cleanup
  does not change the shipped assembly or resource set.

## Historical tools

`tools/build_room_preview.py` produces the old single-hall layout.
`tests/WarmupRangeVerifier/` contains old single-hall seam assertions and is excluded from
the product solution. Neither is a current release verifier; preserve them as reference until
they are migrated or archived together with all consumers. Their historical instructions are
not a reason to deploy the old verifier.

Current architecture is in [architecture.md](architecture.md); user-facing behavior is in
[README.md](../README.md).
