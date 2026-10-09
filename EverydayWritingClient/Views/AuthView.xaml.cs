using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using EverydayWritingClient.Services;

namespace EverydayWritingClient.Views;

public partial class AuthView : UserControl
{
    private readonly string _mode;
    private string _captchaId = "";

    public AuthView(string mode)
    {
        _mode = mode;
        InitializeComponent();
        ApplyMode();
    }

    private void ApplyMode()
    {
        var isCode = _mode is "register" or "reset";
        CodePanel.Visibility = isCode ? Visibility.Visible : Visibility.Collapsed;
        AgreementBox.Visibility = _mode == "register" ? Visibility.Visible : Visibility.Collapsed;
        OldPasswordPanel.Visibility = _mode == "password" ? Visibility.Visible : Visibility.Collapsed;
        EmailBox.IsEnabled = _mode != "password";
        ToRegister.Visibility = _mode == "login" ? Visibility.Visible : Visibility.Collapsed;
        ToReset.Visibility = _mode == "login" ? Visibility.Visible : Visibility.Collapsed;
        ToLogin.Visibility = _mode == "login" ? Visibility.Collapsed : Visibility.Visible;
        EmailBox.Text = AppServices.Session.Email;

        (TitleText.Text, HintText.Text, SubmitButton.Content, PasswordLabel.Text) = _mode switch
        {
            "register" => ("注册", "验证码会发到邮箱。注册后使用同一账号登录网页和桌面。", "注册并登录", "密码"),
            "reset" => ("重置密码", "通过邮箱验证码设置新密码。", "重置密码", "新密码"),
            "password" => ("修改密码", "当前账号需要先设置一个新密码。", "保存新密码", "新密码"),
            _ => ("登录", "使用与网页端相同的账号。", "登录", "密码"),
        };
    }

    private async void SendCodeClick(object sender, RoutedEventArgs e)
    {
        var email = EmailBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(email))
        {
            StatusText.Text = "请先填写邮箱。";
            return;
        }

        try
        {
            StatusText.Text = "正在发送验证码…";
            await AppServices.Api.PostAsync("/auth/send-code", new
            {
                email,
                purpose = _mode == "register" ? "register" : "reset",
            }, skipAuth: true);
            StatusText.Text = "验证码已发送，请查看邮箱。";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private async void RefreshCaptchaClick(object sender, RoutedEventArgs e) => await LoadCaptchaAsync();

    private async Task LoadCaptchaAsync()
    {
        var data = await AppServices.Api.GetAsync("/auth/send-graphcode", skipAuth: true);
        _captchaId = JsonRead.Str(data, "captchaId");
        var raw = JsonRead.Str(data, "imageBase64");
        var comma = raw.IndexOf(',');
        if (raw.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma > 0)
        {
            raw = raw[(comma + 1)..];
        }

        var bytes = Convert.FromBase64String(raw);
        var image = new BitmapImage();
        using var stream = new System.IO.MemoryStream(bytes);
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        CaptchaImage.Source = image;
        CaptchaPanel.Visibility = Visibility.Visible;
    }

    private async void SubmitClick(object sender, RoutedEventArgs e)
    {
        SubmitButton.IsEnabled = false;
        StatusText.Text = "";
        try
        {
            switch (_mode)
            {
                case "register":
                    await RegisterAsync();
                    break;
                case "reset":
                    await ResetAsync();
                    break;
                case "password":
                    await ChangePasswordAsync();
                    break;
                default:
                    await LoginAsync();
                    break;
            }
        }
        catch (ApiException ex)
        {
            StatusText.Text = ex.Message;
            if (ex.Payload is { } data && JsonRead.Bool(data, "requireCaptcha"))
            {
                try
                {
                    await LoadCaptchaAsync();
                }
                catch (Exception captchaEx)
                {
                    StatusText.Text = ex.Message + " " + captchaEx.Message;
                }
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
        finally
        {
            SubmitButton.IsEnabled = true;
        }
    }

    private async Task RegisterAsync()
    {
        if (AgreementBox.IsChecked != true)
        {
            StatusText.Text = "请先勾选用户协议。";
            return;
        }

        await AppServices.Api.PostAsync("/auth/register", new
        {
            email = EmailBox.Text.Trim(),
            password = PasswordBox.Password,
            code = CodeBox.Text.Trim(),
        }, skipAuth: true);
        await LoginAsync();
    }

    private async Task ResetAsync()
    {
        await AppServices.Api.PostAsync("/auth/reset-password", new
        {
            email = EmailBox.Text.Trim(),
            code = CodeBox.Text.Trim(),
            newPassword = PasswordBox.Password,
        }, skipAuth: true);
        StatusText.Text = "密码已重置，请登录。";
        if (Application.Current.MainWindow is MainWindow shell)
        {
            shell.Navigate("login");
        }
    }

    private async Task ChangePasswordAsync()
    {
        var data = await AppServices.Api.PutAsync("/user/password", new
        {
            oldPassword = OldPasswordBox.Password,
            newPassword = PasswordBox.Password,
        });
        AppServices.Session.ApplyRefresh(data);
        AppServices.Session.MustChangePassword = false;
        AppServices.Session.Save();
        if (Application.Current.MainWindow is MainWindow shell)
        {
            shell.OnLoggedIn();
        }
    }

    private async Task LoginAsync()
    {
        object body = string.IsNullOrWhiteSpace(_captchaId)
            ? new { email = EmailBox.Text.Trim(), password = PasswordBox.Password }
            : new { email = EmailBox.Text.Trim(), password = PasswordBox.Password, captchaId = _captchaId, graphCode = CaptchaBox.Text.Trim() };

        var data = await AppServices.Api.PostAsync("/auth/login", body, skipAuth: true);
        AppServices.Session.ApplyLogin(data);
        await AcceptPendingAgreementsAsync();
        if (Application.Current.MainWindow is MainWindow shell)
        {
            shell.OnLoggedIn();
        }
    }

    private static async Task AcceptPendingAgreementsAsync()
    {
        try
        {
            var status = await AppServices.Api.GetAsync("/agreements/status");
            foreach (var item in JsonRead.Items(status))
            {
                if (JsonRead.Bool(item, "accepted"))
                {
                    continue;
                }

                var id = JsonRead.Str(item, "id");
                if (!string.IsNullOrWhiteSpace(id))
                {
                    await AppServices.Api.PostAsync($"/agreements/{id}/accept");
                }
            }
        }
        catch
        {
            // 协议状态失败不阻断登录。
        }
    }

    private void ToRegisterClick(object sender, RoutedEventArgs e)
    {
        if (Application.Current.MainWindow is MainWindow shell)
        {
            shell.Navigate("register");
        }
    }

    private void ToResetClick(object sender, RoutedEventArgs e)
    {
        if (Application.Current.MainWindow is MainWindow shell)
        {
            shell.Navigate("reset");
        }
    }

    private void ToLoginClick(object sender, RoutedEventArgs e)
    {
        if (Application.Current.MainWindow is MainWindow shell)
        {
            shell.Navigate("login");
        }
    }
}
