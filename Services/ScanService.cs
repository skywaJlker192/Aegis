using System.Globalization;
using System.Security.Cryptography;
using Aegis.Models;
using Microsoft.AspNetCore.DataProtection;

namespace Aegis.Services;

/// <summary>Задание на сканирование: зона и подписанный токен, который клиент вернёт при фиксации.</summary>
public record ScanChallenge(string Token, double ZoneStart, double ZoneEnd, double SweepSeconds);

public enum ScanOutcome
{
    Hit,          // попал в зону
    Miss,         // промах
    InvalidToken  // токен подделан, просрочен или от другого пользователя/аномалии
}

public record ScanVerdict(ScanOutcome Outcome, AnomalyKind? Kind);

public interface IScanService
{
    /// <summary>Начать сканирование: сервер выбирает зону и тип аномалии, упаковывает их в защищённый токен.</summary>
    ScanChallenge Start(string userId, int anomalyId, AnomalyKind? existingKind);

    /// <summary>Проверить результат: попала ли отправленная частота в зону, зашитую в токене.</summary>
    ScanVerdict Verify(string token, string userId, int anomalyId, double frequency);
}

/// <summary>
/// Сервер ничего не хранит между запросами: зона, тип и срок действия лежат в токене,
/// зашифрованном и подписанном ASP.NET Core Data Protection. Подделать зону или тип на клиенте нельзя.
/// Токен привязан к пользователю и аномалии и живёт 3 минуты.
/// </summary>
public class ScanService : IScanService
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(3);
    private readonly ITimeLimitedDataProtector _protector;

    public ScanService(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("Aegis.Scan.v1").ToTimeLimitedDataProtector();
    }

    public ScanChallenge Start(string userId, int anomalyId, AnomalyKind? existingKind)
    {
        var rng = Random.Shared;
        var (start, end) = ScanGame.NextZone(rng);

        // Если аномалию уже определяли — тип не меняется, чтобы результат был стабильным
        var kind = existingKind ?? ScanGame.NextKind(rng);

        var plain = string.Join('|',
            userId,
            anomalyId.ToString(CultureInfo.InvariantCulture),
            start.ToString("F2", CultureInfo.InvariantCulture),
            end.ToString("F2", CultureInfo.InvariantCulture),
            ((int)kind).ToString(CultureInfo.InvariantCulture));

        var token = _protector.Protect(plain, TokenLifetime);
        return new ScanChallenge(token, start, end, ScanGame.SweepSeconds);
    }

    public ScanVerdict Verify(string token, string userId, int anomalyId, double frequency)
    {
        string plain;
        try
        {
            plain = _protector.Unprotect(token); // бросит исключение, если токен подделан или просрочен
        }
        catch (CryptographicException)
        {
            return new ScanVerdict(ScanOutcome.InvalidToken, null);
        }

        var parts = plain.Split('|');
        if (parts.Length != 5 ||
            parts[0] != userId ||
            parts[1] != anomalyId.ToString(CultureInfo.InvariantCulture) ||
            !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var start) ||
            !double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var end) ||
            !int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var kindValue) ||
            !Enum.IsDefined(typeof(AnomalyKind), kindValue))
        {
            return new ScanVerdict(ScanOutcome.InvalidToken, null);
        }

        return ScanGame.IsHit(frequency, start, end)
            ? new ScanVerdict(ScanOutcome.Hit, (AnomalyKind)kindValue)
            : new ScanVerdict(ScanOutcome.Miss, null);
    }
}
