using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace VsngrpCoreBeWs.Tests.Integration;

public sealed class ChatWebSocketTestFixture : IAsyncLifetime
{
    public const string JwtSecret = "integration-test-jwt-secret-value-not-real";

    private readonly RedisContainer ownRedisContainer = new RedisBuilder("redis:8").Build();
    private readonly RedisContainer sessionRedisContainer = new RedisBuilder("redis:8").Build();
    private string configPath = string.Empty;
    private CoreBeWsWebApplicationFactory factory = null!;

    public FakeDeepSeekClient FakeDeepSeekClient { get; } = new();
    public IConnectionMultiplexer OwnRedis { get; private set; } = null!;
    public IConnectionMultiplexer SessionRedis { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(ownRedisContainer.StartAsync(), sessionRedisContainer.StartAsync());

        OwnRedis = await ConnectionMultiplexer.ConnectAsync(ownRedisContainer.GetConnectionString());
        SessionRedis = await ConnectionMultiplexer.ConnectAsync(sessionRedisContainer.GetConnectionString());

        configPath = Path.Combine(Path.GetTempPath(), $"vsngrp-core-be-ws-test-config-{Guid.NewGuid():N}.json");
        var config = new
        {
            port = 0,
            version = "0.1.0-test",
            jwtSecret = JwtSecret,
            redis = new { connectionString = ownRedisContainer.GetConnectionString() },
            sessionRedis = new { connectionString = sessionRedisContainer.GetConnectionString() },
            deepSeek = new
            {
                apiKey = "test-key",
                baseUrl = "http://localhost",
                model = "deepseek-v4-flash",
                dailyTokenBudgetPerAccount = 100000,
                maxConcurrentRequests = 200,
            },
            corsAllowedOrigins = new[] { "http://localhost:9003" },
        };
        await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(config));

        factory = new CoreBeWsWebApplicationFactory(configPath, FakeDeepSeekClient);
    }

    public async Task DisposeAsync()
    {
        await factory.DisposeAsync();
        File.Delete(configPath);
        await OwnRedis.DisposeAsync();
        await SessionRedis.DisposeAsync();
        await Task.WhenAll(ownRedisContainer.DisposeAsync().AsTask(), sessionRedisContainer.DisposeAsync().AsTask());
    }

    public WebSocketClient CreateWebSocketClient() => factory.Server.CreateWebSocketClient();

    public Uri WebSocketUri => new(factory.Server.BaseAddress, "/ws/chat");

    public async Task<string> CreateSessionAsync(Guid accountId)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        await SessionRedis.GetDatabase().StringSetAsync($"session:{sessionId}", accountId.ToString(), TimeSpan.FromDays(7));

        return sessionId;
    }

    public static string CreateAccessToken(Guid accountId, string sessionId, TimeSpan? expiresIn = null)
    {
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, accountId.ToString()),
            new Claim("sid", sessionId),
        };
        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.Add(expiresIn ?? TimeSpan.FromMinutes(15)),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
