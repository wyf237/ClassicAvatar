namespace WzReader.Core.Diagnostics;

/// <summary>
/// 收集读取过程中的“非致命问题”（容错信息），供调用方判断数据是否可信。
/// </summary>
/// <remarks>
/// WZ 数据来自各方私服/不同版本，字段缺失、类型异常、链接断裂都很常见。
/// 本类采用“能读则读、读不了就记录并降级”的策略，避免一处坏数据中断整张地图或整套装备的加载。
/// 一个 <see cref="WzDiagnostics"/> 实例可被多个会话共享；内部使用锁保证线程安全。
/// </remarks>
public sealed class WzDiagnostics
{
    private readonly List<WzDiagnosticMessage> _messages = new();
    private readonly object _gate = new();

    /// <summary>可选：外部日志回调（例如写入 <c>ILogger</c> 或控制台）。</summary>
    public Action<WzDiagnosticMessage>? Sink { get; set; }

    /// <summary>已收集的告警快照。</summary>
    public IReadOnlyList<WzDiagnosticMessage> Messages
    {
        get
        {
            lock (this._gate)
            {
                return this._messages.ToArray();
            }
        }
    }

    /// <summary>记录一条告警。</summary>
    /// <param name="message">告警内容。</param>
    /// <param name="context">相关路径或位置（可为 <c>null</c>）。</param>
    public void Warn(string message, string? context = null)
    {
        this.Add(new WzDiagnosticMessage(WzDiagnosticLevel.Warning, message, context));
    }

    /// <summary>记录一条信息性消息（例如“使用 xxx 密钥”）。</summary>
    /// <param name="message">消息内容。</param>
    /// <param name="context">相关路径或位置（可为 <c>null</c>）。</param>
    public void Info(string message, string? context = null)
    {
        this.Add(new WzDiagnosticMessage(WzDiagnosticLevel.Information, message, context));
    }

    /// <summary>清空已收集的消息。</summary>
    public void Clear()
    {
        lock (this._gate)
        {
            this._messages.Clear();
        }
    }

    private void Add(WzDiagnosticMessage message)
    {
        lock (this._gate)
        {
            this._messages.Add(message);
        }

        this.Sink?.Invoke(message);
    }
}

/// <summary>告警级别。</summary>
public enum WzDiagnosticLevel
{
    /// <summary>一般信息。</summary>
    Information = 0,

    /// <summary>可疑但不影响读取。</summary>
    Warning = 1,
}

/// <summary>一条告警记录。</summary>
/// <param name="Level">级别。</param>
/// <param name="Message">内容。</param>
/// <param name="Context">相关路径或位置。</param>
public readonly record struct WzDiagnosticMessage(WzDiagnosticLevel Level, string Message, string? Context)
{
    /// <summary>格式化为单行文本。</summary>
    public override string ToString()
    {
        return string.IsNullOrEmpty(this.Context)
            ? $"[{this.Level}] {this.Message}"
            : $"[{this.Level}] {this.Message}｜{this.Context}";
    }
}
