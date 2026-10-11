using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EverydayWritingClient.Services;

public sealed class SessionStore
{
    private readonly string _path;

    public string Token { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public string ExpiresAt { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Email { get; set; } = "";
    public string Nickname { get; set; } = "";
    public bool MustChangePassword { get; set; }
    public List<string> Roles { get; set; } = [];
    public List<string> Permissions { get; set; } = [];
    public string Theme { get; set; } = "light";
    public string EditorFontSize { get; set; } = "md";
    public int AutoSaveSeconds { get; set; } = 5;
    public bool ConfirmBeforeSubmit { get; set; } = true;
    public string DefaultHome { get; set; } = "writing";
    public string AiProviderId { get; set; } = "";
    public string AiModelId { get; set; } = "";
    public string EncryptedAiKey { get; set; } = "";
    public bool AiGrammar { get; set; } = true;
    public bool AiStructure { get; set; } = true;
    public bool AiSuggestion { get; set; } = true;
    public bool AiRealtime { get; set; }
    public bool ShowGuideRedDot { get; set; }
    public string ApiOrigin { get; set; } = "";

    public SessionStore()
    {
        var folder = IoPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EverydayWriting");
        Directory.CreateDirectory(folder);
        _path = IoPath.Combine(folder, "session.json");
    }

    public bool IsAuthenticated => !string.IsNullOrWhiteSpace(Token);

    public bool IsAdmin =>
        Roles.Any(role => role.Contains("admin", StringComparison.OrdinalIgnoreCase))
        || Permissions.Count > 0;

    public bool HasUserRole =>
        Roles.Any(role => role.Equals("user", StringComparison.OrdinalIgnoreCase))
        || !IsAdmin;

    public bool IsAdminOnly => IsAdmin && !Roles.Any(role => role.Equals("user", StringComparison.OrdinalIgnoreCase));

    public bool HasPermission(string? permission)
    {
        if (string.IsNullOrWhiteSpace(permission))
        {
            return IsAdmin;
        }

        if (Permissions.Count == 0 && IsAdmin)
        {
            return true;
        }

        return Permissions.Contains(permission);
    }

    public void Load()
    {
        if (!File.Exists(_path))
        {
            return;
        }

        try
        {
            var json = ReadFile();
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            Token = JsonRead.Str(root, "token");
            RefreshToken = JsonRead.Str(root, "refreshToken");
            ExpiresAt = JsonRead.Str(root, "expiresAt");
            UserId = JsonRead.Str(root, "userId");
            Email = JsonRead.Str(root, "email");
            Nickname = JsonRead.Str(root, "nickname");
            MustChangePassword = JsonRead.Bool(root, "mustChangePassword");
            Roles = root.TryGetProperty("roles", out var roles) ? JsonRead.Roles(roles) : [];
            Permissions = root.TryGetProperty("permissions", out var permissions) ? JsonRead.StringList(permissions) : [];
            Theme = string.IsNullOrWhiteSpace(JsonRead.Str(root, "theme")) ? "light" : JsonRead.Str(root, "theme");
            EditorFontSize = string.IsNullOrWhiteSpace(JsonRead.Str(root, "editorFontSize")) ? "md" : JsonRead.Str(root, "editorFontSize");
            AutoSaveSeconds = JsonRead.Int(root, "autoSaveSeconds", 5);
            ConfirmBeforeSubmit = !root.TryGetProperty("confirmBeforeSubmit", out _) || JsonRead.Bool(root, "confirmBeforeSubmit");
            DefaultHome = string.IsNullOrWhiteSpace(JsonRead.Str(root, "defaultHome")) ? "writing" : JsonRead.Str(root, "defaultHome");
            AiProviderId = JsonRead.Str(root, "aiProviderId");
            AiModelId = JsonRead.Str(root, "aiModelId");
            EncryptedAiKey = JsonRead.Str(root, "encryptedAiKey");
            AiGrammar = !root.TryGetProperty("aiGrammar", out _) || JsonRead.Bool(root, "aiGrammar");
            AiStructure = !root.TryGetProperty("aiStructure", out _) || JsonRead.Bool(root, "aiStructure");
            AiSuggestion = !root.TryGetProperty("aiSuggestion", out _) || JsonRead.Bool(root, "aiSuggestion");
            AiRealtime = JsonRead.Bool(root, "aiRealtime");
            ApiOrigin = JsonRead.Str(root, "apiOrigin");
            if (WasPlainText)
            {
                Save();
            }
        }
        catch
        {
            // 本地会话损坏时按未登录处理，避免挡住启动。
        }
    }

    public void ApplyLogin(JsonElement data)
    {
        Token = JsonRead.Str(data, "token");
        RefreshToken = JsonRead.Str(data, "refreshToken");
        ExpiresAt = JsonRead.Str(data, "expiresAt");

        var user = JsonRead.Prop(data, "user");
        if (user.ValueKind == JsonValueKind.Object)
        {
            UserId = JsonRead.Str(user, "id");
            Email = JsonRead.Str(user, "email");
            Nickname = JsonRead.Str(user, "nickname");
            MustChangePassword = JsonRead.Bool(user, "mustChangePassword");
            Roles = user.TryGetProperty("roles", out var roles) ? JsonRead.Roles(roles) : [];
            Permissions = user.TryGetProperty("permissions", out var permissions) ? JsonRead.StringList(permissions) : [];
        }

        if (Roles.Count == 0 && data.TryGetProperty("roles", out var rootRoles))
        {
            Roles = JsonRead.Roles(rootRoles);
        }

        if (Permissions.Count == 0 && data.TryGetProperty("permissions", out var rootPermissions))
        {
            Permissions = JsonRead.StringList(rootPermissions);
        }

        Save();
    }

    public void ApplyRefresh(JsonElement data)
    {
        var token = JsonRead.Str(data, "token");
        var refresh = JsonRead.Str(data, "refreshToken");
        if (!string.IsNullOrWhiteSpace(token))
        {
            Token = token;
        }

        if (!string.IsNullOrWhiteSpace(refresh))
        {
            RefreshToken = refresh;
        }

        var expires = JsonRead.Str(data, "expiresAt");
        if (!string.IsNullOrWhiteSpace(expires))
        {
            ExpiresAt = expires;
        }

        Save();
    }

    public void ClearAuth()
    {
        Token = "";
        RefreshToken = "";
        ExpiresAt = "";
        UserId = "";
        Email = "";
        Nickname = "";
        MustChangePassword = false;
        Roles = [];
        Permissions = [];
        ShowGuideRedDot = false;
        Save();
    }

    public void Save()
    {
        var payload = new
        {
            token = Token,
            refreshToken = RefreshToken,
            expiresAt = ExpiresAt,
            userId = UserId,
            email = Email,
            nickname = Nickname,
            mustChangePassword = MustChangePassword,
            roles = Roles,
            permissions = Permissions,
            theme = Theme,
            editorFontSize = EditorFontSize,
            autoSaveSeconds = AutoSaveSeconds,
            confirmBeforeSubmit = ConfirmBeforeSubmit,
            defaultHome = DefaultHome,
            aiProviderId = AiProviderId,
            aiModelId = AiModelId,
            encryptedAiKey = EncryptedAiKey,
            aiGrammar = AiGrammar,
            aiStructure = AiStructure,
            aiSuggestion = AiSuggestion,
            aiRealtime = AiRealtime,
            apiOrigin = ApiOrigin,
        };
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        var protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(json), Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_path, protectedBytes);
    }

    private bool WasPlainText { get; set; }

    private static readonly byte[] Entropy = "EverydayWriting.Session.v1"u8.ToArray();

    private string ReadFile()
    {
        var bytes = File.ReadAllBytes(_path);
        if (bytes.Length == 0)
        {
            return "";
        }

        var start = bytes.Length > 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
        if (bytes[start] == (byte)'{')
        {
            WasPlainText = true;
            return Encoding.UTF8.GetString(bytes);
        }

        WasPlainText = false;
        var plain = ProtectedData.Unprotect(bytes, Entropy, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plain);
    }
}
