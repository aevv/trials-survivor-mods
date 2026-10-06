# RunHud

Live run stats (kills, elites, rooms, active monster card buffs) above the right end of the XP bar.

- The label is a clone of `UI_Module_XPBar._levelText`, so it inherits the game's font, outline and shadow.
  It's parented to `_xpBarImage` and anchored to that rect's top-right. Mirroring it inside the level
  text's own parent doesn't work, because that parent is the small level box on the left.
- If the XP bar ever shows progress by resizing `_xpBarImage` instead of `fillAmount`, the label would
  drift. The attach log line records the image type, fill and width to check this.
- The live tracker comes from postfixes on `RunStatsTracker.BeginRun` and `Cleanup`.
- Kills shown are `_totalKills + _pendingKills`. The meaning of "pending" is inferred, not confirmed. If HUD
  kills ever disagree with the end-of-run recap, start there.
- Monster card buffs aggregate `MonsterCardEffect_StatBuff` entries: multiplicative values are factors
  (1.3 = +30%), additive values are flat. Stat names come from `ARPGDatabaseEntry_Stats.GetEntryDisplayName()`.
- Text refreshes on an interval (`RefreshInterval`), and only when the composed string changes.
