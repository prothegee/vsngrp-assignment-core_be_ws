using System.Text.Json;

namespace VsngrpCoreBeWs.Models;

public static class WsJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

public static class WsMessageType
{
    public const string Auth = "auth";
    public const string AuthOk = "auth_ok";
    public const string AuthError = "auth_error";
    public const string Error = "error";
    public const string CreateConversation = "create_conversation";
    public const string ListConversations = "list_conversations";
    public const string RenameConversation = "rename_conversation";
    public const string DeleteConversation = "delete_conversation";
    public const string OpenConversation = "open_conversation";
    public const string SendMessage = "send_message";
    public const string ConversationCreated = "conversation_created";
    public const string ConversationList = "conversation_list";
    public const string ConversationRenamed = "conversation_renamed";
    public const string ConversationDeleted = "conversation_deleted";
    public const string ConversationHistory = "conversation_history";
    public const string MessageChunk = "message_chunk";
    public const string MessageComplete = "message_complete";
}

public sealed class AuthFrame
{
    public string Token { get; set; } = string.Empty;
}

public sealed class CreateConversationRequest
{
    public string Title { get; set; } = string.Empty;
}

public sealed class RenameConversationRequest
{
    public Guid ConversationId { get; set; }
    public string Title { get; set; } = string.Empty;
}

public sealed class ConversationIdRequest
{
    public Guid ConversationId { get; set; }
}

public sealed class SendMessageRequest
{
    public Guid ConversationId { get; set; }
    public string Content { get; set; } = string.Empty;
}

public sealed class AuthOkMessage
{
    public string Type => WsMessageType.AuthOk;
}

public sealed class AuthErrorMessage
{
    public string Type => WsMessageType.AuthError;
    public string Error { get; set; } = string.Empty;
}

public sealed class ErrorMessage
{
    public string Type => WsMessageType.Error;
    public string Error { get; set; } = string.Empty;
}

public sealed class ConversationCreatedMessage
{
    public string Type => WsMessageType.ConversationCreated;
    public Conversation Conversation { get; set; } = null!;
}

public sealed class ConversationListMessage
{
    public string Type => WsMessageType.ConversationList;
    public IReadOnlyList<Conversation> Conversations { get; set; } = [];
}

public sealed class ConversationRenamedMessage
{
    public string Type => WsMessageType.ConversationRenamed;
    public Guid ConversationId { get; set; }
    public string Title { get; set; } = string.Empty;
}

public sealed class ConversationDeletedMessage
{
    public string Type => WsMessageType.ConversationDeleted;
    public Guid ConversationId { get; set; }
}

public sealed class ConversationHistoryMessage
{
    public string Type => WsMessageType.ConversationHistory;
    public Guid ConversationId { get; set; }
    public IReadOnlyList<ChatMessage> Messages { get; set; } = [];
}

public sealed class MessageChunkMessage
{
    public string Type => WsMessageType.MessageChunk;
    public Guid ConversationId { get; set; }
    public string Delta { get; set; } = string.Empty;
}

public sealed class MessageCompleteMessage
{
    public string Type => WsMessageType.MessageComplete;
    public Guid ConversationId { get; set; }
    public ChatMessage Message { get; set; } = null!;
}
