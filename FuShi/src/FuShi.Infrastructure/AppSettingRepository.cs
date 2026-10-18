using FuShi.Domain;
using FuShi.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FuShi.Infrastructure;

/// <summary>
/// 应用设置仓储（单行表 AppSetting 的读写）。
/// 取代 M1 的 widget.json：悬浮窗位置、透明度、时区、免打扰等统一落库。
/// 每次操作都用工厂创建**短生命周期**上下文，可安全地被 UI 线程与后台线程分别调用。
/// </summary>
public sealed class AppSettingRepository
{
    private readonly IDbContextFactory<FuShiDbContext> _contextFactory;

    /// <summary>构造仓储。</summary>
    /// <param name="contextFactory">上下文工厂。</param>
    public AppSettingRepository(IDbContextFactory<FuShiDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    /// <summary>
    /// 读取单行设置；首次运行（表为空）时插入一行默认值并返回。
    /// 注意：内部 await 一律 <c>ConfigureAwait(false)</c>，使本方法可被 UI 线程以
    /// <c>GetAwaiter().GetResult()</c> 同步等待而不产生死锁（启动时读取设置即为此场景）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<AppSetting> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var setting = await context.Settings
            .FirstOrDefaultAsync(s => s.Id == ModelDefaults.SINGLETON_ROW_ID, cancellationToken)
            .ConfigureAwait(false);

        if (setting is not null)
        {
            return setting;
        }

        setting = new AppSetting();
        context.Settings.Add(setting);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return setting;
    }

    /// <summary>
    /// 覆盖写入单行设置（主键恒为 <see cref="ModelDefaults.SINGLETON_ROW_ID"/>）。
    /// 同样使用 <c>ConfigureAwait(false)</c>，允许 UI 线程同步等待。
    /// </summary>
    /// <param name="setting">待保存的设置（通常来自 <see cref="GetAsync"/> 的返回值）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task SaveAsync(AppSetting setting, CancellationToken cancellationToken = default)
    {
        setting.Id = ModelDefaults.SINGLETON_ROW_ID;

        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        context.Settings.Update(setting);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
