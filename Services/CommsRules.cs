namespace Aegis.Services;

/// <summary>Чистые правила чата «Протокол связи»: проверка и нормализация текста сообщения.</summary>
public static class CommsRules
{
    public const int MaxMessageLength = 500;

    /// <summary>Типы сообщений в чате. Определяют цвет: agent — зелёный, hq — янтарный, system — серый.</summary>
    public const string KindAgent = "agent";
    public const string KindHq = "hq";
    public const string KindSystem = "system";

    public static string KindFor(bool isFromHq) => isFromHq ? KindHq : KindAgent;

    /// <summary>
    /// Проверяет сообщение: не пустое, не длиннее 500 символов. Переводы строк и табуляции заменяются пробелами,
    /// лишние пробелы по краям убираются. Возвращает false и текст ошибки, если отправлять нельзя.
    /// </summary>
    public static bool TryNormalize(string? raw, out string text, out string error)
    {
        text = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(raw))
        {
            error = "Пустое сообщение.";
            return false;
        }

        var cleaned = new string(raw.Select(ch => char.IsControl(ch) ? ' ' : ch).ToArray()).Trim();

        if (cleaned.Length == 0)
        {
            error = "Пустое сообщение.";
            return false;
        }

        if (cleaned.Length > MaxMessageLength)
        {
            error = $"Сообщение длиннее {MaxMessageLength} символов.";
            return false;
        }

        text = cleaned;
        return true;
    }
}
