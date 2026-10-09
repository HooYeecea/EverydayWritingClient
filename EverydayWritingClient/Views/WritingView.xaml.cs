using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using EverydayWritingClient.Services;

namespace EverydayWritingClient.Views;

public partial class WritingView : UserControl, IRefreshOnShow
{
    private readonly DispatcherTimer _autosave = new();
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _realtime = new() { Interval = TimeSpan.FromMilliseconds(1600) };
    private string? _draftId;
    private string? _updatedAt;
    private string? _sourceSubmitId;
    private int _topicId;
    private string _topic = "";
    private bool _customTopic = true;
    private bool _loaded;
    private bool _countUp = true;
    private int _remaining;
    private bool _realtimeBusy;

    public WritingView()
    {
        InitializeComponent();
        GrammarBox.IsChecked = AppServices.Session.AiGrammar;
        StructureBox.IsChecked = AppServices.Session.AiStructure;
        SuggestionBox.IsChecked = AppServices.Session.AiSuggestion;
        RealtimeBox.IsChecked = AppServices.Session.AiRealtime;
        ApplyFont();
        Editor.ContentChanged += (_, _) =>
        {
            UpdateWordCount();
            _realtime.Stop();
            _realtime.Start();
        };
        _autosave.Tick += async (_, _) => await SaveDraftAsync(silent: true);
        _clock.Tick += ClockTick;
        _realtime.Tick += async (_, _) =>
        {
            _realtime.Stop();
            if (RealtimeBox.IsChecked == true)
            {
                await RunAiAsync("realtime_assist", persist: false);
            }
        };
        RestartAutosave();
        Unloaded += (_, _) =>
        {
            _autosave.Stop();
            _clock.Stop();
            _realtime.Stop();
        };
    }

