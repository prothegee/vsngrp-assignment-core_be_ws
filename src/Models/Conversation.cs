namespace VsngrpCoreBeWs.Models;

public sealed class Conversation
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
