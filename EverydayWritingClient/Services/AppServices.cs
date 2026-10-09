namespace EverydayWritingClient.Services;

public static class AppServices
{
    public static SessionStore Session { get; } = new();
    public static ApiClient Api { get; } = new(Session);
}
