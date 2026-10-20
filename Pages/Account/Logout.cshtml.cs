using Aegis.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Aegis.Pages.Account;

/// <summary>
/// Выход выполняется ТОЛЬКО через POST (с антифорджери-токеном), иначе любую
/// страницу можно было бы «разлогинить» простой картинкой или ссылкой.
/// </summary>
public class LogoutModel : PageModel
{
    private readonly SignInManager<Agent> _signInManager;
    private readonly ILogger<LogoutModel> _logger;

    public LogoutModel(SignInManager<Agent> signInManager, ILogger<LogoutModel> logger)
    {
        _signInManager = signInManager;
        _logger = logger;
    }

    // GET ничего не делает: просто отправляем на главную
    public IActionResult OnGet() => RedirectToPage("/Index");

    public async Task<IActionResult> OnPostAsync()
    {
        await _signInManager.SignOutAsync();
        _logger.LogInformation("Агент вышел из системы.");
        return RedirectToPage("/Account/Login");
    }
}
