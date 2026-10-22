# 浮时 FloatTime

一个常驻桌面的 Windows 悬浮课表。卡片贴在屏幕边缘，显示当前在上的课、剩余时间和下一节；到点用系统通知加自绘弹窗提醒；主窗口负责课表、事项和统计。

单机、离线、无账号，所有数据存在本机 `%APPDATA%\FuShi` 下。

当前版本 `0.5.0`。

## 功能

**悬浮卡片**

- 四要素：当前科目、倒计时、进度条、下一节（取景时会过滤掉当日已被跳过的条目）。
- 鼠标悬停展开完整视图，移开收起。
- 拖动后自动贴边，左右上下四边都支持，阈值 32 DIP；贴边侧圆角变直角、亮一条 3px 主题色高亮线，位移做 160ms 缓动加轻微回弹。
- 贴底时展开面板向上生长，不会被屏幕底边截断。
- 助手表情：团子形象有 19 种情绪预设，按课程状态（临近、进行中、刚下课、长期无交互等）切换，带眨眼和张望的瞬态动画。

**课表**

- 7 天 × 时段的周网格，点格子新建，点条目编辑。
- 阶段（学期）管理：新建、重命名、切换生效、删除保护；切换后重排全量调度。
- 科目自定义颜色与分类（学习 / 工作 / 生活）。
- 当日单次顺延与跳过：只改当天这一次，不动周期模板，次日自动恢复原时间。顺延按链式传播，只在与下一节重叠时才推后后续课程，且不跨午夜。

**提醒**

- 全局提前量（0 = 准点），改动即时重排。
- 双通道：强提醒同时弹自绘弹窗和系统 Toast；免打扰时段内降级为系统通知。跨午夜的免打扰时段自动识别。
- 弹窗按钮：完成 / 稍后 / 跳过，结果写入 `CompletionLog`。
- 事项：一次性、每日、每周重复，带优先级和截止时间；到点同样走提醒。

**统计**

- 本周 / 本月 / 自定义区间。
- 完成率、计划时长与实际时长、科目分布、逐日明细。
- 历史记录存科目快照字段，删科目或改科目不会改写过去的数据。

**设置与数据安全**

- 明暗主题、悬浮窗不透明度、免打扰、提前量、IANA 时区。
- 开机自启（写 HKCU 注册表启动项，启动时自动纠正路径漂移；写入失败会回滚并提示）。
- 每日自动备份（`VACUUM INTO` 一致性快照，保留最近 7 份）、手动备份（不参与清理）、CSV 导出（UTF-8 带 BOM，Excel 直接打开不乱码）。

## 环境要求

| 用途 | 要求 |
|---|---|
| 运行 `portable` 包 | Windows 10 1903（build 18362）及以上，无需安装 .NET |
| 运行 `lite` 包或 `bin\` 下的多文件输出 | 同上，另需 [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) |
| 从源码构建 | .NET 8 SDK |

系统 Toast 走 `ToastNotificationManagerCompat`（非打包进程），所以目标框架钉在 `net8.0-windows10.0.18362.0`，低于这个 Windows 版本拿不到对应资产。

## 从源码构建与运行

```powershell
dotnet build FuShi/FuShi.sln -c Release
dotnet run --project FuShi/src/FuShi.App
```

调试产物在 `FuShi\src\FuShi.App\bin\Debug\net8.0-windows10.0.18362.0\`，是一个多文件目录，要连同 DLL 一起拷。

## 发布

两种单文件形态，产物目录里只留一个 `FuShi.exe`：

```powershell
# 自包含便携包，约 73 MB，拷走即用
dotnet publish FuShi/src/FuShi.App -p:PublishProfile=Portable

