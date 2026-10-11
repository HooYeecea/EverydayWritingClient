using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using EverydayWritingClient.Services;

namespace EverydayWritingClient.Views;

public partial class SettingsView : UserControl, IRefreshOnShow
{
    private readonly List<JsonElement> _providers = [];
    private bool _loading = true;

    public SettingsView()
    {
        InitializeComponent();
        ThemeBox.Items.Add(new Choice("light", "浅色"));
        ThemeBox.Items.Add(new Choice("dark", "深色"));
        ThemeBox.Items.Add(new Choice("system", "跟随系统"));
        FontBox.Items.Add(new Choice("sm", "较小"));
        FontBox.Items.Add(new Choice("md", "标准"));
        FontBox.Items.Add(new Choice("lg", "较大"));
        SaveBox.Items.Add(new Choice("3", "3 秒"));
        SaveBox.Items.Add(new Choice("5", "5 秒"));
        SaveBox.Items.Add(new Choice("10", "10 秒"));
        HomeBox.Items.Add(new Choice("writing", "开始写作"));
        HomeBox.Items.Add(new Choice("records", "写作记录"));
        HomeBox.Items.Add(new Choice("user", "用户中心"));
        Select(ThemeBox, AppServices.Session.Theme);
        Select(FontBox, AppServices.Session.EditorFontSize);
        Select(SaveBox, AppServices.Session.AutoSaveSeconds.ToString());
        Select(HomeBox, AppServices.Session.DefaultHome);
        ConfirmBox.IsChecked = AppServices.Session.ConfirmBeforeSubmit;
        ServerBox.Text = string.IsNullOrWhiteSpace(AppServices.Session.ApiOrigin)
            ? ApiClient.DefaultOrigin
            : AppServices.Session.ApiOrigin;
        _loading = false;
    }

