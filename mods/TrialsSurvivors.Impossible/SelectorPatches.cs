using System;
using HarmonyLib;
using UnityEngine;

namespace TrialsSurvivors.Impossible;

internal static class SelectorDisplay
{
    private static readonly Color DefaultNameColour = new(1f, 0.17f, 0.17f);

    public static Color NameColour => Plugin.ParseColour(Plugin.Instance.NameColour, DefaultNameColour);

    public static string Description
    {
        get
        {
            var plugin = Plugin.Instance;
            return $"Unfair, but monsters have {plugin.MonsterHealthMultiplier.Value:0.#}x health and deal " +
                   $"{plugin.MonsterDamageMultiplier.Value:0.#}x damage. Elites appear {plugin.EliteRateMultiplier.Value:0.#}x as often, " +
                   $"and some return as red elites with {plugin.RedEliteHealthMultiplier.Value:0.#}x their health and damage.";
        }
    }

    public static bool AtEndOfChain(DifficultySelector selector, DifficultyManager manager)
    {
        if (!ImpossibleState.IsLastClassic(manager, manager.GetCurrentDifficulty())) return false;
        if (!manager.IsDifficultyUnlocked(manager.GetLastClassicDifficulty())) return false;
        return selector._currentUnfairPlusLevel >= manager.GetMaxUnlockedUnfairPlusLevel();
    }

    public static void Show(DifficultySelector selector)
    {
        var plugin = Plugin.Instance;

        try
        {
            var popup = selector._currentDifficultyPopup;
            if (popup != null)
            {
                popup.SetDifficultyNameRaw(plugin.DisplayName.Value);
                popup.SetDifficultyNameColor(NameColour);
                popup.SetDifficultyDescriptionRaw(Description);
                popup.SetLockedStateSimple(false);
            }
        }
        catch (Exception e)
        {
            plugin.Log.LogWarning($"couldn't update the difficulty popup: {e.Message}");
        }

        try
        {
            selector.UpdateTierText(true, plugin.TierText.Value);
        }
        catch (Exception e)
        {
            plugin.Log.LogWarning($"couldn't update the tier text: {e.Message}");
        }
    }

    public static bool ShowingImpossible(DifficultySelector selector)
    {
        if (!ImpossibleState.Selected) return false;
        var manager = selector._difficultyManager;
        return manager != null && selector._currentUnfairPlusLevel == 0 &&
               ImpossibleState.IsLastClassic(manager, manager.GetCurrentDifficulty());
    }
}

[HarmonyPatch(typeof(DifficultySelector), nameof(DifficultySelector.OnNextDifficulty))]
internal static class NextDifficultyPatch
{
    [HarmonyPrefix]
    private static bool Prefix(DifficultySelector __instance)
    {
        try
        {
            if (!Plugin.Instance.Enabled.Value || ImpossibleState.Selected) return true;
            var manager = __instance._difficultyManager;
            if (manager == null) return true;
            ImpossibleState.Remember(manager);
            if (!SelectorDisplay.AtEndOfChain(__instance, manager)) return true;

            var fromLevel = __instance._currentUnfairPlusLevel;
            ImpossibleState.Select($"next from unfair+{fromLevel}");
            ImpossibleState.WithoutClearing(() =>
            {
                __instance._currentUnfairPlusLevel = 0;
                manager.SetCurrentDifficulty(manager.GetLastClassicDifficulty());
            });
            __instance.RefreshDifficultyDisplay();
            return false;
        }
        catch (Exception e)
        {
            Plugin.Instance.Log.LogWarning($"couldn't step into impossible: {e.Message}");
            return true;
        }
    }
}

