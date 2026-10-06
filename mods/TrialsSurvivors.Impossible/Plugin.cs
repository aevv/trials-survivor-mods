using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;

namespace TrialsSurvivors.Impossible;

[BepInPlugin(Guid, "Trials Survivors: Impossible", MyPluginInfo.PLUGIN_VERSION)]
public sealed class Plugin : BasePlugin
{
    public const string Guid = "net.aevv.trialssurvivors.impossible";

    internal static Plugin Instance = null!;

    internal ConfigEntry<bool> Enabled = null!;
    internal ConfigEntry<string> DisplayName = null!;
    internal ConfigEntry<string> NameColour = null!;
    internal ConfigEntry<string> TierText = null!;
    internal ConfigEntry<float> MonsterHealthMultiplier = null!;
    internal ConfigEntry<float> MonsterDamageMultiplier = null!;
    internal ConfigEntry<float> EliteRateMultiplier = null!;
    internal ConfigEntry<float> RedEliteChance = null!;
    internal ConfigEntry<float> RedEliteHealthMultiplier = null!;
    internal ConfigEntry<float> RedEliteDamageMultiplier = null!;
    internal ConfigEntry<string> RedEliteGlowColour = null!;
    internal ConfigEntry<float> RedEliteGlowIntensity = null!;
    internal ConfigEntry<bool> RedEliteHidePinkAura = null!;
    internal ConfigEntry<string> RedEliteBodyColour = null!;
    internal ConfigEntry<float> RedEliteBodyTint = null!;
    internal ConfigEntry<bool> Selected = null!;
    internal ConfigEntry<bool> Verbose = null!;
    internal ConfigEntry<bool> ForceRedElites = null!;

    public override void Load()
    {
        Instance = this;

        Enabled = Config.Bind("General", "Enabled", true,
            "Offer the Impossible difficulty after the last Unfair+ level in the hub's difficulty selector.");
        DisplayName = Config.Bind("General", "DisplayName", "Impossible", "Name shown for the difficulty. Keep it ASCII.");
        NameColour = Config.Bind("General", "NameColour", "#FF2B2B", "Colour of the difficulty name, as an HTML colour.");
        TierText = Config.Bind("General", "TierText", "!!", "Text on the hub selector's tier display while Impossible is selected. Keep it ASCII.");

        MonsterHealthMultiplier = Config.Bind("Difficulty", "MonsterHealthMultiplier", 3f,
            new ConfigDescription("Monster max health relative to Unfair. Applies to the elite health bonus too, so elites are scaled by the same amount.",
                new AcceptableValueRange<float>(1f, 20f)));
        MonsterDamageMultiplier = Config.Bind("Difficulty", "MonsterDamageMultiplier", 3f,
            new ConfigDescription("Multiplier on all damage the player takes from monsters.",
                new AcceptableValueRange<float>(1f, 20f)));
        EliteRateMultiplier = Config.Bind("Difficulty", "EliteRateMultiplier", 2f,
            new ConfigDescription("Multiplier on the room's elite spawn rate. Objective elites (kill X elites, golden frog) aren't affected.",
                new AcceptableValueRange<float>(1f, 10f)));

        RedEliteChance = Config.Bind("RedElites", "Chance", 0.2f,
            new ConfigDescription("Chance that an elite spawns as a red elite instead.", new AcceptableValueRange<float>(0f, 1f)));
        RedEliteHealthMultiplier = Config.Bind("RedElites", "HealthMultiplier", 2f,
            new ConfigDescription("Red elite max health relative to a regular Impossible elite.", new AcceptableValueRange<float>(1f, 20f)));
        RedEliteDamageMultiplier = Config.Bind("RedElites", "DamageMultiplier", 2f,
            new ConfigDescription("Red elite damage to the player relative to a regular Impossible elite.", new AcceptableValueRange<float>(1f, 20f)));
        RedEliteGlowColour = Config.Bind("RedElites", "GlowColour", "#FF1408", "Emission colour painted onto red elites, as an HTML colour.");
        RedEliteGlowIntensity = Config.Bind("RedElites", "GlowIntensity", 1.5f,
            new ConfigDescription("HDR intensity of the red glow. The game's palette emissions sit around 1.", new AcceptableValueRange<float>(0f, 10f)));
        RedEliteHidePinkAura = Config.Bind("RedElites", "HidePinkAura", true, "Turn off the regular elite's pink aura on red elites.");
        RedEliteBodyColour = Config.Bind("RedElites", "BodyColour", "#B30D0D", "Colour the red elite's body palette is blended toward, as an HTML colour.");
        RedEliteBodyTint = Config.Bind("RedElites", "BodyTint", 0.6f,
            new ConfigDescription("How far the body palette is blended toward BodyColour. 0 keeps the mob's own colours, 1 is solid BodyColour.",
                new AcceptableValueRange<float>(0f, 1f)));

        Selected = Config.Bind("State", "Selected", false,
            "Whether Impossible is the selected difficulty. Set by the hub selector; you don't need to edit this.");

        Verbose = Config.Bind("Diagnostics", "Verbose", false, "Log every scaled monster, elite and player hit.");
        ForceRedElites = Config.Bind("Diagnostics", "ForceRedElites", false, "Make every Impossible elite a red elite, for testing the visuals.");

        new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);
        AddComponent<RedEliteRefresher>();

        Log.LogInfo($"loaded: enabled={Enabled.Value}, selected={Selected.Value}, hp x{MonsterHealthMultiplier.Value}, " +
                    $"damage x{MonsterDamageMultiplier.Value}, elite rate x{EliteRateMultiplier.Value}, " +
                    $"red elites {RedEliteChance.Value:P0} (hp x{RedEliteHealthMultiplier.Value}, damage x{RedEliteDamageMultiplier.Value})");
    }

    internal static Color ParseColour(ConfigEntry<string> entry, Color fallback) =>
        ColorUtility.TryParseHtmlString(entry.Value, out var color) ? color : fallback;
}
