using System.Windows;
using System.Windows.Controls;
using EverydayWritingClient.Services;

namespace EverydayWritingClient.Views;

public partial class AssessmentView : UserControl, IRefreshOnShow
{
    public AssessmentView()
    {
        InitializeComponent();
    }

    public Task RefreshAsync() => LoadAsync("all");

    private async void PeriodClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            await LoadAsync(button.Tag as string ?? "all");
        }
    }

    private async Task LoadAsync(string period)
    {
        try
        {
            StatusText.Text = "";
            var data = await AppServices.Api.GetAsync("/assessment/stats", new Dictionary<string, string?> { ["period"] = period });
            var summary = JsonRead.Prop(data, "summary");
            Cards.Children.Clear();
            AddCard("篇数", JsonRead.Str(summary, "totalWritings"));
            AddCard("累计字数", JsonRead.Str(summary, "totalWords"));
            AddCard("平均分", JsonRead.Str(summary, "averageScore"));
            AddCard("最高分", JsonRead.Str(summary, "highestScore"));
            AddCard("最低分", JsonRead.Str(summary, "lowestScore"));
            AddCard("词库", JsonRead.Str(summary, "vocabularyCount"));
            var lines = new List<string>();
            foreach (var point in JsonRead.Items(JsonRead.Prop(data, "scoreTrend")))
            {
                lines.Add($"{JsonRead.Str(point, "date")}    {JsonRead.Str(point, "score")}");
            }

            foreach (var topic in JsonRead.Items(JsonRead.Prop(data, "topicDistribution")))
            {
                lines.Add($"{JsonRead.Str(topic, "type")}  {JsonRead.Str(topic, "count")} 篇");
            }

            Body.Text = lines.Count == 0 ? "这个周期还没有足够的提交。" : string.Join("\n", lines);
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private void AddCard(string label, string value)
    {
        var card = new Border { Style = (Style)FindResource("Card"), Margin = new Thickness(0, 0, 10, 10) };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = label, Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush") });
        stack.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(value) ? "—" : value,
            FontFamily = new System.Windows.Media.FontFamily("Georgia"),
            FontSize = 26,
            Margin = new Thickness(0, 6, 0, 0),
            Foreground = (System.Windows.Media.Brush)FindResource("TextBrush"),
        });
        card.Child = stack;
        Cards.Children.Add(card);
    }
}
