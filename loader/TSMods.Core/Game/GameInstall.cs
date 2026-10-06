namespace TSMods.Core.Game;

public sealed record GameInstall(string RootPath)
{
    public const int SteamAppId = 3762660;
    public const string ProcessName = "Trials Survivors";
    public const string DisplayName = "Trials Survivors";

    public string ExePath => Path.Combine(RootPath, ProcessName + ".exe");
    public string GameAssemblyPath => Path.Combine(RootPath, "GameAssembly.dll");
    public string BepInExPath => Path.Combine(RootPath, "BepInEx");
    public string PluginsPath => Path.Combine(BepInExPath, "plugins");
    public string ConfigPath => Path.Combine(BepInExPath, "config");
    public string InteropPath => Path.Combine(BepInExPath, "interop");
    public string CorePath => Path.Combine(BepInExPath, "core");
    public string BepInExCorePath => Path.Combine(CorePath, "BepInEx.Core.dll");
    public string DoorstopConfigPath => Path.Combine(RootPath, "doorstop_config.ini");
    public string LogPath => Path.Combine(BepInExPath, "LogOutput.log");
    public string SteamManifestPath => Path.GetFullPath(Path.Combine(RootPath, "..", "..", $"appmanifest_{SteamAppId}.acf"));

    public static bool LooksLikeGame(string path) =>
        File.Exists(Path.Combine(path, "GameAssembly.dll")) && File.Exists(Path.Combine(path, ProcessName + ".exe"));

    public string ConfigFileFor(string guid) => Path.Combine(ConfigPath, guid + ".cfg");
}
