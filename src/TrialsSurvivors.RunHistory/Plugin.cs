using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine.InputSystem;

namespace TrialsSurvivors.RunHistory;

[BepInPlugin(Guid, "Trials Survivors: Run History", "0.1.0")]
public sealed class Plugin : BasePlugin
{
    public const string Guid = "net.aevv.trialssurvivors.runhistory";

    internal static Plugin Instance = null!;

    internal ConfigEntry<Key> ToggleKey = null!;
    internal ConfigEntry<bool> HubOnly = null!;

    public override void Load()
    {
        Instance = this;

        ToggleKey = Config.Bind("General", "ToggleKey", Key.F6, "Key that opens and closes the run history panel.");
        HubOnly = Config.Bind("General", "HubOnly", true, "Only allow opening the panel while in the hub.");

        new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);

        AddComponent<RunHistoryPanel>();

        Log.LogInfo($"loaded; {RunStore.All.Count} past runs on disk");
    }
}
