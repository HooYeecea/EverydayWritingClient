using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using EverydayWritingClient.Services;
using EverydayWritingClient.Views;

namespace EverydayWritingClient;

public partial class MainWindow : Window
{
    private readonly Dictionary<string, UserControl> _pages = new();
    private readonly HashSet<string> _seen = [];
    private readonly List<Button> _navButtons = [];
    private int _fadeVersion;
    private bool _collapsed;
    private bool _adminMode;
    private string _current = "writing";

    public bool NavigationLocked { get; set; }

    public MainWindow()
    {
        InitializeComponent();
        AppServices.Api.Unauthorized += () => Dispatcher.Invoke(() =>
        {
            if (!AppServices.Session.IsAuthenticated)
            {
                BuildNav();
                Navigate("login");
            }
        });
        Loaded += async (_, _) => await StartAsync();
    }

    public void Navigate(string key, bool force = false)
    {
        if (NavigationLocked && key is not ("writing" or "login"))
        {
            Dialogs.Alert("专注写作已锁定页面切换。先在写作页关闭专注锁。", "专注写作");
            return;
        }

        if (key is "records" or "vocabulary" or "assessment" or "user" or "settings" or "proficiency" or "admin" && !AppServices.Session.IsAuthenticated)
        {
            Navigate("login");
            Dialogs.Alert("请先登录后再使用这一页。");
            return;
        }

        var page = GetPage(key);
        if (!force && _current == key && ReferenceEquals(PageHost.Content, page))
        {
            HighlightNav(key);
            return;
        }

        var load = force || !_seen.Contains(key);
        _current = key;
        if (load)
        {
            _seen.Add(key);
        }

        Transition(() =>
        {
            PageHost.Content = page;
            if (load && page is IRefreshOnShow refresh)
            {
                _ = refresh.RefreshAsync();
            }
        });
        HighlightNav(key);
    }

    public void InvalidatePage(params string[] keys)
    {
        foreach (var key in keys)
        {
            _seen.Remove(key);
        }
    }

    public void ContinueDraft(string draftId)
    {
        _adminMode = false;
        BuildNav();
        Navigate("writing");
        if (PageHost.Content is WritingView writing)
        {
            _ = writing.LoadDraftAsync(draftId);
        }
    }

    public void ContinueSubmit(string submitId)
    {
        _adminMode = false;
        BuildNav();
        Navigate("writing");
        if (PageHost.Content is WritingView writing)
        {
            _ = writing.LoadSubmitAsync(submitId);
        }
    }

    public void OpenProficiency()
    {
        _adminMode = false;
        BuildNav();
        Navigate("proficiency");
    }

    private async Task StartAsync()
    {
        BuildNav();
        if (!AppServices.Session.IsAuthenticated)
        {
            Navigate("writing");
            return;
        }

        try
        {
            var profile = await AppServices.Api.GetAsync("/user/profile");
            var nickname = JsonRead.Str(profile, "nickname");
            if (!string.IsNullOrWhiteSpace(nickname))
            {
                AppServices.Session.Nickname = nickname;
                AppServices.Session.Save();
            }
        }
        catch (ApiException ex) when (ex.IsUnauthorized)
        {
            Navigate("login");
            return;
        }
        catch
        {
            // 离线或短暂失败时仍保留本地会话，页面里再提示。
        }

        await RefreshGuideDotAsync();
        if (AppServices.Session.MustChangePassword)
        {
            Navigate("password");
            return;
        }

        if (AppServices.Session.IsAdminOnly)
        {
            _adminMode = true;
            BuildNav();
            Navigate("admin");
            return;
        }

        Navigate(AppServices.Session.DefaultHome);
    }

    private void OpenAdmin(string key)
    {
        if (NavigationLocked)
        {
            Dialogs.Alert("专注写作已锁定页面切换。先在写作页关闭专注锁。", "专注写作");
            return;
        }

        var page = (AdminView)GetPage("admin");
        if (ReferenceEquals(PageHost.Content, page) && page.CurrentKey == key)
        {
            HighlightNav(key);
            return;
        }

        _current = "admin";
        _seen.Add("admin");
        Transition(() =>
        {
            PageHost.Content = page;
            page.ShowModule(key);
        });
        HighlightNav(key);
    }

