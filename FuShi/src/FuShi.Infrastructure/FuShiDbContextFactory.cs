using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FuShi.Infrastructure;

/// <summary>
/// 设计时工厂：仅供 <c>dotnet ef</c> 生成迁移使用。
/// 运行时**不用**这个类——运行时由 <c>AddDbContextFactory</c> 注册的工厂创建上下文。
/// </summary>
public sealed class FuShiDbContextFactory : IDesignTimeDbContextFactory<FuShiDbContext>
{
    /// <inheritdoc />
    public FuShiDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<FuShiDbContext>()
            .UseSqlite(StoragePaths.ConnectionString)
            .Options;

        return new FuShiDbContext(options);
    }
}
