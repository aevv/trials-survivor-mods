using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

namespace TrialsSurvivors.RunHud;

public sealed class RunHudUpdater : MonoBehaviour
{
    private const float ModifierRefreshSeconds = 2f;

    private readonly StringBuilder _text = new();
    private Dictionary<int, ARPGDatabaseEntry_Stats>? _statsById;
    private MonsterCardManager? _monsterCards;
    private string _modifierLine = "";
    private float _nextRefresh;
    private float _nextModifierRefresh;
    private string? _lastText;

    public RunHudUpdater(IntPtr ptr) : base(ptr)
    {
    }

    private void Update()
    {
        if (Time.unscaledTime < _nextRefresh) return;
        _nextRefresh = Time.unscaledTime + Plugin.Instance.RefreshInterval.Value;

        var label = RunHudState.Label;
        if (label == null) return;

        var plugin = Plugin.Instance;
        var tracker = RunHudState.Tracker;
        if (!plugin.Enabled.Value || tracker == null)
        {
            SetText(label, "");
            return;
        }

        try
        {
            SetText(label, Compose(plugin, tracker));
        }
        catch (Exception e)
        {
            plugin.Log.LogWarning($"run stats refresh failed: {e.Message}");
            _nextRefresh = Time.unscaledTime + 5f;
        }
    }

    private string Compose(Plugin plugin, RunStatsTracker tracker)
    {
        _text.Clear();

        if (plugin.ShowKills.Value) Append($"{Number(tracker._totalKills + tracker._pendingKills)} <size=70%>kills</size>");
        if (plugin.ShowEliteKills.Value) Append($"{Number(tracker._totalEliteKills + tracker._pendingEliteKills)} <size=70%>elites</size>");
        if (plugin.ShowRooms.Value) Append($"{tracker.RoomsCompleted} <size=70%>rooms</size>");

        if (plugin.ShowMonsterModifiers.Value)
        {
            if (Time.unscaledTime >= _nextModifierRefresh)
            {
                _nextModifierRefresh = Time.unscaledTime + ModifierRefreshSeconds;
                _modifierLine = DescribeMonsterModifiers();
            }

            if (_modifierLine.Length > 0) _text.Append("\n<size=60%><color=#E8A0A0>").Append(_modifierLine).Append("</color></size>");
        }

        return _text.ToString();
    }

    private void Append(string part)
    {
        if (_text.Length > 0) _text.Append("<color=#FFFFFF80>  ·  </color>");
        _text.Append(part);
    }

    private string DescribeMonsterModifiers()
    {
        if (_monsterCards == null) _monsterCards = FindObjectOfType<MonsterCardManager>();
        if (_monsterCards == null) return "";

        var effects = _monsterCards._activeEffects;
        if (effects == null || effects.Count == 0) return "";

        var byStat = new Dictionary<int, (float Multiplier, float Additive)>();
        for (var i = 0; i < effects.Count; i++)
        {
            var buff = effects[i]?.TryCast<MonsterCardEffect_StatBuff>();
            if (buff == null) continue;

            var current = byStat.TryGetValue(buff.StatId, out var existing) ? existing : (1f, 0f);
            byStat[buff.StatId] = buff.IsAdditive
                ? (current.Item1, current.Item2 + buff.Value)
                : (current.Item1 * buff.Value, current.Item2);
        }

        var parts = new List<string>();
        foreach (var (statId, (multiplier, additive)) in byStat.OrderBy(kv => kv.Key))
        {
            var name = StatName(statId);
            if (!Mathf.Approximately(multiplier, 1f)) parts.Add($"{Signed((multiplier - 1f) * 100f)}% {name}");
            if (!Mathf.Approximately(additive, 0f)) parts.Add($"{Signed(additive)} {name}");
        }

        return parts.Count == 0 ? "" : "monsters " + string.Join(", ", parts);
    }

    private string StatName(int statId)
    {
        _statsById ??= Resources.FindObjectsOfTypeAll<ARPGDatabaseEntry_Stats>()
            .GroupBy(s => s.ID)
            .ToDictionary(g => g.Key, g => g.First());

        if (!_statsById.TryGetValue(statId, out var stat)) return $"stat {statId}";

        var display = stat.GetEntryDisplayName();
        return string.IsNullOrWhiteSpace(display) ? stat.entryName : display;
    }

    private void SetText(TMPro.TextMeshProUGUI label, string text)
    {
        if (text == _lastText) return;
        _lastText = text;
        label.text = text;
    }

    private static string Signed(float value) =>
        (value >= 0 ? "+" : "") + value.ToString(Mathf.Abs(value) >= 10f ? "0" : "0.#", CultureInfo.InvariantCulture);

    private static string Number(int value) => value >= 10000
        ? (value / 1000f).ToString("0.#", CultureInfo.InvariantCulture) + "k"
        : value.ToString("N0", CultureInfo.InvariantCulture);
}
