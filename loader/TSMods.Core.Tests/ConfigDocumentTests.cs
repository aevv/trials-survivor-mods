using TSMods.Core.Config;
using TSMods.Core.Game;

namespace TSMods.Core.Tests;

public class ConfigDocumentTests
{
    private const string Sample = """
        ## Settings file was created by plugin Trials Survivors: Elite Health Bars v0.1.0
        ## Plugin GUID: net.aevv.trialssurvivors.elitehealthbars

        [Arrows]

        ## Arrow size at 1080p.
        # Setting type: Single
        # Default value: 28
        # Acceptable value range: From 8 to 128
        ArrowSize = 56

        ## Arrow colour, as #RRGGBB or #RRGGBBAA.
        # Setting type: String
        # Default value: #F2A33AE6
        ArrowColour = #F2A33AE6

        [General]

        ## Key that opens and closes the panel.
        ## Second line.
        # Setting type: Key
        # Default value: F6
        # Acceptable values: None, F5, F6, F7
        ToggleKey = F6

        ## Show the bar.
        # Setting type: Boolean
        # Default value: true
        Enabled = true

        ## Levels.
        # Setting type: LogLevel
        # Default value: Info
        # Acceptable values: None, Error, Warning, Info
        # Multiple values can be set at the same time by separating them with , (e.g. Debug, Warning)
        Levels = Info

        ## Count.
        # Setting type: Int32
        # Default value: 6
        # Acceptable value range: From 1 to 20
        MaxIcons = 6

        """;

    private static ConfigDocument Load() => ConfigDocument.Parse("test.cfg", Sample);

    [Fact]
    public void Parses_header_and_settings_with_their_metadata()
    {
        var doc = Load();

        Assert.Equal("Trials Survivors: Elite Health Bars", doc.PluginName);
        Assert.Equal("0.1.0", doc.PluginVersion);
        Assert.Equal("net.aevv.trialssurvivors.elitehealthbars", doc.Guid);
        Assert.Equal(["Arrows.ArrowSize", "Arrows.ArrowColour", "General.ToggleKey", "General.Enabled", "General.Levels", "General.MaxIcons"], doc.Settings.Select(s => s.Id));

        var size = doc.Find("ArrowSize")!;
        Assert.Equal(SettingKind.Decimal, size.Kind);
        Assert.Equal(("8", "128"), (size.RangeMin, size.RangeMax));
        Assert.False(size.IsDefault);

        Assert.Equal(SettingKind.Colour, doc.Find("ArrowColour")!.Kind);
        Assert.Equal(SettingKind.Choice, doc.Find("ToggleKey")!.Kind);
        Assert.Equal("Key that opens and closes the panel.\nSecond line.", doc.Find("ToggleKey")!.Description);
        Assert.Equal(SettingKind.Bool, doc.Find("General.Enabled")!.Kind);
        Assert.Equal(SettingKind.Flags, doc.Find("Levels")!.Kind);
        Assert.Equal(SettingKind.Integer, doc.Find("MaxIcons")!.Kind);
    }

    [Theory]
    [InlineData("ArrowSize", "200", "must be between 8 and 128")]
    [InlineData("ArrowSize", "abc", "expected a number")]
    [InlineData("MaxIcons", "2.5", "expected a whole number")]
    [InlineData("ToggleKey", "F12", "expected one of: None, F5, F6, F7")]
    [InlineData("ArrowColour", "red", "expected #RRGGBB or #RRGGBBAA")]
    [InlineData("Enabled", "yes", "expected true or false")]
    public void Invalid_values_are_rejected(string key, string value, string error)
    {
        var doc = Load();
        Assert.Equal(error, doc.Set(doc.Find(key)!, value));
        Assert.False(doc.IsDirty);
    }

    [Fact]
    public void Setting_a_value_only_rewrites_that_line_and_normalises_it()
    {
        var doc = Load();
        Assert.Null(doc.Set(doc.Find("ToggleKey")!, "f7"));
        Assert.Null(doc.Set(doc.Find("Enabled")!, "FALSE"));
        Assert.Null(doc.Set(doc.Find("Levels")!, "error,warning"));
        Assert.Null(doc.Set(doc.Find("ArrowColour")!, "#ff0000"));

        var expected = Sample.ReplaceLineEndings()
            .Replace("ToggleKey = F6", "ToggleKey = F7")
            .Replace("Enabled = true", "Enabled = false")
            .Replace("Levels = Info", "Levels = Error, Warning")
            .Replace("ArrowColour = #F2A33AE6", "ArrowColour = #FF0000");
        Assert.Equal(expected, doc.Render());
        Assert.True(doc.IsDirty);
    }

    [Fact]
    public void Default_detection_normalises_numbers()
    {
        var doc = ConfigDocument.Parse("x.cfg", "[A]\n# Setting type: Boolean\n# Default value: true\nOn = True\n");
        Assert.True(doc.Settings[0].IsDefault);
    }
}

public class GameFilesTests
{
    [Fact]
    public void Reads_build_id_and_library_paths()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir["app.acf"], "\"AppState\"\n{\n\t\"buildid\"\t\t\"25735493\"\n\t\"installdir\"\t\t\"Trials Survivors\"\n}");
        File.WriteAllText(dir["libraryfolders.vdf"], "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"C:\\\\Program Files (x86)\\\\Steam\"\n\t}\n\t\"1\"\n\t{\n\t\t\"path\"\t\t\"D:\\\\SteamLibrary\"\n\t}\n}");

        Assert.Equal("25735493", SteamFiles.ReadBuildId(dir["app.acf"]));
        Assert.Equal("Trials Survivors", SteamFiles.ReadInstallDir(dir["app.acf"]));
        Assert.Equal([@"C:\Program Files (x86)\Steam", @"D:\SteamLibrary"], SteamFiles.ReadLibraryPaths(dir["libraryfolders.vdf"]));
    }

    [Fact]
    public void Doorstop_toggle_only_touches_the_general_enabled_line()
    {
        using var dir = new TempDir();
        var install = Fakes.Game(dir);
        const string ini = "[General]\nenabled = true\ntarget_assembly = x.dll\n[UnityMono]\nenabled = true\n";
        File.WriteAllText(install.DoorstopConfigPath, ini);

        Doorstop.SetEnabled(install, false);

        Assert.False(Doorstop.IsEnabled(install));
        Assert.Equal(ini.Replace("[General]\nenabled = true", "[General]\nenabled = false").ReplaceLineEndings(), File.ReadAllText(install.DoorstopConfigPath));
        Doorstop.SetEnabled(install, true);
        Assert.True(Doorstop.IsEnabled(install));
    }
}
