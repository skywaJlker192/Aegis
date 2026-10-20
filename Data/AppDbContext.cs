using Aegis.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Aegis.Data;

/// <summary>
/// Контекст БД штаба. Наследуется от IdentityDbContext, поэтому таблицы Identity
/// (AspNetUsers, AspNetRoles...) создаются вместе с нашими.
/// Таблица AspNetUsers хранит агентов (класс Agent).
/// </summary>
public class AppDbContext : IdentityDbContext<Agent>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Anomaly> Anomalies => Set<Anomaly>();
    public DbSet<AnomalyLog> AnomalyLogs => Set<AnomalyLog>();
    public DbSet<Expedition> Expeditions => Set<Expedition>();
    public DbSet<ExpeditionMember> ExpeditionMembers => Set<ExpeditionMember>();
    public DbSet<FieldReport> FieldReports => Set<FieldReport>();
    public DbSet<CommunicationLog> CommunicationLogs => Set<CommunicationLog>();
    public DbSet<AgentJournal> AgentJournals => Set<AgentJournal>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Сначала конфигурация Identity (ключи, индексы пользователей и ролей)
        base.OnModelCreating(builder);

        // ---------- Agent ----------
        builder.Entity<Agent>(e =>
        {
            // Fluent API №1: позывной — до 50 символов
            e.Property(a => a.Callsign).HasMaxLength(50).IsRequired();
        });

        // ---------- Anomaly ----------
        builder.Entity<Anomaly>(e =>
        {
            e.Property(a => a.Title).HasMaxLength(100).IsRequired();
            e.Property(a => a.Description).HasMaxLength(500);

            // Fluent API №2: координаты decimal(10,7)
            e.Property(a => a.Latitude).HasPrecision(10, 7);
            e.Property(a => a.Longitude).HasPrecision(10, 7);

            // Индекс №1: статус аномалий часто используется в фильтрах
            e.HasIndex(a => a.Status);

            // Удаляем аномалию — удаляем её замеры и экспедиции
            e.HasMany(a => a.Logs)
                .WithOne(l => l.Anomaly)
                .HasForeignKey(l => l.AnomalyId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasMany(a => a.Expeditions)
                .WithOne(x => x.Anomaly)
                .HasForeignKey(x => x.TargetAnomalyId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ---------- Expedition ----------
        builder.Entity<Expedition>(e =>
        {
            // Индекс №2: экспедиции сортируются по дате начала
            e.HasIndex(x => x.StartDate);

            // Руководитель — агент. Каскад запрещён: нельзя случайно удалить экспедиции вместе с агентом
            e.HasOne(x => x.Leader)
                .WithMany()
                .HasForeignKey(x => x.LeaderId)
                .OnDelete(DeleteBehavior.Restrict);

            // Fluent API №3: Expedition -> FieldReport, каскадное удаление
            e.HasMany(x => x.Reports)
                .WithOne(r => r.Expedition)
                .HasForeignKey(r => r.ExpeditionId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasMany(x => x.Members)
                .WithOne(m => m.Expedition)
                .HasForeignKey(m => m.ExpeditionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ---------- ExpeditionMember (M:N) ----------
        builder.Entity<ExpeditionMember>(e =>
        {
            // Fluent API №4: составной первичный ключ
            e.HasKey(m => new { m.ExpeditionId, m.AgentId });

            e.Property(m => m.RoleInMission).HasMaxLength(50).IsRequired();

            e.HasOne(m => m.Agent)
                .WithMany(a => a.ExpeditionMembers)
                .HasForeignKey(m => m.AgentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ---------- FieldReport ----------
        builder.Entity<FieldReport>(e =>
        {
            e.Property(r => r.Text).HasMaxLength(2000).IsRequired();

            e.HasOne(r => r.Author)
                .WithMany(a => a.FieldReports)
                .HasForeignKey(r => r.AuthorId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ---------- CommunicationLog (чат штаб-поле) ----------
        builder.Entity<CommunicationLog>(e =>
        {
            e.Property(c => c.Message).HasMaxLength(500).IsRequired();

            // История чата всегда читается по экспедиции и времени
            e.HasIndex(c => new { c.ExpeditionId, c.Timestamp });

            e.HasOne(c => c.Expedition)
                .WithMany(x => x.Messages)
                .HasForeignKey(c => c.ExpeditionId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(c => c.Sender)
                .WithMany()
                .HasForeignKey(c => c.SenderId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ---------- AgentJournal (журнал полевого агента) ----------
        builder.Entity<AgentJournal>(e =>
        {
            e.Property(j => j.Text).HasMaxLength(1000).IsRequired();
            e.HasIndex(j => j.AgentId);

            e.HasOne(j => j.Agent)
                .WithMany(a => a.JournalEntries)
                .HasForeignKey(j => j.AgentId)
                .OnDelete(DeleteBehavior.Restrict);

            // Экспедиция удалена — запись в личном журнале остаётся (ExpeditionId станет null)
            e.HasOne(j => j.Expedition)
                .WithMany(x => x.JournalEntries)
                .HasForeignKey(j => j.ExpeditionId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasOne(j => j.Approver)
                .WithMany()
                .HasForeignKey(j => j.ApprovedBy)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ---------- AnomalyLog ----------
        builder.Entity<AnomalyLog>(e =>
        {
            e.Property(l => l.Source).HasMaxLength(50).IsRequired();
            e.Property(l => l.Message).HasMaxLength(300).IsRequired();
        });
    }
}
