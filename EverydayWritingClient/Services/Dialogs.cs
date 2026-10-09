using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace EverydayWritingClient.Services;

public static class Dialogs
{
    public static bool Confirm(string message, string title = "请确认", string confirmText = "确定", bool danger = false)
    {
        var sheet = Open(title, 420);
        sheet.Body.Children.Add(Message(message));
        var accept = ActionButton(confirmText, primary: true, danger: danger, isDefault: true);
        accept.Click += (_, _) => sheet.Window.DialogResult = true;
        sheet.Buttons.Children.Add(ActionButton("取消", primary: false, danger: false, isCancel: true));
        sheet.Buttons.Children.Add(accept);
        return Show(sheet.Window) == true;
    }

    public static void Alert(string message, string title = "Everyday Writing")
    {
        var sheet = Open(title, 420);
        sheet.Body.Children.Add(Message(message));
        var ok = ActionButton("知道了", primary: true, danger: false, isDefault: true, isCancel: true);
        ok.Click += (_, _) => sheet.Window.DialogResult = true;
        sheet.Buttons.Children.Add(ok);
        Show(sheet.Window);
    }

    public static int Choose(string title, string message, string primaryText, string secondaryText)
    {
        var sheet = Open(title, 440);
        sheet.Body.Children.Add(Message(message));
        var choice = 0;
        var secondary = ActionButton(secondaryText, primary: false, danger: false);
        var primary = ActionButton(primaryText, primary: true, danger: false, isDefault: true);
        secondary.Click += (_, _) =>
        {
            choice = 2;
            sheet.Window.DialogResult = true;
        };
        primary.Click += (_, _) =>
        {
            choice = 1;
            sheet.Window.DialogResult = true;
        };
        sheet.Buttons.Children.Add(ActionButton("取消", primary: false, danger: false, isCancel: true));
        sheet.Buttons.Children.Add(secondary);
        sheet.Buttons.Children.Add(primary);
        return Show(sheet.Window) == true ? choice : 0;
    }

    public static string? Prompt(string message, string title, string initial = "")
    {
        var sheet = Open(title, 480);
        sheet.Body.Children.Add(Message(message));
        var input = new TextBox
        {
            Text = initial,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 96,
            MaxHeight = 240,
            Margin = new Thickness(0, 14, 0, 0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Style = StyleOf("FieldBox"),
        };
        sheet.Body.Children.Add(input);
        string? result = null;
        var ok = ActionButton("确定", primary: true, danger: false, isDefault: true);
        ok.Click += (_, _) =>
        {
            result = input.Text;
            sheet.Window.DialogResult = true;
        };
        sheet.Buttons.Children.Add(ActionButton("取消", primary: false, danger: false, isCancel: true));
        sheet.Buttons.Children.Add(ok);
        input.Loaded += (_, _) =>
        {
            input.Focus();
            input.SelectAll();
        };
        return Show(sheet.Window) == true ? result : null;
    }

    private static Sheet Open(string title, double width)
    {
        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            FontFamily = new FontFamily("Segoe UI"),
            UseLayoutRounding = true,
            SnapsToDevicePixels = true,
        };
        var card = new Border
        {
            Width = width,
            Margin = new Thickness(28),
            Padding = new Thickness(22, 20, 22, 18),
            CornerRadius = new CornerRadius(18),
            Background = Brush("SurfaceBrush"),
            BorderBrush = Brush("BorderBrush"),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect
            {
                BlurRadius = 28,
                ShadowDepth = 6,
                Direction = 270,
                Opacity = 0.16,
                Color = Color.FromRgb(23, 23, 23),
            },
        };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontFamily = new FontFamily("Georgia"),
            FontSize = 22,
            Foreground = Brush("TextBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        });
        var body = new StackPanel();
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0),
        };
        stack.Children.Add(body);
        stack.Children.Add(buttons);
        card.Child = stack;
        window.Content = card;
        return new Sheet(window, body, buttons);
    }

    private static bool? Show(Window window)
    {
        var owner = Application.Current?.MainWindow;
        if (owner is { IsVisible: true })
        {
            window.Owner = owner;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        return window.ShowDialog();
    }

    private static TextBlock Message(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        FontSize = 14,
        LineHeight = 22,
        Foreground = Brush("TextBrush"),
    };

    private static Button ActionButton(string text, bool primary, bool danger, bool isDefault = false, bool isCancel = false)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 88,
            Margin = new Thickness(8, 0, 0, 0),
            Style = StyleOf(primary ? "PrimaryButton" : "GhostButton"),
            IsDefault = isDefault,
            IsCancel = isCancel,
        };
        if (danger)
        {
            button.Background = Brush("DangerBrush");
            button.Foreground = Brush("PrimaryFgBrush");
        }

        return button;
    }

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];

    private static Style StyleOf(string key) => (Style)Application.Current.Resources[key];

    private sealed record Sheet(Window Window, StackPanel Body, StackPanel Buttons);
}
