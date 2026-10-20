using Aegis.Authorization;
using Aegis.Data;
using Aegis.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Aegis.Pages.Reports;

/// <summary>Очередь отчётов для модератора: непринятые сверху. Кнопка «Принято» работает через AJAX (PUT).</summary>
[Authorize(Roles = AppRoles.ModeratorOrAdmin)]
[RequireClearance(4)]
public class IndexModel : PageModel
{
    private readonly AppDbContext _db;

    public IndexModel(AppDbContext db)
    {
        _db = db;
    }

    public List<FieldReport> Reports { get; private set; } = new();

    public async Task OnGetAsync()
    {
        Reports = await _db.FieldReports
            .Include(r => r.Author)
            .Include(r => r.Expedition)
            .OrderBy(r => r.IsApproved)               // сначала не принятые
            .ThenByDescending(r => r.CreatedAt)
            .ToListAsync();
    }
}
