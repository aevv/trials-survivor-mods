# TSMods — modding Trials Survivors

Tooling, mods and a mod manager for **Trials Survivors** (Angry Wisp).

The game is Unity **2022.3.62f2** / **IL2CPP** x64, so there is no `Managed/`
folder to swap DLLs in. Mods are BepInEx 6 plugins that Harmony-patch the IL2CPP
runtime through Il2CppInterop.


# Screens

<img width="1182" height="790" alt="image" src="https://github.com/user-attachments/assets/d4ccd17c-e5d9-4d2a-a732-c3c27c73b11b" />
<img width="1600" height="1000" alt="image" src="https://github.com/user-attachments/assets/8bc53aab-fdde-464d-bd85-0ef9d9a9258b" />
<img width="1600" height="1000" alt="image" src="https://github.com/user-attachments/assets/31d5f3ea-f38f-4b7f-92c9-2a9225dd7090" />

## What's here

| path                     | what it is                                                                  |
| ------------------------ | --------------------------------------------------------------------------- |
| `mods/`                  | The BepInEx plugins, one `TrialsSurvivors.<Name>/` folder each, sharing `mods/Directory.Build.*` |
| `loader/`                | TSMods, the mod manager: `TSMods.Core`, the `tsmods` CLI, the Avalonia app and their tests |
| `tools/dump.ps1`         | Dumps the game's IL2CPP assemblies to readable .NET DLLs                    |
| `tools/decompile.ps1`    | Turns those into a greppable per-type C# source tree                        |
| `tools/install-bepinex.ps1` | Installs/removes BepInEx 6 IL2CPP in the game folder                     |
| `tools/release.ps1`      | Publishes mods as GitHub releases the loader can install                    |
| `.github/workflows/loader.yml` | Tests the loader, and releases it when its version is bumped          |
| `docs/findings.md`       | Reverse-engineering notes — types, fields, caps, offsets                    |
| `TSMods.slnx`            | Everything. `TSMods.Mods.slnf` is just the mods, which is what `task deploy` builds |

## Getting set up

Needs .NET SDK, `gh`, and PowerShell 7.

```powershell
# 1. grab Cpp2IL (prerelease — stable is too old for Unity 6 metadata v31)
gh release download 2022.1.0-pre-release.21 --repo SamboyCoding/Cpp2IL `
   --pattern "Cpp2IL-*-Windows.exe" --output tools\cpp2il.exe

# 2. dump the game so you can read it
tools\dump.ps1

# 3. install BepInEx (needs the IL2CPP zip in tools\, see below; the TSMods app can do this too)
tools\install-bepinex.ps1

