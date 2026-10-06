using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TrialsSurvivors.WeaponSlots;

public sealed class SpellBarAligner : MonoBehaviour
{
    private const float Tolerance = 0.5f;
    private const float RetryInterval = 0.25f;
    private const float GiveUpAfter = 10f;

    private static UI_Module_SpellBar? _pending;
    private static int _readyFrame;
    private static float _nextAttempt;
    private static float _deadline;
    private static readonly HashSet<IntPtr> FallbackShifted = new();
    private static readonly Il2CppStructArray<Vector3> Corners = new(4);

    public SpellBarAligner(IntPtr ptr) : base(ptr)
    {
    }

    public static void Schedule(UI_Module_SpellBar bar)
    {
        _pending = bar;
        _readyFrame = Time.frameCount + 1;
        _nextAttempt = 0f;
        _deadline = Time.unscaledTime + GiveUpAfter;
    }

    private void LateUpdate()
    {
        var bar = _pending;
        if (bar == null) return;
        if (bar.WasCollected)
        {
            _pending = null;
            return;
        }
        if (Time.frameCount < _readyFrame || Time.unscaledTime < _nextAttempt || !bar.isActiveAndEnabled) return;
        _nextAttempt = Time.unscaledTime + RetryInterval;

        try
        {
            var giveUp = Time.unscaledTime >= _deadline;
            if (Align(bar, giveUp) || giveUp) _pending = null;
        }
        catch (Exception e)
        {
            _pending = null;
            Plugin.Instance.Log.LogWarning($"couldn't align the spell bar: {e.Message}");
        }
    }

    private static bool Align(UI_Module_SpellBar bar, bool lastAttempt)
    {
        var barRect = bar.transform.Cast<RectTransform>();
        LayoutRebuilder.ForceRebuildLayoutImmediate(barRect);

        if (FindLifeBar(barRect, lastAttempt) is not { } lifeBar)
        {
            if (lastAttempt) ShiftByHalfSlot(bar, barRect);
            return false;
        }

        var lifeRect = lifeBar.transform.Cast<RectTransform>();
        var (lifeLeft, lifeRight) = HorizontalExtent(lifeRect);
        var centre = (lifeLeft + lifeRight) * 0.5f;
        var (slotsLeft, slotsRight) = SlotExtent(bar);

        var shared = SharedLayoutParent(barRect, lifeRect);
        if (shared != null) return AlignWithinLayout(bar, barRect, lifeBar, lifeRect, shared, centre, lifeLeft, lifeRight);

        var mover = LayoutFreeAncestor(barRect, lifeRect);
        var shift = centre - (slotsLeft + slotsRight) * 0.5f;
        if (Mathf.Abs(shift) > Tolerance)
        {
            mover.position += new Vector3(shift, 0f, 0f);
            (slotsLeft, slotsRight) = SlotExtent(bar);
        }

        FitHorizontally(lifeRect, slotsLeft, slotsRight);
        var (newLeft, newRight) = HorizontalExtent(lifeRect);

        Plugin.Instance.Log.LogInfo(
            $"aligned spell bar: moved '{mover.name}' by {shift:0.#} so slots span {slotsLeft:0}-{slotsRight:0}; " +
            $"life bar '{lifeBar.name}' {lifeLeft:0}-{lifeRight:0} ({lifeRight - lifeLeft:0}) -> {newLeft:0}-{newRight:0} ({newRight - newLeft:0})");
        return true;
    }

    private static bool AlignWithinLayout(UI_Module_SpellBar bar, RectTransform barRect, UI_Module_LifeBar lifeBar, RectTransform lifeRect,
        LayoutGroup layout, float centre, float lifeLeft, float lifeRight)
    {
        var container = layout.transform.Cast<RectTransform>();
        var alignmentBefore = layout.childAlignment;
        layout.childAlignment = Centred(alignmentBefore);

        var (slotsLeft, slotsRight) = SlotExtent(bar);
        var scale = Mathf.Abs(lifeRect.lossyScale.x);
        if (scale > 0f)
        {
            var width = (slotsRight - slotsLeft) / scale;
            var element = lifeRect.GetComponent<LayoutElement>() ?? lifeRect.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = width;
            element.minWidth = width;
            lifeRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        }

        Rebuild(container);
        (slotsLeft, slotsRight) = SlotExtent(bar);

        var mover = LayoutFreeAncestor(container, null);
        var shift = centre - (slotsLeft + slotsRight) * 0.5f;
        if (Mathf.Abs(shift) > Tolerance)
        {
            mover.position += new Vector3(shift, 0f, 0f);
            Rebuild(container);
            (slotsLeft, slotsRight) = SlotExtent(bar);
        }

        var (newLeft, newRight) = HorizontalExtent(lifeRect);
        var vertical = layout.TryCast<HorizontalOrVerticalLayoutGroup>();
        Plugin.Instance.Log.LogInfo(
            $"aligned spell bar inside {layout.GetIl2CppType().Name} on '{container.name}' " +
            $"(alignment {alignmentBefore} -> {layout.childAlignment}, controlWidth={vertical?.childControlWidth}, expandWidth={vertical?.childForceExpandWidth}, " +
            $"pivot {container.pivot.x:0.##}): moved '{mover.name}' by {shift:0.#}; slots {slotsLeft:0}-{slotsRight:0} (centre {(slotsLeft + slotsRight) * 0.5f:0}, target {centre:0}); " +
            $"life bar '{lifeBar.name}' {lifeLeft:0}-{lifeRight:0} -> {newLeft:0}-{newRight:0}");
        return true;
    }

