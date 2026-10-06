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
    internal ConfigEntry<float> DpsMeterScale = null!;
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

        DpsMeterScale = Config.Bind("DpsMeter", "Scale", 0.75f,
            new ConfigDescription("Size of the in-run DPS meter. 1 = the game's size. Applied when each run starts.",
                new AcceptableValueRange<float>(0.3f, 2f)));

        Verbose = Config.Bind("Diagnostics", "Verbose", false,
            "Log every slot cycle, every slot key press and every skill module that gets widened.");

        var harmony = new Harmony(Guid);
        harmony.CreateClassProcessor(typeof(KikimeterInitializePatch)).Patch();

        if (!Enabled.Value || SlotCount.Value <= SlotLimitPatches.VanillaSlots)
        {
            Log.LogInfo($"loaded: disabled (enabled={Enabled.Value}, slots={SlotCount.Value})");
            return;
        }

        if (!ImmediatePatcher.TryApply(SlotLimitPatches.For(SlotCount.Value), Log)) return;
        Slots = SlotCount.Value;

        foreach (var type in AccessTools.GetTypesFromAssembly(typeof(Plugin).Assembly))
        {
            if (type != typeof(KikimeterInitializePatch)) harmony.CreateClassProcessor(type).Patch();
        }
        AddComponent<LastSlotKey>();
        AddComponent<SpellBarAligner>();

        Log.LogInfo($"loaded: {Slots} weapon slots, last slot key {LastSlotKeyBinding.Value}");
    }
}
