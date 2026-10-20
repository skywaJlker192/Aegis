using System.ComponentModel.DataAnnotations;
using Aegis.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Aegis.Pages.Account;

[AllowAnonymous]
public class RegisterModel : PageModel
{
    private readonly UserManager<Agent> _userManager;
    private readonly SignInManager<Agent> _signInManager;
    private readonly ILogger<RegisterModel> _logger;

    public RegisterModel(UserManager<Agent> userManager, SignInManager<Agent> signInManager, ILogger<RegisterModel> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _logger = logger;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public class InputModel
    {
        [Required(ErrorMessage = "Укажите email")]
        [EmailAddress(ErrorMessage = "Некорректный email")]
        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Укажите позывной")]
        [StringLength(50, MinimumLength = 2, ErrorMessage = "Позывной: от 2 до 50 символов")]
        [Display(Name = "Позывной")]
        public string Callsign { get; set; } = string.Empty;

        [Required(ErrorMessage = "Введите пароль")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "Пароль: минимум 6 символов")]
        [DataType(DataType.Password)]
        [Display(Name = "Пароль (минимум 6 символов, есть цифра)")]
        public string Password { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        [Display(Name = "Повторите пароль")]
        [Compare(nameof(Password), ErrorMessage = "Пароли не совпадают")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var callsign = Input.Callsign.Trim();

        if (await _userManager.Users.AnyAsync(u => u.Callsign == callsign))
        {
            ModelState.AddModelError("Input.Callsign", "Этот позывной уже занят.");
            return Page();
        }

        var agent = new Agent
        {
            UserName = Input.Email,
            Email = Input.Email,
            Callsign = callsign,
            ClearanceLevel = 1,
            Status = AgentStatus.Active
        };

        var result = await _userManager.CreateAsync(agent, Input.Password);

        if (!result.Succeeded)
        {
            // Ошибки Identity (слабый пароль, занятый email) показываем в форме
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return Page();
        }

        // Каждый новый пользователь автоматически получает роль Agent
        await _userManager.AddToRoleAsync(agent, AppRoles.Agent);
        _logger.LogInformation("Зарегистрирован агент {Email} ({Callsign}).", agent.Email, agent.Callsign);

        // После регистрации — сразу входим
        await _signInManager.SignInAsync(agent, isPersistent: false);

        return RedirectToPage("/Index");
    }
}
