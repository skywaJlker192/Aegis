using System.ComponentModel.DataAnnotations;

namespace Aegis.Models;

/// <summary>Аномалия, за которой наблюдает штаб.</summary>
public class Anomaly
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(500)]
    public string Description { get; set; } = string.Empty;

    /// <summary>Широта. В БД: decimal(10,7).</summary>
    public decimal Latitude { get; set; }

    /// <summary>Долгота. В БД: decimal(10,7).</summary>
    public decimal Longitude { get; set; }

    public ThreatLevel ThreatLevel { get; set; } = ThreatLevel.Low;
    public AnomalyStatus Status { get; set; } = AnomalyStatus.Detected;
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Тип аномалии, определённый сканированием. null — аномалию ещё не сканировали.</summary>
    public AnomalyKind? Kind { get; set; }

    /// <summary>Когда было завершено последнее успешное сканирование.</summary>
    public DateTime? ScannedAt { get; set; }

    public List<Expedition> Expeditions { get; set; } = new();
    public List<AnomalyLog> Logs { get; set; } = new();
}
