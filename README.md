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
| `tools/install-bepinex.ps1`     | Installs/removes BepInEx 6 IL2CPP in the game folder          |
| `docs/findings.md`              | Reverse-engineering notes — types, fields, caps, offsets      |
| `src/TrialsSurvivors.UncapAoE/` | Mod: removes the per-skill cap on AoE targets hit             |

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

## Reading the game

```powershell
tools\dump.ps1                      # dll_il_recovery — decompilable assemblies
tools\dump.ps1 -Format isil         # raw instruction stream, when you need bodies
dotnet tool install -g ilspycmd
ilspycmd -t SS_Effect_AOE dumps\dll_il_recovery\Assembly-CSharp.dll
```

Be aware of what the dump does and doesn't give you: **types, fields, offsets and
signatures are reliable; most method bodies come out as `throw null`** because
they're native. Control flow needs the `isil` dump or a disassembler. Field
layout is enough for most mods.

Naming conventions in `Assembly-CSharp`: `SO_*` ScriptableObject data assets,
`SS_*` skill-system behaviours, `SSV2_*` skill-system v2 effect instances,
`ARPG*` the core entity/combat layer.

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
| `LogOriginalLimits` | `false`   | logs each distinct authored cap once — good for discovery      |

How it works: every `SS_Effect_AOE` carries an authored `_limitDetectionCount`
that the developers themselves document as *"-2 to use formula, -1 for no limit"*.
`NoLimit` mode just writes `-1`, so the mod rides a code path the game already
supports rather than forcing a value past a cap other code assumes is bounded.
The field feeds both the spatial query's `maxResults` and the post-detection
`SortTargets` trim, which is why the mod patches the field rather than the query —
patching the query alone would leave the sort throwing the extra targets away.

Caps the mod deliberately leaves alone: effects authored as `-2` (a designer
formula we can't scale meaningfully) and anything already `-1`.

Two things to expect:

- **A hard 256 ceiling remains.** `SS_AOEDeferredQueries` batches AoE queries into
  256-wide slabs, so no single AoE reports more than 256 hits while batching is
  on. That's well above a full screen, so it isn't a practical limit, but
  `Multiplier` mode clamps to it so the number you configure means what it says.
- **It costs frametime.** Every AoE now walks more entities and resolves more
  damage. Late endless runs are where you'll feel it.

See `docs/findings.md` for the full picture, including the `ARPGSpatialQueryArgs`
field offsets and the other caps in the codebase.

## Status

The mod builds, loads cleanly under BepInEx be.788, and its Harmony patch applies
without error — but **the gameplay effect has not been confirmed in a run yet.**
To verify: set `LogOriginalLimits = true`, play until you have an AoE skill, and
check `BepInEx\LogOutput.log` for `authored AoE cap seen: …` lines. Those tell you
the game's real authored numbers, which is also the data needed to pick a sensible
`Multiplier` or `MinimumTargets`.
