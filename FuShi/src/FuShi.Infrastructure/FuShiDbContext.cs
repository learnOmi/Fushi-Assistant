using FuShi.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FuShi.Infrastructure;

/// <summary>
/// 浮时的 EF Core 上下文（设计文档 2.1 的 8 张表）。
/// 只做映射与索引，不塞业务逻辑；每次操作由工厂创建**短生命周期**实例——
/// 因为 <see cref="DbContext"/> 非线程安全，引擎后台线程与 UI 线程不能共享同一实例。
/// </summary>
public class FuShiDbContext : DbContext
{
    /// <summary>构造上下文。</summary>
    /// <param name="options">由工厂注入的选项（含 SQLite 提供程序与连接串）。</param>
    public FuShiDbContext(DbContextOptions<FuShiDbContext> options)
        : base(options)
    {
    }

    /// <summary>阶段表。</summary>
    public DbSet<Phase> Phases => Set<Phase>();

    /// <summary>科目表。</summary>
    public DbSet<Subject> Subjects => Set<Subject>();

    /// <summary>课表条目表（周期模板）。</summary>
    public DbSet<ScheduleEntry> ScheduleEntries => Set<ScheduleEntry>();

    /// <summary>事项表（映射到表名 Tasks，避开 BCL 的 Task 命名冲突）。</summary>
    public DbSet<TaskItem> Tasks => Set<TaskItem>();

    /// <summary>提醒规则表。</summary>
    public DbSet<ReminderRule> ReminderRules => Set<ReminderRule>();

    /// <summary>提醒实例表（引擎账本）。</summary>
    public DbSet<Occurrence> Occurrences => Set<Occurrence>();

    /// <summary>完成记录表。</summary>
    public DbSet<CompletionLog> CompletionLogs => Set<CompletionLog>();

    /// <summary>应用设置表（单行）。</summary>
    public DbSet<AppSetting> Settings => Set<AppSetting>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Phase>().ToTable("Phases");
        modelBuilder.Entity<Subject>().ToTable("Subjects");
        modelBuilder.Entity<ScheduleEntry>().ToTable("ScheduleEntries");
        modelBuilder.Entity<TaskItem>().ToTable("Tasks");
        modelBuilder.Entity<ReminderRule>().ToTable("ReminderRules");
        modelBuilder.Entity<Occurrence>().ToTable("Occurrences");
        modelBuilder.Entity<CompletionLog>().ToTable("CompletionLogs");
        modelBuilder.Entity<AppSetting>().ToTable("Settings");

        // Phase 1 ──< N ScheduleEntry：删除阶段时级联删除其条目。
        modelBuilder.Entity<ScheduleEntry>()
            .HasOne(e => e.Phase)
            .WithMany(p => p.Entries)
            .HasForeignKey(e => e.PhaseId)
            .OnDelete(DeleteBehavior.Cascade);

        // ScheduleEntry >── 1 Subject：科目被条目引用，禁止级联删除（先解绑再删科目）。
        modelBuilder.Entity<ScheduleEntry>()
            .HasOne(e => e.Subject)
            .WithMany()
            .HasForeignKey(e => e.SubjectId)
            .OnDelete(DeleteBehavior.Restrict);

        // 核心约束：Occurrence 唯一键 (SourceType, SourceId, PlannedAt)——
        // 「同一计划时刻不重复弹」的数据库级保证，也是去重算法（内存）的兜底。
        modelBuilder.Entity<Occurrence>()
            .HasIndex(o => new { o.SourceType, o.SourceId, o.PlannedAt })
            .IsUnique();

        // 引擎每轮按计划时刻取最小堆，为扫描建一个时间索引。
        modelBuilder.Entity<Occurrence>()
            .HasIndex(o => o.PlannedAt);

        // AppSetting 是单行表：主键由业务指定（恒为 1），不由数据库生成。
        modelBuilder.Entity<AppSetting>()
            .Property(s => s.Id)
            .ValueGeneratedNever();

        // 课表条目按 (PhaseId, DayOfWeek) 查询，建组合索引。
        modelBuilder.Entity<ScheduleEntry>()
            .HasIndex(e => new { e.PhaseId, e.DayOfWeek });
    }
}
