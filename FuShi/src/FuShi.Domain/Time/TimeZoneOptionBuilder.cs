using System;
using System.Collections.Generic;

namespace FuShi.Domain.Time;

/// <summary>
/// 时区下拉选项的构造器（设置页用时区列表）。
/// <para>
/// 背景：<see cref="TimeZoneInfo.GetSystemTimeZones"/> 在 Windows 上返回的是
/// 「GMT Standard Time」这类 Windows ID，而本项目存储的是「Europe/London」这类 IANA ID。
/// 若直接把系统列表丢给下拉框，当前值不在候选集里，选中项就会显示为空白。
/// 因此这里统一保证：默认时区（IANA）一定存在，且排在首位。
/// </para>
/// </summary>
public static class TimeZoneOptionBuilder
{
    /// <summary>
    /// 生成时区候选列表：系统时区 + 默认时区置顶（去重）。
    /// </summary>
    /// <param name="defaultTimeZoneId">项目使用的默认时区 ID（IANA，如 Europe/London）。</param>
    /// <returns>可直接绑定到下拉框的时区 ID 列表；默认时区恒在索引 0，且只出现一次。</returns>
    public static IReadOnlyList<string> Build(string defaultTimeZoneId)
    {
        var ids = new List<string>();
        foreach (var zone in TimeZoneInfo.GetSystemTimeZones())
        {
            ids.Add(zone.Id);
        }

        // 默认时区挪到首位：既是「当前值一定在候选中」的保证，也让用户第一眼看到自己的时区。
        // 用 Ordinal 比较，避免不同区域设置下的排序/比较差异。
        ids.RemoveAll(id => string.Equals(id, defaultTimeZoneId, StringComparison.Ordinal));
        ids.Insert(0, defaultTimeZoneId);
        return ids;
    }
}
