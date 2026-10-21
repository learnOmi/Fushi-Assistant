using System.Diagnostics;
using System.Windows;
using Serilog;

namespace FuShi.UI;

/// <summary>
/// 运行时主题切换（M3.5）。
/// <para>
/// 职责只有一件事：把 <see cref="Application.Resources"/> 合并字典中<b>固定槽位</b>的那一份
/// 换成 Light 或 Dark。所有界面一律用 <c>DynamicResource</c> 引用令牌，换字典后自动重解析，
/// 窗口不重建、页签与滚动位置不丢。
/// </para>
/// <para>
/// 槽位约定（由 App.xaml 的合并顺序决定）：0 = Shared.xaml，1 = 主题，2 = Controls.xaml。
/// 只替换不增删，避免多次切换后合并表越换越长。
/// </para>
/// </summary>
internal sealed class ThemeService
{
    /// <summary>主题值：暗色（与 AppSetting.Theme 的存储值一致）。</summary>
    public const string THEME_DARK = "dark";

    /// <summary>主题值：亮色。</summary>
    public const string THEME_LIGHT = "light";

    /// <summary>主题字典在 MergedDictionaries 中的固定槽位。</summary>
    private const int THEME_SLOT_INDEX = 1;

    // pack URI 不写程序集名：省略程序集段即指向当前应用自身的资源。
    // 程序集名是 FuShi（不是 FuShi.App），写死程序集名会在改项目名时静默失效。
    private static readonly Uri LIGHT_SOURCE =
        new("pack://application:,,,/UI/Themes/Light.xaml", UriKind.Absolute);

    private static readonly Uri DARK_SOURCE =
        new("pack://application:,,,/UI/Themes/Dark.xaml", UriKind.Absolute);

    private readonly Application _application;
    private readonly ILogger _logger;

    /// <summary>当前生效的主题字典；null 表示启动后尚未应用。</summary>
    private ResourceDictionary? _current;

    /// <summary>当前主题值（规范化后）。</summary>
    private string _currentTheme = string.Empty;

    /// <summary>构造。</summary>
    /// <param name="application">WPF 应用实例（提供 Application.Resources）。</param>
    /// <param name="logger">Serilog 日志器。</param>
    public ThemeService(Application application, ILogger logger)
    {
        _application = application;
        _logger = logger;
    }

    /// <summary>
    /// 应用主题。启动时必须早于任何窗口创建（否则首帧会用上一套令牌再闪一下）。
    /// 相同主题重复调用是空操作。
    /// </summary>
    /// <param name="theme">存储值；无法识别时按暗色处理，不抛异常。</param>
    public void Apply(string? theme)
    {
        var normalized = Normalize(theme);
        if (_current is not null && string.Equals(normalized, _currentTheme, StringComparison.Ordinal))
        {
            return;
        }

        var dictionary = new ResourceDictionary
        {
            Source = normalized == THEME_LIGHT ? LIGHT_SOURCE : DARK_SOURCE,
        };

        var merged = _application.Resources.MergedDictionaries;
        if (_current is not null)
        {
            var index = merged.IndexOf(_current);
            if (index >= 0)
            {
                merged[index] = dictionary;
            }
            else
            {
                // 槽位被外部改动（例如后续追加了字典）：插回约定位置，保持索引稳定。
                merged.Insert(THEME_SLOT_INDEX, dictionary);
            }
        }
        else
        {
            merged.Insert(THEME_SLOT_INDEX, dictionary);
        }

        _current = dictionary;
        _currentTheme = normalized;
        _logger.Information("主题已应用：{Theme}", normalized);
    }

    /// <summary>
    /// 把外部传入的主题值规范化为 dark / light。
    /// 未知值（含空串）一律暗色：这是历史默认，也是悬浮组件的原始配色。
    /// </summary>
    /// <param name="theme">存储值。</param>
    /// <returns>规范化后的主题值。</returns>
    public static string Normalize(string? theme) =>
        string.Equals(theme, THEME_LIGHT, StringComparison.OrdinalIgnoreCase) ? THEME_LIGHT : THEME_DARK;

#if DEBUG
    /// <summary>
    /// 开发期自检：两套字典的键集合必须完全一致。
    /// 只比键名不比值——漏一个键会让某个控件在切换后静默取不到画刷。
    /// 仅在 Debug 构建里执行，Release 不做额外加载。
    /// </summary>
    public void VerifyKeyParity()
    {
        var light = LoadKeys(LIGHT_SOURCE);
        var dark = LoadKeys(DARK_SOURCE);

        var missingInDark = light.Except(dark).ToArray();
        var missingInLight = dark.Except(light).ToArray();
        if (missingInDark.Length == 0 && missingInLight.Length == 0)
        {
            return;
        }

        var message = $"主题字典键不一致：Dark 缺 [{string.Join(", ", missingInDark)}]，Light 缺 [{string.Join(", ", missingInLight)}]";
        _logger.Error(message);
        Debug.Fail(message);
    }

    /// <summary>加载字典并取出全部键名。</summary>
    /// <param name="source">字典 pack URI。</param>
    /// <returns>键名集合。</returns>
    private static HashSet<string> LoadKeys(Uri source)
    {
        var dictionary = new ResourceDictionary { Source = source };
        return dictionary.Keys.Cast<object>().Select(static key => key.ToString() ?? string.Empty).ToHashSet();
    }
#endif
}
