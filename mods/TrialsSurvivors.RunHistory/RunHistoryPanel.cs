using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace TrialsSurvivors.RunHistory;

public sealed class RunHistoryPanel : MonoBehaviour
{
    private const int RowsPerPage = 12;
    private const float RowHeight = 54f;
    private const int MaxSkillRows = 10;
    private const float DetailLeft = 592f;
    private const float HeaderHeight = 290f;

    private static readonly Color PanelColour = new(0.06f, 0.07f, 0.09f, 0.96f);
    private static readonly Color RowColour = new(1f, 1f, 1f, 0.04f);
    private static readonly Color SelectedRowColour = new(1f, 1f, 1f, 0.16f);

    private readonly List<(Image Background, TextMeshProUGUI Label)> _rows = new();
    private readonly List<SkillRow> _skillRows = new();
    private Dictionary<string, Sprite>? _spritesByName;
    private GameObject? _root;
    private TextMeshProUGUI? _detail;
    private TextMeshProUGUI? _skillsHeader;
    private TextMeshProUGUI? _pageLabel;
    private int _selected;
    private int _page;

    public RunHistoryPanel(IntPtr ptr) : base(ptr)
    {
    }

    public bool IsOpen => _root != null && _root.activeSelf;

    private void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        if (keyboard[Plugin.Instance.ToggleKey.Value].wasPressedThisFrame)
        {
            if (IsOpen) Close();
            else if (!Plugin.Instance.HubOnly.Value || IsInHub()) Open();
            return;
        }

        if (!IsOpen) return;

