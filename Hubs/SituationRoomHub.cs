using Aegis.Data;
using Aegis.Helpers;
using Aegis.Models;
using Aegis.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Aegis.Hubs;

/// <summary>
/// Хаб ситуационного центра. Доступен только авторизованным пользователям.
///
/// События, которые получают клиенты:
///   BroadcastThreatLevelChange — сменился уровень угрозы аномалии (всем);
///   AgentTelemetryUpdate       — телеметрия агента (участникам экспедиции + модераторам);
///   NewAnomaly                 — обнаружена новая аномалия (всем; шлёт AnomalyService);
///   PresenceChanged            — кто-то вышел на связь / потерял сигнал, обновлён счётчик онлайна.
/// </summary>
[Authorize]
public class SituationRoomHub : Hub
{
    private const string GroupsKey = "joined-groups";

    private readonly AppDbContext _db;
    private readonly PresenceTracker _presence;
    private readonly IAnomalyService _anomalies;
    private readonly ILogger<SituationRoomHub> _logger;

    public SituationRoomHub(AppDbContext db, PresenceTracker presence, IAnomalyService anomalies, ILogger<SituationRoomHub> logger)
    {
        _db = db;
        _presence = presence;
        _anomalies = anomalies;
        _logger = logger;
    }

    // ------------------------------------------------------------------
    // Жизненный цикл подключения
    // ------------------------------------------------------------------

