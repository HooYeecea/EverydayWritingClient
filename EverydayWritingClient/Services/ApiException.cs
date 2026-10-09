using System.Text.Json;

namespace EverydayWritingClient.Services;

public sealed class ApiException : Exception
{
    public int Code { get; }
    public int HttpStatus { get; }
    public JsonElement? Payload { get; }

    public ApiException(int code, string message, int httpStatus, JsonElement? data)
        : base(string.IsNullOrWhiteSpace(message) ? "请求失败" : message)
    {
        Code = code;
        HttpStatus = httpStatus;
        Payload = data;
    }

    public bool IsUnauthorized => HttpStatus == 401 || Code == 401;
}
