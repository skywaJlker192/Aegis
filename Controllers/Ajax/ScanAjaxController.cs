using System.Security.Claims;
using Aegis.Data;
using Aegis.Dtos;
using Aegis.Helpers;
using Aegis.Hubs;
using Aegis.Models;
using Aegis.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Aegis.Controllers.Ajax;

/// <summary>
/// Мини-игра «Протокол сканирования аномалии».
///   POST /ajax/scan/start — сервер выдаёт зону и токен;
///   POST /ajax/scan/lock  — клиент присылает частоту, сервер проверяет попадание,
///                           при успехе сохраняет тип аномалии и уведомляет штаб через SignalR.
/// </summary>
[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Authorize]
[Route("ajax/scan")]
public class ScanAjaxController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IScanService _scan;
    private readonly IHubContext<SituationRoomHub> _hub;

    public ScanAjaxController(AppDbContext db, IScanService scan, IHubContext<SituationRoomHub> hub)
    {
        _db = db;
        _scan = scan;
        _hub = hub;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
    private bool IsPrivileged => User.IsInRole(AppRoles.Moderator) || User.IsInRole(AppRoles.Admin);

    /// <summary>Сканировать можно аномалию, к которой у агента есть экспедиция (модератор/админ — любую).</summary>
    private async Task<bool> CanScanAsync(int anomalyId)
    {
        if (IsPrivileged) return true;

        var userId = CurrentUserId;
        return await _db.Expeditions.AnyAsync(e =>
            e.TargetAnomalyId == anomalyId &&
            (e.LeaderId == userId || e.Members.Any(m => m.AgentId == userId)));
    }

    [HttpPost("start")]
    public async Task<IActionResult> Start([FromBody] ScanStartRequest request)
    {
        var anomaly = await _db.Anomalies.FirstOrDefaultAsync(a => a.Id == request.AnomalyId);
        if (anomaly is null)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Аномалия не найдена");
        }

        if (!await CanScanAsync(anomaly.Id))
        {
            return Forbid();
        }

        var challenge = _scan.Start(CurrentUserId, anomaly.Id, anomaly.Kind);

        return Ok(new
        {
            anomalyId = anomaly.Id,
            code = UiText.AnomalyCode(anomaly.Id),
            title = anomaly.Title,
            token = challenge.Token,
            zoneStart = challenge.ZoneStart,
            zoneEnd = challenge.ZoneEnd,
            sweepSeconds = challenge.SweepSeconds
        });
    }

    [HttpPost("lock")]
    public async Task<IActionResult> Lock([FromBody] ScanLockRequest request)
    {
        var anomaly = await _db.Anomalies.FirstOrDefaultAsync(a => a.Id == request.AnomalyId);
        if (anomaly is null)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Аномалия не найдена");
        }

        if (!await CanScanAsync(anomaly.Id))
        {
            return Forbid();
        }

        var verdict = _scan.Verify(request.Token, CurrentUserId, anomaly.Id, request.Frequency);

        if (verdict.Outcome == ScanOutcome.InvalidToken)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Сеанс сканирования устарел", detail: "Запустите сканирование заново.");
        }

        if (verdict.Outcome == ScanOutcome.Miss)
        {
            // Промах — это не ошибка сервера, а нормальный результат игры: 200 и success = false
            return Ok(new
            {
                success = false,
                message = $"ОШИБКА СКАНИРОВАНИЯ: частота {request.Frequency:F2} кГц вне зоны захвата. Повторите."
            });
        }

        // Успех: сохраняем тип аномалии в базу
        anomaly.Kind = verdict.Kind;
        anomaly.ScannedAt = DateTime.UtcNow;

        var callsign = await _db.Users
            .Where(u => u.Id == CurrentUserId)
            .Select(u => u.Callsign)
            .FirstOrDefaultAsync() ?? "НЕИЗВЕСТНЫЙ";

        // След в телеметрии аномалии
        _db.AnomalyLogs.Add(new AnomalyLog
        {
            AnomalyId = anomaly.Id,
            Source = "SCAN",
            Message = $"Тип определён: {UiText.KindLabel(anomaly.Kind)} (агент {callsign})",
            Reading = Math.Round(request.Frequency, 2),
            RecordedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        // SignalR: штаб (группа HQ) получает уведомление
        var code = UiText.AnomalyCode(anomaly.Id);
        await _hub.Clients.Group(HubGroups.Hq).SendAsync("ScanCompleted", new
        {
            anomalyId = anomaly.Id,
            code,
            callsign,
            kind = anomaly.Kind.ToString(),
            kindLabel = UiText.KindLabel(anomaly.Kind),
            message = $"Агент {callsign} завершил сканирование аномалии {code}",
            timestamp = DateTime.UtcNow
        });

        return Ok(new
        {
            success = true,
            kind = anomaly.Kind.ToString(),
            kindLabel = UiText.KindLabel(anomaly.Kind),
            message = $"ЧАСТОТА ЗАФИКСИРОВАНА. ТИП АНОМАЛИИ: {UiText.KindLabel(anomaly.Kind)}."
        });
    }
}
