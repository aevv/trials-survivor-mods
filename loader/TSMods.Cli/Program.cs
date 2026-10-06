using System.Text.Json;
using TSMods.Cli;
using TSMods.Core;
using TSMods.Core.Compatibility;
using TSMods.Core.Config;
using TSMods.Core.Game;
using TSMods.Core.Library;
using TSMods.Core.Mods;
using TSMods.Core.Releases;

var args0 = args.ToList();
var gameOverride = Options.Take(args0, "--game");
var json = Options.Flag(args0, "--json");

if (args0.Count == 0 || args0[0] is "help" or "-h" or "--help")
{
    Console.WriteLine(Help.Text);
    return 0;
}

try
{
    var paths = LoaderPaths.Default();
    var settings = LoaderSettings.Load(paths);

    if (args0[0] == "inspect")
    {
        var mod = ModInspector.Inspect(Options.Arg(args0, 1, "dll path"));
        Console.WriteLine(json ? JsonSerializer.Serialize(mod, Output.Json) : Output.Describe(mod));
        return 0;
    }

    if (args0[0] == "game" && args0.Count > 1 && args0[1] == "set")
    {
        var path = Options.Arg(args0, 2, "game folder");
        if (!GameInstall.LooksLikeGame(path)) throw new InvalidOperationException($"'{path}' doesn't look like the {GameInstall.DisplayName} folder.");
        settings.GamePath = Path.GetFullPath(path);
        settings.Save(paths);
        Console.WriteLine($"game path set to {settings.GamePath}");
        return 0;
    }

    var install = GameLocator.Find(gameOverride ?? settings.GamePath)
                  ?? throw new InvalidOperationException($"Couldn't find {GameInstall.DisplayName}. Pass --game <folder> or run 'tsmods game set <folder>'.");
    var manager = new ModManager(install, new ModLibrary(paths.Library), new ProfileStore(paths.Profiles));
    using var http = new HttpClient();

    switch (args0[0])
    {
        case "status":
            Commands.Status(install, paths);
            break;
        case "list":
            Commands.List(manager, json);
            break;
        case "check":
            Commands.Check(manager, args0.ElementAtOrDefault(1));
            break;
        case "sync":
            var imported = manager.SyncLibrary();
            Console.WriteLine(imported.Count == 0 ? "library already has every installed mod" : string.Join("\n", imported.Select(e => $"stored {e.Name} {e.Id}")));
            break;
        case "enable":
            var enabled = manager.Enable(manager.Get(Options.Arg(args0, 1, "mod")).Guid, args0.ElementAtOrDefault(2));
            Console.WriteLine($"enabled {enabled.Name} {enabled.Id}");
            break;
        case "disable":
            var disabled = manager.Disable(manager.Get(Options.Arg(args0, 1, "mod")).Guid);
            Console.WriteLine($"disabled {disabled.Name} (kept {disabled.Id} in the library)");
            break;
        case "versions":
            Commands.Versions(manager, Options.Arg(args0, 1, "mod"));
            break;
        case "remove-version":
            var target = manager.Get(Options.Arg(args0, 1, "mod"));
            manager.DeleteVersion(target.Guid, Options.Arg(args0, 2, "version"));
            Console.WriteLine($"removed {target.Name} {args0[2]} from the library");
            break;
        case "import":
            var dll = Options.Arg(args0, 1, "dll path");
            var entry = Options.Flag(args0, "--enable") ? manager.ImportAndEnable(dll, "file") : manager.Library.ImportFile(dll, "file");
            Console.WriteLine($"imported {entry.Name} {entry.Id}");
            break;
        case "config":
            Commands.Config(manager, args0.Skip(1).ToList());
            break;
        case "profile":
            Commands.Profile(manager, args0.Skip(1).ToList());
            break;
        case "mods":
            var on = Options.Arg(args0, 1, "on|off") switch { "on" => true, "off" => false, var v => throw new InvalidOperationException($"expected on or off, got '{v}'") };
            manager.EnsureGameClosed();
            Doorstop.SetEnabled(install, on);
            Console.WriteLine(on ? "BepInEx enabled: mods will load" : "BepInEx disabled: the game will start vanilla");
            break;
        case "bepinex":
            if (args0.ElementAtOrDefault(1) != "install") throw new InvalidOperationException("usage: tsmods bepinex install [zip]");
            await BepInExInstaller.InstallAsync(install, paths, http, args0.ElementAtOrDefault(2), new Progress<string>(Console.WriteLine));
            break;
        case "releases":
            await Commands.Releases(new GitHubReleases(http, settings.ReleaseRepo));
            break;
        case "install":
            await Commands.InstallRelease(manager, new GitHubReleases(http, settings.ReleaseRepo), paths, Options.Arg(args0, 1, "mod"), args0.ElementAtOrDefault(2));
            break;
        case "launch":
            GameProcess.LaunchViaSteam();
            Console.WriteLine("launching via Steam");
            break;
        default:
            Console.Error.WriteLine($"unknown command '{args0[0]}'\n");
            Console.WriteLine(Help.Text);
            return 2;
    }

    return 0;
}
catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException or HttpRequestException)
{
    Console.Error.WriteLine($"error: {e.Message}");
    return 1;
}

