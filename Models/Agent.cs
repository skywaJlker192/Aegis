using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace Aegis.Models;

/// <summary>
/// Агент организации. Это и есть пользователь системы (в ТЗ он же «AppUser»):
/// наследник IdentityUser, поэтому логин/пароль/роли берутся из ASP.NET Core Identity.
/// </summary>
public class Agent : IdentityUser
{
    /// <summary>Позывной.</summary>
    [MaxLength(50)]
    public string Callsign { get; set; } = string.Empty;

    /// <summary>Уровень допуска 1–5.</summary>
    [Range(1, 5)]
    public int ClearanceLevel { get; set; } = 1;

    public AgentStatus Status { get; set; } = AgentStatus.Active;

    // Навигационные свойства
    public List<ExpeditionMember> ExpeditionMembers { get; set; } = new();
    public List<FieldReport> FieldReports { get; set; } = new();
    public List<AgentJournal> JournalEntries { get; set; } = new();
}