    public async Task RefreshAsync()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        await LoadTypesAsync();
        if (AppServices.Session.IsAuthenticated && string.IsNullOrEmpty(_draftId))
        {
            try
            {
                var latest = await AppServices.Api.GetAsync("/writings/drafts/latest");
                ApplyDraft(latest);
            }
            catch
            {
                // 没有最近草稿时保持空白编辑器。
            }
        }
    }

    public async Task LoadDraftAsync(string id)
    {
        var data = await AppServices.Api.GetAsync($"/writings/drafts/{id}");
        ApplyDraft(data);
        _sourceSubmitId = NullIfEmpty(JsonRead.Str(data, "sourceSubmitId"));
        StatusText.Text = "已打开草稿。";
    }

    public async Task LoadSubmitAsync(string id)
    {
        var data = await AppServices.Api.GetAsync($"/writings/submits/{id}");
        _sourceSubmitId = id;
        _draftId = null;
        _updatedAt = null;
        _topicId = JsonRead.Int(data, "topicId");
        _customTopic = _topicId <= 0;
        _topic = JsonRead.Str(data, "topic");
        TopicText.Text = string.IsNullOrWhiteSpace(_topic) ? "自拟题目" : _topic;
        TitleBox.Text = JsonRead.Str(data, "title");
        Editor.Html = JsonRead.Str(data, "content");
        StatusText.Text = "基于这篇提交继续修改。再次正式提交会生成下一版。";
    }

    private void ApplyDraft(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        _draftId = NullIfEmpty(JsonRead.Str(data, "id"));
        _updatedAt = NullIfEmpty(JsonRead.Str(data, "updatedAt"));
        _topicId = JsonRead.Int(data, "topicId");
        _customTopic = _topicId <= 0;
        _topic = JsonRead.Str(data, "topic");
        TopicText.Text = string.IsNullOrWhiteSpace(_topic) ? "自拟题目" : _topic;
        TitleBox.Text = JsonRead.Str(data, "title");
        Editor.Html = JsonRead.Str(data, "content");
        UpdateWordCount();
    }

    private async Task LoadTypesAsync()
    {
        TypeBox.Items.Clear();
        TypeBox.Items.Add("全部题型");
        try
        {
            var data = await AppServices.Api.GetAsync("/topics/types");
            foreach (var item in JsonRead.Items(data))
            {
                var name = JsonRead.Str(item, "name");
                if (!string.IsNullOrWhiteSpace(name))
                {
                    TypeBox.Items.Add(name);
                }
            }
        }
        catch
        {
            foreach (var name in new[] { "CET4", "CET6", "IELTS", "TOEFL", "考研" })
            {
                TypeBox.Items.Add(name);
            }
        }

        TypeBox.SelectedIndex = 0;
    }

    private async void RandomTopicClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var selected = TypeBox.SelectedItem as string;
            var query = new Dictionary<string, string?>();
            if (!string.IsNullOrWhiteSpace(selected) && selected != "全部题型")
            {
                query["type"] = selected;
            }

            var topic = await AppServices.Api.GetAsync("/topics/random", query);
            _topicId = JsonRead.Int(topic, "id");
            _customTopic = false;
            _topic = JsonRead.Str(topic, "description");
            if (string.IsNullOrWhiteSpace(_topic))
            {
                _topic = JsonRead.Str(topic, "title");
            }

            var limit = JsonRead.Str(topic, "wordLimit");
            TopicText.Text = string.IsNullOrWhiteSpace(limit) ? _topic : $"{_topic}\n建议词数：{limit}";
            if (string.IsNullOrWhiteSpace(TitleBox.Text))
            {
                TitleBox.Text = JsonRead.Str(topic, "title");
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private async void SaveDraftClick(object sender, RoutedEventArgs e) => await SaveDraftAsync(silent: false);

    private async Task SaveDraftAsync(bool silent)
    {
        if (!AppServices.Session.IsAuthenticated)
        {
            if (!silent)
            {
                StatusText.Text = "登录后才能保存草稿。";
            }
            return;
        }

        if (silent && string.IsNullOrWhiteSpace(TitleBox.Text) && string.IsNullOrWhiteSpace(Editor.PlainText))
        {
            return;
        }

        var payload = new Dictionary<string, object?>
        {
            ["topicId"] = _customTopic ? null : _topicId,
            ["topic"] = _topic,
            ["title"] = TitleBox.Text.Trim(),
            ["content"] = Editor.Html,
        };
        if (!string.IsNullOrWhiteSpace(_sourceSubmitId))
        {
            payload["sourceSubmitId"] = _sourceSubmitId;
        }

        try
        {
            JsonElement result;
            if (!string.IsNullOrWhiteSpace(_draftId))
            {
                if (!string.IsNullOrWhiteSpace(_updatedAt))
                {
                    payload["expectedUpdatedAt"] = _updatedAt;
                }

                try
                {
                    result = await AppServices.Api.PutAsync($"/writings/drafts/{_draftId}", payload);
                }
                catch (ApiException ex) when (ex.HttpStatus == 404)
                {
                    _draftId = null;
                    result = await AppServices.Api.PostAsync("/writings/drafts", payload);
                }
            }
            else
            {
                result = await AppServices.Api.PostAsync("/writings/drafts", payload);
            }

            _draftId = NullIfEmpty(JsonRead.Str(result, "id")) ?? _draftId;
            _updatedAt = NullIfEmpty(JsonRead.Str(result, "updatedAt")) ?? _updatedAt;
            if (!silent)
            {
                StatusText.Text = "草稿已保存。";
            }
        }
        catch (ApiException ex) when (ex.HttpStatus == 409)
        {
            StatusText.Text = "草稿已在其他端更新。正在载入服务器版本。";
            if (ex.Payload is { } data)
            {
                ApplyDraft(data);
            }
        }
        catch (Exception ex)
        {
            if (!silent)
            {
                StatusText.Text = ex.Message;
            }
        }
    }

    private async void SubmitClick(object sender, RoutedEventArgs e)
    {
        if (!AppServices.Session.IsAuthenticated)
        {
            StatusText.Text = "登录后才能提交。";
            return;
        }

        if (AppServices.Session.ConfirmBeforeSubmit && !Dialogs.Confirm("确认把这篇作文正式提交吗？"))
        {
            return;
        }

        var content = Editor.Html;
        var stages = new List<(string Purpose, bool Enabled)>
        {
            ("grammar", GrammarBox.IsChecked == true),
            ("structure", StructureBox.IsChecked == true),
            ("vocabulary", SuggestionBox.IsChecked == true),
        };
        var enabled = stages.Where(item => item.Enabled).ToList();
        var stageText = new Dictionary<string, string>();
        StatusText.Text = enabled.Count > 0 ? "正在请求 AI 批改…" : "正在提交…";

        try
        {
            foreach (var stage in enabled)
            {
                var text = await CallAiAsync(stage.Purpose, content, null);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    stageText[stage.Purpose] = text;
                }
            }

            JsonElement result;
            if (!string.IsNullOrWhiteSpace(_sourceSubmitId))
            {
                result = await AppServices.Api.PostAsync($"/writings/submits/{_sourceSubmitId}/iterate", new
                {
                    title = TitleBox.Text.Trim(),
                    content,
                    draftId = _draftId,
                    aiCheckEnabled = GrammarBox.IsChecked == true,
                    aiStructureEnabled = StructureBox.IsChecked == true,
                    aiSuggestionEnabled = SuggestionBox.IsChecked == true,
                });
            }
            else
            {
                result = await AppServices.Api.PostAsync("/writings/submits", new
                {
                    topicId = _customTopic ? 0 : _topicId,
                    topic = _topic,
                    title = TitleBox.Text.Trim(),
                    content,
                    draftId = _draftId,
                    aiCheckEnabled = GrammarBox.IsChecked == true,
                    aiStructureEnabled = StructureBox.IsChecked == true,
                    aiSuggestionEnabled = SuggestionBox.IsChecked == true,
                });
            }

            var submitId = JsonRead.Str(result, "id");
            await PersistAiAsync(submitId, stageText);
            _draftId = null;
            _updatedAt = null;
            _sourceSubmitId = NullIfEmpty(submitId);
            var score = JsonRead.Str(result, "aiScore");
            StatusText.Text = string.IsNullOrWhiteSpace(score)
                ? "已提交。可以在写作记录里回看。"
                : $"已提交。评分 {score}。可以在写作记录里回看。";
            if (stageText.Count > 0)
            {
                AiText.Text = string.Join("\n\n", stageText.Select(pair => $"【{Label(pair.Key)}】\n{PrettyAi(pair.Value)}"));
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private async Task RunAiAsync(string purpose, bool persist)
    {
        if (_realtimeBusy || !AppServices.Session.IsAuthenticated)
        {
            return;
        }

        _realtimeBusy = true;
        try
        {
            var text = await CallAiAsync(purpose, Editor.Html, null);
            AiText.Text = string.IsNullOrWhiteSpace(text) ? "这次没有返回建议。" : PrettyAi(text);
            if (persist)
            {
                StatusText.Text = "AI 建议已更新。";
            }
        }
        catch (Exception ex)
        {
            AiText.Text = ex.Message;
        }
        finally
        {
            _realtimeBusy = false;
        }
    }

    private async Task<string> CallAiAsync(string purpose, string content, string? sessionId)
    {
        var hasKey = !string.IsNullOrWhiteSpace(AppServices.Session.EncryptedAiKey);
        var headers = hasKey
            ? new Dictionary<string, string> { ["X-Encrypted-Key"] = AppServices.Session.EncryptedAiKey }
            : null;
        var data = await AppServices.Api.PostAsync($"/ai/proxy/{purpose}", new
        {
            providerId = hasKey && !string.IsNullOrWhiteSpace(AppServices.Session.AiProviderId) ? AppServices.Session.AiProviderId : "free",
            modelId = hasKey && !string.IsNullOrWhiteSpace(AppServices.Session.AiModelId) ? AppServices.Session.AiModelId : "free",
            userContent = content,
            gradingSessionId = sessionId,
        }, headers: headers);
        return JsonRead.Str(data, "content");
    }

    private async Task PersistAiAsync(string submitId, Dictionary<string, string> stages)
    {
        if (string.IsNullOrWhiteSpace(submitId))
        {
            return;
        }

        var hasKey = !string.IsNullOrWhiteSpace(AppServices.Session.EncryptedAiKey);
        var providerId = hasKey ? AppServices.Session.AiProviderId : "free";
        var modelId = hasKey ? AppServices.Session.AiModelId : "free";
        if (string.IsNullOrWhiteSpace(providerId))
        {
            providerId = "free";
        }

        if (string.IsNullOrWhiteSpace(modelId))
        {
            modelId = "free";
        }

        foreach (var pair in stages)
        {
            int? score = null;
            try
            {
                using var doc = JsonDocument.Parse(StripFence(pair.Value));
                if (doc.RootElement.TryGetProperty("score", out var scoreElement) && scoreElement.TryGetInt32(out var number))
                {
                    score = number;
                }
            }
            catch
            {
                score = null;
            }

            var body = new Dictionary<string, object?>
            {
                ["purpose"] = pair.Key,
                ["resultContent"] = pair.Value,
                ["providerId"] = providerId,
                ["modelId"] = modelId,
            };
            if (score.HasValue)
            {
                body["aiScore"] = score.Value;
            }

            try
            {
                await AppServices.Api.PostAsync($"/writings/submits/{submitId}/ai-results", body);
            }
            catch
            {
                // 单条批改落库失败不取消已经完成的提交。
            }
        }
    }

    private void FocusChanged(object sender, RoutedEventArgs e)
    {
        if (Application.Current.MainWindow is MainWindow shell)
        {
            shell.NavigationLocked = FocusLockBox.IsChecked == true;
        }
    }

    private void StartTimerClick(object sender, RoutedEventArgs e)
    {
        _countUp = !int.TryParse(MinutesBox.Text, out var minutes) || minutes <= 0;
        if (!_countUp)
        {
            _remaining = minutes * 60;
        }

        _clock.Start();
    }

    private void PauseTimerClick(object sender, RoutedEventArgs e) => _clock.Stop();

    private void ResetTimerClick(object sender, RoutedEventArgs e)
    {
        _clock.Stop();
        _remaining = 0;
        TimerText.Text = "00:00";
    }

    private void ClockTick(object? sender, EventArgs e)
    {
        if (_countUp)
        {
            _remaining++;
            TimerText.Text = Format(_remaining);
            return;
        }

        if (_remaining <= 0)
        {
            _clock.Stop();
            TimerText.Text = "00:00";
            System.Media.SystemSounds.Exclamation.Play();
            return;
        }

        _remaining--;
        TimerText.Text = Format(_remaining);
    }

    private void UpdateWordCount() => WordText.Text = $"{JsonRead.CountWords(Editor.PlainText)} 词";

    private void ApplyFont()
    {
        var size = AppServices.Session.EditorFontSize switch
        {
            "sm" => 15d,
            "lg" => 20d,
            _ => 17d,
        };
        Editor.SetFontSize(size);
    }

    private void RestartAutosave()
    {
        var seconds = AppServices.Session.AutoSaveSeconds <= 0 ? 5 : AppServices.Session.AutoSaveSeconds;
        _autosave.Interval = TimeSpan.FromSeconds(seconds);
        _autosave.Start();
    }

    private static string Format(int total) => $"{total / 60:00}:{total % 60:00}";

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string Label(string purpose) => purpose switch
    {
        "grammar" => "语法",
        "structure" => "结构",
        "vocabulary" => "建议",
        _ => "实时建议",
    };

    private static string PrettyAi(string raw)
    {
        var text = StripFence(raw);
        try
        {
            using var doc = JsonDocument.Parse(text);
            return JsonRead.Pretty(doc.RootElement.Clone());
        }
        catch
        {
            return raw;
        }
    }

    private static string StripFence(string raw)
    {
        var text = raw.Trim();
        if (!text.StartsWith("```", StringComparison.Ordinal))
        {
            return text;
        }

        var first = text.IndexOf('\n');
        var last = text.LastIndexOf("```", StringComparison.Ordinal);
        return first > 0 && last > first ? text[(first + 1)..last].Trim() : text;
    }
}
