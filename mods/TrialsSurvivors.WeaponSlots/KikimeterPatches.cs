using System;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace TrialsSurvivors.WeaponSlots;

[HarmonyPatch(typeof(UI_Module_Kikimeter), nameof(UI_Module_Kikimeter.Awake))]
internal static class KikimeterAwakePatch
{
    [HarmonyPostfix]
    private static void Postfix(UI_Module_Kikimeter __instance)
    {
        try
        {
            var wanted = Plugin.Instance.Slots;
            var before = __instance._rows?.Length ?? 0;
            if (before >= wanted) return;

            __instance._rows = Widen(__instance._rows, wanted);
            __instance._skillsBySlot = Widen(__instance._skillsBySlot, wanted);
            __instance._dpsBuffer = Widen(__instance._dpsBuffer, wanted);
            __instance._visibleRowIndices = Widen(__instance._visibleRowIndices, wanted);
            __instance._hiddenRowIndices = Widen(__instance._hiddenRowIndices, wanted);
            __instance._percentIntBuffer = Widen(__instance._percentIntBuffer, wanted);
            __instance._percentFractionBuffer = Widen(__instance._percentFractionBuffer, wanted);
            __instance._percentRankIndices = Widen(__instance._percentRankIndices, wanted);

            Plugin.Instance.Log.LogInfo($"dps meter '{__instance.name}': widened rows {before} -> {wanted}");
        }
        catch (Exception e)
        {
            Plugin.Instance.Log.LogWarning($"couldn't widen the dps meter: {e.Message}");
        }
    }

    private static Il2CppReferenceArray<T> Widen<T>(Il2CppReferenceArray<T>? source, int length) where T : Il2CppObjectBase
    {
        var widened = new Il2CppReferenceArray<T>(length);
        if (source != null)
        {
            for (var i = 0; i < Math.Min(source.Length, length); i++) widened[i] = source[i];
        }
        return widened;
    }

    private static Il2CppStructArray<T> Widen<T>(Il2CppStructArray<T>? source, int length) where T : unmanaged
    {
        var widened = new Il2CppStructArray<T>(length);
        if (source != null)
        {
            for (var i = 0; i < Math.Min(source.Length, length); i++) widened[i] = source[i];
        }
        return widened;
    }
}

[HarmonyPatch(typeof(UI_Module_Kikimeter), nameof(UI_Module_Kikimeter.OnInitialize))]
internal static class KikimeterInitializePatch
{
    private static float _loggedScale = -1f;

    [HarmonyPostfix]
    private static void Postfix(UI_Module_Kikimeter __instance)
    {
        try
        {
            var scale = Plugin.Instance.DpsMeterScale.Value;
            var root = __instance.transform;
            if (root.localScale != Vector3.one) root.localScale = Vector3.one;

            var rect = (__instance._rowsContainer != null ? __instance._rowsContainer : root).Cast<RectTransform>();
            ScaleAroundTopLeft(rect, scale);

            if (Mathf.Approximately(_loggedScale, scale)) return;
            _loggedScale = scale;
            Plugin.Instance.Log.LogInfo(
                $"dps meter '{__instance.name}' rows container '{rect.name}' scaled to {scale:0.##}: {rect.rect.width:0}x{rect.rect.height:0}, pivot ({rect.pivot.x:0.##},{rect.pivot.y:0.##}), " +
                $"anchors ({rect.anchorMin.x:0.##},{rect.anchorMin.y:0.##})-({rect.anchorMax.x:0.##},{rect.anchorMax.y:0.##}), {__instance._rows?.Length ?? 0} rows");
        }
        catch (Exception e)
        {
            Plugin.Instance.Log.LogWarning($"couldn't scale the dps meter: {e.Message}");
        }
    }

    private static void ScaleAroundTopLeft(RectTransform rect, float scale)
    {
        var target = new Vector3(scale, scale, 1f);
        if (rect.localScale == target) return;

        var corners = new Il2CppStructArray<Vector3>(4);
        rect.GetWorldCorners(corners);
        var topLeft = corners[1];

        rect.localScale = target;
        rect.GetWorldCorners(corners);
        rect.position += topLeft - corners[1];
    }
}
