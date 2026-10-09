using System.Windows;
using EverydayWritingClient.Services;

namespace EverydayWritingClient;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppServices.Session.Load();
        ThemeService.Apply(AppServices.Session.Theme);
        DispatcherUnhandledException += (_, args) =>
        {
            try
            {
                Dialogs.Alert(args.Exception.Message, "出了点问题");
            }
            catch
            {
                MessageBox.Show(args.Exception.Message, "Everyday Writing", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            args.Handled = true;
        };
    }
}
