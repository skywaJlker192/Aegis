using System.ComponentModel.DataAnnotations;

namespace Aegis.Models;

/// <summary>Запись телеметрии (замер датчика) по аномалии. Её же отдаёт API v2 в поле TelemetryLogs.</summary>
public class AnomalyLog
{
    public int Id { get; set; }

    public int AnomalyId { get; set; }
    public Anomaly Anomaly { get; set; } = null!;

    /// <summary>Источник замера, например SENSOR-T1.</summary>
    [Required, MaxLength(50)]
    public string Source { get; set; } = string.Empty;

    [Required, MaxLength(300)]
    public string Message { get; set; } = string.Empty;

    /// <summary>Числовое значение замера.</summary>
    public double Reading { get; set; }

    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
}
