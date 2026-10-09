using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using EverydayWritingClient.Services;
using Microsoft.Win32;

namespace EverydayWritingClient.Views;

public partial class AdminView : UserControl, IRefreshOnShow
{
    private static readonly Dictionary<string, Module> Modules = new()
    {
        ["admin:dashboard"] = new("数据中心", "/admin/dashboard/overview", null, null, true, ["period"], null),
        ["admin:system"] = new("系统监控", "/admin/system/info", null, null, true, [], null),
        ["admin:token"] = new("Token 用量", "/admin/token-usage", null, null, false, ["purpose", "totalTokens", "createdAt"], null),
        ["admin:users"] = new("用户管理", "/admin/users", null, "/admin/users/{id}", false, ["email", "nickname", "vipLevel", "isBanned", "createdAt"], null),
        ["admin:roles"] = new("角色权限", "/admin/roles", "/admin/roles", "/admin/roles/{id}", false, ["name", "description"], """{"name":"","description":"","permissionIds":[]}"""),
        ["admin:announcements"] = new("公告管理", "/admin/announcements", "/admin/announcements", "/admin/announcements/{id}", false, ["title", "priority", "isPublished", "publishedAt"], """{"title":"","content":"","priority":"Normal","isPublished":false}"""),
        ["admin:agreements"] = new("协议管理", "/admin/agreements", "/admin/agreements", "/admin/agreements/{id}", false, ["title", "type", "version"], """{"type":"privacy","title":"","content":""}"""),
        ["admin:quotes"] = new("励志语录", "/admin/quotes", "/admin/quotes", "/admin/quotes/{id}", false, ["content", "isEnabled", "sortOrder"], """{"content":"","isEnabled":true,"sortOrder":0}"""),
        ["admin:tiers"] = new("签到段位", "/admin/checkin-tiers", "/admin/checkin-tiers", "/admin/checkin-tiers/{id}", false, ["name", "minDays", "sortOrder"], """{"name":"","minDays":1,"iconUrl":"","sortOrder":0}"""),
        ["admin:topic-types"] = new("题目类型", "/admin/topic-types", "/admin/topic-types", "/admin/topic-types/{id}", false, ["name", "description", "isEnabled", "sortOrder"], """{"name":"","description":"","sortOrder":0,"isEnabled":true}"""),
        ["admin:topics"] = new("写作题库", "/admin/writing-topics", "/admin/writing-topics", "/admin/writing-topics/{id}", false, ["title", "type", "wordLimit", "isEnabled"], """{"title":"","description":"","type":"CET4","wordLimit":"120-150","isEnabled":true}"""),
        ["admin:questions"] = new("题库管理", "/admin/questions", "/admin/questions", "/admin/questions/{id}", false, ["questionType", "difficulty", "isEnabled"], """{"questionType":"vocabulary","difficulty":1,"content":{},"isEnabled":true}"""),
        ["admin:prompts"] = new("Prompt 模板", "/admin/prompts", "/admin/prompts", "/admin/prompts/{id}", false, ["name", "purpose", "isEnabled"], """{"name":"","purpose":"grammar","content":"","description":"","isEnabled":true}"""),
        ["admin:providers"] = new("模型供应商", "/admin/providers", "/admin/providers", "/admin/providers/{id}", false, ["name", "baseUrl", "isEnabled"], """{"id":"","name":"","baseUrl":"","authHeader":"Authorization","sortOrder":0}"""),
        ["admin:configs"] = new("系统配置", "/admin/configs", null, "/admin/configs/{id}", false, ["key", "value", "description"], null),
        ["admin:access"] = new("访问记录", "/admin/access-logs", null, null, false, ["path", "statusCode", "ip", "createdAt"], null),
        ["admin:audit"] = new("操作审计", "/admin/audit-logs", null, null, false, ["action", "target", "createdAt"], null),
    };

