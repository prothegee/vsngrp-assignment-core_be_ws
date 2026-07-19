using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using VsngrpCoreBeWs.Models;
using VsngrpCoreBeWs.Services;
using VsngrpCoreBeWs.WebSockets;

namespace VsngrpCoreBeWs.Tests.Integration;

public sealed class ChatWebSocketTests(ChatWebSocketTestFixture fixture) : IClassFixture<ChatWebSocketTestFixture>
{
    [Fact]
    public async Task Handshake_InvalidToken_RejectsAndCloses()
    {
        using var socket = await fixture.CreateWebSocketClient().ConnectAsync(fixture.WebSocketUri, CancellationToken.None);

        await SendAsync(socket, new { type = "auth", token = "not-a-real-token" });
        var response = await ReceiveAsync(socket);

        Assert.Equal("auth_error", response.GetProperty("type").GetString());
        var closeStatus = await WaitForCloseAsync(socket);
        Assert.Equal(WebSocketCloseStatus.PolicyViolation, closeStatus);
    }

    [Fact]
    public async Task Handshake_ValidToken_AcceptsAndSendsAuthOk()
    {
        var accountId = Guid.NewGuid();
        var sessionId = await fixture.CreateSessionAsync(accountId);
        using var socket = await fixture.CreateWebSocketClient().ConnectAsync(fixture.WebSocketUri, CancellationToken.None);

        await SendAsync(socket, new { type = "auth", token = ChatWebSocketTestFixture.CreateAccessToken(accountId, sessionId) });
        var response = await ReceiveAsync(socket);

        Assert.Equal("auth_ok", response.GetProperty("type").GetString());
    }

    [Fact]
    public async Task ConversationLifecycle_CreateListRenameDelete()
    {
        var (socket, _) = await ConnectAndAuthenticateAsync();
        using var owned = socket;

        await SendAsync(owned, new { type = "create_conversation", title = "first chat" });
        var created = await ReceiveAsync(owned);
        Assert.Equal("conversation_created", created.GetProperty("type").GetString());
        var conversationId = created.GetProperty("conversation").GetProperty("id").GetString();

        await SendAsync(owned, new { type = "list_conversations" });
        var listed = await ReceiveAsync(owned);
        Assert.Equal("conversation_list", listed.GetProperty("type").GetString());
        Assert.Equal(1, listed.GetProperty("conversations").GetArrayLength());

        await SendAsync(owned, new { type = "rename_conversation", conversationId, title = "renamed chat" });
        var renamed = await ReceiveAsync(owned);
        Assert.Equal("conversation_renamed", renamed.GetProperty("type").GetString());
        Assert.Equal("renamed chat", renamed.GetProperty("title").GetString());

        await SendAsync(owned, new { type = "delete_conversation", conversationId });
        var deleted = await ReceiveAsync(owned);
        Assert.Equal("conversation_deleted", deleted.GetProperty("type").GetString());

        await SendAsync(owned, new { type = "list_conversations" });
        var listedAfterDelete = await ReceiveAsync(owned);
        Assert.Equal(0, listedAfterDelete.GetProperty("conversations").GetArrayLength());
    }