# 框架依赖轻量包，约 37 MB，需先装 .NET 8 Desktop Runtime
dotnet publish FuShi/src/FuShi.App -p:PublishProfile=PortableLite
```

| 配置 | 产物目录 | 体积 | 依赖 |
|---|---|---|---|
| `Portable.pubxml` | `bin\Release\publish\portable\` | ~73 MB | 无 |
| `PortableLite.pubxml` | `bin\Release\publish\lite\` | ~37 MB | .NET 8 Desktop Runtime |

两个包都不带数据库文件，只内嵌 SQLite 原生引擎 `e_sqlite3.dll`（单文件运行时自解压）。数据在首次启动时自动建库。

## 数据存放与恢复

```
%APPDATA%\FuShi\
├── fushi.db          主库（首次启动自动创建，日志模式固定 DELETE）
├── logs\             按天滚动的 Serilog 日志，保留 14 天
├── backups\          fushi-YYYYMMDD.db（自动，保留 7 份）
│                     fushi-manual-YYYYMMDD_HHmmss.db（手动，不清理）
└── export\           导出的 schedule-*.csv 与 tasks-*.csv
```

- 库在用户目录下，不在安装目录里。换 exe、挪文件夹、换发布形态都不会丢数据。
- 恢复：把备份文件改名成 `fushi.db` 放回 `%APPDATA%\FuShi\` 覆盖即可（程序未运行时操作）。
- 彻底重置：删掉整个 `%APPDATA%\FuShi` 目录。
- 数据库迁移失败不会直接删库：先清连接池退避重试，仍失败才把主库连同 `-journal` 等附属文件整体改名备份为 `fushi.db.broken-<时间戳>`，然后重建。

## 项目结构

```
FuShi/
├── src/
│   ├── FuShi.Domain/          实体、枚举、纯算法（周期展开、去重、错过补偿、
│   │                          时间线取景、顺延链、贴边吸附、助手表情状态机）
│   ├── FuShi.Infrastructure/  EF Core 上下文与迁移、调度引擎、备份导出、
│   │                          统计、Serilog、H.NotifyIcon 适配
│   └── FuShi.App/             WPF 表现层、MVVM、DI 宿主、托盘、悬浮窗
├── tests/FuShi.Tests/         xUnit + FluentAssertions，232 项
└── docs/                      各阶段实施计划（M2 → M5）
```

依赖方向是单向的：`App → Infrastructure → Domain`，领域层不引用 WPF 也不碰数据库，所有算法都能脱离 UI 单测。

## 几个关键设计

- **时间**：存墙钟时间 + IANA 时区 ID，不存 UTC 偏移。夏令时的无效时刻和歧义时刻显式归一；跨午夜的时段归起始日，不拆成两天。
- **调度**：`Task.Delay` + 可取消重排，不用固定周期轮询。每次休眠瞄准「下一个将触发的点」和「下一个进行中结束时刻」中较早的那个；系统唤醒事件只负责打断当前睡眠，补偿扫描作为每轮启动的前置兜底。
- **数据访问**：EF Core + `AddDbContextFactory` 短命上下文，不用单例 DbContext。SQLite 固定 DELETE 日志模式，避免 `-wal`/`-shm` 被安全软件拦截。
- **对账**：课表编辑后重展开未来的 Occurrence，不回写已发生的历史；模板修改时同步回退同批次的顺延条目。
- **单实例**：命名互斥锁 + 命名事件。二次启动不会静默退出，而是唤醒已有实例并前置主窗口。

## 测试与 CI

```powershell
dotnet test FuShi/FuShi.sln
```

`.github/workflows/ci.yml` 在 push / PR 到 `main` 时跑 Restore → Build（`-warnaserror`）→ Test，Windows runner，基线就是本地这条命令。

## 文档

`FuShi/docs/` 下按阶段记录，每份都包含范围、取舍、实施记录与踩坑结论：

| 文档 | 内容 |
|---|---|
| [M2-实施计划.md](FuShi/docs/M2-实施计划.md) | 存储层、三个领域算法、提醒引擎、双通道通知 |
| [M3-实施计划.md](FuShi/docs/M3-实施计划.md) | 周网格录入、设置页、提醒交互 |
| [M3.5-主窗口视觉实施计划.md](FuShi/docs/M3.5-主窗口视觉实施计划.md) | 设计系统、明暗主题、控件样式 |
| [M4-实施计划.md](FuShi/docs/M4-实施计划.md) | 阶段管理、统计页、事项页、发布收尾 |
| [M4.5-助手表情实施计划.md](FuShi/docs/M4.5-助手表情实施计划.md) | 19 种情绪、状态机、瞬态播放器 |
| [M5-实施计划.md](FuShi/docs/M5-实施计划.md) | 数据安全、交互语义、工程化、吸附增强、图标与菜单 |

仓库根目录另有一批 HTML 设计稿（主窗口视觉稿、助手形象探索、表情定稿、软件设计文档），是开发前的原型，不参与构建。

## 已知限制

- 只支持 Windows；界面仅中文。
- 单实例运行，同一用户同时只能开一个。
- 没有导入功能（只有导出）、没有云同步、没有自动更新、没有 MSIX 安装包。
- 重复规则只支持 `NONE` / `DAILY` / `WEEKLY` 三档，不解析完整 RRULE。
- 菜单模板只覆盖叶子项、复选标记和分隔线，没有二级子菜单。
- 未声明开源许可证。
