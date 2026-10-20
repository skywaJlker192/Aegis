using Aegis.Data;
using Aegis.Helpers;
using Aegis.Models;
using Aegis.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Aegis.Hubs;

/// <summary>Сообщение чата для клиента. Kind: agent / hq / system.</summary>
public record CommsMessageDto(int Id, int ExpeditionId, string Sender, string Kind, string Message, DateTime Timestamp, bool IsRead);

/// <summary>
/// Отдельный хаб чата «Протокол связи» (штаб ⇄ полевые агенты), адрес /hubs/comms.
/// Клиенты объединяются в группы по ExpeditionId. Событие ReceiveMessage получают все участники
/// экспедиции и координаторы (модераторы / админы), которые находятся в группе.
/// Сообщения сохраняются в таблицу CommunicationLog.
/// </summary>
[Authorize]
public class ExpeditionCommsHub : Hub
{
    private const string JoinedKey = "comms-joined";       // в какие группы добавлено подключение
    private const string AnnouncedKey = "comms-announced"; // о каких экспедициях объявлено «вышел на связь»
    private const int HistorySize = 50;

    private readonly AppDbContext _db;
    private readonly IHubContext<SituationRoomHub> _situationRoom;
    private readonly ILogger<ExpeditionCommsHub> _logger;

    public ExpeditionCommsHub(AppDbContext db, IHubContext<SituationRoomHub> situationRoom, ILogger<ExpeditionCommsHub> logger)
    {
        _db = db;
        _situationRoom = situationRoom;
        _logger = logger;
    }

    private string UserId => Context.UserIdentifier ?? string.Empty;
    private bool IsPrivileged => Context.User?.IsInRole(AppRoles.Moderator) == true || Context.User?.IsInRole(AppRoles.Admin) == true;

    // ------------------------------------------------------------------
    // Жизненный цикл подключения
    // ------------------------------------------------------------------

    public override async Task OnConnectedAsync()
    {
        // Координаторы слушают все экспедиции, агенты — только свои
        var ids = IsPrivileged
            ? await _db.Expeditions.Select(e => e.Id).ToListAsync()
            : await GetUserExpeditionIdsAsync();

        foreach (var id in ids)
        {
            await AddToGroupAsync(id);
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var callsign = await GetCallsignAsync(UserId);

        // Тем, кому мы сообщали «вышел на связь», сообщаем «сигнал потерян»
        foreach (var id in Announced.ToList())
        {
            await Clients.Group(HubGroups.Comms(id)).SendAsync("ReceiveMessage",
                SystemMessage(id, $"{callsign}: СИГНАЛ ПОТЕРЯН"));
        }

        Joined.Clear();
        Announced.Clear();
        await base.OnDisconnectedAsync(exception);
    }

    // ------------------------------------------------------------------
    // Методы, которые вызывает клиент (connection.invoke)
    // ------------------------------------------------------------------

    /// <summary>
    /// Открыть чат экспедиции: вступить в группу, получить историю (последние 50 сообщений) и пометить чужие сообщения прочитанными.
    /// Возвращённая история хранит исходное значение IsRead, чтобы клиент мог подсветить новые сообщения.
    /// </summary>
    public async Task<List<CommsMessageDto>> JoinExpedition(int expeditionId)
    {
        await EnsureAccessAsync(expeditionId);
        await AddToGroupAsync(expeditionId);

        var history = await _db.CommunicationLogs
            .Include(c => c.Sender)
            .Where(c => c.ExpeditionId == expeditionId)
            .OrderByDescending(c => c.Timestamp)
            .Take(HistorySize)
            .ToListAsync();

        history.Reverse(); // от старых к новым

        var result = history.Select(ToDto).ToList();

        await MarkReadInternalAsync(expeditionId);

        // Один раз за подключение сообщаем остальным, что агент на связи
        if (Announced.Add(expeditionId))
        {
            var callsign = await GetCallsignAsync(UserId);
            await Clients.OthersInGroup(HubGroups.Comms(expeditionId)).SendAsync("ReceiveMessage",
                SystemMessage(expeditionId, $"{callsign}: НА СВЯЗИ"));
        }

        return result;
    }

    public async Task LeaveExpedition(int expeditionId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, HubGroups.Comms(expeditionId));
        Joined.Remove(expeditionId);
        Announced.Remove(expeditionId);
    }

