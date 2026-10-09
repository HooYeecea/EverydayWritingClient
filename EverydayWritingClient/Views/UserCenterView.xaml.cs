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
        AddText(JsonRead.Str(profile, "nickname"), 24);
        AddText(JsonRead.Str(profile, "email"));
        var stats = JsonRead.Prop(profile, "stats");
        AddText($"提交 {JsonRead.Str(stats, "totalWritings")} 篇 · 字数 {JsonRead.Str(stats, "totalWords")} · 词库 {JsonRead.Str(stats, "vocabularyCount")}");
        var box = new TextBox { Style = (Style)FindResource("FieldBox"), Text = JsonRead.Str(profile, "nickname"), Margin = new Thickness(0, 12, 0, 8), Width = 280, HorizontalAlignment = HorizontalAlignment.Left };
        var save = new Button { Content = "保存昵称", Style = (Style)FindResource("PrimaryButton"), HorizontalAlignment = HorizontalAlignment.Left };
        save.Click += async (_, _) =>
        {
            await AppServices.Api.PutAsync("/user/profile", new { nickname = box.Text.Trim() });
            AppServices.Session.Nickname = box.Text.Trim();
            AppServices.Session.Save();
            AddText("昵称已更新。");
        };
        Body.Children.Add(box);
        Body.Children.Add(save);

        var test = new Button { Content = "英语能力测评", Style = (Style)FindResource("GhostButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 16, 0, 8) };
        test.Click += (_, _) =>
        {
            if (Application.Current.MainWindow is MainWindow shell)
            {
                shell.OpenProficiency();
            }
        };
        Body.Children.Add(test);

        try
        {
            var announcements = await AppServices.Api.GetAsync("/announcements");
            AddText("公告", 18);
            foreach (var item in JsonRead.Items(announcements))
            {
                AddText($"{JsonRead.Str(item, "title")}\n{JsonRead.PlainText(JsonRead.Str(item, "content"))}");
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
        }
        catch (Exception ex)
        {
            AddText(ex.Message, danger: true);
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
        AddText("本月 Token", 22);
        AddText(JsonRead.Pretty(summary));
        try
        {
            var budget = await AppServices.Api.GetAsync("/usage/budget");
            AddText("预算", 18);
            AddText(JsonRead.Pretty(budget));
        }
        catch (Exception ex)
        {
            AddText(ex.Message, danger: true);
        }
    }

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
