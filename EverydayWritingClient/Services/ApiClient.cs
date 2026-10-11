using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace EverydayWritingClient.Services;

public sealed class ApiClient
{
    public const string DefaultOrigin = "http://localhost:5000";

    public string Origin
    {
        get
        {
            var value = Session.ApiOrigin?.Trim().TrimEnd('/');
            return string.IsNullOrWhiteSpace(value) ? DefaultOrigin : value;
        }
    }

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(3) };
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public SessionStore Session { get; }
    public event Action? Unauthorized;

    public ApiClient(SessionStore session)
    {
        Session = session;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("EverydayWritingClient/1.0");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public Task<JsonElement> GetAsync(string path, IDictionary<string, string?>? query = null, bool skipAuth = false) =>
        SendAsync(HttpMethod.Get, path, null, query, skipAuth, null);

    public Task<JsonElement> PostAsync(string path, object? body = null, bool skipAuth = false, IDictionary<string, string>? headers = null) =>
        SendAsync(HttpMethod.Post, path, body, null, skipAuth, headers);

    public Task<JsonElement> PutAsync(string path, object? body = null, IDictionary<string, string>? headers = null) =>
        SendAsync(HttpMethod.Put, path, body, null, false, headers);

    public Task<JsonElement> DeleteAsync(string path) =>
        SendAsync(HttpMethod.Delete, path, null, null, false, null);

    public async Task<JsonElement> UploadAsync(string path, string filePath, string fieldName = "file")
    {
        var bytes = await File.ReadAllBytesAsync(filePath);
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(ContentTypeFor(filePath));
        content.Add(file, fieldName, IoPath.GetFileName(filePath));
        return await SendAsync(HttpMethod.Post, path, null, null, false, null, content);
    }

    private static string ContentTypeFor(string filePath) => IoPath.GetExtension(filePath).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        ".csv" => "text/csv",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        _ => "application/octet-stream",
    };

    public Task<string> PostStreamAsync(
        string path,
        object body,
        IDictionary<string, string>? headers,
        Action<string> onDelta,
        CancellationToken cancellationToken = default) =>
        ReadStreamAsync(path, body, headers, onDelta, cancellationToken, allowRefresh: true);

    public async Task<JsonElement> SendAsync(
        HttpMethod method,
        string path,
        object? body,
        IDictionary<string, string?>? query,
        bool skipAuth,
        IDictionary<string, string>? headers,
        HttpContent? rawContent = null,
        bool allowRefresh = true)
    {
        using var request = new HttpRequestMessage(method, BuildUrl(path, query));
        if (!skipAuth && !string.IsNullOrWhiteSpace(Session.Token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Session.Token);
        }

        if (headers != null)
        {
            foreach (var pair in headers)
            {
                request.Headers.TryAddWithoutValidation(pair.Key, pair.Value);
            }
        }

        if (rawContent != null)
        {
            request.Content = rawContent;
        }
        else if (body != null)
        {
            var json = body is JsonElement element
                ? element.GetRawText()
                : body is string text ? text : JsonSerializer.Serialize(body, JsonRead.Options);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new ApiException(0, DescribeTransportError(ex), 0, null);
        }

        using (response)
        {
            if (allowRefresh
                && !skipAuth
                && response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden
                && !string.IsNullOrWhiteSpace(Session.RefreshToken)
                && !path.Contains("/auth/refresh", StringComparison.OrdinalIgnoreCase))
            {
                if (await RefreshAsync())
                {
                    return await SendAsync(method, path, body, query, skipAuth, headers, rawContent, false);
                }
            }

            var text = await response.Content.ReadAsStringAsync();
            try
            {
                return ParseEnvelope(response, text);
            }
            catch (ApiException ex) when (ex.IsUnauthorized && allowRefresh)
            {
                Unauthorized?.Invoke();
                throw;
            }
        }
    }

    private async Task<bool> RefreshAsync()
    {
        await _refreshLock.WaitAsync();
        try
        {
            if (string.IsNullOrWhiteSpace(Session.RefreshToken) || string.IsNullOrWhiteSpace(Session.Token))
            {
                return false;
            }

            var data = await SendAsync(
                HttpMethod.Post,
                "/auth/refresh",
                new { refreshToken = Session.RefreshToken },
                null,
                false,
                null,
                null,
                false);
            Session.ApplyRefresh(data);
            return !string.IsNullOrWhiteSpace(Session.Token);
        }
        catch
        {
            Session.ClearAuth();
            Unauthorized?.Invoke();
            return false;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private static JsonElement ParseEnvelope(HttpResponseMessage response, string text)
    {
        var status = (int)response.StatusCode;
        if (string.IsNullOrWhiteSpace(text))
        {
            if (response.IsSuccessStatusCode)
            {
                return default;
            }

            throw new ApiException(status, status == 401 ? "登录状态已失效，请重新登录" : $"请求失败 ({status})", status, null);
        }

        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            var code = root.TryGetProperty("code", out var codeElement) && codeElement.TryGetInt32(out var parsed)
                ? parsed
                : status;
            var message = root.TryGetProperty("message", out var messageElement)
                ? messageElement.GetString() ?? "请求失败"
                : "请求失败";
            JsonElement? data = root.TryGetProperty("data", out var dataElement) ? dataElement.Clone() : null;
            if (!response.IsSuccessStatusCode || (code != 200 && code != 201))
            {
                throw new ApiException(code, message, status, data);
            }

            return data ?? default;
        }
        catch (ApiException)
        {
            throw;
        }
        catch (JsonException)
        {
            throw new ApiException(status, status == 401 ? "登录状态已失效，请重新登录" : $"请求失败 ({status})", status, null);
        }
    }

    private static string DescribeTransportError(Exception exception)
    {
        var details = new StringBuilder();
        for (var current = exception; current != null; current = current.InnerException)
        {
            details.Append(' ').Append(current.Message);
        }

        var text = details.ToString();
        if (text.Contains("NotTimeValid", StringComparison.OrdinalIgnoreCase)
            || text.Contains("certificate", StringComparison.OrdinalIgnoreCase)
            || text.Contains("证书", StringComparison.Ordinal))
        {
            return "服务器证书无效，系统拒绝了这次 HTTPS 连接。";
        }

        if (exception is TaskCanceledException)
        {
            return "连接服务器超时，请稍后重试。";
        }

        return "无法连接服务器，请检查网络后重试。";
    }

    private string BuildUrl(string path, IDictionary<string, string?>? query)
    {
        if (!path.StartsWith('/'))
        {
            path = "/" + path;
        }

        if (!path.StartsWith("/api/v1", StringComparison.OrdinalIgnoreCase))
        {
            path = "/api/v1" + path;
        }

        var builder = new StringBuilder(Origin.TrimEnd('/')).Append(path);
        if (query is { Count: > 0 })
        {
            var first = true;
            foreach (var pair in query)
            {
                if (string.IsNullOrWhiteSpace(pair.Value))
                {
                    continue;
                }

                builder.Append(first ? '?' : '&');
                first = false;
                builder.Append(Uri.EscapeDataString(pair.Key));
                builder.Append('=');
                builder.Append(Uri.EscapeDataString(pair.Value));
            }
        }

        return builder.ToString();
    }

    private async Task<string> ReadStreamAsync(
        string path,
        object body,
        IDictionary<string, string>? headers,
        Action<string> onDelta,
        CancellationToken cancellationToken,
        bool allowRefresh)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUrl(path, null));
        if (!string.IsNullOrWhiteSpace(Session.Token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Session.Token);
        }

        if (headers != null)
        {
            foreach (var pair in headers)
            {
                request.Headers.TryAddWithoutValidation(pair.Key, pair.Value);
            }
        }

        var json = JsonSerializer.Serialize(body, JsonRead.Options);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new ApiException(0, DescribeTransportError(ex), 0, null);
        }

        if (allowRefresh
            && response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden
            && !string.IsNullOrWhiteSpace(Session.RefreshToken))
        {
            response.Dispose();
            if (await RefreshAsync())
            {
                return await ReadStreamAsync(path, body, headers, onDelta, cancellationToken, false);
            }

            throw new ApiException(401, "登录状态已失效，请重新登录", 401, null);
        }

        using (response)
        {
            var media = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!media.Contains("event-stream", StringComparison.OrdinalIgnoreCase))
            {
                var text = await response.Content.ReadAsStringAsync(cancellationToken);
                var data = ParseEnvelope(response, text);
                var content = JsonRead.Str(data, "content");
                if (content.Length > 0)
                {
                    onDelta(content);
                }

                return content;
            }

            var full = new StringBuilder();
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);
            while (true)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line == null)
                {
                    break;
                }

                var trimmed = line.Trim();
                if (!trimmed.StartsWith("data:", StringComparison.Ordinal))
                {
                    continue;
                }

                var payload = trimmed[5..].Trim();
                if (payload.Length == 0 || payload == "[DONE]")
                {
                    continue;
                }

                try
                {
                    using var doc = JsonDocument.Parse(payload);
                    var root = doc.RootElement;
                    var type = JsonRead.Str(root, "type");
                    if (type == "delta")
                    {
                        var delta = JsonRead.Str(root, "text");
                        if (delta.Length > 0)
                        {
                            full.Append(delta);
                            onDelta(delta);
                        }
                    }
                    else if (type == "done")
                    {
                        var done = JsonRead.Str(root, "content");
                        if (done.Length > 0)
                        {
                            return done;
                        }
                    }
                    else if (type == "error")
                    {
                        var message = JsonRead.Str(root, "message");
                        throw new ApiException(502, string.IsNullOrWhiteSpace(message) ? "流式输出失败" : message, 502, null);
                    }
                }
                catch (ApiException)
                {
                    throw;
                }
                catch (JsonException)
                {
                    // 忽略无法解析的一行，继续读后面的内容。
                }
            }

            return full.ToString();
        }
    }
}
