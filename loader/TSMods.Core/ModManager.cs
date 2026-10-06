using TSMods.Core.Game;
using TSMods.Core.Library;
using TSMods.Core.Mods;

namespace TSMods.Core;

public sealed class GameRunningException()
    : InvalidOperationException($"{GameInstall.DisplayName} is running and locks its plugins. Close the game first.");

public sealed record InstalledPlugin(ModInfo Info, string RelativePath, IReadOnlyList<string> RelativeFiles)
{
    public PluginInfo Plugin => Info.Plugin!;
}

public sealed record ModOverview(
    string Guid,
    string Name,
    InstalledPlugin? Active,
    LibraryEntry? ActiveEntry,
    IReadOnlyList<LibraryEntry> Versions,
    IReadOnlyList<string> MissingDependencies)
{
    public bool Enabled => Active is not null;
    public string? Version => Active?.Plugin.Version ?? Versions.FirstOrDefault()?.Version;
    public string? ActiveEntryId => ActiveEntry?.Id;
    public string ShortName =>
        Path.GetFileNameWithoutExtension(Active?.Info.FileName ?? Versions.FirstOrDefault()?.MainFile ?? Guid).Split('.')[^1];
}

public sealed class ModManager(GameInstall install, ModLibrary library, ProfileStore profiles, Func<bool>? isGameRunning = null)
{
    private readonly Func<bool> _isGameRunning = isGameRunning ?? GameProcess.IsRunning;

    public GameInstall Install { get; } = install;
    public ModLibrary Library { get; } = library;
    public ProfileStore Profiles { get; } = profiles;

    public IReadOnlyList<InstalledPlugin> ScanPlugins()
    {
        var root = Install.PluginsPath;
        if (!Directory.Exists(root)) return [];

        var plugins = new List<InstalledPlugin>();
        foreach (var dll in Directory.EnumerateFiles(root, "*.dll", SearchOption.TopDirectoryOnly))
        {
            var info = ModInspector.Inspect(dll);
            if (info.IsPlugin) plugins.Add(new InstalledPlugin(info, info.FileName, [info.FileName]));
        }

        foreach (var folder in Directory.EnumerateDirectories(root))
        {
            var folderPlugins = Directory.EnumerateFiles(folder, "*.dll", SearchOption.AllDirectories)
                .Select(ModInspector.Inspect)
                .Where(i => i.IsPlugin)
                .ToList();

            var folderFiles = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(root, f))
                .ToList();

            foreach (var info in folderPlugins)
            {
                var relative = Path.GetRelativePath(root, info.FilePath);
                plugins.Add(new InstalledPlugin(info, relative, folderPlugins.Count == 1 ? folderFiles : [relative]));
            }
        }

