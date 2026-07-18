using StackExchange.Redis;

namespace VsngrpCoreBeWs.Services;

public interface ISessionService
{
    Task<bool> IsActiveAsync(string sessionId, Guid accountId);
}

public sealed class SessionService([FromKeyedServices("session")] IConnectionMultiplexer sessionRedis) : ISessionService
{
    private const string SessionKeyPrefix = "session:";

    public async Task<bool> IsActiveAsync(string sessionId, Guid accountId)
    {
        var database = sessionRedis.GetDatabase();
        var value = await database.StringGetAsync(SessionKeyPrefix + sessionId);
        if (value.IsNullOrEmpty)
        {
            return false;
        }

        return Guid.TryParse(value.ToString(), out var storedAccountId) && storedAccountId == accountId;
    }
}
