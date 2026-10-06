using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TrialsSurvivors.RoomSkip;

internal static class UiDump
{
    private const int MaxDepth = 6;
    private const int MaxLines = 150;

    private static bool _done;

    public static void Once(ChunkObjectiveUI ui)
    {
        if (_done || !Plugin.Instance.DumpObjectiveUi.Value) return;
        _done = true;

        var lines = new List<string>();
        var canvas = ui.GetComponentInParent<Canvas>();
        var root = canvas != null ? canvas.rootCanvas : null;
        lines.Add($"objective panel '{ui.name}' on canvas '{(root != null ? root.name : "none")}' " +
                  $"({(root != null ? root.renderMode.ToString() : "-")}, scale {(root != null ? root.scaleFactor : 0f):0.##}), " +
                  $"parent '{(ui.transform.parent != null ? ui.transform.parent.name : "none")}', " +
                  $"rewardFiller '{(ui._rewardFiller != null ? ui._rewardFiller.name : "none")}', rewardIcon '{(ui._rewardIcon != null ? ui._rewardIcon.name : "none")}'");
        Walk(ui.transform, 0, lines);

        Plugin.Instance.Log.LogInfo(string.Join("\n", lines.Take(MaxLines)) + (lines.Count > MaxLines ? $"\n  ... {lines.Count - MaxLines} more" : ""));
    }

    private static void Walk(Transform transform, int depth, List<string> lines)
    {
        if (depth > MaxDepth) return;

        var line = new StringBuilder();
        line.Append(' ', 2 + depth * 2).Append(transform.name);
        if (!transform.gameObject.activeSelf) line.Append(" [off]");

        if (transform.TryCast<RectTransform>() is { } rect)
        {
            var size = rect.rect.size;
            line.Append($" {size.x:0}x{size.y:0} anchors({rect.anchorMin.x:0.##},{rect.anchorMin.y:0.##})-({rect.anchorMax.x:0.##},{rect.anchorMax.y:0.##}) pivot({rect.pivot.x:0.##},{rect.pivot.y:0.##}) pos({rect.anchoredPosition.x:0},{rect.anchoredPosition.y:0})");
        }

        var go = transform.gameObject;
        if (go.GetComponent<Image>() is { } image)
        {
            var sprite = image.sprite != null ? $"{image.sprite.name} border{image.sprite.border}" : "no sprite";
            line.Append($" | Image({sprite}, {image.type}, {Hex(image.color)})");
        }
        if (go.GetComponent<TextMeshProUGUI>() is { } text)
        {
            var value = text.text ?? "";
            if (value.Length > 24) value = value[..24] + "..";
            line.Append($" | TMP('{value}', {text.fontSize:0}, {(text.font != null ? text.font.name : "no font")}, {Hex(text.color)})");
        }
        if (go.GetComponent<LayoutGroup>() is { } layout) line.Append($" | {layout.GetIl2CppType().Name}({layout.childAlignment})");
        if (go.GetComponent<ContentSizeFitter>() != null) line.Append(" | ContentSizeFitter");
        if (go.GetComponent<LayoutElement>() is { } element) line.Append($" | LayoutElement(ignore={element.ignoreLayout})");
        if (go.GetComponent<CanvasGroup>() is { } group) line.Append($" | CanvasGroup(alpha {group.alpha:0.##})");
        if (go.GetComponent<Canvas>() != null) line.Append(" | Canvas");

        lines.Add(line.ToString());
        for (var i = 0; i < transform.childCount; i++) Walk(transform.GetChild(i), depth + 1, lines);
    }

    private static string Hex(Color colour) =>
        $"#{(int)(colour.r * 255):X2}{(int)(colour.g * 255):X2}{(int)(colour.b * 255):X2}{(int)(colour.a * 255):X2}";
}
