# Mod projects

One folder per mod, `TrialsSurvivors.<Name>`, in `TSMods.slnx` (`mods` folder) and `TSMods.Mods.slnf`.

Everything shared lives in `Directory.Build.props` / `Directory.Build.targets` here: target framework, the
BepInEx packages, `GamePath`/`InteropPath`, interop reference wiring, `CheckInterop`, `DeployToGame` and
`StampGameBuild`. `StampGameBuild` embeds the Steam buildid, a `GameAssembly.dll` hash and the BepInEx
version as `AssemblyMetadata("TSMods.*")`. The loader uses these to tell whether a mod was built for the
installed game. Don't remove it.

## New mod checklist

1. Copy an existing `.csproj` and rename `AssemblyName`. `Version` there is the only place the plugin
   version is set: the plugin uses `[BepInPlugin(Guid, "...", MyPluginInfo.PLUGIN_VERSION)]`.
2. List the extra interop assemblies you use as `<InteropReference Include="A;B" />`:
   - `Assembly-CSharp`, `UnityEngine.CoreModule` and `Il2Cppmscorlib` are already referenced
   - `UnityEngine.UI` + `UnityEngine.UIModule` for uGUI
   - `Unity.TextMeshPro` for text
   - `Unity.InputSystem` for keys
   - `Il2CppSystem.Core` whenever a signature involves `HashSet`/LINQ-ish Il2Cpp types (the compiler
     will say so with CS0012)
3. Plugin GUID is `net.aevv.trialssurvivors.<lowercasename>`. Display name is `Trials Survivors: <Name>`.
4. `dotnet sln TSMods.slnx add mods/TrialsSurvivors.<Name>/TrialsSurvivors.<Name>.csproj --solution-folder mods`,
   and add the same path to `TSMods.Mods.slnf` so `task deploy` picks it up.
5. Add a README section for it, then `task deploy`. `task tsmods -- check <Name>` confirms every game
   member and Harmony target it uses resolves against the current interop.
6. To ship it, bump `Version`, commit, and run `task release MOD=<Name>` (or `task release:all`). Use `DRY=1`
   first. Mods are released from here, not CI: CI has no game interop to build against.

## Shape of a plugin

- `Plugin : BasePlugin` binds every config entry in `Load`, runs `new Harmony(Guid).PatchAll(...)`, then
  `AddComponent<T>()` for any per-frame component. Expose config as `internal ConfigEntry<T>` fields, read
  through `Plugin.Instance`.
- Every user-facing knob goes in config with a description and an `AcceptableValueRange` where it makes
  sense. Group with sections (`General`, `Layout`, `Style`, `Diagnostics`).
- Harmony patches are small static classes next to the code they feed. Prefer a postfix that records
  state. Parameter names in patch methods must match the game's, as in the interop dump.
- Wrap each independent piece of work done inside a game hook in its own try/catch that logs a warning. One
  missing field shouldn't throw away the rest (see `RunCapture.Capture`).

## UI

- Each UI mod owns a `ScreenSpaceOverlay` canvas. Build it lazily on first use, not in `Awake`.
- Borrow the game's font by finding any live `TextMeshProUGUI` and using its `font`. Do this lazily, once the
  HUD exists.
- `Image` with no sprite renders as a solid rect, which is enough for bars and backgrounds. Generate other
  shapes as a `Texture2D` + `Sprite.Create` (see `OffscreenArrow`).
- Only touch `color`, `sizeDelta`, `text` and the like when the value changes. Setting them every frame
  dirties the canvas.
- Small UI helper classes (`Ui`, `UiFactory`) are duplicated per mod on purpose, so each mod stays standalone.
