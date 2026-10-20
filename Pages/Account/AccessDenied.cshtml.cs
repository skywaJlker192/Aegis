using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Aegis.Pages.Account;

[AllowAnonymous]
public class AccessDeniedModel : PageModel
{
    public void OnGet()
    {
        // Пользователь вошёл, но роли не хватает — отдаём настоящий статус 403
        Response.StatusCode = StatusCodes.Status403Forbidden;
    }
}
