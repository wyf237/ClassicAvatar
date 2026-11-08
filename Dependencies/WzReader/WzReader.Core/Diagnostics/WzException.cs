namespace WzReader.Core.Diagnostics;

/// <summary>
/// WZ 读取过程中的异常基类。
/// </summary>
/// <remarks>
/// 设计原则：<b>格式错误只影响局部</b>。目录树/版本这类“没有正确数据就无法继续”的错误抛异常；
/// 单个画布格式未知、链接断裂、checksum 不符等局部问题只记录到 <see cref="WzDiagnostics"/>。
/// 这样大规模导出时不会因为一张坏图中断整个任务。
/// </remarks>
public class WzException : Exception
{
    /// <summary>创建异常。</summary>
    /// <param name="message">错误说明。</param>
    public WzException(string message)
        : base(message)
    {
    }

    /// <summary>创建带内部异常的异常。</summary>
    /// <param name="message">错误说明。</param>
    /// <param name="innerException">内部异常。</param>
    public WzException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// 文件不是 WZ（签名不符）或头部字段非法时抛出。
/// </summary>
public sealed class WzNotWzException : WzException
{
    /// <summary>创建异常。</summary>
    /// <param name="message">错误说明。</param>
    public WzNotWzException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// 文件是 WZ，但二进制结构与预期不符（越界、非法类型、校验失败到无法继续）时抛出。
/// </summary>
public sealed class WzFormatException : WzException
{
    /// <summary>创建异常。</summary>
    /// <param name="message">错误说明。</param>
    public WzFormatException(string message)
        : base(message)
    {
    }

    /// <summary>创建带内部异常的异常。</summary>
    /// <param name="message">错误说明。</param>
    /// <param name="innerException">内部异常。</param>
    public WzFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// 文件结构可识别，但当前实现尚不支持该特性（例如 BC7 纹理、更新的 PKG2 版本 profile）时抛出。
/// </summary>
public sealed class WzUnsupportedException : WzException
{
    /// <summary>创建异常。</summary>
    /// <param name="message">错误说明。</param>
    public WzUnsupportedException(string message)
        : base(message)
    {
    }
}
