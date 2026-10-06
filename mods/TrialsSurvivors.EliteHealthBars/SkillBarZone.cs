using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace TrialsSurvivors.EliteHealthBars;

internal sealed class SkillBarZone
{
    private const float SearchInterval = 2f;

    private readonly Il2CppStructArray<Vector3> _corners = new(4);
    private UI_Module_SpellBar? _bar;
    private Canvas? _canvas;
    private float _nextSearch;
    private Rect? _loggedRect;

    public Rect? Current { get; private set; }

    public void Refresh()
    {
        Current = null;
        if (!Resolve()) return;

        try
        {
            Current = Measure();
        }
        catch (System.Exception e)
        {
            Plugin.Instance.Log.LogWarning($"couldn't measure the skill bar: {e.Message}");
            _bar = null;
            return;
        }

        if (Current is { } rect && !SameRect(rect, _loggedRect))
        {
            _loggedRect = rect;
            Plugin.Instance.Log.LogInfo($"arrows avoid the skill bar at x {rect.xMin:0}-{rect.xMax:0}, y {rect.yMin:0}-{rect.yMax:0}");
        }
    }

    private bool Resolve()
    {
        if (_bar != null && !_bar.WasCollected && _canvas != null) return _bar.isActiveAndEnabled;

        _bar = null;
        _canvas = null;
        if (Time.unscaledTime < _nextSearch) return false;
        _nextSearch = Time.unscaledTime + SearchInterval;

        _bar = Object.FindObjectOfType<UI_Module_SpellBar>();
        if (_bar == null) return false;

        var parent = _bar.GetComponentInParent<Canvas>();
        _canvas = parent != null ? parent.rootCanvas : null;
        if (_canvas == null)
        {
            Plugin.Instance.Log.LogWarning("found the skill bar but not its canvas; arrows won't avoid it");
            _bar = null;
            return false;
        }

        Plugin.Instance.Log.LogInfo($"found skill bar '{_bar.name}' on canvas '{_canvas.name}' ({_canvas.renderMode})");
        return _bar.isActiveAndEnabled;
    }

    private Rect? Measure()
    {
        var camera = _canvas!.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);
        var found = false;

        foreach (var slot in _bar!._spellSlots)
        {
            if (slot == null || !slot.gameObject.activeInHierarchy) continue;
            if (slot.transform.TryCast<RectTransform>() is not { } rect) continue;

            rect.GetWorldCorners(_corners);
            foreach (var corner in _corners)
            {
                var screen = RectTransformUtility.WorldToScreenPoint(camera, corner);
                min = Vector2.Min(min, screen);
                max = Vector2.Max(max, screen);
            }
            found = true;
        }

        return found ? Rect.MinMaxRect(min.x, min.y, max.x, max.y) : null;
    }

    private static bool SameRect(Rect a, Rect? b) =>
        b is { } other && Mathf.Abs(a.xMin - other.xMin) < 2f && Mathf.Abs(a.xMax - other.xMax) < 2f &&
        Mathf.Abs(a.yMin - other.yMin) < 2f && Mathf.Abs(a.yMax - other.yMax) < 2f;
}
