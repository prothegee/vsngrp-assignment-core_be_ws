using System.Net;
using System.Text;
using System.Text.Json;
using VsngrpCoreBeWs.Models;
using VsngrpCoreBeWs.Services;

namespace VsngrpCoreBeWs.Tests.Unit;

public sealed class DeepSeekClientTests
{
    [Fact]
    public async Task StreamCompletionAsync_ParsesDeltaChunksAndFinalUsage()
    {
        var handler = new FakeHttpMessageHandler(_ => Task.FromResult(BuildSseResponse(["Hello", ", world"], totalTokens: 12)));
        var client = new DeepSeekClient(new HttpClient(handler), CreateConfig());

        var chunks = new List<DeepSeekStreamChunk>();
        await foreach (var chunk in client.StreamCompletionAsync([UserMessage("hi")], CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        Assert.Equal("Hello", chunks[0].Delta);
        Assert.Equal(", world", chunks[1].Delta);
        Assert.True(chunks[^1].IsDone);
        Assert.Equal(12, chunks[^1].TotalTokens);
    }

    [Fact]
    public async Task StreamCompletionAsync_NonSuccessStatusCode_ThrowsHttpRequestException()
    {
        var handler = new FakeHttpMessageHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        var client = new DeepSeekClient(new HttpClient(handler), CreateConfig());

        await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await foreach (var chunk in client.StreamCompletionAsync([UserMessage("hi")], CancellationToken.None))
            {
                _ = chunk;
            }
        });
    }

    [Fact]
    public async Task StreamCompletionAsync_ConcurrencyCap_QueuesRequestsInsteadOfErroring()
    {
        var gate = new SemaphoreSlim(0);
        var lockObject = new object();
        var concurrentCount = 0;
        var maxObservedConcurrency = 0;

        var handler = new FakeHttpMessageHandler(async _ =>
        {
            lock (lockObject)
            {
                concurrentCount++;
                maxObservedConcurrency = Math.Max(maxObservedConcurrency, concurrentCount);
            }

            await gate.WaitAsync();

            lock (lockObject)
            {
                concurrentCount--;
            }

            return BuildSseResponse(["ok"], totalTokens: 1);
        });
        var client = new DeepSeekClient(new HttpClient(handler), CreateConfig(maxConcurrentRequests: 1));

        var firstCall = ConsumeAsync(client);
        var secondCall = ConsumeAsync(client);
        await Task.Delay(200);

        Assert.Equal(1, maxObservedConcurrency);

        gate.Release(2);
        await Task.WhenAll(firstCall, secondCall);

        Assert.Equal(1, maxObservedConcurrency);
    }

    private static AppConfig CreateConfig(int maxConcurrentRequests = 200) => new()
    {
        DeepSeek = new DeepSeekConfig
        {
            ApiKey = "test-key",
            BaseUrl = "http://localhost",
            Model = "deepseek-v4-flash",
            MaxConcurrentRequests = maxConcurrentRequests,
        },
    };

    private static ChatMessage UserMessage(string content) => new()
    {
        Role = ChatMessageRole.User,
        Content = content,
        Timestamp = DateTimeOffset.UtcNow,
    };

    private static async Task ConsumeAsync(DeepSeekClient client)
    {
        await foreach (var chunk in client.StreamCompletionAsync([UserMessage("hi")], CancellationToken.None))
        {
            _ = chunk;
        }
    }

    private static HttpResponseMessage BuildSseResponse(string[] deltas, long totalTokens)
    {
        var builder = new StringBuilder();
        foreach (var delta in deltas)
        {
            var chunk = JsonSerializer.Serialize(new { choices = new[] { new { delta = new { content = delta } } } });
            builder.Append("data: ").Append(chunk).Append('\n');
        }

        var usageChunk = JsonSerializer.Serialize(new { usage = new { total_tokens = totalTokens } });
        builder.Append("data: ").Append(usageChunk).Append('\n');
        builder.Append("data: [DONE]\n");

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(builder.ToString()) };
    }

    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request);
    }
}
