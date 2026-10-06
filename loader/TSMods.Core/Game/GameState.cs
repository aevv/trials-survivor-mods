using System.Diagnostics;
using System.Security.Cryptography;

namespace TSMods.Core.Game;

public enum InteropStatus
{
    Missing,
    Stale,
    Current,
}

public sealed record GameState(
    GameInstall Install,
    string? SteamBuildId,
    bool BepInExInstalled,
    string? BepInExVersion,
    bool? ModsEnabled,
    InteropStatus Interop,
    bool IsRunning)
{
    public static GameState Read(GameInstall install) => new(
        install,
        SteamFiles.ReadBuildId(install.SteamManifestPath),
        File.Exists(install.BepInExCorePath),
        ReadBepInExVersion(install),
        Doorstop.IsEnabled(install),
        ReadInteropStatus(install),
        GameProcess.IsRunning());

    public static string? ReadBepInExVersion(GameInstall install)
    {
        if (!File.Exists(install.BepInExCorePath)) return null;
        var version = FileVersionInfo.GetVersionInfo(install.BepInExCorePath).ProductVersion;
        return version?.Split('+')[0];
    }

    public static InteropStatus ReadInteropStatus(GameInstall install)
    {
        var interopAssembly = Path.Combine(install.InteropPath, "Assembly-CSharp.dll");
        if (!File.Exists(interopAssembly)) return InteropStatus.Missing;
        return File.GetLastWriteTimeUtc(interopAssembly) >= File.GetLastWriteTimeUtc(install.GameAssemblyPath)
            ? InteropStatus.Current
            : InteropStatus.Stale;
    }
}

public static class GameProcess
{
    public static bool IsRunning()
    {
        var processes = Process.GetProcessesByName(GameInstall.ProcessName);
        foreach (var process in processes) process.Dispose();
        return processes.Length > 0;
    }

    public static void LaunchViaSteam() =>
        Process.Start(new ProcessStartInfo($"steam://rungameid/{GameInstall.SteamAppId}") { UseShellExecute = true });
}

public static class GameFingerprint
{
    private static readonly Lock Gate = new();
    private static (string Path, DateTime WriteTime, long Length, string Hash)? _cached;

    public static string HashGameAssembly(GameInstall install)
    {
        var file = new FileInfo(install.GameAssemblyPath);
        lock (Gate)
        {
            if (_cached is { } c && c.Path == file.FullName && c.WriteTime == file.LastWriteTimeUtc && c.Length == file.Length)
                return c.Hash;

            using var stream = file.OpenRead();
            var hash = Convert.ToHexStringLower(SHA256.HashData(stream));
            _cached = (file.FullName, file.LastWriteTimeUtc, file.Length, hash);
            return hash;
        }
    }
}