    public async Task RefreshAsync()
    {
        if (!AppServices.Session.IsAuthenticated)
        {
            return;
        }

        try
        {
            var config = await AppServices.Api.GetAsync("/ai/config");
            _providers.Clear();
            ProviderBox.Items.Clear();
            ProviderBox.Items.Add(new Choice("free", "免费通道"));
            foreach (var provider in JsonRead.Items(JsonRead.Prop(config, "providers")))
            {
                _providers.Add(provider.Clone());
                ProviderBox.Items.Add(new Choice(JsonRead.Str(provider, "id"), JsonRead.Str(provider, "name")));
            }

            Select(ProviderBox, string.IsNullOrWhiteSpace(AppServices.Session.AiProviderId) ? "free" : AppServices.Session.AiProviderId);
            FillModels();
            var quota = JsonRead.Prop(config, "freeQuota");
            if (quota.ValueKind == JsonValueKind.Object)
            {
                StatusText.Text = $"免费通道今日 Token {JsonRead.Str(quota, "todayTokensUsed")} / {JsonRead.Str(quota, "dailyTokenLimit")}";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private void ProviderChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading)
        {
            FillModels();
        }
    }

    private void FillModels()
    {
        ModelBox.Items.Clear();
        var providerId = (ProviderBox.SelectedItem as Choice)?.Value;
        if (providerId == "free" || string.IsNullOrWhiteSpace(providerId))
        {
            ModelBox.Items.Add(new Choice("free", "免费通道默认模型"));
            ModelBox.SelectedIndex = 0;
            return;
        }

        var provider = _providers.FirstOrDefault(item => JsonRead.Str(item, "id") == providerId);
        foreach (var model in JsonRead.Items(JsonRead.Prop(provider, "models")))
        {
            ModelBox.Items.Add(new Choice(JsonRead.Str(model, "id"), JsonRead.Str(model, "name")));
        }

        Select(ModelBox, AppServices.Session.AiModelId);
        if (ModelBox.SelectedIndex < 0 && ModelBox.Items.Count > 0)
        {
            ModelBox.SelectedIndex = 0;
        }
    }

    private async void SavePrefsClick(object sender, RoutedEventArgs e)
    {
        if (!TryOrigin(ServerBox.Text, out var origin))
        {
            StatusText.Text = "服务器地址需要是 http 或 https，例如 http://localhost:5000。";
            return;
        }

        AppServices.Session.ApiOrigin = origin == ApiClient.DefaultOrigin ? "" : origin;
        var theme = (ThemeBox.SelectedItem as Choice)?.Value ?? "light";
        var font = (FontBox.SelectedItem as Choice)?.Value ?? "md";
        var seconds = int.TryParse((SaveBox.SelectedItem as Choice)?.Value, out var parsed) ? parsed : 5;
        var home = (HomeBox.SelectedItem as Choice)?.Value ?? "writing";
        AppServices.Session.Theme = theme;
        AppServices.Session.EditorFontSize = font;
        AppServices.Session.AutoSaveSeconds = seconds;
        AppServices.Session.ConfirmBeforeSubmit = ConfirmBox.IsChecked == true;
        AppServices.Session.DefaultHome = home;
        AppServices.Session.Save();
        ThemeService.Apply(theme);

        if (!AppServices.Session.IsAuthenticated)
        {
            StatusText.Text = "已保存在这台电脑。登录后会再同步到账号。";
            return;
        }

        try
        {
            JsonObject prefs;
            try
            {
                var remote = await AppServices.Api.GetAsync("/user/preferences");
                prefs = JsonNode.Parse(remote.GetRawText()) as JsonObject ?? new JsonObject();
            }
            catch
            {
                prefs = new JsonObject { ["locale"] = "zh" };
            }

            var ui = prefs["ui"] as JsonObject ?? new JsonObject();
            ui["theme"] = theme;
            ui["defaultHomePath"] = home switch
            {
                "records" => "/records",
                "user" => "/user-center",
                _ => "/writing",
            };
            prefs["ui"] = ui;
            var writing = prefs["writing"] as JsonObject ?? new JsonObject();
            writing["editorFontSize"] = font;
            writing["autoSaveIntervalSec"] = seconds;
            writing["confirmBeforeSubmit"] = ConfirmBox.IsChecked == true;
            prefs["writing"] = writing;
            prefs["locale"] = "zh";
            await AppServices.Api.PutAsync("/user/preferences", prefs.ToJsonString());
            StatusText.Text = "偏好已保存，并同步到账号。";
        }
        catch (Exception ex)
        {
            StatusText.Text = "本机已保存。同步账号失败：" + ex.Message;
        }
    }

    private async void SaveAiClick(object sender, RoutedEventArgs e)
    {
        var providerId = (ProviderBox.SelectedItem as Choice)?.Value ?? "free";
        var modelId = (ModelBox.SelectedItem as Choice)?.Value ?? "free";
        AppServices.Session.AiProviderId = providerId == "free" ? "" : providerId;
        AppServices.Session.AiModelId = modelId == "free" ? "" : modelId;
        if (!string.IsNullOrWhiteSpace(KeyBox.Password) && providerId != "free")
        {
            try
            {
                var data = await AppServices.Api.PostAsync("/ai/key", new { providerId, apiKey = KeyBox.Password });
                AppServices.Session.EncryptedAiKey = JsonRead.Str(data, "encryptedKey");
                KeyBox.Clear();
            }
            catch (Exception ex)
            {
                StatusText.Text = ex.Message;
                return;
            }
        }

        if (providerId == "free")
        {
            AppServices.Session.EncryptedAiKey = "";
        }

        AppServices.Session.Save();
        StatusText.Text = string.IsNullOrWhiteSpace(AppServices.Session.EncryptedAiKey) ? "将使用免费通道。" : "自有 Key 已加密保存在本机。";
    }

    private void PasswordClick(object sender, RoutedEventArgs e)
    {
        if (Application.Current.MainWindow is MainWindow shell)
        {
            shell.Navigate("password");
        }
    }

    private static bool TryOrigin(string text, out string origin)
    {
        origin = "";
        text = text.Trim();
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        var path = uri.AbsolutePath.TrimEnd('/');
        if (path.Length > 0 && !path.Equals("/api/v1", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        origin = uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        return true;
    }

    private static void Select(ComboBox box, string value)
    {
        foreach (Choice choice in box.Items)
        {
            if (choice.Value == value)
            {
                box.SelectedItem = choice;
                return;
            }
        }

        if (box.Items.Count > 0)
        {
            box.SelectedIndex = 0;
        }
    }

    private sealed record Choice(string Value, string Label)
    {
        public override string ToString() => Label;
    }
}