    private static readonly Dictionary<string, string> Headers = new()
    {
        ["email"] = "邮箱",
        ["nickname"] = "昵称",
        ["vipLevel"] = "会员等级",
        ["isBanned"] = "已封禁",
        ["createdAt"] = "创建时间",
        ["name"] = "名称",
        ["description"] = "说明",
        ["title"] = "标题",
        ["priority"] = "优先级",
        ["isPublished"] = "已发布",
        ["publishedAt"] = "发布时间",
        ["type"] = "类型",
        ["version"] = "版本",
        ["content"] = "内容",
        ["isEnabled"] = "已启用",
        ["sortOrder"] = "排序",
        ["minDays"] = "最少天数",
        ["wordLimit"] = "字数要求",
        ["questionType"] = "题型",
        ["difficulty"] = "难度",
        ["purpose"] = "用途",
        ["totalTokens"] = "Token",
        ["baseUrl"] = "接口地址",
        ["key"] = "键",
        ["value"] = "值",
        ["path"] = "路径",
        ["statusCode"] = "状态码",
        ["ip"] = "IP",
        ["action"] = "操作",
        ["target"] = "对象",
        ["period"] = "周期",
    };

    private readonly List<(UniformGrid Grid, int Columns)> _responsive = [];
    private string _key = "";
    private string _period = "7d";
    private int _page = 1;
    private int _totalPages = 1;
    private int _version;
    private JsonElement? _selected;

    public string CurrentKey => _key;

    public AdminView()
    {
        InitializeComponent();
    }

    public Task RefreshAsync()
    {
        if (string.IsNullOrEmpty(_key))
        {
            ShowModule("admin:dashboard");
        }

        return Task.CompletedTask;
    }

    public void ShowModule(string key)
    {
        if (!Modules.ContainsKey(key))
        {
            key = "admin:dashboard";
        }

        _key = key;
        _page = 1;
        var module = Modules[key];
        TitleText.Text = module.Title;
        CreateButton.Visibility = module.CreatePath == null ? Visibility.Collapsed : Visibility.Visible;
        SaveButton.Visibility = module.ItemPath == null ? Visibility.Collapsed : Visibility.Visible;
        DeleteButton.Visibility = module.ItemPath == null || module.Summary ? Visibility.Collapsed : Visibility.Visible;
        ExtraButton.Visibility = key is "admin:users" or "admin:topics" or "admin:providers" ? Visibility.Visible : Visibility.Collapsed;
        ExtraButton.Content = key switch
        {
            "admin:users" => "封禁/解封",
            "admin:topics" => "导入或生成",
            "admin:providers" => "启用/停用",
            _ => "更多",
        };
        var summary = module.Summary;
        SummaryHost.Visibility = summary ? Visibility.Visible : Visibility.Collapsed;
        ListHost.Visibility = summary ? Visibility.Collapsed : Visibility.Visible;
        PagerBar.Visibility = summary ? Visibility.Collapsed : Visibility.Visible;
        SearchBox.Visibility = key is "admin:users" or "admin:topics" or "admin:questions" ? Visibility.Visible : Visibility.Collapsed;
        PeriodBar.Visibility = key == "admin:dashboard" ? Visibility.Visible : Visibility.Collapsed;
        MarkPeriod();
        JsonBox.Text = summary ? "" : module.Template ?? "";
        _ = LoadAsync(++_version);
    }

