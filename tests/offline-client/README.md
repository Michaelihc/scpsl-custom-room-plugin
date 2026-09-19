# Native countdown verification

Run these CN-only recipes with the offline-client harness's `tools/playtest.py run`.
Use a network-disabled Windows Sandbox, the unmodified native client, and a clean
server containing WarmupScpSelector, HSM, OfflineLab.Observer and CountdownNativeProbe.
Enable both activity lanes in the plugin configuration; keep their default HUD coordinates.
The probe requires `OFFLINE_LAB_OBSERVER=1` and ServerConfigs permission. It sets the
native lobby duration to 25 seconds and arranges grounded approaches using the public
station geometry. It does not write hints, set camera angles or invoke activity methods.

Before each recipe, verify one native Tutorial player, no fixture dummy, closed inventory
and menus, and camera yaw/pitch within 0.5 degrees of zero. Correct the camera with native
mouse input and verify its response. A failed correction must stop the test. Cursor
visibility alone does not prove that the inventory is closed. Do not resize or refocus
the window after confirming orientation without checking it again.

Run `countdown-full.json`, `countdown-aim.json`, then `countdown-parkour.json`.
The first two remove their named fixture to return to waiting; the third lets the native
round begin. RA commands travel through LocalAdmin. Native D/W crosses the lane boundary.
Read fresh logs and reports before reviewing footage. Automated role/displacement checks
are supporting evidence only: verify actual destination, countdown text, wrap, HUD spacing
and round handoff visually. Distance alone cannot establish the correct direction.

The `*-boundary.json` files are supplemental exploratory recipes, not validated acceptance
tests. The Aim recipe's Tab input can leave inventory open; explicitly close and verify it
before any further mouse input. Do not count these recipes as a pass solely from their
automated checks. Parkour boundary coverage remains unexecuted.

Keep dated results and limitations in the adjacent verification JSON. Audio is not captured
by the current recorder. These tests cover the default 1280x720, 16:9 capture profile.
