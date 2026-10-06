using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TrialsSurvivors.EliteHealthBars;

internal readonly record struct BarStyle(
    Vector2 Size,
    float Border,
    Color Background,
    Color Delayed,
    Color Fill,
    float TextSize,
    float IconSize,
    bool ShowHpText);

internal sealed class HealthBar
{
    private static readonly Color BuffTint = Color.white;
    private static readonly Color DebuffBorder = new(0.85f, 0.25f, 0.25f, 1f);
    private static readonly Color BuffBorder = new(0f, 0f, 0f, 0.8f);

    private readonly GameObject _root;
    private readonly RectTransform _rootRect;
    private readonly RectTransform _trackRect;
    private readonly RectTransform _delayedRect;
    private readonly RectTransform _fillRect;
    private readonly Image _background;
    private readonly Image _delayed;
    private readonly Image _fill;
    private readonly TextMeshProUGUI _hpText;
    private readonly RectTransform _iconRow;
    private readonly List<(GameObject Root, RectTransform Rect, Image Border, Image Icon, TextMeshProUGUI Stacks)> _icons = new();
    private readonly TMP_FontAsset? _font;
    private BarStyle? _style;
    private string? _lastHp;

    public HealthBar(Transform parent, TMP_FontAsset? font)
    {
        _font = font;

        (_root, _rootRect) = Ui.Rect("EliteHealthBar", parent);
        _rootRect.anchorMin = Vector2.zero;
        _rootRect.anchorMax = Vector2.zero;
        _rootRect.pivot = new Vector2(0.5f, 0f);
        _background = Ui.Image(_root);

        (_, _trackRect) = Ui.Rect("Track", _rootRect);

        GameObject delayed, fill;
        (delayed, _delayedRect) = Ui.Rect("Delayed", _trackRect);
        _delayed = Ui.Image(delayed);

        (fill, _fillRect) = Ui.Rect("Fill", _trackRect);
        _fill = Ui.Image(fill);

        GameObject hp;
        (hp, var hpRect) = Ui.Rect("Hp", _rootRect);
        hpRect.anchorMin = new Vector2(0f, 1f);
        hpRect.anchorMax = new Vector2(1f, 1f);
        hpRect.pivot = new Vector2(0.5f, 0f);
        _hpText = Ui.Text(hp, font, TextAlignmentOptions.BottomRight);

        (_, _iconRow) = Ui.Rect("Buffs", _rootRect);
        _iconRow.anchorMin = new Vector2(0f, 1f);
        _iconRow.anchorMax = new Vector2(0f, 1f);
        _iconRow.pivot = new Vector2(0f, 0f);
    }

    public void Apply(in BarStyle style)
    {
        if (_style == style) return;
        _style = style;

        _rootRect.sizeDelta = style.Size;
        _trackRect.offsetMin = new Vector2(style.Border, style.Border);
        _trackRect.offsetMax = new Vector2(-style.Border, -style.Border);
        _background.color = style.Background;
        _delayed.color = style.Delayed;
        _fill.color = style.Fill;

        var gap = style.Border + 1f;
        var hpRect = _hpText.rectTransform;
        hpRect.offsetMin = new Vector2(0f, gap);
        hpRect.offsetMax = new Vector2(0f, gap + style.TextSize * 1.2f);
        _hpText.fontSize = style.TextSize;
        _hpText.gameObject.SetActive(style.ShowHpText);

        _iconRow.anchoredPosition = new Vector2(0f, gap);
        for (var i = 0; i < _icons.Count; i++) LayoutIcon(_icons[i], i, style);
    }

    public void Show(Vector2 screenPosition, float fraction, float delayedFraction, string hp, List<BuffIcon> buffs)
    {
        if (!_root.activeSelf) _root.SetActive(true);
        _rootRect.position = screenPosition;
        _fillRect.anchorMax = new Vector2(fraction, 1f);
        _delayedRect.anchorMax = new Vector2(delayedFraction, 1f);

        if (_style is { ShowHpText: true } && hp != _lastHp)
        {
            _lastHp = hp;
            _hpText.text = hp;
        }

        ShowBuffs(buffs);
    }

    public void Hide()
    {
        if (_root.activeSelf) _root.SetActive(false);
    }

    private void ShowBuffs(List<BuffIcon> buffs)
    {
        while (_icons.Count < buffs.Count) AddIcon();

        for (var i = 0; i < _icons.Count; i++)
        {
            var slot = _icons[i];
            if (i >= buffs.Count)
            {
                if (slot.Root.activeSelf) slot.Root.SetActive(false);
                continue;
            }

            var buff = buffs[i];
            if (!slot.Root.activeSelf) slot.Root.SetActive(true);
            if (slot.Icon.sprite != buff.Icon) slot.Icon.sprite = buff.Icon;
            slot.Border.color = buff.IsDebuff ? DebuffBorder : BuffBorder;

            var stacks = buff.Stacks > 1 ? buff.Stacks.ToString() : "";
            if (slot.Stacks.text != stacks) slot.Stacks.text = stacks;
        }
    }

    private void AddIcon()
    {
        var (root, rect) = Ui.Rect($"Buff{_icons.Count}", _iconRow);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.zero;
        rect.pivot = Vector2.zero;
        var border = Ui.Image(root);

        var (iconObject, iconRect) = Ui.Rect("Icon", rect);
        iconRect.offsetMin = Vector2.one;
        iconRect.offsetMax = -Vector2.one;
        var icon = Ui.Image(iconObject);
        icon.color = BuffTint;
        icon.preserveAspect = true;

        var (stacksObject, _) = Ui.Rect("Stacks", rect);
        var stacks = Ui.Text(stacksObject, _font, TextAlignmentOptions.BottomRight);
        stacks.outlineWidth = 0.25f;

        var slot = (root, rect, border, icon, stacks);
        _icons.Add(slot);
        if (_style is { } style) LayoutIcon(slot, _icons.Count - 1, style);
    }

    private static void LayoutIcon((GameObject Root, RectTransform Rect, Image Border, Image Icon, TextMeshProUGUI Stacks) slot, int index, in BarStyle style)
    {
        slot.Rect.sizeDelta = new Vector2(style.IconSize, style.IconSize);
        slot.Rect.anchoredPosition = new Vector2(index * (style.IconSize + 2f), 0f);
        slot.Stacks.fontSize = style.IconSize * 0.6f;
    }
}

internal static class Ui
{
    public static (GameObject, RectTransform) Rect(string name, Transform parent)
    {
        var go = new GameObject(name);
        var rect = go.AddComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return (go, rect);
    }

    public static Image Image(GameObject go)
    {
        var image = go.AddComponent<Image>();
        image.raycastTarget = false;
        return image;
    }

    public static TextMeshProUGUI Text(GameObject go, TMP_FontAsset? font, TextAlignmentOptions alignment)
    {
        var text = go.AddComponent<TextMeshProUGUI>();
        if (font != null) text.font = font;
        text.alignment = alignment;
        text.raycastTarget = false;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Overflow;
        return text;
    }

    public static TMP_FontAsset? FindGameFont()
    {
        foreach (var text in Object.FindObjectsOfType<TextMeshProUGUI>())
        {
            if (text.font != null) return text.font;
        }

        var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
        return fonts.Length > 0 ? fonts[0] : TMP_Settings.defaultFontAsset;
    }
}
