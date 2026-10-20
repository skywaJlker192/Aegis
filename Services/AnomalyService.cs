using Aegis.Data;
using Aegis.Dtos;
using Aegis.Helpers;
using Aegis.Hubs;
using Aegis.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Aegis.Services;

public enum OperationStatus
{
    Ok,
    NotFound,
    Invalid
}

/// <summary>Результат операции сервиса: успех, «не найдено» или «недопустимо» с текстом причины.</summary>
public class OperationResult<T>
{
    public OperationStatus Status { get; private init; }
    public T? Value { get; private init; }
    public string? Error { get; private init; }

    public static OperationResult<T> Success(T value) => new() { Status = OperationStatus.Ok, Value = value };
    public static OperationResult<T> NotFound() => new() { Status = OperationStatus.NotFound };
    public static OperationResult<T> Invalid(string error) => new() { Status = OperationStatus.Invalid, Error = error };
}

public interface IAnomalyService
{
    /// <summary>Аномалии, доступные пользователю: админ/модератор видят все, агент — только свои (по его экспедициям).</summary>
    Task<List<Anomaly>> GetVisibleAsync(string userId, bool seeAll, bool includeLogs = false);

    Task<Anomaly?> GetVisibleByIdAsync(int id, string userId, bool seeAll, bool includeLogs = false);

    /// <summary>Создаёт аномалию и рассылает всем событие NewAnomaly.</summary>
    Task<Anomaly> CreateAsync(CreateAnomalyDto dto);

    Task<OperationResult<Anomaly>> UpdateAsync(int id, UpdateAnomalyDto dto);

    /// <summary>Меняет уровень угрозы и рассылает всем BroadcastThreatLevelChange.</summary>
    Task<OperationResult<Anomaly>> ChangeThreatLevelAsync(int id, ThreatLevel level);

    Task<bool> DeleteAsync(int id);
}

/// <summary>
/// Бизнес-логика по аномалиям. Использует IHubContext, поэтому может сама
/// отправлять SignalR-уведомления из любого места (API, AJAX, хаб), не будучи хабом.
/// </summary>
public class AnomalyService : IAnomalyService
{
    private readonly AppDbContext _db;
    private readonly IHubContext<SituationRoomHub> _hub;
    private readonly ILogger<AnomalyService> _logger;

    public AnomalyService(AppDbContext db, IHubContext<SituationRoomHub> hub, ILogger<AnomalyService> logger)
    {
        _db = db;
        _hub = hub;
        _logger = logger;
    }

    public async Task<List<Anomaly>> GetVisibleAsync(string userId, bool seeAll, bool includeLogs = false)
    {
        var query = BuildVisibleQuery(userId, seeAll, includeLogs);

        return await query
            .OrderByDescending(a => a.ThreatLevel)
            .ThenByDescending(a => a.DetectedAt)
            .ToListAsync();
    }

    public async Task<Anomaly?> GetVisibleByIdAsync(int id, string userId, bool seeAll, bool includeLogs = false)
    {
        return await BuildVisibleQuery(userId, seeAll, includeLogs).FirstOrDefaultAsync(a => a.Id == id);
    }

    public async Task<Anomaly> CreateAsync(CreateAnomalyDto dto)
    {
        var anomaly = new Anomaly
        {
            Title = dto.Title.Trim(),
            Description = dto.Description?.Trim() ?? string.Empty,
            Latitude = dto.Coordinates.Latitude,
            Longitude = dto.Coordinates.Longitude,
            ThreatLevel = dto.ThreatLevel,
            Status = AnomalyStatus.Detected,
            DetectedAt = DateTime.UtcNow
        };

        _db.Anomalies.Add(anomaly);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Создана аномалия {Id}: {Title}", anomaly.Id, anomaly.Title);

        // Сервис сам пушит уведомление всем подключённым клиентам
        await _hub.Clients.All.SendAsync("NewAnomaly", new
        {
            id = anomaly.Id,
            title = anomaly.Title,
            level = anomaly.ThreatLevel.ToString(),
            levelLabel = UiText.ThreatLabel(anomaly.ThreatLevel),
            message = "Новая аномалия обнаружена"
        });

        return anomaly;
    }

    public async Task<OperationResult<Anomaly>> UpdateAsync(int id, UpdateAnomalyDto dto)
    {
        var anomaly = await _db.Anomalies.FirstOrDefaultAsync(a => a.Id == id);
        if (anomaly is null)
        {
            return OperationResult<Anomaly>.NotFound();
        }

        // Бизнес-правило: нейтрализованную аномалию нельзя «вернуть» в статус «Обнаружена»
        if (anomaly.Status == AnomalyStatus.Neutralized && dto.Status == AnomalyStatus.Detected)
        {
            return OperationResult<Anomaly>.Invalid(
                "Нейтрализованную аномалию нельзя вернуть в статус «Обнаружена».");
        }

        var threatChanged = anomaly.ThreatLevel != dto.ThreatLevel;

        anomaly.Status = dto.Status;
        anomaly.ThreatLevel = dto.ThreatLevel;
        await _db.SaveChangesAsync();

        if (threatChanged)
        {
            await BroadcastThreatAsync(anomaly);
        }

        return OperationResult<Anomaly>.Success(anomaly);
    }

    public async Task<OperationResult<Anomaly>> ChangeThreatLevelAsync(int id, ThreatLevel level)
    {
        var anomaly = await _db.Anomalies.FirstOrDefaultAsync(a => a.Id == id);
        if (anomaly is null)
        {
            return OperationResult<Anomaly>.NotFound();
        }

        if (anomaly.ThreatLevel != level)
        {
            anomaly.ThreatLevel = level;
            await _db.SaveChangesAsync();
            await BroadcastThreatAsync(anomaly);
        }

        return OperationResult<Anomaly>.Success(anomaly);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var anomaly = await _db.Anomalies.FirstOrDefaultAsync(a => a.Id == id);
        if (anomaly is null)
        {
            return false;
        }

        _db.Anomalies.Remove(anomaly);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Удалена аномалия {Id}", id);
        return true;
    }

    // ------------------------------------------------------------------

    private IQueryable<Anomaly> BuildVisibleQuery(string userId, bool seeAll, bool includeLogs)
    {
        IQueryable<Anomaly> query = _db.Anomalies;

        if (includeLogs)
        {
            query = query.Include(a => a.Logs);
        }

        if (!seeAll)
        {
            // «Свои» аномалии агента — те, по которым есть экспедиции с его участием
            query = query.Where(a => a.Expeditions.Any(e =>
                e.LeaderId == userId || e.Members.Any(m => m.AgentId == userId)));
        }

        return query;
    }

    /// <summary>Рассылает ВСЕМ клиентам: уровень угрозы изменился (дашборды перекрашивают индикаторы).</summary>
    private Task BroadcastThreatAsync(Anomaly anomaly)
    {
        return _hub.Clients.All.SendAsync("BroadcastThreatLevelChange", new
        {
            anomalyId = anomaly.Id,
            title = anomaly.Title,
            level = anomaly.ThreatLevel.ToString(),
            levelLabel = UiText.ThreatLabel(anomaly.ThreatLevel)
        });
    }
}