    private void Transition(Action swap)
    {
        if (PageHost.Content == null)
        {
            swap();
            return;
        }

        var version = ++_fadeVersion;
        var fadeOut = new DoubleAnimation(PageHost.Opacity, 0, TimeSpan.FromMilliseconds(90))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
        };
        fadeOut.Completed += (_, _) =>
        {
            if (version != _fadeVersion)
            {
                return;
            }

            swap();
            PageHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            });
        };
        PageHost.BeginAnimation(OpacityProperty, fadeOut);
    }

    private UserControl GetPage(string key)
    {
        if (_pages.TryGetValue(key, out var existing))
        {
            return existing;
        }

        UserControl page = key switch
        {
            "login" => new AuthView("login"),
            "register" => new AuthView("register"),
            "reset" => new AuthView("reset"),
            "password" => new AuthView("password"),
            "user" => new UserCenterView(),
            "writing" => new WritingView(),
            "records" => new RecordsView(),
            "vocabulary" => new VocabularyView(),
            "assessment" => new AssessmentView(),
            "guide" => new GuideView(),
            "settings" => new SettingsView(),
            "proficiency" => new ProficiencyView(),
            "admin" => new AdminView(),
            _ => new WritingView(),
        };
        _pages[key] = page;
        return page;
    }

    private void BuildNav()
    {
        NavPanel.Children.Clear();
        _navButtons.Clear();
        var session = AppServices.Session;
        BrandSubtitle.Text = _adminMode ? "管理后台" : "每日英语写作";
        SwitchModeButton.Visibility = session.IsAdmin ? Visibility.Visible : Visibility.Collapsed;
        SwitchModeButton.Content = _adminMode ? "返回写作" : "管理后台";
        LogoutButton.Visibility = session.IsAuthenticated ? Visibility.Visible : Visibility.Collapsed;

        if (_adminMode)
        {
            AddGroup("概览", ("admin:dashboard", "数据中心"), ("admin:system", "系统监控"), ("admin:token", "Token 用量"));
            AddGroup("用户与权限", ("admin:users", "用户管理"), ("admin:roles", "角色权限"));
            AddGroup("内容运营", ("admin:announcements", "公告管理"), ("admin:agreements", "协议管理"), ("admin:quotes", "励志语录"), ("admin:tiers", "签到段位"));
            AddGroup("题库与写作", ("admin:topic-types", "题目类型"), ("admin:topics", "写作题库"), ("admin:questions", "题库管理"), ("admin:prompts", "Prompt 模板"));
            AddGroup("系统与安全", ("admin:providers", "模型供应商"), ("admin:configs", "系统配置"), ("admin:access", "访问记录"), ("admin:audit", "操作审计"));
        }
        else
        {
            AddNav(session.IsAuthenticated ? "user" : "login", session.IsAuthenticated ? "用户中心" : "立即登录");
            AddNav("writing", "开始写作");
            AddNav("records", "写作记录");
            AddNav("vocabulary", "个人词库");
            AddNav("assessment", "个人测评");
            AddNav("guide", "使用指南", session.ShowGuideRedDot);
            AddNav("settings", "系统设置");
        }

        HighlightNav(_current);
        ApplyCollapsed();
    }

    private void AddGroup(string title, params (string Key, string Label)[] items)
    {
        NavPanel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("MutedBrush"),
            Margin = new Thickness(12, 12, 8, 4),
        });
        foreach (var item in items)
        {
            AddNav(item.Key, item.Label);
        }
    }

    private void AddNav(string key, string label, bool dot = false)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        if (dot)
        {
            panel.Children.Add(new Ellipse
            {
                Width = 7,
                Height = 7,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Fill = new SolidColorBrush(Color.FromRgb(180, 35, 24)),
            });
        }

        var button = new Button
        {
            Content = _collapsed ? label[..1] : panel,
            Uid = key,
            Style = (Style)FindResource("NavButton"),
            ToolTip = label,
        };
        button.Click += (_, _) =>
        {
            if (key.StartsWith("admin:", StringComparison.Ordinal))
            {
                OpenAdmin(key);
                return;
            }

            Navigate(key);
        };
        _navButtons.Add(button);
        NavPanel.Children.Add(button);
    }

    private void HighlightNav(string key)
    {
        foreach (var button in _navButtons)
        {
            var buttonKey = button.Uid;
            var active = buttonKey == key || (key == "admin" && buttonKey == "admin:dashboard") || (key is "register" or "reset" or "password" && buttonKey == "login");
            button.Tag = active ? "active" : null;
            button.FontWeight = active ? FontWeights.Medium : FontWeights.Normal;
        }
    }

    private async void ToggleMode(object sender, RoutedEventArgs e)
    {
        if (!AppServices.Session.IsAuthenticated)
        {
            Navigate("login");
            return;
        }

        if (!_adminMode && !AppServices.Session.IsAdmin)
        {
            Dialogs.Alert("当前账号没有管理后台权限。");
            return;
        }

        _adminMode = !_adminMode;
        BuildNav();
        Navigate(_adminMode ? "admin" : "writing");
        await Task.CompletedTask;
    }

    private void ToggleCollapse(object sender, RoutedEventArgs e)
    {
        _collapsed = !_collapsed;
        ApplyCollapsed();
        BuildNav();
    }

    private void ApplyCollapsed()
    {
        SidebarColumn.Width = new GridLength(_collapsed ? 72 : 232);
        BrandExpanded.Visibility = _collapsed ? Visibility.Collapsed : Visibility.Visible;
        BrandCollapsed.Visibility = _collapsed ? Visibility.Visible : Visibility.Collapsed;
        CollapseButton.Content = _collapsed ? "»" : "收起侧栏";
    }

    private async void LogoutClick(object sender, RoutedEventArgs e)
    {
        var choice = Dialogs.Choose("退出登录", "只退出这台电脑，或让其他设备上的登录一起失效。", "只退出这台", "退出全部设备");
        if (choice == 0)
        {
            return;
        }

        try
        {
            await AppServices.Api.PostAsync(choice == 2 ? "/auth/logout-all" : "/auth/logout");
        }
        catch
        {
            // 服务端拒绝时仍然清掉本机会话。
        }

        AppServices.Session.ClearAuth();
        _pages.Clear();
        _seen.Clear();
        _adminMode = false;
        NavigationLocked = false;
        BuildNav();
        Navigate("login");
    }

    public void OnLoggedIn()
    {
        _pages.Clear();
        _seen.Clear();
        _adminMode = AppServices.Session.IsAdminOnly;
        BuildNav();
        if (AppServices.Session.MustChangePassword)
        {
            Navigate("password");
            return;
        }

        Navigate(_adminMode ? "admin" : AppServices.Session.DefaultHome);
        _ = RefreshGuideDotAsync();
    }

    public async Task RefreshGuideDotAsync()
    {
        if (!AppServices.Session.IsAuthenticated)
        {
            return;
        }

        try
        {
            var status = await AppServices.Api.GetAsync("/proficiency-test/status");
            AppServices.Session.ShowGuideRedDot = JsonRead.Bool(status, "showGuideRedDot");
            if (!_adminMode)
            {
                BuildNav();
            }
        }
        catch
        {
            // 红点失败不影响主流程。
        }
    }
}