# 4. launch the game once — BepInEx generates BepInEx\interop, which mods build against
```

The BepInEx zip is `BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.<build>.zip` from
<https://builds.bepinex.dev/projects/bepinex_be>. **It must be the IL2CPP line of
BepInEx 6** — BepInEx 5 and the Unity.Mono builds will not load on this game.
Keep the BepInEx build and the `BepInEx.Unity.IL2CPP` NuGet version in step
(currently both `be.788`).

To get the game back to vanilla: `tools\install-bepinex.ps1 -Uninstall`. Nothing
in this repo ever writes to a game file; everything is additive.

## Day to day

There's a `Taskfile.yml` for the loop you'll repeat constantly (`task` with no args lists everything):

| task                         | what it does                                                        |
| ---------------------------- | ------------------------------------------------------------------- |
| `task build`                 | build every mod and the loader (`TSMods.slnx`)                      |
| `task test`                  | run the loader tests                                                |
| `task deploy`                | build and copy every mod into `BepInEx\plugins`, refusing if the game is running (it locks the DLLs) |
| `task deploy:one MOD=RunHud` | same, for one mod                                                   |
| `task play`                  | close the game (asks first), deploy, relaunch through Steam         |
| `task game:status` / `game:start` / `game:stop` | the game process                                 |
| `task logs`                  | mod lines, warnings and errors from `BepInEx\LogOutput.log`         |
| `task logs:errors`           | exceptions from Unity's `Player.log`. UI/TMP errors land here, not in the BepInEx log |
| `task runs`                  | runs recorded by the run history mod                                |
| `task dump` / `task decompile` | wrappers for the scripts below                                    |
| `task loader`                | run the TSMods app                                                  |
| `task tsmods -- <args>`      | run the `tsmods` CLI, e.g. `task tsmods -- check`                   |
| `task loader:publish`        | single-file `TSMods.Loader.exe` and `tsmods.exe` in `dist\loader`   |
| `task loader:screenshots`    | render the app headlessly against the real install into `dumps\screenshots` |
| `task release MOD=RunHud`    | publish a mod as a GitHub release (`DRY=1` to preview)              |
| `task release:all`           | publish every mod whose version isn't released yet (`DRY=1` to preview) |

Pass `GAME_PATH=...` to any task if the game isn't in the default Steam library.

## TSMods loader

A Windows app (Avalonia, .NET 10) for managing the mods on an install, plus a `tsmods` CLI
that does everything the app does. Both sit on `TSMods.Core`.

- **Swap mods while the game is closed.** Toggling a mod off moves it out of `BepInEx\plugins`
  into a library at `%LocalAppData%\TSMods\library`, so nothing is lost. Every distinct build
  the loader sees is kept there (keyed by version and hash), including each `task deploy`, so
  you can roll back to an earlier build from the Versions tab or `tsmods enable RunHud 0.1.0+051f`.
  Every change is refused while the game runs.
- **Profiles** save which mods and which builds are installed, and apply that set later.
- **Edit configs.** BepInEx `.cfg` files describe their own types, defaults, ranges and allowed
  values, so the Settings tab renders toggles, sliders, dropdowns and colour swatches from them.
  Saving rewrites only the changed lines. A config only exists once the mod has loaded in game.
- **Check mods against the installed game.** Two checks:
  - The build stamp. `mods/Directory.Build.targets` embeds the Steam buildid, a `GameAssembly.dll`
    hash and the BepInEx version into each mod as `AssemblyMetadata`. The loader compares
    that with what's installed.
  - Whether it still binds. The loader walks every game type, member and Harmony patch target the
    mod references and resolves them against `BepInEx\interop`. A game update that renames a
    patched method shows up as `BROKEN: missing patch target Game.Type.Method` before you launch.
    BepInEx only regenerates interop on the first launch after an update. Until then the
    loader says the check is pending instead of guessing.
- **Mods on/off** flips `enabled` in `doorstop_config.ini`, so you can launch vanilla without
  uninstalling anything. **Install BepInEx** downloads the pinned be.788 build.
- **Get mods** lists releases on `aevv/trials-survivor-mods`. `task release MOD=<Name>`
  builds a mod, reads its stamp and creates a `<name>-v<version>` release with the DLL attached.

### Installing it

Grab `TSMods-<version>-win-x64.zip` from the [latest release](https://github.com/aevv/trials-survivor-mods/releases/latest),
unzip it anywhere and run `TSMods.Loader.exe`. It finds the game through Steam, installs BepInEx if
it's missing, and **Get mods** installs any of the mods below. Launch the game once after installing
BepInEx so it can generate its interop assemblies.

### Releasing

- **The loader** is released by CI. Bump `<Version>` in `loader/Directory.Build.props` and push to
  main. `.github/workflows/loader.yml` runs the tests, publishes both exes and creates a
  `loader-v<version>` release with the zip, marked as the repo's latest release. Every run also
  uploads the zip as a build artifact, PRs included.
- **Mods** are released from a machine with the game installed, because they compile against the
  game's `BepInEx\interop` assemblies, which can't go in a public repo. Bump `<Version>` in the mod's
  csproj, commit, then `task release:all` ships every mod whose version has no tag yet. It refuses mods
  with uncommitted changes. Mod releases never take the latest flag, so that keeps pointing at the loader.

```powershell
task tsmods -- status          # game, build, BepInEx, interop freshness
task tsmods -- list            # every mod with its compatibility
task tsmods -- check runhud    # what was checked and what's missing
task tsmods -- config elitehealthbars ArrowSize 40
task tsmods -- profile save everything
```

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


