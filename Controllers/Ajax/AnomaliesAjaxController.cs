using Aegis.Dtos;
using Aegis.Helpers;
using Aegis.Models;
using Aegis.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aegis.Controllers.Ajax;

/// <summary>AJAX для модераторов: смена уровня угрозы. Сервис рассылает всем событие BroadcastThreatLevelChange.</summary>
[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Authorize(Roles = AppRoles.ModeratorOrAdmin)]
[Route("ajax/anomalies")]
public class AnomaliesAjaxController : ControllerBase
{
    private readonly IAnomalyService _service;

    public AnomaliesAjaxController(IAnomalyService service)
    {
        _service = service;
    }

    [HttpPut("{id:int}/threat")]
    public async Task<IActionResult> ChangeThreat(int id, [FromBody] ChangeThreatRequest request)
    {
        var result = await _service.ChangeThreatLevelAsync(id, request.ThreatLevel);
        if (result.Status == OperationStatus.NotFound)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Аномалия не найдена");
        }

        var anomaly = result.Value!;
        return Ok(new
        {
            id = anomaly.Id,
            level = anomaly.ThreatLevel.ToString(),
            levelLabel = UiText.ThreatLabel(anomaly.ThreatLevel)
        });
    }
}
