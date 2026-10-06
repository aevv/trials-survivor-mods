using System;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TrialsSurvivors.RoomSkip;

internal sealed class SkipButton
{
    private const int FinalTier = 2;
    private const float RelayoutInterval = 0.5f;
    private static readonly Color AvailableColour = new(1f, 0.78f, 0.27f, 1f);
    private static readonly Color LockedColour = new(0.36f, 0.27f, 0.5f, 0.85f);
    private static readonly Color AvailableLabel = Color.white;
    private static readonly Color LockedLabel = new(1f, 1f, 1f, 0.45f);
    private static readonly Color ShadowColour = new(0f, 0f, 0f, 0.5f);

    private static SkipButton? _active;

    private readonly IntPtr _uiPointer;
    private readonly ChunkObjectiveUI _ui;
    private readonly ChunkObjective _objective;
    private readonly Transform _anchorGroup;
    private readonly GameObject _root;
    private readonly RectTransform _rect;
    private readonly Image _background;
    private readonly Button _button;
    private readonly TextMeshProUGUI _label;
    private readonly Il2CppStructArray<Vector3> _corners = new(4);
    private bool? _available;
    private float _nextRelayout;
    private Vector2 _loggedPosition = new(float.NaN, float.NaN);
    private bool _skipped;

    private SkipButton(ChunkObjectiveUI ui, ChunkObjective objective)
    {
        _uiPointer = ui.Pointer;
        _ui = ui;
        _objective = objective;
        _anchorGroup = ProgressGroup(ui);

        var plugin = Plugin.Instance;

        _root = new GameObject("TSModsSkipButton");
        _rect = _root.AddComponent<RectTransform>();
        _rect.SetParent(ui.transform, false);
        _rect.anchorMin = ui.transform.Cast<RectTransform>().pivot;
        _rect.anchorMax = _rect.anchorMin;
        _rect.pivot = new Vector2(0.5f, 1f);
        _rect.sizeDelta = new Vector2(Mathf.Max(plugin.Width.Value, 40f), plugin.Height.Value);
        _root.AddComponent<LayoutElement>().ignoreLayout = true;

        _root.AddComponent<Canvas>();
        _root.AddComponent<GraphicRaycaster>();

        _background = _root.AddComponent<Image>();
        _background.sprite = ButtonSprite.Get();
        _background.type = Image.Type.Sliced;

        var shadow = _root.AddComponent<Shadow>();
        shadow.effectColor = ShadowColour;
        shadow.effectDistance = new Vector2(0f, -4f);

        _button = _root.AddComponent<Button>();
        _button.targetGraphic = _background;
        var colours = _button.colors;
        colours.normalColor = new Color(0.92f, 0.92f, 0.92f, 1f);
        colours.highlightedColor = Color.white;
        colours.selectedColor = colours.normalColor;
        colours.pressedColor = new Color(0.72f, 0.72f, 0.72f, 1f);
        colours.disabledColor = Color.white;
        colours.fadeDuration = 0.08f;
        _button.colors = colours;
        _button.onClick.AddListener((UnityAction)new Action(() => Skip("button")));

        var labelObject = new GameObject("Label");
        var labelRect = labelObject.AddComponent<RectTransform>();
        labelRect.SetParent(_rect, false);
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = new Vector2(0f, 2f);
        _label = labelObject.AddComponent<TextMeshProUGUI>();
        var title = TitleText(ui);
        if (title != null && title.font != null)
        {
            _label.font = title.font;
            _label.fontSharedMaterial = title.fontSharedMaterial != null ? title.fontSharedMaterial : title.font.material;
        }
        _label.text = "SKIP";
        _label.fontSize = plugin.Height.Value * 0.6f;
        _label.alignment = TextAlignmentOptions.Center;
        _label.enableWordWrapping = false;
        _label.overflowMode = TextOverflowModes.Overflow;
        _label.raycastTarget = false;

        Plugin.Instance.Log.LogInfo(
            $"skip button added to '{ui.name}' for room {objective.RoomIndex + 1}/{objective.MaxRoomIndex + 1}: anchored under '{_anchorGroup.name}', " +
            $"font '{(title != null && title.font != null ? title.font.name : "default")}', event system '{(EventSystem.current != null ? EventSystem.current.name : "none")}'");

        Relayout();
        Refresh(force: true);
    }

    public static void Attach(ChunkObjectiveUI ui, ChunkObjective objective)
    {
        _active?.Destroy();
        _active = new SkipButton(ui, objective);
    }

    public static void Detach(ChunkObjectiveUI ui)
    {
        if (_active == null || _active._uiPointer != ui.Pointer) return;
        _active.Destroy();
        _active = null;
    }

    public static void Reset()
    {
        _active?.Destroy();
        _active = null;
    }

    public static void Tick()
    {
        var active = _active;
        if (active == null) return;

        if (active._root == null || active._ui.WasCollected || active._ui == null || active._objective.WasCollected || active._objective == null)
        {
            Reset();
            return;
        }

        if (Time.unscaledTime >= active._nextRelayout) active.Relayout();
        active.Refresh(force: false);

        var key = Plugin.Instance.SkipKey.Value;
        if (key != Key.None && Keyboard.current is { } keyboard && keyboard[key].wasPressedThisFrame) active.Skip(key.ToString());
    }