    private async void PeriodClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string period || period == _period)
        {
            return;
        }

        _period = period;
        MarkPeriod();
        await LoadAsync(++_version);
    }

    private void MarkPeriod()
    {
        foreach (var child in PeriodBar.Children)
        {
            if (child is Button button)
            {
                button.Style = (Style)FindResource(button.Tag as string == _period ? "PrimaryButton" : "GhostButton");
            }
        }
    }

    private void SummaryHostSizeChanged(object sender, SizeChangedEventArgs e) => ApplyColumns();

    private async void ReloadClick(object sender, RoutedEventArgs e) => await LoadAsync(++_version);
    private async void PrevClick(object sender, RoutedEventArgs e)
    {
        if (_page > 1)
        {
            _page--;
            await LoadAsync(++_version);
        }
    }

    private async void NextClick(object sender, RoutedEventArgs e)
    {
        if (_page < _totalPages)
        {
            _page++;
            await LoadAsync(++_version);
        }
    }

    private async Task LoadAsync(int version)
    {
        var module = Modules[_key];
        StatusText.Text = "正在加载…";
        try
        {
            var query = new Dictionary<string, string?> { ["page"] = _page.ToString(), ["pageSize"] = "20" };
            if (_key == "admin:dashboard")
            {
                query["period"] = _period;
            }

            if (!string.IsNullOrWhiteSpace(SearchBox.Text) && _key is "admin:users" or "admin:topics" or "admin:questions")
            {
                query["search"] = SearchBox.Text.Trim();
            }

            var data = await AppServices.Api.GetAsync(module.ListPath, query);
            if (version != _version)
            {
                return;
            }

            _totalPages = Math.Max(1, JsonRead.Int(data, "totalPages", 1));
            PageText.Text = $"{_page} / {_totalPages}";
            var items = JsonRead.Items(data).ToList();
            if (module.Summary)
            {
                RenderSummary(data);
                return;
            }

            if (items.Count == 0)
            {
                Table.ItemsSource = null;
                Table.Columns.Clear();
                JsonBox.Text = "";
                StatusText.Text = "没有数据。";
                return;
            }

            var columns = module.Columns.Length > 0
                ? module.Columns
                : items[0].EnumerateObject().Where(p => p.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False).Select(p => p.Name).Take(6).ToArray();
            var rows = items.Select(item =>
            {
                var cells = new Dictionary<string, string>();
                foreach (var column in columns)
                {
                    cells[column] = CellText(item, column);
                }

                return new Row(item, cells);
            }).ToList();
            Table.Columns.Clear();
            foreach (var column in columns)
            {
                Table.Columns.Add(new DataGridTextColumn
                {
                    Header = Headers.TryGetValue(column, out var header) ? header : column,
                    Binding = new Binding($"Cells[{column}]"),
                    Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                });
            }

            Table.ItemsSource = rows;
            StatusText.Text = $"共 {rows.Count} 条。选中一行后，在右侧修改 JSON 再保存。";
            if (rows.Count > 0)
            {
                Table.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
            if (version == _version)
            {
                StatusText.Text = ex.Message;
            }
        }
    }

    private void TableChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Table.SelectedItem is Row row)
        {
            _selected = row.Element;
            JsonBox.Text = JsonRead.Pretty(row.Element);
        }
    }

    private async void CreateClick(object sender, RoutedEventArgs e)
    {
        var module = Modules[_key];
        var json = Dialogs.Prompt("按 JSON 新建。字段名保持和接口一致。", "新建", module.Template ?? "{}");
        if (json == null || module.CreatePath == null)
        {
            return;
        }

        try
        {
            await AppServices.Api.PostAsync(module.CreatePath, json);
            StatusText.Text = "已新建。";
            await LoadAsync(++_version);
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private async void SaveClick(object sender, RoutedEventArgs e)
    {
        var module = Modules[_key];
        if (module.ItemPath == null || _selected == null)
        {
            StatusText.Text = "先选择一条记录。";
            return;
        }

        var id = IdOf(_selected.Value);
        if (string.IsNullOrWhiteSpace(id))
        {
            StatusText.Text = "这条记录没有 id。";
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(JsonBox.Text);
            await AppServices.Api.PutAsync(module.ItemPath.Replace("{id}", Uri.EscapeDataString(id)), doc.RootElement.Clone());
            StatusText.Text = "已保存。";
            await LoadAsync(++_version);
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private async void DeleteClick(object sender, RoutedEventArgs e)
    {
        var module = Modules[_key];
        if (module.ItemPath == null || _selected == null || !Dialogs.Confirm("删除后不能恢复。", "删除记录", "删除", danger: true))
        {
            return;
        }

        var id = IdOf(_selected.Value);
        try
        {
            await AppServices.Api.DeleteAsync(module.ItemPath.Replace("{id}", Uri.EscapeDataString(id)));
            StatusText.Text = "已删除。";
            await LoadAsync(++_version);
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private async void ExtraClick(object sender, RoutedEventArgs e)
    {
        if (_selected == null && _key != "admin:topics")
        {
            StatusText.Text = "先选择一条记录。";
            return;
        }

        try
        {
            if (_key == "admin:users" && _selected != null)
            {
                var id = IdOf(_selected.Value);
                var banned = JsonRead.Bool(_selected.Value, "isBanned");
                if (banned)
                {
                    await AppServices.Api.PutAsync($"/admin/users/{id}/unban", new { note = "管理员解封" });
                }
                else
                {
                    var reason = Dialogs.Prompt("填写封禁原因。", "封禁", "违反社区规范");
                    if (reason == null)
                    {
                        return;
                    }

                    await AppServices.Api.PutAsync($"/admin/users/{id}/ban", new { reason });
                }
            }
            else if (_key == "admin:providers" && _selected != null)
            {
                var id = IdOf(_selected.Value);
                var enabled = !JsonRead.Bool(_selected.Value, "isEnabled");
                await AppServices.Api.PutAsync($"/admin/providers/{id}/toggle", new { isEnabled = enabled });
            }
            else if (_key == "admin:topics")
            {
                var choice = Dialogs.Choose("写作题库", "从表格文件导入，或让 AI 生成新题目。", "从文件导入", "AI 生成");
                if (choice == 0)
                {
                    return;
                }

                if (choice == 1)
                {
                    var dialog = new OpenFileDialog { Filter = "表格|*.xlsx;*.csv|所有文件|*.*" };
                    if (dialog.ShowDialog() != true)
                    {
                        return;
                    }

                    await AppServices.Api.UploadAsync("/admin/writing-topics/import", dialog.FileName);
                }
                else
                {
                    var count = Dialogs.Prompt("生成数量：1、5、10 或 20。", "AI 出题", "5");
                    if (count == null)
                    {
                        return;
                    }

                    await AppServices.Api.PostAsync("/admin/writing-topics/generate", new { count = int.Parse(count) });
                }
            }

            StatusText.Text = "操作已提交。";
            await LoadAsync(++_version);
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private void RenderSummary(JsonElement data)
    {
        SummaryBody.Children.Clear();
        _responsive.Clear();
        try
        {
            if (_key == "admin:dashboard" && JsonRead.Prop(data, "users").ValueKind == JsonValueKind.Object)
            {
                RenderDashboard(data);
            }
            else if (_key == "admin:system" && JsonRead.Prop(data, "process").ValueKind == JsonValueKind.Object)
            {
                RenderSystem(data);
            }
            else
            {
                RenderRaw(data);
            }
        }
        catch (Exception ex)
        {
            SummaryBody.Children.Clear();
            _responsive.Clear();
            RenderRaw(data);
            StatusText.Text = ex.Message;
            return;
        }

        ApplyColumns();
        var when = When(data);
        StatusText.Text = string.IsNullOrWhiteSpace(when) ? "汇总已更新。" : $"更新于 {when}";
    }

    private void RenderDashboard(JsonElement data)
    {
        var users = JsonRead.Prop(data, "users");
        var writing = JsonRead.Prop(data, "writing");
        var activity = JsonRead.Prop(data, "activity");
        var tokens = JsonRead.Prop(data, "tokenUsage");
        var vocab = JsonRead.Prop(data, "vocabulary");
        var checkIn = JsonRead.Prop(data, "checkIn");
        var stats = Track(new UniformGrid(), 4);
        stats.Children.Add(Stat("注册用户", Num(users, "total"), $"今日新增 {Num(users, "todayNew")} · 周期新增 {Num(users, "periodNew")}"));
        stats.Children.Add(Stat("写作提交", Num(writing, "totalWritings"), $"今日 {Num(writing, "todayWritings")} · 周期 {Num(writing, "periodWritings")}"));
        stats.Children.Add(Stat("今日活跃", Num(activity, "todayDau"), $"登录 {Num(activity, "todayLogins")} · 会话 {Num(activity, "activeSessions")}"));
        stats.Children.Add(Stat("周期 Token", Num(tokens, "periodTokens"), $"本月 {Num(tokens, "monthTokens")}"));
        stats.Children.Add(Stat("总字数", Num(writing, "totalWords"), $"平均分 {Num(writing, "averageScore")}"));
        stats.Children.Add(Stat("词库词条", Num(vocab, "total"), $"周期新增 {Num(vocab, "periodAdded")}"));
        stats.Children.Add(Stat("今日打卡", Num(checkIn, "todayCheckIns"), ""));
        stats.Children.Add(Stat("封禁用户", Num(users, "banned"), ""));
        SummaryBody.Children.Add(stats);

        var trends = JsonRead.Prop(data, "trends");
        var trendCard = new StackPanel();
        trendCard.Children.Add(Heading("运营趋势"));
        trendCard.Children.Add(Muted("注册、提交、打卡、活跃和 Token，按日期排列。"));
        var registration = ArrayOf(trends, "registration");
        var submits = ArrayOf(trends, "submits");
        var checkIns = ArrayOf(trends, "checkIns");
        var dau = ArrayOf(trends, "dau");
        var tokenTrend = ArrayOf(trends, "tokens");
        if (registration.Count == 0)
        {
            trendCard.Children.Add(Muted("这个时间范围内还没有趋势数据。"));
        }
        else
        {
            trendCard.Children.Add(TrendHeader());
            for (var i = 0; i < registration.Count; i++)
            {
                trendCard.Children.Add(TrendRow(
                    ShortDate(JsonRead.Str(registration[i], "date")),
                    Num(registration[i], "count"),
                    At(submits, i, "count"),
                    At(checkIns, i, "count"),
                    At(dau, i, "count"),
                    At(tokenTrend, i, "tokens")));
            }
        }

        SummaryBody.Children.Add(Card(trendCard));

        var splits = Track(new UniformGrid(), 2);
        splits.Children.Add(Distribution("Token 用途", ArrayOf(tokens, "byPurpose"), "purpose", "tokens", "calls"));
        splits.Children.Add(Distribution("Token 供应商", ArrayOf(tokens, "byProvider"), "providerId", "tokens", "calls"));
        SummaryBody.Children.Add(splits);

        var thirds = Track(new UniformGrid(), 3);
        thirds.Children.Add(Distribution("会员分布", ArrayOf(users, "vipDistribution"), "vipLevel", "count", null));
        thirds.Children.Add(Distribution("题目类型", ArrayOf(writing, "topicDistribution"), "type", "count", null));
        thirds.Children.Add(Distribution("分数分布", ObjectPairs(JsonRead.Prop(writing, "scoreDistribution")), "name", "value", null));
        SummaryBody.Children.Add(thirds);
    }

    private void RenderSystem(JsonElement data)
    {
        var process = JsonRead.Prop(data, "process");
        var memory = JsonRead.Prop(data, "memory");
        var cpu = JsonRead.Prop(data, "cpu");
        var gc = JsonRead.Prop(memory, "gc");
        var health = JsonRead.Prop(data, "health");
        var database = JsonRead.Str(health, "database");
        var stats = Track(new UniformGrid(), 4);
        stats.Children.Add(Stat("数据库", database == "healthy" ? "健康" : "异常", string.IsNullOrWhiteSpace(database) ? "" : database));
        stats.Children.Add(Stat("进程 CPU", Num(cpu, "processUsagePercent") + "%", $"{Num(cpu, "logicalCores")} 逻辑核心"));
        stats.Children.Add(Stat("工作集内存", Num(memory, "workingSetMb") + " MB", $"托管 {Num(memory, "managedMemoryMb")} MB"));
        stats.Children.Add(Stat("运行时长", Uptime(process), $"PID {Num(process, "pid")} · {Num(process, "threadCount")} 线程"));
        SummaryBody.Children.Add(stats);

        var halves = Track(new UniformGrid(), 2);
        var processCard = new StackPanel();
        processCard.Children.Add(Heading("进程"));
        processCard.Children.Add(MetricGrid(
            ("进程名", JsonRead.Str(process, "name")),
            ("线程数", Num(process, "threadCount")),
            ("启动时间", When(process, "startTimeUtc")),
            ("运行秒数", Num(process, "uptimeSeconds"))));
        halves.Children.Add(Card(processCard, false));
        var memoryCard = new StackPanel();
        memoryCard.Children.Add(Heading("内存 / GC"));
        memoryCard.Children.Add(MetricGrid(
            ("Working Set", Num(memory, "workingSetMb") + " MB"),
            ("Private Memory", Num(memory, "privateMemoryMb") + " MB"),
            ("Managed Heap", Num(memory, "managedMemoryMb") + " MB"),
            ("GC gen0/1/2", $"{Num(gc, "gen0Collections")}/{Num(gc, "gen1Collections")}/{Num(gc, "gen2Collections")}"),
            ("Server GC", JsonRead.Bool(gc, "isServerGC") ? "是" : "否")));
        halves.Children.Add(Card(memoryCard, false));
        SummaryBody.Children.Add(halves);

        var disks = new StackPanel();
        disks.Children.Add(Heading("磁盘"));
        var diskItems = ArrayOf(data, "disks");
        if (diskItems.Count == 0)
        {
            disks.Children.Add(Muted("无法读取磁盘信息。"));
        }

        foreach (var disk in diskItems)
        {
            disks.Children.Add(DiskRow(disk));
        }

        SummaryBody.Children.Add(Card(disks));

        var runtime = JsonRead.Prop(data, "runtime");
        var runtimeCard = new StackPanel();
        runtimeCard.Children.Add(Heading("运行环境"));
        runtimeCard.Children.Add(MetricGrid(
            ("主机", JsonRead.Str(runtime, "machineName")),
            ("操作系统", JsonRead.Str(runtime, "osDescription")),
            ("OS 架构", JsonRead.Str(runtime, "osArchitecture")),
            ("进程架构", JsonRead.Str(runtime, "processArchitecture")),
            ("运行时", JsonRead.Str(runtime, "frameworkDescription")),
            ("系统时间", When(runtime, "systemUtcNow"))));
        SummaryBody.Children.Add(Card(runtimeCard));
    }

    private void RenderRaw(JsonElement data)
    {
        var box = new TextBox
        {
            Style = (Style)FindResource("FieldBox"),
            Text = JsonRead.Pretty(data),
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 280,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        SummaryBody.Children.Add(Card(box));
    }

    private UniformGrid Track(UniformGrid grid, int columns)
    {
        _responsive.Add((grid, columns));
        grid.Columns = columns;
        return grid;
    }

    private void ApplyColumns()
    {
        var width = SummaryHost.ActualWidth;
        if (width <= 0)
        {
            width = 960;
        }

        foreach (var (grid, columns) in _responsive)
        {
            var count = columns;
            if (width < 680)
            {
                count = 1;
            }
            else if (width < 980 && columns > 2)
            {
                count = 2;
            }

            grid.Columns = Math.Max(1, count);
        }
    }

    private Border Stat(string label, string value, string hint)
    {
        var stack = new StackPanel();
        stack.Children.Add(Muted(label));
        stack.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(value) ? "—" : value,
            FontFamily = new FontFamily("Georgia"),
            FontSize = 28,
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("TextBrush"),
        });
        if (!string.IsNullOrWhiteSpace(hint))
        {
            stack.Children.Add(Muted(hint));
        }

        return Card(stack);
    }

    private Border Distribution(string title, IReadOnlyList<JsonElement> items, string labelKey, string valueKey, string? extraKey)
    {
        var stack = new StackPanel();
        stack.Children.Add(Heading(title));
        if (items.Count == 0)
        {
            stack.Children.Add(Muted("暂无数据。"));
            return Card(stack, false);
        }

        foreach (var item in items)
        {
            var label = JsonRead.Str(item, labelKey);
            if (labelKey == "vipLevel")
            {
                label = label is "" or "0" ? "普通" : "VIP " + label;
            }

            var value = Num(item, valueKey);
            if (extraKey != null)
            {
                value += $" · {Num(item, extraKey)} 次";
            }

            stack.Children.Add(Pair(string.IsNullOrWhiteSpace(label) ? "未知" : label, value));
        }

        return Card(stack, false);
    }

    private static List<JsonElement> ObjectPairs(JsonElement element)
    {
        var list = new List<JsonElement>();
        if (element.ValueKind != JsonValueKind.Object)
        {
            return list;
        }

        foreach (var property in element.EnumerateObject())
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(new { name = property.Name, value = property.Value.ToString() }));
            list.Add(doc.RootElement.Clone());
        }

        return list;
    }

    private UIElement TrendHeader()
    {
        return TrendRow("日期", "注册", "提交", "打卡", "活跃", "Token", muted: true);
    }

    private UIElement TrendRow(string date, string register, string submit, string checkIn, string dau, string tokens, bool muted = false)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        for (var i = 0; i < 6; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        var values = new[] { date, register, submit, checkIn, dau, tokens };
        for (var i = 0; i < values.Length; i++)
        {
            var text = new TextBlock
            {
                Text = values[i],
                FontSize = 13,
                FontWeight = muted ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = (Brush)FindResource(muted ? "MutedBrush" : "TextBrush"),
            };
            Grid.SetColumn(text, i);
            grid.Children.Add(text);
        }

        return grid;
    }

    private UIElement DiskRow(JsonElement disk)
    {
        var used = ParseNumber(JsonRead.Str(disk, "usedPercent"));
        used = Math.Clamp(used, 0, 100);
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        stack.Children.Add(Pair(
            $"{JsonRead.Str(disk, "name")}  {JsonRead.Str(disk, "driveType")} · {JsonRead.Str(disk, "format")}",
            $"{Num(disk, "usedGb")} / {Num(disk, "totalGb")} GB · {Num(disk, "usedPercent")}%"));
        var bar = new Grid { Height = 8, Margin = new Thickness(0, 4, 0, 4) };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(used, 0.01), GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(100 - used, 0.01), GridUnitType.Star) });
        var fill = new Border
        {
            Background = (Brush)FindResource("PrimaryBrush"),
            CornerRadius = new CornerRadius(4, used >= 99.5 ? 4 : 0, used >= 99.5 ? 4 : 0, 4),
        };
        var rest = new Border
        {
            Background = (Brush)FindResource("SubtleBrush"),
            CornerRadius = new CornerRadius(used <= 0.5 ? 4 : 0, 4, 4, used <= 0.5 ? 4 : 0),
        };
        Grid.SetColumn(rest, 1);
        bar.Children.Add(fill);
        bar.Children.Add(rest);
        stack.Children.Add(bar);
        stack.Children.Add(Muted($"剩余 {Num(disk, "freeGb")} GB"));
        return stack;
    }

    private UniformGrid MetricGrid(params (string Label, string Value)[] items)
    {
        var grid = Track(new UniformGrid(), 2);
        foreach (var (label, value) in items)
        {
            var tile = new Border
            {
                Background = (Brush)FindResource("SubtleBrush"),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 10, 10),
            };
            var stack = new StackPanel();
            stack.Children.Add(Muted(label));
            stack.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(value) ? "—" : value,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14,
                FontWeight = FontWeights.Medium,
                Foreground = (Brush)FindResource("TextBrush"),
            });
            tile.Child = stack;
            grid.Children.Add(tile);
        }

        return grid;
    }

    private UIElement Pair(string label, string value)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new TextBlock
        {
            Text = label,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            Foreground = (Brush)FindResource("TextBrush"),
        };
        var right = new TextBlock
        {
            Text = value,
            Margin = new Thickness(16, 0, 0, 0),
            FontSize = 13,
            Foreground = (Brush)FindResource("MutedBrush"),
        };
        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);
        return grid;
    }

    private Border Card(UIElement child, bool gap = true) => new()
    {
        Style = (Style)FindResource("Card"),
        Margin = new Thickness(0, 0, gap ? 12 : 12, 12),
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch,
        Child = child,
    };

    private TextBlock Heading(string text) => new()
    {
        Text = text,
        FontSize = 16,
        FontWeight = FontWeights.Medium,
        Margin = new Thickness(0, 0, 0, 10),
        Foreground = (Brush)FindResource("TextBrush"),
    };

    private TextBlock Muted(string text) => new()
    {
        Text = text,
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 6),
        Foreground = (Brush)FindResource("MutedBrush"),
    };

    private static string Num(JsonElement element, string name)
    {
        var text = JsonRead.Str(element, name);
        return string.IsNullOrWhiteSpace(text) ? "—" : text;
    }

    private static string At(IReadOnlyList<JsonElement> items, int index, string name)
    {
        return index >= 0 && index < items.Count ? Num(items[index], name) : "—";
    }

    private static List<JsonElement> ArrayOf(JsonElement element, string name)
    {
        var value = JsonRead.Prop(element, name);
        if (value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value.EnumerateArray().Select(item => item.Clone()).ToList();
    }

    private static string ShortDate(string date) => date.Length >= 10 ? date[5..10] : date;

    private static string Uptime(JsonElement process)
    {
        var seconds = JsonRead.Int(process, "uptimeSeconds");
        if (seconds <= 0)
        {
            return "—";
        }

        var days = seconds / 86400;
        var hours = seconds % 86400 / 3600;
        var minutes = seconds % 3600 / 60;
        if (days > 0)
        {
            return $"{days} 天 {hours} 小时";
        }

        return hours > 0 ? $"{hours} 小时 {minutes} 分" : $"{minutes} 分钟";
    }

    private static string When(JsonElement element, string name)
    {
        return FormatTime(JsonRead.Str(element, name));
    }

    private static string When(JsonElement element) => FormatTime(JsonRead.Str(element, "generatedAt"));

    private static string FormatTime(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "";
        }

        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var time)
            ? time.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : raw;
    }

    private static double ParseNumber(string raw)
    {
        return double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var number) ? number : 0;
    }

    private static string CellText(JsonElement item, string column)
    {
        if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty(column, out var value))
        {
            if (value.ValueKind == JsonValueKind.True)
            {
                return "是";
            }

            if (value.ValueKind == JsonValueKind.False)
            {
                return "否";
            }
        }

        var text = JsonRead.Str(item, column);
        return column.EndsWith("At", StringComparison.Ordinal) ? FormatTime(text) : text;
    }

    private static string IdOf(JsonElement element)
    {
        var id = JsonRead.Str(element, "id");
        return string.IsNullOrWhiteSpace(id) ? JsonRead.Str(element, "key") : id;
    }

    private sealed record Module(string Title, string ListPath, string? CreatePath, string? ItemPath, bool Summary, string[] Columns, string? Template);

    private sealed class Row(JsonElement element, Dictionary<string, string> cells)
    {
        public JsonElement Element { get; } = element;
        public Dictionary<string, string> Cells { get; } = cells;
    }
}
