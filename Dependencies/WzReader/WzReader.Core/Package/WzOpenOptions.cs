using WzReader.Core.Diagnostics;

namespace WzReader.Core.Package;

/// <summary>
/// 打开 WZ/Data 时使用的可选项（全部可选，默认值适用于绝大多数官方文件）。
/// </summary>
/// <remarks>
/// 这里的选项只影响“读取行为”，不改变数据模型。需要改变输出格式的选项在导出层。
/// </remarks>
public sealed class WzOpenOptions
{
    private WzDiagnostics? _fallbackDiagnostics;

    /// <summary>诊断收集器；为 <c>null</c> 时内部创建一个共享实例（同一选项对象只会创建一次）。</summary>
    public WzDiagnostics? Diagnostics { get; init; }

    /// <summary>是否校验 img 块 checksum（默认关闭：私服/改版文件经常不匹配）。</summary>
    public bool VerifyChecksum { get; init; }

    /// <summary>强制指定密钥类型；为 <c>null</c> 时自动探测。</summary>
    public WzReader.Core.Crypto.WzKeyType? ForceKeyType { get; init; }

    /// <summary>强制指定 hashVersion（PKG1 调试用）；为 <c>null</c> 时自动探测。</summary>
    public uint? ForceHashVersion { get; init; }

    /// <summary>目录树最大递归深度，防止损坏文件导致栈溢出。</summary>
    public int MaximumDepth { get; init; } = 64;

    /// <summary>创建默认选项。</summary>
    /// <param name="diagnostics">诊断收集器。</param>
    /// <returns>选项实例。</returns>
    public static WzOpenOptions Default(WzDiagnostics? diagnostics = null)
    {
        return new WzOpenOptions { Diagnostics = diagnostics };
    }

    /// <summary>获取诊断收集器（保证非空；未显式提供时复用内部实例）。</summary>
    internal WzDiagnostics GetDiagnostics()
    {
        return this.Diagnostics ?? (this._fallbackDiagnostics ??= new WzDiagnostics());
    }

    /// <summary>
    /// 复制当前选项并替换诊断收集器（数据根需要把所有包的问题汇总到同一个收集器）。
    /// </summary>
    /// <param name="diagnostics">新的诊断收集器。</param>
    /// <returns>新的选项实例。</returns>
    internal WzOpenOptions WithDiagnostics(WzDiagnostics diagnostics)
    {
        return new WzOpenOptions
        {
            Diagnostics = diagnostics,
            VerifyChecksum = this.VerifyChecksum,
            ForceKeyType = this.ForceKeyType,
            ForceHashVersion = this.ForceHashVersion,
            MaximumDepth = this.MaximumDepth,
        };
    }
}
