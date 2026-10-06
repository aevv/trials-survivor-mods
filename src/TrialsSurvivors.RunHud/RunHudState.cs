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
            if (levelText == null) return;

            var clone = UnityEngine.Object.Instantiate(levelText.gameObject, levelText.transform.parent);
            clone.name = "RunHudStats";

            var localizer = clone.GetComponent<TMPLocalizer>();
            if (localizer != null) UnityEngine.Object.Destroy(localizer);

            Mirror(levelText.rectTransform, clone.GetComponent<RectTransform>());

            var label = clone.GetComponent<TextMeshProUGUI>();
            label.alignment = MirrorAlignment(levelText.alignment);
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Overflow;
            label.richText = true;
            label.text = "";

            Label = label;
            Plugin.Instance.Log.LogInfo("attached run stats to the XP bar");
        }
        catch (Exception e)
        {
            Plugin.Instance.Log.LogError($"couldn't attach to the XP bar: {e}");
        }
    }

    private static void Mirror(RectTransform source, RectTransform target)
    {
        target.anchorMin = new Vector2(1f - source.anchorMax.x, source.anchorMin.y);
        target.anchorMax = new Vector2(1f - source.anchorMin.x, source.anchorMax.y);
        target.pivot = new Vector2(1f - source.pivot.x, source.pivot.y);
        target.anchoredPosition = new Vector2(-source.anchoredPosition.x, source.anchoredPosition.y);
        target.sizeDelta = new Vector2(Mathf.Max(source.sizeDelta.x * 4f, 400f), source.sizeDelta.y);
    }

    private static TextAlignmentOptions MirrorAlignment(TextAlignmentOptions alignment)
    {
        var name = alignment.ToString();
        var mirrored = name.Contains("Left") ? name.Replace("Left", "Right")
            : name.Contains("Right") ? name.Replace("Right", "Left")
            : "MidlineRight";
        return Enum.TryParse<TextAlignmentOptions>(mirrored, out var result) ? result : TextAlignmentOptions.MidlineRight;
    }
}
