using System.ComponentModel.DataAnnotations;
using Aegis.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Aegis.Pages.Account;

[AllowAnonymous]
public class LoginModel : PageModel
{
    private readonly SignInManager<Agent> _signInManager;
    private readonly ILogger<LoginModel> _logger;

    public LoginModel(SignInManager<Agent> signInManager, ILogger<LoginModel> logger)
    {
        _signInManager = signInManager;
        _logger = logger;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public class InputModel
    {
        [Required(ErrorMessage = "Укажите email")]
        [EmailAddress(ErrorMessage = "Некорректный email")]
        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Введите пароль")]
        [DataType(DataType.Password)]
        [Display(Name = "Пароль")]
        public string Password { get; set; } = string.Empty;

        [Display(Name = "Запомнить меня")]
        public bool RememberMe { get; set; }
    }

    public void OnGet()
    {
    }

    // Вход — только POST. Антифорджери-токен проверяется глобальным фильтром.
    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        // lockoutOnFailure: true — после 5 неудачных попыток аккаунт блокируется на 15 минут
        var result = await _signInManager.PasswordSignInAsync(
            Input.Email, Input.Password, Input.RememberMe, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            _logger.LogInformation("Агент {Email} вошёл в систему.", Input.Email);

            // Редиректим только на локальные адреса (защита от open redirect)
            if (!string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
            {
                return LocalRedirect(ReturnUrl);
            }

            return RedirectToPage("/Index");
        }

        if (result.IsLockedOut)
        {
            ModelState.AddModelError(string.Empty, "ДОСТУП ЗАБЛОКИРОВАН: слишком много неудачных попыток. Повторите через 15 минут.");
        }
        else
        {
            ModelState.AddModelError(string.Empty, "ОТКАЗАНО В ДОСТУПЕ: неверный email или пароль.");
        }

        return Page();
    }
}
