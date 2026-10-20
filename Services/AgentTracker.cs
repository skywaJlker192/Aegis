using System.Collections.Concurrent;
using Aegis.Data;
using Aegis.Helpers;
using Aegis.Hubs;
using Aegis.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Aegis.Services;

public record MapPoint(double X, double Y);

/// <summary>Агент на схеме. Signal: ok — на связи (зелёный), warn — предупреждение (жёлтый), crit — критический (красный).</summary>
public record MapAgent(
    string AgentId,
    string Callsign,
    MapPoint Coordinates,
    string Status,
    string StatusLabel,
    string Signal,
    DateTime LastActivity,
    int? ExpeditionId,
    string? ExpeditionCode,
    string? Target);

public record MapAnomaly(int Id, string Code, string Title, string Level, string LevelLabel, MapPoint Position);

public record MapState(List<MapAnomaly> Anomalies, List<MapAgent> Agents, DateTime ServerTime);

public interface IAgentTracker
{
    /// <summary>Текущее состояние схемы: аномалии и агенты (для первой загрузки страницы).</summary>
    Task<MapState> GetStateAsync(CancellationToken ct = default);

    /// <summary>Один шаг имитации: сдвигает агентов и рассылает AgentLocationUpdate группе HQ.</summary>
    Task TickAsync(CancellationToken ct = default);
}

/// <summary>
/// «Живой трекер агентов». Положения агентов на схеме держим в памяти (это имитация телеметрии, хранить историю не нужно),
/// а кто из агентов отслеживается, в какой он экспедиции и какой у неё цель — каждый раз берём из БД.
/// </summary>
public class AgentTracker : IAgentTracker
{
    private class Tracked
    {
        public double X, Y;
        public string Signal = "ok";
        public DateTime LastActivity;
    }

    private readonly ConcurrentDictionary<string, Tracked> _store = new();
    private readonly SemaphoreSlim _gate = new(1, 1); // чтобы Tick и GetState не меняли положения одновременно
    private readonly IServiceScopeFactory _scopes;
    private readonly IHubContext<SituationRoomHub> _hub;
    private readonly ILogger<AgentTracker> _logger;
    private readonly Random _rng = new();

    public AgentTracker(IServiceScopeFactory scopes, IHubContext<SituationRoomHub> hub, ILogger<AgentTracker> logger)
    {
        _scopes = scopes;
        _hub = hub;
        _logger = logger;
    }

    public async Task<MapState> GetStateAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var (state, _) = await RefreshAsync(move: false, ct);
            return state;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task TickAsync(CancellationToken ct = default)
    {
        List<MapAgent> agents;

        await _gate.WaitAsync(ct);
        try
        {
            (_, agents) = await RefreshAsync(move: true, ct);
        }
        finally
        {
            _gate.Release();
        }

        // Событие уходит только в группу HQ: штаб — это модераторы и админы
        foreach (var agent in agents)
        {
            await _hub.Clients.Group(HubGroups.Hq).SendAsync("AgentLocationUpdate", agent, ct);
        }

        _logger.LogDebug("Трекер: отправлено {Count} обновлений положения.", agents.Count);
    }

    // ------------------------------------------------------------------

    private async Task<(MapState State, List<MapAgent> Agents)> RefreshAsync(bool move, CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var anomalies = await db.Anomalies.OrderBy(a => a.Id).ToListAsync(ct);

        // Отслеживаем тех, кто в поле/пропал, и тех, кто состоит в экспедициях
        var people = await db.Users
            .Where(u => u.Status != AgentStatus.Active || u.ExpeditionMembers.Any())
            .OrderBy(u => u.Callsign)
            .ToListAsync(ct);

        var memberships = await db.ExpeditionMembers
            .Include(m => m.Expedition).ThenInclude(e => e.Anomaly)
            .Where(m => m.Expedition.Status == ExpeditionStatus.Active || m.Expedition.Status == ExpeditionStatus.Planned)
            .ToListAsync(ct);

        // Для каждого агента берём «главную» экспедицию: активная важнее запланированной, затем более свежая
        var currentExpedition = memberships
            .GroupBy(m => m.AgentId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(m => m.Expedition.Status == ExpeditionStatus.Active ? 0 : 1)
                      .ThenByDescending(m => m.Expedition.StartDate)
                      .First().Expedition);

        var now = DateTime.UtcNow;
        var result = new List<MapAgent>();

        foreach (var person in people)
        {
            currentExpedition.TryGetValue(person.Id, out var expedition);

            var target = expedition is not null
                ? MapLayout.AnomalySlot(expedition.TargetAnomalyId)
                : MapLayout.Base;

            var tracked = _store.GetOrAdd(person.Id, _ =>
            {
                var start = MapLayout.InitialPosition(person.Id, target);
                return new Tracked
                {
                    X = start.X,
                    Y = start.Y,
                    // пропавший агент «последний раз выходил на связь» несколько часов назад
                    LastActivity = person.Status == AgentStatus.Missing ? now.AddHours(-6) : now
                };
            });

            if (move)
            {
                Advance(tracked, person, expedition, target, now);
            }

            result.Add(new MapAgent(
                person.Id,
                person.Callsign,
                new MapPoint(Math.Round(tracked.X, 1), Math.Round(tracked.Y, 1)),
                person.Status.ToString(),
                UiText.AgentStatusLabel(person.Status),
                person.Status == AgentStatus.Missing ? "crit" : tracked.Signal,
                tracked.LastActivity,
                expedition?.Id,
                expedition is null ? null : UiText.ExpeditionCode(expedition.Id),
                expedition?.Anomaly.Title));
        }

        // Тех, кто перестал отслеживаться (например, экспедиция закрыта и статус «на базе»), убираем из памяти
        var keep = people.Select(p => p.Id).ToHashSet();
        foreach (var key in _store.Keys.Where(k => !keep.Contains(k)))
        {
            _store.TryRemove(key, out _);
        }

        var anomalyDtos = anomalies.Select(a =>
        {
            var slot = MapLayout.AnomalySlot(a.Id);
            return new MapAnomaly(a.Id, UiText.AnomalyCode(a.Id), a.Title, a.ThreatLevel.ToString(),
                UiText.ThreatLabel(a.ThreatLevel), new MapPoint(slot.X, slot.Y));
        }).ToList();

        return (new MapState(anomalyDtos, result, now), result);
    }

    /// <summary>Двигает агента к цели и выбирает цвет индикатора связи.</summary>
    private void Advance(Tracked tracked, Agent person, Expedition? expedition, (double X, double Y) target, DateTime now)
    {
        if (person.Status == AgentStatus.Missing)
        {
            // Пропавший агент почти не двигается и не выходит на связь
            var drift = MapLayout.Step((tracked.X, tracked.Y), (tracked.X, tracked.Y), _rng, pull: 0, jitter: 3);
            tracked.X = drift.X;
            tracked.Y = drift.Y;
            tracked.Signal = "crit";
            return;
        }

        var next = MapLayout.Step((tracked.X, tracked.Y), target, _rng, pull: 0.6, jitter: 30);
        tracked.X = next.X;
        tracked.Y = next.Y;
        tracked.LastActivity = now;

        // Чем опаснее цель, тем чаще связь «шумит»
        var threat = expedition?.Anomaly.ThreatLevel;
        var roll = _rng.NextDouble();
        tracked.Signal = threat switch
        {
            ThreatLevel.Critical => roll < 0.08 ? "crit" : roll < 0.50 ? "warn" : "ok",
            ThreatLevel.High => roll < 0.28 ? "warn" : "ok",
            _ => roll < 0.06 ? "warn" : "ok"
        };
    }
}
