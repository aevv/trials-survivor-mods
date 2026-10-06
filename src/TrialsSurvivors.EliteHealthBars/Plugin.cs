using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;

namespace TrialsSurvivors.EliteHealthBars;

[BepInPlugin(Guid, "Trials Survivors: Elite Health Bars", "0.1.0")]
public sealed class Plugin : BasePlugin
{
    public const string Guid = "net.aevv.trialssurvivors.elitehealthbars";

    internal static Plugin Instance = null!;

    internal ConfigEntry<bool> Enabled = null!;
    internal ConfigEntry<float> Width = null!;
    internal ConfigEntry<float> Height = null!;
    internal ConfigEntry<float> Border = null!;
    internal ConfigEntry<float> WorldOffset = null!;
    internal ConfigEntry<bool> HideAtFullHealth = null!;
    internal ConfigEntry<float> DelayedBarSpeed = null!;
    internal ConfigEntry<string> FillColour = null!;
    internal ConfigEntry<string> DelayedColour = null!;
    internal ConfigEntry<string> BackgroundColour = null!;
    internal ConfigEntry<bool> LogCamera = null!;

    public override void Load()
    {
        Instance = this;

        Enabled = Config.Bind("General", "Enabled", true,
            "Master switch. Turn off to hide all elite health bars without uninstalling.");

        HideAtFullHealth = Config.Bind("General", "HideAtFullHealth", false,
            "Only show a bar once the elite has taken damage.");

        Width = Config.Bind("Layout", "Width", 90f,
            new ConfigDescription("Bar width in pixels at 1080p; scales with screen height.", new AcceptableValueRange<float>(10f, 600f)));

        Height = Config.Bind("Layout", "Height", 10f,
            new ConfigDescription("Bar height in pixels at 1080p; scales with screen height.", new AcceptableValueRange<float>(2f, 100f)));

        Border = Config.Bind("Layout", "Border", 2f,
            new ConfigDescription("Background border around the fill, in pixels at 1080p.", new AcceptableValueRange<float>(0f, 20f)));

        WorldOffset = Config.Bind("Layout", "WorldOffset", 0.5f,
            new ConfigDescription("Distance above the top of the elite, in world units.", new AcceptableValueRange<float>(-5f, 20f)));

        DelayedBarSpeed = Config.Bind("Style", "DelayedBarSpeed", 0.6f,
            new ConfigDescription("How fast the 'damage taken' trail catches up, in bar-widths per second. 0 disables the trail.", new AcceptableValueRange<float>(0f, 10f)));

        FillColour = Config.Bind("Style", "FillColour", "#D6312B", "Health fill colour, as #RRGGBB or #RRGGBBAA.");
        DelayedColour = Config.Bind("Style", "DelayedColour", "#F2D16B", "Damage-taken trail colour, as #RRGGBB or #RRGGBBAA.");
        BackgroundColour = Config.Bind("Style", "BackgroundColour", "#000000B4", "Background colour, as #RRGGBB or #RRGGBBAA.");

        LogCamera = Config.Bind("Diagnostics", "LogCamera", true,
            "Log which camera the bars are projected through whenever it changes.");

        new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);

        AddComponent<EliteHealthBarRenderer>();

        Log.LogInfo("loaded");
    }

    internal static Color ParseColour(string hex, Color fallback)
    {
        var s = hex.Trim().TrimStart('#');
        if (s.Length != 6 && s.Length != 8) return fallback;
        if (!uint.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out var v)) return fallback;
        if (s.Length == 6) v = (v << 8) | 0xFF;
        return new Color(((v >> 24) & 0xFF) / 255f, ((v >> 16) & 0xFF) / 255f, ((v >> 8) & 0xFF) / 255f, (v & 0xFF) / 255f);
    }
}

[HarmonyPatch(typeof(ARPGEntity_Module_Elite), nameof(ARPGEntity_Module_Elite.ActivateElite))]
internal static class ActivateElitePatch
{
    [HarmonyPostfix]
    private static void Postfix(ARPGEntity_Module_Elite __instance)
    {
        EliteTracker.Track(__instance);
    }
}
