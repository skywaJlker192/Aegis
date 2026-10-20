using System.ComponentModel.DataAnnotations;
using Aegis.Data;
using Aegis.Helpers;
using Aegis.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Aegis.Pages.Expeditions;

/// <summary>Форма создания экспедиции (модератор / админ). Валидация на сервере через Data Annotations.</summary>
[Authorize(Roles = AppRoles.ModeratorOrAdmin)]
public class CreateModel : PageModel
{
    private readonly AppDbContext _db;

    public CreateModel(AppDbContext db)
    {
        _db = db;
    }

    [BindProperty]
    public CreateExpeditionInput Input { get; set; } = new();

    /// <summary>/Expeditions/Create?anomalyId=3 — заранее выбрать цель.</summary>
    [BindProperty(SupportsGet = true)]
    public int? AnomalyId { get; set; }

    public List<SelectListItem> AnomalyOptions { get; private set; } = new();
    public List<SelectListItem> AgentOptions { get; private set; } = new();

    public class CreateExpeditionInput : IValidatableObject
    {
        [Required(ErrorMessage = "Выберите целевую аномалию")]
        [Display(Name = "Целевая аномалия")]
        public int? TargetAnomalyId { get; set; }

        [Required(ErrorMessage = "Назначьте руководителя экспедиции")]
        [Display(Name = "Руководитель")]
        public string? LeaderId { get; set; }

        [Required(ErrorMessage = "Укажите дату начала")]
        [DataType(DataType.Date)]
        [Display(Name = "Дата начала")]
        public DateTime? StartDate { get; set; }

        [DataType(DataType.Date)]
        [Display(Name = "Плановая дата окончания (необязательно)")]
        public DateTime? EndDate { get; set; }

        // Проверка, которую одними атрибутами не выразить: окончание не раньше начала
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (StartDate.HasValue && EndDate.HasValue && EndDate.Value < StartDate.Value)
            {
                yield return new ValidationResult(
                    "Дата окончания не может быть раньше даты начала.",
                    new[] { nameof(EndDate) });
            }
        }
    }

    public async Task OnGetAsync()
    {
        if (AnomalyId.HasValue)
        {
            Input.TargetAnomalyId = AnomalyId;
        }

        Input.StartDate ??= DateTime.Today;
        await LoadOptionsAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            await LoadOptionsAsync();
            return Page();
        }

        // Данные формы могли подделать — проверяем, что аномалия и агент реально существуют
        if (!await _db.Anomalies.AnyAsync(a => a.Id == Input.TargetAnomalyId))
        {
            ModelState.AddModelError("Input.TargetAnomalyId", "Такой аномалии не существует.");
        }

        if (!await _db.Users.AnyAsync(u => u.Id == Input.LeaderId))
        {
            ModelState.AddModelError("Input.LeaderId", "Такого агента не существует.");
        }

        if (!ModelState.IsValid)
        {
            await LoadOptionsAsync();
            return Page();
        }

        var expedition = new Expedition
        {
            TargetAnomalyId = Input.TargetAnomalyId!.Value,
            LeaderId = Input.LeaderId!,
            StartDate = DateTime.SpecifyKind(Input.StartDate!.Value, DateTimeKind.Utc),
            EndDate = Input.EndDate.HasValue ? DateTime.SpecifyKind(Input.EndDate.Value, DateTimeKind.Utc) : null,
            Status = ExpeditionStatus.Planned
        };

        // Руководитель автоматически становится членом экипажа
        expedition.Members.Add(new ExpeditionMember
        {
            AgentId = expedition.LeaderId,
            RoleInMission = "Руководитель",
            IsAlive = true
        });

        _db.Expeditions.Add(expedition);
        await _db.SaveChangesAsync();

        TempData["Flash"] = $"Экспедиция {UiText.ExpeditionCode(expedition.Id)} создана.";
        return RedirectToPage("/Expeditions/Details", new { id = expedition.Id });
    }

    private async Task LoadOptionsAsync()
    {
        AnomalyOptions = (await _db.Anomalies
                .Where(a => a.Status != AnomalyStatus.Neutralized)
                .OrderBy(a => a.Title)
                .Select(a => new { a.Id, a.Title })
                .ToListAsync())
            .Select(a => new SelectListItem($"{UiText.AnomalyCode(a.Id)} // {a.Title}", a.Id.ToString()))
            .ToList();

        AgentOptions = (await _db.Users
                .OrderBy(u => u.Callsign)
                .Select(u => new { u.Id, u.Callsign, u.ClearanceLevel })
                .ToListAsync())
            .Select(u => new SelectListItem($"{u.Callsign} (допуск {u.ClearanceLevel})", u.Id))
            .ToList();
    }
}