namespace TSMods.Cli
{
    internal static class Commands
    {
        public static void Status(GameInstall install, LoaderPaths paths)
        {
            var state = GameState.Read(install);
            Output.Row("game", install.RootPath);
            Output.Row("build", state.SteamBuildId ?? "unknown (no Steam manifest)");
            Output.Row("bepinex", state.BepInExInstalled
                ? $"{state.BepInExVersion}, mods {(state.ModsEnabled == false ? "off (vanilla)" : "on")}"
                : "not installed (tsmods bepinex install)");
            Output.Row("interop", state.Interop switch
            {
                InteropStatus.Current => "current",
                InteropStatus.Stale => "stale: launch the game once to regenerate",
                _ => "missing: launch the game once with BepInEx installed",
            });
            Output.Row("running", state.IsRunning ? "yes (changes are blocked)" : "no");
            Output.Row("library", paths.Root);
        }

        public static void List(ModManager manager, bool json)
        {
            manager.SyncLibrary();
            var state = GameState.Read(manager.Install);
            using var checker = CompatibilityChecker.For(manager.Install);
            var rows = manager.Overview().Select(m => (Mod: m, Health: Health(m, state, checker))).ToList();

            if (json)
            {
                Console.WriteLine(JsonSerializer.Serialize(rows.Select(r => new
                {
                    r.Mod.Guid, r.Mod.Name, r.Mod.Enabled, r.Mod.Version, r.Mod.ActiveEntryId,
                    Versions = r.Mod.Versions.Select(v => v.Id),
                    Health = r.Health is null ? null : new { Level = r.Health.Level.ToString(), r.Health.Summary, Details = r.Health.Details() },
                    r.Mod.MissingDependencies,
                }), Output.Json));
                return;
            }

            if (rows.Count == 0)
            {
                Console.WriteLine("no mods installed or stored");
                return;
            }

            var nameWidth = rows.Max(r => r.Mod.Name.Length);
            foreach (var (mod, health) in rows)
            {
                var flag = mod.Enabled ? "on " : "off";
                var level = health is null ? "-" : health.Level.ToString().ToLowerInvariant();
                var extra = mod.MissingDependencies.Count > 0 ? $"  needs {string.Join(", ", mod.MissingDependencies)}" : "";
                Console.WriteLine($"  {flag}  {mod.Name.PadRight(nameWidth)}  {mod.ActiveEntryId ?? mod.Versions.FirstOrDefault()?.Id,-16}  {level,-8} {health?.Summary}{extra}");
            }
        }

        public static void Check(ModManager manager, string? query)
        {
            var state = GameState.Read(manager.Install);
            using var checker = CompatibilityChecker.For(manager.Install);
            var mods = query is null ? manager.Overview() : [manager.Get(query)];
            foreach (var mod in mods)
            {
                var health = Health(mod, state, checker);
                Console.WriteLine($"{mod.Name} {mod.Version}: {health?.Level.ToString().ToLowerInvariant() ?? "-"} - {health?.Summary ?? "no file to check"}");
                if (health is null) continue;
                foreach (var detail in health.Details()) Console.WriteLine($"    {detail}");
            }
        }

