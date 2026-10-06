using Microsoft.Win32;

namespace TSMods.Core.Game;

public static class GameLocator
{
    public const string DefaultWindowsPath = @"C:\Program Files (x86)\Steam\steamapps\common\Trials Survivors";

    public static GameInstall? Find(string? overridePath = null)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
            return GameInstall.LooksLikeGame(overridePath) ? new GameInstall(Path.GetFullPath(overridePath)) : null;

        foreach (var library in SteamLibraries())
        {
            var steamApps = Path.Combine(library, "steamapps");
            var installDir = SteamFiles.ReadInstallDir(Path.Combine(steamApps, $"appmanifest_{GameInstall.SteamAppId}.acf"));
            if (installDir is null) continue;

            var path = Path.Combine(steamApps, "common", installDir);
            if (GameInstall.LooksLikeGame(path)) return new GameInstall(path);
        }

        return GameInstall.LooksLikeGame(DefaultWindowsPath) ? new GameInstall(DefaultWindowsPath) : null;
    }

    public static IEnumerable<string> SteamLibraries()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in SteamRoots().Where(Directory.Exists))
        {
            var normalisedRoot = Path.GetFullPath(root);
            if (seen.Add(normalisedRoot)) yield return normalisedRoot;

            foreach (var library in SteamFiles.ReadLibraryPaths(Path.Combine(root, "steamapps", "libraryfolders.vdf")))
            {
                if (Directory.Exists(library) && seen.Add(Path.GetFullPath(library))) yield return Path.GetFullPath(library);
            }
        }
    }

    private static IEnumerable<string> SteamRoots()
    {
        if (OperatingSystem.IsWindows())
        {
            if (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) is string userPath)
                yield return userPath.Replace('/', Path.DirectorySeparatorChar);
            if (Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) is string machinePath)
                yield return machinePath;
            yield return @"C:\Program Files (x86)\Steam";
            yield break;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Path.Combine(home, ".steam", "steam");
        yield return Path.Combine(home, ".local", "share", "Steam");
    }
}
