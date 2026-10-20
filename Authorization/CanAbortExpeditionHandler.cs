using System.Security.Claims;
using Aegis.Models;
using Microsoft.AspNetCore.Authorization;

namespace Aegis.Authorization;

/// <summary>Требование политики "CanAbortExpedition" (маркер, данных не несёт).</summary>
public class CanAbortExpeditionRequirement : IAuthorizationRequirement
{
}

/// <summary>
/// Resource-based авторизация: проверяет не роль, а конкретный ресурс (экспедицию).
/// Прервать экспедицию может только её руководитель (Expedition.LeaderId == Id пользователя).
///
/// Использование:
///   var result = await authorizationService.AuthorizeAsync(User, expedition, "CanAbortExpedition");
/// </summary>
public class CanAbortExpeditionHandler : AuthorizationHandler<CanAbortExpeditionRequirement, Expedition>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CanAbortExpeditionRequirement requirement,
        Expedition resource)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!string.IsNullOrEmpty(userId) && resource.LeaderId == userId)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
