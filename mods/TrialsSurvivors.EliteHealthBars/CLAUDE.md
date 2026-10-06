# EliteHealthBars

Health bar, HP text and buff/debuff icons above each elite, plus edge arrows for off-screen elites.

- Elites are tracked from a postfix on `ARPGEntity_Module_Elite.ActivateElite`, keyed by the pointer
  captured at that moment. Enemies are pooled, so cleanup is done by sweeping each frame
  (`StaleReason`: destroyed / no longer elite / inactive / dead) instead of hooking reset or disable. Don't
  switch to a reset hook without evidence it fires on every path.
- The camera is chosen by culling mask against the elite's layer (`ResolveCamera`). `Camera.main` is the UI
  camera here.
- Buffs come from `entity._buffDebuffGrouperContainer._buffDebuffGrouperList`. Icon, `isDebuff` and
  `hideInUI` live on the grouper's `SSV2_SO_BuffDebuffData` and are cached per data asset in `BuffReader`.
- The arrow sprite is a generated triangle texture pointing +x, then rotated with `Atan2`. Keep it on
  `HideAndDontSave`.
- `Diagnostics.Verbose` logs a 5s heartbeat: tracked/drawn/arrows, drop reasons and every camera. Turn it
  on before asking Matt for a test run when something doesn't render.
