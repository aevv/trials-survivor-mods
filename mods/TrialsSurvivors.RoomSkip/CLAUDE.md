# RoomSkip

SKIP button under the top-right room objective panel. It unlocks at tier 3 and ends the room the same way a
time-out does. docs/findings.md ("Room objectives and completion") has the game side.

- `ChunkObjectiveUI.Setup` postfix attaches, `Teardown` prefix detaches. One button at a time (`SkipButton._active`),
  keyed by the UI pointer captured at attach.
- Available = started, not completed, not stopped, `CurrentTier >= 2`. Skip calls `CompleteObjective()`. Don't
  fake it by moving the timer: reward and cleanup all hang off `CompleteObjective`.
- The button carries its own nested `Canvas` + `GraphicRaycaster`, so clicks don't depend on the HUD canvas having
  a raycaster. It has `LayoutElement.ignoreLayout` and is positioned every 0.5s under the union of the panel's
  visible `Graphic`s, because the panel's own rect may not match what's drawn.
- Styling is borrowed from the panel: the largest sprite-backed `Image`, and the `_titleText` TMP font, colour and
  size. `Diagnostics.DumpObjectiveUi` logs the full panel hierarchy once per session to tune this against.
