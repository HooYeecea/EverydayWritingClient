using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using EverydayWritingClient.Services;

namespace EverydayWritingClient.Views;

public partial class VocabularyView : UserControl, IRefreshOnShow
{
    private int _page = 1;
    private int _totalPages = 1;
    private string? _keyword;
    private string? _editingId;
    private string _wrongUsage = "";
    private bool _filling;

    public VocabularyView()
    {
        InitializeComponent();
        TypeBox.Items.Add(new VocabType("NewWord", "生词"));
        TypeBox.Items.Add(new VocabType("WrongWord", "错词"));
        TypeBox.SelectedIndex = 0;
    }

    public Task RefreshAsync() => LoadAsync();

    private async void SearchClick(object sender, RoutedEventArgs e)
    {
        _page = 1;
        _keyword = SearchBox.Text.Trim();
        await LoadAsync();
    }

    private async void PrevClick(object sender, RoutedEventArgs e)
    {
        if (_page <= 1)
        {
            return;
        }

        _page--;
        await LoadAsync();
    }

    private async void NextClick(object sender, RoutedEventArgs e)
    {
        if (_page >= _totalPages)
        {
            return;
        }

        _page++;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            SetStatus("");
            JsonElement data;
            if (string.IsNullOrWhiteSpace(_keyword))
            {
                data = await AppServices.Api.GetAsync("/vocabulary", new Dictionary<string, string?>
                {
                    ["page"] = _page.ToString(),
                    ["pageSize"] = "20",
                });
                _totalPages = Math.Max(1, JsonRead.Int(data, "totalPages", 1));
                PageText.Text = $"{_page} / {_totalPages}";
            }
            else
            {
                data = await AppServices.Api.GetAsync("/vocabulary/search", new Dictionary<string, string?>
                {
                    ["q"] = _keyword,
                    ["limit"] = "100",
                });
                _totalPages = 1;
                _page = 1;
                PageText.Text = $"搜索到 {JsonRead.Int(data, "totalCount")} 条";
            }

            _filling = true;
            List.Items.Clear();
            foreach (var item in JsonRead.Items(data))
            {
                var translations = JsonRead.Prop(item, "translations");
                var meaning = translations.ValueKind == JsonValueKind.Array
                    ? string.Join("；", JsonRead.StringList(translations))
                    : JsonRead.Str(item, "translation");
                List.Items.Add(new WordItem(
                    JsonRead.Str(item, "id"),
                    JsonRead.Str(item, "word"),
                    JsonRead.Str(item, "partOfSpeech"),
                    meaning,
                    JsonRead.Str(item, "contextSentence"),
                    JsonRead.Str(item, "type"),
                    JsonRead.Str(item, "wrongUsage")));
            }

            _filling = false;
            List.SelectedIndex = -1;
            ClearForm();
        }
        catch (Exception ex)
        {
            _filling = false;
            SetStatus(ex.Message, danger: true);
        }
    }

    private void ListChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || List.SelectedItem is not WordItem item)
        {
            return;
        }

        _editingId = item.Id;
        _wrongUsage = item.WrongUsage;
        WordBox.Text = item.Word;
        PosBox.Text = item.PartOfSpeech;
        TranslationBox.Text = item.Translation;
        SentenceBox.Text = item.Sentence;
        TypeBox.SelectedIndex = item.Type.Equals("WrongWord", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        FormTitle.Text = "编辑词条";
        SaveButton.Content = "保存修改";
        SetStatus("接口没有单独的修改地址，保存时会用新内容替换原来这一条。");
    }

    private async void SaveClick(object sender, RoutedEventArgs e)
    {
        var word = WordBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(word))
        {
            SetStatus("先填写单词。", danger: true);
            return;
        }

        var payload = new
        {
            word,
            partOfSpeech = PosBox.Text.Trim(),
            translation = TranslationBox.Text.Trim(),
            contextSentence = SentenceBox.Text.Trim(),
            wrongUsage = _wrongUsage,
            type = (TypeBox.SelectedItem as VocabType)?.Value ?? "NewWord",
        };

        try
        {
            var editingId = _editingId;
            await AppServices.Api.PostAsync("/vocabulary", payload);
            if (!string.IsNullOrWhiteSpace(editingId))
            {
                await AppServices.Api.DeleteAsync($"/vocabulary/{editingId}");
            }

            ClearForm();
            await LoadAsync();
            if (Application.Current.MainWindow is MainWindow shell)
            {
                shell.InvalidatePage("user", "assessment");
            }

            SetStatus(string.IsNullOrWhiteSpace(editingId) ? "已加入词库。" : "已更新这条词。");
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, danger: true);
        }
    }

    private async void DeleteClick(object sender, RoutedEventArgs e)
    {
        if (List.SelectedItem is not WordItem row)
        {
            SetStatus("先选择一条词。", danger: true);
            return;
        }

        if (!Dialogs.Confirm("从词库里删除这个词？", "删除词条", "删除", danger: true))
        {
            return;
        }

        try
        {
            await AppServices.Api.DeleteAsync($"/vocabulary/{row.Id}");
            ClearForm();
            await LoadAsync();
            if (Application.Current.MainWindow is MainWindow shell)
            {
                shell.InvalidatePage("user", "assessment");
            }

            SetStatus("已删除。");
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, danger: true);
        }
    }

    private void ClearClick(object sender, RoutedEventArgs e)
    {
        List.SelectedIndex = -1;
        ClearForm();
    }

    private void ClearForm()
    {
        _editingId = null;
        _wrongUsage = "";
        WordBox.Clear();
        PosBox.Text = "n.";
        TranslationBox.Clear();
        SentenceBox.Clear();
        TypeBox.SelectedIndex = 0;
        FormTitle.Text = "新增词条";
        SaveButton.Content = "加入词库";
    }

    private void SetStatus(string text, bool danger = false)
    {
        StatusText.Text = text;
        StatusText.Foreground = (System.Windows.Media.Brush)FindResource(danger ? "DangerBrush" : "MutedBrush");
    }

    private sealed record VocabType(string Value, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record WordItem(
        string Id,
        string Word,
        string PartOfSpeech,
        string Translation,
        string Sentence,
        string Type,
        string WrongUsage)
    {
        public override string ToString()
        {
            var kind = Type.Equals("WrongWord", StringComparison.OrdinalIgnoreCase) ? "错词" : "生词";
            return $"{Word}  {PartOfSpeech}  {Translation}  · {kind}";
        }
    }
}
