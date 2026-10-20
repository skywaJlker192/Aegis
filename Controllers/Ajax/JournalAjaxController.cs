using System.Security.Claims;
using Aegis.Data;
using Aegis.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aegis.Controllers.Ajax;

/// <summary>AJAX модерации журналов агентов (только координатор и админ).</summary>
[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Authorize(Roles = AppRoles.ModeratorOrAdmin)]
[Route("ajax/journal")]
public class JournalAjaxController : ControllerBase
{
    private readonly AppDbContext _db;

    public JournalAjaxController(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>Одобрить запись: она попадает в общий журнал экспедиции.</summary>
    [HttpPut("{id:int}/approve")]
    public Task<IActionResult> Approve(int id) => Decide(id, approve: true);

    /// <summary>Отклонить запись: остаётся только в личном журнале агента.</summary>
    [HttpPut("{id:int}/reject")]
    public Task<IActionResult> Reject(int id) => Decide(id, approve: false);

    private async Task<IActionResult> Decide(int id, bool approve)
    {
        var entry = await _db.AgentJournals.FirstOrDefaultAsync(j => j.Id == id);
        if (entry is null)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Запись не найдена");
        }

        entry.IsApproved = approve;
        entry.IsRejected = !approve;
        entry.ApprovedBy = approve ? User.FindFirstValue(ClaimTypes.NameIdentifier) : null;
        await _db.SaveChangesAsync();

        return Ok(new { id = entry.Id, isApproved = entry.IsApproved, isRejected = entry.IsRejected });
    }
}
