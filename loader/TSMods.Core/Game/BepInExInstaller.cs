using System.IO.Compression;

namespace TSMods.Core.Game;

public static class BepInExInstaller
{
    public const string PinnedVersion = "6.0.0-be.788";
    public const string PinnedUrl = "https://builds.bepinex.dev/projects/bepinex_be/788/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788%2B5b766a3.zip";

    public static async Task InstallAsync(GameInstall install, LoaderPaths paths, HttpClient http, string? zipPath = null, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (GameProcess.IsRunning()) throw new GameRunningException();

        if (zipPath is null)
        {
            Directory.CreateDirectory(paths.Downloads);
            zipPath = Path.Combine(paths.Downloads, $"BepInEx-Unity.IL2CPP-win-x64-{PinnedVersion}.zip");
            if (!File.Exists(zipPath))
            {
                progress?.Report($"downloading BepInEx {PinnedVersion}");
                var partial = zipPath + ".part";
                await using (var source = await http.GetStreamAsync(PinnedUrl, ct))
                await using (var target = File.Create(partial))
                    await source.CopyToAsync(target, ct);
                File.Move(partial, zipPath, overwrite: true);
            }
        }

        progress?.Report($"extracting {Path.GetFileName(zipPath)} into {install.RootPath}");
        await ZipFile.ExtractToDirectoryAsync(zipPath, install.RootPath, overwriteFiles: true, ct);
        progress?.Report("BepInEx installed. Launch the game once so it generates interop assemblies.");
    }
}
