using VsngrpCoreBeWs.Models;
using VsngrpCoreBeWs.Services;

namespace VsngrpCoreBeWs.Tests.Unit;

public sealed class TokenBudgetServiceTests(RedisTestFixture fixture) : IClassFixture<RedisTestFixture>
{
    private TokenBudgetService CreateService(long dailyBudget = 1000) =>
        new(fixture.Redis, new AppConfig { DeepSeek = new DeepSeekConfig { DailyTokenBudgetPerAccount = dailyBudget } });

    [Fact]
    public async Task IsOverBudgetAsync_NoUsageYet_ReturnsFalse()
    {
        var service = CreateService();

        var overBudget = await service.IsOverBudgetAsync(Guid.NewGuid());

        Assert.False(overBudget);
    }

    [Fact]
    public async Task AddUsageAsync_ThenGetUsedTokensAsync_ReturnsAccumulatedTotal()
    {
        var service = CreateService();
        var accountId = Guid.NewGuid();

        await service.AddUsageAsync(accountId, 100);
        await service.AddUsageAsync(accountId, 50);
        var usedTokens = await service.GetUsedTokensAsync(accountId);

        Assert.Equal(150, usedTokens);
    }

    [Fact]
    public async Task AddUsageAsync_ExceedingBudget_IsOverBudgetAsyncReturnsTrue()
    {
        var service = CreateService(dailyBudget: 100);
        var accountId = Guid.NewGuid();

        await service.AddUsageAsync(accountId, 150);
        var overBudget = await service.IsOverBudgetAsync(accountId);

        Assert.True(overBudget);
    }

    [Fact]
    public async Task IsOverBudgetAsync_DoesNotAffectOtherAccounts()
    {
        var service = CreateService(dailyBudget: 100);
        var overBudgetAccount = Guid.NewGuid();
        var unrelatedAccount = Guid.NewGuid();
        await service.AddUsageAsync(overBudgetAccount, 150);

        var unrelatedOverBudget = await service.IsOverBudgetAsync(unrelatedAccount);

        Assert.False(unrelatedOverBudget);
    }
}