        if (keyboard.escapeKey.wasPressedThisFrame) Close();
        else if (keyboard.downArrowKey.wasPressedThisFrame || keyboard.sKey.wasPressedThisFrame) Select(_selected + 1);
        else if (keyboard.upArrowKey.wasPressedThisFrame || keyboard.wKey.wasPressedThisFrame) Select(_selected - 1);
        else if (keyboard.pageDownKey.wasPressedThisFrame || keyboard.rightArrowKey.wasPressedThisFrame || keyboard.dKey.wasPressedThisFrame) Select(_selected + RowsPerPage);
        else if (keyboard.pageUpKey.wasPressedThisFrame || keyboard.leftArrowKey.wasPressedThisFrame || keyboard.aKey.wasPressedThisFrame) Select(_selected - RowsPerPage);
    }

    private void OnDestroy() => InputBlocker.Release();

    public void Open()
    {
        if (_root == null) Build();
        _root!.SetActive(true);
        _spritesByName = null;
        InputBlocker.Block();
        Select(0);
    }

    public void Close()
    {
        if (_root != null) _root.SetActive(false);
        InputBlocker.Release();
    }

    private static bool IsInHub() => FindObjectOfType<Hub>() != null;

    private void Build()
    {
        var font = UiFactory.FindGameFont();

        _root = new GameObject("RunHistoryCanvas");
        DontDestroyOnLoad(_root);
        var canvas = _root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue;
        var scaler = _root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        _root.AddComponent<GraphicRaycaster>();

        var rootRect = _root.GetComponent<RectTransform>();
        var panel = UiFactory.Rect("Panel", rootRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-760f, -440f), new Vector2(760f, 440f));
        UiFactory.Image(panel, PanelColour, raycast: true);

        var title = UiFactory.Rect("Title", panel, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(32f, -72f), new Vector2(-32f, -16f));
        var titleText = UiFactory.Text(title, font, 40f, TextAlignmentOptions.MidlineLeft);
        titleText.overflowMode = TextOverflowModes.Overflow;
        titleText.text = $"<b>Run history</b>  <size=24><color=#8A8F98>{Plugin.Instance.ToggleKey.Value} / Esc to close  |  Up/Down select  |  Left/Right page</color></size>";

        var list = UiFactory.Rect("List", panel, Vector2.zero, new Vector2(0f, 1f), new Vector2(32f, 72f), new Vector2(DetailLeft - 40f, -88f));
        for (var i = 0; i < RowsPerPage; i++)
        {
            var index = i;
            var row = UiFactory.Rect($"Row{i}", list, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -(i + 1) * RowHeight + 4f), new Vector2(0f, -i * RowHeight));
            var background = UiFactory.Image(row, RowColour, raycast: true);
            UiFactory.Button(row, background, () => Select(_page * RowsPerPage + index));

            var labelRect = UiFactory.Rect("Label", row, Vector2.zero, Vector2.one, new Vector2(14f, 0f), new Vector2(-14f, 0f));
            var label = UiFactory.Text(labelRect, font, 22f, TextAlignmentOptions.MidlineLeft);
            label.overflowMode = TextOverflowModes.Overflow;
            _rows.Add((background, label));
        }

        var pager = UiFactory.Rect("Pager", panel, Vector2.zero, Vector2.zero, new Vector2(32f, 24f), new Vector2(DetailLeft - 40f, 64f));
        _pageLabel = UiFactory.Text(pager, font, 22f, TextAlignmentOptions.Center);
        _pageLabel.overflowMode = TextOverflowModes.Overflow;

        var detail = UiFactory.Rect("Detail", panel, new Vector2(0f, 1f), Vector2.one, new Vector2(DetailLeft, -88f - HeaderHeight), new Vector2(-32f, -88f));
        _detail = UiFactory.Text(detail, font, 24f, TextAlignmentOptions.TopLeft);
        _detail.overflowMode = TextOverflowModes.Overflow;

        var skillsTop = -88f - HeaderHeight;
        var skillsHeader = UiFactory.Rect("SkillsHeader", panel, new Vector2(0f, 1f), Vector2.one, new Vector2(DetailLeft, skillsTop - 36f), new Vector2(-32f, skillsTop));
        _skillsHeader = UiFactory.Text(skillsHeader, font, 22f, TextAlignmentOptions.BottomLeft);
        _skillsHeader.overflowMode = TextOverflowModes.Overflow;

        var skills = UiFactory.Rect("Skills", panel, Vector2.zero, Vector2.one, new Vector2(DetailLeft, 24f), new Vector2(-32f, skillsTop - 44f));
        for (var i = 0; i < MaxSkillRows; i++) _skillRows.Add(new SkillRow(skills, i, font));
    }

    private void Select(int index)
    {
        var runs = RunStore.All;
        _selected = runs.Count == 0 ? 0 : Math.Clamp(index, 0, runs.Count - 1);
        _page = _selected / RowsPerPage;
        Render(runs);
    }

    private void Render(IReadOnlyList<RunRecord> runs)
    {
        var pages = Math.Max(1, (runs.Count + RowsPerPage - 1) / RowsPerPage);
        _pageLabel!.text = runs.Count == 0 ? "" : $"<color=#8A8F98>page {_page + 1} of {pages}  |  {runs.Count} {(runs.Count == 1 ? "run" : "runs")}</color>";

        for (var i = 0; i < _rows.Count; i++)
        {
            var runIndex = _page * RowsPerPage + i;
            var (background, label) = _rows[i];
            var hasRun = runIndex < runs.Count;

            background.gameObject.SetActive(hasRun);
            if (!hasRun) continue;

            var run = runs[runIndex];
            background.color = runIndex == _selected ? SelectedRowColour : RowColour;
            label.text =
                $"<color={RunFormat.ResultColour(run.Result)}>{RunFormat.ResultLabel(run.Result)}</color>  {run.ClassName}" +
                $"<pos=62%><color=#8A8F98>{RunFormat.Duration(run.DurationSeconds)}</color><pos=80%><color=#8A8F98><size=18>{run.EndedAtUtc.ToLocalTime():d MMM}</size></color>";
        }

        if (runs.Count == 0)
        {
            _detail!.text = "<color=#8A8F98>No runs recorded yet.\n\nFinish a run (win, lose or endless) and it'll show up here.</color>";
            _skillsHeader!.text = "";
            foreach (var row in _skillRows) row.Hide();
            return;
        }

        var selected = runs[_selected];
        _detail!.text = Describe(selected);
        RenderSkills(selected);
    }

    private void RenderSkills(RunRecord run)
    {
        var skills = run.Skills.OrderByDescending(s => s.DamageDealt).ToList();
        var total = skills.Sum(s => (double)s.DamageDealt);

        _skillsHeader!.text = skills.Count == 0
            ? "<b>Skills</b>  <size=18><color=#8A8F98>not recorded for this run</color></size>"
            : "<b>Skills</b><pos=" + SkillColumn(48) + "><size=18><color=#8A8F98>level</color></size>" +
              "<pos=" + SkillColumn(60) + "><size=18><color=#8A8F98>damage</color></size>" +
              "<pos=" + SkillColumn(76) + "><size=18><color=#8A8F98>share</color></size>" +
              "<pos=" + SkillColumn(88) + "><size=18><color=#8A8F98>kills</color></size>";

        for (var i = 0; i < _skillRows.Count; i++)
        {
            if (i >= skills.Count)
            {
                _skillRows[i].Hide();
                continue;
            }

            var skill = skills[i];
            var share = total > 0 ? (float)(skill.DamageDealt / total) : 0f;
            _skillRows[i].Show(skill, share, FindSprite(skill.IconSprite));
        }
    }

    private static string SkillColumn(float percentOfText)
    {
        const float rowWidth = 1520f - DetailLeft - 32f;
        const float textLeft = 48f;
        const float textWidth = rowWidth - textLeft - 8f;
        var x = textLeft + textWidth * percentOfText / 100f;
        return $"{x / rowWidth * 100f:0.##}%";
    }

    private Sprite? FindSprite(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;

        if (_spritesByName == null)
        {
            _spritesByName = new Dictionary<string, Sprite>();
            foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
            {
                if (sprite != null && !string.IsNullOrEmpty(sprite.name)) _spritesByName.TryAdd(sprite.name, sprite);
            }
        }

        return _spritesByName.TryGetValue(name, out var found) ? found : null;
    }

    private static string Describe(RunRecord run)
    {
        var sb = new StringBuilder();
        var difficulty = run.UnfairPlusLevel > 0 ? $"{run.Difficulty} +{run.UnfairPlusLevel}" : run.Difficulty;

        sb.Append($"<size=44><b><color={RunFormat.ResultColour(run.Result)}>{RunFormat.ResultLabel(run.Result)}</color></b></size>")
          .Append($"   <color=#8A8F98>{RunFormat.When(run.EndedAtUtc)}</color>\n")
          .Append($"<color=#C9CDD4>{run.ClassName}  |  {difficulty}  |  {run.Map}</color>\n\n");

        Stats(sb,
            ("Duration", RunFormat.Duration(run.DurationSeconds)),
            ("Level", run.PlayerLevel.ToString()),
            ("Rooms", run.RoomsCompleted.ToString()),
            ("Card draws", run.CardDraws.ToString()));
        Stats(sb,
            ("Kills", RunFormat.Number(run.Kills)),
            ("Elites", RunFormat.Number(run.EliteKills)),
            ("Relics", run.RelicsCollected.ToString()),
            ("Class XP", RunFormat.Number(run.ClassXpGained)));
        Stats(sb,
            ("Damage dealt", RunFormat.Number(run.DamageDealt)),
            ("Damage taken", RunFormat.Number(run.DamageTaken)),
            ("DPS", run.DurationSeconds > 0 ? RunFormat.Number(run.DamageDealt / run.DurationSeconds) : "-"));

        return sb.ToString();
    }

    private static void Stats(StringBuilder sb, params (string Label, string Value)[] stats)
    {
        for (var i = 0; i < stats.Length; i++)
        {
            sb.Append($"<pos={i * 25}%><color=#8A8F98><size=20>{stats[i].Label}</size></color> {stats[i].Value}");
        }
        sb.Append('\n');
    }
}
