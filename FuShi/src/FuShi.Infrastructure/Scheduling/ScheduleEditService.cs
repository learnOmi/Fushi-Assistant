using FuShi.Domain;
using FuShi.Domain.Entities;
using FuShi.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace FuShi.Infrastructure.Scheduling;

/// <summary>
/// 课表<b>写</b>服务：阶段自举、科目 CRUD、课表条目 CRUD。
/// 与只读的 <see cref="ScheduleService"/> 分家——读写职责分开，避免把编辑逻辑塞进读路径。
/// <para>
/// 每次操作都用工厂创建<b>短生命周期</b>上下文：本服务会被 UI 线程（编辑落库）调用，
/// 内部 await 一律 <c>ConfigureAwait(false)</c>，使 UI 线程可 <c>GetAwaiter().GetResult()</c> 同步等待而不死锁
/// （沿用 M2 <c>PersistPosition</c> 已验证的模式）。
/// </para>
/// </summary>
public sealed class ScheduleEditService
{
    private readonly IDbContextFactory<FuShiDbContext> _contextFactory;
    private readonly ILogger _logger;

    /// <summary>构造写服务。</summary>
    /// <param name="contextFactory">上下文工厂。</param>
    /// <param name="logger">Serilog 日志器。</param>
    public ScheduleEditService(IDbContextFactory<FuShiDbContext> contextFactory, ILogger logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    /// <summary>
    /// 取当前生效阶段；若无任何 <c>IsActive</c> 阶段，则自举一个「默认课表」并落库。
    /// M3 尚不给阶段管理 UI，但 <see cref="ScheduleEntry"/> 必须挂阶段，故此处凭空兜一个。
    /// 风险对策：无论用户此前怎么删，只要"无生效阶段"就无条件重建，保证录入永远有落点。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>生效阶段（已落库）。</returns>
    public async Task<Phase> EnsureActivePhaseAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var phase = await context.Phases
            .Where(p => p.IsActive)
            .OrderByDescending(p => p.StartDate)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (phase is not null)
        {
            return phase;
        }

        phase = new Phase
        {
            Name = ModelDefaults.DEFAULT_PHASE_NAME,
            StartDate = DateOnly.FromDateTime(DateTime.Today),
            EndDate = null,
            IsActive = true,
        };

        context.Phases.Add(phase);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.Information("自举生效阶段：{Name}（Id={Id}）", phase.Name, phase.Id);
        return phase;
    }

    /// <summary>取全部科目（按名称升序），供编辑对话框的下拉选择。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<IReadOnlyList<Subject>> GetSubjectsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        return await context.Subjects
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>取单条课表条目（编辑既有条目时回填表单）；不存在返回 null。</summary>
    /// <param name="id">条目主键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<ScheduleEntry?> GetEntryAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        return await context.ScheduleEntries
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>新建科目；名称必填。</summary>
    /// <param name="name">科目名。</param>
    /// <param name="color">展示颜色（#RRGGBB）。</param>
    /// <param name="category">分类。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>落库后的科目（含 Id）。</returns>
    /// <exception cref="ArgumentException">名称为空白。</exception>
    public async Task<Subject> CreateSubjectAsync(
        string name,
        string color,
        SubjectCategory category,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("科目名不能为空", nameof(name));
        }

        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var subject = new Subject
        {
            Name = name.Trim(),
            Color = string.IsNullOrWhiteSpace(color) ? ModelDefaults.DEFAULT_SUBJECT_COLOR : color,
            Category = category,
        };

        context.Subjects.Add(subject);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.Information("新建科目：{Name}（Id={Id}）", subject.Name, subject.Id);
        return subject;
    }

    /// <summary>修改科目名称/颜色/分类；名称为空白时抛异常，科目不存在时返回 false。</summary>
    /// <param name="id">科目主键。</param>
    /// <param name="name">新名称。</param>
    /// <param name="color">新颜色。</param>
    /// <param name="category">新分类。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>是否命中并更新。</returns>
    public async Task<bool> UpdateSubjectAsync(
        int id,
        string name,
        string color,
        SubjectCategory category,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("科目名不能为空", nameof(name));
        }

        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var affected = await context.Subjects
            .Where(s => s.Id == id)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(s => s.Name, name.Trim())
                    .SetProperty(s => s.Color, color)
                    .SetProperty(s => s.Category, category),
                cancellationToken)
            .ConfigureAwait(false);

        return affected > 0;
    }

    /// <summary>
    /// 删除科目。若仍被课表条目引用（外键 Restrict），数据库会拒绝删除——
    /// 此处捕获该异常并返回 false，让调用方提示"请先解绑该科目下的时段"。
    /// </summary>
    /// <param name="id">科目主键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>是否删除成功。</returns>
    public async Task<bool> DeleteSubjectAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var subject = await context.Subjects
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (subject is null)
        {
            return false;
        }

        context.Subjects.Remove(subject);
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateException ex)
        {
            // 外键 Restrict 触发的拒绝删除属预期业务分支，记警告后返回 false，不静默吞掉。
            _logger.Warning(ex, "科目 {Id} 仍被课表条目引用，拒绝删除", id);
            return false;
        }
    }

    /// <summary>
    /// 新增或更新一条课表条目：<c>Id == 0</c> 视为新增，否则更新。
    /// </summary>
    /// <param name="entry">待落库的条目（新增时 Id 留 0）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>落库后的条目（含 Id）。</returns>
    public async Task<ScheduleEntry> UpsertEntryAsync(ScheduleEntry entry, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        if (entry.Id == 0)
        {
            context.ScheduleEntries.Add(entry);
        }
        else
        {
            context.ScheduleEntries.Update(entry);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.Information("保存课表条目：Id={Id} 周{Day} {Start}-{End} 科目={Subject}",
            entry.Id, entry.DayOfWeek, entry.StartTime, entry.EndTime, entry.SubjectId);
        return entry;
    }

    /// <summary>删除一条课表条目（历史 Occurrence 保留，不级联清除——"已发生的账本不改写"）。</summary>
    /// <param name="id">条目主键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>是否删除成功。</returns>
    public async Task<bool> DeleteEntryAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var affected = await context.ScheduleEntries
            .Where(e => e.Id == id)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        if (affected > 0)
        {
            _logger.Information("删除课表条目：Id={Id}", id);
        }

        return affected > 0;
    }
}
