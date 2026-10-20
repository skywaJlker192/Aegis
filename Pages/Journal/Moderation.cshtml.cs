using Aegis.Data;
using Aegis.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Aegis.Pages.Journal;

/// <summary>Модерация журналов агентов (координатор / админ). Решение отправляется AJAX-запросом, см. journal.js.</summary>
[Authorize(Roles = AppRoles.ModeratorOrAdmin)]
public class ModerationModel : PageModel
{
    private readonly AppDbContext _db;

    public ModerationModel(AppDbContext db)
    {
        _db = db;
    }

    public List<AgentJournal> Pending { get; private set; } = new();
    public List<AgentJournal> Recent { get; private set; } = new();

    public async Task OnGetAsync()
    {
        var all = _db.AgentJournals.Include(j => j.Agent).Include(j => j.Expedition);

        Pending = await all.Where(j => !j.IsApproved && !j.IsRejected)
            .OrderBy(j => j.CreatedAt).ToListAsync();

        Recent = await all.Where(j => j.IsApproved || j.IsRejected)
            .OrderByDescending(j => j.CreatedAt).Take(20).ToListAsync();
    }
}
