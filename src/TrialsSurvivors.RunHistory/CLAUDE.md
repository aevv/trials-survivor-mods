# RunHistory

Saves every finished run to `BepInEx\config\runhistory\<yyyyMMdd-HHmmss>-<result>.json` and shows them in
a panel in the hub (F6).

- Capture is two-phase. `RunStatsTracker.PopulateRecapData` builds and saves the record. The recap's
  skill list is only filled later, in `RunRecapData.PopulateSkillRecap`, which adds the skills and
  rewrites the same file (matched by `RunRecapData` pointer, within 5 minutes). A run with
  `"Skills": []` means the second hook didn't fire.
- `PopulateRecapData` can be called more than once per run end. `_lastCaptured` dedupes on
  (result, duration, kills).
- `RunRecord` is the on-disk schema. Add fields with defaults and bump `SchemaVersion` only for breaking
  changes, since old files must keep loading. Unreadable files are skipped with a warning.
- Skill icons are stored by sprite name and looked up through `Resources.FindObjectsOfTypeAll<Sprite>()`
  when the panel opens. Icons that aren't loaded in the hub just don't show.
- The panel disables game input actions while open (`InputBlocker`) and reads keys from `Keyboard.current`
  itself. Always pair `Block` with `Release`, including `OnDestroy`.
- The panel canvas uses `sortingOrder = short.MaxValue` because some hub HUD elements sit on high-order
  overlay canvases.
- Still planned: a walk-up interactable in the hub (clone `CodexInteractable`) as an alternative to the hotkey.
