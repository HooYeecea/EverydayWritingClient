using System.Windows;
using System.Windows.Controls;

namespace EverydayWritingClient.Views;

public partial class GuideView : UserControl, IRefreshOnShow
{
    public GuideView()
    {
        InitializeComponent();
    }

    public async Task RefreshAsync()
    {
        if (Application.Current.MainWindow is MainWindow shell)
        {
            await shell.RefreshGuideDotAsync();
        }
    }

    private void OpenTest(object sender, RoutedEventArgs e)
    {
        if (Application.Current.MainWindow is MainWindow shell)
        {
            shell.OpenProficiency();
        }
    }
}
