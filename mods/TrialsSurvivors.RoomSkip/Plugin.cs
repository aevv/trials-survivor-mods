using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine.InputSystem;

namespace TrialsSurvivors.RoomSkip;

[BepInPlugin(Guid, "Trials Survivors: Room Skip", MyPluginInfo.PLUGIN_VERSION)]
public sealed class Plugin : BasePlugin
{
    public const string Guid = "net.aevv.trialssurvivors.roomskip";

    internal static Plugin Instance = null!;

    internal ConfigEntry<bool> Enabled = null!;
    internal ConfigEntry<Key> SkipKey = null!;
    internal ConfigEntry<bool> ShowBeforeAvailable = null!;
    internal ConfigEntry<float> Width = null!;
    internal ConfigEntry<float> Height = null!;
    internal ConfigEntry<float> Gap = null!;
    internal ConfigEntry<bool> DumpObjectiveUi = null!;

    public override void Load()
    {
        Instance = this;

        Enabled = Config.Bind("General", "Enabled", true, "Show a Skip button under the room objective once its tier 3 goal is met.");
        SkipKey = Config.Bind("General", "SkipKey", Key.None, "Optional key that does the same as clicking Skip. None to disable.");
        ShowBeforeAvailable = Config.Bind("General", "ShowBeforeAvailable", true,
            "Show the button dimmed before tier 3 is reached. Off hides it until it can be used.");

        Width = Config.Bind("Layout", "Width", 0f,
            new ConfigDescription("Button width in the objective panel's canvas units. 0 matches the progress bar's width.", new AcceptableValueRange<float>(0f, 600f)));
        Height = Config.Bind("Layout", "Height", 44f,
            new ConfigDescription("Button height, in the objective panel's canvas units.", new AcceptableValueRange<float>(16f, 200f)));
        Gap = Config.Bind("Layout", "Gap", 10f,
            new ConfigDescription("Space between the objective panel and the button.", new AcceptableValueRange<float>(-200f, 200f)));

        DumpObjectiveUi = Config.Bind("Diagnostics", "DumpObjectiveUi", true,
            "Log the objective panel's hierarchy (rects, sprites, fonts) once per session, the first time a room starts.");

        new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);
        AddComponent<SkipButtonDriver>();

        Log.LogInfo($"loaded: enabled={Enabled.Value}, key={SkipKey.Value}");
    }
}
