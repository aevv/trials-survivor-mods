using TSMods.Core.Library;

namespace TSMods.Core.Tests;

public class ModManagerTests
{
    private static (ModManager Manager, Func<bool> Running, Action<bool> SetRunning) Create(TempDir dir)
    {
        var running = false;
        var install = Fakes.Game(dir);
        var manager = new ModManager(install, new ModLibrary(dir["home/library"]), new ProfileStore(dir["home/profiles"]), () => running);
        return (manager, () => running, value => running = value);
    }

    private static string Deploy(ModManager manager, FakeMod mod, string? subfolder = null)
    {
        var folder = subfolder is null ? manager.Install.PluginsPath : Path.Combine(manager.Install.PluginsPath, subfolder);
        return Fakes.WriteMod(Path.Combine(folder, mod.AssemblyName + ".dll"), mod);
    }

    [Fact]
    public void Disable_keeps_the_mod_in_the_library_and_enable_restores_it()
    {
        using var dir = new TempDir();
        var (manager, _, _) = Create(dir);
        var file = Deploy(manager, new FakeMod());

        var stored = manager.Disable("test.mod");
        Assert.False(File.Exists(file));
        Assert.False(manager.Get("test.mod").Enabled);

        manager.Enable("test.mod");
        Assert.True(File.Exists(file));
        Assert.Equal(stored.Id, manager.Get("Test Mod").ActiveEntryId);
    }

    [Fact]
    public void Switching_versions_replaces_the_installed_file()
    {
        using var dir = new TempDir();
        var (manager, _, _) = Create(dir);
        Deploy(manager, new FakeMod(Version: "1.0.0"));
        var v1 = manager.SyncLibrary().Single();
        Deploy(manager, new FakeMod(Version: "1.1.0"));
        manager.SyncLibrary();

        var mod = manager.Get("TestMod");
        Assert.Equal(["1.1.0", "1.0.0"], mod.Versions.Select(v => v.Version));
        Assert.Equal("1.1.0", mod.Version);

        manager.Enable(mod.Guid, v1.Id);
        Assert.Equal("1.0.0", manager.Get("TestMod").Version);
        Assert.Single(manager.ScanPlugins());
    }

    [Fact]
    public void Same_version_with_different_bytes_gets_its_own_entry()
    {
        using var dir = new TempDir();
        var (manager, _, _) = Create(dir);
        Deploy(manager, new FakeMod(Salt: "a"));
        manager.SyncLibrary();
        Deploy(manager, new FakeMod(Salt: "b"));
        manager.SyncLibrary();

        Assert.Equal(2, manager.Get("test.mod").Versions.Count);
    }

    [Fact]
    public void Plugin_in_its_own_folder_moves_with_its_files()
    {
        using var dir = new TempDir();
        var (manager, _, _) = Create(dir);
        Deploy(manager, new FakeMod(), "Author-TestMod");
        File.WriteAllText(Path.Combine(manager.Install.PluginsPath, "Author-TestMod", "data.json"), "{}");

        manager.Disable("test.mod");
        Assert.False(Directory.Exists(Path.Combine(manager.Install.PluginsPath, "Author-TestMod")));

        manager.Enable("test.mod");
        Assert.True(File.Exists(Path.Combine(manager.Install.PluginsPath, "Author-TestMod", "data.json")));
    }

    [Fact]
    public void Changes_are_refused_while_the_game_runs()
    {
        using var dir = new TempDir();
        var (manager, _, setRunning) = Create(dir);
        var file = Deploy(manager, new FakeMod());
        setRunning(true);

        Assert.Throws<GameRunningException>(() => manager.Disable("test.mod"));
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void Profiles_capture_and_restore_the_installed_set()
    {
        using var dir = new TempDir();
        var (manager, _, _) = Create(dir);
        Deploy(manager, new FakeMod(Guid: "a", Name: "A", AssemblyName: "Mod.A"));
        Deploy(manager, new FakeMod(Guid: "b", Name: "B", AssemblyName: "Mod.B"));
        manager.CaptureProfile("both");

        manager.Disable("a");
        manager.CaptureProfile("just b");

        var changes = manager.ApplyProfile(manager.Profiles.Find("both")!);
        Assert.Equal(["enabled A " + manager.Get("a").ActiveEntryId], changes);

        manager.ApplyProfile(manager.Profiles.Find("just b")!);
        Assert.False(manager.Get("a").Enabled);
        Assert.True(manager.Get("b").Enabled);
    }

    [Fact]
    public void Ambiguous_queries_are_rejected()
    {
        using var dir = new TempDir();
        var (manager, _, _) = Create(dir);
        Deploy(manager, new FakeMod(Guid: "x.one", Name: "Thing One", AssemblyName: "Mod.One"));
        Deploy(manager, new FakeMod(Guid: "x.two", Name: "Thing Two", AssemblyName: "Mod.Two"));

        Assert.Throws<InvalidOperationException>(() => manager.Get("thing"));
        Assert.Equal("x.two", manager.Get("two").Guid);
    }
}
