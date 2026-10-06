using System.Text;
using System.Text.RegularExpressions;

namespace TSMods.Core.Config;

public sealed partial class ConfigDocument
{
    private readonly List<string> _lines;
    private readonly List<ConfigSetting> _settings;

    private ConfigDocument(string path, List<string> lines, List<ConfigSetting> settings, string? pluginName, string? pluginVersion, string? guid)
    {
        Path = path;
        _lines = lines;
        _settings = settings;
        PluginName = pluginName;
        PluginVersion = pluginVersion;
        Guid = guid;
    }

    public string Path { get; }
    public string? PluginName { get; }
    public string? PluginVersion { get; }
    public string? Guid { get; }
    public IReadOnlyList<ConfigSetting> Settings => _settings;
    public bool IsDirty { get; private set; }

    public static ConfigDocument Load(string path) => Parse(path, File.ReadAllText(path));

    public static ConfigDocument Parse(string path, string content)
    {
        var lines = content.ReplaceLineEndings("\n").Split('\n').ToList();
        if (lines.Count > 0 && lines[^1] == "") lines.RemoveAt(lines.Count - 1);

        var settings = new List<ConfigSetting>();
        string? pluginName = null, pluginVersion = null, guid = null;
        var section = "";
        var pending = new PendingSetting();

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0) continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1];
                pending = new PendingSetting();
                continue;
            }

            if (section.Length == 0)
            {
                if (HeaderPluginPattern().Match(line) is { Success: true } header)
                {
                    pluginName = header.Groups[1].Value;
                    pluginVersion = header.Groups[2].Value;
                }
                else if (HeaderGuidPattern().Match(line) is { Success: true } guidMatch)
                {
                    guid = guidMatch.Groups[1].Value.Trim();
                }
                continue;
            }

            if (line.StartsWith("##"))
            {
                pending.Description.Add(line.TrimStart('#').Trim());
            }
            else if (line.StartsWith('#'))
            {
                pending.Absorb(line.TrimStart('#').Trim());
            }
            else if (KeyValuePattern().Match(lines[i]) is { Success: true } kv)
            {
                settings.Add(pending.Build(section, kv.Groups[1].Value.Trim(), kv.Groups[2].Value, i));
                pending = new PendingSetting();
            }
        }

        return new ConfigDocument(path, lines, settings, pluginName, pluginVersion, guid);
    }

    public ConfigSetting? Find(string id) =>
        _settings.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? _settings.SingleOrDefaultIfUnique(s => string.Equals(s.Key, id, StringComparison.OrdinalIgnoreCase));

    public string? Set(ConfigSetting setting, string value)
    {
        var error = setting.Validate(value, out var normalised);
        if (error is not null) return error;
        if (normalised == setting.Value) return null;

        setting.Value = normalised;
        _lines[setting.LineIndex] = $"{setting.Key} = {normalised}";
        IsDirty = true;
        return null;
    }

    public string Render()
    {
        var builder = new StringBuilder();
        foreach (var line in _lines) builder.Append(line).Append(Environment.NewLine);
        return builder.ToString();
    }

    public void Save()
    {
        File.WriteAllText(Path, Render());
        IsDirty = false;
    }

    private sealed class PendingSetting
    {
        public List<string> Description { get; } = [];
        private string? _type, _default, _min, _max;
        private List<string> _acceptable = [];
        private bool _flags;

        public void Absorb(string comment)
        {
            if (Strip(comment, "Setting type:") is { } type) _type = type;
            else if (Strip(comment, "Default value:") is { } def) _default = def;
            else if (Strip(comment, "Acceptable values:") is { } values)
                _acceptable = values.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
            else if (RangePattern().Match(comment) is { Success: true } range)
                (_min, _max) = (range.Groups[1].Value, range.Groups[2].Value);
            else if (comment.StartsWith("Multiple values can be set", StringComparison.Ordinal)) _flags = true;
        }

        public ConfigSetting Build(string section, string key, string value, int line) =>
            new(section, key, value, line, string.Join("\n", Description), _type, _default, _min, _max, _acceptable, _flags);

        private static string? Strip(string comment, string prefix) =>
            comment.StartsWith(prefix, StringComparison.Ordinal) ? comment[prefix.Length..].Trim() : null;
    }

    [GeneratedRegex(@"^## Settings file was created by plugin (.+?) v(\S+)$")]
    private static partial Regex HeaderPluginPattern();

    [GeneratedRegex(@"^## Plugin GUID:\s*(.+)$")]
    private static partial Regex HeaderGuidPattern();

    [GeneratedRegex(@"^\s*([^=#\[][^=]*?)\s*=\s?(.*)$")]
    private static partial Regex KeyValuePattern();

    [GeneratedRegex(@"^Acceptable value range: From (\S+) to (\S+)$")]
    private static partial Regex RangePattern();
}

internal static class EnumerableExtensions
{
    public static T? SingleOrDefaultIfUnique<T>(this IEnumerable<T> source, Func<T, bool> predicate) where T : class
    {
        var matches = source.Where(predicate).Take(2).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }
}
