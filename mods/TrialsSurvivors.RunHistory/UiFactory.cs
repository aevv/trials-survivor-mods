using System;
using Il2CppInterop.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace TrialsSurvivors.RunHistory;

internal static class UiFactory
{
    public static RectTransform Rect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        var go = new GameObject(name);
        var rect = go.AddComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
        return rect;
    }

    public static RectTransform Fill(string name, Transform parent, float inset = 0f) =>
        Rect(name, parent, Vector2.zero, Vector2.one, new Vector2(inset, inset), new Vector2(-inset, -inset));

    public static Image Image(RectTransform rect, Color colour, bool raycast = false)
    {
        var image = rect.gameObject.AddComponent<Image>();
        image.color = colour;
        image.raycastTarget = raycast;
        return image;
    }

    public static TextMeshProUGUI Text(RectTransform rect, TMP_FontAsset? font, float size, TextAlignmentOptions alignment)
    {
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null)
        {
            text.font = font;
            text.fontSharedMaterial = font.material;
        }
        text.fontSize = size;
        text.alignment = alignment;
        text.richText = true;
        text.raycastTarget = false;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        return text;
    }

    public static Button Button(RectTransform rect, Image target, Action onClick)
    {
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = target;
        button.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityAction>(onClick));
        return button;
    }

    public static TMP_FontAsset? FindGameFont()
    {
        foreach (var text in UnityEngine.Object.FindObjectsOfType<TextMeshProUGUI>())
        {
            if (text.font != null) return text.font;
        }

        var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
        return fonts.Length > 0 ? fonts[0] : TMP_Settings.defaultFontAsset;
    }
}
