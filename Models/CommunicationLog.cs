using System.ComponentModel.DataAnnotations;

namespace Aegis.Models;

/// <summary>Сообщение в чате «Протокол связи» между штабом и полевыми агентами.</summary>
public class CommunicationLog
{
    public int Id { get; set; }

    public int ExpeditionId { get; set; }
    public Expedition Expedition { get; set; } = null!;

    public string SenderId { get; set; } = string.Empty;
    public Agent Sender { get; set; } = null!;

    [Required, MaxLength(500)]
    public string Message { get; set; } = string.Empty;

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>Прочитано ли сообщение кем-то, кроме автора.</summary>
    public bool IsRead { get; set; }

    /// <summary>Сообщение отправил штаб (модератор/админ). Нужно для окраски в чате: штаб — янтарный, агент — зелёный.</summary>
    public bool IsFromHq { get; set; }
}
