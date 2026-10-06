using Mono.Cecil;
using TSMods.Core.Game;

namespace TSMods.Core.Compatibility;

public sealed class CompatibilityChecker : IDisposable
{
    private const string HarmonyPatchAttribute = "HarmonyLib.HarmonyPatch";
    private const string MethodTypeEnum = "HarmonyLib.MethodType";

    private readonly DefaultAssemblyResolver _resolver = new();
    private readonly HashSet<string> _interopAssemblies;
    private readonly string _interopPath;

    public CompatibilityChecker(string interopPath, params string[] extraSearchPaths)
    {
        _interopPath = interopPath;
        foreach (var directory in _resolver.GetSearchDirectories()) _resolver.RemoveSearchDirectory(directory);
        _resolver.AddSearchDirectory(interopPath);
        foreach (var path in extraSearchPaths.Where(Directory.Exists)) _resolver.AddSearchDirectory(path);

        _interopAssemblies = Directory.Exists(interopPath)
            ? Directory.EnumerateFiles(interopPath, "*.dll").Select(Path.GetFileNameWithoutExtension).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase)
            : [];
    }

    public static CompatibilityChecker For(GameInstall install) => new(install.InteropPath, install.CorePath);

    public CompatibilityReport Check(string modPath)
    {
        if (_interopAssemblies.Count == 0)
            return CompatibilityReport.Unknown($"No interop assemblies at {_interopPath}. Launch the game once with BepInEx installed.");

        using var module = ModuleDefinition.ReadModule(new MemoryStream(File.ReadAllBytes(modPath)), new ReaderParameters { AssemblyResolver = _resolver });

        var issues = new List<CompatibilityIssue>();
        var seen = new HashSet<string>();
        void Report(IssueKind kind, string description)
        {
            if (seen.Add(description)) issues.Add(new CompatibilityIssue(kind, description));
        }

        var referencesChecked = 0;
        foreach (var type in module.GetTypeReferences().Where(IsInterop))
        {
            referencesChecked++;
            if (TryResolve(type, Report) is null) Report(IssueKind.MissingType, type.FullName);
        }

        foreach (var member in module.GetMemberReferences())
        {
            if (member.DeclaringType is ArrayType || !IsInterop(member.DeclaringType)) continue;
            referencesChecked++;
            if (TryResolve(member, Report) is null) Report(IssueKind.MissingMember, Describe(member));
        }

        var targets = HarmonyTargets(module).ToList();
        foreach (var target in targets) CheckPatchTarget(target, Report);

        var status = issues.Count == 0 ? CompatibilityStatus.Compatible : CompatibilityStatus.Broken;
        return new CompatibilityReport(status, issues, referencesChecked, targets.Count);
    }

    public void Dispose() => _resolver.Dispose();

    private bool IsInterop(TypeReference type)
    {
        var element = type.GetElementType();
        while (element.DeclaringType is not null) element = element.DeclaringType;
        return element.Scope is AssemblyNameReference name && _interopAssemblies.Contains(name.Name);
    }

    private static T? TryResolve<T>(T reference, Action<IssueKind, string> report) where T : MemberReference
    {
        try
        {
            return reference switch
            {
                TypeReference t => t.Resolve() is null ? null : reference,
                MethodReference m => m.Resolve() is null ? null : reference,
                FieldReference f => f.Resolve() is null ? null : reference,
                _ => reference,
            };
        }
        catch (AssemblyResolutionException e)
        {
            report(IssueKind.MissingAssembly, e.AssemblyReference.Name);
            return reference;
        }
    }

    private static string Describe(MemberReference member) => member switch
    {
        MethodReference { Name: var name } m when name.StartsWith("get_") || name.StartsWith("set_") =>
            $"{m.DeclaringType.GetElementType().FullName}.{name[4..]} ({name[..3]})",
        MethodReference m => $"{m.DeclaringType.GetElementType().FullName}.{m.Name}({string.Join(", ", m.Parameters.Select(p => p.ParameterType.Name))})",
        FieldReference f => $"{f.DeclaringType.GetElementType().FullName}.{f.Name}",
        _ => member.FullName,
    };

    private sealed record PatchTarget(TypeReference? Type, string? Method, int? MethodType, IReadOnlyList<TypeReference>? Arguments, string Patcher);

    private static IEnumerable<PatchTarget> HarmonyTargets(ModuleDefinition module)
    {
        foreach (var type in module.GetTypes())
        {
            var classTarget = Merge(null, type.CustomAttributes, type.FullName);
            var hasDynamicTarget = type.Methods.Any(m => m.Name is "TargetMethod" or "TargetMethods");

            var methodTargets = type.Methods
                .Where(m => m.CustomAttributes.Any(a => a.AttributeType.FullName == HarmonyPatchAttribute))
                .Select(m => Merge(classTarget, m.CustomAttributes, $"{type.FullName}.{m.Name}"))
                .OfType<PatchTarget>()
                .ToList();

            var targets = methodTargets.Count > 0 ? methodTargets : classTarget is null ? [] : [classTarget];
            foreach (var target in targets)
            {
                if (hasDynamicTarget && (target.Type is null || target.Method is null)) continue;
                yield return target;
            }
        }
    }

    private static PatchTarget? Merge(PatchTarget? inherited, IEnumerable<CustomAttribute> attributes, string patcher)
    {
        var target = inherited is null ? null : inherited with { Patcher = patcher };
        foreach (var attribute in attributes.Where(a => a.AttributeType.FullName == HarmonyPatchAttribute))
        {
            target ??= new PatchTarget(null, null, null, null, patcher);
            var strings = new List<string>();
            foreach (var argument in attribute.ConstructorArguments)
            {
                switch (argument.Value)
                {
                    case TypeReference typeRef: target = target with { Type = typeRef }; break;
                    case string text: strings.Add(text); break;
                    case CustomAttributeArgument[] items when items.All(i => i.Value is TypeReference):
                        target = target with { Arguments = items.Select(i => (TypeReference)i.Value).ToList() };
                        break;
                    case int value when argument.Type.FullName == MethodTypeEnum: target = target with { MethodType = value }; break;
                }
            }

            if (strings.Count == 1) target = target with { Method = strings[0] };
        }
        return target;
    }

    private void CheckPatchTarget(PatchTarget target, Action<IssueKind, string> report)
    {
        if (target.Type is null || !IsInterop(target.Type)) return;

        var description = $"{target.Type.FullName}.{target.Method ?? MethodTypeName(target.MethodType)} (patched by {target.Patcher})";
        TypeDefinition? type;
        try { type = target.Type.Resolve(); }
        catch (AssemblyResolutionException e) { report(IssueKind.MissingAssembly, e.AssemblyReference.Name); return; }

        if (type is null)
        {
            report(IssueKind.MissingPatchTarget, description);
            return;
        }

        if (!HasTarget(type, target)) report(IssueKind.MissingPatchTarget, description);
    }

    private static bool HasTarget(TypeDefinition type, PatchTarget target)
    {
        for (var current = type; current is not null; current = SafeResolve(current.BaseType))
        {
            var found = target.MethodType switch
            {
                1 => current.Properties.Any(p => p.Name == target.Method && p.GetMethod is not null),
                2 => current.Properties.Any(p => p.Name == target.Method && p.SetMethod is not null),
                3 => current.Methods.Any(m => m.IsConstructor && !m.IsStatic && ArgumentsMatch(m, target.Arguments)),
                4 => current.Methods.Any(m => m.IsConstructor && m.IsStatic),
                _ => target.Method is null || current.Methods.Any(m => m.Name == target.Method && ArgumentsMatch(m, target.Arguments)),
            };
            if (found) return true;
            if (target.MethodType is 3 or 4) return false;
        }
        return false;
    }

    private static TypeDefinition? SafeResolve(TypeReference? type)
    {
        try { return type?.Resolve(); }
        catch (AssemblyResolutionException) { return null; }
    }

    private static bool ArgumentsMatch(MethodDefinition method, IReadOnlyList<TypeReference>? arguments) =>
        arguments is null ||
        (method.Parameters.Count == arguments.Count &&
         method.Parameters.Zip(arguments).All(p => p.First.ParameterType.GetElementType().FullName == p.Second.GetElementType().FullName));

    private static string MethodTypeName(int? methodType) => methodType switch
    {
        3 => ".ctor",
        4 => ".cctor",
        _ => "?",
    };
}
