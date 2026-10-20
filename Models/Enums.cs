namespace Aegis.Models;

/// <summary>Состояние агента.</summary>
public enum AgentStatus
{
    Active,   // на базе, готов к заданию
    InField,  // в поле
    Missing   // пропал без вести
}

/// <summary>Уровень угрозы аномалии.</summary>
public enum ThreatLevel
{
    Low,
    Medium,
    High,
    Critical
}

/// <summary>Статус работы с аномалией.</summary>
public enum AnomalyStatus
{
    Detected,       // обнаружена
    Investigating,  // изучается
    Neutralized,    // нейтрализована
    Contained       // локализована
}

/// <summary>Тип аномалии. Определяется мини-игрой «Протокол сканирования».</summary>
public enum AnomalyKind
{
    Thermal,         // тепловая
    Electromagnetic, // электромагнитная
    Biological,      // биологическая
    Gravitational    // гравитационная
}

/// <summary>Тип записи в журнале полевого агента.</summary>
public enum JournalEntryType
{
    Observation, // наблюдение
    Alert,       // тревога
    Report       // отчёт
}

/// <summary>Статус экспедиции.</summary>
public enum ExpeditionStatus
{
    Planned,
    Active,
    Completed,
    Aborted
}
