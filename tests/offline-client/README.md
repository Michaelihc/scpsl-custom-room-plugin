# Native countdown verification

Run these CN-only cases from the offline-client harness's prepared guest:

```powershell
python tools/playtest.py suite <this-checkout>/tests/offline-client/countdown-suite.json
```

Select the run and deploy its complete bundle once; subsequent runs reuse the guest/profile.
The suite connects once, sizes the window, measures native mouse response and verifies camera
yaw/pitch within 0.5 degrees of zero before recording. Failed preparation stops the suite.
Keep one Sandbox viewer open and do not supply manual input while the suite owns the client.
The target is 120–180 seconds for connection, preparation, cases and review artifacts on a prepared
guest. Report actual elapsed time; build/deployment and manual review are separate.
Use a network-disabled Windows Sandbox, the unmodified native client, and a clean
server containing WarmupScpSelector, HSM, OfflineLab.Observer and CountdownNativeProbe.
Enable both activity lanes in the plugin configuration; keep their default HUD coordinates.
The probe requires `OFFLINE_LAB_OBSERVER=1` and ServerConfigs permission. It sets the
native lobby duration to 15 seconds and arranges grounded approaches using the public
station geometry. It does not write hints, set camera angles or invoke activity methods.

Preparation requires one native Tutorial player, no fixture dummy and closed menus/inventory.
Cursor visibility alone does not prove that inventory is closed: the suite verifies camera
response. It stops recorded input if the client moves or resizes.

The suite runs `countdown-full.json`, `countdown-aim.json`, then `countdown-parkour.json`.
The first two remove their named fixture to return to waiting; the third lets the native
round begin. RA commands travel through LocalAdmin. Native D/W crosses the lane boundary;
Aim also returns to the hub with native A. Signed displacement and public lane-boundary
assertions verify destination. The probe arranges only the starting approach.
Read fresh logs and reports before reviewing footage. Automated role/displacement checks
are supporting evidence only: verify actual destination, countdown text, wrap, HUD spacing
and round handoff visually. Distance alone cannot establish the correct direction.

The `*-boundary.json` files are supplemental exploratory recipes, not validated acceptance
tests. The Aim recipe's Tab input can leave inventory open; explicitly close and verify it
before any further mouse input. Do not count these recipes as a pass solely from their
automated checks. Parkour boundary coverage remains unexecuted.

Keep dated results and limitations in adjacent verification JSON files. Clips contain guest
playback audio via WASAPI/AAC, with microphone sharing disabled. Check signal as well as track
presence; quiet scenes can be silent. These tests cover the default 1280x720, 16:9 capture profile.
