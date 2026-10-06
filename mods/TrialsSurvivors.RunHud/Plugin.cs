using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace TrialsSurvivors.RunHud;

[BepInPlugin(Guid, "Trials Survivors: Run HUD", MyPluginInfo.PLUGIN_VERSION)]
public sealed class Plugin : BasePlugin
{
    public const string Guid = "net.aevv.trialssurvivors.runhud";

    internal static Plugin Instance = null!;

    internal ConfigEntry<bool> Enabled = null!;
    internal ConfigEntry<bool> ShowKills = null!;
    internal ConfigEntry<bool> ShowEliteKills = null!;
    internal ConfigEntry<bool> ShowRooms = null!;
    internal ConfigEntry<bool> ShowMonsterModifiers = null!;
    internal ConfigEntry<float> RefreshInterval = null!;
    internal ConfigEntry<float> FontScale = null!;
    internal ConfigEntry<float> OffsetX = null!;
    internal ConfigEntry<float> OffsetY = null!;

    public override void Load()
    {
        Instance = this;

        Enabled = Config.Bind("General", "Enabled", true, "Show live run stats to the right of the XP bar.");
        ShowKills = Config.Bind("General", "ShowKills", true, "Show total kills this run.");
        ShowEliteKills = Config.Bind("General", "ShowEliteKills", true, "Show elite kills this run.");
        ShowRooms = Config.Bind("General", "ShowRooms", true, "Show rooms completed this run.");
        ShowMonsterModifiers = Config.Bind("General", "ShowMonsterModifiers", true,
            "Show a second line summarising the monster card buffs currently applied to every enemy.");
        RefreshInterval = Config.Bind("General", "RefreshInterval", 0.25f,
            new ConfigDescription("Seconds between text refreshes.", new AcceptableValueRange<float>(0.05f, 5f)));

        FontScale = Config.Bind("Layout", "FontScale", 0.45f,
            new ConfigDescription("Text size relative to the XP bar's level number.", new AcceptableValueRange<float>(0.1f, 2f)));
        OffsetX = Config.Bind("Layout", "OffsetX", 8f, "Pixels in from the right end of the XP bar.");
        OffsetY = Config.Bind("Layout", "OffsetY", 6f, "Pixels above the top of the XP bar.");

        new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);

        AddComponent<RunHudUpdater>();

        Log.LogInfo("loaded");
    }
}

[HarmonyPatch(typeof(RunStatsTracker), nameof(RunStatsTracker.BeginRun))]
internal static class BeginRunPatch
{
    [HarmonyPostfix]
    private static void Postfix(RunStatsTracker __instance) => RunHudState.Tracker = __instance;
}

[HarmonyPatch(typeof(RunStatsTracker), nameof(RunStatsTracker.Cleanup))]
internal static class CleanupPatch
{
    [HarmonyPostfix]
    private static void Postfix(RunStatsTracker __instance)
    {
        if (RunHudState.Tracker != null && RunHudState.Tracker.Pointer == __instance.Pointer) RunHudState.Tracker = null;
    }
}

[HarmonyPatch(typeof(UI_Module_XPBar), nameof(UI_Module_XPBar.OnInitialize))]
internal static class XpBarInitializePatch
{
    [HarmonyPostfix]
    private static void Postfix(UI_Module_XPBar __instance) => RunHudState.AttachTo(__instance);
}