        return plugins;
    }

    public IReadOnlyList<ModOverview> Overview()
    {
        var installed = ScanPlugins();
        var entries = Library.All();
        var activeGuids = installed.Select(p => p.Plugin.Guid).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var guids = installed.Select(p => p.Plugin.Guid)
            .Concat(entries.Select(e => e.Guid))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return guids.Select(guid =>
            {
                var active = installed.FirstOrDefault(p => string.Equals(p.Plugin.Guid, guid, StringComparison.OrdinalIgnoreCase));
                var versions = entries.Where(e => string.Equals(e.Guid, guid, StringComparison.OrdinalIgnoreCase)).ToList();
                var activeEntry = active is null ? null : versions.FirstOrDefault(e => string.Equals(e.Sha256, active.Info.Sha256, StringComparison.OrdinalIgnoreCase));
                var missing = active?.Info.Dependencies.Where(d => !d.Soft && !activeGuids.Contains(d.Guid)).Select(d => d.Guid).ToList() ?? [];
                var name = active?.Plugin.Name ?? versions[0].Name;
                return new ModOverview(guid, name, active, activeEntry, versions, missing);
            })
            .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<LibraryEntry> SyncLibrary() =>
        ScanPlugins()
            .Where(p => Library.Find(p.Plugin.Guid, p.Info.Sha256) is null)
            .Select(Store)
            .ToList();

    public LibraryEntry Enable(string guid, string? entryId = null)
    {
        EnsureGameClosed();
        var mod = Get(guid);
        if (entryId is null && mod.Active is not null) return Store(mod.Active);
        var entry = entryId is null ? mod.Versions.FirstOrDefault() : FindVersion(mod, entryId);
        if (entry is null) throw new InvalidOperationException($"No stored version of {mod.Name}{(entryId is null ? "" : $" matching '{entryId}'")}.");
        if (mod.ActiveEntry?.Id == entry.Id) return entry;

        if (mod.Active is not null)
        {
            Store(mod.Active);
            Remove(mod.Active);
        }

        foreach (var file in entry.Files)
        {
            var destination = Path.Combine(Install.PluginsPath, file);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(Path.Combine(entry.FilesDirectory, file), destination, overwrite: true);
        }

        return entry;
    }

    public LibraryEntry Disable(string guid)
    {
        EnsureGameClosed();
        var mod = Get(guid);
        if (mod.Active is null) throw new InvalidOperationException($"{mod.Name} is already disabled.");

        var entry = Store(mod.Active);
        Remove(mod.Active);
        return entry;
    }

    public void DeleteVersion(string guid, string entryId)
    {
        var mod = Get(guid);
        var entry = FindVersion(mod, entryId) ?? throw new InvalidOperationException($"No stored version '{entryId}' of {mod.Name}.");
        if (entry.Id == mod.ActiveEntryId) throw new InvalidOperationException("That version is the one installed. Switch or disable it first.");
        Library.Delete(entry);
    }

    public LibraryEntry ImportAndEnable(string dllPath, string source)
    {
        EnsureGameClosed();
        var entry = Library.ImportFile(dllPath, source);
        Enable(entry.Guid, entry.Id);
        return entry;
    }

    public Profile CaptureProfile(string name)
    {
        SyncLibrary();
        var mods = Overview()
            .Where(m => m.ActiveEntry is not null)
            .Select(m => new ProfileMod(m.Guid, m.ActiveEntry!.Id))
            .ToList();
        var profile = new Profile(name.Trim(), mods);
        Profiles.Save(profile);
        return profile;
    }

    public IReadOnlyList<string> ApplyProfile(Profile profile)
    {
        EnsureGameClosed();
        var changes = new List<string>();
        var wanted = profile.Mods.ToDictionary(m => m.Guid, StringComparer.OrdinalIgnoreCase);

        foreach (var mod in Overview())
        {
            if (wanted.TryGetValue(mod.Guid, out var target))
            {
                if (mod.ActiveEntryId == target.EntryId) continue;
                Enable(mod.Guid, target.EntryId);
                changes.Add($"enabled {mod.Name} {target.EntryId}");
            }
            else if (mod.Enabled)
            {
                Disable(mod.Guid);
                changes.Add($"disabled {mod.Name}");
            }
        }

        foreach (var missing in wanted.Keys.Except(Overview().Select(m => m.Guid), StringComparer.OrdinalIgnoreCase))
            changes.Add($"skipped {missing}: not in the library");

        return changes;
    }

    public IReadOnlyList<ModOverview> Match(string query)
    {
        var mods = Overview();
        bool Is(string? value) => string.Equals(value, query, StringComparison.OrdinalIgnoreCase);
        var exact = mods.Where(m => Is(m.Guid) || Is(m.Name) || Is(m.ShortName)).ToList();
        if (exact.Count > 0) return exact;

        var squashed = query.Replace(" ", "");
        return mods.Where(m =>
                m.Name.Replace(" ", "").Contains(squashed, StringComparison.OrdinalIgnoreCase) ||
                m.Guid.Contains(squashed, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public ModOverview Get(string guidOrQuery)
    {
        var matches = Match(guidOrQuery);
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException($"No mod matches '{guidOrQuery}'."),
            _ => throw new InvalidOperationException($"'{guidOrQuery}' matches {string.Join(", ", matches.Select(m => m.Name))}. Be more specific."),
        };
    }

    public void EnsureGameClosed()
    {
        if (_isGameRunning()) throw new GameRunningException();
    }

    private static LibraryEntry? FindVersion(ModOverview mod, string entryId) =>
        mod.Versions.FirstOrDefault(e => string.Equals(e.Id, entryId, StringComparison.OrdinalIgnoreCase))
        ?? mod.Versions.FirstOrDefault(e => e.Id.StartsWith(entryId, StringComparison.OrdinalIgnoreCase))
        ?? mod.Versions.FirstOrDefault(e => e.Sha256.StartsWith(entryId, StringComparison.OrdinalIgnoreCase));

    private LibraryEntry Store(InstalledPlugin plugin) =>
        Library.Import(plugin.Info, Install.PluginsPath, plugin.RelativeFiles, plugin.RelativePath, "plugins");

    private void Remove(InstalledPlugin plugin)
    {
        foreach (var file in plugin.RelativeFiles)
        {
            var path = Path.Combine(Install.PluginsPath, file);
            if (File.Exists(path)) File.Delete(path);
        }

        foreach (var directory in plugin.RelativeFiles
                     .Select(f => Path.GetDirectoryName(Path.Combine(Install.PluginsPath, f))!)
                     .Distinct()
                     .OrderByDescending(d => d.Length))
        {
            var current = directory;
            while (!PathsEqual(current, Install.PluginsPath) && Directory.Exists(current) && !Directory.EnumerateFileSystemEntries(current).Any())
            {
                Directory.Delete(current);
                current = Path.GetDirectoryName(current)!;
            }
        }
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
}
