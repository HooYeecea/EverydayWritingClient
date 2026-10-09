using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EverydayWritingClient.Services;

public static class Dialogs
{
    public static bool Confirm(string message, string title = "请确认")
    {
        return MessageBox.Show(Application.Current.MainWindow, message, title, MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;
    }

    public static void Alert(string message, string title = "Everyday Writing")
    {
        MessageBox.Show(Application.Current.MainWindow, message, title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public static string? Prompt(string message, string title, string initial = "")
    {
        var window = new Window
        {
            Title = title,
            Width = 440,
            Height = 230,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Application.Current.MainWindow,
            ResizeMode = ResizeMode.NoResize,
            Background = (Brush)Application.Current.Resources["BgBrush"],
            FontFamily = new FontFamily("Segoe UI"),
        };

        var root = new DockPanel { Margin = new Thickness(20) };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };
        DockPanel.SetDock(buttons, Dock.Bottom);
        var input = new TextBox
        {
            Text = initial,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 72,
            Style = (Style)Application.Current.Resources["FieldBox"],
        };
        string? result = null;
        var ok = new Button { Content = "确定", Style = (Style)Application.Current.Resources["PrimaryButton"], Margin = new Thickness(8, 0, 0, 0), IsDefault = true };
        var cancel = new Button { Content = "取消", Style = (Style)Application.Current.Resources["GhostButton"], IsCancel = true };
        ok.Click += (_, _) =>
        {
            result = input.Text;
            window.DialogResult = true;
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        root.Children.Add(buttons);
        root.Children.Add(new TextBlock
        {
            Text = message,
            Foreground = (Brush)Application.Current.Resources["MutedBrush"],
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        });
        DockPanel.SetDock(root.Children[1], Dock.Top);
        root.Children.Add(input);
        window.Content = root;
        return window.ShowDialog() == true ? result : null;
    }
}
