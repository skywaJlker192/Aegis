namespace Aegis.Services;

/// <summary>
/// Фоновая служба: раз в N секунд (по умолчанию 30) «получает телеметрию» агентов —
/// двигает их на схеме и рассылает событие AgentLocationUpdate штабу.
/// Интервал задаётся в appsettings.json: Simulation:LocationIntervalSeconds.
/// </summary>
public class AgentTelemetryWorker : BackgroundService
{
    private readonly IAgentTracker _tracker;
    private readonly ILogger<AgentTelemetryWorker> _logger;
    private readonly TimeSpan _interval;

    public AgentTelemetryWorker(IAgentTracker tracker, IConfiguration config, ILogger<AgentTelemetryWorker> logger)
    {
        _tracker = tracker;
        _logger = logger;

        var seconds = config.GetValue("Simulation:LocationIntervalSeconds", 30);
        _interval = TimeSpan.FromSeconds(Math.Max(1, seconds));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Трекер агентов запущен, интервал обновления: {Interval} с.", _interval.TotalSeconds);

        try
        {
            // Небольшая пауза, чтобы приложение успело полностью стартовать
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await _tracker.TickAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // Одна неудачная итерация не должна останавливать службу
                    _logger.LogError(ex, "Ошибка при обновлении положений агентов.");
                }

                await Task.Delay(_interval, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // штатная остановка приложения
        }
    }
}
