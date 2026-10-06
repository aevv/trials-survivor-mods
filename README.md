# TMods — modding Trials Survivors

Tooling and mods for **Trials Survivors** (Angry Wisp). There was no modding scene
for this game when this repo started, so it's set up to make the next mod easy
rather than just to ship the first one.

The game is Unity **2022.3.62f2** / **IL2CPP** x64, so there is no `Managed/`
folder to swap DLLs in. Mods are BepInEx 6 plugins that Harmony-patch the IL2CPP
runtime through Il2CppInterop.

## What's here

| path                            | what it is                                                   |
| ------------------------------- | ------------------------------------------------------------ |
| `tools/dump.ps1`                | Dumps the game's IL2CPP assemblies to readable .NET DLLs      |
| `tools/decompile.ps1`           | Turns those into a greppable per-type C# source tree          |
| `tools/install-bepinex.ps1`     | Installs/removes BepInEx 6 IL2CPP in the game folder          |
| `docs/findings.md`              | Reverse-engineering notes — types, fields, caps, offsets      |
| `src/TrialsSurvivors.UncapAoE/` | Mod: removes the per-skill cap on AoE targets hit             |
| `src/TrialsSurvivors.EliteHealthBars/` | Mod: draws a health bar above each elite enemy        |

## Getting set up

Needs .NET SDK, `gh`, and PowerShell 7.

```powershell
# 1. grab Cpp2IL (prerelease — stable is too old for Unity 6 metadata v31)
gh release download 2022.1.0-pre-release.21 --repo SamboyCoding/Cpp2IL `
   --pattern "Cpp2IL-*-Windows.exe" --output tools\cpp2il.exe

# 2. dump the game so you can read it
tools\dump.ps1

# 3. install the loader (needs the BepInEx IL2CPP zip in tools\, see below)
tools\install-bepinex.ps1

# 4. launch the game once — BepInEx generates BepInEx\interop, which mods build against
```

The loader zip is `BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.<build>.zip` from
<https://builds.bepinex.dev/projects/bepinex_be>. **It must be the IL2CPP line of
BepInEx 6** — BepInEx 5 and the Unity.Mono builds will not load on this game.
Keep the loader build and the `BepInEx.Unity.IL2CPP` NuGet version in step
(currently both `be.788`).

To get the game back to vanilla: `tools\install-bepinex.ps1 -Uninstall`. Nothing
in this repo ever writes to a game file; everything is additive.

## Day to day

There's a `Taskfile.yml` for the loop you'll repeat constantly (`task` with no args lists everything):

| task                         | what it does                                                        |
| ---------------------------- | ------------------------------------------------------------------- |
| `task build`                 | build every mod (`TMods.sln`)                                       |
| `task deploy`                | build and copy every mod into `BepInEx\plugins`, refusing if the game is running (it locks the DLLs) |
| `task deploy:one MOD=RunHud` | same, for one mod                                                   |
| `task play`                  | close the game (asks first), deploy, relaunch through Steam         |
| `task game:status` / `game:start` / `game:stop` | the game process                                 |
| `task logs`                  | mod lines, warnings and errors from `BepInEx\LogOutput.log`         |
| `task logs:errors`           | exceptions from Unity's `Player.log`. UI/TMP errors land here, not in the BepInEx log |
| `task runs`                  | runs recorded by the run history mod                                |
| `task dump` / `task decompile` | wrappers for the scripts below                                    |

Pass `GAME_PATH=...` to any task if the game isn't in the default Steam library.

## Reading the game

Dump once, then grep — you shouldn't need to re-dump to work out what to patch.

```powershell
dotnet tool install -g ilspycmd
tools\dump.ps1              # IL2CPP -> .NET assemblies
tools\decompile.ps1         # assemblies -> dumps\cs\, one .cs per type
```

That gives you two trees, which answer different questions:

- **`dumps\cs\game\`** — the game as written. Real field names, offsets,
  `[SerializeField]`/`[Tooltip]` attributes, inheritance. Read this to find the
  thing you want to change. ~1550 types for `Assembly-CSharp`.
- **`dumps\cs\interop\`** — the game as a mod sees it, decompiled from the
  Il2CppInterop assemblies. Tells you whether a member is reachable from a plugin
  and what the generated wrapper is called (interop makes `protected` fields into
  public properties). **Check here before writing a patch**, because a member
  being `protected` in the game is not what your plugin will see.

```powershell
rg -n "_limitDetectionCount" dumps/cs/game          # who has the field
rg -ln "class SS_Effect" dumps/cs/game              # what the skill effects are
rg -n "MAX_|const int" dumps/cs/game/Assembly-CSharp  # hunt hard-coded caps
```

Two other formats, for when the C# tree isn't enough:

```powershell
tools\dump.ps1 -Format diffable-cs   # flat, stable-ordered C# per assembly —
                                     # diff two game versions to spot what changed
