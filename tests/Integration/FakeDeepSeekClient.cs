using System.Runtime.CompilerServices;
using VsngrpCoreBeWs.Models;
using VsngrpCoreBeWs.Services;

namespace VsngrpCoreBeWs.Tests.Integration;

public sealed class FakeDeepSeekClient : IDeepSeekClient
{
    public string[] ResponseChunks { get; set; } = ["Hello", ", world!"];
    public long TotalTokens { get; set; } = 42;
    public bool ThrowOnStream { get; set; }
    public int CallCount { get; private set; }
    public IReadOnlyList<ChatMessage> LastMessages { get; private set; } = [];

    public async IAsyncEnumerable<DeepSeekStreamChunk> StreamCompletionAsync(
        IReadOnlyList<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        CallCount++;
        LastMessages = messages;

        if (ThrowOnStream)
        {
            throw new HttpRequestException("simulated deepseek failure");
        }

        foreach (var chunk in ResponseChunks)
        {
            await Task.Yield();
            yield return new DeepSeekStreamChunk { Delta = chunk };
        }

        yield return new DeepSeekStreamChunk { IsDone = true, TotalTokens = TotalTokens };
    }
}
