using StackExchange.Redis;
using Testcontainers.Redis;

namespace VsngrpCoreBeWs.Tests.Unit;

public sealed class RedisTestFixture : IAsyncLifetime
{
    private readonly RedisContainer container = new RedisBuilder("redis:8").Build();

    public IConnectionMultiplexer Redis { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await container.StartAsync();
        Redis = await ConnectionMultiplexer.ConnectAsync(container.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        await Redis.DisposeAsync();
        await container.DisposeAsync();
    }
}
