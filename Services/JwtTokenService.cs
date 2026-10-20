using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Aegis.Models;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Aegis.Services;

/// <summary>Настройки JWT из раздела "Jwt" в appsettings.json.</summary>
public class JwtSettings
{
    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public int ExpireMinutes { get; set; } = 30;
}

public interface IJwtTokenService
{
    (string Token, DateTime ExpiresAtUtc) CreateToken(Agent user, IEnumerable<string> roles);
}

/// <summary>Выпускает JWT с claims: UserId, Email, Roles. Срок жизни — из настроек (30 минут).</summary>
public class JwtTokenService : IJwtTokenService
{
    private readonly JwtSettings _settings;

    public JwtTokenService(IOptions<JwtSettings> settings)
    {
        _settings = settings.Value;
    }

    public (string Token, DateTime ExpiresAtUtc) CreateToken(Agent user, IEnumerable<string> roles)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),   // стандартный id — по нему сервер определяет пользователя
            new("UserId", user.Id),                    // то же самое под понятным именем
            new(ClaimTypes.Email, user.Email ?? string.Empty),
            new(ClaimTypes.Name, user.UserName ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        // Каждая роль — отдельный claim
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Key));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(_settings.ExpireMinutes);

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