        public static void Versions(ModManager manager, string query)
        {
            var mod = manager.Get(query);
            if (mod.Versions.Count == 0) Console.WriteLine($"{mod.Name}: nothing stored yet (tsmods sync)");
            foreach (var version in mod.Versions)
            {
                var marker = version.Id == mod.ActiveEntryId ? "*" : " ";
                Console.WriteLine($"{marker} {version.Id,-18} {version.ImportedAt.LocalDateTime:yyyy-MM-dd HH:mm}  from {version.Source}");
            }
        }

        public static void Config(ModManager manager, List<string> args)
        {
            var mod = manager.Get(Options.Arg(args, 0, "mod"));
            var path = manager.Install.ConfigFileFor(mod.Guid);
            if (!File.Exists(path)) throw new InvalidOperationException($"{mod.Name} has no config yet. Launch the game once with it enabled.");
            var document = ConfigDocument.Load(path);

            if (args.Count == 1)
            {
                foreach (var section in document.Settings.GroupBy(s => s.Section))
                {
                    Console.WriteLine($"[{section.Key}]");
                    foreach (var setting in section)
                    {
                        var hint = setting.HasRange ? $" ({setting.RangeMin}..{setting.RangeMax})"
                            : setting.AcceptableValues.Count is > 0 and <= 8 ? $" ({string.Join("|", setting.AcceptableValues)})" : "";
                        var changed = setting.IsDefault ? "" : $"  [default {setting.DefaultValue}]";
                        Console.WriteLine($"  {setting.Key} = {setting.Value}{hint}{changed}");
                    }
                }
                return;
            }

            manager.EnsureGameClosed();
            var target = document.Find(Options.Arg(args, 1, "setting")) ?? throw new InvalidOperationException($"No setting '{args[1]}'. Use Section.Key if the key is ambiguous.");
            var value = args.Count > 2 ? string.Join(' ', args.Skip(2)) : throw new InvalidOperationException("usage: tsmods config <mod> <setting> <value>");
            var error = document.Set(target, value);
            if (error is not null) throw new InvalidOperationException($"{target.Id}: {error}");
            document.Save();
            Console.WriteLine($"{target.Id} = {target.Value}");
        }

        public static void Profile(ModManager manager, List<string> args)
        {
            switch (args.ElementAtOrDefault(0))
            {
                case null or "list":
                    var all = manager.Profiles.All();
                    if (all.Count == 0) Console.WriteLine("no profiles (tsmods profile save <name>)");
                    foreach (var p in all) Console.WriteLine($"  {p.Name}: {string.Join(", ", p.Mods.Select(m => m.Guid.Split('.')[^1] + " " + m.EntryId))}");
                    break;
                case "save":
                    var saved = manager.CaptureProfile(Options.Arg(args, 1, "name"));
                    Console.WriteLine($"saved {saved.Name} with {saved.Mods.Count} mod(s)");
                    break;
                case "apply":
                    var profile = manager.Profiles.Find(Options.Arg(args, 1, "name")) ?? throw new InvalidOperationException($"No profile '{args[1]}'.");
                    var changes = manager.ApplyProfile(profile);
                    Console.WriteLine(changes.Count == 0 ? "already matches" : string.Join("\n", changes));
                    break;
                case "delete":
                    Console.WriteLine(manager.Profiles.Delete(Options.Arg(args, 1, "name")) ? "deleted" : "no such profile");
                    break;
                default:
                    throw new InvalidOperationException("usage: tsmods profile list|save|apply|delete <name>");
            }
        }

        public static async Task Releases(GitHubReleases releases)
        {
            var mods = await releases.ListAsync();
            if (mods.Count == 0) Console.WriteLine($"no releases on {releases.Repo}");
            foreach (var mod in mods) Console.WriteLine($"  {mod.ModName,-18} {mod.Version,-10} {mod.PublishedAt.LocalDateTime:yyyy-MM-dd}  {mod.Tag}");
        }

