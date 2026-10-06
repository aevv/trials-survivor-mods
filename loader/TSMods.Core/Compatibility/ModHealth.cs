using TSMods.Core.Game;
using TSMods.Core.Mods;

namespace TSMods.Core.Compatibility;

public enum BuildMatch
{
    Unstamped,
    Match,
    Different,
}

public enum HealthLevel
{
    Ok,
    Warning,
    Error,
    Unknown,
}

public sealed record ModHealth(
    HealthLevel Level,
    string Summary,
    BuildMatch Build,
    string? BuiltForBuildId,
    bool BepInExMismatch,
    CompatibilityReport Compatibility)
{
    public IEnumerable<string> Details()
    {
        if (BuiltForBuildId is not null) yield return $"built for game build {BuiltForBuildId}";
        if (BepInExMismatch) yield return "built against a different BepInEx version";
        if (Compatibility.Note is not null) yield return Compatibility.Note;
        if (Compatibility.Status != CompatibilityStatus.Unknown)
            yield return $"{Compatibility.ReferencesChecked} game references and {Compatibility.PatchTargetsChecked} patch targets checked";
        foreach (var issue in Compatibility.Issues) yield return $"{IssueLabel(issue.Kind)}: {issue.Description}";
    }

    private static string IssueLabel(IssueKind kind) => kind switch
    {
        IssueKind.MissingType => "missing type",
        IssueKind.MissingMember => "missing member",
        IssueKind.MissingPatchTarget => "missing patch target",
        IssueKind.MissingAssembly => "missing assembly",
        _ => kind.ToString(),
    };
}

public static class ModHealthCheck
{
    public static ModHealth? Evaluate(ModOverview mod, GameState game, CompatibilityChecker checker)
    {
        var file = mod.Active?.Info.FilePath ?? mod.Versions.FirstOrDefault()?.MainFilePath;
        return file is null || !File.Exists(file) ? null : Evaluate(ModInspector.Inspect(file), game, checker);
    }

    public static ModHealth Evaluate(ModInfo mod, GameState game, CompatibilityChecker checker)
    {
        var build = CompareBuild(mod.Stamp, game);
        var bepinexMismatch = mod.Stamp?.BepInExVersion is { } built && game.BepInExVersion is { } installed &&
                              !string.Equals(built, installed, StringComparison.OrdinalIgnoreCase);

        var compatibility = game.Interop switch
        {
            InteropStatus.Missing => CompatibilityReport.Unknown("No interop assemblies yet. Launch the game once with BepInEx installed."),
            InteropStatus.Stale => CompatibilityReport.Unknown("The game updated since BepInEx last generated interop. Launch it once to refresh, then check again."),
            _ => checker.Check(mod.FilePath),
        };

        var (level, summary) = (compatibility.Status, build) switch
        {
            (CompatibilityStatus.Broken, _) => (HealthLevel.Error, $"{compatibility.Issues.Count} game reference(s) missing"),
            (CompatibilityStatus.Unknown, BuildMatch.Match) => (HealthLevel.Ok, "built for this game build"),
            (CompatibilityStatus.Unknown, _) => (HealthLevel.Unknown, "can't check until interop is regenerated"),
            (_, BuildMatch.Match) => (HealthLevel.Ok, "built for this game build"),
            (_, BuildMatch.Different) => (HealthLevel.Warning, $"built for build {mod.Stamp?.GameBuildId ?? "?"}, references still resolve"),
            _ => (HealthLevel.Ok, "references resolve"),
        };

        if (level == HealthLevel.Ok && bepinexMismatch)
            (level, summary) = (HealthLevel.Warning, summary + ", different BepInEx");

        return new ModHealth(level, summary, build, mod.Stamp?.GameBuildId, bepinexMismatch, compatibility);
    }

    public static BuildMatch CompareBuild(BuildStamp? stamp, GameState game)
    {
        if (stamp is null) return BuildMatch.Unstamped;
        if (stamp.GameAssemblyHash is { } hash && File.Exists(game.Install.GameAssemblyPath))
            return string.Equals(hash, GameFingerprint.HashGameAssembly(game.Install), StringComparison.OrdinalIgnoreCase) ? BuildMatch.Match : BuildMatch.Different;
        if (stamp.GameBuildId is { } buildId && game.SteamBuildId is not null)
            return buildId == game.SteamBuildId ? BuildMatch.Match : BuildMatch.Different;
        return BuildMatch.Unstamped;
    }
}
