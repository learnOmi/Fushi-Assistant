namespace FuShi.Infrastructure.Scheduling;

/// <summary>
/// 提醒派发通道（设计文档 ADR-4 双通道的抽象）。
/// 引擎在后台线程同步调用本方法；实现方负责线程编组（切到 UI 线程）与降级策略。
/// 之所以抽成接口：让引擎（net8.0）与 WPF 表现层解耦，引擎因此可独立单测。
/// </summary>
public interface INotificationChannel
{
    /// <summary>派发一条提醒。实现应保证不抛出致命异常，内部失败自行降级并记日志。</summary>
    /// <param name="message">提醒内容。</param>
    void Dispatch(ReminderMessage message);
}
