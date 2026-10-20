using Aegis.Data;
using Aegis.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aegis.Controllers.Ajax;

/// <summary>AJAX для модераторов: быстрое одобрение полевых отчётов.</summary>
[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Authorize(Roles = AppRoles.ModeratorOrAdmin)]
[Route("ajax/reports")]
public class ReportsAjaxController : ControllerBase
{
    private readonly AppDbContext _db;

    public ReportsAjaxController(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>Отметить отчёт как принятый («Принято»). Меняет данные в БД.</summary>
    [HttpPut("{id:int}/approve")]
    public async Task<IActionResult> Approve(int id)
    {
        var report = await _db.FieldReports.FirstOrDefaultAsync(r => r.Id == id);
        if (report is null)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Отчёт не найден");
        }

        if (!report.IsApproved)
        {
            report.IsApproved = true;
            await _db.SaveChangesAsync();
        }

        return Ok(new { id = report.Id, isApproved = report.IsApproved });
    }
}
