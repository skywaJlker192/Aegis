namespace Aegis.Models;

/// <summary>Экспедиция к аномалии.</summary>
public class Expedition
{
    public int Id { get; set; }

    public int TargetAnomalyId { get; set; }
    public Anomaly Anomaly { get; set; } = null!;

    /// <summary>Руководитель экспедиции (Id агента).</summary>
    public string LeaderId { get; set; } = string.Empty;
    public Agent Leader { get; set; } = null!;

    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public ExpeditionStatus Status { get; set; } = ExpeditionStatus.Planned;

    public List<ExpeditionMember> Members { get; set; } = new();
    public List<FieldReport> Reports { get; set; } = new();
    public List<CommunicationLog> Messages { get; set; } = new();
    public List<AgentJournal> JournalEntries { get; set; } = new();
}
