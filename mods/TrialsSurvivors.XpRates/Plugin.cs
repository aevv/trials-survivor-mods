using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace TrialsSurvivors.XpRates;

[BepInPlugin(Guid, "Trials Survivors: XP Rates", MyPluginInfo.PLUGIN_VERSION)]
public sealed class Plugin : BasePlugin
{
    public const string Guid = "net.aevv.trialssurvivors.xprates";

    internal static Plugin Instance = null!;

    internal ConfigEntry<bool> Enabled = null!;
    internal ConfigEntry<float> OrbMultiplier = null!;
    internal ConfigEntry<float> PickupMultiplier = null!;
    internal ConfigEntry<bool> Verbose = null!;

    public override void Load()
    {
        Instance = this;

        Enabled = Config.Bind("General", "Enabled", true, "Scale in-run XP. Class/global level XP is never touched.");
        OrbMultiplier = Config.Bind("General", "OrbMultiplier", 2f,
            new ConfigDescription("Multiplier on XP from orbs dropped by enemies, applied after the game's own XP bonuses. 1 = vanilla.",
                new AcceptableValueRange<float>(0f, 20f)));
        PickupMultiplier = Config.Bind("General", "PickupMultiplier", 1f,
            new ConfigDescription("Multiplier on XP granted by other collectibles (XP pickups that aren't orbs). 1 = vanilla.",
                new AcceptableValueRange<float>(0f, 20f)));

        Verbose = Config.Bind("Diagnostics", "Verbose", false, "Log every XP grant with its original and scaled value.");

        new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);

        Log.LogInfo($"loaded: enabled={Enabled.Value}, orbs x{OrbMultiplier.Value}, pickups x{PickupMultiplier.Value}");
    }
}
