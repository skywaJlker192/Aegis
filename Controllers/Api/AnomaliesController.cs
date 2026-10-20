using System.Security.Claims;
using Aegis.Dtos;
using Aegis.Models;
using Aegis.Services;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;

namespace Aegis.Controllers.Api;

/// <summary>
/// REST API аномалий. Маршруты: /api/v1/anomalies и /api/v2/anomalies.
/// Защищён JWT: без токена — 401. Агент видит только «свои» аномалии (по его экспедициям),
/// модератор и админ — все. UserId всегда берётся из claims токена, а не из тела запроса.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[ApiVersion("2.0")]
[Route("api/v{version:apiVersion}/anomalies")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[IgnoreAntiforgeryToken] // схема JWT — CSRF неприменим
[Produces("application/json")]
public class AnomaliesController : ControllerBase
{
    private readonly IAnomalyService _service;

    public AnomaliesController(IAnomalyService service)
    {
        _service = service;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    /// <summary>Админ и модератор видят все аномалии, агент — только свои.</summary>
    private bool SeeAll => User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Moderator);

    /// <summary>Список аномалий (v1).</summary>
    /// <response code="200">Список доступных пользователю аномалий.</response>
    /// <response code="401">Нет токена или он недействителен.</response>
    [HttpGet]
    [MapToApiVersion("1.0")]
    [ProducesResponseType(typeof(IEnumerable<AnomalyDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IEnumerable<AnomalyDto>>> GetAll()
    {
        var items = await _service.GetVisibleAsync(CurrentUserId, SeeAll);
        return Ok(items.Select(a => a.ToDto()));
    }

    /// <summary>Список аномалий (v2): дополнительно возвращает TelemetryLogs — последние замеры.</summary>
    /// <response code="200">Список доступных пользователю аномалий с телеметрией.</response>
    /// <response code="401">Нет токена или он недействителен.</response>
    [HttpGet]
    [MapToApiVersion("2.0")]
    [ProducesResponseType(typeof(IEnumerable<AnomalyV2Dto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IEnumerable<AnomalyV2Dto>>> GetAllV2()
    {
        var items = await _service.GetVisibleAsync(CurrentUserId, SeeAll, includeLogs: true);
        return Ok(items.Select(a => a.ToV2Dto()));
    }

    /// <summary>Аномалия по Id (v1).</summary>
    /// <param name="id">Идентификатор аномалии.</param>
    /// <response code="200">Аномалия найдена.</response>
    /// <response code="404">Аномалии нет или она недоступна этому пользователю.</response>
    [HttpGet("{id:int}")]
    [MapToApiVersion("1.0")]
    [ProducesResponseType(typeof(AnomalyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AnomalyDto>> GetById(int id)
    {
        var anomaly = await _service.GetVisibleByIdAsync(id, CurrentUserId, SeeAll);
        if (anomaly is null)
        {
            return NotFound();
        }

        return Ok(anomaly.ToDto());
    }

    /// <summary>Аномалия по Id (v2) — вместе с TelemetryLogs.</summary>
    /// <param name="id">Идентификатор аномалии.</param>
    /// <response code="200">Аномалия найдена.</response>
    /// <response code="404">Аномалии нет или она недоступна этому пользователю.</response>
    [HttpGet("{id:int}")]
    [MapToApiVersion("2.0")]
    [ProducesResponseType(typeof(AnomalyV2Dto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AnomalyV2Dto>> GetByIdV2(int id)
    {
        var anomaly = await _service.GetVisibleByIdAsync(id, CurrentUserId, SeeAll, includeLogs: true);
        if (anomaly is null)
        {
            return NotFound();
        }

        return Ok(anomaly.ToV2Dto());
    }

    /// <summary>Создать аномалию (модератор или админ). Всем подключённым клиентам уходит уведомление «Новая аномалия обнаружена».</summary>
    /// <response code="201">Аномалия создана, адрес — в заголовке Location.</response>
    /// <response code="400">Ошибка валидации.</response>
    /// <response code="403">Недостаточно прав.</response>
    [HttpPost]
    [Authorize(Roles = AppRoles.ModeratorOrAdmin)]
    [ProducesResponseType(typeof(AnomalyDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AnomalyDto>> Create([FromBody] CreateAnomalyDto dto)
    {
        var created = await _service.CreateAsync(dto);

        // Location строим от текущего пути: /api/v1/anomalies -> /api/v1/anomalies/{id}
        var location = $"{Request.Path.Value!.TrimEnd('/')}/{created.Id}";
        return Created(location, created.ToDto());
    }

    /// <summary>Обновить статус и уровень угрозы аномалии (модератор или админ).</summary>
    /// <param name="id">Идентификатор аномалии.</param>
    /// <param name="dto">Новые значения Status и ThreatLevel.</param>
    /// <response code="200">Аномалия обновлена.</response>
    /// <response code="400">Недопустимое значение или запрещённый переход (ProblemDetails).</response>
    /// <response code="404">Аномалия не найдена.</response>
    [HttpPut("{id:int}")]
    [Authorize(Roles = AppRoles.ModeratorOrAdmin)]
    [ProducesResponseType(typeof(AnomalyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AnomalyDto>> Update(int id, [FromBody] UpdateAnomalyDto dto)
    {
        var result = await _service.UpdateAsync(id, dto);

        if (result.Status == OperationStatus.NotFound)
        {
            return NotFound();
        }

        if (result.Status == OperationStatus.Invalid)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Недопустимая операция", detail: result.Error);
        }

        return Ok(result.Value!.ToDto());
    }

    /// <summary>Удалить аномалию вместе с её замерами и экспедициями (только админ).</summary>
    /// <param name="id">Идентификатор аномалии.</param>
    /// <response code="204">Аномалия удалена.</response>
    /// <response code="404">Аномалия не найдена.</response>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = AppRoles.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Delete(int id)
    {
        var deleted = await _service.DeleteAsync(id);
        if (!deleted)
        {
            return NotFound();
        }

        return NoContent();
    }
}
