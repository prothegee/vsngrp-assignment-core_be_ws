namespace VsngrpCoreBeWs.Models;

public static class ChatMessageRole
{
    public const string System = "system";
    public const string User = "user";
    public const string Assistant = "assistant";
}

public sealed class ChatMessage
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
}
