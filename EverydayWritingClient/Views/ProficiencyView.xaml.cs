using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using EverydayWritingClient.Services;

namespace EverydayWritingClient.Views;

public partial class ProficiencyView : UserControl, IRefreshOnShow
{
    private string _testId = "";
    private readonly Dictionary<string, ComboBox> _selfAnswers = new();
    private readonly Dictionary<string, TextBox> _objectiveAnswers = new();
    private readonly Dictionary<string, TextBox> _writings = new();

    public ProficiencyView()
    {
        InitializeComponent();
    }

    public Task RefreshAsync() => LoadStatusAsync();

    private async Task LoadStatusAsync()
    {
        Body.Children.Clear();
        try
        {
            var status = await AppServices.Api.GetAsync("/proficiency-test/status");
            var state = JsonRead.Str(status, "status");
            _testId = JsonRead.Str(status, "activeTestId");
            if (state == "completed")
            {
                Heading("测评已完成");
                Paragraph($"等级 {JsonRead.Str(status, "overallLevel")}    分数 {JsonRead.Str(status, "overallScore")}");
                Button("查看个人计划", async () =>
                {
                    if (Application.Current.MainWindow is MainWindow shell)
                    {
                        shell.InvalidatePage("user");
                        shell.Navigate("user");
                    }

                    await Task.CompletedTask;
                });
                Button("整份重测", () => RestartAsync());
                return;
            }

            Heading("英语能力测评");
            Paragraph("大约几分钟：先自评，再做一组客观题，最后写一易一难两篇短文。");
            if (state == "in_progress" && !string.IsNullOrWhiteSpace(_testId))
            {
                Paragraph($"当前进度：{JsonRead.Str(status, "activeStage")}");
                Button("继续测评", () => ContinueAsync(JsonRead.Str(status, "activeStage")));
                Button("放弃并重开", () => RestartAsync());
            }
            else
            {
                Button("开始测评", () => StartAsync());
                Button("稍后再做", async () =>
                {
                    await AppServices.Api.PostAsync("/proficiency-test/skip");
                    if (Application.Current.MainWindow is MainWindow shell)
                    {
                        await shell.RefreshGuideDotAsync();
                        shell.Navigate("writing");
                    }
                });
            }
        }
        catch (Exception ex)
        {
            Paragraph(ex.Message);
        }
    }

    private async Task StartAsync() => ApplyStart(await AppServices.Api.PostAsync("/proficiency-test/start"));

    private async Task RestartAsync() => ApplyStart(await AppServices.Api.PostAsync("/proficiency-test/restart", new { reason = "user_requested" }));

    private void ApplyStart(JsonElement data)
    {
        _testId = JsonRead.Str(data, "testId");
        var questions = JsonRead.Items(JsonRead.Prop(data, "selfAssessmentQuestions")).ToList();
        if (questions.Count == 0)
        {
            _ = LoadSelfQuestionsAsync();
            return;
        }

        ShowSelf(questions);
    }

    private async Task ContinueAsync(string stage)
    {
        if (stage == "C")
        {
            await ShowWritingAsync();
        }
        else if (stage == "B")
        {
            await ShowObjectiveAsync();
        }
        else if (stage == "done")
        {
            await EvaluateAsync();
        }
        else
        {
            await LoadSelfQuestionsAsync();
        }
    }

    private async Task LoadSelfQuestionsAsync()
    {
        var data = await AppServices.Api.GetAsync("/proficiency-test/self-assessment-questions");
        ShowSelf(JsonRead.Items(data).ToList());
    }

    private void ShowSelf(List<JsonElement> questions)
    {
        Body.Children.Clear();
        _selfAnswers.Clear();
        Heading("自评");
        foreach (var question in questions)
        {
            var id = JsonRead.Str(question, "id");
            Paragraph(JsonRead.Str(question, "question"));
            var box = new ComboBox { Style = (Style)FindResource("FieldCombo"), Margin = new Thickness(0, 0, 0, 12) };
            foreach (var option in JsonRead.Items(JsonRead.Prop(question, "options")))
            {
                box.Items.Add(new Option(JsonRead.Str(option, "key"), JsonRead.Str(option, "label")));
            }

            if (box.Items.Count > 0)
            {
                box.SelectedIndex = 0;
            }

            _selfAnswers[id] = box;
            Body.Children.Add(box);
        }

        Button("提交自评", SubmitSelfAsync);
    }

    private async Task SubmitSelfAsync()
    {
        var answers = _selfAnswers.ToDictionary(pair => pair.Key, pair => (pair.Value.SelectedItem as Option)?.Value ?? "");
        await AppServices.Api.PostAsync($"/proficiency-test/{_testId}/self-assessment", new { answers });
        await ShowObjectiveAsync();
    }

    private async Task ShowObjectiveAsync()
    {
        var data = await AppServices.Api.GetAsync($"/proficiency-test/{_testId}/objective-questions");
        Body.Children.Clear();
        _objectiveAnswers.Clear();
        Heading("客观题");
        foreach (var question in JsonRead.Items(data))
        {
            var id = JsonRead.Str(question, "id");
            var content = JsonRead.Prop(question, "content");
            var prompt = content.ValueKind == JsonValueKind.String
                ? content.GetString() ?? ""
                : FirstText(content, "prompt", "sentence", "instruction", "topic");
            Paragraph($"{JsonRead.Str(question, "questionType")}  {prompt}");
            var options = content.ValueKind == JsonValueKind.Object ? JsonRead.Prop(content, "options") : default;
            if (options.ValueKind == JsonValueKind.Array)
            {
                var box = new ComboBox { Style = (Style)FindResource("FieldCombo"), Margin = new Thickness(0, 0, 0, 12) };
                foreach (var option in options.EnumerateArray())
                {
                    box.Items.Add(new Option(JsonRead.Str(option, "key"), JsonRead.Str(option, "text")));
                }

                if (box.Items.Count > 0)
                {
                    box.SelectedIndex = 0;
                }

                Body.Children.Add(box);
                _objectiveAnswers[id] = new TextBox { Tag = box };
            }
            else
            {
                var input = new TextBox { Style = (Style)FindResource("FieldBox"), Margin = new Thickness(0, 0, 0, 12), AcceptsReturn = true, MinHeight = 72 };
                _objectiveAnswers[id] = input;
                Body.Children.Add(input);
            }
        }

        Button("提交客观题", SubmitObjectiveAsync);
    }

