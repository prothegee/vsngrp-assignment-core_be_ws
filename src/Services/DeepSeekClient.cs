using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using VsngrpCoreBeWs.Models;

namespace VsngrpCoreBeWs.Services;

public sealed class DeepSeekStreamChunk
{
    public string? Delta { get; init; }
    public bool IsDone { get; init; }
    public long? TotalTokens { get; init; }
}

public interface IDeepSeekClient
{
    IAsyncEnumerable<DeepSeekStreamChunk> StreamCompletionAsync(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken);
}

public sealed class DeepSeekClient : IDeepSeekClient
{
    private readonly HttpClient httpClient;
    private readonly AppConfig appConfig;
    private readonly SemaphoreSlim concurrencyLimiter;

    public DeepSeekClient(HttpClient httpClient, AppConfig appConfig)
    {
        this.httpClient = httpClient;
        this.appConfig = appConfig;
        concurrencyLimiter = new SemaphoreSlim(appConfig.DeepSeek.MaxConcurrentRequests, appConfig.DeepSeek.MaxConcurrentRequests);
    }

    public async IAsyncEnumerable<DeepSeekStreamChunk> StreamCompletionAsync(
        IReadOnlyList<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await concurrencyLimiter.WaitAsync(cancellationToken);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{appConfig.DeepSeek.BaseUrl}/chat/completions");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", appConfig.DeepSeek.ApiKey);
            request.Content = JsonContent.Create(new
            {
                model = appConfig.DeepSeek.Model,
                stream = true,
                messages = messages.Select(message => new { role = message.Role, content = message.Content }),
            });

            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);

            string? line;
            while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
            {
                if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: ", StringComparison.Ordinal))
                {
                    continue;
                }

                var payload = line["data: ".Length..];
                if (payload == "[DONE]")
                {
                    yield break;
                }

                using var document = JsonDocument.Parse(payload);
                var root = document.RootElement;

                if (root.TryGetProperty("usage", out var usageElement)
                    && usageElement.ValueKind == JsonValueKind.Object
                    && usageElement.TryGetProperty("total_tokens", out var totalTokensElement))
                {
                    yield return new DeepSeekStreamChunk { IsDone = true, TotalTokens = totalTokensElement.GetInt64() };
                    continue;
                }

                if (!root.TryGetProperty("choices", out var choicesElement) || choicesElement.GetArrayLength() == 0)
                {
                    continue;
                }

                var delta = choicesElement[0].GetProperty("delta");
                if (delta.TryGetProperty("content", out var contentElement) && contentElement.ValueKind == JsonValueKind.String)
                {
                    yield return new DeepSeekStreamChunk { Delta = contentElement.GetString() };
                }
            }
        }
        finally
        {
            concurrencyLimiter.Release();
        }
    }
}