        public static async Task InstallRelease(ModManager manager, GitHubReleases releases, LoaderPaths paths, string modName, string? version)
        {
            manager.EnsureGameClosed();
            var candidates = (await releases.ListAsync())
                .Where(m => string.Equals(m.ModName, modName, StringComparison.OrdinalIgnoreCase))
                .Where(m => version is null || m.Version == version)
                .ToList();
            var release = candidates.FirstOrDefault() ?? throw new InvalidOperationException($"No release of '{modName}'{(version is null ? "" : $" {version}")} on {releases.Repo}.");
            var file = await releases.DownloadAsync(release, paths.Downloads);
            var entry = manager.ImportAndEnable(file, $"github:{release.Tag}");
            Console.WriteLine($"installed {entry.Name} {entry.Id}");
        }

        private static ModHealth? Health(ModOverview mod, GameState state, CompatibilityChecker checker)
        {
            var file = mod.Active?.Info.FilePath ?? mod.Versions.FirstOrDefault()?.MainFilePath;
            return file is null || !File.Exists(file) ? null : ModHealthCheck.Evaluate(ModInspector.Inspect(file), state, checker);
        }
    }

    internal static class Output
    {
        public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

        public static void Row(string label, string value) => Console.WriteLine($"{label,-9} {value}");

        public static string Describe(ModInfo mod)
        {
            var lines = new List<string>
            {
                $"file      {mod.FilePath}",
                $"sha256    {mod.Sha256}",
                mod.Plugin is { } p ? $"plugin    {p.Name} {p.Version} ({p.Guid})" : "plugin    none (not a BepInEx plugin)",
            };
            if (mod.Stamp is { } s) lines.Add($"built for game build {s.GameBuildId ?? "?"}, BepInEx {s.BepInExVersion ?? "?"}, GameAssembly {s.GameAssemblyHash?[..12] ?? "?"}");
            else lines.Add("no TSMods build stamp");
            lines.AddRange(mod.Dependencies.Select(d => $"depends   {d.Guid}{(d.Soft ? " (soft)" : "")}"));
            return string.Join(Environment.NewLine, lines);
        }
    }

    internal static class Options
    {
        public static string? Take(List<string> args, string name)
        {
            var index = args.IndexOf(name);
            if (index < 0) return null;
            if (index + 1 >= args.Count) throw new InvalidOperationException($"{name} needs a value");
            var value = args[index + 1];
            args.RemoveRange(index, 2);
            return value;
        }

        public static bool Flag(List<string> args, string name) => args.Remove(name);

        public static string Arg(List<string> args, int index, string what) =>
            args.ElementAtOrDefault(index) ?? throw new InvalidOperationException($"missing {what}");
    }

    internal static class Help
    {
        public const string Text = """
            tsmods - Trials Survivors mod manager

            usage: tsmods [--game <folder>] <command>

              status                         game, BepInEx and interop state
              list [--json]                  installed and stored mods with compatibility
              check [mod]                    detailed compatibility check against the installed game
              enable <mod> [version]         install a stored version (latest if omitted)
              disable <mod>                  remove from plugins, keep in the library
              versions <mod>                 stored versions, * marks the installed one
              remove-version <mod> <version> delete a stored version
              sync                           store every installed mod in the library
              import <dll> [--enable]        add a mod file to the library
              config <mod>                   show settings
              config <mod> <setting> <value> change a setting (Section.Key or Key)
              profile list|save|apply|delete <name>
              mods on|off                    toggle BepInEx without uninstalling
              bepinex install [zip]          install BepInEx 6 IL2CPP (downloads the pinned build)
              releases                       mods published on GitHub
              install <mod> [version]        download a release, store and enable it
              launch                         start the game through Steam
              inspect <dll> [--json]         read a mod's plugin info and build stamp
              game set <folder>              remember the game folder

            <mod> matches the guid, the display name or the short name (RunHud).
            """;
    }
}
