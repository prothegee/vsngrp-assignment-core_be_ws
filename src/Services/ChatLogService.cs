using System.Text.Json;
using StackExchange.Redis;
using VsngrpCoreBeWs.Models;

namespace VsngrpCoreBeWs.Services;

public interface IChatLogService
{
    Task AppendAsync(Guid accountId, Guid conversationId, ChatMessage message);
    Task<IReadOnlyList<ChatMessage>> GetHistoryAsync(Guid accountId, Guid conversationId);
    Task DeleteAsync(Guid accountId, Guid conversationId);
}

public sealed class ChatLogService([FromKeyedServices("own")] IConnectionMultiplexer redis) : IChatLogService
{
    public const int MaxMessagesPerConversation = 100;

    private const string ChatLogKeyPrefix = "chatlog:";

    private IDatabase Database => redis.GetDatabase();

    public async Task AppendAsync(Guid accountId, Guid conversationId, ChatMessage message)
    {
        var key = BuildKey(accountId, conversationId);
        await Database.ListRightPushAsync(key, JsonSerializer.Serialize(message, WsJson.Options));
        await Database.ListTrimAsync(key, -MaxMessagesPerConversation, -1);
    }

    public async Task<IReadOnlyList<ChatMessage>> GetHistoryAsync(Guid accountId, Guid conversationId)
    {
        var values = await Database.ListRangeAsync(BuildKey(accountId, conversationId));

        return values
            .Select(value => JsonSerializer.Deserialize<ChatMessage>((string)value!, WsJson.Options)!)
            .ToArray();
    }

    public async Task DeleteAsync(Guid accountId, Guid conversationId)
    {
        await Database.KeyDeleteAsync(BuildKey(accountId, conversationId));
    }

    private static string BuildKey(Guid accountId, Guid conversationId) =>
        $"{ChatLogKeyPrefix}{accountId:N}:{conversationId:N}";
}
