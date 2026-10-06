using TSMods.Core;
using TSMods.Core.Game;
using TSMods.Core.Library;

namespace TSMods.Loader;

public sealed class LoaderContext
{
    private LoaderContext(LoaderPaths paths, LoaderSettings settings)
    {
        Paths = paths;
        Settings = settings;
        Locate(GameLocator.Find(settings.GamePath));
    }

    public LoaderPaths Paths { get; }
    public LoaderSettings Settings { get; }
    public HttpClient Http { get; } = new();
    public GameInstall? Install { get; private set; }
    public ModManager? Manager { get; private set; }

    public static LoaderContext Create(LoaderPaths? paths = null)
    {
        paths ??= LoaderPaths.Default();
        return new LoaderContext(paths, LoaderSettings.Load(paths));
    }

    public bool TrySetGamePath(string path)
    {
        if (!GameInstall.LooksLikeGame(path)) return false;
        Settings.GamePath = Path.GetFullPath(path);
        Settings.Save(Paths);
        Locate(new GameInstall(Settings.GamePath));
        return true;
    }

    private void Locate(GameInstall? install)
    {
        Install = install;
        Manager = install is null ? null : new ModManager(install, new ModLibrary(Paths.Library), new ProfileStore(Paths.Profiles));
    }
}
