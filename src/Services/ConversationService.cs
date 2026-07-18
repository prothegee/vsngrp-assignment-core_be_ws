using System.Text.Json;
using StackExchange.Redis;
using VsngrpCoreBeWs.Models;

namespace VsngrpCoreBeWs.Services;

public interface IConversationService
{
    Task<Conversation> CreateAsync(Guid accountId, string title);
    Task<IReadOnlyList<Conversation>> ListAsync(Guid accountId);
    Task<bool> RenameAsync(Guid accountId, Guid conversationId, string title);
    Task<bool> DeleteAsync(Guid accountId, Guid conversationId);
    Task<Conversation?> GetAsync(Guid accountId, Guid conversationId);
}

public sealed class ConversationService([FromKeyedServices("own")] IConnectionMultiplexer redis) : IConversationService
{
    private const string ConversationKeyPrefix = "conversation:";
    private const string ConversationIndexKeyPrefix = "conversations:";

    private IDatabase Database => redis.GetDatabase();

    public async Task<Conversation> CreateAsync(Guid accountId, string title)
    {
        var conversation = new Conversation
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Title = title,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        await Database.StringSetAsync(BuildConversationKey(accountId, conversation.Id), JsonSerializer.Serialize(conversation, WsJson.Options));
        await Database.SortedSetAddAsync(BuildIndexKey(accountId), conversation.Id.ToString(), conversation.CreatedAt.ToUnixTimeSeconds());

        return conversation;
    }

    public async Task<IReadOnlyList<Conversation>> ListAsync(Guid accountId)
    {
        var conversationIds = await Database.SortedSetRangeByScoreAsync(BuildIndexKey(accountId));
        if (conversationIds.Length == 0)
        {
            return [];
        }

        var keys = conversationIds
            .Select(conversationId => (RedisKey)BuildConversationKey(accountId, Guid.Parse((string)conversationId!)))
            .ToArray();
        var values = await Database.StringGetAsync(keys);

        return values
            .Where(value => !value.IsNullOrEmpty)
            .Select(value => JsonSerializer.Deserialize<Conversation>((string)value!, WsJson.Options)!)
            .ToArray();
    }

    public async Task<bool> RenameAsync(Guid accountId, Guid conversationId, string title)
    {
        var conversation = await GetAsync(accountId, conversationId);
        if (conversation is null)
        {
            return false;
        }

        conversation.Title = title;
        await Database.StringSetAsync(BuildConversationKey(accountId, conversationId), JsonSerializer.Serialize(conversation, WsJson.Options));

        return true;
    }

    public async Task<bool> DeleteAsync(Guid accountId, Guid conversationId)
    {
        var removedFromIndex = await Database.SortedSetRemoveAsync(BuildIndexKey(accountId), conversationId.ToString());
        await Database.KeyDeleteAsync(BuildConversationKey(accountId, conversationId));

        return removedFromIndex;
    }

    public async Task<Conversation?> GetAsync(Guid accountId, Guid conversationId)
    {
        var value = await Database.StringGetAsync(BuildConversationKey(accountId, conversationId));

        return value.IsNullOrEmpty ? null : JsonSerializer.Deserialize<Conversation>((string)value!, WsJson.Options);
    }

    private static string BuildConversationKey(Guid accountId, Guid conversationId) =>
        $"{ConversationKeyPrefix}{accountId:N}:{conversationId:N}";

    private static string BuildIndexKey(Guid accountId) => $"{ConversationIndexKeyPrefix}{accountId:N}";
}
