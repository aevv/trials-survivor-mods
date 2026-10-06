using System;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace TrialsSurvivors.RunHistory;

[HarmonyPatch(typeof(RunStatsTracker), nameof(RunStatsTracker.PopulateRecapData))]
internal static class RunCapture
{
    private static (RunResultType, float, int)? _lastCaptured;

    internal static RunRecord? LastRecord { get; private set; }
    internal static nint LastRecapData { get; private set; }

    internal static void ReadSkills(RunRecapData data, RunRecord record)
    {
        var skills = data._skillEntries;
        if (skills == null) return;

        record.Skills.Clear();
        for (var i = 0; i < skills.Count; i++)
        {
            var skill = skills[i];
            record.Skills.Add(new SkillRecord
            {
                NameKey = skill.NameLocalizeID ?? "",
                Name = Localize(skill.NameLocalizeID),
                IconSprite = skill.Icon != null ? skill.Icon.name : "",
                Level = skill.CurrentLevel,
                MaxLevel = skill.MaxLevel,
                DamageDealt = skill.CumulativeDamageDealt,
                Kills = skill.CumulativeKills,
                DamagePercent = skill.DamagePercent
            });
        }
    }

    [HarmonyPostfix]
    private static void Postfix(RunStatsTracker __instance, RunRecapData data, RunResultType result)
    {
        var signature = (result, __instance.TotalDuration, __instance.TotalKills);
        if (_lastCaptured == signature) return;
        _lastCaptured = signature;

        var record = new RunRecord
        {
            EndedAtUtc = DateTime.UtcNow,
            Result = result.ToString()
        };

        Capture("totals", () =>
        {
            record.DurationSeconds = __instance.TotalDuration;
            record.Kills = __instance.TotalKills;
            record.EliteKills = __instance.TotalEliteKills;
            record.RoomsCompleted = __instance.RoomsCompleted;
            record.DamageDealt = __instance.TotalDamageDealt;
            record.DamageTaken = __instance.TotalDamageTaken;
            record.RelicsCollected = __instance.RelicsCollected;
            record.ClassXpGained = __instance.ClassXpGained;
            record.PlayerLevel = __instance._cachedLevelAtEnd;
            record.CardDraws = __instance._cachedCardDraws;
        });

        Capture("damage by type", () =>
        {
            foreach (var entry in __instance._damageDealtByDamageType) record.DamageByDamageType[entry.Key] = entry.Value;
        });

        Capture("kills by entity type", () =>
        {
            foreach (var entry in __instance._killsByEntityType) record.KillsByEntityType[entry.Key] = entry.Value;
        });

        Capture("skills", () => ReadSkills(data, record));

        Capture("class", () =>
        {
            var classManager = __instance._classManager ?? LevelManager.Instance.Configuration.ClassManager;
            var classData = classManager.GetClassDataById(classManager.GetCurrentClassID());
            record.ClassId = classData.ClassID;
            record.ClassName = Localize(classData.ClassNameLocalizedKey);
        });

        Capture("difficulty", () =>
        {
            var manager = LevelManager.Instance.Configuration.DifficultyManager;
            var difficulty = manager.GetCurrentDifficulty();
            record.Difficulty = Localize(difficulty.DifficultyName.Key);
            record.DifficultyTier = difficulty.Tier;
            record.UnfairPlusLevel = manager.GetEffectiveUnfairPlusLevel();
        });

        Capture("map", () =>
        {
            record.Map = Localize(LevelManager.Instance.Configuration.MapManager.GetCurrentMap().MapName.Key);
        });

        try
        {
            RunStore.Save(record);
            LastRecord = record;
            LastRecapData = data.Pointer;
            Plugin.Instance.Log.LogInfo(
                $"recorded {record.Result} run: {record.ClassName}, {record.Difficulty}, {RunFormat.Duration(record.DurationSeconds)}, " +
                $"{record.Kills} kills, {record.Skills.Count} skills");
        }
        catch (Exception e)
        {
            Plugin.Instance.Log.LogError($"failed to save run: {e}");
        }
    }

    internal static string Localize(string? key)
    {
        if (string.IsNullOrEmpty(key)) return "";

        try
        {
            var lookup = key;
            SO_LocalizationManager.GetLocalizedValue(ref lookup, out string value, new Il2CppReferenceArray<Il2CppSystem.Object>(0));
            return string.IsNullOrEmpty(value) ? key : value;
        }
        catch
        {
            return key;
        }
    }

    private static void Capture(string part, Action read)
    {
        try
        {
            read();
        }
        catch (Exception e)
        {
            Plugin.Instance.Log.LogWarning($"couldn't capture {part}: {e.Message}");
        }
    }
}

[HarmonyPatch(typeof(RunRecapData), nameof(RunRecapData.PopulateSkillRecap))]
internal static class SkillRecapPatch
{
    [HarmonyPostfix]
    private static void Postfix(RunRecapData __instance)
    {
        var record = RunCapture.LastRecord;
        if (record == null || RunCapture.LastRecapData != __instance.Pointer) return;
        if ((DateTime.UtcNow - record.EndedAtUtc).TotalMinutes > 5) return;

        try
        {
            RunCapture.ReadSkills(__instance, record);
            RunStore.Rewrite(record);
            Plugin.Instance.Log.LogInfo($"added {record.Skills.Count} skills to the {record.Result} run");
        }
        catch (Exception e)
        {
            Plugin.Instance.Log.LogWarning($"couldn't add skills to the run: {e.Message}");
        }
    }
}