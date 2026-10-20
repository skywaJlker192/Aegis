using Aegis.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Aegis.Data;

/// <summary>
/// Подготовка базы при старте приложения:
/// 1) применяет миграции (если их ещё нет — создаёт схему напрямую),
/// 2) создаёт роли Agent / Moderator / Admin,
/// 3) создаёт пользователей (в том числе админа admin@aegis.org),
/// 4) наполняет таблицы тестовыми данными. Повторный запуск ничего не дублирует.
/// </summary>
public static class DbSeeder
{
    private record SeedUser(string Email, string Callsign, int Clearance, AgentStatus Status, string Password, string Role);

    // Учётные данные для тестирования (все пароли удовлетворяют правилам: >= 6 символов и есть цифра)
    private static readonly SeedUser[] SeedUsers =
    {
        new("admin@aegis.org",     "ORACLE",  5, AgentStatus.Active,  "AegisAdmin1!", AppRoles.Admin),
        new("moderator@aegis.org", "CURATOR", 4, AgentStatus.Active,  "Moder123",     AppRoles.Moderator),
        new("agent1@aegis.org",    "KESTREL", 3, AgentStatus.InField, "Agent123",     AppRoles.Agent),
        new("agent2@aegis.org",    "NOMAD",   2, AgentStatus.InField, "Agent123",     AppRoles.Agent),
        new("agent3@aegis.org",    "VIPER",   2, AgentStatus.Missing, "Agent123",     AppRoles.Agent),
    };

    public static async Task InitializeAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("DbSeeder");

        // Если миграции созданы (dotnet ef migrations add) — применяем их.
        // Если миграций пока нет — создаём схему по модели, чтобы `dotnet run` работал сразу.
        if (db.Database.GetMigrations().Any())
        {
            await db.Database.MigrateAsync();
        }
        else
        {
            logger.LogWarning("Миграции не найдены — схема БД создана через EnsureCreated. " +
                              "Для работы через миграции выполните: dotnet ef migrations add InitialCreate");
            await db.Database.EnsureCreatedAsync();
        }

        await VerifySchemaAsync(db);

        await SeedRolesAsync(services);
        var agents = await SeedUsersAsync(services);

