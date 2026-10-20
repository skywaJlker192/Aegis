using System.ComponentModel.DataAnnotations;
using Aegis.Models;

namespace Aegis.Dtos;

/// <summary>Географические координаты аномалии.</summary>
public class CoordinatesDto
{
    /// <summary>Широта, от −90 до 90.</summary>
    [Range(-90, 90)]
    public decimal Latitude { get; set; }

    /// <summary>Долгота, от −180 до 180.</summary>
    [Range(-180, 180)]
    public decimal Longitude { get; set; }
}

/// <summary>Аномалия в ответе API v1.</summary>
public class AnomalyDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public CoordinatesDto Coordinates { get; set; } = new();
    public ThreatLevel ThreatLevel { get; set; }
    public AnomalyStatus Status { get; set; }
}

/// <summary>Один замер телеметрии.</summary>
public class TelemetryLogDto
{
    public string Source { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public double Reading { get; set; }
    public DateTime RecordedAt { get; set; }
}

/// <summary>Аномалия в ответе API v2: к полям v1 добавлен массив последних замеров.</summary>
public class AnomalyV2Dto : AnomalyDto
{
    /// <summary>Последние замеры телеметрии (до 5 штук, новые первыми).</summary>
    public List<TelemetryLogDto> TelemetryLogs { get; set; } = new();
}

/// <summary>Тело запроса на создание аномалии.</summary>
public class CreateAnomalyDto
{
    [Required, StringLength(100, MinimumLength = 3)]
    public string Title { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }

    [Required]
    public CoordinatesDto Coordinates { get; set; } = new();

    [EnumDataType(typeof(ThreatLevel))]
    public ThreatLevel ThreatLevel { get; set; }
}

/// <summary>Тело запроса на обновление аномалии: можно менять только статус и уровень угрозы.</summary>
public class UpdateAnomalyDto
{
    [EnumDataType(typeof(AnomalyStatus))]
    public AnomalyStatus Status { get; set; }

    [EnumDataType(typeof(ThreatLevel))]
    public ThreatLevel ThreatLevel { get; set; }
}
