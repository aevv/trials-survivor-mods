using System.Globalization;
using System.Text.RegularExpressions;

namespace TSMods.Core.Config;

public enum SettingKind
{
    Text,
    Bool,
    Integer,
    Decimal,
    Choice,
    Flags,
    Colour,
}

public sealed partial class ConfigSetting
{
    private static readonly HashSet<string> IntegerTypes = ["Byte", "SByte", "Int16", "UInt16", "Int32", "UInt32", "Int64", "UInt64"];
    private static readonly HashSet<string> DecimalTypes = ["Single", "Double", "Decimal"];

    internal ConfigSetting(
        string section, string key, string value, int lineIndex, string description, string? settingType,
        string? defaultValue, string? rangeMin, string? rangeMax, IReadOnlyList<string> acceptableValues, bool isFlags)
    {
        Section = section;
        Key = key;
        Value = value;
        LineIndex = lineIndex;
        Description = description;
        SettingType = settingType;
        DefaultValue = defaultValue;
        RangeMin = rangeMin;
        RangeMax = rangeMax;
        AcceptableValues = acceptableValues;
        IsFlags = isFlags;
        Kind = InferKind();
    }

    public string Section { get; }
    public string Key { get; }
    public string Value { get; internal set; }
    public int LineIndex { get; }
    public string Description { get; }
    public string? SettingType { get; }
    public string? DefaultValue { get; }
    public string? RangeMin { get; }
    public string? RangeMax { get; }
    public IReadOnlyList<string> AcceptableValues { get; }
    public bool IsFlags { get; }
    public SettingKind Kind { get; }

    public string Id => $"{Section}.{Key}";
    public bool IsDefault => DefaultValue is not null && string.Equals(Normalise(Value), Normalise(DefaultValue), StringComparison.Ordinal);
    public bool HasRange => RangeMin is not null && RangeMax is not null;

    public string? Validate(string value, out string normalised)
    {
        normalised = value.Trim();
        switch (Kind)
        {
            case SettingKind.Bool:
                if (!bool.TryParse(normalised, out var b)) return "expected true or false";
                normalised = b ? "true" : "false";
                return null;

            case SettingKind.Integer:
                if (!long.TryParse(normalised, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)) return "expected a whole number";
                return CheckRange(l);

            case SettingKind.Decimal:
                if (!double.TryParse(normalised, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return "expected a number";
                return CheckRange(d);

            case SettingKind.Choice:
                var wanted = normalised;
                var choice = AcceptableValues.FirstOrDefault(v => string.Equals(v, wanted, StringComparison.OrdinalIgnoreCase));
                if (choice is null) return $"expected one of: {string.Join(", ", AcceptableValues)}";
                normalised = choice;
                return null;

            case SettingKind.Flags:
                var parts = normalised.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                var resolved = parts.Select(p => AcceptableValues.FirstOrDefault(v => string.Equals(v, p, StringComparison.OrdinalIgnoreCase))).ToList();
                if (resolved.Any(r => r is null)) return $"expected a comma separated list of: {string.Join(", ", AcceptableValues)}";
                normalised = string.Join(", ", resolved);
                return null;

            case SettingKind.Colour:
                if (!ColourPattern().IsMatch(normalised)) return "expected #RRGGBB or #RRGGBBAA";
                normalised = normalised.ToUpperInvariant();
                return null;

            default:
                return null;
        }
    }

    private string? CheckRange(double value)
    {
        if (!HasRange) return null;
        var min = double.Parse(RangeMin!, CultureInfo.InvariantCulture);
        var max = double.Parse(RangeMax!, CultureInfo.InvariantCulture);
        return value < min || value > max ? $"must be between {RangeMin} and {RangeMax}" : null;
    }

    private SettingKind InferKind()
    {
        if (AcceptableValues.Count > 0) return IsFlags ? SettingKind.Flags : SettingKind.Choice;
        if (SettingType == "Boolean") return SettingKind.Bool;
        if (SettingType is not null && IntegerTypes.Contains(SettingType)) return SettingKind.Integer;
        if (SettingType is not null && DecimalTypes.Contains(SettingType)) return SettingKind.Decimal;
        if (SettingType == "String" && DefaultValue is not null && ColourPattern().IsMatch(DefaultValue)) return SettingKind.Colour;
        return SettingKind.Text;
    }

    private string Normalise(string value) => Validate(value, out var normalised) is null ? normalised : value.Trim();

    [GeneratedRegex("^#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$")]
    private static partial Regex ColourPattern();
}
