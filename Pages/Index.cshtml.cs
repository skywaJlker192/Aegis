using Aegis.Data;
using Aegis.Models;
using Aegis.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Aegis.Pages;

/// <summary>
/// Модель дашборда штаба (@model DashboardViewModel).
/// Вся статистика считается на сервере и сразу попадает в HTML (серверный рендеринг);
/// потом SignalR только обновляет цвета индикаторов и счётчик «на связи».
/// </summary>
[Authorize]
public class DashboardViewModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly PresenceTracker _presence;

    public DashboardViewModel(AppDbContext db, PresenceTracker presence)
    {
        _db = db;
        _presence = presence;
    }

    public int ActiveAnomalies { get; private set; }
    public int CriticalAnomalies { get; private set; }
    public int AgentsInField { get; private set; }
    public int OnlineNow { get; private set; }
    public int PendingReports { get; private set; }

    /// <summary>Карту и трекер агентов показываем только штабу (модератор / админ).</summary>
    public bool ShowMap => User.IsInRole(AppRoles.Moderator) || User.IsInRole(AppRoles.Admin);

    public List<Expedition> RecentExpeditions { get; private set; } = new();
    public List<Anomaly> TopAnomalies { get; private set; } = new();

    public async Task OnGetAsync()
    {
        ActiveAnomalies = await _db.Anomalies.CountAsync(a =>
            a.Status == AnomalyStatus.Detected || a.Status == AnomalyStatus.Investigating);

        CriticalAnomalies = await _db.Anomalies.CountAsync(a =>
            a.ThreatLevel == ThreatLevel.Critical && a.Status != AnomalyStatus.Neutralized);

        AgentsInField = await _db.Users.CountAsync(u => u.Status == AgentStatus.InField);
        PendingReports = await _db.FieldReports.CountAsync(r => !r.IsApproved);
        OnlineNow = _presence.OnlineCount;

        RecentExpeditions = await _db.Expeditions
            .Include(e => e.Anomaly)
            .Include(e => e.Leader)
            .Include(e => e.Members)
            .OrderByDescending(e => e.StartDate)
            .Take(5)
            .ToListAsync();

        TopAnomalies = await _db.Anomalies
            .OrderByDescending(a => a.ThreatLevel)
            .ThenByDescending(a => a.DetectedAt)
            .Take(6)
            .ToListAsync();
    }
}
