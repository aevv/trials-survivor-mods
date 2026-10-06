# TSMods loader

The mod manager. Windows-only for now, but nothing outside `GameLocator`'s registry lookup is Windows-specific;
keep it that way. Targets net10.0 with warnings as errors (`loader/Directory.Build.props`).

| project              | what it is                                                                                  |
| -------------------- | ------------------------------------------------------------------------------------------- |
| `TSMods.Core`        | All the logic. No UI types.                                                                  |
| `TSMods.Cli`         | `tsmods`, a thin hand-rolled command switch over Core. Anything the app can do, this can too. |
| `TSMods.Loader`      | Avalonia 12 app (`TSMods.Loader.exe`), MVVM with CommunityToolkit.Mvvm partial properties.    |
| `TSMods.Core.Tests`  | xunit v3 on Microsoft.Testing.Platform (opted in via `global.json`).                         |
| `TSMods.Loader.Tests`| Headless Avalonia: renders the window, fails on any binding warning, drives the config editor. |

## How Core is laid out

- `Game/`: `GameLocator` (Steam registry + `libraryfolders.vdf` + appmanifest), `GameState` (buildid, BepInEx
  version, interop freshness, running), `Doorstop` (mods on/off), `BepInExInstaller` (pinned be.788 URL).
- `Mods/ModInspector` reads `[BepInPlugin]`, `[BepInDependency]` and the `TSMods.*` build stamp with Cecil from
  a byte copy, so it never locks plugin files.
- `Compatibility/CompatibilityChecker` resolves every TypeRef/MemberRef that points into an interop assembly, plus
  `[HarmonyPatch]` targets, against `BepInEx\interop`. `ModHealthCheck` combines that with the stamp. Interop older
  than `GameAssembly.dll` means "pending", never "broken".
- `Config/ConfigDocument` parses BepInEx cfg comments into typed settings and edits values line by line, leaving the
  rest of the file alone.
- `Library/` holds versions as `library/<guid>/<version>+<hash8>/{entry.json,files/...}`. Profiles are JSON files.
  `ModManager` ties plugins, library and profiles together and refuses changes while the game runs.

`TSMODS_HOME` overrides `%LocalAppData%\TSMods`. Use it, pointed at the scratchpad, when trying CLI commands that
write, so Matt's real library isn't touched.

## Verifying UI changes

You can't see the window, so render it: `task loader:screenshots` writes PNGs of each tab against the real install to
`dumps\screenshots`, using a throwaway library. Read them before saying a layout change works. The non-explicit
`Window_renders_mods_and_settings_without_binding_errors` test catches broken compiled bindings, which otherwise
fail silently.

## Gotchas

- Avalonia 12 has compiled bindings on by default, so every template needs `x:DataType`.
- `HeadlessUnitTestSession` hangs on `Dispose`. Tests share one session for the process.
- `TSMods.exe` and `tsmods.exe` would collide on Windows' case-insensitive filesystem when published together,
  so the app's assembly is `TSMods.Loader`.
- The `Explicit` screenshot test runs through the xunit exe, not `dotnet test`:
  `TSMods.Loader.Tests.exe -explicit only -method "*Capture*"`.