        if (!await db.Anomalies.AnyAsync())
        {
            await SeedDomainDataAsync(db, agents);
            logger.LogInformation("Тестовые данные штаба загружены.");
        }
    }

    /// <summary>
    /// Если aegis.db осталась от старой версии проекта (до новых функций), в ней нет новых таблиц/колонок.
    /// Вместо загадочной SQL-ошибки показываем понятную инструкцию.
    /// </summary>
    private static async Task VerifySchemaAsync(AppDbContext db)
    {
        try
        {
            await db.CommunicationLogs.AnyAsync();
            await db.AgentJournals.AnyAsync();
            await db.Anomalies.Select(a => a.Kind).AnyAsync();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Схема БД устарела (нет таблиц CommunicationLogs / AgentJournals или колонки Anomalies.Kind). " +
                "Удалите файл aegis.db и запустите приложение снова. " +
                "Если вы используете миграции — выполните: dotnet ef migrations add AddInteractiveFeatures && dotnet ef database update.", ex);
        }
    }

    private static async Task SeedRolesAsync(IServiceProvider services)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in AppRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }
    }

    private static async Task<Dictionary<string, Agent>> SeedUsersAsync(IServiceProvider services)
    {
        var userManager = services.GetRequiredService<UserManager<Agent>>();
        var result = new Dictionary<string, Agent>();

        foreach (var s in SeedUsers)
        {
            var user = await userManager.FindByEmailAsync(s.Email);
            if (user is null)
            {
                user = new Agent
                {
                    UserName = s.Email,
                    Email = s.Email,
                    EmailConfirmed = true,
                    Callsign = s.Callsign,
                    ClearanceLevel = s.Clearance,
                    Status = s.Status
                };

                var created = await userManager.CreateAsync(user, s.Password);
                if (!created.Succeeded)
                {
                    var errors = string.Join("; ", created.Errors.Select(e => e.Description));
                    throw new InvalidOperationException($"Не удалось создать пользователя {s.Email}: {errors}");
                }
            }

            if (!await userManager.IsInRoleAsync(user, s.Role))
            {
                await userManager.AddToRoleAsync(user, s.Role);
            }

            result[s.Callsign] = user;
        }

        return result;
    }

    private static async Task SeedDomainDataAsync(AppDbContext db, Dictionary<string, Agent> agents)
    {
        var now = DateTime.UtcNow;

        // ---------- 5 аномалий ----------
        var anomalies = new[]
        {
            new Anomaly
            {
                Title = "Тепловой выброс в леднике, сектор Д-7",
                Description = "Источник тепла неизвестной природы под толщей льда. На поверхности +14°C при внешних −41°C. Датчики фиксируют периодические пульсации.",
                Latitude = -78.4640000m, Longitude = 106.8370000m,
                ThreatLevel = ThreatLevel.Critical, Status = AnomalyStatus.Investigating,
                DetectedAt = now.AddDays(-12),
                Kind = AnomalyKind.Thermal, ScannedAt = now.AddDays(-10)
            },
            new Anomaly
            {
                Title = "Сигнал на частоте 4.2 кГц, шахта «Вега»",
                Description = "Повторяющийся сигнал из заброшенной шахты. Структура напоминает ответ на наши собственные запросы.",
                Latitude = 68.9585000m, Longitude = 33.0827000m,
                ThreatLevel = ThreatLevel.High, Status = AnomalyStatus.Detected,
                DetectedAt = now.AddDays(-5)
            },
            new Anomaly
            {
                Title = "Организм в мёрзлом грунте (образец N-19)",
                Description = "Образец из вечной мерзлоты проявил активность после размораживания. Изолирован в блоке Б, доступ только по допуску 4+.",
                Latitude = 71.6900000m, Longitude = 128.8700000m,
                ThreatLevel = ThreatLevel.High, Status = AnomalyStatus.Contained,
                DetectedAt = now.AddDays(-30),
                Kind = AnomalyKind.Biological, ScannedAt = now.AddDays(-28)
            },
            new Anomaly
            {
                Title = "Аномальная гравитация, склад №3",
                Description = "Предметы на стеллажах смещались к центру помещения. Эффект исчез после отключения резервной линии питания.",
                Latitude = 47.2357000m, Longitude = 39.7015000m,
                ThreatLevel = ThreatLevel.Medium, Status = AnomalyStatus.Neutralized,
                DetectedAt = now.AddDays(-45),
                Kind = AnomalyKind.Gravitational, ScannedAt = now.AddDays(-44)
            },
            new Anomaly
            {
                Title = "Мерцание огней, станция «Каспий-2»",
                Description = "Освещение мерцает с периодом 7 секунд. Причина не найдена, сбоев оборудования не зафиксировано.",
                Latitude = 40.1792000m, Longitude = 49.3320000m,
                ThreatLevel = ThreatLevel.Low, Status = AnomalyStatus.Detected,
                DetectedAt = now.AddDays(-2)
            }
        };
        db.Anomalies.AddRange(anomalies);
        await db.SaveChangesAsync();

        // ---------- Замеры телеметрии ----------
        db.AnomalyLogs.AddRange(
            new AnomalyLog { AnomalyId = anomalies[0].Id, Source = "SENSOR-T1", Message = "Температура поверхности льда, °C", Reading = 14.2, RecordedAt = now.AddHours(-6) },
            new AnomalyLog { AnomalyId = anomalies[0].Id, Source = "SENSOR-T1", Message = "Температура поверхности льда, °C", Reading = 15.8, RecordedAt = now.AddHours(-3) },
            new AnomalyLog { AnomalyId = anomalies[0].Id, Source = "SENSOR-EM", Message = "Электромагнитный фон, мкТл", Reading = 87.4, RecordedAt = now.AddHours(-1) },
            new AnomalyLog { AnomalyId = anomalies[1].Id, Source = "RADIO-4", Message = "Мощность сигнала, дБ", Reading = -61.5, RecordedAt = now.AddHours(-8) },
            new AnomalyLog { AnomalyId = anomalies[1].Id, Source = "RADIO-4", Message = "Мощность сигнала, дБ", Reading = -58.9, RecordedAt = now.AddHours(-2) },
            new AnomalyLog { AnomalyId = anomalies[2].Id, Source = "BIO-LAB", Message = "Температура камеры хранения, °C", Reading = -24.0, RecordedAt = now.AddHours(-5) },
            new AnomalyLog { AnomalyId = anomalies[2].Id, Source = "BIO-LAB", Message = "Биологическая активность, усл. ед.", Reading = 0.3, RecordedAt = now.AddHours(-4) },
            new AnomalyLog { AnomalyId = anomalies[3].Id, Source = "GRAV-2", Message = "Ускорение свободного падения, м/с²", Reading = 9.81, RecordedAt = now.AddDays(-1) },
            new AnomalyLog { AnomalyId = anomalies[4].Id, Source = "LUX-1", Message = "Период мерцания, с", Reading = 7.0, RecordedAt = now.AddHours(-12) },
            new AnomalyLog { AnomalyId = anomalies[4].Id, Source = "LUX-1", Message = "Период мерцания, с", Reading = 7.0, RecordedAt = now.AddHours(-7) });

        // ---------- 4 экспедиции ----------
        var kestrel = agents["KESTREL"];
        var nomad = agents["NOMAD"];
        var viper = agents["VIPER"];

        var exp1 = new Expedition
        {
            TargetAnomalyId = anomalies[0].Id, LeaderId = kestrel.Id,
            StartDate = now.AddDays(-3), Status = ExpeditionStatus.Active
        };
        var exp2 = new Expedition
        {
            TargetAnomalyId = anomalies[1].Id, LeaderId = nomad.Id,
            StartDate = now.AddDays(4), Status = ExpeditionStatus.Planned
        };
        var exp3 = new Expedition
        {
            TargetAnomalyId = anomalies[2].Id, LeaderId = viper.Id,
            StartDate = now.AddDays(-28), EndDate = now.AddDays(-20), Status = ExpeditionStatus.Completed
        };
        var exp4 = new Expedition
        {
            TargetAnomalyId = anomalies[4].Id, LeaderId = kestrel.Id,
            StartDate = now.AddDays(-9), EndDate = now.AddDays(-8), Status = ExpeditionStatus.Aborted
        };
        db.Expeditions.AddRange(exp1, exp2, exp3, exp4);
        await db.SaveChangesAsync();

        // ---------- Составы экспедиций ----------
        db.ExpeditionMembers.AddRange(
            new ExpeditionMember { ExpeditionId = exp1.Id, AgentId = kestrel.Id, RoleInMission = "Руководитель", IsAlive = true },
            new ExpeditionMember { ExpeditionId = exp1.Id, AgentId = nomad.Id, RoleInMission = "Связной", IsAlive = true },
            new ExpeditionMember { ExpeditionId = exp1.Id, AgentId = viper.Id, RoleInMission = "Медик", IsAlive = true },
            new ExpeditionMember { ExpeditionId = exp2.Id, AgentId = nomad.Id, RoleInMission = "Руководитель", IsAlive = true },
            new ExpeditionMember { ExpeditionId = exp2.Id, AgentId = kestrel.Id, RoleInMission = "Подрывник", IsAlive = true },
            new ExpeditionMember { ExpeditionId = exp3.Id, AgentId = viper.Id, RoleInMission = "Руководитель", IsAlive = true },
            new ExpeditionMember { ExpeditionId = exp3.Id, AgentId = nomad.Id, RoleInMission = "Медик", IsAlive = true },
            new ExpeditionMember { ExpeditionId = exp4.Id, AgentId = kestrel.Id, RoleInMission = "Руководитель", IsAlive = true },
            new ExpeditionMember { ExpeditionId = exp4.Id, AgentId = viper.Id, RoleInMission = "Связной", IsAlive = false });

        // ---------- Полевые отчёты ----------
        db.FieldReports.AddRange(
            new FieldReport
            {
                ExpeditionId = exp1.Id, AuthorId = kestrel.Id, IsApproved = true, CreatedAt = now.AddDays(-2),
                Text = "Прибыли в сектор Д-7. Лёд вокруг источника подтаял, радиус около 30 м. Приборы показывают скачки электромагнитного фона. Продолжаем наблюдение."
            },
            new FieldReport
            {
                ExpeditionId = exp1.Id, AuthorId = nomad.Id, IsApproved = false, CreatedAt = now.AddDays(-1),
                Text = "Обнаружены следы на льду, ведущие от источника. Следы босые. Просим уточнить, есть ли в секторе другие группы."
            },
            new FieldReport
            {
                ExpeditionId = exp3.Id, AuthorId = viper.Id, IsApproved = true, CreatedAt = now.AddDays(-21),
                Text = "Образец N-19 неактивен при температуре ниже −20°C. Рекомендую хранить в камере глубокой заморозки и не допускать оттаивания."
            },
            new FieldReport
            {
                // Намеренно «опасный» текст: Razor экранирует его, скрипт не выполнится (проверка XSS-защиты)
                ExpeditionId = exp2.Id, AuthorId = nomad.Id, IsApproved = false, CreatedAt = now.AddHours(-5),
                Text = "Подготовка к выходу завершена. Тест экранирования: <script>alert('XSS')</script>"
            },
            new FieldReport
            {
                ExpeditionId = exp4.Id, AuthorId = kestrel.Id, IsApproved = true, CreatedAt = now.AddDays(-8),
                Text = "Потеряна связь с передовой группой. Экспедиция прервана по протоколу эвакуации."
            });

        await db.SaveChangesAsync();

        // ---------- Чат «Протокол связи» (экспедиция 1) ----------
        var curator = agents["CURATOR"];
        db.CommunicationLogs.AddRange(
            new CommunicationLog { ExpeditionId = exp1.Id, SenderId = kestrel.Id, IsFromHq = false, IsRead = true, Timestamp = now.AddHours(-30),
                Message = "Группа на точке. Связь устойчивая, приступаем к замерам." },
            new CommunicationLog { ExpeditionId = exp1.Id, SenderId = curator.Id, IsFromHq = true, IsRead = true, Timestamp = now.AddHours(-29),
                Message = "Принято. Докладывайте о тепловых показаниях каждые два часа." },
            new CommunicationLog { ExpeditionId = exp1.Id, SenderId = kestrel.Id, IsFromHq = false, IsRead = true, Timestamp = now.AddHours(-5),
                Message = "Фон растёт. Лёд подтаивает быстрее прогноза." },
            new CommunicationLog { ExpeditionId = exp1.Id, SenderId = curator.Id, IsFromHq = true, IsRead = false, Timestamp = now.AddHours(-4),
                Message = "Держите дистанцию 30 м. Эвакуация по первому сигналу." },
            new CommunicationLog { ExpeditionId = exp1.Id, SenderId = nomad.Id, IsFromHq = false, IsRead = false, Timestamp = now.AddHours(-1),
                Message = "Нашли следы на льду, ведут от источника. Просим проверить, нет ли других групп." });

        // ---------- Журнал полевого агента ----------
        db.AgentJournals.AddRange(
            new AgentJournal { AgentId = kestrel.Id, ExpeditionId = exp1.Id, EntryType = JournalEntryType.Observation,
                Text = "Температура льда у источника +14°C. Периодические пульсации раз в 40 секунд.",
                CreatedAt = now.AddDays(-2), IsApproved = true, ApprovedBy = curator.Id },
            new AgentJournal { AgentId = nomad.Id, ExpeditionId = exp1.Id, EntryType = JournalEntryType.Alert,
                Text = "Датчики зафиксировали скачок электромагнитного фона. Рекомендую отступить на второй рубеж.",
                CreatedAt = now.AddHours(-3), IsApproved = false },
            new AgentJournal { AgentId = viper.Id, ExpeditionId = exp3.Id, EntryType = JournalEntryType.Report,
                Text = "Образец N-19 передан в блок Б. Хранение при −24°C, оттаивание исключено.",
                CreatedAt = now.AddDays(-20), IsApproved = true, ApprovedBy = curator.Id },
            new AgentJournal { AgentId = kestrel.Id, ExpeditionId = null, EntryType = JournalEntryType.Observation,
                Text = "Личная заметка: у склада №3 после отключения резервной линии гул прекратился.",
                CreatedAt = now.AddHours(-8), IsApproved = false },
            new AgentJournal { AgentId = nomad.Id, ExpeditionId = exp2.Id, EntryType = JournalEntryType.Alert,
                Text = "Слышал голоса по радио на частоте шахты. Подтверждения нет.",
                CreatedAt = now.AddDays(-1), IsApproved = false, IsRejected = true });

        await db.SaveChangesAsync();
    }
}
