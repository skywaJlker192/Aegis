using Aegis.Authorization;
using Aegis.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Aegis.Pages.Map;

/// <summary>Карта операций: схема территории с агентами в реальном времени. Доступна штабу (модератор / админ).</summary>
[Authorize(Roles = AppRoles.ModeratorOrAdmin)]
[RequireClearance(3)]
public class IndexModel : PageModel
{
    public void OnGet()
    {
    }
}
