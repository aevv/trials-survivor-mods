using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Logging;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TSMods.Core;
using TSMods.Core.Tests;
using TSMods.Loader.ViewModels;
using TSMods.Loader.Views;

namespace TSMods.Loader.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHarfBuzz()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public sealed class MainWindowTests
{
    private static readonly Lazy<HeadlessUnitTestSession> Session = new(() => HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder)));

    private const string Config = """
        ## Settings file was created by plugin Test Mod v1.0.0
        ## Plugin GUID: test.mod

        [General]

        ## Show the thing.
        # Setting type: Boolean
        # Default value: true
        Enabled = false

        ## How big.
        # Setting type: Single
        # Default value: 28
        # Acceptable value range: From 8 to 128
        Size = 56

        ## Colour.
        # Setting type: String
        # Default value: #F2A33AE6
        Colour = #F2A33AE6

        ## Key.
        # Setting type: Key
        # Default value: F6
        # Acceptable values: None, F5, F6, F7
        ToggleKey = F6

        """;

    [Fact]
    public async Task Window_renders_mods_and_settings_without_binding_errors()
    {
        using var dir = new TempDir();
        var install = Fakes.Game(dir);
        Fakes.WriteMod(Path.Combine(install.PluginsPath, "TrialsSurvivors.TestMod.dll"), new FakeMod(Calls: ["Activate"], GameBuildId: "100"));
        Fakes.WriteMod(Path.Combine(install.PluginsPath, "TrialsSurvivors.Other.dll"), new FakeMod(Guid: "other.mod", Name: "Other Mod", AssemblyName: "TrialsSurvivors.Other", Calls: ["Gone"]));
        File.WriteAllText(install.ConfigFileFor("test.mod"), Config);

        var paths = new LoaderPaths(dir["home"]);
        new LoaderSettings { GamePath = install.RootPath }.Save(paths);

        var shots = Environment.GetEnvironmentVariable("TSMODS_SCREENSHOTS") is { } root ? Path.Combine(root, "fake") : Path.Combine(dir.Path, "shots");
        var errors = await Render(paths, shots, "other.mod");

        Assert.Empty(errors);
    }

    [Fact]
    public async Task Editing_settings_validates_and_saves_only_changed_lines()
    {
        using var dir = new TempDir();
        var install = Fakes.Game(dir);
        Fakes.WriteMod(Path.Combine(install.PluginsPath, "TrialsSurvivors.TestMod.dll"), new FakeMod(Calls: ["Activate"]));
        var configPath = install.ConfigFileFor("test.mod");
        File.WriteAllText(configPath, Config);
        var paths = new LoaderPaths(dir["home"]);
        new LoaderSettings { GamePath = install.RootPath }.Save(paths);

        var saved = await Session.Value.Dispatch(async () =>
        {
            var viewModel = new MainViewModel(LoaderContext.Create(paths), new NoDialogs());
            await viewModel.RefreshAsync();
            var editor = viewModel.Mods.Single().Config;
            var settings = editor.Sections.SelectMany(s => s.Settings).ToDictionary(s => s.Key);
            var size = (NumberSettingViewModel)settings["Size"];
            var key = (ChoiceSettingViewModel)settings["ToggleKey"];

            size.Text = "200";
            Assert.Equal("must be between 8 and 128", size.Error);
            editor.SaveCommand.Execute(null);
            Assert.Equal("Fix 1 invalid setting(s) first.", editor.Error);

            size.SliderValue = 64.4;
            Assert.Equal("64.4", size.Text);
            size.Text = "64";
            key.Selected = "F7";
            settings["Enabled"].ResetToDefaultCommand.Execute(null);
            Assert.True(editor.HasChanges);
            Assert.True(size.IsModified);

            editor.SaveCommand.Execute(null);
            Assert.Null(editor.Error);
            Assert.False(editor.HasChanges);
            return File.ReadAllText(configPath);
        }, CancellationToken.None);

        var expected = Config.ReplaceLineEndings()
            .Replace("Size = 56", "Size = 64")
            .Replace("ToggleKey = F6", "ToggleKey = F7")
            .Replace("Enabled = false", "Enabled = true");
        Assert.Equal(expected, saved);
    }

    [Fact(Explicit = true)]
    public async Task Capture_screenshots_of_the_real_install()
    {
        var output = Environment.GetEnvironmentVariable("TSMODS_SCREENSHOTS") ?? Path.Combine(Path.GetTempPath(), "tsmods-shots");
        using var home = new TempDir();
        var errors = await Render(new LoaderPaths(home.Path), output);
        Assert.Empty(errors);
    }

    private static async Task<List<string>> Render(LoaderPaths paths, string output, string? overviewGuid = null)
    {
        Directory.CreateDirectory(output);
        var sink = new CollectingSink();

        await Session.Value.Dispatch(async () =>
        {
            Logger.Sink = sink;
            var window = new MainWindow { Width = 1180, Height = 760 };
            var viewModel = new MainViewModel(LoaderContext.Create(paths), new NoDialogs());
            window.DataContext = viewModel;
            window.Show();
            await viewModel.RefreshAsync();

            for (var tab = 0; tab < 3; tab++)
            {
                var preferred = tab == 0 ? viewModel.Mods.FirstOrDefault(m => m.Guid == overviewGuid) : null;
                viewModel.SelectedMod = preferred ?? viewModel.Mods.FirstOrDefault(m => m.HealthDetails.Count > 0 && m.Config.HasConfig) ?? viewModel.Mods.FirstOrDefault();
                Dispatcher.UIThread.RunJobs();
                var tabs = window.GetVisualDescendants().OfType<TabControl>().FirstOrDefault();
                if (tabs is not null) tabs.SelectedIndex = tab;
                Dispatcher.UIThread.RunJobs();
                window.CaptureRenderedFrame()?.Save(Path.Combine(output, $"tab{tab}.png"), new PngBitmapEncoderOptions());
            }
            return true;
        }, new CancellationTokenSource(TimeSpan.FromSeconds(60)).Token);

        return sink.Errors;
    }

    private sealed class NoDialogs : IDialogs
    {
        public Task<string?> PickModFileAsync() => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(false);
    }

    private sealed class CollectingSink : ILogSink
    {
        public List<string> Errors { get; } = [];

        public bool IsEnabled(LogEventLevel level, string area) => level >= LogEventLevel.Warning;

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate) =>
            Errors.Add($"{level} {area}: {messageTemplate}");

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues) =>
            Errors.Add($"{level} {area}: {messageTemplate} [{string.Join(", ", propertyValues)}]");
    }
}
