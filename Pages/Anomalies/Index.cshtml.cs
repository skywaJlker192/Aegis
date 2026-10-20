using Aegis.Data;
using Aegis.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Aegis.Pages.Anomalies;

/// <summary>Карта аномалий: список с фильтром по уровню угрозы (серверный рендеринг).</summary>
[Authorize]
public class IndexModel : PageModel
{
    private readonly AppDbContext _db;

    public IndexModel(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>Фильтр из строки запроса: /Anomalies?Level=Critical. Пусто — показать все.</summary>
    [BindProperty(SupportsGet = true)]
    public ThreatLevel? Level { get; set; }

    public List<Anomaly> Anomalies { get; private set; } = new();

    public async Task OnGetAsync()
    {
        var query = _db.Anomalies.AsQueryable();

        if (Level.HasValue)
        {
            query = query.Where(a => a.ThreatLevel == Level.Value);
        }

        Anomalies = await query
            .OrderByDescending(a => a.ThreatLevel)
            .ThenByDescending(a => a.DetectedAt)
            .ToListAsync();
    }
}
