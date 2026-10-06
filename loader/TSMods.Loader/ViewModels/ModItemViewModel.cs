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
    public RemoteMod Release => release;
    public string ModName => release.ModName;
    public string Version => $"v{release.Version}";
    public string Published => release.PublishedAt.LocalDateTime.ToString("yyyy-MM-dd");
    public string Notes => release.Notes.Trim();
    public bool HasNotes => Notes.Length > 0;
    public string LocalState => local is null
        ? "not installed"
        : local.IsEnabled && local.Version == release.Version ? "installed" : $"you have v{local.Version}{(local.IsEnabled ? "" : " (disabled)")}";

    [RelayCommand]
    private Task InstallAsync() => owner.InstallReleaseAsync(this);
}
