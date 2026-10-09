using System.Windows;
using System.Windows.Controls;
using EverydayWritingClient.Services;

namespace EverydayWritingClient.Views;

public partial class VocabularyView : UserControl, IRefreshOnShow
{
    public VocabularyView()
    {
        InitializeComponent();
        TypeBox.Items.Add("生词 NewWord");
        TypeBox.Items.Add("错词 WrongWord");
        TypeBox.SelectedIndex = 0;
    }

    public Task RefreshAsync() => LoadAsync(null);

    private async void SearchClick(object sender, RoutedEventArgs e) => await LoadAsync(SearchBox.Text.Trim());

    private async Task LoadAsync(string? keyword)
    {
        try
        {
            StatusText.Text = "";
            var data = string.IsNullOrWhiteSpace(keyword)
                ? await AppServices.Api.GetAsync("/vocabulary", new Dictionary<string, string?> { ["page"] = "1", ["pageSize"] = "50" })
                : await AppServices.Api.GetAsync("/vocabulary/search", new Dictionary<string, string?> { ["q"] = keyword, ["limit"] = "50" });
            List.Items.Clear();
            foreach (var item in JsonRead.Items(data))
            {
                var translations = JsonRead.Prop(item, "translations");
                var meaning = translations.ValueKind == System.Text.Json.JsonValueKind.Array
                    ? string.Join("；", JsonRead.StringList(translations))
                    : JsonRead.Str(item, "translation");
                List.Items.Add(new WordRow(JsonRead.Str(item, "id"), $"{JsonRead.Str(item, "word")}  {JsonRead.Str(item, "partOfSpeech")}  {meaning}"));
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private async void AddClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await AppServices.Api.PostAsync("/vocabulary", new
            {
                word = WordBox.Text.Trim(),
                partOfSpeech = PosBox.Text.Trim(),
                translation = TranslationBox.Text.Trim(),
                contextSentence = SentenceBox.Text.Trim(),
                type = TypeBox.SelectedIndex == 1 ? "WrongWord" : "NewWord",
            });
            WordBox.Clear();
            TranslationBox.Clear();
            SentenceBox.Clear();
            await LoadAsync(null);
            StatusText.Text = "已加入词库。";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private async void DeleteClick(object sender, RoutedEventArgs e)
    {
        if (List.SelectedItem is not WordRow row)
        {
            StatusText.Text = "先选择一条词。";
            return;
        }

        try
        {
            await AppServices.Api.DeleteAsync($"/vocabulary/{row.Id}");
            await LoadAsync(null);
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private sealed record WordRow(string Id, string Label)
    {
        public override string ToString() => Label;
    }
}
