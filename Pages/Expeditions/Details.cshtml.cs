using System.Security.Claims;
using Aegis.Data;
using Aegis.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Aegis.Pages.Expeditions;

/// <summary>Страница экспедиции: статус, экипаж, телеметрия, отчёты. AJAX-действия — в wwwroot/js/expedition.js.</summary>
[Authorize]
public class DetailsModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IAuthorizationService _authorization;

    public DetailsModel(AppDbContext db, IAuthorizationService authorization)
    {
        _db = db;
        _authorization = authorization;
    }

    public Expedition Expedition { get; private set; } = null!;

    /// <summary>Общий журнал экспедиции: только записи агентов, одобренные координатором.</summary>
    public List<AgentJournal> JournalEntries { get; private set; } = new();

    /// <summary>Позывной текущего пользователя: чат по нему отличает свои сообщения от чужих.</summary>
    public string MyCallsign { get; private set; } = string.Empty;

    /// <summary>Результат политики CanAbortExpedition: кнопка «Прервать» показывается только если true.</summary>
    public bool CanAbort { get; private set; }

    /// <summary>Руководитель, модератор и админ могут менять состав экипажа.</summary>
    public bool CanManageCrew { get; private set; }

    public bool IsModerator => User.IsInRole(AppRoles.Moderator) || User.IsInRole(AppRoles.Admin);

    /// <summary>Агенты, которых ещё нет в экипаже (для выпадающего списка).</summary>
    public List<Agent> AvailableAgents { get; private set; } = new();

    public static readonly string[] RoleOptions = { "Связной", "Подрывник", "Медик", "Разведчик", "Аналитик", "Инженер" };

    public bool IsClosed => Expedition.Status is ExpeditionStatus.Completed or ExpeditionStatus.Aborted;

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var expedition = await _db.Expeditions
            .Include(e => e.Anomaly)
            .Include(e => e.Leader)
            .Include(e => e.Members).ThenInclude(m => m.Agent)
            .Include(e => e.Reports).ThenInclude(r => r.Author)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (expedition is null)
        {
            return NotFound();
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var isParticipant = expedition.LeaderId == userId || expedition.Members.Any(m => m.AgentId == userId);

        // Чужие экспедиции видят только модераторы и админы
        if (!isParticipant && !IsModerator)
        {
            return Forbid();
        }

        Expedition = expedition;

        JournalEntries = await _db.AgentJournals
            .Include(j => j.Agent)
            .Where(j => j.ExpeditionId == id && j.IsApproved)
            .OrderByDescending(j => j.CreatedAt)
            .ToListAsync();

        MyCallsign = await _db.Users.Where(u => u.Id == userId).Select(u => u.Callsign).FirstOrDefaultAsync() ?? string.Empty;

        // Resource-based авторизация: можно ли именно этому пользователю прервать именно эту экспедицию
        var authResult = await _authorization.AuthorizeAsync(User, expedition, "CanAbortExpedition");
        CanAbort = authResult.Succeeded && !IsClosed;

        CanManageCrew = (expedition.LeaderId == userId || IsModerator) && !IsClosed;

        if (CanManageCrew)
        {
            var memberIds = expedition.Members.Select(m => m.AgentId).ToList();
            AvailableAgents = await _db.Users
                .Where(u => !memberIds.Contains(u.Id))
                .OrderBy(u => u.Callsign)
                .ToListAsync();
        }

        return Page();
    }
}
