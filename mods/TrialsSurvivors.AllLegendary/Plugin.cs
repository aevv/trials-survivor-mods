using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace TrialsSurvivors.AllLegendary;

[BepInPlugin(Guid, "Trials Survivors: Oops! All Legendary!", MyPluginInfo.PLUGIN_VERSION)]
public sealed class Plugin : BasePlugin
{
    public const string Guid = "net.aevv.trialssurvivors.alllegendary";

    internal static Plugin Instance = null!;

    internal ConfigEntry<bool> Enabled = null!;
    internal ConfigEntry<string> Quality = null!;
    internal ConfigEntry<bool> FallbackToBest = null!;
    internal ConfigEntry<bool> Verbose = null!;

    public override void Load()
    {
        Instance = this;

        Enabled = Config.Bind("General", "Enabled", true, "Upgrade every level-up card to the target quality.");
        Quality = Config.Bind("General", "Quality", "Legendary",
            "Quality to force, matched against the quality's asset name or translation key. The log lists every quality on the first level up.");
        FallbackToBest = Config.Bind("General", "FallbackToBest", true,
            "When a card has no tier at the target quality, upgrade it to the best quality it does have instead of leaving its roll alone.");

        Verbose = Config.Bind("Diagnostics", "Verbose", false, "Log every card's rolled and forced quality.");

        new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);

        Log.LogInfo($"loaded: enabled={Enabled.Value}, quality={Quality.Value}, fallbackToBest={FallbackToBest.Value}");
    }
}