    [Fact]
    public async Task SendMessage_StreamsChunksAndPersistsAssembledMessage()
    {
        fixture.FakeDeepSeekClient.ThrowOnStream = false;
        fixture.FakeDeepSeekClient.ResponseChunks = ["Hel", "lo!"];
        var (socket, _) = await ConnectAndAuthenticateAsync();
        using var owned = socket;
        var conversationId = await CreateConversationAsync(owned);

        await SendAsync(owned, new { type = "send_message", conversationId, content = "hi there" });
        var firstChunk = await ReceiveAsync(owned);
        var secondChunk = await ReceiveAsync(owned);
        var complete = await ReceiveAsync(owned);

        Assert.Equal("message_chunk", firstChunk.GetProperty("type").GetString());
        Assert.Equal("Hel", firstChunk.GetProperty("delta").GetString());
        Assert.Equal("message_chunk", secondChunk.GetProperty("type").GetString());
        Assert.Equal("lo!", secondChunk.GetProperty("delta").GetString());
        Assert.Equal("message_complete", complete.GetProperty("type").GetString());
        Assert.Equal("Hello!", complete.GetProperty("message").GetProperty("content").GetString());

        await SendAsync(owned, new { type = "open_conversation", conversationId });
        var history = await ReceiveAsync(owned);
        var messages = history.GetProperty("messages");
        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal("hi there", messages[0].GetProperty("content").GetString());
        Assert.Equal("Hello!", messages[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task SendMessage_PrependsLanguageSystemPromptWithoutPersistingIt()
    {
        fixture.FakeDeepSeekClient.ThrowOnStream = false;
        fixture.FakeDeepSeekClient.ResponseChunks = ["Hel", "lo!"];
        var (socket, _) = await ConnectAndAuthenticateAsync();
        using var owned = socket;
        var conversationId = await CreateConversationAsync(owned);

        await SendAsync(owned, new { type = "send_message", conversationId, content = "hi there" });
        await ReceiveAsync(owned);
        await ReceiveAsync(owned);
        await ReceiveAsync(owned);

        var sentMessages = fixture.FakeDeepSeekClient.LastMessages;
        Assert.Equal(ChatMessageRole.System, sentMessages[0].Role);
        Assert.Equal("hi there", sentMessages[1].Content);

        await SendAsync(owned, new { type = "open_conversation", conversationId });
        var history = await ReceiveAsync(owned);
        Assert.Equal(2, history.GetProperty("messages").GetArrayLength());
    }

    [Fact]
    public async Task SendMessage_AccountOverBudget_ReturnsErrorWithoutCallingDeepSeek()
    {
        var (socket, accountId) = await ConnectAndAuthenticateAsync();
        using var owned = socket;
        var conversationId = await CreateConversationAsync(owned);

        var tokenBudgetService = new TokenBudgetService(fixture.OwnRedis, new AppConfig { DeepSeek = new DeepSeekConfig { DailyTokenBudgetPerAccount = 100 } });
        await tokenBudgetService.AddUsageAsync(accountId, 100_000);
        var callCountBefore = fixture.FakeDeepSeekClient.CallCount;

        await SendAsync(owned, new { type = "send_message", conversationId, content = "hi there" });
        var response = await ReceiveAsync(owned);

        Assert.Equal("error", response.GetProperty("type").GetString());
        Assert.Equal("daily_token_budget_exceeded", response.GetProperty("error").GetString());
        Assert.Equal(callCountBefore, fixture.FakeDeepSeekClient.CallCount);
    }

    [Fact]
    public async Task SendMessage_DeepSeekFailure_ReturnsErrorWithoutClosingConnection()
    {
        fixture.FakeDeepSeekClient.ThrowOnStream = true;
        var (socket, _) = await ConnectAndAuthenticateAsync();
        using var owned = socket;
        var conversationId = await CreateConversationAsync(owned);

        await SendAsync(owned, new { type = "send_message", conversationId, content = "hi there" });
        var response = await ReceiveAsync(owned);

        Assert.Equal("error", response.GetProperty("type").GetString());
        Assert.Equal("deepseek_unavailable", response.GetProperty("error").GetString());

        fixture.FakeDeepSeekClient.ThrowOnStream = false;
        await SendAsync(owned, new { type = "list_conversations" });
        var listed = await ReceiveAsync(owned);
        Assert.Equal("conversation_list", listed.GetProperty("type").GetString());
        Assert.Equal(WebSocketState.Open, owned.State);
    }

    [Fact]
    public async Task OversizedMessage_ReturnsErrorWithoutClosingConnection()
    {
        var (socket, _) = await ConnectAndAuthenticateAsync();
        using var owned = socket;
        var oversizedTitle = new string('a', ChatWebSocketHandler.MaxMessageSizeBytes + 1024);

        await SendAsync(owned, new { type = "create_conversation", title = oversizedTitle });
        var response = await ReceiveAsync(owned);

        Assert.Equal("error", response.GetProperty("type").GetString());
        Assert.Equal("message_too_large", response.GetProperty("error").GetString());
        Assert.Equal(WebSocketState.Open, owned.State);

        await SendAsync(owned, new { type = "list_conversations" });
        var listed = await ReceiveAsync(owned);
        Assert.Equal("conversation_list", listed.GetProperty("type").GetString());
    }

    [Fact]
    public async Task UnknownMessageType_ReturnsErrorMessage()
    {
        var (socket, _) = await ConnectAndAuthenticateAsync();
        using var owned = socket;

        await SendAsync(owned, new { type = "not_a_real_type" });
        var response = await ReceiveAsync(owned);

        Assert.Equal("error", response.GetProperty("type").GetString());
        Assert.Equal("unknown_type", response.GetProperty("error").GetString());
    }

    private async Task<(WebSocket Socket, Guid AccountId)> ConnectAndAuthenticateAsync()
    {
        var accountId = Guid.NewGuid();
        var sessionId = await fixture.CreateSessionAsync(accountId);
        var socket = await fixture.CreateWebSocketClient().ConnectAsync(fixture.WebSocketUri, CancellationToken.None);

        await SendAsync(socket, new { type = "auth", token = ChatWebSocketTestFixture.CreateAccessToken(accountId, sessionId) });
        var response = await ReceiveAsync(socket);
        Assert.Equal("auth_ok", response.GetProperty("type").GetString());

        return (socket, accountId);
    }

    private async Task<string> CreateConversationAsync(WebSocket socket)
    {
        await SendAsync(socket, new { type = "create_conversation", title = "test conversation" });
        var created = await ReceiveAsync(socket);

        return created.GetProperty("conversation").GetProperty("id").GetString()!;
    }

    private static async Task SendAsync(WebSocket socket, object message)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, WsJson.Options);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
    }

    private static async Task<JsonElement> ReceiveAsync(WebSocket socket)
    {
        var buffer = new byte[16 * 1024];
        using var stream = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, CancellationToken.None);
            stream.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        stream.Position = 0;
        using var document = JsonDocument.Parse(stream);

        return document.RootElement.Clone();
    }

    private static async Task<WebSocketCloseStatus?> WaitForCloseAsync(WebSocket socket)
    {
        var buffer = new byte[1024];
        try
        {
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, CancellationToken.None);
            } while (result.MessageType != WebSocketMessageType.Close);

            return result.CloseStatus;
        }
        catch (WebSocketException)
        {
            return null;
        }
    }
}
