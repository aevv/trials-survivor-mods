using System.Security.Cryptography;
using Mono.Cecil;

namespace TSMods.Core.Mods;

public static class ModInspector
{
    public const string StampPrefix = "TSMods.";
    private const string PluginAttribute = "BepInEx.BepInPlugin";
    private const string DependencyAttribute = "BepInEx.BepInDependency";
    private const int SoftDependencyFlag = 2;

    public static ModInfo Inspect(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));

        try
        {
            using var module = ModuleDefinition.ReadModule(new MemoryStream(bytes));
            return new ModInfo(Path.GetFullPath(path), hash, ReadPlugin(module), ReadStamp(module), ReadDependencies(module), ReadDescription(module));
        }
        catch (BadImageFormatException)
        {
            return new ModInfo(Path.GetFullPath(path), hash, null, null, []);
        }
    }

    private static PluginInfo? ReadPlugin(ModuleDefinition module)
    {
        var attribute = module.GetTypes()
            .SelectMany(t => t.CustomAttributes)
            .FirstOrDefault(a => a.AttributeType.FullName == PluginAttribute && a.ConstructorArguments.Count >= 3);
        if (attribute is null) return null;

        var args = attribute.ConstructorArguments;
        return new PluginInfo((string)args[0].Value, (string)args[1].Value, (string)args[2].Value);
    }

    private static List<ModDependency> ReadDependencies(ModuleDefinition module) =>
        module.GetTypes()
            .SelectMany(t => t.CustomAttributes)
            .Where(a => a.AttributeType.FullName == DependencyAttribute && a.ConstructorArguments.Count >= 1)
            .Select(a => new ModDependency(
                (string)a.ConstructorArguments[0].Value,
                a.ConstructorArguments.Count > 1 && a.ConstructorArguments[1].Value is int flags && (flags & SoftDependencyFlag) != 0))
            .DistinctBy(d => d.Guid)
            .ToList();

    private static string? ReadDescription(ModuleDefinition module) =>
        module.Assembly.CustomAttributes
            .Where(a => a.AttributeType.FullName == typeof(System.Reflection.AssemblyDescriptionAttribute).FullName && a.ConstructorArguments.Count == 1)
            .Select(a => a.ConstructorArguments[0].Value as string)
            .FirstOrDefault(d => !string.IsNullOrWhiteSpace(d));

    private static BuildStamp? ReadStamp(ModuleDefinition module)
    {
        var values = module.Assembly.CustomAttributes
            .Where(a => a.AttributeType.FullName == typeof(System.Reflection.AssemblyMetadataAttribute).FullName && a.ConstructorArguments.Count == 2)
            .Select(a => (Key: a.ConstructorArguments[0].Value as string, Value: a.ConstructorArguments[1].Value as string))
            .Where(kv => kv.Key?.StartsWith(StampPrefix, StringComparison.Ordinal) == true)
            .ToDictionary(kv => kv.Key![StampPrefix.Length..], kv => string.IsNullOrEmpty(kv.Value) ? null : kv.Value);

        if (values.Count == 0) return null;
        return new BuildStamp(
            values.GetValueOrDefault("GameBuildId"),
            values.GetValueOrDefault("GameAssemblyHash"),
            values.GetValueOrDefault("BepInExVersion"));
    }
}
