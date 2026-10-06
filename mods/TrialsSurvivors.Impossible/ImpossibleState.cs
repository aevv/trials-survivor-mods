using System;
using UnityEngine;

namespace TrialsSurvivors.Impossible;

internal static class ImpossibleState
{
    private const string MaxHealthStatName = "Max Health";

    private static DifficultyManager? _manager;
    private static int _activeFrame = -1;
    private static bool _active;
    private static int? _maxHealthStatId;
    private static bool _suppressClear;

    public static bool Selected => Plugin.Instance.Enabled.Value && Plugin.Instance.Selected.Value;

    public static bool IsActive
    {
        get
        {
            var frame = Time.frameCount;
            if (frame == _activeFrame) return _active;
            _activeFrame = frame;
            _active = ComputeActive();
            return _active;
        }
    }

    public static void Remember(DifficultyManager? manager)
    {
        if (manager != null) _manager = manager;
    }

    public static DifficultyManager? Manager
    {
        get
        {
            if (_manager != null && !_manager.WasCollected) return _manager;
            try
            {
                var level = LevelManager.Instance;
                if (level != null && level.Configuration != null) _manager = level.Configuration.DifficultyManager;
            }
            catch (Exception)
            {
                _manager = null;
            }
            return _manager;
        }
    }

    public static bool IsLastClassic(DifficultyManager manager, SO_DifficultyData? difficulty)
    {
        if (difficulty == null) return false;
        var last = manager.GetLastClassicDifficulty();
        return last != null && last.Pointer == difficulty.Pointer;
    }

    public static void Select(string reason)
    {
        _activeFrame = -1;
        if (Plugin.Instance.Selected.Value) return;
        Plugin.Instance.Selected.Value = true;
        Plugin.Instance.Log.LogInfo($"impossible selected ({reason})");
    }

    public static void Deselect(string reason)
    {
        _activeFrame = -1;
        if (!Plugin.Instance.Selected.Value) return;
        Plugin.Instance.Selected.Value = false;
        Plugin.Instance.Log.LogInfo($"impossible deselected ({reason})");
    }

    public static void WithoutClearing(Action action)
    {
        _suppressClear = true;
        try
        {
            action();
        }
        finally
        {
            _suppressClear = false;
        }
    }

    public static bool ClearingSuppressed => _suppressClear;

    public static int? MaxHealthStatId
    {
        get
        {
            if (_maxHealthStatId.HasValue) return _maxHealthStatId;
            var database = Manager_ARPGDatabase.Instance;
            if (database == null) return null;

            foreach (var entry in database.GetStats())
            {
                if (entry.Value == null || entry.Value.entryName != MaxHealthStatName) continue;
                _maxHealthStatId = entry.Key;
                Plugin.Instance.Log.LogInfo($"resolved '{MaxHealthStatName}' stat id {entry.Key}");
                return _maxHealthStatId;
            }

            Plugin.Instance.Log.LogWarning($"no '{MaxHealthStatName}' stat in the database; monster health won't be scaled");
            _maxHealthStatId = -1;
            return _maxHealthStatId;
        }
    }

    public static CombatEntityStatData? MaxHealthStat(ARPGEntity? entity)
    {
        if (entity == null) return null;
        var id = MaxHealthStatId;
        if (id is null or -1) return null;
        var stats = entity.ARPGStats;
        if (stats == null) return null;
        return stats.TryGetStat(id.Value, out var stat) ? stat : null;
    }

    private static bool ComputeActive()
    {
        if (!Selected) return false;
        var manager = Manager;
        if (manager == null) return false;
        return IsLastClassic(manager, manager.GetPersistentDifficulty());
    }
}
