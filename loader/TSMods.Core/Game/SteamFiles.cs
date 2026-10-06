using System.Text.RegularExpressions;

namespace TSMods.Core.Game;

public static partial class SteamFiles
{
    public static string? ReadBuildId(string appManifestPath) => ReadValue(appManifestPath, BuildIdPattern());

    public static string? ReadInstallDir(string appManifestPath) => ReadValue(appManifestPath, InstallDirPattern());

    public static IReadOnlyList<string> ReadLibraryPaths(string libraryFoldersVdfPath)
    {
        if (!File.Exists(libraryFoldersVdfPath)) return [];
        return LibraryPathPattern().Matches(File.ReadAllText(libraryFoldersVdfPath))
            .Select(m => Unescape(m.Groups[1].Value))
            .ToList();
    }

    private static string? ReadValue(string path, Regex pattern)
    {
        if (!File.Exists(path)) return null;
        var match = pattern.Match(File.ReadAllText(path));
        return match.Success ? Unescape(match.Groups[1].Value) : null;
    }

    private static string Unescape(string vdfValue) => vdfValue.Replace(@"\\", @"\");

    [GeneratedRegex("\"buildid\"\\s+\"(\\d+)\"")]
    private static partial Regex BuildIdPattern();

    [GeneratedRegex("\"installdir\"\\s+\"([^\"]+)\"")]
    private static partial Regex InstallDirPattern();

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"")]
    private static partial Regex LibraryPathPattern();
}
