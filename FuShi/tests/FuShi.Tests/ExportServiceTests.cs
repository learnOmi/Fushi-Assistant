using FluentAssertions;
using FuShi.Domain.Entities;
using FuShi.Domain.Enums;
using FuShi.Domain.Recurrence;
using FuShi.Infrastructure.Data;

namespace FuShi.Tests;

/// <summary>
/// M5a 导出服务测试：两份 CSV 都带 UTF-8 BOM，字段含逗号 / 引号 / 换行时不串列，中文正常。
/// </summary>
public sealed class ExportServiceTests : IDisposable
{
    private readonly TestDatabase _database = new();
    private readonly string _exportDirectory =
        Path.Combine(Path.GetTempPath(), $"fushi-export-test-{Guid.NewGuid():N}");

    /// <summary>课表导出：仅当前生效阶段的条目，BOM 存在，含逗号与换行的字段被正确包裹。</summary>
    [Fact]
    public async Task Export_Schedule_WritesBomAndEscapesFields()
    {
        Seed();

        var result = await new ExportService(_database.Factory, _database.Logger, _exportDirectory)
            .ExportAsync(new DateTime(2026, 10, 22, 10, 0, 0));

        result.Success.Should().BeTrue(result.Error);
        result.SchedulePath.Should().NotBeNullOrEmpty();
        var path = result.SchedulePath!;

        var bytes = await File.ReadAllBytesAsync(path!);
        bytes.Take(3).Should().Equal(0xEF, 0xBB, 0xBF);

        var text = await File.ReadAllTextAsync(path!);
        text.Should().Contain("周几,开始,结束,科目,分类,备注,启用");
        text.Should().Contain("周一,08:00,09:30,\"数学, 进阶\",学习,\"第1章");
        text.Should().Contain("\",是");
    }

    /// <summary>事项导出：全部事项，重复规则与状态用中文标签，标题中的引号翻倍转义。</summary>
    [Fact]
    public async Task Export_Tasks_WritesAllRowsWithLabels()
    {
        Seed();

        var result = await new ExportService(_database.Factory, _database.Logger, _exportDirectory)
            .ExportAsync(new DateTime(2026, 10, 22, 10, 0, 0));

        result.Success.Should().BeTrue(result.Error);
        result.TaskPath.Should().NotBeNullOrEmpty();
        var path = result.TaskPath!;
        var text = await File.ReadAllTextAsync(path!);

        text.Should().Contain("标题,截止,优先级,重复,状态");
        text.Should().Contain("\"复盘 \"\"双引号\"\"\",2026-10-22 20:00,高,每天,待办");
    }

    /// <summary>种一条生效阶段 + 科目 + 周一课表 + 一条每日重复事项。</summary>
    private void Seed()
    {
        using var context = _database.CreateContext();

        var phase = new Phase
        {
            Name = "2026 秋",
            StartDate = new DateOnly(2026, 9, 1),
            IsActive = true,
        };

        var inactive = new Phase
        {
            Name = "旧阶段",
            StartDate = new DateOnly(2025, 9, 1),
            IsActive = false,
        };

        var subject = new Subject { Name = "数学, 进阶", Category = SubjectCategory.Study };

        context.Phases.AddRange(phase, inactive);
        context.Subjects.Add(subject);
        context.SaveChanges();

        context.ScheduleEntries.Add(new ScheduleEntry
        {
            PhaseId = phase.Id,
            SubjectId = subject.Id,
            DayOfWeek = DayOfWeek.Monday,
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(9, 30),
            Note = "第1章\n重点",
            Enabled = true,
        });

        // 停用阶段下的条目不应出现在导出里。
        context.ScheduleEntries.Add(new ScheduleEntry
        {
            PhaseId = inactive.Id,
            SubjectId = subject.Id,
            DayOfWeek = DayOfWeek.Friday,
            StartTime = new TimeOnly(10, 0),
            EndTime = new TimeOnly(11, 0),
            Enabled = true,
        });

        context.Tasks.Add(new TaskItem
        {
            Title = "复盘 \"双引号\"",
            DueAt = new DateTime(2026, 10, 22, 20, 0, 0),
            RepeatRule = TaskRecurrenceExpander.RULE_DAILY,
            Priority = TaskPriority.High,
            Status = TaskItemStatus.Pending,
        });

        context.SaveChanges();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _database.Dispose();

        if (Directory.Exists(_exportDirectory))
        {
            Directory.Delete(_exportDirectory, recursive: true);
        }
    }
}