tools\dump.ps1 -Format isil          # raw instruction stream, for method bodies
```

Be aware of what the dump does and doesn't give you: **types, fields, offsets and
signatures are reliable; most method bodies come out as `throw null`** because
they're native. Control flow needs the `isil` dump or a disassembler. Field
layout is enough for most mods.

Naming conventions in `Assembly-CSharp`: `SO_*` ScriptableObject data assets,
`SS_*` skill-system behaviours, `SSV2_*` skill-system v2 effect instances,
`ARPG*` the core entity/combat layer.

`dumps/` is gitignored — it's ~150MB of the game's own decompiled code, so it
stays reproducible from these scripts rather than committed.

## Mods

### TrialsSurvivors.UncapAoE

Removes the cap on how many enemies a single AoE can hit, so a big explosion into
a full screen hits the whole screen.

```powershell
cd src\TrialsSurvivors.UncapAoE
dotnet build -c Release -p:Deploy=true     # builds and copies into BepInEx\plugins
```

Config at `BepInEx\config\net.aevv.trialssurvivors.uncapaoe.cfg` (written on first
launch):

| setting             | default   | meaning                                                       |
| ------------------- | --------- | ------------------------------------------------------------- |
| `Enabled`           | `true`    | master switch                                                 |
| `Mode`              | `NoLimit` | `NoLimit` / `Multiplier` / `Minimum`                          |
| `Multiplier`        | `4`       | scales the authored cap, keeping relative skill balance        |
| `MinimumTargets`    | `50`      | floor for `Minimum` mode                                       |
| `KeepCapOnSpawners` | `true`    | leave AoEs that launch projectiles/chains per target capped     |
| `UncapAimedProjectiles` | `false` | also uncap aimed projectile launchers (one projectile per enemy) |
| `LogOriginalLimits` | `false`   | logs each distinct authored cap once — good for discovery      |

How it works: each effect carries an authored `_limitDetectionCount` that the
developers themselves document as *"-2 to use formula, -1 for no limit"*.
`NoLimit` mode just writes `-1`, so the mod rides a code path the game already
supports rather than forcing a value past a cap other code assumes is bounded.
The field feeds both the spatial query's `maxResults` and the post-detection
`SortTargets` trim, which is why the mod patches the field rather than the query —
patching the query alone would leave the sort throwing the extra targets away.

It patches `OnPlayBehaviour` on **three** types — `SS_Effect_AOE`,
`SS_Effect_AOE_Line` and `SS_Behaviour_LaunchAimedProjectile`. Despite the names
these are siblings, not a hierarchy, each with its own copy of the field, so
patching only the sphere AoE would quietly leave line/beam and aimed-projectile
effects capped.

Caps the mod deliberately leaves alone: effects authored as `-2` (a designer
formula we can't scale meaningfully) and anything already `-1`.

Some skills use a capped AoE as a **target picker**, not a damage zone: "find the
nearest N enemies and launch an orb at each". `_effectsOnHit` runs once per detected
target, so uncapping those spawns one projectile per enemy on screen (this is what
broke Aqua Nova). The mod asks the game's own `GetChildrenEffectInstance` walk
which effect instances an AoE can produce. If any is an `SSV2_ProjectileInstance` or
`SSV2_ChainingInstance`, the cap is kept. This includes projectiles launched on kill,
so some AoEs stay capped when they didn't strictly need to. Aimed projectile
launchers are the same thing by construction (their cap *is* the projectile count), so
they're only rewritten when `UncapAimedProjectiles` is on. With `LogOriginalLimits`
on, each kept cap is logged as `keeping cap N on <type>: launches <instance> per target`.

Two things to expect:

- **A hard 256 ceiling remains.** `SS_AOEDeferredQueries` batches AoE queries into
  256-wide slabs, so no single AoE reports more than 256 hits while batching is
  on. That's well above a full screen, so it isn't a practical limit, but
  `Multiplier` mode clamps to it so the number you configure means what it says.
- **It costs frametime.** Every AoE now walks more entities and resolves more
  damage. Late endless runs are where you'll feel it.

See `docs/findings.md` for the full picture, including the `ARPGSpatialQueryArgs`
field offsets and the other caps in the codebase.

### TrialsSurvivors.EliteHealthBars

Draws a health bar, with a trailing "damage taken" segment, above every elite on screen.

```powershell
cd src\TrialsSurvivors.EliteHealthBars
dotnet build -c Release -p:Deploy=true
```

Config at `BepInEx\config\net.aevv.trialssurvivors.elitehealthbars.cfg`: `Enabled`, `HideAtFullHealth`,
bar `Width`/`Height`/`Border` (pixels at 1080p, scaled to screen height), `WorldOffset` above the
head, `DelayedBarSpeed`, and `#RRGGBB[AA]` colours.

How it works: a Harmony postfix on `ARPGEntity_Module_Elite.ActivateElite` records each elite.
Enemies are pooled and flip between elite and normal, so instead of trusting a reset/disable hook,
the renderer drops any tracked entry each frame that is no longer `IsElite`, is dead, or is
inactive. Bars are plain uGUI `Image`s on the mod's own screen-space overlay canvas, positioned
from `ARPGEntity.GetTopPosition` through the world camera. The camera comes from
`WorldSpaceCanvasManager._worldCamera` and falls back to `Camera.main`. The chosen camera is
logged (`Diagnostics.LogCamera`) so you can check it's the gameplay one.

The game's own `WorldSpaceUIBindable<T>` system isn't used. Binding to it needs a new IL2CPP
generic instantiation, which is more trouble than an overlay canvas is worth.

## Status

The mod builds, loads cleanly under BepInEx be.788, and all three Harmony patches
apply without error — but **the gameplay effect has not been confirmed in a run
yet.** To verify: set `LogOriginalLimits = true`, play until you have an AoE skill,
and check `BepInEx\LogOutput.log` for lines like:

```
[Info: Trials Survivors: Uncap AoE] authored cap: SS_Effect_AOE = 8
```

Those confirm the prefix is firing on real casts, and tell you the game's real
authored numbers — which is also the data needed to pick a sensible `Multiplier`
or `MinimumTargets` if full uncap turns out to be too much.
