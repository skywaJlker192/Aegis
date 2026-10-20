using System.ComponentModel.DataAnnotations;

namespace Aegis.Models;

/// <summary>
/// Связующая таблица M:N «агент — экспедиция».
/// Первичный ключ составной: (ExpeditionId, AgentId) — настраивается в AppDbContext через Fluent API.
/// </summary>
public class ExpeditionMember
{
    public int ExpeditionId { get; set; }
    public Expedition Expedition { get; set; } = null!;

    public string AgentId { get; set; } = string.Empty;
    public Agent Agent { get; set; } = null!;

    /// <summary>Роль в миссии: Связной, Подрывник, Медик...</summary>
    [Required, MaxLength(50)]
    public string RoleInMission { get; set; } = string.Empty;

    public bool IsAlive { get; set; } = true;
}
