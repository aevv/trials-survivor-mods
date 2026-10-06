using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TrialsSurvivors.RunHistory;

internal sealed class SkillRow
{
    public const float Height = 40f;
    private const float IconSize = 34f;

    private static readonly Color ShareColour = new(0.36f, 0.62f, 1f, 0.22f);
    private static readonly Color RowColour = new(1f, 1f, 1f, 0.03f);

    private readonly GameObject _root;
    private readonly RectTransform _share;
    private readonly Image _icon;
    private readonly TextMeshProUGUI _text;

    public SkillRow(RectTransform parent, int index, TMP_FontAsset? font)
    {
        var rect = UiFactory.Rect($"Skill{index}", parent, new Vector2(0f, 1f), Vector2.one,
            new Vector2(0f, -(index + 1) * Height + 3f), new Vector2(0f, -index * Height));
        _root = rect.gameObject;
        UiFactory.Image(rect, RowColour);

        _share = UiFactory.Rect("Share", rect, Vector2.zero, new Vector2(0f, 1f), Vector2.zero, Vector2.zero);
        UiFactory.Image(_share, ShareColour);

        var icon = UiFactory.Rect("Icon", rect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(4f, -IconSize / 2f), new Vector2(4f + IconSize, IconSize / 2f));
        _icon = UiFactory.Image(icon, Color.white);
        _icon.preserveAspect = true;

        var text = UiFactory.Rect("Text", rect, Vector2.zero, Vector2.one, new Vector2(IconSize + 14f, 0f), new Vector2(-8f, 0f));
        _text = UiFactory.Text(text, font, 22f, TextAlignmentOptions.MidlineLeft);
        _text.overflowMode = TextOverflowModes.Overflow;
    }

    public void Show(SkillRecord skill, float share, Sprite? icon)
    {
        _root.SetActive(true);
        _share.anchorMax = new Vector2(Mathf.Clamp01(share), 1f);

        _icon.sprite = icon;
        _icon.enabled = icon != null;

        var name = string.IsNullOrEmpty(skill.Name) ? skill.NameKey : skill.Name;
        _text.text =
            $"{name}" +
            $"<pos=48%>{skill.Level}<color=#8A8F98>/{skill.MaxLevel}</color>" +
            $"<pos=60%>{RunFormat.Number(skill.DamageDealt)}" +
            $"<pos=76%>{(share * 100f).ToString("0.#", CultureInfo.InvariantCulture)}%" +
            $"<pos=88%>{RunFormat.Number(skill.Kills)}";
    }

    public void Hide() => _root.SetActive(false);
}