    /// <summary>Отправить сообщение: сохранить в CommunicationLog и разослать группе экспедиции (ReceiveMessage).</summary>
    public async Task SendMessage(int expeditionId, string message)
    {
        if (!CommsRules.TryNormalize(message, out var text, out var error))
        {
            throw new HubException(error);
        }

        await EnsureAccessAsync(expeditionId);
        await AddToGroupAsync(expeditionId); // на случай, если экспедиция создана после подключения

        var sender = await _db.Users.FirstOrDefaultAsync(u => u.Id == UserId);
        if (sender is null)
        {
            throw new HubException("Агент не найден.");
        }

        var isFromHq = IsPrivileged;
        var log = new CommunicationLog
        {
            ExpeditionId = expeditionId,
            SenderId = sender.Id,
            Message = text,
            Timestamp = DateTime.UtcNow,
            IsRead = false,
            IsFromHq = isFromHq
        };
        _db.CommunicationLogs.Add(log);
        await _db.SaveChangesAsync();

        var dto = new CommsMessageDto(log.Id, expeditionId, sender.Callsign, CommsRules.KindFor(isFromHq),
            log.Message, log.Timestamp, log.IsRead);

        // Участники экспедиции и координаторы, находящиеся в группе
        await Clients.Group(HubGroups.Comms(expeditionId)).SendAsync("ReceiveMessage", dto);

        // Координатору, который сейчас на другой странице, приходит короткое уведомление через основной хаб (группа HQ)
        if (!isFromHq)
        {
            await _situationRoom.Clients.Group(HubGroups.Hq).SendAsync("CommsMessage", new
            {
                expeditionId,
                code = UiText.ExpeditionCode(expeditionId),
                callsign = sender.Callsign,
                message = text
            });
        }
    }

    /// <summary>Клиент увидел чужие сообщения — отметить их прочитанными.</summary>
    public async Task MarkRead(int expeditionId)
    {
        await EnsureAccessAsync(expeditionId);
        await MarkReadInternalAsync(expeditionId);
    }

    // ------------------------------------------------------------------

    private HashSet<int> Joined => GetSet(JoinedKey);
    private HashSet<int> Announced => GetSet(AnnouncedKey);

    private HashSet<int> GetSet(string key)
    {
        if (Context.Items.TryGetValue(key, out var existing) && existing is HashSet<int> set)
        {
            return set;
        }

        var created = new HashSet<int>();
        Context.Items[key] = created;
        return created;
    }

    private async Task AddToGroupAsync(int expeditionId)
    {
        if (Joined.Add(expeditionId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, HubGroups.Comms(expeditionId));
        }
    }

    private async Task EnsureAccessAsync(int expeditionId)
    {
        var allowed = IsPrivileged
            ? await _db.Expeditions.AnyAsync(e => e.Id == expeditionId)
            : await _db.Expeditions.AnyAsync(e =>
                e.Id == expeditionId && (e.LeaderId == UserId || e.Members.Any(m => m.AgentId == UserId)));

        if (!allowed)
        {
            throw new HubException("Нет допуска к каналу связи этой экспедиции.");
        }
    }

    private async Task MarkReadInternalAsync(int expeditionId)
    {
        var unread = await _db.CommunicationLogs
            .Where(c => c.ExpeditionId == expeditionId && c.SenderId != UserId && !c.IsRead)
            .ToListAsync();

        if (unread.Count == 0) return;

        foreach (var item in unread) item.IsRead = true;
        await _db.SaveChangesAsync();
    }

    private async Task<List<int>> GetUserExpeditionIdsAsync()
    {
        var asMember = await _db.ExpeditionMembers.Where(m => m.AgentId == UserId).Select(m => m.ExpeditionId).ToListAsync();
        var asLeader = await _db.Expeditions.Where(e => e.LeaderId == UserId).Select(e => e.Id).ToListAsync();
        return asMember.Union(asLeader).ToList();
    }

    private async Task<string> GetCallsignAsync(string userId)
    {
        var callsign = await _db.Users.Where(u => u.Id == userId).Select(u => u.Callsign).FirstOrDefaultAsync();
        return callsign ?? "НЕИЗВЕСТНЫЙ";
    }

    private static CommsMessageDto ToDto(CommunicationLog c) =>
        new(c.Id, c.ExpeditionId, c.Sender.Callsign, CommsRules.KindFor(c.IsFromHq), c.Message, c.Timestamp, c.IsRead);

    /// <summary>Системное уведомление (серый текст). В базу не пишется.</summary>
    private static CommsMessageDto SystemMessage(int expeditionId, string text) =>
        new(0, expeditionId, "СИСТЕМА", CommsRules.KindSystem, text, DateTime.UtcNow, true);
}
