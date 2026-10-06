using Il2CppSystem.Collections.Generic;

namespace TrialsSurvivors.UncapAoE;

internal static class SpawnDetector
{
    internal static string? FindSpawnPerTarget(SS_Behaviour effect)
    {
        var children = new List<SSV2_EffectInstance>();
        effect.GetChildrenEffectInstance(ref children, null);

        for (var i = 0; i < children.Count; i++)
        {
            var child = children[i];
            if (child == null) continue;
            if (child.TryCast<SSV2_ProjectileInstance>() != null) return nameof(SSV2_ProjectileInstance);
            if (child.TryCast<SSV2_ChainingInstance>() != null) return nameof(SSV2_ChainingInstance);
        }

        return null;
    }
}
