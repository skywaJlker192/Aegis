using Aegis.Models;
using Aegis.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aegis.Controllers.Ajax;

/// <summary>Данные для карты операций. Доступны только штабу (модератор / админ).</summary>
[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Authorize(Roles = AppRoles.ModeratorOrAdmin)]
[Route("ajax/map")]
public class MapAjaxController : ControllerBase
{
    private readonly IAgentTracker _tracker;

    public MapAjaxController(IAgentTracker tracker)
    {
        _tracker = tracker;
    }

    /// <summary>Первая загрузка схемы: аномалии и текущие положения агентов. Дальше схему обновляет SignalR.</summary>
    [HttpGet("state")]
    public async Task<ActionResult<MapState>> State(CancellationToken ct)
    {
        return Ok(await _tracker.GetStateAsync(ct));
    }
}