    public override async Task OnConnectedAsync()
    {
        var userId = Context.UserIdentifier ?? string.Empty;
        var callsign = await GetCallsignAsync(userId);

        _presence.Connect(Context.ConnectionId, userId);

        // Модераторы и админы получают телеметрию по ВСЕМ экспедициям через общую группу
        if (IsPrivileged())
        {
            await JoinGroupAsync(HubGroups.Moderators);
            await JoinGroupAsync(HubGroups.Hq); // штаб получает AgentLocationUpdate и ScanCompleted
        }
        else
        {
            // Остальные — только по своим экспедициям (где они руководитель или участник)
            var expeditionIds = await GetUserExpeditionIdsAsync(userId);
            foreach (var id in expeditionIds)
            {
                await JoinGroupAsync(HubGroups.Expedition(id));
            }
        }

        _logger.LogInformation("Агент {Callsign} ({UserId}) вышел на связь. Подключение {ConnectionId}. Онлайн: {Online}",
            callsign, userId, Context.ConnectionId, _presence.OnlineCount);

        await Clients.All.SendAsync("PresenceChanged", new
        {
            online = _presence.OnlineCount,
            callsign,
            connected = true,
            state = "На связи"
        });

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = Context.UserIdentifier ?? string.Empty;
        var callsign = await GetCallsignAsync(userId);

        // Явно удаляем подключение из всех групп, в которые оно вступало
        foreach (var group in JoinedGroups.ToList())
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, group);
        }
        JoinedGroups.Clear();

        _presence.Disconnect(Context.ConnectionId);

        _logger.LogInformation("Агент {Callsign} потерял сигнал. Подключение {ConnectionId}. Онлайн: {Online}. Причина: {Reason}",
            callsign, Context.ConnectionId, _presence.OnlineCount, exception?.Message ?? "штатное отключение");

        // Если у агента открыта другая вкладка — он всё ещё на связи
        var stillOnline = _presence.IsOnline(userId);
        await Clients.All.SendAsync("PresenceChanged", new
        {
            online = _presence.OnlineCount,
            callsign,
            connected = stillOnline,
            state = stillOnline ? "На связи" : "Сигнал потерян"
        });

        await base.OnDisconnectedAsync(exception);
    }

    // ------------------------------------------------------------------
    // Методы, которые клиент вызывает через connection.invoke(...)
    // ------------------------------------------------------------------

    /// <summary>Вступить в группу экспедиции (клиент вызывает это после переподключения).</summary>
    public async Task JoinExpeditionGroup(int expeditionId)
    {
        // Модераторы уже в общей группе, им отдельная не нужна
        if (IsPrivileged()) return;

        if (!await CanAccessExpeditionAsync(expeditionId))
        {
            throw new HubException("Нет допуска к этой экспедиции.");
        }

        await JoinGroupAsync(HubGroups.Expedition(expeditionId));
    }

    public async Task LeaveExpeditionGroup(int expeditionId)
    {
        var group = HubGroups.Expedition(expeditionId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, group);
        JoinedGroups.Remove(group);
    }

    /// <summary>
    /// Агент передаёт сообщение телеметрии. Событие AgentTelemetryUpdate получат
    /// только участники этой экспедиции и модераторы.
    /// </summary>
    public async Task SendTelemetry(int expeditionId, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new HubException("Пустое сообщение.");
        }

        if (!await CanAccessExpeditionAsync(expeditionId))
        {
            throw new HubException("Нет допуска к этой экспедиции.");
        }

        var userId = Context.UserIdentifier ?? string.Empty;
        var callsign = await GetCallsignAsync(userId);
        var text = message.Trim();
        if (text.Length > 200) text = text[..200];

        await Clients.Groups(new[] { HubGroups.Expedition(expeditionId), HubGroups.Moderators })
            .SendAsync("AgentTelemetryUpdate", new
            {
                expeditionId,
                callsign,
                message = text,
                timestamp = DateTime.UtcNow
            });
    }

    /// <summary>
    /// Смена уровня угрозы из хаба (только модератор/админ).
    /// Сервис сохраняет изменение и сам рассылает BroadcastThreatLevelChange через Clients.All.
    /// </summary>
    [Authorize(Roles = AppRoles.ModeratorOrAdmin)]
    public async Task BroadcastThreatLevelChange(int anomalyId, string level)
    {
        if (!Enum.TryParse<ThreatLevel>(level, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
        {
            throw new HubException("Неизвестный уровень угрозы.");
        }

        var result = await _anomalies.ChangeThreatLevelAsync(anomalyId, parsed);
        if (result.Status == OperationStatus.NotFound)
        {
            throw new HubException("Аномалия не найдена.");
        }
    }

    // ------------------------------------------------------------------
    // Вспомогательные методы
    // ------------------------------------------------------------------

    private bool IsPrivileged() =>
        Context.User?.IsInRole(AppRoles.Moderator) == true || Context.User?.IsInRole(AppRoles.Admin) == true;

    private HashSet<string> JoinedGroups
    {
        get
        {
            if (Context.Items.TryGetValue(GroupsKey, out var existing) && existing is HashSet<string> set)
            {
                return set;
            }

            var created = new HashSet<string>();
            Context.Items[GroupsKey] = created;
            return created;
        }
    }

    private async Task JoinGroupAsync(string group)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, group);
        JoinedGroups.Add(group);
    }

    private async Task<string> GetCallsignAsync(string userId)
    {
        var callsign = await _db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.Callsign)
            .FirstOrDefaultAsync();

        return callsign ?? "НЕИЗВЕСТНЫЙ";
    }

    private async Task<List<int>> GetUserExpeditionIdsAsync(string userId)
    {
        var asMember = await _db.ExpeditionMembers
            .Where(m => m.AgentId == userId)
            .Select(m => m.ExpeditionId)
            .ToListAsync();

        var asLeader = await _db.Expeditions
            .Where(e => e.LeaderId == userId)
            .Select(e => e.Id)
            .ToListAsync();

        return asMember.Union(asLeader).ToList();
    }

    private async Task<bool> CanAccessExpeditionAsync(int expeditionId)
    {
        if (IsPrivileged())
        {
            return await _db.Expeditions.AnyAsync(e => e.Id == expeditionId);
        }

        var userId = Context.UserIdentifier ?? string.Empty;
        return await _db.Expeditions.AnyAsync(e =>
            e.Id == expeditionId &&
            (e.LeaderId == userId || e.Members.Any(m => m.AgentId == userId)));
    }
}
