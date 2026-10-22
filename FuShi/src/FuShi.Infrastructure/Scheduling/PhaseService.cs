using FuShi.Domain;
using FuShi.Domain.Entities;
using FuShi.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace FuShi.Infrastructure.Scheduling;

/// <summary>
/// 阶段<b>管理</b>服务（M4a）：列出 / 新建 / 重命名 / 切换生效 / 删除。
/// <para>
/// 与 <see cref="ScheduleEditService"/> 的分工：后者负责「阶段内」的科目与课表条目，
/// 本服务只负责「阶段本身」的生命周期。切换只翻转 <c>IsActive</c> 一列，
/// <b>不复制课表</b>——各阶段课表相互独立，避免隐式复制产生脏数据。
/// </para>
/// <para>
/// 每次操作都用工厂创建短生命周期上下文；内部 await 统一 <c>ConfigureAwait(false)</c>，
/// 使 UI 线程可以同步等待而不死锁（沿用 M2 起已验证的模式）。
/// </para>
/// </summary>
public sealed class PhaseService
{
    private readonly IDbContextFactory<FuShiDbContext> _contextFactory;
    private readonly ILogger _logger;

    /// <summary>构造阶段管理服务。</summary>
    /// <param name="contextFactory">上下文工厂。</param>
    /// <param name="logger">Serilog 日志器。</param>
    public PhaseService(IDbContextFactory<FuShiDbContext> contextFactory, ILogger logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    /// <summary>取全部阶段（按生效起始日升序），供下拉与管理列表使用。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<IReadOnlyList<Phase>> GetPhasesAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        return await context.Phases
            .OrderBy(p => p.StartDate)
            .ThenBy(p => p.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 新建阶段。新建的阶段<b>不自动生效</b>：生效只有一个入口（<see cref="ActivateAsync"/>），
    /// 避免"建着建着课表就换了"。
    /// </summary>
    /// <param name="name">阶段名（必填）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>落库后的阶段（含 Id）。</returns>
    /// <exception cref="ArgumentException">名称为空白。</exception>
    public async Task<Phase> CreateAsync(string name, CancellationToken cancellationToken = default)
    {
        var trimmed = RequireName(name);

        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var phase = new Phase
        {
            Name = trimmed,
            StartDate = DateOnly.FromDateTime(DateTime.Today),
            EndDate = null,
            IsActive = false,
        };

        context.Phases.Add(phase);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.Information("新建阶段：{Name}（Id={Id}）", phase.Name, phase.Id);
        return phase;
    }

    /// <summary>重命名阶段。</summary>
    /// <param name="id">阶段主键。</param>
    /// <param name="name">新名称（必填）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>是否命中并更新。</returns>
    /// <exception cref="ArgumentException">名称为空白。</exception>
    public async Task<bool> RenameAsync(int id, string name, CancellationToken cancellationToken = default)
    {
        var trimmed = RequireName(name);

        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var affected = await context.Phases
            .Where(p => p.Id == id)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(p => p.Name, trimmed),
                cancellationToken)
            .ConfigureAwait(false);

        if (affected > 0)
        {
            _logger.Information("重命名阶段：Id={Id} → {Name}", id, trimmed);
        }

        return affected > 0;
    }

    /// <summary>
    /// 切换生效阶段。<b>唯一生效</b>在一个事务内完成：先把其余阶段置为 false，再把目标置为 true。
    /// 不用两段独立写入，是为了避免"两个阶段同时生效"的中间态被引擎读到。
    /// </summary>
    /// <param name="id">目标阶段主键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>目标阶段存在并已生效返回 true。</returns>
    public async Task<bool> ActivateAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var exists = await context.Phases
            .AnyAsync(p => p.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (!exists)
        {
            return false;
        }

        await using var transaction = await context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        await context.Phases
            .Where(p => p.IsActive && p.Id != id)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(p => p.IsActive, false),
                cancellationToken)
            .ConfigureAwait(false);

        await context.Phases
            .Where(p => p.Id == id)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(p => p.IsActive, true),
                cancellationToken)
            .ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        _logger.Information("切换生效阶段：Id={Id}", id);
        return true;
    }

    /// <summary>
    /// 删除阶段。三条保护：
    /// <list type="number">
    /// <item>生效中的阶段不允许删（否则界面与调度瞬间失去落点）。</item>
    /// <item>条目已产生完成记录时拒绝硬删（历史统计需要归属）。</item>
    /// <item>其余情况级联删除该阶段的课表条目；已发生的 <c>Occurrence</c> 不随条目删除，保留为历史。</item>
    /// </list>
    /// </summary>
    /// <param name="id">阶段主键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>删除结果（见 <see cref="PhaseDeleteOutcome"/>）。</returns>
    public async Task<PhaseDeleteOutcome> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var phase = await context.Phases
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (phase is null)
        {
            return PhaseDeleteOutcome.NotFound;
        }

        if (phase.IsActive)
        {
            return PhaseDeleteOutcome.Active;
        }

        var hasHistory = await (from log in context.CompletionLogs
                                join entry in context.ScheduleEntries on log.EntryId equals entry.Id
                                where entry.PhaseId == id
                                select log.Id)
            .AnyAsync(cancellationToken)
            .ConfigureAwait(false);

        if (hasHistory)
        {
            return PhaseDeleteOutcome.HasHistory;
        }

        context.Phases.Remove(phase);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.Information("删除阶段：{Name}（Id={Id}），其课表条目已级联删除", phase.Name, phase.Id);
        return PhaseDeleteOutcome.Deleted;
    }

    /// <summary>校验阶段名非空并去除首尾空白。</summary>
    /// <param name="name">原始名称。</param>
    /// <returns>修剪后的名称。</returns>
    /// <exception cref="ArgumentException">名称为空白。</exception>
    private static string RequireName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("阶段名不能为空", nameof(name));
        }

        return name.Trim();
    }
}
