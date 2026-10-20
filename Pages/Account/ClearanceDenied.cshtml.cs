using System.Security.Claims;
using Aegis.Authorization;
using Aegis.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Aegis.Pages.Account;

/// <summary>Сюда попадает агент, которому не хватило уровня допуска ([RequireClearance]).</summary>
[Authorize]
public class ClearanceDeniedModel : PageModel
{
    private readonly UserManager<Agent> _userManager;

    public ClearanceDeniedModel(UserManager<Agent> userManager)
    {
        _userManager = userManager;
    }

    public int Required { get; private set; }
    public int Current { get; private set; }

    public async Task OnGetAsync(int required)
    {
        Required = ClearanceRules.ClampRequired(required);

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        Current = await _userManager.Users.Where(u => u.Id == userId).Select(u => u.ClearanceLevel).FirstOrDefaultAsync();

        // Настоящий статус 403; layout по ViewData запускает анимацию «ДОСТУП ЗАПРЕЩЁН»
        Response.StatusCode = StatusCodes.Status403Forbidden;
        ViewData["ClearanceRequired"] = Required;
    }
}
