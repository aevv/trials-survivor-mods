using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using TSMods.Core.Game;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using TypeAttributes = Mono.Cecil.TypeAttributes;

namespace TSMods.Core.Tests;

public sealed class TempDir : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("tsmods-tests-").FullName;

    public string this[string relative] => System.IO.Path.Combine(Path, relative);

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch (IOException) { }
    }
}

public sealed record FakeMod(
    string Guid = "test.mod",
    string Name = "Test Mod",
    string Version = "1.0.0",
    string AssemblyName = "TrialsSurvivors.TestMod",
    string[]? Calls = null,
    string[]? MissingTypes = null,
    (string Type, string Method)[]? Patches = null,
    string? GameBuildId = null,
    string? Salt = null,
    string? Description = null);

public static class Fakes
{
    public static GameInstall Game(TempDir dir, string buildId = "100", params string[] gameMethods)
    {
        var root = dir["steamapps/common/Trials Survivors"];
        Directory.CreateDirectory(root);
        File.WriteAllText(System.IO.Path.Combine(root, "GameAssembly.dll"), "native");
        File.WriteAllText(System.IO.Path.Combine(root, GameInstall.ProcessName + ".exe"), "exe");
        var install = new GameInstall(root);
        Directory.CreateDirectory(install.PluginsPath);
        Directory.CreateDirectory(install.ConfigPath);
        Directory.CreateDirectory(install.InteropPath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(install.SteamManifestPath)!);
        File.WriteAllText(install.SteamManifestPath, $"\"AppState\"\n{{\n\t\"appid\"\t\t\"{GameInstall.SteamAppId}\"\n\t\"buildid\"\t\t\"{buildId}\"\n}}\n");
        WriteGameAssembly(System.IO.Path.Combine(install.InteropPath, "Assembly-CSharp.dll"), gameMethods.Length == 0 ? ["Activate"] : gameMethods);
        File.SetLastWriteTimeUtc(install.GameAssemblyPath, DateTime.UtcNow.AddMinutes(-5));
        return install;
    }

    public static void WriteGameAssembly(string path, IEnumerable<string> methods)
    {
        using var assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("Assembly-CSharp", new Version(0, 0, 0, 0)), "Assembly-CSharp", ModuleKind.Dll);
        var module = assembly.MainModule;
        var enemy = new TypeDefinition("Game", "Enemy", TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
        module.Types.Add(enemy);
        foreach (var name in methods)
        {
            var method = new MethodDefinition(name, MethodAttributes.Public, module.TypeSystem.Void);
            method.Body.GetILProcessor().Emit(OpCodes.Ret);
            enemy.Methods.Add(method);
        }
        assembly.Write(path);
    }

    public static string WriteMod(string path, FakeMod spec)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        using var assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition(spec.AssemblyName, new Version(1, 0, 0, 0)), spec.AssemblyName, ModuleKind.Dll);
        var module = assembly.MainModule;
        var game = new AssemblyNameReference("Assembly-CSharp", new Version(0, 0, 0, 0));
        var bepinex = new AssemblyNameReference("BepInEx.Core", new Version(6, 0, 0, 0));
        var harmony = new AssemblyNameReference("0Harmony", new Version(2, 0, 0, 0));
        module.AssemblyReferences.Add(game);
        module.AssemblyReferences.Add(bepinex);
        module.AssemblyReferences.Add(harmony);

        var enemy = new TypeReference("Game", "Enemy", module, game);
        var plugin = new TypeDefinition("TestMod", "Plugin", TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
        module.Types.Add(plugin);

        var pluginAttribute = Attribute(module, bepinex, "BepInEx", "BepInPlugin", module.TypeSystem.String, module.TypeSystem.String, module.TypeSystem.String);
        foreach (var value in new[] { spec.Guid, spec.Name, spec.Version })
            pluginAttribute.ConstructorArguments.Add(new CustomAttributeArgument(module.TypeSystem.String, value));
        plugin.CustomAttributes.Add(pluginAttribute);

        var body = new MethodDefinition("Run", MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.Void);
        var il = body.Body.GetILProcessor();
        foreach (var call in spec.Calls ?? [])
        {
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Callvirt, new MethodReference(call, module.TypeSystem.Void, enemy) { HasThis = true });
        }
        foreach (var missing in spec.MissingTypes ?? [])
        {
            il.Emit(OpCodes.Ldtoken, new TypeReference("Game", missing, module, game));
            il.Emit(OpCodes.Pop);
        }
        if (spec.Salt is not null)
        {
            il.Emit(OpCodes.Ldstr, spec.Salt);
            il.Emit(OpCodes.Pop);
        }
        il.Emit(OpCodes.Ret);
        plugin.Methods.Add(body);

        var typeType = module.ImportReference(typeof(Type));
        foreach (var (type, method) in spec.Patches ?? [])
        {
            var patch = new TypeDefinition("TestMod", $"Patch_{type}_{method}", TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Abstract | TypeAttributes.Sealed, module.TypeSystem.Object);
            var attribute = Attribute(module, harmony, "HarmonyLib", "HarmonyPatch", typeType, module.TypeSystem.String);
            attribute.ConstructorArguments.Add(new CustomAttributeArgument(typeType, new TypeReference("Game", type, module, game)));
            attribute.ConstructorArguments.Add(new CustomAttributeArgument(module.TypeSystem.String, method));
            patch.CustomAttributes.Add(attribute);
            module.Types.Add(patch);
        }

        if (spec.GameBuildId is not null)
        {
            var ctor = module.ImportReference(typeof(AssemblyMetadataAttribute).GetConstructor([typeof(string), typeof(string)]));
            foreach (var (key, value) in new[] { ("TSMods.GameBuildId", spec.GameBuildId), ("TSMods.BepInExVersion", "6.0.0-be.788") })
            {
                var stamp = new CustomAttribute(ctor);
                stamp.ConstructorArguments.Add(new CustomAttributeArgument(module.TypeSystem.String, key));
                stamp.ConstructorArguments.Add(new CustomAttributeArgument(module.TypeSystem.String, value));
                assembly.CustomAttributes.Add(stamp);
            }
        }

        if (spec.Description is not null)
        {
            var description = new CustomAttribute(module.ImportReference(typeof(AssemblyDescriptionAttribute).GetConstructor([typeof(string)])));
            description.ConstructorArguments.Add(new CustomAttributeArgument(module.TypeSystem.String, spec.Description));
            assembly.CustomAttributes.Add(description);
        }

        assembly.Write(path);
        return path;
    }

    private static CustomAttribute Attribute(ModuleDefinition module, AssemblyNameReference scope, string ns, string name, params TypeReference[] parameters)
    {
        var type = new TypeReference(ns, name, module, scope);
        var ctor = new MethodReference(".ctor", module.TypeSystem.Void, type) { HasThis = true };
        foreach (var parameter in parameters) ctor.Parameters.Add(new ParameterDefinition(parameter));
        return new CustomAttribute(ctor);
    }
}
