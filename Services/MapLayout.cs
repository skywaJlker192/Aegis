namespace Aegis.Services;

/// <summary>
/// Чистая геометрия схемы «Карта операций» (SVG 800×500). Координаты X, Y — в единицах схемы, а не в градусах.
/// Эти же точки нарисованы как сектора в Pages/Shared/_OpsMap.cshtml — если меняете здесь, меняйте и там.
/// </summary>
public static class MapLayout
{
    public const double Width = 800;
    public const double Height = 500;

    /// <summary>Отступ от края схемы, за который агенты не заходят.</summary>
    public const double Margin = 24;

    /// <summary>Штаб / база.</summary>
    public static readonly (double X, double Y) Base = (400, 430);

    /// <summary>Места аномалий на схеме. Аномалия с Id n занимает слот (n-1) mod 8.</summary>
    private static readonly (double X, double Y)[] Slots =
    {
        (150, 110), // 1: ледник Д-7
        (630, 100), // 2: шахта «Вега»
        (690, 290), // 3: блок Б
        (150, 330), // 4: склад №3
        (420, 170), // 5: станция «Каспий-2»
        (300, 260), // 6
        (550, 230), // 7
        (80, 230)   // 8
    };

    public static (double X, double Y) AnomalySlot(int anomalyId)
    {
        var index = ((anomalyId - 1) % Slots.Length + Slots.Length) % Slots.Length;
        return Slots[index];
    }

    public static (double X, double Y) Clamp(double x, double y) =>
        (Math.Clamp(x, Margin, Width - Margin), Math.Clamp(y, Margin, Height - Margin));

    /// <summary>Стабильное число из строки (string.GetHashCode в .NET меняется при каждом запуске, а нам нужна повторяемость).</summary>
    public static int StableSeed(string text)
    {
        unchecked
        {
            var hash = 17;
            foreach (var ch in text) hash = hash * 31 + ch;
            return hash;
        }
    }

    /// <summary>Начальная позиция агента: рядом с целью (аномалией или базой), с постоянным для агента смещением.</summary>
    public static (double X, double Y) InitialPosition(string agentId, (double X, double Y) target)
    {
        var rng = new Random(StableSeed(agentId));
        var angle = rng.NextDouble() * Math.PI * 2;
        var radius = 25 + rng.NextDouble() * 35;
        return Clamp(target.X + Math.Cos(angle) * radius, target.Y + Math.Sin(angle) * radius);
    }

    /// <summary>
    /// Один шаг «имитации телеметрии»: агент подтягивается к цели и дрожит случайным образом.
    /// pull — доля расстояния до цели, которую агент проходит за шаг; jitter — размах случайного смещения.
    /// </summary>
    public static (double X, double Y) Step((double X, double Y) current, (double X, double Y) target, Random rng, double pull, double jitter)
    {
        var x = current.X + (target.X - current.X) * pull + (rng.NextDouble() * 2 - 1) * jitter;
        var y = current.Y + (target.Y - current.Y) * pull + (rng.NextDouble() * 2 - 1) * jitter;
        return Clamp(x, y);
    }
}
