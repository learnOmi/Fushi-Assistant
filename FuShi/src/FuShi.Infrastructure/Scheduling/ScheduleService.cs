using FuShi.Domain;
using FuShi.Domain.Dedup;
using FuShi.Domain.Entities;
using FuShi.Domain.Enums;
using FuShi.Domain.Recurrence;
using FuShi.Domain.Scheduling;
using FuShi.Domain.Time;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace FuShi.Infrastructure.Scheduling;

/// <summary>
/// 课表服务：把「周期模板」展开成具体实例，供调度引擎与悬浮组件共用。
/// 承担四件事：
/// <list type="number">
/// <item>展开落库：把当前生效阶段的课表展开成 <see cref="Occurrence"/> 账本行（幂等，靠唯一索引兜底）。</item>
/// <item>上屏时间线：展开出带科目名/颜色的 <see cref="TimelineSlot"/>，供悬浮组件展示。</item>
/// <item>引擎取数：给出「未来待触发」与「已到期待处理」的计划实例。</item>
/// <item>状态写入：把实例标记为已触发 / 已错过。</item>
/// </list>
/// 每次操作都用工厂创建**短生命周期**上下文：本服务会被后台引擎线程与 UI 线程分别调用。
/// </summary>
/// <remarks>
/// M5c 起按职责拆成三个 partial 文件（纯移动、不改行为）：
/// 本文件放类头/字段/构造与共享的私有查询行类型；
/// <c>ScheduleService.Expansion.cs</c> 放展开、落库与对账；
/// <c>ScheduleService.Query.cs</c> 放取数、状态写入与时间计算。
/// </remarks>
public sealed partial class ScheduleService
{
    private readonly IDbContextFactory<FuShiDbContext> _contextFactory;
    private readonly AppSettingRepository _settings;
    private readonly ILogger _logger;

    /// <summary>构造课表服务。</summary>
    /// <param name="contextFactory">上下文工厂。</param>
    /// <param name="settings">设置仓储（读取时区）。</param>
    /// <param name="logger">Serilog 日志器。</param>
    public ScheduleService(
        IDbContextFactory<FuShiDbContext> contextFactory,
        AppSettingRepository settings,
        ILogger logger)
    {
        _contextFactory = contextFactory;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>查询行：账本行 + 上屏/组装所需的模板与科目（事项）上下文。</summary>
    /// <param name="Occurrence">账本行。</param>
    /// <param name="Title">标题：课表科目名或事项标题。</param>
    /// <param name="Note">备注（课表条目备注；事项无备注，为 null）。</param>
    /// <param name="EndTime">课表模板结束时刻；事项没有时长，为 null。</param>
    /// <param name="IsTask">是否来自事项。</param>
    private sealed record PendingRow(
        Occurrence Occurrence,
        string Title,
        string? Note,
        TimeOnly? EndTime,
        bool IsTask);

    /// <summary>
    /// 每物主提前量上下文（M4c）：全局值 + 各 (来源类型, 来源主键) 解析后的提前量 + 窗口用最大值。
    /// </summary>
    /// <param name="GlobalLeadMinutes">全局提前量。</param>
    /// <param name="MaxLeadMinutes">本轮所有规则与全局值中的最大提前量（仅用于放宽 SQL 窗口）。</param>
    /// <param name="ByOwner">按物主解析后的提前量。</param>
    private sealed record LeadContext(
        int GlobalLeadMinutes,
        int MaxLeadMinutes,
        IReadOnlyDictionary<(OwnerType OwnerType, int OwnerId), int> ByOwner)
    {
        /// <summary>取某个物主的提前量；没有规则时回落全局。</summary>
        public int Resolve(OwnerType ownerType, int ownerId) =>
            ByOwner.TryGetValue((ownerType, ownerId), out var lead) ? lead : GlobalLeadMinutes;
    }
}
