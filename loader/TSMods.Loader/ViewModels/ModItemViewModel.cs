using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TSMods.Core;
using TSMods.Core.Compatibility;
using TSMods.Core.Game;
using TSMods.Core.Releases;

namespace TSMods.Loader.ViewModels;

public sealed partial class ModItemViewModel : ObservableObject
{
    private const string NamePrefix = "Trials Survivors: ";
    private readonly MainViewModel _owner;
    private readonly GameInstall _install;
    private ConfigEditorViewModel? _config;

    public ModItemViewModel(MainViewModel owner, ModOverview mod, ModHealth? health, GameInstall install)
    {
        _owner = owner;
        _install = install;
        Guid = mod.Guid;
        Name = mod.Name;
        ShortName = mod.ShortName;
        Version = mod.Version ?? "?";
        EntryId = mod.ActiveEntryId ?? mod.Versions.FirstOrDefault()?.Id;
        FilePath = mod.Active?.Info.FilePath ?? mod.Versions.FirstOrDefault()?.MainFilePath ?? "";
        MissingDependencies = mod.MissingDependencies.Count == 0 ? null : $"Needs {string.Join(", ", mod.MissingDependencies)}";

        Level = health?.Level;
        HealthSummary = health?.Summary ?? (mod.Enabled ? "not checked" : "disabled");
        HealthDetails = health?.Details().ToList() ?? [];
        BuiltFor = health?.BuiltForBuildId is { } build ? $"game build {build}" : "no build stamp";

        Versions = new ObservableCollection<VersionViewModel>(mod.Versions.Select(v => new VersionViewModel(this, v, v.Id == mod.ActiveEntryId)));
        owner.SyncToggle(() => IsEnabled = mod.Enabled);
    }

    public MainViewModel Owner => _owner;
    public string Guid { get; }
    public string Name { get; }
    public string ShortName { get; }
    public string DisplayName => Name.StartsWith(NamePrefix, StringComparison.Ordinal) ? Name[NamePrefix.Length..] : Name;
    public string Version { get; }
    public string VersionLabel => $"v{Version}";
    public string? EntryId { get; }
    public string FilePath { get; }
    public string? MissingDependencies { get; }
    public string BuiltFor { get; }
    public HealthLevel? Level { get; }
    public string HealthSummary { get; }
    public IReadOnlyList<string> HealthDetails { get; }
    public ObservableCollection<VersionViewModel> Versions { get; }
    public string ReleaseTag => GitHubReleases.TagFor(ShortName, Version);

    public bool IsOk => Level == HealthLevel.Ok;
    public bool IsWarn => Level == HealthLevel.Warning;
    public bool IsError => Level == HealthLevel.Error;
    public string HealthText => Level switch
    {
        HealthLevel.Ok => "OK",
        HealthLevel.Warning => "CHECK",
        HealthLevel.Error => "BROKEN",
        HealthLevel.Unknown => "?",
        _ => "-",
    };

    public ConfigEditorViewModel Config => _config ??= new ConfigEditorViewModel(this, _install.ConfigFileFor(Guid));

    [ObservableProperty] public partial bool IsEnabled { get; set; }

    partial void OnIsEnabledChanged(bool value) => _ = _owner.SetModEnabledAsync(this, value);

    [RelayCommand]
    private void ReloadConfig()
    {
        _config = null;
        OnPropertyChanged(nameof(Config));
    }
}

public sealed partial class VersionViewModel(ModItemViewModel mod, TSMods.Core.Library.LibraryEntry entry, bool isActive) : ObservableObject
{
    public MainViewModel Owner => mod.Owner;
    public string Id => entry.Id;
    public string Version => entry.Version;
    public string ShortHash => entry.Sha256[..8];
    public string Imported => entry.ImportedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
    public string Source => entry.Source;
    public bool IsActive => isActive;
    public bool IsInactive => !isActive;

    [RelayCommand]
    private Task UseAsync() => mod.Owner.UseVersionAsync(mod, this);

    [RelayCommand]
    private Task DeleteAsync() => mod.Owner.DeleteVersionAsync(mod, this);
}

public sealed partial class RemoteModViewModel(MainViewModel owner, RemoteMod release, ModItemViewModel? local) : ObservableObject
{
    private readonly VersionViewModel? _stored = local?.Versions.FirstOrDefault(v => ModVersions.Same(v.Version, release.Version));

    public RemoteMod Release => release;
    public string ModName => release.ModName;
    public string Version => $"v{release.Version}";
    public string Published => release.PublishedAt.LocalDateTime.ToString("yyyy-MM-dd");
    public string Notes => release.Notes.Trim();
    public bool HasNotes => Notes.Length > 0;
    public bool IsInstalled => _stored is { IsActive: true } && local!.IsEnabled;
    public bool CanInstall => !IsInstalled;
    public string InstallText => _stored is null ? "Install" : "Use";
    public string LocalState => (local, _stored) switch
    {
        (null, _) => "not installed",
        (_, { IsActive: true }) when local.IsEnabled => "installed",
        (_, not null) => $"in your library, you're using v{local.Version}{(local.IsEnabled ? "" : " (disabled)")}",
        _ => $"you have v{local.Version}{(local.IsEnabled ? "" : " (disabled)")}",
    };

    [RelayCommand]
    private Task InstallAsync() => _stored is { } stored ? owner.UseStoredReleaseAsync(local!, stored) : owner.InstallReleaseAsync(this);
}

public static class ModVersions
{
    public static bool Same(string a, string b) =>
        System.Version.TryParse(a, out var x) && System.Version.TryParse(b, out var y)
            ? Normalise(x) == Normalise(y)
            : string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static System.Version Normalise(System.Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
}
