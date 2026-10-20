using Aegis.Models;

namespace Aegis.Services;

/// <summary>
/// Чистая логика мини-игры «Протокол сканирования» (без БД и без HTTP) — её удобно тестировать отдельно.
/// Шкала частот: 0.00 – 99.99 кГц. «Зелёная зона» — небольшой случайный диапазон на этой шкале.
/// </summary>
public static class ScanGame
{
    public const double MinFrequency = 0.00;
    public const double MaxFrequency = 99.99;

    /// <summary>Сколько секунд ползунок идёт от одного края шкалы до другого (CSS-анимация на клиенте).</summary>
    public const double SweepSeconds = 5.0;

    /// <summary>Ширина зоны в кГц: от 3.00 до 4.50 (чтобы в зону реально можно было попасть).</summary>
    public const double MinZoneWidth = 3.0;
    public const double MaxZoneWidth = 4.5;

    /// <summary>Случайная зона захвата, например 45.20 – 49.10.</summary>
    public static (double Start, double End) NextZone(Random rng)
    {
        var width = Math.Round(MinZoneWidth + rng.NextDouble() * (MaxZoneWidth - MinZoneWidth), 2);

        // Зона не прижимается к краям шкалы: отступ минимум 5 кГц с каждой стороны
        var minStart = 5.0;
        var maxStart = MaxFrequency - 5.0 - width;
        var start = Math.Round(minStart + rng.NextDouble() * (maxStart - minStart), 2);

        return (start, Math.Round(start + width, 2));
    }

    /// <summary>Случайный тип аномалии: тепловая, электромагнитная, биологическая или гравитационная.</summary>
    public static AnomalyKind NextKind(Random rng) => (AnomalyKind)rng.Next(0, 4);

    /// <summary>Частота должна быть числом в пределах шкалы.</summary>
    public static bool IsValidFrequency(double frequency) =>
        !double.IsNaN(frequency) && !double.IsInfinity(frequency) &&
        frequency >= MinFrequency && frequency <= MaxFrequency;

    /// <summary>Попал ли игрок в зелёную зону (границы включительно).</summary>
    public static bool IsHit(double frequency, double zoneStart, double zoneEnd) =>
        IsValidFrequency(frequency) && frequency >= zoneStart && frequency <= zoneEnd;
}
