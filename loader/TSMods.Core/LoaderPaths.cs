using System.Text.Json;

namespace TSMods.Core;

public sealed record LoaderPaths(string Root)
{
    public const string HomeVariable = "TSMODS_HOME";

    public string Library => Path.Combine(Root, "library");
    public string Profiles => Path.Combine(Root, "profiles");
    public string Downloads => Path.Combine(Root, "downloads");
    public string SettingsFile => Path.Combine(Root, "settings.json");

    public static LoaderPaths Default()
    {
        var overridden = Environment.GetEnvironmentVariable(HomeVariable);
        return new LoaderPaths(string.IsNullOrWhiteSpace(overridden)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TSMods")
            : overridden);
    }
}

public sealed class LoaderSettings
{
    public const string DefaultReleaseRepo = "aevv/trials-survivor-mods";

    public string? GamePath { get; set; }
    public string ReleaseRepo { get; set; } = DefaultReleaseRepo;

    public static LoaderSettings Load(LoaderPaths paths)
    {
        if (!File.Exists(paths.SettingsFile)) return new LoaderSettings();
        return JsonSerializer.Deserialize<LoaderSettings>(File.ReadAllText(paths.SettingsFile), Json.Options) ?? new LoaderSettings();
    }

    public void Save(LoaderPaths paths)
    {
        Directory.CreateDirectory(paths.Root);
        File.WriteAllText(paths.SettingsFile, JsonSerializer.Serialize(this, Json.Options));
    }
}

internal static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
}
