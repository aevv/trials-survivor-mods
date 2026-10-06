using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine.InputSystem;

namespace TrialsSurvivors.WeaponSlots;

[BepInPlugin(Guid, "Trials Survivors: Weapon Slots", MyPluginInfo.PLUGIN_VERSION)]
public sealed class Plugin : BasePlugin
{
    public const string Guid = "net.aevv.trialssurvivors.weaponslots";

    internal static Plugin Instance = null!;

    internal ConfigEntry<bool> Enabled = null!;
    internal ConfigEntry<int> SlotCount = null!;
    internal ConfigEntry<Key> LastSlotKeyBinding = null!;
    internal ConfigEntry<bool> Verbose = null!;

    internal int Slots { get; private set; } = SlotLimitPatches.VanillaSlots;

    public override void Load()
    {
        Instance = this;

        Enabled = Config.Bind("General", "Enabled", true, "Raise the weapon slot cap. Read at game start; restart after changing.");
        SlotCount = Config.Bind("General", "SlotCount", 6,
            new ConfigDescription("Weapon slots per run. 5 = vanilla. Read at game start; restart after changing.",
                new AcceptableValueRange<int>(SlotLimitPatches.VanillaSlots, 8)));
        LastSlotKeyBinding = Config.Bind("General", "LastSlotKey", Key.Digit6,
            "Key that selects the last weapon slot, since the game only binds keys for the first five. None to disable.");

        Verbose = Config.Bind("Diagnostics", "Verbose", false,
            "Log every slot cycle, every slot key press and every skill module that gets widened.");

        if (!Enabled.Value || SlotCount.Value <= SlotLimitPatches.VanillaSlots)
        {
            Log.LogInfo($"loaded: disabled (enabled={Enabled.Value}, slots={SlotCount.Value})");
            return;
        }

        if (!ImmediatePatcher.TryApply(SlotLimitPatches.For(SlotCount.Value), Log)) return;
        Slots = SlotCount.Value;

        new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);
        AddComponent<LastSlotKey>();
        AddComponent<SpellBarAligner>();

        Log.LogInfo($"loaded: {Slots} weapon slots, last slot key {LastSlotKeyBinding.Value}");
    }
}
