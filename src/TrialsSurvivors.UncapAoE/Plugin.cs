using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace TrialsSurvivors.UncapAoE;

[BepInPlugin(Guid, "Trials Survivors: Uncap AoE", "0.1.0")]
public sealed class Plugin : BasePlugin
{
    public const string Guid = "net.aevv.trialssurvivors.uncapaoe";

    internal static Plugin Instance = null!;

    internal ConfigEntry<bool> Enabled = null!;
    internal ConfigEntry<LimitMode> Mode = null!;
    internal ConfigEntry<float> Multiplier = null!;
    internal ConfigEntry<int> MinimumTargets = null!;
    internal ConfigEntry<bool> LogOriginalLimits = null!;

    public override void Load()
    {
        Instance = this;

        Enabled = Config.Bind("General", "Enabled", true,
            "Master switch. Turn off to restore the game's own target caps without uninstalling.");

        Mode = Config.Bind("General", "Mode", LimitMode.NoLimit,
            "How to rewrite each AoE's target cap.\n" +
            "NoLimit   - every AoE hits everything in radius (the game's own -1 'no limit' path).\n" +
            "Multiplier- multiply the authored cap, keeping relative skill balance.\n" +
            "Minimum   - raise any cap below MinimumTargets up to it, leave higher caps alone.");

        Multiplier = Config.Bind("General", "Multiplier", 4f,
            new ConfigDescription("Used when Mode is Multiplier.", new AcceptableValueRange<float>(1f, 64f)));

        MinimumTargets = Config.Bind("General", "MinimumTargets", 50,
            new ConfigDescription("Used when Mode is Minimum.", new AcceptableValueRange<int>(1, 256)));

        LogOriginalLimits = Config.Bind("Diagnostics", "LogOriginalLimits", false,
            "Log each distinct authored cap the game asks for, once per value. Useful for working " +
            "out the game's real numbers before deciding on a Mode.");

        var harmony = new Harmony(Guid);
        harmony.PatchAll(typeof(AoeLimitPatch));

        Log.LogInfo($"loaded; mode={Mode.Value}");
    }
}

public enum LimitMode
{
    NoLimit,
    Multiplier,
    Minimum
}

/// <summary>
/// Rewrites the per-effect AoE target cap.
///
/// <para>Each <c>SS_Effect_AOE</c> carries an authored <c>_limitDetectionCount</c>, which the
/// devs document as "-2 to use formula, -1 for no limit" and otherwise treat as a hard cap. It
/// feeds both the spatial query's <c>maxResults</c> and the post-detection <c>SortTargets</c>
/// trim, so rewriting the field covers the whole chain — patching the query alone would still
/// leave the sort discarding the extra targets.</para>
///
/// <para>We hook <c>OnPlayBehaviour</c> rather than <c>OnInitBehaviour</c> because behaviours are
/// cloned per entity and per skill instance (<c>CopyFromInstanceInternal</c>), so an init-time
/// write can be copied over. A single int compare per cast is cheaper than tracking clones.</para>
/// </summary>
[HarmonyPatch]
internal static class AoeLimitPatch
{
    /// <summary>Authored caps we've already reported, so diagnostics stay one line per value.</summary>
    private static readonly HashSet<int> LoggedLimits = new();

    /// <summary>
    /// Original cap per instance, so repeated casts rewrite from the authored value rather than
    /// compounding the multiplier on an already-raised one.
    /// </summary>
    private static readonly Dictionary<nint, int> AuthoredLimits = new();

    [HarmonyPostfix]
    [HarmonyPatch(typeof(SS_Effect_AOE), nameof(SS_Effect_AOE.OnPlayBehaviour))]
    private static void BeforeAoePlay(SS_Effect_AOE __instance)
    {
        var plugin = Plugin.Instance;
        if (!plugin.Enabled.Value) return;

        var key = __instance.Pointer;
        if (!AuthoredLimits.TryGetValue(key, out var authored))
        {
            authored = __instance._limitDetectionCount;
            AuthoredLimits[key] = authored;

            if (plugin.LogOriginalLimits.Value && LoggedLimits.Add(authored))
            {
                plugin.Log.LogInfo($"authored AoE cap seen: {Describe(authored)}");
            }
        }

        // -1 is already unlimited; -2 defers to a designer formula we have no safe way to scale,
        // so leave both alone rather than guess.
        if (authored < 0) return;

        var rewritten = plugin.Mode.Value switch
        {
            LimitMode.NoLimit => NoLimit,
            LimitMode.Multiplier => ScaleWithinSlab(authored, plugin.Multiplier.Value),
            LimitMode.Minimum => authored < plugin.MinimumTargets.Value ? plugin.MinimumTargets.Value : authored,
            _ => authored
        };

        if (__instance._limitDetectionCount != rewritten)
        {
            __instance._limitDetectionCount = rewritten;
        }
    }

    private const int NoLimit = -1;

    /// <summary>
    /// SS_AOEDeferredQueries batches queries into 256-wide slabs, so a finite cap above that is
    /// never honoured. Clamp so the configured number means what it says.
    /// </summary>
    private const int DeferredSlabSize = 256;

    private static int ScaleWithinSlab(int authored, float multiplier)
    {
        var scaled = (long)(authored * multiplier);
        return scaled >= DeferredSlabSize ? DeferredSlabSize : (int)scaled;
    }

    private static string Describe(int limit) => limit switch
    {
        -1 => "-1 (no limit)",
        -2 => "-2 (formula)",
        _ => limit.ToString()
    };
}
