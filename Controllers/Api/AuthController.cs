using Aegis.Dtos;
using Aegis.Models;
using Aegis.Services;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Aegis.Controllers.Api;

/// <summary>Выдача JWT-токенов для REST API.</summary>
[ApiController]
[ApiVersionNeutral]
[AllowAnonymous]
[IgnoreAntiforgeryToken] // JWT передаётся в заголовке, cookie нет — CSRF здесь невозможен
[Route("api/auth")]
[Produces("application/json")]
public class AuthController : ControllerBase
{
    private readonly UserManager<Agent> _userManager;
    private readonly SignInManager<Agent> _signInManager;
    private readonly IJwtTokenService _tokenService;

    public AuthController(UserManager<Agent> userManager, SignInManager<Agent> signInManager, IJwtTokenService tokenService)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _tokenService = tokenService;
    }

    /// <summary>Вход по email и паролю. Возвращает JWT со сроком жизни 30 минут.</summary>
    /// <remarks>Токен нужно передавать в заголовке <c>Authorization: Bearer {token}</c>.</remarks>
    /// <response code="200">Токен выдан.</response>
    /// <response code="401">Неверный email или пароль.</response>
    /// <response code="423">Учётная запись временно заблокирована после 5 неудачных попыток.</response>
    [HttpPost("login")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status423Locked)]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user is null)
        {
            return Problem(statusCode: StatusCodes.Status401Unauthorized,
                title: "Отказано в доступе", detail: "Неверный email или пароль.");
        }

        // lockoutOnFailure: true — неудачные попытки считаются, после 5-й аккаунт блокируется на 15 минут
        var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);

        if (result.IsLockedOut)
        {
            return Problem(statusCode: StatusCodes.Status423Locked,
                title: "Учётная запись заблокирована", detail: "Слишком много неудачных попыток. Повторите через 15 минут.");
        }

        if (!result.Succeeded)
        {
            return Problem(statusCode: StatusCodes.Status401Unauthorized,
                title: "Отказано в доступе", detail: "Неверный email или пароль.");
        }

        var roles = await _userManager.GetRolesAsync(user);
        var (token, expires) = _tokenService.CreateToken(user, roles);

        return Ok(new LoginResponse
        {
            Token = token,
            ExpiresAtUtc = expires,
            Email = user.Email ?? string.Empty,
            Roles = roles.ToArray()
        });
    }
}
