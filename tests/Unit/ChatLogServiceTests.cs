using VsngrpCoreBeWs.Models;
using VsngrpCoreBeWs.Services;

namespace VsngrpCoreBeWs.Tests.Unit;

public sealed class ChatLogServiceTests(RedisTestFixture fixture) : IClassFixture<RedisTestFixture>
{
    private ChatLogService CreateService() => new(fixture.Redis);

    [Fact]
    public async Task AppendAsync_ThenGetHistoryAsync_ReturnsMessagesInOrder()
    {
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();

        await service.AppendAsync(accountId, conversationId, BuildMessage("hi"));
        await service.AppendAsync(accountId, conversationId, BuildMessage("hello"));

        var history = await service.GetHistoryAsync(accountId, conversationId);

        Assert.Equal(2, history.Count);
        Assert.Equal("hi", history[0].Content);
        Assert.Equal("hello", history[1].Content);
    }

    [Fact]
    public async Task AppendAsync_MoreThanCap_TrimsOldestMessagesFirst()
    {
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();

        for (var index = 0; index < ChatLogService.MaxMessagesPerConversation + 10; index++)
        {
            await service.AppendAsync(accountId, conversationId, BuildMessage($"message-{index}"));
        }

        var history = await service.GetHistoryAsync(accountId, conversationId);

        Assert.Equal(ChatLogService.MaxMessagesPerConversation, history.Count);
        Assert.Equal("message-10", history[0].Content);
        Assert.Equal($"message-{ChatLogService.MaxMessagesPerConversation + 9}", history[^1].Content);
    }

    [Fact]
    public async Task DeleteAsync_RemovesHistory()
    {
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        await service.AppendAsync(accountId, conversationId, BuildMessage("hi"));

        await service.DeleteAsync(accountId, conversationId);
        var history = await service.GetHistoryAsync(accountId, conversationId);

        Assert.Empty(history);
    }

    [Fact]
    public async Task GetHistoryAsync_UnknownConversation_ReturnsEmpty()
    {
        var service = CreateService();

        var history = await service.GetHistoryAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Empty(history);
    }

    private static ChatMessage BuildMessage(string content) => new()
    {
        Role = ChatMessageRole.User,
        Content = content,
        Timestamp = DateTimeOffset.UtcNow,
    };
}
