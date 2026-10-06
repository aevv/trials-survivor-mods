using System.Text.RegularExpressions;

namespace TSMods.Core.Game;

public static partial class Doorstop
{
    public static bool? IsEnabled(GameInstall install)
    {
        if (!File.Exists(install.DoorstopConfigPath)) return null;
        var line = GeneralSectionLines(File.ReadAllLines(install.DoorstopConfigPath))
            .Select(l => EnabledPattern().Match(l))
            .FirstOrDefault(m => m.Success);
        return line is null || !bool.TryParse(line.Groups[2].Value.Trim(), out var enabled) || enabled;
    }

    public static void SetEnabled(GameInstall install, bool enabled)
    {
        if (!File.Exists(install.DoorstopConfigPath))
            throw new InvalidOperationException("BepInEx isn't installed (no doorstop_config.ini).");

        var lines = File.ReadAllLines(install.DoorstopConfigPath);
        var section = "";
        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.StartsWith('[')) section = trimmed;
            else if (section == "[General]" && EnabledPattern().Match(lines[i]) is { Success: true } match)
            {
                lines[i] = match.Groups[1].Value + (enabled ? "true" : "false");
                File.WriteAllLines(install.DoorstopConfigPath, lines);
                return;
            }
        }

        throw new InvalidOperationException("doorstop_config.ini has no [General] enabled entry.");
    }

    private static IEnumerable<string> GeneralSectionLines(IEnumerable<string> lines)
    {
        var section = "";
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('[')) section = trimmed;
            else if (section == "[General]") yield return line;
        }
    }

    [GeneratedRegex(@"^(\s*enabled\s*=\s*)(.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex EnabledPattern();
}
