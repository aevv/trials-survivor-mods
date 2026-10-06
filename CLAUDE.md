# TMods

BepInEx 6 IL2CPP mods for **Trials Survivors** (Unity 2022.3, IL2CPP x64). Public repo on Matt's personal
account (`aevv/trials-survivor-mods`): work on `main`, commit, and `git push origin main`. No PRs.

README.md covers setup, the dump tooling and what each mod does. docs/findings.md holds the
reverse-engineering deep dives. Add new game knowledge there, not here.

## The loop

Use the Taskfile. Don't hand-roll dotnet or grep commands for these:

- `task build` / `task deploy` / `task deploy:one MOD=<Name>`. Deploy refuses while the game runs, because
  the game locks the plugin DLLs. If it refuses, ask Matt to close the game; don't kill it yourself.
- `task logs` shows mod output from `BepInEx\LogOutput.log`.
- `task logs:errors` shows exceptions from Unity's `Player.log`. **UI/TMP exceptions only show up here.** When
  something renders wrong and the BepInEx log is clean, check this first.
- `task runs` lists the run history mod's saved runs.

You can't play the game. Each change is verified by Matt doing a run. Make every change as diagnosable
as possible from logs alone: log what was attached, chosen or dropped, and put noisy diagnostics behind a
`Diagnostics.Verbose` config entry that defaults to false.

## Finding things in the game

`dumps/` is gitignored and reproducible (`task dump`, `task decompile`):

- `dumps/cs/game/Assembly-CSharp/` has the game as written: real names, base types, `[Tooltip]`s
  (often in French), field offsets. Method bodies are `throw null`.
- `dumps/cs/interop/Assembly-CSharp/` has what a plugin can actually call. **Check here before writing a
  patch.** Private game fields are exposed as public properties with the same name (e.g. `_levelText`),
  and overloads or nested types can differ from the game source.

To skim a type without the attribute noise:

```bash
grep -vE "^\s*(\[Token|\[Address|\[FieldOffset|throw null;|\{|\}|\[CompilerGenerated\]|get|set|$|using )" -- Type.cs
```

Some dumped filenames start with `-`, so always pass `--` before globs to grep/ls in the dump folders.

## IL2CPP / Unity gotchas we've already paid for

- **Unity null vs collected.** Check `obj == null` (Unity's destroyed check) and `obj.WasCollected`.
  `obj.Pointer` throws on a collected wrapper, so capture pointer keys when you first see an object.
- **Injected MonoBehaviours** need a `(IntPtr ptr) : base(ptr)` ctor and are added via `AddComponent<T>()`
  in `Load`. The "unsupported return type/parameter" warnings at load are harmless. Private helpers that use
  managed types just aren't exposed to IL2CPP.
- **Don't rely on `Awake` state in injected components.** The overlay canvas created in `Awake` was
  missing at runtime. Create Unity objects lazily, and recreate them if they get destroyed.
- **`Camera.main` is the UI camera** (`UICamera`). So is `WorldSpaceCanvasManager._worldCamera`. Use the
  camera whose `cullingMask` includes the target's layer. Entities are on layer `Entity`, rendered by `Main Camera`.
- **TMP and the game font.** The game's TMP font has no glyphs beyond basic ASCII. Characters like `·`, `…`
  or arrows make TMP hit a fallback with a null material, which throws in `GetFallbackMaterial` and
  fails silently in-game. Keep UI strings ASCII. Also set `fontSharedMaterial = font.material` with the font.
- **TMP overflow.** `Ellipsis`/`Truncate` drop a whole line that doesn't fit *vertically*. Use
  `Overflow` unless the rect is definitely tall enough.
- **`DontDestroyOnLoad` only works on root GameObjects.** For runtime textures and sprites, set
  `hideFlags = HideFlags.HideAndDontSave`.
- **Input** goes through the new Input System. Read `Keyboard.current[key]` directly. To stop the game
  reacting while a mod UI is open, disable enabled `InputAction`s outside the `UI` map, and re-enable them on close.
- **Il2Cpp collections**: `Il2CppSystem.Collections.Generic.List/Dictionary` index and `foreach` fine.
  For `IReadOnly*` interface properties, use the backing private field instead (e.g. `_skillEntries`).
  `TryCast<T>()` downcasts interface or base wrappers. Methods with `out` overloads may need an explicit
  `out string` to disambiguate.
- **Config defaults don't migrate.** BepInEx keeps whatever is already in the user's `.cfg`. If you change
  a default and Matt should get it, edit `BepInEx\config\<guid>.cfg` too, but only while the game is closed.
- **Don't assume a type hierarchy from a name** (e.g. `SS_Effect_AOE_Line` isn't an `SS_Effect_AOE`). Check
  the base type in the dump.

## Game facts that shaped the mods

- Elites are normal pooled enemies with `ARPGEntity_Module_Elite`. `ActivateElite()` flips them on. There
  are **no per-elite affixes**. Every elite shares one `EliteSettingsSO`.
- Monster card buffs (`MonsterCardManager._activeEffects`) apply to **every** enemy, not per mob type.
- Run end flows through `RunStatsTracker.PopulateRecapData`. The recap's skill list is only filled
  afterwards, in `RunRecapData.PopulateSkillRecap`.
- `SS_Effect_AOE._effectsOnHit` runs once per detected target. A capped AoE is sometimes a target picker
  ("launch an orb at the nearest 3"), not a damage zone.
- The hub is a 3D space with walk-up interactables (`CodexInteractable`, `HubPortal`). `Hub` exists
  only while you're in it.