    private static Transform ProgressGroup(ChunkObjectiveUI ui)
    {
        var filler = ui._rewardFiller;
        var icon = ui._rewardIcon;
        if (filler == null && icon == null) return ui.transform;
        if (filler == null) return icon!.transform.parent ?? ui.transform;
        if (icon == null) return filler.transform.parent ?? ui.transform;

        for (var current = filler.transform; current != null; current = current.parent)
        {
            if (icon.transform.IsChildOf(current)) return current;
            if (current == ui.transform) break;
        }
        return ui.transform;
    }

    private static TextMeshProUGUI? TitleText(ChunkObjectiveUI ui)
    {
        var localizer = ui._titleText;
        var title = localizer != null ? localizer.GetComponent<TextMeshProUGUI>() : null;
        return title != null ? title : ui.GetComponentInChildren<TextMeshProUGUI>();
    }

    private bool IsAvailable() =>
        !_skipped && _objective.HasStarted && !_objective.IsCompleted && !_objective.IsStopped && _objective.CurrentTier >= FinalTier;

    private void Refresh(bool force)
    {
        var available = IsAvailable();
        if (!force && available == _available) return;

        var wasAvailable = _available;
        _available = available;
        _button.interactable = available;
        _background.color = available ? AvailableColour : LockedColour;
        _label.color = available ? AvailableLabel : LockedLabel;

        var visible = available || (Plugin.Instance.ShowBeforeAvailable.Value && !_skipped && !_objective.IsCompleted);
        if (_root.activeSelf != visible) _root.SetActive(visible);

        if (available && wasAvailable == false)
        {
            Plugin.Instance.Log.LogInfo(
                $"skip available: tier {_objective.CurrentTier + 1} reached in room {_objective.RoomIndex + 1} with {_objective.GetRemainingTimeInSeconds()}s left");
        }
    }

    private void Skip(string source)
    {
        if (!IsAvailable())
        {
            Plugin.Instance.Log.LogInfo($"skip ({source}) ignored: tier {_objective.CurrentTier + 1}, completed={_objective.IsCompleted}, stopped={_objective.IsStopped}");
            return;
        }

        if (Time.timeScale <= 0f)
        {
            Plugin.Instance.Log.LogInfo($"skip ({source}) ignored while the game is paused");
            return;
        }

        Plugin.Instance.Log.LogInfo(
            $"skipping room {_objective.RoomIndex + 1}/{_objective.MaxRoomIndex + 1} via {source}: tier {_objective.CurrentTier + 1}, " +
            $"score {_objective.GetNormalizedScore():0.00}, {_objective.GetRemainingTimeInSeconds()}s left");

        _skipped = true;
        _objective.CompleteObjective();
        Plugin.Instance.Log.LogInfo($"room completed: {_objective.IsCompleted}");
        Refresh(force: true);
    }

    private void Relayout()
    {
        _nextRelayout = Time.unscaledTime + RelayoutInterval;

        var uiTransform = _ui.transform;
        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);
        var found = false;

        foreach (var graphic in _anchorGroup.GetComponentsInChildren<Graphic>(false))
        {
            if (!graphic.enabled || graphic.color.a < 0.01f || graphic.transform.IsChildOf(_rect)) continue;

            graphic.rectTransform.GetWorldCorners(_corners);
            foreach (var corner in _corners)
            {
                var local = (Vector2)uiTransform.InverseTransformPoint(corner);
                min = Vector2.Min(min, local);
                max = Vector2.Max(max, local);
            }
            found = true;
        }

        if (!found) return;

        var plugin = Plugin.Instance;
        var width = plugin.Width.Value > 0f ? plugin.Width.Value : max.x - min.x;
        var size = new Vector2(width, plugin.Height.Value);
        if ((_rect.sizeDelta - size).sqrMagnitude > 0.25f) _rect.sizeDelta = size;

        var position = new Vector2((min.x + max.x) * 0.5f, min.y - plugin.Gap.Value);
        if ((_rect.anchoredPosition - position).sqrMagnitude > 0.25f) _rect.anchoredPosition = position;

        if (!float.IsNaN(_loggedPosition.x) && (_loggedPosition - position).sqrMagnitude < 4f) return;
        _loggedPosition = position;
        LogPlacement(min, max);
    }

    private void LogPlacement(Vector2 min, Vector2 max)
    {
        var canvas = _ui.GetComponentInParent<Canvas>();
        var root = canvas != null ? canvas.rootCanvas : null;
        var camera = root != null && root.renderMode != RenderMode.ScreenSpaceOverlay ? root.worldCamera : null;

        _rect.GetWorldCorners(_corners);
        var bottomLeft = RectTransformUtility.WorldToScreenPoint(camera, _corners[0]);
        var topRight = RectTransformUtility.WorldToScreenPoint(camera, _corners[2]);

        Plugin.Instance.Log.LogInfo(
            $"skip button under '{_anchorGroup.name}' (local bounds {min.x:0},{min.y:0} to {max.x:0},{max.y:0}): " +
            $"screen {bottomLeft.x:0},{bottomLeft.y:0} to {topRight.x:0},{topRight.y:0} of {Screen.width}x{Screen.height}, active={_root.activeInHierarchy}");
    }

    private void Destroy()
    {
        if (_root != null) Object.Destroy(_root);
    }
}
