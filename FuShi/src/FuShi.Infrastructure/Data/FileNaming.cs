using System.Globalization;
using System.IO;

namespace FuShi.Infrastructure.Data;

/// <summary>
/// 生成不冲突的文件名（M5a）：备份与导出都可能在同一秒内被连续触发，
/// 目标已存在时追加 <c>-2</c>、<c>-3</c> 等序号，绝不覆盖既有文件。
/// </summary>
public static class FileNaming
{
    /// <summary>冲突时最多尝试的序号上限。</summary>
    public const int MAX_SUFFIX_ATTEMPTS = 100;

    /// <summary>
    /// 返回一个当前不存在的路径；<paramref name="basePath"/> 本身可用时原样返回。
    /// </summary>
    /// <param name="basePath">按时间戳拼出的目标路径。</param>
    public static string EnsureUnique(string basePath)
    {
        if (!File.Exists(basePath))
        {
            return basePath;
        }

        var directory = Path.GetDirectoryName(basePath) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(basePath);
        var extension = Path.GetExtension(basePath);

        for (var index = 2; index <= MAX_SUFFIX_ATTEMPTS; index++)
        {
            var candidate = Path.Combine(
                directory,
                stem + "-" + index.ToString(CultureInfo.InvariantCulture) + extension);

            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        // 极端情况（同一秒触发几十次）：退回 ticks 唯一名字，保证不覆盖。
        return Path.Combine(
            directory,
            stem + "-" + DateTime.Now.Ticks.ToString(CultureInfo.InvariantCulture) + extension);
    }
}
