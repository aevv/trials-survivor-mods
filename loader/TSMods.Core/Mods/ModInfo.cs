namespace TSMods.Core.Mods;

public sealed record PluginInfo(string Guid, string Name, string Version);

public sealed record ModDependency(string Guid, bool Soft);

public sealed record BuildStamp(string? GameBuildId, string? GameAssemblyHash, string? BepInExVersion);

public sealed record ModInfo(
    string FilePath,
    string Sha256,
    PluginInfo? Plugin,
    BuildStamp? Stamp,
    IReadOnlyList<ModDependency> Dependencies)
{
    public string FileName => Path.GetFileName(FilePath);
    public bool IsPlugin => Plugin is not null;
    public string ShortHash => Sha256[..8];
}
