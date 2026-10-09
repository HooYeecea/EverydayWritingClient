using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using EverydayWritingClient.Services;

namespace EverydayWritingClient.Views;

public partial class RecordsView : UserControl, IRefreshOnShow
{
    private bool _drafts = true;
    private string _selectedId = "";
    private string _suggestionId = "";

    public RecordsView()
    {
        InitializeComponent();
    }

    public Task RefreshAsync() => LoadAsync();

    private async void ReloadClick(object sender, RoutedEventArgs e) => await LoadAsync();
    private async void ShowDraftsClick(object sender, RoutedEventArgs e)
    {
        _drafts = true;
        DraftTab.Style = (Style)FindResource("PrimaryButton");
        SubmitTab.Style = (Style)FindResource("GhostButton");
        await LoadAsync();
    }

    private async void ShowSubmitsClick(object sender, RoutedEventArgs e)
    {
        _drafts = false;
        SubmitTab.Style = (Style)FindResource("PrimaryButton");
        DraftTab.Style = (Style)FindResource("GhostButton");
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (!AppServices.Session.IsAuthenticated)
        {
            StatusText.Text = "请先登录。";
            return;
        }

        try
        {
            StatusText.Text = "";
            var query = new Dictionary<string, string?> { ["page"] = "1", ["pageSize"] = "30" };
            if (!_drafts && !string.IsNullOrWhiteSpace(KeywordBox.Text))
            {
                query["keyword"] = KeywordBox.Text.Trim();
            }

            var path = _drafts ? "/writings/drafts" : "/writings/submits";
            var data = await AppServices.Api.GetAsync(path, query);
            List.Items.Clear();
            foreach (var item in JsonRead.Items(data))
            {
                var title = JsonRead.Str(item, "title");
                if (string.IsNullOrWhiteSpace(title))
                {
                    title = "未命名";
                }

                var when = JsonRead.Str(item, _drafts ? "updatedAt" : "submittedAt");
                List.Items.Add(new ListRow(JsonRead.Str(item, "id"), $"{title}    {when}"));
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private async void ListChanged(object sender, SelectionChangedEventArgs e)
    {
        if (List.SelectedItem is not ListRow row)
        {
            return;
        }

        _selectedId = row.Id;
        _suggestionId = "";
        try
        {
            var data = await AppServices.Api.GetAsync(_drafts ? $"/writings/drafts/{row.Id}" : $"/writings/submits/{row.Id}");
            DetailTitle.Text = JsonRead.Str(data, "title");
            DetailMeta.Text = $"{JsonRead.Str(data, "topicType")}  {JsonRead.Str(data, "topic")}".Trim();
            DetailBody.Text = JsonRead.PlainText(JsonRead.Str(data, "content"));
            AiBody.Text = BuildAi(data);
            var suggestions = JsonRead.Prop(data, "grammarSuggestions");
            if (suggestions.ValueKind == JsonValueKind.Array)
            {
                foreach (var suggestion in suggestions.EnumerateArray())
                {
                    var id = JsonRead.Str(suggestion, "id");
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        _suggestionId = id;
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private static string BuildAi(JsonElement data)
    {
        var parts = new List<string>();
        var score = JsonRead.Str(data, "aiScore");
        if (!string.IsNullOrWhiteSpace(score))
        {
            parts.Add($"评分 {score}");
        }

        var evaluation = JsonRead.Str(data, "aiEvaluation");
        if (!string.IsNullOrWhiteSpace(evaluation))
        {
            parts.Add(evaluation);
        }

        var results = JsonRead.Prop(data, "aiResults");
        if (results.ValueKind == JsonValueKind.Array)
        {
            foreach (var result in results.EnumerateArray())
            {
                parts.Add($"【{JsonRead.Str(result, "purpose")}】\n{JsonRead.Str(result, "resultContent")}");
            }
        }

        return parts.Count == 0 ? "" : string.Join("\n\n", parts);
    }

    private void ContinueClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedId) || Application.Current.MainWindow is not MainWindow shell)
        {
            return;
        }

        if (_drafts)
        {
            shell.ContinueDraft(_selectedId);
        }
        else
        {
            shell.ContinueSubmit(_selectedId);
        }
    }

    private async void DeleteClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedId) || !Dialogs.Confirm("删除后不能恢复。", "删除记录", "删除", danger: true))
        {
            return;
        }

        try
        {
            await AppServices.Api.DeleteAsync(_drafts ? $"/writings/drafts/{_selectedId}" : $"/writings/submits/{_selectedId}");
            await LoadAsync();
            DetailTitle.Text = "";
            DetailBody.Text = "";
            AiBody.Text = "";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private async void ChatClick(object sender, RoutedEventArgs e)
    {
        if (_drafts || string.IsNullOrWhiteSpace(_selectedId) || string.IsNullOrWhiteSpace(_suggestionId))
        {
            StatusText.Text = "当前记录没有可追问的语法建议。";
            return;
        }

        if (string.IsNullOrWhiteSpace(ChatBox.Text))
        {
            return;
        }

        try
        {
            var data = await AppServices.Api.PostAsync($"/writings/submits/{_selectedId}/suggestions/{_suggestionId}/chat", new
            {
                question = ChatBox.Text.Trim(),
            });
            ChatBody.Text = JsonRead.Pretty(data);
            ChatBox.Clear();
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private sealed record ListRow(string Id, string Label)
    {
        public override string ToString() => Label;
    }
}
