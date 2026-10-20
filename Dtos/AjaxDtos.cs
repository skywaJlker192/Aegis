using System.ComponentModel.DataAnnotations;
using Aegis.Models;

namespace Aegis.Dtos;

/// <summary>Добавление агента в экипаж (AJAX).</summary>
public class AddMemberRequest
{
    [Required]
    public string AgentId { get; set; } = string.Empty;

    [Required, StringLength(50)]
    public string RoleInMission { get; set; } = string.Empty;
}

/// <summary>Начало сканирования аномалии (AJAX).</summary>
public class ScanStartRequest
{
    [Range(1, int.MaxValue)]
    public int AnomalyId { get; set; }
}

/// <summary>Фиксация частоты: токен из /ajax/scan/start и частота, на которой стоял ползунок.</summary>
public class ScanLockRequest
{
    [Range(1, int.MaxValue)]
    public int AnomalyId { get; set; }

    [Required]
    public string Token { get; set; } = string.Empty;

    [Range(0, 99.99)]
    public double Frequency { get; set; }
}

/// <summary>Смена уровня угрозы (AJAX, для модераторов).</summary>
public class ChangeThreatRequest
{
    [EnumDataType(typeof(ThreatLevel))]
    public ThreatLevel ThreatLevel { get; set; }
}
