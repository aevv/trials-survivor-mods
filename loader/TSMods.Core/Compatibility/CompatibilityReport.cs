namespace TSMods.Core.Compatibility;

public enum CompatibilityStatus
{
    Unknown,
    Compatible,
    Broken,
}

public enum IssueKind
{
    MissingType,
    MissingMember,
    MissingPatchTarget,
    MissingAssembly,
}

public sealed record CompatibilityIssue(IssueKind Kind, string Description);

public sealed record CompatibilityReport(
    CompatibilityStatus Status,
    IReadOnlyList<CompatibilityIssue> Issues,
    int ReferencesChecked,
    int PatchTargetsChecked,
    string? Note = null)
{
    public static CompatibilityReport Unknown(string note) => new(CompatibilityStatus.Unknown, [], 0, 0, note);
}
