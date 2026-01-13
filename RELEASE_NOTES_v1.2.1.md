Froggy v1.2.1 - Release Notes

Summary
-------
This patch fixes the battery drain detection logic for Bluetooth devices and adds short-term diagnostics to help validate behavior on user devices.

Highlights
----------
- Fix: More reliable battery drain detection for Bluetooth devices.
  - Recording interval reduced from 5 minutes to 1 minute to capture short/fast battery changes.
  - Drain-rate calculation window lowered from ~6 minutes to ~1 minute to allow early estimates.
  - Display threshold reduced so small but measurable drain rates are shown.
- Add: Temporary diagnostic logging to `%LOCALAPPDATA%\\BluetoothWidget\\log.txt` to help validate readings and tuning.
- Docs: In-code comments added to `BatteryTracker.cs` and `App.xaml.cs` explaining the changes and guidance for maintainers.
- Version bumped to `v1.2.1`.

Notes for users and maintainers
------------------------------
- Log file path: `%LOCALAPPDATA%\\BluetoothWidget\\log.txt`.
  - Contains diagnostic lines from `BatteryTracker.RecordBattery` and `BatteryTracker.GetSummaryText` while debugging is enabled.
  - Consider removing or gating verbose logs in production builds to avoid large log files.
- If you see noisy drain readings, consider increasing the thresholds in `BatteryTracker.cs`:
  - `shouldRecord` interval (minutes),
  - `elapsed.TotalHours` threshold for drain calculation, and
  - `DrainRatePerHour` display threshold.
- Git note: There is an embedded repository warning for path `froggy` in the outer repo. If this was unintentional, remove the nested repo with `git rm --cached froggy`, then commit and push.

How to verify
-------------
1. Build and run the app.
2. Connect a Bluetooth device that reports battery level.
3. Let the device run for a few minutes while the app is open.
4. Check the app UI for the device `StatsText` and the log file at the path above for diagnostic entries.

Suggested release description (short)
------------------------------------
v1.2.1 — Improve battery drain detection for Bluetooth devices and add diagnostics to help validate and tune behavior. See RELEASE_NOTES_v1.2.1.md for details.
