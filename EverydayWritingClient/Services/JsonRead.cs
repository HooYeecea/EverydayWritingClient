using System.Text.Json;
using System.Text.Json.Serialization;

namespace EverydayWritingClient.Services;

public static class JsonRead
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Str(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
        {
            return "";
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null or JsonValueKind.Undefined => "",
            _ => value.GetRawText(),
        };
    }

    public static int Int(JsonElement element, string name, int fallback = 0)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
        {
            return fallback;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        return int.TryParse(value.ToString(), out var parsed) ? parsed : fallback;
    }

    public static bool Bool(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(value.GetString(), out var parsed) && parsed,
            _ => false,
        };
    }

    public static JsonElement Prop(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value))
        {
            return value;
        }

        return default;
    }

    public static string Pretty(JsonElement element)
    {
        if (element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return "";
        }

        return JsonSerializer.Serialize(element, new JsonSerializerOptions { WriteIndented = true });
    }

    public static List<string> StringList(JsonElement element)
    {
        var list = new List<string>();
        if (element.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (var item in element.EnumerateArray())
        {
            var text = item.ValueKind == JsonValueKind.String ? item.GetString() : item.GetRawText();
            if (!string.IsNullOrWhiteSpace(text))
            {
                list.Add(text);
            }
        }

        return list;
    }

    public static List<string> Roles(JsonElement element)
    {
        var list = new List<string>();
        if (element.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var name = item.GetString();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    list.Add(name.Trim());
                }
            }
            else if (item.ValueKind == JsonValueKind.Object)
            {
                var name = Str(item, "name");
                if (!string.IsNullOrWhiteSpace(name))
                {
                    list.Add(name.Trim());
                }
            }
        }

        return list;
    }

    public static IEnumerable<JsonElement> Items(JsonElement data)
    {
        if (data.ValueKind == JsonValueKind.Array)
        {
            return data.EnumerateArray().Select(item => item.Clone()).ToList();
        }

        if (data.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in new[] { "items", "questions", "tasks", "months", "providers" })
            {
                if (data.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array)
                {
                    return array.EnumerateArray().Select(item => item.Clone()).ToList();
                }
            }
        }

        return [];
    }

    public static string PlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return "";
        }

        var text = System.Text.RegularExpressions.Regex.Replace(html, "<br\\s*/?>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        text = System.Text.RegularExpressions.Regex.Replace(text, "</p>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        text = System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        return System.Text.RegularExpressions.Regex.Replace(text, @"[ \t]+\n", "\n").Trim();
    }

    public static int CountWords(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        return System.Text.RegularExpressions.Regex.Matches(text, @"[A-Za-z0-9]+(?:[-'][A-Za-z0-9]+)*").Count;
    }
}
