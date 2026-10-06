using System;
using HarmonyLib;

namespace TrialsSurvivors.RoomSkip;

[HarmonyPatch(typeof(ChunkObjectiveUI), nameof(ChunkObjectiveUI.Setup))]
internal static class ObjectiveUiSetupPatch
{
    [HarmonyPostfix]
    private static void Postfix(ChunkObjectiveUI __instance, ChunkObjective objective)
    {
        if (!Plugin.Instance.Enabled.Value) return;

        try
        {
            UiDump.Once(__instance);
        }
        catch (Exception e)
        {
            Plugin.Instance.Log.LogWarning($"couldn't dump the objective panel: {e.Message}");
        }

        try
        {
            SkipButton.Attach(__instance, objective);
        }
        catch (Exception e)
        {
            Plugin.Instance.Log.LogWarning($"couldn't add the skip button: {e}");
        }
    }
}

[HarmonyPatch(typeof(ChunkObjectiveUI), nameof(ChunkObjectiveUI.Teardown))]
internal static class ObjectiveUiTeardownPatch
{
    [HarmonyPrefix]
    private static void Prefix(ChunkObjectiveUI __instance)
    {
        try
        {
            SkipButton.Detach(__instance);
        }
        catch (Exception e)
        {
            Plugin.Instance.Log.LogWarning($"couldn't remove the skip button: {e.Message}");
        }
    }
}
