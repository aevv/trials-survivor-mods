using System.Collections.Generic;

namespace TrialsSurvivors.EliteHealthBars;

internal sealed class TrackedElite
{
    public TrackedElite(nint key, ARPGEntity_Module_Elite module)
    {
        Key = key;
        Module = module;
    }

    public nint Key { get; }
    public ARPGEntity_Module_Elite Module { get; }
    public ARPGEntity? Entity { get; set; }
    public ARPGEntity_Module_Health? Health { get; set; }
    public float DelayedFraction { get; set; } = 1f;
}

internal static class EliteTracker
{
    private static readonly Dictionary<nint, TrackedElite> Tracked = new();
    private static readonly List<nint> Stale = new();

    public static IEnumerable<TrackedElite> All => Tracked.Values;
    public static int Count => Tracked.Count;
    public static int Activations { get; private set; }

    public static void Track(ARPGEntity_Module_Elite module)
    {
        Activations++;
        var key = module.Pointer;
        Tracked[key] = new TrackedElite(key, module);
    }

    public static void MarkStale(TrackedElite elite)
    {
        Stale.Add(elite.Key);
    }

    public static void Sweep()
    {
        foreach (var key in Stale) Tracked.Remove(key);
        Stale.Clear();
    }
}
