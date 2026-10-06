using System;
using TMPro;
using UnityEngine;

namespace TrialsSurvivors.RunHud;

internal static class RunHudState
{
    public static RunStatsTracker? Tracker { get; set; }
    public static TextMeshProUGUI? Label { get; private set; }

    public static void AttachTo(UI_Module_XPBar xpBar)
    {
        if (Label != null) return;

        try
        {
            var levelText = xpBar._levelText;
            var bar = xpBar._xpBarImage;
            if (levelText == null || bar == null) return;

            var clone = UnityEngine.Object.Instantiate(levelText.gameObject, bar.rectTransform);
            clone.name = "RunHudStats";

            var localizer = clone.GetComponent<TMPLocalizer>();
            if (localizer != null) UnityEngine.Object.Destroy(localizer);

            var label = clone.GetComponent<TextMeshProUGUI>();
            label.fontSize = levelText.fontSize * Plugin.Instance.FontScale.Value;
            label.enableAutoSizing = false;
            label.alignment = TextAlignmentOptions.BottomRight;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Overflow;
            label.richText = true;
            label.text = "";

            PlaceAboveRightEnd(label.rectTransform);
            Label = label;
            Plugin.Instance.Log.LogInfo($"attached run stats to the XP bar '{bar.name}' (type {bar.type}, fill {bar.fillAmount:0.##}, width {bar.rectTransform.rect.width:0}, level font {levelText.fontSize:0})");
        }
        catch (Exception e)
        {
            Plugin.Instance.Log.LogError($"couldn't attach to the XP bar: {e}");
        }
    }

    public static void PlaceAboveRightEnd(RectTransform rect)
    {
        var plugin = Plugin.Instance;
        rect.anchorMin = Vector2.one;
        rect.anchorMax = Vector2.one;
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;
        rect.sizeDelta = new Vector2(1200f, 200f);
        rect.pivot = new Vector2(1f, 0f);
        rect.anchoredPosition = new Vector2(-plugin.OffsetX.Value, plugin.OffsetY.Value);
    }
}