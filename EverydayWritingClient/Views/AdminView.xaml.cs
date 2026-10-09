using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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

    private string _key = "";
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
        JsonBox.Text = module.Summary ? "" : module.Template ?? "";
        _ = LoadAsync(++_version);
    }

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
                query["period"] = "7d";
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
            if (module.Summary || items.Count == 0)
            {
                Table.ItemsSource = null;
                Table.Columns.Clear();
                JsonBox.Text = JsonRead.Pretty(data);
                StatusText.Text = module.Summary ? "这是汇总数据，可以直接阅读右侧内容。" : "没有数据。";
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
                    cells[column] = JsonRead.Str(item, column);
                }

                return new Row(item, cells);
            }).ToList();
            Table.Columns.Clear();
            foreach (var column in columns)
            {
                Table.Columns.Add(new DataGridTextColumn
                {
                    Header = column,
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
        if (module.ItemPath == null || _selected == null || !Dialogs.Confirm("确定删除这条记录吗？"))
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
                var choice = MessageBox.Show("选“是”从文件导入，选“否”让 AI 生成题目。", "写作题库", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (choice == MessageBoxResult.Cancel)
                {
                    return;
                }

                if (choice == MessageBoxResult.Yes)
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
