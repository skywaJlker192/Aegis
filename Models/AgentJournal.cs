using System.ComponentModel.DataAnnotations;

namespace Aegis.Models;

/// <summary>Запись в личном журнале полевого агента. Проходит модерацию координатором.</summary>
public class AgentJournal
{
    public int Id { get; set; }

    public string AgentId { get; set; } = string.Empty;
    public Agent Agent { get; set; } = null!;

    /// <summary>Экспедиция, к которой относится запись (необязательно).</summary>
    public int? ExpeditionId { get; set; }
    public Expedition? Expedition { get; set; }

    public JournalEntryType EntryType { get; set; } = JournalEntryType.Observation;

    [Required, MaxLength(1000)]
    public string Text { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Одобрена координатором — такая запись попадает в общий журнал экспедиции.</summary>
    public bool IsApproved { get; set; }

    /// <summary>Отклонена координатором. Если обе отметки false — запись «На модерации».</summary>
    public bool IsRejected { get; set; }

    /// <summary>Id координатора, одобрившего запись.</summary>
    public string? ApprovedBy { get; set; }
    public Agent? Approver { get; set; }
}