    private async Task SubmitObjectiveAsync()
    {
        var answers = _objectiveAnswers.Select(pair =>
        {
            var answer = pair.Value.Tag is ComboBox box
                ? (box.SelectedItem as Option)?.Value ?? ""
                : pair.Value.Text.Trim();
            return new { questionId = pair.Key, answer };
        }).ToList();
        var result = await AppServices.Api.PostAsync($"/proficiency-test/{_testId}/objective-answers", new { answers });
        Paragraph($"客观题得分 {JsonRead.Str(result, "objectiveScore")}");
        await ShowWritingAsync();
    }

    private async Task ShowWritingAsync()
    {
        var data = await AppServices.Api.GetAsync($"/proficiency-test/{_testId}/writing-prompts");
        Body.Children.Clear();
        _writings.Clear();
        Heading("两篇短写作");
        foreach (var task in JsonRead.Items(data))
        {
            var slot = JsonRead.Str(task, "slot");
            Paragraph($"{slot} · {JsonRead.Str(task, "minWords")}-{JsonRead.Str(task, "maxWords")} 词");
            Paragraph(JsonRead.Str(task, "prompt"));
            var box = new TextBox
            {
                Style = (Style)FindResource("FieldBox"),
                AcceptsReturn = true,
                MinHeight = 140,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12),
                Text = JsonRead.Str(task, "text"),
            };
            _writings[slot] = box;
            Body.Children.Add(box);
        }

        Button("提交写作并生成计划", SubmitWritingAsync);
    }

    private async Task SubmitWritingAsync()
    {
        var answers = _writings.Select(pair => new { slot = pair.Key, text = pair.Value.Text }).ToList();
        var result = await AppServices.Api.PostAsync($"/proficiency-test/{_testId}/writing", new { answers });
        if (JsonRead.Bool(result, "readyForEvaluation") || JsonRead.Str(result, "currentStage") == "done")
        {
            await EvaluateAsync();
        }
        else
        {
            Paragraph("写作已保存。");
        }
    }

    private async Task EvaluateAsync()
    {
        Body.Children.Clear();
        Heading("正在生成等级和计划");
        var payload = await AppServices.Api.GetAsync($"/proficiency-test/{_testId}/evaluation-payload");
        var purpose = JsonRead.Str(payload, "purpose");
        if (string.IsNullOrWhiteSpace(purpose))
        {
            purpose = "proficiency_test";
        }

        var hasKey = !string.IsNullOrWhiteSpace(AppServices.Session.EncryptedAiKey);
        var headers = hasKey ? new Dictionary<string, string> { ["X-Encrypted-Key"] = AppServices.Session.EncryptedAiKey } : null;
        var ai = await AppServices.Api.PostAsync($"/ai/proxy/{purpose}", new
        {
            providerId = hasKey && AppServices.Session.AiProviderId.Length > 0 ? AppServices.Session.AiProviderId : "free",
            modelId = hasKey && AppServices.Session.AiModelId.Length > 0 ? AppServices.Session.AiModelId : "free",
            userContent = JsonRead.Str(payload, "userContent"),
        }, headers: headers);
        var content = JsonRead.Str(ai, "content").Trim();
        if (content.StartsWith("```", StringComparison.Ordinal))
        {
            var first = content.IndexOf('\n');
            var last = content.LastIndexOf("```", StringComparison.Ordinal);
            if (first > 0 && last > first)
            {
                content = content[(first + 1)..last].Trim();
            }
        }

        using var parsed = JsonDocument.Parse(content);
        var completed = await AppServices.Api.PostAsync($"/proficiency-test/{_testId}/complete", parsed.RootElement.Clone());
        Body.Children.Clear();
        Heading("测评完成");
        Paragraph($"等级 {JsonRead.Str(completed, "overallLevel")}    分数 {JsonRead.Str(completed, "overallScore")}");
        Paragraph(JsonRead.Pretty(JsonRead.Prop(completed, "result")));
        if (Application.Current.MainWindow is MainWindow shell)
        {
            await shell.RefreshGuideDotAsync();
        }
    }

    private void Heading(string text) => Body.Children.Add(new TextBlock
    {
        Text = text,
        Style = (Style)FindResource("PageTitle"),
        Margin = new Thickness(0, 0, 0, 12),
    });

    private void Paragraph(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        Body.Children.Add(new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
            Foreground = (System.Windows.Media.Brush)FindResource("TextBrush"),
        });
    }

    private void Button(string label, Func<Task> action)
    {
        var button = new Button
        {
            Content = label,
            Style = (Style)FindResource("PrimaryButton"),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 8),
        };
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                Paragraph(ex.Message);
            }
            finally
            {
                button.IsEnabled = true;
            }
        };
        Body.Children.Add(button);
    }

    private static string FirstText(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            var value = JsonRead.Str(element, name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return element.ValueKind == JsonValueKind.Object ? element.GetRawText() : "";
    }

    private sealed record Option(string Value, string Label)
    {
        public override string ToString() => string.IsNullOrWhiteSpace(Label) ? Value : Label;
    }
}