    private static LayoutGroup? SharedLayoutParent(Transform bar, Transform lifeBar)
    {
        for (var current = bar.parent; current != null; current = current.parent)
        {
            if (!lifeBar.IsChildOf(current)) continue;
            var layout = current.GetComponent<LayoutGroup>();
            return layout != null && layout.enabled ? layout : null;
        }
        return null;
    }

    private static TextAnchor Centred(TextAnchor anchor) => anchor switch
    {
        TextAnchor.UpperLeft or TextAnchor.UpperRight => TextAnchor.UpperCenter,
        TextAnchor.MiddleLeft or TextAnchor.MiddleRight => TextAnchor.MiddleCenter,
        TextAnchor.LowerLeft or TextAnchor.LowerRight => TextAnchor.LowerCenter,
        _ => anchor
    };

    private static void Rebuild(RectTransform container)
    {
        LayoutRebuilder.ForceRebuildLayoutImmediate(container);
        if (container.parent != null && container.parent.TryCast<RectTransform>() is { } parent) LayoutRebuilder.ForceRebuildLayoutImmediate(parent);
    }

    private static void ShiftByHalfSlot(UI_Module_SpellBar bar, RectTransform barRect)
    {
        if (!FallbackShifted.Add(bar.Pointer)) return;

        var slots = bar.GetComponentsInChildren<UI_Module_SpellSlot>();
        if (slots.Length < 2) return;

        var step = slots[slots.Length - 1].transform.position.x - slots[slots.Length - 2].transform.position.x;
        var mover = LayoutFreeAncestor(barRect, null);
        mover.position -= new Vector3(step * 0.5f, 0f, 0f);
        Plugin.Instance.Log.LogWarning($"no life bar found after {GiveUpAfter:0}s; moved '{mover.name}' left by half a slot ({step * 0.5f:0.#})");
    }

    private static UI_Module_LifeBar? FindLifeBar(RectTransform barRect, bool logCandidates)
    {
        var canvas = barRect.GetComponentInParent<Canvas>();
        var root = canvas != null ? canvas.rootCanvas : null;
        UI_Module_LifeBar? best = null;
        var bestScore = float.MaxValue;
        var candidates = new List<string>();

        foreach (var lifeBar in Object.FindObjectsOfType<UI_Module_LifeBar>(true))
        {
            var lifeCanvas = lifeBar.GetComponentInParent<Canvas>(true);
            var lifeRoot = lifeCanvas != null ? lifeCanvas.rootCanvas : null;
            var distance = Vector3.Distance(lifeBar.transform.position, barRect.position);
            candidates.Add($"'{lifeBar.name}' active={lifeBar.isActiveAndEnabled} canvas='{(lifeRoot != null ? lifeRoot.name : "none")}' distance={distance:0}");

            var score = distance + (lifeBar.isActiveAndEnabled ? 0f : 1e6f) + (lifeRoot == root ? 0f : 1e7f);
            if (score < bestScore)
            {
                best = lifeBar;
                bestScore = score;
            }
        }

        if (logCandidates || Plugin.Instance.Verbose.Value)
        {
            Plugin.Instance.Log.LogInfo($"life bar candidates for '{barRect.name}' on '{(root != null ? root.name : "none")}': " +
                                        (candidates.Count > 0 ? string.Join("; ", candidates) : "none"));
        }

        return best;
    }

    private static Transform LayoutFreeAncestor(Transform transform, Transform? lifeBar)
    {
        var current = transform;
        while (current.parent != null && current.parent.GetComponent<LayoutGroup>() is { } layout && layout.enabled)
        {
            if (lifeBar != null && lifeBar.IsChildOf(current.parent))
            {
                Plugin.Instance.Log.LogWarning(
                    $"'{current.name}' is laid out by {layout.GetIl2CppType().Name} on '{current.parent.name}', which also holds the life bar; the shift may be undone");
                break;
            }
            current = current.parent;
        }
        return current;
    }

    private static (float Left, float Right) SlotExtent(UI_Module_SpellBar bar)
    {
        var left = float.MaxValue;
        var right = float.MinValue;
        foreach (var slot in bar.GetComponentsInChildren<UI_Module_SpellSlot>())
        {
            var (slotLeft, slotRight) = HorizontalExtent(slot.transform.Cast<RectTransform>());
            left = Mathf.Min(left, slotLeft);
            right = Mathf.Max(right, slotRight);
        }
        return (left, right);
    }

    private static (float Left, float Right) HorizontalExtent(RectTransform rect)
    {
        rect.GetWorldCorners(Corners);
        return (Mathf.Min(Corners[0].x, Corners[2].x), Mathf.Max(Corners[0].x, Corners[2].x));
    }

    private static void FitHorizontally(RectTransform rect, float left, float right)
    {
        var (currentLeft, currentRight) = HorizontalExtent(rect);
        var scale = Mathf.Abs(rect.lossyScale.x);
        if (scale <= 0f) return;

        if (Mathf.Abs((currentRight - currentLeft) - (right - left)) > Tolerance)
        {
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, rect.rect.width + ((right - left) - (currentRight - currentLeft)) / scale);
            (currentLeft, currentRight) = HorizontalExtent(rect);
        }

        var shift = (left + right) * 0.5f - (currentLeft + currentRight) * 0.5f;
        if (Mathf.Abs(shift) > Tolerance) rect.position += new Vector3(shift, 0f, 0f);
    }
}
