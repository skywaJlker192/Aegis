using Aegis.Data;
using Aegis.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Aegis.Pages.Anomalies;

/// <summary>Досье аномалии: полная информация, связанные экспедиции, последние замеры.</summary>
[Authorize]
public class DetailsModel : PageModel
{
    private readonly AppDbContext _db;

    public DetailsModel(AppDbContext db)
    {
        _db = db;
    }

    public Anomaly Anomaly { get; private set; } = null!;
    public List<Expedition> Expeditions { get; private set; } = new();
    public List<AnomalyLog> Logs { get; private set; } = new();

    /// <summary>Модератор и админ могут менять уровень угрозы и создавать экспедиции.</summary>
    public bool CanModerate => User.IsInRole(AppRoles.Moderator) || User.IsInRole(AppRoles.Admin);

    public bool IsAgent => User.IsInRole(AppRoles.Agent);

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var anomaly = await _db.Anomalies.FirstOrDefaultAsync(a => a.Id == id);
        if (anomaly is null)
        {
            return NotFound();
        }

        Anomaly = anomaly;

        Expeditions = await _db.Expeditions
            .Include(e => e.Leader)
            .Include(e => e.Members)
            .Where(e => e.TargetAnomalyId == id)
            .OrderByDescending(e => e.StartDate)
            .ToListAsync();

        Logs = await _db.AnomalyLogs
            .Where(l => l.AnomalyId == id)
            .OrderByDescending(l => l.RecordedAt)
            .Take(10)
            .ToListAsync();

        return Page();
    }
}
