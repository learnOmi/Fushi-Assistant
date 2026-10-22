using System.Runtime.Versioning;
using FluentAssertions;
using FuShi.Infrastructure.Platform;

namespace FuShi.Tests;

/// <summary>
/// 开机自启的纯函数测试（M4d）：注册表命令值的规范化。
/// 不触碰真实注册表——写入 Run 键会影响当前测试机的登录启动项。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AutostartServiceTests
{
    [Fact]
    public void BuildCommand_ShouldQuoteFullPathWithSpaces()
    {
        var path = Path.Combine(Path.GetTempPath(), "Fu Shi", "FuShi.exe");

        AutostartService.BuildCommand(path).Should().Be($"\"{Path.GetFullPath(path)}\"", "路径含空格时必须加引号");
    }

    [Fact]
    public void BuildCommand_ShouldResolveRelativePath()
    {
        var relative = Path.Combine("publish", "FuShi.exe");

        AutostartService.BuildCommand(relative)
            .Should().Be($"\"{Path.GetFullPath(relative)}\"", "注册表里必须是全路径");
    }

    [Fact]
    public void GetExecutablePath_ShouldReturnExistingPath()
    {
        var path = AutostartService.GetExecutablePath();

        path.Should().NotBeNullOrWhiteSpace("测试进程必然有可执行文件路径");
        File.Exists(path!).Should().BeTrue();
    }
}
