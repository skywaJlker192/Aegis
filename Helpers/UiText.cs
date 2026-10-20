using Aegis.Models;

namespace Aegis.Helpers;

/// <summary>Русские подписи и CSS-классы для перечислений (используются в Razor-страницах и SignalR-уведомлениях).</summary>
public static class UiText
{
    public static string ThreatLabel(ThreatLevel level) => level switch
    {
        ThreatLevel.Low => "НИЗКИЙ",
        ThreatLevel.Medium => "СРЕДНИЙ",
        ThreatLevel.High => "ВЫСОКИЙ",
        ThreatLevel.Critical => "КРИТИЧЕСКИЙ",
        _ => level.ToString().ToUpperInvariant()
    };

    /// <summary>CSS-класс цвета: threat-low / threat-medium / threat-high / threat-critical.</summary>
    public static string ThreatClass(ThreatLevel level) => "threat-" + level.ToString().ToLowerInvariant();

    public static string AnomalyStatusLabel(AnomalyStatus status) => status switch
    {
        AnomalyStatus.Detected => "ОБНАРУЖЕНА",
        AnomalyStatus.Investigating => "ИЗУЧАЕТСЯ",
        AnomalyStatus.Neutralized => "НЕЙТРАЛИЗОВАНА",
        AnomalyStatus.Contained => "ЛОКАЛИЗОВАНА",
        _ => status.ToString().ToUpperInvariant()
    };

    public static string ExpeditionStatusLabel(ExpeditionStatus status) => status switch
    {
        ExpeditionStatus.Planned => "ЗАПЛАНИРОВАНА",
        ExpeditionStatus.Active => "АКТИВНА",
        ExpeditionStatus.Completed => "ЗАВЕРШЕНА",
        ExpeditionStatus.Aborted => "ПРЕРВАНА",
        _ => status.ToString().ToUpperInvariant()
    };

    /// <summary>CSS-класс: status-planned / status-active / status-completed / status-aborted.</summary>
    public static string ExpeditionStatusClass(ExpeditionStatus status) => "status-" + status.ToString().ToLowerInvariant();

    public static string AgentStatusLabel(AgentStatus status) => status switch
    {
        AgentStatus.Active => "НА БАЗЕ",
        AgentStatus.InField => "В ПОЛЕ",
        AgentStatus.Missing => "ПРОПАЛ",
        _ => status.ToString().ToUpperInvariant()
    };

    /// <summary>Тип аномалии по результатам сканирования.</summary>
    public static string KindLabel(AnomalyKind? kind) => kind switch
    {
        AnomalyKind.Thermal => "ТЕПЛОВАЯ",
        AnomalyKind.Electromagnetic => "ЭЛЕКТРОМАГНИТНАЯ",
        AnomalyKind.Biological => "БИОЛОГИЧЕСКАЯ",
        AnomalyKind.Gravitational => "ГРАВИТАЦИОННАЯ",
        _ => "НЕ ОПРЕДЕЛЁН"
    };

    public static string JournalTypeLabel(JournalEntryType type) => type switch
    {
        JournalEntryType.Observation => "НАБЛЮДЕНИЕ",
        JournalEntryType.Alert => "ТРЕВОГА",
        JournalEntryType.Report => "ОТЧЁТ",
        _ => type.ToString().ToUpperInvariant()
    };

    /// <summary>CSS-класс записи журнала: journal-observation / journal-alert / journal-report.</summary>
    public static string JournalTypeClass(JournalEntryType type) => "journal-" + type.ToString().ToLowerInvariant();

    /// <summary>Цвет карты допуска: 1-2 зелёный, 3 жёлтый, 4-5 красный.</summary>
    public static string ClearanceClass(int level) =>
        level <= 2 ? "clearance-low" : level == 3 ? "clearance-mid" : "clearance-high";

    public static string ExpeditionCode(int id) => $"ЭКСП-{id:000}";
    public static string AnomalyCode(int id) => $"АНМ-{id:000}";
}
