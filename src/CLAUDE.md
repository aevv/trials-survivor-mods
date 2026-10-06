# Mod projects

One folder per mod, `TrialsSurvivors.<Name>`, all in `TMods.sln`.

## New mod checklist

1. Copy an existing `.csproj` and rename `AssemblyName`. Keep the `GamePath`/`InteropPath` properties,
   the `CheckInterop` target and the `DeployToGame` target as they are.
2. Reference only the interop assemblies you use, from `$(InteropPath)` with `<Private>false</Private>`:
   - `Assembly-CSharp`, `UnityEngine.CoreModule` and `Il2Cppmscorlib` are always needed
   - `UnityEngine.UI` + `UnityEngine.UIModule` for uGUI
   - `Unity.TextMeshPro` for text
   - `Unity.InputSystem` for keys
   - `Il2CppSystem.Core` whenever a signature involves `HashSet`/LINQ-ish Il2Cpp types (the compiler
     will say so with CS0012)
3. Plugin GUID is `net.aevv.trialssurvivors.<lowercasename>`. Display name is `Trials Survivors: <Name>`.
4. `dotnet sln TMods.sln add src/TrialsSurvivors.<Name>/TrialsSurvivors.<Name>.csproj --in-root`.
5. Add a README section for it, then `task deploy`.

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
