using System.Collections.Generic;
using UnityEngine;

namespace TrialsSurvivors.EliteHealthBars;

internal readonly record struct BuffIcon(Sprite Icon, bool IsDebuff, int Stacks);

internal static class BuffReader
{
    private readonly record struct BuffInfo(Sprite? Icon, bool IsDebuff, bool Hidden);

    private static readonly Dictionary<nint, BuffInfo> InfoCache = new();

    public static void Read(ARPGEntity entity, List<BuffIcon> into, int max)
    {
        into.Clear();

        var container = entity._buffDebuffGrouperContainer;
        if (container == null) return;

        var groupers = container._buffDebuffGrouperList;
        if (groupers == null) return;

        for (var i = 0; i < groupers.Count && into.Count < max; i++)
        {
            var grouper = groupers[i];
            if (grouper == null || grouper.PendingKill) continue;

            var info = Describe(grouper.BuffDebuffData);
            if (info.Hidden || info.Icon == null) continue;

            into.Add(new BuffIcon(info.Icon, info.IsDebuff, grouper.ActiveStackCount));
        }
    }

    private static BuffInfo Describe(SSV2_SO_BuffDebuffData? data)
    {
        if (data == null) return new BuffInfo(null, false, true);
        if (InfoCache.TryGetValue(data.Pointer, out var cached)) return cached;

        var flags = data._buffDebuffData;
        var info = new BuffInfo(data._identity.Icon, flags.isDebuff, flags.hideInUI);
        InfoCache[data.Pointer] = info;
        return info;
    }
}
