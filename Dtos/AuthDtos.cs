using System.ComponentModel.DataAnnotations;

namespace Aegis.Dtos;

/// <summary>Запрос на получение JWT.</summary>
public class LoginRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}

/// <summary>Ответ с выданным токеном.</summary>
public class LoginResponse
{
    /// <summary>JWT, его нужно передавать в заголовке Authorization: Bearer ...</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>Когда токен перестанет действовать (UTC). Срок жизни — 30 минут.</summary>
    public DateTime ExpiresAtUtc { get; set; }

    public string Email { get; set; } = string.Empty;
    public string[] Roles { get; set; } = Array.Empty<string>();
}
