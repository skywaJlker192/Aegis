using System.Security.Claims;
using Aegis.Data;
using Aegis.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Aegis.Pages.Expeditions;

/// <summary>
/// Список экспедиций. Для агента — «Мои миссии» (только где он участвует);
/// модератор и админ видят все («Управление экспедициями»).
/// </summary>
[Authorize]
public class IndexModel : PageModel
{
    private readonly AppDbContext _db;

    public IndexModel(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>/Expeditions?mine=true — показать только мои экспедиции.</summary>
    [BindProperty(SupportsGet = true)]
    public bool Mine { get; set; }

    public List<Expedition> Expeditions { get; private set; } = new();

    /// <summary>Непрочитанные сообщения чата по экспедициям (чужие, IsRead = false).</summary>
    public Dictionary<int, int> Unread { get; private set; } = new();

    public bool IsPrivileged => User.IsInRole(AppRoles.Moderator) || User.IsInRole(AppRoles.Admin);

    /// <summary>Показываем только свои: агент всегда, привилегированный — если нажал «Мои миссии».</summary>
    public bool OnlyMine => !IsPrivileged || Mine;

    public async Task OnGetAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

        var query = _db.Expeditions
            .Include(e => e.Anomaly)
            .Include(e => e.Leader)
            .Include(e => e.Members)
            .AsQueryable();

        if (OnlyMine)
        {
            query = query.Where(e => e.LeaderId == userId || e.Members.Any(m => m.AgentId == userId));
        }

        Expeditions = await query.OrderByDescending(e => e.StartDate).ToListAsync();

        var ids = Expeditions.Select(e => e.Id).ToList();
        var unreadRows = await _db.CommunicationLogs
            .Where(c => ids.Contains(c.ExpeditionId) && !c.IsRead && c.SenderId != userId)
            .GroupBy(c => c.ExpeditionId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync();
        Unread = unreadRows.ToDictionary(x => x.Key, x => x.Count);
    }
}