[HarmonyPatch(typeof(DifficultySelector), nameof(DifficultySelector.OnPrevDifficulty))]
internal static class PrevDifficultyPatch
{
    [HarmonyPrefix]
    private static bool Prefix(DifficultySelector __instance)
    {
        try
        {
            if (!ImpossibleState.Selected) return true;
            var manager = __instance._difficultyManager;
            if (manager == null) return true;

            var maxLevel = manager.GetMaxUnlockedUnfairPlusLevel();
            ImpossibleState.Deselect($"prev to unfair+{maxLevel}");
            if (maxLevel > 0)
            {
                __instance._currentUnfairPlusLevel = maxLevel;
                manager.SetCurrentUnfairPlusLevel(maxLevel);
            }
            __instance.RefreshDifficultyDisplay();
            return false;
        }
        catch (Exception e)
        {
            Plugin.Instance.Log.LogWarning($"couldn't step out of impossible: {e.Message}");
            return true;
        }
    }
}

[HarmonyPatch(typeof(DifficultySelector), nameof(DifficultySelector.RefreshDifficultyDisplay))]
internal static class RefreshDisplayPatch
{
    [HarmonyPostfix]
    private static void Postfix(DifficultySelector __instance)
    {
        try
        {
            ImpossibleState.Remember(__instance._difficultyManager);
            if (SelectorDisplay.ShowingImpossible(__instance)) SelectorDisplay.Show(__instance);
        }
        catch (Exception e)
        {
            Plugin.Instance.Log.LogWarning($"couldn't refresh the impossible display: {e.Message}");
        }
    }
}

[HarmonyPatch(typeof(DifficultySelector), nameof(DifficultySelector.UpdateTierText), new Type[0])]
internal static class TierTextPatch
{
    [HarmonyPostfix]
    private static void Postfix(DifficultySelector __instance)
    {
        try
        {
            if (SelectorDisplay.ShowingImpossible(__instance))
                __instance.UpdateTierText(true, Plugin.Instance.TierText.Value);
        }
        catch (Exception e)
        {
            Plugin.Instance.Log.LogWarning($"couldn't set the impossible tier text: {e.Message}");
        }
    }
}

[HarmonyPatch(typeof(UI_DifficultyLabel), nameof(UI_DifficultyLabel.UpdateText))]
internal static class DifficultyLabelPatch
{
    [HarmonyPostfix]
    private static void Postfix(UI_DifficultyLabel __instance, SO_DifficultyData difficulty)
    {
        try
        {
            if (!ImpossibleState.IsActive) return;
            var manager = ImpossibleState.Manager;
            if (manager == null || manager.IsInUnfairPlusMode() || !ImpossibleState.IsLastClassic(manager, difficulty)) return;

            var text = __instance._text;
            if (text == null) return;
            text.SetRawText(Plugin.Instance.DisplayName.Value);
            text.Color = SelectorDisplay.NameColour;
        }
        catch (Exception e)
        {
            Plugin.Instance.Log.LogWarning($"couldn't relabel the difficulty: {e.Message}");
        }
    }
}

[HarmonyPatch(typeof(DifficultyManager), nameof(DifficultyManager.SetCurrentDifficulty))]
internal static class SetDifficultyPatch
{
    [HarmonyPostfix]
    private static void Postfix(DifficultyManager __instance, SO_DifficultyData data)
    {
        try
        {
            ImpossibleState.Remember(__instance);
            if (ImpossibleState.ClearingSuppressed || !Plugin.Instance.Selected.Value) return;
            if (!ImpossibleState.IsLastClassic(__instance, data))
                ImpossibleState.Deselect($"difficulty changed to tier {(data != null ? data.Tier : -1)}");
        }
        catch (Exception e)
        {
            Plugin.Instance.Log.LogWarning($"couldn't check the difficulty change: {e.Message}");
        }
    }
}

[HarmonyPatch(typeof(DifficultyManager), nameof(DifficultyManager.SetCurrentUnfairPlusLevel))]
internal static class SetUnfairPlusPatch
{
    [HarmonyPostfix]
    private static void Postfix(DifficultyManager __instance, int level)
    {
        try
        {
            ImpossibleState.Remember(__instance);
            if (ImpossibleState.ClearingSuppressed || !Plugin.Instance.Selected.Value) return;
            if (level > 0) ImpossibleState.Deselect($"unfair+ level changed to {level}");
        }
        catch (Exception e)
        {
            Plugin.Instance.Log.LogWarning($"couldn't check the unfair+ change: {e.Message}");
        }
    }
}
