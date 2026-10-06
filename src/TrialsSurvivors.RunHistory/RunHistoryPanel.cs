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

    private static readonly Color PanelColour = new(0.06f, 0.07f, 0.09f, 0.94f);
    private static readonly Color RowColour = new(1f, 1f, 1f, 0.04f);
    private static readonly Color SelectedRowColour = new(1f, 1f, 1f, 0.16f);

    private readonly List<(Image Background, TextMeshProUGUI Label)> _rows = new();
    private GameObject? _root;
    private TextMeshProUGUI? _detail;
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
        else if (keyboard.downArrowKey.wasPressedThisFrame) Select(_selected + 1);
        else if (keyboard.upArrowKey.wasPressedThisFrame) Select(_selected - 1);
        else if (keyboard.pageDownKey.wasPressedThisFrame || keyboard.rightArrowKey.wasPressedThisFrame) Select(_selected + RowsPerPage);
        else if (keyboard.pageUpKey.wasPressedThisFrame || keyboard.leftArrowKey.wasPressedThisFrame) Select(_selected - RowsPerPage);
    }

    public void Open()
    {
        if (_root == null) Build();
        _root!.SetActive(true);
        Select(0);
    }

    public void Close()
    {
        if (_root != null) _root.SetActive(false);
    }

    private static bool IsInHub() => FindObjectOfType<Hub>() != null;

    private void Build()
    {
        var font = UiFactory.FindGameFont();

        _root = new GameObject("RunHistoryCanvas");
        DontDestroyOnLoad(_root);
        var canvas = _root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;
        var scaler = _root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        _root.AddComponent<GraphicRaycaster>();

        var rootRect = _root.GetComponent<RectTransform>();
        var panel = UiFactory.Rect("Panel", rootRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-760f, -440f), new Vector2(760f, 440f));
        UiFactory.Image(panel, PanelColour, raycast: true);

        var title = UiFactory.Rect("Title", panel, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(32f, -72f), new Vector2(-32f, -16f));
        UiFactory.Text(title, font, 40f, TextAlignmentOptions.MidlineLeft).text =
            $"<b>Run history</b>  <size=24><color=#8A8F98>{Plugin.Instance.ToggleKey.Value} / Esc to close · ↑↓ select · ←→ page</color></size>";

        var list = UiFactory.Rect("List", panel, Vector2.zero, new Vector2(0f, 1f), new Vector2(32f, 72f), new Vector2(552f, -88f));
        for (var i = 0; i < RowsPerPage; i++)
        {
            var index = i;
            var row = UiFactory.Rect($"Row{i}", list, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -(i + 1) * RowHeight + 4f), new Vector2(0f, -i * RowHeight));
            var background = UiFactory.Image(row, RowColour, raycast: true);
            UiFactory.Button(row, background, () => Select(_page * RowsPerPage + index));

            var label = UiFactory.Text(UiFactory.Fill("Label", row, 12f), font, 22f, TextAlignmentOptions.MidlineLeft);
            _rows.Add((background, label));
        }

        var pager = UiFactory.Rect("Pager", panel, Vector2.zero, Vector2.zero, new Vector2(32f, 24f), new Vector2(552f, 64f));
        _pageLabel = UiFactory.Text(pager, font, 22f, TextAlignmentOptions.Center);

        var detail = UiFactory.Rect("Detail", panel, Vector2.zero, Vector2.one, new Vector2(592f, 32f), new Vector2(-32f, -88f));
        _detail = UiFactory.Text(detail, font, 24f, TextAlignmentOptions.TopLeft);
        _detail.enableWordWrapping = true;
        _detail.overflowMode = TextOverflowModes.Truncate;
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
        _pageLabel!.text = runs.Count == 0 ? "" : $"<color=#8A8F98>page {_page + 1} of {pages} · {runs.Count} runs</color>";

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
                $"<pos=62%><color=#8A8F98>{RunFormat.Duration(run.DurationSeconds)}</color><pos=78%><color=#8A8F98><size=18>{run.EndedAtUtc.ToLocalTime():d MMM}</size></color>";
        }

        _detail!.text = runs.Count == 0
            ? "<color=#8A8F98>No runs recorded yet.\n\nFinish a run (win, lose or endless) and it'll show up here.</color>"
            : Describe(runs[_selected]);
    }

    private static string Describe(RunRecord run)
    {
        var sb = new StringBuilder();
        var difficulty = run.UnfairPlusLevel > 0 ? $"{run.Difficulty} +{run.UnfairPlusLevel}" : run.Difficulty;

        sb.Append($"<size=44><b><color={RunFormat.ResultColour(run.Result)}>{RunFormat.ResultLabel(run.Result)}</color></b></size>")
          .Append($"   <color=#8A8F98>{RunFormat.When(run.EndedAtUtc)}</color>\n")
          .Append($"<color=#C9CDD4>{run.ClassName} · {difficulty} · {run.Map}</color>\n\n");

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

        if (run.Skills.Count == 0) return sb.ToString();

        sb.Append("\n<b>Skills</b>\n")
          .Append("<color=#8A8F98><size=20>skill<pos=46%>level<pos=60%>damage<pos=76%>share<pos=88%>kills</size></color>\n");

        var shareScale = run.Skills.All(s => s.DamagePercent <= 1f) ? 100f : 1f;

        foreach (var skill in run.Skills.OrderByDescending(s => s.DamageDealt))
        {
            var name = string.IsNullOrEmpty(skill.Name) ? skill.NameKey : skill.Name;
            sb.Append($"{name}<pos=46%>{skill.Level}<color=#8A8F98>/{skill.MaxLevel}</color>")
              .Append($"<pos=60%>{RunFormat.Number(skill.DamageDealt)}")
              .Append($"<pos=76%>{skill.DamagePercent * shareScale:0.#}%")
              .Append($"<pos=88%>{RunFormat.Number(skill.Kills)}\n");
        }

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
