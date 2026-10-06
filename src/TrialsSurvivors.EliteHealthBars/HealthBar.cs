using UnityEngine;
using UnityEngine.UI;

namespace TrialsSurvivors.EliteHealthBars;

internal readonly record struct BarStyle(Vector2 Size, float Border, Color Background, Color Delayed, Color Fill);

internal sealed class HealthBar
{
    private readonly GameObject _root;
    private readonly RectTransform _rootRect;
    private readonly RectTransform _trackRect;
    private readonly RectTransform _delayedRect;
    private readonly RectTransform _fillRect;
    private readonly Image _background;
    private readonly Image _delayed;
    private readonly Image _fill;
    private BarStyle? _style;

    public HealthBar(Transform parent)
    {
        (_root, _rootRect) = CreateRect("EliteHealthBar", parent);
        _rootRect.anchorMin = Vector2.zero;
        _rootRect.anchorMax = Vector2.zero;
        _rootRect.pivot = new Vector2(0.5f, 0f);
        _background = AddImage(_root);

        (_, _trackRect) = CreateRect("Track", _rootRect);

        GameObject delayed, fill;
        (delayed, _delayedRect) = CreateRect("Delayed", _trackRect);
        _delayed = AddImage(delayed);

        (fill, _fillRect) = CreateRect("Fill", _trackRect);
        _fill = AddImage(fill);
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
    }

    public void Show(Vector2 screenPosition, float fraction, float delayedFraction)
    {
        if (!_root.activeSelf) _root.SetActive(true);
        _rootRect.position = screenPosition;
        _fillRect.anchorMax = new Vector2(fraction, 1f);
        _delayedRect.anchorMax = new Vector2(delayedFraction, 1f);
    }

    public void Hide()
    {
        if (_root.activeSelf) _root.SetActive(false);
    }

    private static (GameObject, RectTransform) CreateRect(string name, Transform parent)
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

    private static Image AddImage(GameObject go)
    {
        var image = go.AddComponent<Image>();
        image.raycastTarget = false;
        return image;
    }
}
