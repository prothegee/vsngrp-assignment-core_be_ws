using StackExchange.Redis;
using VsngrpCoreBeWs.Models;

namespace VsngrpCoreBeWs.Services;

public interface ITokenBudgetService
{
    Task<bool> IsOverBudgetAsync(Guid accountId);
    Task<long> GetUsedTokensAsync(Guid accountId);
    Task AddUsageAsync(Guid accountId, long tokens);
}

public sealed class TokenBudgetService(
    [FromKeyedServices("own")] IConnectionMultiplexer redis,
    AppConfig appConfig) : ITokenBudgetService
{
    private const string KeyPrefix = "token_budget:";
    private static readonly TimeSpan KeyLifetime = TimeSpan.FromHours(25);

    private IDatabase Database => redis.GetDatabase();

    public async Task<bool> IsOverBudgetAsync(Guid accountId)
    {
        var usedTokens = await GetUsedTokensAsync(accountId);

        return usedTokens >= appConfig.DeepSeek.DailyTokenBudgetPerAccount;
    }

    public async Task<long> GetUsedTokensAsync(Guid accountId)
    {
        var value = await Database.StringGetAsync(BuildKey(accountId));

        return value.IsNullOrEmpty ? 0 : (long)value;
    }

    public async Task AddUsageAsync(Guid accountId, long tokens)
    {
        var key = BuildKey(accountId);
        await Database.StringIncrementAsync(key, tokens);
        await Database.KeyExpireAsync(key, KeyLifetime);
    }

    private static string BuildKey(Guid accountId) => $"{KeyPrefix}{accountId:N}:{DateTimeOffset.UtcNow:yyyyMMdd}";
}
