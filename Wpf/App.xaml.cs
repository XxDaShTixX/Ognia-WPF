using Ognia.Wpf.Configuration;
using Ognia.Wpf.Services;
using Ognia.Wpf.Theming;
using Ognia.Wpf.YouTube;
using System.Windows;

namespace Ognia.Wpf;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            base.OnStartup(e);

            AppPaths.Initialize();
            AppSettings.Instance.Save();

            ThemeService.Initialize();

            System.Diagnostics.PresentationTraceSources.DataBindingSource.Switch.Level =
                System.Diagnostics.SourceLevels.Warning;
            System.Diagnostics.PresentationTraceSources.DataBindingSource.Listeners.Add(
                new System.Diagnostics.ConsoleTraceListener());

            new MainWindow().Show();

            _ = UpdateService.Instance.CheckAsync(
                downloadIfFound: AppSettings.Instance.Update.AutoUpdateEnabled);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "Startup error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (AppSettings.Instance.Update.AutoUpdateEnabled)
            UpdateService.Instance.ApplyOnExit();

        PreviewDownloader.ClearAll();

        base.OnExit(e);
    }
}