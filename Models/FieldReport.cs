using System.ComponentModel.DataAnnotations;

namespace Aegis.Models;

/// <summary>Полевой отчёт агента по экспедиции.</summary>
public class FieldReport
{
    public int Id { get; set; }

    public int ExpeditionId { get; set; }
    public Expedition Expedition { get; set; } = null!;

    public string AuthorId { get; set; } = string.Empty;
    public Agent Author { get; set; } = null!;

    [Required, MaxLength(2000)]
    public string Text { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Принят ли отчёт модератором.</summary>
    public bool IsApproved { get; set; }
}
