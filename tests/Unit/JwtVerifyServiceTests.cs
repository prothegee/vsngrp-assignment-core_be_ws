using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using VsngrpCoreBeWs.Models;
using VsngrpCoreBeWs.Services;

namespace VsngrpCoreBeWs.Tests.Unit;

public sealed class JwtVerifyServiceTests
{
    private const string Secret = "unit-test-jwt-secret-value-not-real";

    private static JwtVerifyService CreateService() => new(new AppConfig { JwtSecret = Secret });

    [Fact]
    public void TryValidate_ValidToken_ReturnsAccountIdAndSessionId()
    {
        var service = CreateService();
        var accountId = Guid.NewGuid();

        var token = CreateToken(accountId, "session-abc", Secret, DateTime.UtcNow.AddMinutes(15));
        var valid = service.TryValidate(token, out var resultAccountId, out var resultSessionId);

        Assert.True(valid);
        Assert.Equal(accountId, resultAccountId);
        Assert.Equal("session-abc", resultSessionId);
    }

    [Fact]
    public void TryValidate_ExpiredToken_ReturnsFalse()
    {
        var service = CreateService();

        var token = CreateToken(Guid.NewGuid(), "session-abc", Secret, DateTime.UtcNow.AddMinutes(-15));
        var valid = service.TryValidate(token, out _, out _);

        Assert.False(valid);
    }

    [Fact]
    public void TryValidate_WrongSecret_ReturnsFalse()
    {
        var service = CreateService();

        var token = CreateToken(Guid.NewGuid(), "session-abc", "a-completely-different-secret-value", DateTime.UtcNow.AddMinutes(15));
        var valid = service.TryValidate(token, out _, out _);

        Assert.False(valid);
    }

    [Fact]
    public void TryValidate_MissingSessionClaim_ReturnsFalse()
    {
        var service = CreateService();
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
        var claims = new[] { new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()) };
        var token = new JwtSecurityToken(claims: claims, expires: DateTime.UtcNow.AddMinutes(15), signingCredentials: credentials);
        var accessToken = new JwtSecurityTokenHandler().WriteToken(token);

        var valid = service.TryValidate(accessToken, out _, out _);

        Assert.False(valid);
    }

    [Fact]
    public void TryValidate_MalformedToken_ReturnsFalse()
    {
        var service = CreateService();

        var valid = service.TryValidate("not-a-real-jwt", out _, out _);

        Assert.False(valid);
    }

    private static string CreateToken(Guid accountId, string sessionId, string secret, DateTime expires)
    {
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, accountId.ToString()),
            new Claim("sid", sessionId),
        };
        var token = new JwtSecurityToken(claims: claims, expires: expires, signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
