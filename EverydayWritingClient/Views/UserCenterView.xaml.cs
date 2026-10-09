using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using EverydayWritingClient.Services;

namespace EverydayWritingClient.Views;

public partial class UserCenterView : UserControl, IRefreshOnShow
{
    public UserCenterView()
    {
        InitializeComponent();
    }

    public Task RefreshAsync() => ShowAsync("overview");

    private async void TabClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            await ShowAsync(button.Tag as string ?? "overview");
        }
    }

    private void OpenSettings(object sender, RoutedEventArgs e)
    {
        if (Application.Current.MainWindow is MainWindow shell)
        {
            shell.Navigate("settings");
        }
    }

    private async Task ShowAsync(string tab)
    {
        Body.Children.Clear();
        try
        {
            switch (tab)
            {
                case "plan":
                    await ShowPlanAsync();
                    break;
                case "checkin":
                    await ShowCheckInAsync();
                    break;
                case "usage":
                    await ShowUsageAsync();
                    break;
                default:
                    await ShowOverviewAsync();
                    break;
            }
        }
        catch (Exception ex)
        {
            Body.Children.Add(Text(ex.Message, danger: true));
        }
    }

    private async Task ShowOverviewAsync()
    {
        var profile = await AppServices.Api.GetAsync("/user/profile");
        var nickname = JsonRead.Str(profile, "nickname");
        var email = JsonRead.Str(profile, "email");
        var stats = JsonRead.Prop(profile, "stats");
        var profileCard = new StackPanel();
        profileCard.Children.Add(Text(string.IsNullOrWhiteSpace(nickname) ? email : nickname, 24));
        if (!string.IsNullOrWhiteSpace(nickname))
        {
            profileCard.Children.Add(Text(email));
        }

        var metrics = new UniformGrid { Columns = 3, Margin = new Thickness(0, 8, 0, 0) };
        metrics.Children.Add(Metric("提交", JsonRead.Str(stats, "totalWritings"), "篇"));
        metrics.Children.Add(Metric("字数", JsonRead.Str(stats, "totalWords"), ""));
        metrics.Children.Add(Metric("词库", JsonRead.Str(stats, "vocabularyCount"), "条"));
        profileCard.Children.Add(metrics);
        var editor = new StackPanel();
        editor.Children.Add(Text("昵称", 16));
        var box = new TextBox
        {
            Style = (Style)FindResource("FieldBox"),
            Text = nickname,
            Margin = new Thickness(0, 4, 0, 12),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var save = new Button { Content = "保存昵称", Style = (Style)FindResource("PrimaryButton"), HorizontalAlignment = HorizontalAlignment.Left };
        save.Click += async (_, _) =>
        {
            await AppServices.Api.PutAsync("/user/profile", new { nickname = box.Text.Trim() });
            AppServices.Session.Nickname = box.Text.Trim();
            AppServices.Session.Save();
            await ShowAsync("overview");
        };
        editor.Children.Add(box);
        editor.Children.Add(save);
        var test = new Button
        {
            Content = "英语能力测评",
            Style = (Style)FindResource("GhostButton"),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 12, 0, 0),
        };
        test.Click += (_, _) =>
        {
            if (Application.Current.MainWindow is MainWindow shell)
            {
                shell.OpenProficiency();
            }
        };
        editor.Children.Add(test);
        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var profileBorder = Card(profileCard);
        profileBorder.Margin = new Thickness(0, 0, 14, 14);
        var editorBorder = Card(editor);
        editorBorder.Margin = new Thickness(0, 0, 0, 14);
        Grid.SetColumn(editorBorder, 1);
        columns.Children.Add(profileBorder);
        columns.Children.Add(editorBorder);
        Body.Children.Add(columns);

        try
        {
            var announcements = await AppServices.Api.GetAsync("/announcements");
            var list = new StackPanel();
            list.Children.Add(Text("公告", 18));
            var any = false;
            foreach (var item in JsonRead.Items(announcements))
            {
                any = true;
                list.Children.Add(Text(JsonRead.Str(item, "title"), 16));
                list.Children.Add(Text(JsonRead.PlainText(JsonRead.Str(item, "content"))));
                var id = JsonRead.Str(item, "id");
                if (!JsonRead.Bool(item, "hasRead") && !string.IsNullOrWhiteSpace(id))
                {
                    try
                    {
                        await AppServices.Api.PostAsync($"/announcements/{id}/read");
                    }
                    catch
                    {
                        // 已读回写失败不影响阅读。
                    }
                }
            }

            if (!any)
            {
                list.Children.Add(Text("暂时没有公告。"));
            }

            Body.Children.Add(Card(list));
        }
        catch (Exception ex)
        {
            Body.Children.Add(Text(ex.Message, danger: true));
        }
    }

    private async Task ShowPlanAsync()
    {
        var plan = await AppServices.Api.GetAsync("/proficiency-test/plan");
        if (plan.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            AddText("还没有个人计划。完成英语能力测评后会生成。");
            return;
        }

        AddText(JsonRead.Str(plan, "title"), 22);
        AddText(JsonRead.Str(plan, "summary"));
        var phases = JsonRead.Prop(plan, "phases");
        if (phases.ValueKind != JsonValueKind.Array)
        {
            phases = JsonRead.Prop(plan, "items");
        }

        if (phases.ValueKind == JsonValueKind.Array)
        {
            var index = 1;
            foreach (var phase in phases.EnumerateArray())
            {
                var title = First(phase, "title", "name", "focus");
                var detail = First(phase, "description", "advice", "content");
                AddText($"{index}. {title}");
                if (!string.IsNullOrWhiteSpace(detail))
                {
                    AddText(detail);
                }
                index++;
            }
        }
        else
        {
            AddText(JsonRead.Pretty(plan));
        }
    }

    private async Task ShowCheckInAsync()
    {
        var status = await AppServices.Api.GetAsync("/checkin/status");
        var tier = JsonRead.Prop(status, "checkinTier");
        AddText(JsonRead.Bool(status, "checkedInToday") ? "今天已经打卡" : "今天还没有正式提交", 22);
        AddText($"连续 {JsonRead.Str(status, "currentStreak")} 天 · 最长 {JsonRead.Str(status, "longestStreak")} 天 · 累计 {JsonRead.Str(status, "totalCheckIns")} 天");
        AddText($"段位 {JsonRead.Str(tier, "name")}");
        var quote = JsonRead.Prop(status, "quote");
        var quoteText = JsonRead.Str(quote, "content");
        if (!string.IsNullOrWhiteSpace(quoteText))
        {
            AddText(quoteText);
        }

        var now = DateTime.Now;
        var calendar = await AppServices.Api.GetAsync("/checkin/calendar", new Dictionary<string, string?>
        {
            ["year"] = now.Year.ToString(),
            ["month"] = now.Month.ToString(),
        });
        var days = new HashSet<int>();
        var checkedDays = JsonRead.Prop(calendar, "checkedDays");
        if (checkedDays.ValueKind == JsonValueKind.Array)
        {
            foreach (var day in checkedDays.EnumerateArray())
            {
                if (day.TryGetInt32(out var number))
                {
                    days.Add(number);
                }
            }
        }

        AddText($"{now.Year} 年 {now.Month} 月");
        var grid = new UniformGrid { Columns = 7, Margin = new Thickness(0, 8, 0, 0) };
        var first = new DateTime(now.Year, now.Month, 1);
        for (var i = 0; i < (int)first.DayOfWeek; i++)
        {
            grid.Children.Add(new TextBlock());
        }

        for (var day = 1; day <= DateTime.DaysInMonth(now.Year, now.Month); day++)
        {
            grid.Children.Add(new Border
            {
                Background = days.Contains(day) ? (Brush)FindResource("PrimaryBrush") : (Brush)FindResource("SubtleBrush"),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(3),
                Padding = new Thickness(0, 6, 0, 6),
                Child = new TextBlock
                {
                    Text = day.ToString(),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = days.Contains(day) ? (Brush)FindResource("PrimaryFgBrush") : (Brush)FindResource("TextBrush"),
                },
            });
        }

        Body.Children.Add(grid);
    }

    private async Task ShowUsageAsync()
    {
        var summary = await AppServices.Api.GetAsync("/usage/summary");
        var metrics = new UniformGrid { Columns = 4 };
        metrics.Children.Add(Metric("本月消耗", JsonRead.Str(summary, "consumedThisMonth"), "Token"));
        metrics.Children.Add(Metric("累计消耗", JsonRead.Str(summary, "totalConsumed"), "Token"));
        metrics.Children.Add(Metric("调用次数", JsonRead.Str(summary, "totalCalls"), "次"));
        metrics.Children.Add(Metric("月预算", JsonRead.Str(summary, "monthlyBudget"), "Token"));
        var panel = new StackPanel();
        panel.Children.Add(Text("用量", 18));
        panel.Children.Add(metrics);
        var status = JsonRead.Str(summary, "budgetStatus");
        if (!string.IsNullOrWhiteSpace(status))
        {
            panel.Children.Add(Text($"预算状态：{StatusLabel(status)}"));
        }

        try
        {
            var budget = await AppServices.Api.GetAsync("/usage/budget");
            var limit = JsonRead.Str(budget, "budgetLimit");
            var percent = JsonRead.Str(budget, "consumedPercent");
            if (!string.IsNullOrWhiteSpace(limit))
            {
                panel.Children.Add(Text($"预算上限 {limit}，已用 {percent}%"));
            }
        }
        catch (Exception ex)
        {
            panel.Children.Add(Text(ex.Message, danger: true));
        }

        Body.Children.Add(Card(panel));
    }

    private Border Card(UIElement child) => new()
    {
        Style = (Style)FindResource("Card"),
        Margin = new Thickness(0, 0, 0, 14),
        HorizontalAlignment = HorizontalAlignment.Stretch,
        Child = child,
    };

    private Border Metric(string label, string value, string unit)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 8, 12, 8) };
        stack.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = (Brush)FindResource("MutedBrush"),
            FontSize = 12,
        });
        stack.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(value) ? "—" : value,
            FontFamily = new FontFamily("Georgia"),
            FontSize = 26,
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = (Brush)FindResource("TextBrush"),
        });
        if (!string.IsNullOrWhiteSpace(unit))
        {
            stack.Children.Add(new TextBlock
            {
                Text = unit,
                Foreground = (Brush)FindResource("MutedBrush"),
                FontSize = 12,
            });
        }

        return new Border { Child = stack };
    }

    private static string StatusLabel(string status) => status switch
    {
        "normal" => "正常",
        "warning" => "接近上限",
        "exceeded" => "已超出",
        _ => status,
    };

    private void AddText(string text, double size = 15, bool danger = false)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        Body.Children.Add(Text(text, size, danger));
    }

    private TextBlock Text(string text, double size = 15, bool danger = false) => new()
    {
        Text = text,
        FontSize = size,
        FontFamily = new FontFamily(size >= 20 ? "Georgia" : "Segoe UI"),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 8),
        Foreground = (Brush)FindResource(danger ? "DangerBrush" : "TextBrush"),
    };

    private static string First(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            var value = JsonRead.Str(element, name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return "";
    }
}
