using System.Text.Json;
using System.Text.Json.Serialization;
using TSMods.Core.Mods;

namespace TSMods.Core.Library;

public sealed record LibraryEntry(
    string Guid,
    string Name,
    string Version,
    string Sha256,
    string MainFile,
    IReadOnlyList<string> Files,
    string Source,
    DateTimeOffset ImportedAt)
{
    [JsonIgnore] public string Id => $"{Version}+{Sha256[..8]}";

    internal string Directory { get; init; } = "";

    [JsonIgnore] public string FilesDirectory => Path.Combine(Directory, "files");
    [JsonIgnore] public string MainFilePath => Path.Combine(FilesDirectory, MainFile);
}

public sealed class ModLibrary(string root)
{
    private const string EntryFile = "entry.json";

    public string Root { get; } = root;

    public IReadOnlyList<LibraryEntry> All()
    {
        if (!Directory.Exists(Root)) return [];
        return Directory.EnumerateFiles(Root, EntryFile, SearchOption.AllDirectories)
            .Select(ReadEntry)
            .OfType<LibraryEntry>()
            .OrderBy(e => e.Guid, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(e => e, VersionComparer.Instance)
            .ToList();
    }

    public IReadOnlyList<LibraryEntry> For(string guid) =>
        All().Where(e => string.Equals(e.Guid, guid, StringComparison.OrdinalIgnoreCase)).ToList();

    public LibraryEntry? Find(string guid, string sha256) =>
        For(guid).FirstOrDefault(e => string.Equals(e.Sha256, sha256, StringComparison.OrdinalIgnoreCase));

    public LibraryEntry Import(ModInfo mod, string sourceRoot, IReadOnlyList<string> relativeFiles, string mainFile, string source)
    {
        var plugin = mod.Plugin ?? throw new InvalidOperationException($"{mod.FileName} isn't a BepInEx plugin.");
        var existing = Find(plugin.Guid, mod.Sha256);
        if (existing is not null) return existing;

        var directory = Path.Combine(Root, SafeName(plugin.Guid), $"{SafeName(plugin.Version)}+{mod.ShortHash}");
        var filesDirectory = Path.Combine(directory, "files");
        foreach (var file in relativeFiles)
        {
            var destination = Path.Combine(filesDirectory, file);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(Path.Combine(sourceRoot, file), destination, overwrite: true);
        }

        var entry = new LibraryEntry(plugin.Guid, plugin.Name, plugin.Version, mod.Sha256, mainFile, relativeFiles, source, DateTimeOffset.UtcNow)
        {
            Directory = directory,
        };
        File.WriteAllText(Path.Combine(directory, EntryFile), JsonSerializer.Serialize(entry, Json.Options));
        return entry;
    }

    public LibraryEntry ImportFile(string dllPath, string source)
    {
        var mod = ModInspector.Inspect(dllPath);
        var name = Path.GetFileName(dllPath);
        return Import(mod, Path.GetDirectoryName(Path.GetFullPath(dllPath))!, [name], name, source);
    }

    public void Delete(LibraryEntry entry) => Directory.Delete(entry.Directory, recursive: true);

    private static LibraryEntry? ReadEntry(string path)
    {
        try
        {
            var entry = JsonSerializer.Deserialize<LibraryEntry>(File.ReadAllText(path), Json.Options);
            return entry is null ? null : entry with { Directory = Path.GetDirectoryName(path)! };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string SafeName(string value) =>
        string.Concat(value.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}

public sealed class VersionComparer : IComparer<LibraryEntry>
{
    public static readonly VersionComparer Instance = new();

    public int Compare(LibraryEntry? x, LibraryEntry? y)
    {
        if (x is null || y is null) return x is null ? (y is null ? 0 : -1) : 1;
        var byVersion = CompareVersions(x.Version, y.Version);
        return byVersion != 0 ? byVersion : x.ImportedAt.CompareTo(y.ImportedAt);
    }

    public static int CompareVersions(string a, string b)
    {
        var left = a.Split('-', '+')[0];
        var right = b.Split('-', '+')[0];
        if (Version.TryParse(left, out var va) && Version.TryParse(right, out var vb) && va != vb) return va.CompareTo(vb);
        return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
