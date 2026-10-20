using System.Security.Claims;
using Aegis.Data;
using Aegis.Dtos;
using Aegis.Hubs;
using Aegis.Models;
using Aegis.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Aegis.Controllers.Ajax;

/// <summary>
/// Эндпоинты для fetch-запросов со страницы экспедиции. Работают по cookie-авторизации.
/// CSRF: глобальный AutoValidateAntiforgeryToken требует заголовок X-CSRF-TOKEN — без него ответ 400.
/// Не авторизован -> 401 (а не редирект), нет прав -> 403.
/// </summary>
[ApiController]
[ApiExplorerSettings(IgnoreApi = true)] // служебные эндпоинты интерфейса, в Swagger не показываем
[Authorize]
[Route("ajax/expeditions")]
public class ExpeditionsAjaxController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IAuthorizationService _authorization;
    private readonly IHubContext<SituationRoomHub> _hub;

    public ExpeditionsAjaxController(AppDbContext db, IAuthorizationService authorization, IHubContext<SituationRoomHub> hub)
    {
        _db = db;
        _authorization = authorization;
        _hub = hub;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    /// <summary>
    /// SOS / запрос эвакуации: меняет статус экспедиции на Aborted.
    /// Доступно только руководителю — проверяется resource-based политикой CanAbortExpedition.
    /// </summary>
    [HttpPost("{id:int}/abort")]
    public async Task<IActionResult> Abort(int id)
    {
        var expedition = await _db.Expeditions.FirstOrDefaultAsync(e => e.Id == id);
        if (expedition is null)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Экспедиция не найдена");
        }

        // Проверяем не роль, а право на конкретный ресурс
        var authResult = await _authorization.AuthorizeAsync(User, expedition, "CanAbortExpedition");
        if (!authResult.Succeeded)
        {
            return Forbid();
        }

        if (expedition.Status is ExpeditionStatus.Completed or ExpeditionStatus.Aborted)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Экспедиция уже закрыта", detail: "Нельзя прервать завершённую или уже прерванную экспедицию.");
        }

        expedition.Status = ExpeditionStatus.Aborted;
        expedition.EndDate = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        // Сообщаем участникам и модераторам в реальном времени
        var callsign = await GetCallsignAsync(CurrentUserId);
        await _hub.Clients.Groups(new[] { HubGroups.Expedition(id), HubGroups.Moderators })
            .SendAsync("AgentTelemetryUpdate", new
            {
                expeditionId = id,
                callsign,
                message = "SOS! Запрос эвакуации. Экспедиция прервана.",
                timestamp = DateTime.UtcNow
            });

        return Ok(new
        {
            id = expedition.Id,
            status = expedition.Status.ToString(),
            statusLabel = UiText.ExpeditionStatusLabel(expedition.Status)
        });
    }

    /// <summary>Добавить агента в экипаж. Может руководитель экспедиции, модератор или админ.</summary>
    [HttpPost("{id:int}/members")]
    public async Task<IActionResult> AddMember(int id, [FromBody] AddMemberRequest request)
    {
        var expedition = await _db.Expeditions.FirstOrDefaultAsync(e => e.Id == id);
        if (expedition is null)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Экспедиция не найдена");
        }

        var canManage = User.IsInRole(AppRoles.Admin) ||
                        User.IsInRole(AppRoles.Moderator) ||
                        expedition.LeaderId == CurrentUserId;
        if (!canManage)
        {
            return Forbid();
        }

        if (expedition.Status is ExpeditionStatus.Completed or ExpeditionStatus.Aborted)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Экспедиция закрыта", detail: "Состав закрытой экспедиции менять нельзя.");
        }

        var agent = await _db.Users.FirstOrDefaultAsync(u => u.Id == request.AgentId);
        if (agent is null)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Агент не найден");
        }

        var alreadyInCrew = await _db.ExpeditionMembers
            .AnyAsync(m => m.ExpeditionId == id && m.AgentId == agent.Id);
        if (alreadyInCrew)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Дубликат", detail: $"Агент {agent.Callsign} уже в составе экспедиции.");
        }

        var member = new ExpeditionMember
        {
            ExpeditionId = id,
            AgentId = agent.Id,
            RoleInMission = request.RoleInMission.Trim(),
            IsAlive = true
        };
        _db.ExpeditionMembers.Add(member);
        await _db.SaveChangesAsync();

        await _hub.Clients.Groups(new[] { HubGroups.Expedition(id), HubGroups.Moderators })
            .SendAsync("AgentTelemetryUpdate", new
            {
                expeditionId = id,
                callsign = agent.Callsign,
                message = $"Включён в состав экипажа. Роль: {member.RoleInMission}.",
                timestamp = DateTime.UtcNow
            });

        return Ok(new
        {
            agentId = agent.Id,
            callsign = agent.Callsign,
            role = member.RoleInMission,
            isAlive = member.IsAlive
        });
    }

    private async Task<string> GetCallsignAsync(string userId)
    {
        var callsign = await _db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.Callsign)
            .FirstOrDefaultAsync();

        return callsign ?? "НЕИЗВЕСТНЫЙ";
    }
}
