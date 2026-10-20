using System.Security.Claims;
using Aegis.Authorization;
using Aegis.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Aegis.Pages.Admin;

/// <summary>
/// Админ-панель. Закрыта атрибутом [Authorize(Roles = "Admin")]:
/// неавторизованный -> редирект на логин, вошёл без роли Admin -> 403.
/// </summary>
[Authorize(Roles = AppRoles.Admin)]
[RequireClearance(5)]
public class IndexModel : PageModel
{
    private readonly UserManager<Agent> _userManager;

    public IndexModel(UserManager<Agent> userManager)
    {
        _userManager = userManager;
    }

    public record UserRow(string Id, string Email, string Callsign, int ClearanceLevel, AgentStatus Status, List<string> Roles);

    public List<UserRow> Users { get; private set; } = new();

    /// <summary>Id текущего админа: в его строке нельзя выставить допуск ниже 5.</summary>
    public string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    public async Task OnGetAsync()
    {
        var agents = await _userManager.Users.OrderBy(u => u.Callsign).ToListAsync();

        foreach (var agent in agents)
        {
            var roles = (await _userManager.GetRolesAsync(agent)).ToList();
            Users.Add(new UserRow(agent.Id, agent.Email ?? string.Empty, agent.Callsign, agent.ClearanceLevel, agent.Status, roles));
        }
    }

    /// <summary>Назначить роль.</summary>
    public async Task<IActionResult> OnPostAddRoleAsync(string userId, string role)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null || !AppRoles.All.Contains(role))
        {
            TempData["Error"] = "Агент или роль не найдены.";
            return RedirectToPage();
        }

        if (!await _userManager.IsInRoleAsync(user, role))
        {
            await _userManager.AddToRoleAsync(user, role);
        }

        TempData["Flash"] = $"Агенту {user.Callsign} назначена роль {role}.";
        return RedirectToPage();
    }

    /// <summary>Снять роль.</summary>
    public async Task<IActionResult> OnPostRemoveRoleAsync(string userId, string role)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null || !AppRoles.All.Contains(role))
        {
            TempData["Error"] = "Агент или роль не найдены.";
            return RedirectToPage();
        }

        // Защита от «самоблокировки»: админ не может снять роль Admin сам с себя
        if (role == AppRoles.Admin && user.Id == _userManager.GetUserId(User))
        {
            TempData["Error"] = "Нельзя снять роль Admin с самого себя.";
            return RedirectToPage();
        }

        if (await _userManager.IsInRoleAsync(user, role))
        {
            await _userManager.RemoveFromRoleAsync(user, role);
        }

        TempData["Flash"] = $"У агента {user.Callsign} снята роль {role}.";
        return RedirectToPage();
    }

    /// <summary>Изменить уровень допуска (1–5).</summary>
    public async Task<IActionResult> OnPostClearanceAsync(string userId, int level)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null || level < 1 || level > 5)
        {
            TempData["Error"] = "Уровень допуска должен быть от 1 до 5.";
            return RedirectToPage();
        }

        // Защита от самоблокировки: админ не может сам себе понизить допуск ниже 5
        var isSelf = user.Id == User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (ClearanceRules.IsSelfLockout(isSelf, User.IsInRole(AppRoles.Admin), level))
        {
            TempData["Error"] = "Нельзя понизить собственный допуск ниже 5: вы потеряете доступ к админ-панели.";
            return RedirectToPage();
        }

        user.ClearanceLevel = level;
        await _userManager.UpdateAsync(user);

        TempData["Flash"] = $"Агенту {user.Callsign} установлен допуск уровня {level}.";
        return RedirectToPage();
    }
}
