using System.Security.Claims;
using Aegis.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Aegis.Authorization;

/// <summary>Чистая логика допусков (без зависимостей — удобно проверять отдельно).</summary>
public static class ClearanceRules
{
    public const int Min = 1;
    public const int Max = 5;

    /// <summary>Допуск достаточен, если уровень агента не ниже требуемого.</summary>
    public static bool IsSufficient(int agentLevel, int required) => agentLevel >= required;

    /// <summary>Приводит значение из строки запроса к диапазону 1–5 (в URL можно написать что угодно).</summary>
    public static int ClampRequired(int required) => Math.Clamp(required, Min, Max);

    /// <summary>Админ не может сам себе понизить допуск ниже максимального: иначе он потеряет доступ к админ-панели.</summary>
    public static bool IsSelfLockout(bool isSelf, bool isAdmin, int newLevel) => isSelf && isAdmin && newLevel < Max;
}

/// <summary>
/// [RequireClearance(3)] на странице: пускает только агентов с допуском не ниже указанного.
/// Уровень берётся из БД на каждый запрос, поэтому изменение админом действует сразу, без перелогина.
/// Не хватает допуска — редирект на /Account/ClearanceDenied (там статус 403 и анимация).
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class RequireClearanceAttribute : Attribute, IAsyncPageFilter
{
    public int Level { get; }

    public RequireClearanceAttribute(int level)
    {
        Level = ClearanceRules.ClampRequired(level);
    }

    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var http = context.HttpContext;
        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);

        // Неавторизованных отправляем на вход (обычно их раньше останавливает [Authorize])
        if (string.IsNullOrEmpty(userId))
        {
            context.Result = new ChallengeResult();
            return;
        }

        var users = http.RequestServices.GetRequiredService<UserManager<Agent>>();
        var level = await users.Users.Where(u => u.Id == userId).Select(u => u.ClearanceLevel).FirstOrDefaultAsync();

        if (!ClearanceRules.IsSufficient(level, Level))
        {
            context.Result = new RedirectToPageResult("/Account/ClearanceDenied", new { required = Level });
            return;
        }

        await next();
    }
}
