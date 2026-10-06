using TSMods.Core.Compatibility;
using TSMods.Core.Game;
using TSMods.Core.Mods;

namespace TSMods.Core.Tests;

public class CompatibilityTests
{
    [Fact]
    public void Mod_whose_references_all_exist_is_compatible()
    {
        using var dir = new TempDir();
        var install = Fakes.Game(dir, "100", "Activate", "Die");
        var mod = Fakes.WriteMod(dir["mod.dll"], new FakeMod(Calls: ["Activate", "Die"], Patches: [("Enemy", "Activate")]));

        using var checker = CompatibilityChecker.For(install);
        var report = checker.Check(mod);

        Assert.Equal(CompatibilityStatus.Compatible, report.Status);
        Assert.Empty(report.Issues);
        Assert.Equal(1, report.PatchTargetsChecked);
        Assert.True(report.ReferencesChecked >= 3);
    }

    [Fact]
    public void Missing_method_type_and_patch_target_are_reported()
    {
        using var dir = new TempDir();
        var install = Fakes.Game(dir, "100", "Activate");
        var mod = Fakes.WriteMod(dir["mod.dll"], new FakeMod(
            Calls: ["Activate", "Gone"],
            MissingTypes: ["Boss"],
            Patches: [("Enemy", "Activate"), ("Enemy", "Vanished")]));

        using var checker = CompatibilityChecker.For(install);
        var report = checker.Check(mod);

        Assert.Equal(CompatibilityStatus.Broken, report.Status);
        Assert.Contains(report.Issues, i => i.Kind == IssueKind.MissingMember && i.Description.Contains("Game.Enemy.Gone"));
        Assert.Contains(report.Issues, i => i.Kind == IssueKind.MissingType && i.Description == "Game.Boss");
        Assert.Contains(report.Issues, i => i.Kind == IssueKind.MissingPatchTarget && i.Description.Contains("Game.Enemy.Vanished"));
        Assert.DoesNotContain(report.Issues, i => i.Description.Contains("Game.Enemy.Activate"));
    }

    [Fact]
    public void Inspector_reads_plugin_and_stamp()
    {
        using var dir = new TempDir();
        var mod = ModInspector.Inspect(Fakes.WriteMod(dir["mod.dll"], new FakeMod(Guid: "a.b", Name: "AB", Version: "2.1.0", GameBuildId: "555")));

        Assert.Equal(new PluginInfo("a.b", "AB", "2.1.0"), mod.Plugin);
        Assert.Equal("555", mod.Stamp?.GameBuildId);
        Assert.Equal("6.0.0-be.788", mod.Stamp?.BepInExVersion);
    }

    [Fact]
    public void Non_dotnet_file_is_not_a_plugin()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir["native.dll"], "not a PE file");

        Assert.False(ModInspector.Inspect(dir["native.dll"]).IsPlugin);
    }

    [Theory]
    [InlineData("100", BuildMatch.Match, HealthLevel.Ok)]
    [InlineData("99", BuildMatch.Different, HealthLevel.Warning)]
    public void Health_compares_stamp_with_installed_build(string builtFor, BuildMatch expectedMatch, HealthLevel expectedLevel)
    {
        using var dir = new TempDir();
        var install = Fakes.Game(dir, "100");
        var mod = ModInspector.Inspect(Fakes.WriteMod(dir["mod.dll"], new FakeMod(Calls: ["Activate"], GameBuildId: builtFor)));
        var state = GameState.Read(install) with { BepInExVersion = "6.0.0-be.788", IsRunning = false };

        using var checker = CompatibilityChecker.For(install);
        var health = ModHealthCheck.Evaluate(mod, state, checker);

        Assert.Equal(expectedMatch, health.Build);
        Assert.Equal(expectedLevel, health.Level);
    }

    [Fact]
    public void Stale_interop_is_unknown_rather_than_broken()
    {
        using var dir = new TempDir();
        var install = Fakes.Game(dir, "100");
        File.SetLastWriteTimeUtc(install.GameAssemblyPath, DateTime.UtcNow.AddHours(1));
        var mod = ModInspector.Inspect(Fakes.WriteMod(dir["mod.dll"], new FakeMod(Calls: ["Gone"])));

        using var checker = CompatibilityChecker.For(install);
        var health = ModHealthCheck.Evaluate(mod, GameState.Read(install), checker);

        Assert.Equal(InteropStatus.Stale, GameState.ReadInteropStatus(install));
        Assert.Equal(HealthLevel.Unknown, health.Level);
    }
}
