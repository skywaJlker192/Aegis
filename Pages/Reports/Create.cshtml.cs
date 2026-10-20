using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Aegis.Data;
using Aegis.Helpers;
using Aegis.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Aegis.Pages.Reports;

/// <summary>Форма полевого отчёта. Автор берётся из сессии, а не из формы — подделать нельзя.</summary>
[Authorize]
public class CreateModel : PageModel
{
    private readonly AppDbContext _db;

    public CreateModel(AppDbContext db)
    {
        _db = db;
    }

    [BindProperty]
    public ReportInput Input { get; set; } = new();

    /// <summary>/Reports/Create?expeditionId=1 — заранее выбрать экспедицию.</summary>
    [BindProperty(SupportsGet = true, Name = "expeditionId")]
    public int? PreselectedExpeditionId { get; set; }

    /// <summary>Вариант выбора экспедиции. AnomalyId нужен мини-игре сканирования: она сканирует цель экспедиции.</summary>
    public record ExpeditionOption(int Id, int AnomalyId, string AnomalyTitle, string Label);

    public List<ExpeditionOption> ExpeditionOptions { get; private set; } = new();

    public class ReportInput
    {
        [Required(ErrorMessage = "Выберите экспедицию")]
        [Display(Name = "Экспедиция")]
        public int? ExpeditionId { get; set; }

        [Required(ErrorMessage = "Текст отчёта обязателен")]
        [StringLength(2000, MinimumLength = 10, ErrorMessage = "Длина отчёта — от 10 до 2000 символов")]
        [DataType(DataType.MultilineText)]
        [Display(Name = "Описание наблюдений")]
        public string Text { get; set; } = string.Empty;
    }

    private bool IsPrivileged => User.IsInRole(AppRoles.Moderator) || User.IsInRole(AppRoles.Admin);
    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    public async Task OnGetAsync()
    {
        if (PreselectedExpeditionId.HasValue)
        {
            Input.ExpeditionId = PreselectedExpeditionId;
        }

        await LoadOptionsAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            await LoadOptionsAsync();
            return Page();
        }

        // Писать отчёт можно только по экспедиции, в которой участвуешь (модератор/админ — по любой)
        var expeditionId = Input.ExpeditionId!.Value;
        var allowed = await _db.Expeditions.AnyAsync(e =>
            e.Id == expeditionId &&
            (IsPrivileged || e.LeaderId == CurrentUserId || e.Members.Any(m => m.AgentId == CurrentUserId)));

        if (!allowed)
        {
            ModelState.AddModelError("Input.ExpeditionId", "Вы не участвуете в этой экспедиции.");
            await LoadOptionsAsync();
            return Page();
        }

        var report = new FieldReport
        {
            ExpeditionId = expeditionId,
            AuthorId = CurrentUserId,      // автор — всегда текущий пользователь
            Text = Input.Text.Trim(),
            CreatedAt = DateTime.UtcNow,
            IsApproved = false
        };

        _db.FieldReports.Add(report);
        await _db.SaveChangesAsync();

        TempData["Flash"] = "ОТЧЁТ ПРИНЯТ К РАССМОТРЕНИЮ МОДЕРАТОРОМ.";
        return RedirectToPage("/Expeditions/Details", new { id = expeditionId });
    }

    private async Task LoadOptionsAsync()
    {
        var userId = CurrentUserId;
        var query = _db.Expeditions.Include(e => e.Anomaly).AsQueryable();

        if (!IsPrivileged)
        {
            query = query.Where(e => e.LeaderId == userId || e.Members.Any(m => m.AgentId == userId));
        }

        var items = await query
            .OrderByDescending(e => e.StartDate)
            .Select(e => new { e.Id, e.TargetAnomalyId, AnomalyTitle = e.Anomaly.Title })
            .ToListAsync();

        ExpeditionOptions = items
            .Select(e => new ExpeditionOption(e.Id, e.TargetAnomalyId, e.AnomalyTitle, $"{UiText.ExpeditionCode(e.Id)} // {e.AnomalyTitle}"))
            .ToList();
    }
}
