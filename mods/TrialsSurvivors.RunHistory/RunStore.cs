using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BepInEx;

namespace TrialsSurvivors.RunHistory;

internal static class RunStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string Directory => Path.Combine(Paths.ConfigPath, "runhistory");

    private static List<RunRecord>? _cache;

    public static IReadOnlyList<RunRecord> All => _cache ??= Load();

    public static void Save(RunRecord record)
    {
        _cache ??= Load();
        Rewrite(record);
        _cache.Insert(0, record);
    }

    public static void Rewrite(RunRecord record)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var path = Path.Combine(Directory, $"{record.EndedAtUtc:yyyyMMdd-HHmmss}-{record.Result.ToLowerInvariant()}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(record, JsonOptions));
    }

    private static List<RunRecord> Load()
    {
        if (!System.IO.Directory.Exists(Directory)) return new List<RunRecord>();

        var runs = new List<RunRecord>();
        foreach (var path in System.IO.Directory.EnumerateFiles(Directory, "*.json"))
        {
            try
            {
                if (JsonSerializer.Deserialize<RunRecord>(File.ReadAllText(path), JsonOptions) is { } run) runs.Add(run);
            }
            catch (Exception e)
            {
                Plugin.Instance.Log.LogWarning($"skipping unreadable run file {Path.GetFileName(path)}: {e.Message}");
            }
        }

        return runs.OrderByDescending(r => r.EndedAtUtc).ToList();
    }
}
