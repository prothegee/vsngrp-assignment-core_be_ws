using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using VsngrpCoreBeWs.Models;
using VsngrpCoreBeWs.Services;

namespace VsngrpCoreBeWs.WebSockets;

public sealed class ChatWebSocketHandler(
    IJwtVerifyService jwtVerifyService,
    ISessionService sessionService,
    IConversationService conversationService,
    IChatLogService chatLogService,
    ITokenBudgetService tokenBudgetService,
    IDeepSeekClient deepSeekClient,
    ILogger<ChatWebSocketHandler> logger)
{
    public const int MaxMessageSizeBytes = 64 * 1024;

    private const int ReceiveBufferSize = 8192;

    public async Task HandleAsync(WebSocket webSocket, CancellationToken cancellationToken)
    {
        var accountId = await AuthenticateAsync(webSocket, cancellationToken);
        if (accountId is null)
        {
            return;
        }

        while (webSocket.State == WebSocketState.Open)
        {
            var (outcome, text) = await ReceiveTextAsync(webSocket, cancellationToken);

            if (outcome == ReceiveOutcome.Close)
            {
                await CloseAsync(webSocket, WebSocketCloseStatus.NormalClosure, "closing", cancellationToken);
                return;
            }

            if (outcome == ReceiveOutcome.Aborted)
            {
                return;
            }

            if (outcome == ReceiveOutcome.TooLarge)
            {
                await SendAsync(webSocket, new ErrorMessage { Error = "message_too_large" }, cancellationToken);
                continue;
            }

            if (text is null)
            {
                continue;
            }

            await DispatchAsync(webSocket, accountId.Value, text, cancellationToken);
        }
    }

    private async Task<Guid?> AuthenticateAsync(WebSocket webSocket, CancellationToken cancellationToken)
    {
        var (outcome, text) = await ReceiveTextAsync(webSocket, cancellationToken);
        if (outcome != ReceiveOutcome.Message || text is null)
        {
            await CloseAsync(webSocket, WebSocketCloseStatus.PolicyViolation, "expected an auth frame first", cancellationToken);
            return null;
        }

        AuthFrame? authFrame;
        try
        {
            using var document = JsonDocument.Parse(text);
            if (!TryGetType(document, out var type) || type != WsMessageType.Auth)
            {
                await SendAsync(webSocket, new AuthErrorMessage { Error = "expected_auth_frame" }, cancellationToken);
                await CloseAsync(webSocket, WebSocketCloseStatus.PolicyViolation, "expected an auth frame first", cancellationToken);
                return null;
            }

            authFrame = document.RootElement.Deserialize<AuthFrame>(WsJson.Options);
        }
        catch (JsonException)
        {
            await SendAsync(webSocket, new AuthErrorMessage { Error = "malformed_auth_frame" }, cancellationToken);
            await CloseAsync(webSocket, WebSocketCloseStatus.PolicyViolation, "malformed auth frame", cancellationToken);
            return null;
        }

        if (authFrame is null
            || string.IsNullOrEmpty(authFrame.Token)
            || !jwtVerifyService.TryValidate(authFrame.Token, out var accountId, out var sessionId)
            || !await sessionService.IsActiveAsync(sessionId, accountId))
        {
            await SendAsync(webSocket, new AuthErrorMessage { Error = "invalid_or_expired_session" }, cancellationToken);
            await CloseAsync(webSocket, WebSocketCloseStatus.PolicyViolation, "invalid or expired session", cancellationToken);
            return null;
        }

        await SendAsync(webSocket, new AuthOkMessage(), cancellationToken);

        return accountId;
    }

    private async Task DispatchAsync(WebSocket webSocket, Guid accountId, string text, CancellationToken cancellationToken)
    {
        string? type;
        JsonElement root;

        try
        {
            using var document = JsonDocument.Parse(text);
            if (!TryGetType(document, out type))
            {
                await SendAsync(webSocket, new ErrorMessage { Error = "missing_type" }, cancellationToken);
                return;
            }

            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            await SendAsync(webSocket, new ErrorMessage { Error = "malformed_message" }, cancellationToken);
            return;
        }

        try
        {
            switch (type)
            {
                case WsMessageType.CreateConversation:
                    await HandleCreateConversationAsync(webSocket, accountId, root, cancellationToken);
                    break;
                case WsMessageType.ListConversations:
                    await HandleListConversationsAsync(webSocket, accountId, cancellationToken);
                    break;
                case WsMessageType.RenameConversation:
                    await HandleRenameConversationAsync(webSocket, accountId, root, cancellationToken);
                    break;
                case WsMessageType.DeleteConversation:
                    await HandleDeleteConversationAsync(webSocket, accountId, root, cancellationToken);
                    break;
                case WsMessageType.OpenConversation:
                    await HandleOpenConversationAsync(webSocket, accountId, root, cancellationToken);
                    break;
                case WsMessageType.SendMessage:
                    await HandleSendMessageAsync(webSocket, accountId, root, cancellationToken);
                    break;
                default:
                    await SendAsync(webSocket, new ErrorMessage { Error = "unknown_type" }, cancellationToken);
                    break;
            }
        }
        catch (JsonException)
        {
            await SendAsync(webSocket, new ErrorMessage { Error = "malformed_message" }, cancellationToken);
        }
    }

    private async Task HandleCreateConversationAsync(WebSocket webSocket, Guid accountId, JsonElement root, CancellationToken cancellationToken)
    {
        var request = root.Deserialize<CreateConversationRequest>(WsJson.Options);
        if (request is null || string.IsNullOrWhiteSpace(request.Title))
        {
            await SendAsync(webSocket, new ErrorMessage { Error = "title_required" }, cancellationToken);
            return;
        }

        var conversation = await conversationService.CreateAsync(accountId, request.Title);
        await SendAsync(webSocket, new ConversationCreatedMessage { Conversation = conversation }, cancellationToken);
    }

    private async Task HandleListConversationsAsync(WebSocket webSocket, Guid accountId, CancellationToken cancellationToken)
    {
        var conversations = await conversationService.ListAsync(accountId);
        await SendAsync(webSocket, new ConversationListMessage { Conversations = conversations }, cancellationToken);
    }

    private async Task HandleRenameConversationAsync(WebSocket webSocket, Guid accountId, JsonElement root, CancellationToken cancellationToken)
    {
        var request = root.Deserialize<RenameConversationRequest>(WsJson.Options);
        if (request is null || string.IsNullOrWhiteSpace(request.Title))
        {
            await SendAsync(webSocket, new ErrorMessage { Error = "title_required" }, cancellationToken);
            return;
        }

        var renamed = await conversationService.RenameAsync(accountId, request.ConversationId, request.Title);
        if (!renamed)
        {
            await SendAsync(webSocket, new ErrorMessage { Error = "conversation_not_found" }, cancellationToken);
            return;
        }

        await SendAsync(webSocket, new ConversationRenamedMessage { ConversationId = request.ConversationId, Title = request.Title }, cancellationToken);
    }

    private async Task HandleDeleteConversationAsync(WebSocket webSocket, Guid accountId, JsonElement root, CancellationToken cancellationToken)
    {
        var request = root.Deserialize<ConversationIdRequest>(WsJson.Options);
        if (request is null)
        {
            await SendAsync(webSocket, new ErrorMessage { Error = "conversation_id_required" }, cancellationToken);
            return;
        }

        var deleted = await conversationService.DeleteAsync(accountId, request.ConversationId);
        if (!deleted)
        {
            await SendAsync(webSocket, new ErrorMessage { Error = "conversation_not_found" }, cancellationToken);
            return;
        }

        await chatLogService.DeleteAsync(accountId, request.ConversationId);
        await SendAsync(webSocket, new ConversationDeletedMessage { ConversationId = request.ConversationId }, cancellationToken);
    }

    private async Task HandleOpenConversationAsync(WebSocket webSocket, Guid accountId, JsonElement root, CancellationToken cancellationToken)
    {
        var request = root.Deserialize<ConversationIdRequest>(WsJson.Options);
        if (request is null)
        {
            await SendAsync(webSocket, new ErrorMessage { Error = "conversation_id_required" }, cancellationToken);
            return;
        }

        var conversation = await conversationService.GetAsync(accountId, request.ConversationId);
        if (conversation is null)
        {
            await SendAsync(webSocket, new ErrorMessage { Error = "conversation_not_found" }, cancellationToken);
            return;
        }

        var history = await chatLogService.GetHistoryAsync(accountId, request.ConversationId);
        await SendAsync(webSocket, new ConversationHistoryMessage { ConversationId = request.ConversationId, Messages = history }, cancellationToken);
    }

    private async Task HandleSendMessageAsync(WebSocket webSocket, Guid accountId, JsonElement root, CancellationToken cancellationToken)
    {
        var request = root.Deserialize<SendMessageRequest>(WsJson.Options);
        if (request is null || string.IsNullOrWhiteSpace(request.Content))
        {
            await SendAsync(webSocket, new ErrorMessage { Error = "content_required" }, cancellationToken);
            return;
        }

        var conversation = await conversationService.GetAsync(accountId, request.ConversationId);
        if (conversation is null)
        {
            await SendAsync(webSocket, new ErrorMessage { Error = "conversation_not_found" }, cancellationToken);
            return;
        }

        if (await tokenBudgetService.IsOverBudgetAsync(accountId))
        {
            await SendAsync(webSocket, new ErrorMessage { Error = "daily_token_budget_exceeded" }, cancellationToken);
            return;
        }

        var history = await chatLogService.GetHistoryAsync(accountId, request.ConversationId);
        var userMessage = new ChatMessage
        {
            Role = ChatMessageRole.User,
            Content = request.Content,
            Timestamp = DateTimeOffset.UtcNow,
        };
        await chatLogService.AppendAsync(accountId, request.ConversationId, userMessage);

        var promptMessages = history.Append(userMessage).ToArray();
        var assembledContent = new StringBuilder();
        long? totalTokens = null;

        try
        {
            await foreach (var chunk in deepSeekClient.StreamCompletionAsync(promptMessages, cancellationToken))
            {
                if (chunk.Delta is not null)
                {
                    assembledContent.Append(chunk.Delta);
                    await SendAsync(webSocket, new MessageChunkMessage { ConversationId = request.ConversationId, Delta = chunk.Delta }, cancellationToken);
                }

                if (chunk.IsDone)
                {
                    totalTokens = chunk.TotalTokens;
                }
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(exception, "deepseek completion failed for account {AccountId}", accountId);
            await SendAsync(webSocket, new ErrorMessage { Error = "deepseek_unavailable" }, cancellationToken);
            return;
        }

        var assistantMessage = new ChatMessage
        {
            Role = ChatMessageRole.Assistant,
            Content = assembledContent.ToString(),
            Timestamp = DateTimeOffset.UtcNow,
        };
        await chatLogService.AppendAsync(accountId, request.ConversationId, assistantMessage);

        if (totalTokens is > 0)
        {
            await tokenBudgetService.AddUsageAsync(accountId, totalTokens.Value);
        }

        await SendAsync(webSocket, new MessageCompleteMessage { ConversationId = request.ConversationId, Message = assistantMessage }, cancellationToken);
    }

    private static bool TryGetType(JsonDocument document, out string? type)
    {
        type = null;
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("type", out var typeElement)
            || typeElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        type = typeElement.GetString();

        return !string.IsNullOrEmpty(type);
    }

    private static async Task<(ReceiveOutcome Outcome, string? Text)> ReceiveTextAsync(WebSocket webSocket, CancellationToken cancellationToken)
    {
        var buffer = new byte[ReceiveBufferSize];
        using var messageStream = new MemoryStream();

        while (true)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await webSocket.ReceiveAsync(buffer, cancellationToken);
            }
            catch (WebSocketException)
            {
                return (ReceiveOutcome.Aborted, null);
            }
            catch (OperationCanceledException)
            {
                return (ReceiveOutcome.Aborted, null);
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                return (ReceiveOutcome.Close, null);
            }

            if (messageStream.Length + result.Count > MaxMessageSizeBytes)
            {
                await DrainUntilEndOfMessageAsync(webSocket, buffer, result, cancellationToken);
                return (ReceiveOutcome.TooLarge, null);
            }

            messageStream.Write(buffer, 0, result.Count);

            if (result.EndOfMessage)
            {
                return (ReceiveOutcome.Message, Encoding.UTF8.GetString(messageStream.ToArray()));
            }
        }
    }

    private static async Task DrainUntilEndOfMessageAsync(
        WebSocket webSocket,
        byte[] buffer,
        WebSocketReceiveResult lastResult,
        CancellationToken cancellationToken)
    {
        var result = lastResult;
        while (!result.EndOfMessage)
        {
            try
            {
                result = await webSocket.ReceiveAsync(buffer, cancellationToken);
            }
            catch (WebSocketException)
            {
                return;
            }
        }
    }

    private static async Task SendAsync(WebSocket webSocket, object message, CancellationToken cancellationToken)
    {
        if (webSocket.State != WebSocketState.Open)
        {
            return;
        }

        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, message.GetType(), WsJson.Options);

        try
        {
            await webSocket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
        }
        catch (WebSocketException)
        {
        }
    }

    private static async Task CloseAsync(WebSocket webSocket, WebSocketCloseStatus status, string description, CancellationToken cancellationToken)
    {
        if (webSocket.State != WebSocketState.Open)
        {
            return;
        }

        try
        {
            await webSocket.CloseAsync(status, description, cancellationToken);
        }
        catch (WebSocketException)
        {
        }
    }

    private enum ReceiveOutcome
    {
        Message,
        Close,
        TooLarge,
        Aborted,
    }
}
