using System;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TrialsSurvivors.WeaponSlots;

public sealed class SpellBarAligner : MonoBehaviour
{
    private const float Tolerance = 0.5f;

    private static UI_Module_SpellBar? _pending;
    private static int _readyFrame;
    private static readonly Il2CppStructArray<Vector3> Corners = new(4);

    public SpellBarAligner(IntPtr ptr) : base(ptr)
    {
    }

    public static void Schedule(UI_Module_SpellBar bar)
    {
        _pending = bar;
        _readyFrame = Time.frameCount + 1;
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
        if (Time.frameCount < _readyFrame || !bar.isActiveAndEnabled) return;

        _pending = null;
        try
        {
            Align(bar);
        }
        catch (Exception e)
        {
            Plugin.Instance.Log.LogWarning($"couldn't align the spell bar: {e.Message}");
        }
    }

    private static void Align(UI_Module_SpellBar bar)
    {
        var barRect = bar.transform.Cast<RectTransform>();
        LayoutRebuilder.ForceRebuildLayoutImmediate(barRect);

        if (FindLifeBar(barRect) is not { } lifeBar)
        {
            Plugin.Instance.Log.LogWarning("no life bar near the spell bar; leaving the layout alone");
            return;
        }

        var lifeRect = lifeBar.transform.Cast<RectTransform>();
        var (lifeLeft, lifeRight) = HorizontalExtent(lifeRect);
        var (slotsLeft, slotsRight) = SlotExtent(bar);

        var mover = LayoutFreeAncestor(barRect, lifeRect);
        var shift = (lifeLeft + lifeRight) * 0.5f - (slotsLeft + slotsRight) * 0.5f;
        if (Mathf.Abs(shift) > Tolerance)
        {
            mover.position += new Vector3(shift, 0f, 0f);
            (slotsLeft, slotsRight) = SlotExtent(bar);
        }

        var lifeParentLayout = lifeRect.parent != null ? lifeRect.parent.GetComponent<LayoutGroup>() : null;
        var widthBefore = lifeRight - lifeLeft;
        FitHorizontally(lifeRect, slotsLeft, slotsRight);
        var (newLeft, newRight) = HorizontalExtent(lifeRect);

        Plugin.Instance.Log.LogInfo(
            $"aligned spell bar: moved '{mover.name}' by {shift:0.#} so slots span {slotsLeft:0}-{slotsRight:0}; " +
            $"life bar '{lifeBar.name}' {lifeLeft:0}-{lifeRight:0} ({widthBefore:0}) -> {newLeft:0}-{newRight:0} ({newRight - newLeft:0})" +
            (lifeParentLayout != null ? $", but its parent has a {lifeParentLayout.GetIl2CppType().Name} that may undo this" : ""));
    }

    private static UI_Module_LifeBar? FindLifeBar(RectTransform barRect)
    {
        var canvas = barRect.GetComponentInParent<Canvas>();
        var root = canvas != null ? canvas.rootCanvas : null;
        UI_Module_LifeBar? best = null;
        var bestDistance = float.MaxValue;

        foreach (var lifeBar in Object.FindObjectsOfType<UI_Module_LifeBar>())
        {
            if (!lifeBar.isActiveAndEnabled) continue;
            var lifeCanvas = lifeBar.GetComponentInParent<Canvas>();
            if (root != null && (lifeCanvas == null || lifeCanvas.rootCanvas != root)) continue;

            var distance = Vector3.Distance(lifeBar.transform.position, barRect.position);
            if (distance < bestDistance)
            {
                best = lifeBar;
                bestDistance = distance;
            }
        }

        return best;
    }

    private static Transform LayoutFreeAncestor(Transform transform, Transform lifeBar)
    {
        var current = transform;
        while (current.parent != null && current.parent.GetComponent<LayoutGroup>() is { } layout && layout.enabled)
        {
            if (lifeBar.IsChildOf(current.parent))
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
