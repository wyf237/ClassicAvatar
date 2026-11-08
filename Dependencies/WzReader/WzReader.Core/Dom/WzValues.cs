using WzReader.Core.Common;

namespace WzReader.Core.Dom;

/// <summary>
/// “属性容器”标记类型。节点 <see cref="WzNode.Value"/> 为该类型时，表示它是可包含子节点的 Property。
/// </summary>
/// <remarks>
/// 单独设置标记类型（而不是用 <c>null</c> 表示容器）可以区分两种情形：
/// <list type="bullet">
/// <item><c>Value == null</c>：值为 Null 的叶子（flag <c>0x00</c>）；</item>
/// <item><c>Value == WzProperty.Instance</c>：属性容器，子节点在 <see cref="WzNode.Children"/>。</item>
/// </list>
/// 该类型无状态，使用共享实例。
/// </remarks>
public sealed class WzProperty
{
    private WzProperty()
    {
    }

    /// <summary>共享实例。</summary>
    public static WzProperty Instance { get; } = new();

    /// <inheritdoc />
    public override string ToString() => "Property";
}

/// <summary>
/// UOL（Unity Object Link）节点值：一个相对其“父属性容器”的路径。
/// </summary>
/// <remarks>
/// UOL 与 <c>_inlink</c>/<c>_outlink</c> 的区别：
/// <list type="bullet">
/// <item>UOL 是节点值本身，路径相对父容器，可包含 <c>..</c> 跨出 img；</item>
/// <item>_inlink 是普通字符串属性，路径相对 img 根；</item>
/// <item>_outlink/source 是普通字符串属性，路径相对整个 Data 根。</item>
/// </list>
/// </remarks>
public sealed class WzUol
{
    /// <summary>创建 UOL 值。</summary>
    /// <param name="path">UOL 路径。</param>
    public WzUol(string path)
    {
        this.Path = path ?? string.Empty;
    }

    /// <summary>UOL 路径（以 <c>/</c> 分隔，允许 <c>..</c>）。</summary>
    public string Path { get; }

    /// <inheritdoc />
    public override string ToString() => this.Path;
}

/// <summary>
/// <c>Shape2D#Convex2D</c> 节点值：一组二维点，通常表示碰撞/点击区域。
/// </summary>
public sealed class WzConvex
{
    /// <summary>创建凸多边形数据。</summary>
    /// <param name="points">顶点集合。</param>
    public WzConvex(IReadOnlyList<WzVector> points)
    {
        this.Points = points ?? Array.Empty<WzVector>();
    }

    /// <summary>顶点集合（按文件顺序）。</summary>
    public IReadOnlyList<WzVector> Points { get; }
}

/// <summary>
/// <c>RawData</c> / <c>Canvas#Video</c> 节点值：未解释的二进制负载。
/// </summary>
/// <remarks>
/// RawData 自 GMS v243 引入，可能是任意压缩数据；本读取器只负责原样取出，
/// 具体含义由调用方（例如地图特效）解释。
/// </remarks>
public sealed class WzRawData
{
    /// <summary>创建原始负载。</summary>
    /// <param name="data">负载字节。</param>
    /// <param name="version">RawData 的版本字节（Video 固定为 0）。</param>
    /// <param name="isVideo">是否为 <c>Canvas#Video</c> 负载。</param>
    public WzRawData(byte[] data, byte version, bool isVideo)
    {
        this.Data = data ?? Array.Empty<byte>();
        this.Version = version;
        this.IsVideo = isVideo;
    }

    /// <summary>负载字节（未解密/解压）。</summary>
    public byte[] Data { get; }

    /// <summary>RawData 版本字节。</summary>
    public byte Version { get; }

    /// <summary>是否为视频负载。</summary>
    public bool IsVideo { get; }
}
