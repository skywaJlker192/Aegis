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

namespace Aegis.Pages.Journal;

/// <summary>«Мой журнал»: личные записи агента. Новая запись уходит на модерацию координатору.</summary>
[Authorize]
public class IndexModel : PageModel
{
    private readonly AppDbContext _db;

    public IndexModel(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>Фильтр по типу записи (?type=Alert).</summary>
    [BindProperty(SupportsGet = true)]
    public string? Type { get; set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public class InputModel
    {
        [Display(Name = "ДАТА")]
        [DataType(DataType.Date)]
        public DateTime Date { get; set; } = DateTime.UtcNow.Date;

        [Display(Name = "ЭКСПЕДИЦИЯ")]
        public int? ExpeditionId { get; set; }

        [Display(Name = "ТИП ЗАПИСИ")]
        [EnumDataType(typeof(JournalEntryType))]
        public JournalEntryType EntryType { get; set; } = JournalEntryType.Observation;

        [Display(Name = "ТЕКСТ ЗАПИСИ")]
        [Required(ErrorMessage = "Введите текст записи.")]
        [StringLength(1000, ErrorMessage = "Не более 1000 символов.")]
        public string Text { get; set; } = string.Empty;
    }

    public List<AgentJournal> Entries { get; private set; } = new();
    public List<SelectListItem> ExpeditionOptions { get; private set; } = new();

    /// <summary>Тип из строки запроса -> enum (null = «все типы»).</summary>
    public JournalEntryType? TypeFilter =>
        Enum.TryParse<JournalEntryType>(Type, ignoreCase: true, out var t) && Enum.IsDefined(t) ? t : null;

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
    private bool IsPrivileged => User.IsInRole(AppRoles.Moderator) || User.IsInRole(AppRoles.Admin);

    public async Task OnGetAsync()
    {
        Input.Date = DateTime.UtcNow.Date;
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Input.Text = (Input.Text ?? string.Empty).Trim();
        ModelState.Remove("Input.Text");
        if (string.IsNullOrWhiteSpace(Input.Text))
        {
            ModelState.AddModelError("Input.Text", "Введите текст записи.");
        }
        else if (Input.Text.Length > 1000)
        {
            ModelState.AddModelError("Input.Text", "Не более 1000 символов.");
        }

        if (Input.Date.Date > DateTime.UtcNow.Date)
        {
            ModelState.AddModelError("Input.Date", "Дата записи не может быть в будущем.");
        }

        // Привязать запись можно только к экспедиции, в которой агент участвует (модератор и админ — к любой)
        if (Input.ExpeditionId is int expId)
        {
            var allowed = await _db.Expeditions.AnyAsync(e => e.Id == expId &&
                (IsPrivileged || e.LeaderId == UserId || e.Members.Any(m => m.AgentId == UserId)));
            if (!allowed)
            {
                ModelState.AddModelError("Input.ExpeditionId", "Нет доступа к этой экспедиции.");
            }
        }

        if (!ModelState.IsValid)
        {
            await LoadAsync();
            return Page();
        }

        // Выбранная дата + текущее время, чтобы записи за один день сортировались по порядку добавления
        _db.AgentJournals.Add(new AgentJournal
        {
            AgentId = UserId,
            ExpeditionId = Input.ExpeditionId,
            EntryType = Input.EntryType,
            Text = Input.Text,
            CreatedAt = Input.Date.Date + DateTime.UtcNow.TimeOfDay,
            IsApproved = false,
            IsRejected = false
        });
        await _db.SaveChangesAsync();

        TempData["Flash"] = "Запись добавлена в журнал и отправлена на модерацию.";
        return RedirectToPage(new { type = Type });
    }

    private async Task LoadAsync()
    {
        var query = _db.AgentJournals
            .Include(j => j.Agent)
            .Include(j => j.Expedition)
            .Where(j => j.AgentId == UserId);

        if (TypeFilter is { } filter)
        {
            query = query.Where(j => j.EntryType == filter);
        }

        Entries = await query.OrderByDescending(j => j.CreatedAt).ThenByDescending(j => j.Id).ToListAsync();

        var exps = _db.Expeditions.AsQueryable();
        if (!IsPrivileged)
        {
            exps = exps.Where(e => e.LeaderId == UserId || e.Members.Any(m => m.AgentId == UserId));
        }

        var list = await exps.OrderByDescending(e => e.StartDate).Select(e => new { e.Id, Title = e.Anomaly.Title }).ToListAsync();
        ExpeditionOptions = list.Select(e => new SelectListItem($"{UiText.ExpeditionCode(e.Id)} — {e.Title}", e.Id.ToString())).ToList();
    }
}
