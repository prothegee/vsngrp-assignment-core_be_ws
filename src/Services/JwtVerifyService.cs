using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using VsngrpCoreBeWs.Models;

namespace VsngrpCoreBeWs.Services;

public interface IJwtVerifyService
{
    bool TryValidate(string accessToken, out Guid accountId, out string sessionId);
}

public sealed class JwtVerifyService(AppConfig appConfig) : IJwtVerifyService
{
    public bool TryValidate(string accessToken, out Guid accountId, out string sessionId)
    {
        accountId = Guid.Empty;
        sessionId = string.Empty;

        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(appConfig.JwtSecret)),
        };

        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };

        ClaimsPrincipal principal;
        try
        {
            principal = handler.ValidateToken(accessToken, validationParameters, out _);
        }
        catch (SecurityTokenException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }

        var subjectClaim = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        var sessionClaim = principal.FindFirst("sid")?.Value;
        if (string.IsNullOrEmpty(subjectClaim) || string.IsNullOrEmpty(sessionClaim) || !Guid.TryParse(subjectClaim, out var parsedAccountId))
        {
            return false;
        }

        accountId = parsedAccountId;
        sessionId = sessionClaim;

        return true;
    }
}
