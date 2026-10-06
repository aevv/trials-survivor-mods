using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TSMods.Core;
using TSMods.Core.Compatibility;
using TSMods.Core.Game;
using TSMods.Core.Mods;
using TSMods.Core.Releases;
using TSMods.Loader.Views;

namespace TSMods.Loader.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly LoaderContext _context;
    private readonly IDialogs _dialogs;
    private readonly DispatcherTimer _gameWatcher;
    private bool _syncingToggles;

    public MainViewModel(LoaderContext context, IDialogs dialogs)
    {
        _context = context;
        _dialogs = dialogs;
        _gameWatcher = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => _ = WatchGameAsync());
    }

    public ObservableCollection<ModItemViewModel> Mods { get; } = [];
    public ObservableCollection<string> Profiles { get; } = [];
    public ObservableCollection<RemoteModViewModel> RemoteMods { get; } = [];

    [ObservableProperty] public partial ModItemViewModel? SelectedMod { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanChange), nameof(ShowInstallBepInEx), nameof(ShowModsOff))] public partial bool GameFound { get; set; }
    [ObservableProperty] public partial string GameDetails { get; set; } = "";
    [ObservableProperty] public partial string GamePath { get; set; } = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanChange))] public partial bool IsGameRunning { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowInstallBepInEx))] public partial bool BepInExInstalled { get; set; } = true;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(BepInExToggleText), nameof(ShowModsOff))] public partial bool ModsEnabled { get; set; } = true;
    [ObservableProperty] public partial bool InteropStale { get; set; }
    [ObservableProperty] public partial bool InteropMissing { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanChange))] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial string StatusMessage { get; set; } = "";
    [ObservableProperty] public partial bool StatusIsError { get; set; }
    [ObservableProperty] public partial string? SelectedProfile { get; set; }
    [ObservableProperty] public partial string NewProfileName { get; set; } = "";
    [ObservableProperty] public partial bool ShowingReleases { get; set; }
    [ObservableProperty] public partial bool ReleasesLoading { get; set; }
    [ObservableProperty] public partial string ReleasesMessage { get; set; } = "";

    public bool CanChange => GameFound && !IsGameRunning && !IsBusy;
    public string ReleaseRepo => _context.Settings.ReleaseRepo;
    public bool HasMods => Mods.Count > 0;
    public bool ShowInstallBepInEx => GameFound && !BepInExInstalled;
    public bool ShowModsOff => GameFound && BepInExInstalled && !ModsEnabled;
    public string BepInExToggleText => ModsEnabled ? "Turn mods off" : "Turn mods on";

    public async Task InitialiseAsync()
    {
        await RefreshAsync();
        _gameWatcher.Start();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        var manager = _context.Manager;
        GameFound = manager is not null;
        if (manager is null)
        {
            Mods.Clear();
            OnPropertyChanged(nameof(HasMods));
            SetStatus("Couldn't find Trials Survivors. Use Locate game to point at its folder.", error: true);
            return;
        }

        var selected = SelectedMod?.Guid;
        try
        {
            var snapshot = await Task.Run(() => Snapshot.Take(manager));
            ApplyGameState(snapshot.State);

            Mods.Clear();
            foreach (var (mod, health) in snapshot.Mods) Mods.Add(new ModItemViewModel(this, mod, health, manager.Install));
            OnPropertyChanged(nameof(HasMods));
            SelectedMod = Mods.FirstOrDefault(m => m.Guid == selected) ?? Mods.FirstOrDefault();

            Profiles.Clear();
            foreach (var profile in manager.Profiles.All()) Profiles.Add(profile.Name);
            if (SelectedProfile is not null && !Profiles.Contains(SelectedProfile)) SelectedProfile = null;
        }
        catch (Exception e)
        {
            SetStatus($"Refresh failed: {e.Message}", error: true);
        }
    }

    internal async Task SetModEnabledAsync(ModItemViewModel mod, bool enabled)
    {
        if (_syncingToggles) return;
        await RunAsync(() =>
        {
            var entry = enabled ? Manager.Enable(mod.Guid) : Manager.Disable(mod.Guid);
            return $"{(enabled ? "Enabled" : "Disabled")} {mod.DisplayName} {entry.Id}";
        });
    }

    internal Task UseVersionAsync(ModItemViewModel mod, VersionViewModel version) =>
        RunAsync(() =>
        {
            var entry = Manager.Enable(mod.Guid, version.Id);
            return $"{mod.DisplayName} is now {entry.Id}";
        });

    internal async Task DeleteVersionAsync(ModItemViewModel mod, VersionViewModel version)
    {
        if (!await _dialogs.ConfirmAsync("Delete stored version", $"Remove {mod.DisplayName} {version.Id} from the library? This can't be undone.")) return;
        await RunAsync(() =>
        {
            Manager.DeleteVersion(mod.Guid, version.Id);
            return $"Deleted {mod.DisplayName} {version.Id}";
        });
    }

    [RelayCommand]
    private async Task ImportModAsync()
    {
        var file = await _dialogs.PickModFileAsync();
        if (file is null) return;
        await RunAsync(() =>
        {
            var info = ModInspector.Inspect(file);
            if (!info.IsPlugin) throw new InvalidOperationException($"{Path.GetFileName(file)} isn't a BepInEx plugin.");
            var entry = Manager.ImportAndEnable(file, "file");
            return $"Imported and enabled {entry.Name} {entry.Id}";
        });
    }

    [RelayCommand]
    private async Task LocateGameAsync()
    {
        var folder = await _dialogs.PickFolderAsync("Select the Trials Survivors folder");
        if (folder is null) return;
        if (!_context.TrySetGamePath(folder))
        {
            SetStatus($"'{folder}' doesn't contain Trials Survivors.exe and GameAssembly.dll.", error: true);
            return;
        }
        await RefreshAsync();
        SetStatus($"Using {folder}");
    }

    [RelayCommand]
    private void LaunchGame()
    {
        GameProcess.LaunchViaSteam();
        SetStatus("Launching through Steam");
    }

    [RelayCommand]
    private void OpenFolder(string which)
    {
        var path = which switch
        {
            "plugins" => _context.Install?.PluginsPath,
            "config" => _context.Install?.ConfigPath,
            "library" => _context.Paths.Root,
            "log" => _context.Install?.BepInExPath,
            _ => _context.Install?.RootPath,
        };
        if (path is null) return;
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    [RelayCommand]
    private Task ToggleBepInExAsync() =>
        RunAsync(() =>
        {
            Manager.EnsureGameClosed();
            var enable = !ModsEnabled;
            Doorstop.SetEnabled(Manager.Install, enable);
            return enable ? "BepInEx on: mods will load" : "BepInEx off: the game will start vanilla";
        });

    [RelayCommand]
    private async Task InstallBepInExAsync()
    {
        if (!await _dialogs.ConfirmAsync("Install BepInEx", $"Download BepInEx {BepInExInstaller.PinnedVersion} (IL2CPP) and extract it into the game folder?")) return;
        await RunAsync(async () =>
        {
            await BepInExInstaller.InstallAsync(Manager.Install, _context.Paths, _context.Http, progress: new Progress<string>(m => SetStatus(m)));
            return "BepInEx installed. Launch the game once so it can generate interop assemblies.";
        });
    }

    [RelayCommand]
    private Task SaveProfileAsync() =>
        RunAsync(() =>
        {
            var name = string.IsNullOrWhiteSpace(NewProfileName) ? SelectedProfile : NewProfileName.Trim();
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Type a profile name first.");
            var profile = Manager.CaptureProfile(name);
            NewProfileName = "";
            SelectedProfile = profile.Name;
            return $"Saved profile '{profile.Name}' with {profile.Mods.Count} mod(s)";
        });

    [RelayCommand]
    private Task ApplyProfileAsync() =>
        RunAsync(() =>
        {
            var profile = SelectedProfile is null ? null : Manager.Profiles.Find(SelectedProfile);
            if (profile is null) throw new InvalidOperationException("Pick a profile first.");
            var changes = Manager.ApplyProfile(profile);
            return changes.Count == 0 ? $"Already matches '{profile.Name}'" : $"Applied '{profile.Name}': {string.Join("; ", changes)}";
        });

    [RelayCommand]
    private async Task DeleteProfileAsync()
    {
        if (SelectedProfile is not { } name) return;
        if (!await _dialogs.ConfirmAsync("Delete profile", $"Delete the profile '{name}'? Mods and versions stay as they are.")) return;
        await RunAsync(() => Manager.Profiles.Delete(name) ? $"Deleted profile '{name}'" : $"No profile '{name}'");
    }

    [RelayCommand]
    private async Task ShowReleasesAsync()
    {
        ShowingReleases = true;
        ReleasesLoading = true;
        ReleasesMessage = "";
        RemoteMods.Clear();
        try
        {
            var releases = await new GitHubReleases(_context.Http, ReleaseRepo).ListAsync();
            var installed = Mods.ToDictionary(m => m.ShortName, StringComparer.OrdinalIgnoreCase);
            foreach (var release in releases)
            {
                installed.TryGetValue(release.ModName, out var local);
                RemoteMods.Add(new RemoteModViewModel(this, release, local));
            }
            ReleasesMessage = releases.Count == 0 ? $"Nothing published on {ReleaseRepo} yet." : "";
        }
        catch (Exception e)
        {
            ReleasesMessage = $"Couldn't load releases: {e.Message}";
        }
        finally
        {
            ReleasesLoading = false;
        }
    }

    [RelayCommand]
    private void HideReleases() => ShowingReleases = false;

    internal Task InstallReleaseAsync(RemoteModViewModel remote) =>
        RunAsync(async () =>
        {
            Manager.EnsureGameClosed();
            var file = await new GitHubReleases(_context.Http, ReleaseRepo).DownloadAsync(remote.Release, _context.Paths.Downloads);
            var entry = Manager.ImportAndEnable(file, $"github:{remote.Release.Tag}");
            ShowingReleases = false;
            return $"Installed {entry.Name} {entry.Id}";
        });

    internal void ConfigSaved(ModItemViewModel mod, int changes) =>
        SetStatus($"Saved {changes} setting(s) for {mod.DisplayName}");

    internal void SetStatus(string message, bool error = false)
    {
        StatusMessage = message;
        StatusIsError = error;
    }

    private ModManager Manager => _context.Manager ?? throw new InvalidOperationException("Game not found.");

    private Task RunAsync(Func<string> action) => RunAsync(() => Task.FromResult(action()));

    private async Task RunAsync(Func<Task<string>> action)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var message = await action();
            await RefreshAsync();
            SetStatus(message);
        }
        catch (Exception e)
        {
            await RefreshAsync();
            SetStatus(e.Message, error: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyGameState(GameState state)
    {
        GamePath = state.Install.RootPath;
        IsGameRunning = state.IsRunning;
        BepInExInstalled = state.BepInExInstalled;
        ModsEnabled = state.ModsEnabled != false;
        InteropStale = state.Interop == InteropStatus.Stale;
        InteropMissing = state.BepInExInstalled && state.Interop == InteropStatus.Missing;
        var parts = new List<string> { state.SteamBuildId is null ? "build unknown" : $"build {state.SteamBuildId}" };
        parts.Add(state.BepInExInstalled ? $"BepInEx {state.BepInExVersion}" : "BepInEx not installed");
        GameDetails = string.Join("  ·  ", parts);
    }

    private async Task WatchGameAsync()
    {
        if (_context.Install is null) return;
        var running = await Task.Run(GameProcess.IsRunning);
        if (running == IsGameRunning) return;

        IsGameRunning = running;
        if (running) SetStatus("Game is running. Mods and settings are locked until it closes.");
        else
        {
            await RefreshAsync();
            SetStatus("Game closed. Refreshed mods and settings.");
        }
    }

    internal void SyncToggle(Action apply)
    {
        _syncingToggles = true;
        try { apply(); }
        finally { _syncingToggles = false; }
    }

    private sealed record Snapshot(GameState State, IReadOnlyList<(ModOverview Mod, ModHealth? Health)> Mods)
    {
        public static Snapshot Take(ModManager manager)
        {
            manager.SyncLibrary();
            var state = GameState.Read(manager.Install);
            using var checker = CompatibilityChecker.For(manager.Install);
            var mods = manager.Overview().Select(m => (m, ModHealthCheck.Evaluate(m, state, checker))).ToList();
            return new Snapshot(state, mods);
        }
    }
}
