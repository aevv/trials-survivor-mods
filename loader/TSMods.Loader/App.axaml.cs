using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using TSMods.Loader.ViewModels;
using TSMods.Loader.Views;

namespace TSMods.Loader;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            var viewModel = new MainViewModel(LoaderContext.Create(), new WindowDialogs(window));
            window.DataContext = viewModel;
            desktop.MainWindow = window;
            _ = viewModel.InitialiseAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
