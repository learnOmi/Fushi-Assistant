using System.IO;
using System.Text.Json;

namespace FuShi.Core;

/// <summary>
/// 悬浮窗状态持久化（M1 只记位置；后续里程碑在此追加折叠态、提醒分档等设置）。
/// 存储位置：%APPDATA%\FuShi\widget.json
/// </summary>
public sealed class WidgetStateStore
{
    private static string DirPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FuShi");

    private static string FilePath => Path.Combine(DirPath, "widget.json");

    public double X { get; set; }
    public double Y { get; set; }
    public bool HasSavedPosition { get; set; }

    public static WidgetStateStore Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<WidgetStateStore>(File.ReadAllText(FilePath)) ?? new();
        }
        catch
        {
            // 配置损坏时不阻断启动，回退默认位置
        }
        return new WidgetStateStore { X = 0, Y = 0 };
    }

    public void Save(double x, double y)
    {
        X = x; Y = y; HasSavedPosition = true;
        try
        {
            Directory.CreateDirectory(DirPath);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // 磁盘不可写时静默忽略，不影响运行
        }
    }
}
