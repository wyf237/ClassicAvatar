using WzReader.Core.Common;
using WzReader.Core.Package;

namespace WzReader.Core.Dom;

/// <summary>
/// WZ 节点树的统一节点模型：目录、img 根、属性容器与叶子值都使用本类表示。
/// </summary>
/// <remarks>
/// <para>
/// 设计取舍：
/// <list type="bullet">
/// <item>容器节点（Property）的 <see cref="Value"/> 是 <see cref="WzProperty"/> 标记对象，子节点存放在 <see cref="Children"/>；</item>
/// <item>叶子节点的 <see cref="Value"/> 直接是值对象（<c>int</c>/<c>string</c>/<see cref="WzVector"/>/<see cref="Codecs.WzCanvas"/> 等）；</item>
/// <item>img 条目节点同样是一个 <see cref="WzNode"/>（<see cref="Name"/> 为 <c>xxx.img</c>），首次访问 <see cref="Children"/> 时才解析 img 块，
/// 因此可以安全地只加载目录、按需读取大文件。</item>
/// </list>
/// </para>
/// <para>
/// 节点一旦构建完成，父子关系与名称不再改变；img 懒解析是唯一会改变 <see cref="Value"/> 与子节点的操作，
/// 内部用锁保证多线程下只解析一次。
/// </para>
/// </remarks>
public sealed class WzNode
{
    private readonly List<WzNode> _children = new();
    private readonly object _parseGate = new();
    private string? _pathOverride;
    private bool _parsed = true;

    /// <summary>创建节点。</summary>
    /// <param name="name">节点名。</param>
    /// <param name="parent">父节点。</param>
    /// <param name="value">节点值；容器节点传 <see cref="WzProperty"/>。</param>
    internal WzNode(string name, WzNode? parent, object? value)
    {
        this.Name = name;
        this.Parent = parent;
        this.Value = value;
    }

    /// <summary>创建一个“路径覆盖”节点（用于包根：名称短，但完整路径由包根路径决定）。</summary>
    /// <param name="name">短名称。</param>
    /// <param name="path">完整根路径。</param>
    /// <param name="value">节点值。</param>
    /// <returns>根节点。</returns>
    internal static WzNode CreateRoot(string name, string path, object? value)
    {
        return new WzNode(name, null, value) { _pathOverride = path };
    }

    /// <summary>节点名称（不含路径）。</summary>
    public string Name { get; internal set; }

    /// <summary>父节点；包根/Data 根为 <c>null</c>。</summary>
    public WzNode? Parent { get; private set; }

    /// <summary>节点值；容器节点为 <see cref="WzProperty"/> 标记。</summary>
    public object? Value { get; internal set; }

    /// <summary>img 条目的原始位置信息；非 img 节点为 <c>null</c>。</summary>
    public WzImageInfo? ImageInfo { get; internal set; }

    /// <summary>是否为 img 条目节点。</summary>
    public bool IsImage => this.ImageInfo != null;

    /// <summary>是否为容器节点（属性容器）。</summary>
    public bool IsContainer => this.Value is WzProperty;

    /// <summary>
    /// 子节点列表（img 节点会在首次访问时触发解析）。
    /// </summary>
    public IReadOnlyList<WzNode> Children
    {
        get
        {
            this.EnsureParsed();
            return this._children;
        }
    }

    /// <summary>不经解析直接获取当前已构建的子节点（内部与导出统计使用）。</summary>
    internal IReadOnlyList<WzNode> RawChildren => this._children;

    /// <summary>完整路径（从 Data 根或包根开始）。</summary>
    public string Path
    {
        get
        {
            if (this._pathOverride != null)
            {
                return this._pathOverride;
            }

            if (this.Parent == null)
            {
                return this.Name;
            }

            string parentPath = this.Parent.Path;
            return parentPath.Length == 0 ? this.Name : $"{parentPath}/{this.Name}";
        }
    }

    /// <summary>把节点标记为“需要解析 img 块”。</summary>
    internal void MarkImagePending(WzImageInfo info)
    {
        this.ImageInfo = info;
        this.Value = null;
        this._parsed = false;
    }

    /// <summary>替换 img 位置信息并重置解析状态（分包合并时用于择优）。</summary>
    internal void ReplaceImageInfo(WzImageInfo info)
    {
        if (this.ImageInfo == null)
        {
            return;
        }

        this.ImageInfo = info;
        this.Value = null;
        this._parsed = false;
        this._children.Clear();
    }

    /// <summary>把节点挂到新的父节点下（分包合并使用）。</summary>
    /// <param name="parent">新父节点。</param>
    internal void Reparent(WzNode parent)
    {
        this.Parent = parent;
    }

    /// <summary>
    /// 反解析 img 节点：丢弃已解析的子节点树与画布解码缓存，回到"待解析"状态。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 调用后该 img 的所有 <see cref="Codecs.WzCanvas"/>（含其 <c>CompressedData</c> 字节数组）
    /// 与子 <see cref="WzNode"/> 都不再被强引用，可被 GC 回收。
    /// 下次访问 <see cref="Children"/> 时会重新从文件解析，不影响功能正确性。
    /// </para>
    /// <para>
    /// 对非 img 节点调用无效。线程安全（与解析共用同一把锁）。
    /// </para>
    /// </remarks>
    public void UnparseImage()
    {
        if (!this.IsImage)
        {
            return;
        }

        lock (this._parseGate)
        {
            // 先释放已解码的像素缓存（可能位于大对象堆），再断开子节点引用。
            ClearCanvasFrames(this._children);
            this._children.Clear();
            this.Value = null;
            this._parsed = false;
        }
    }

    /// <summary>递归释放子树中所有画布的解码帧缓存。</summary>
    private static void ClearCanvasFrames(List<WzNode> nodes)
    {
        foreach (WzNode node in nodes)
        {
            if (node.Value is Codecs.WzCanvas canvas)
            {
                canvas.ClearFrames();
            }

            if (node._children.Count > 0)
            {
                ClearCanvasFrames(node._children);
            }
        }
    }

    /// <summary>按名称查找直接子节点，但<b>不触发</b> img 解析。</summary>
    /// <param name="name">子节点名。</param>
    /// <returns>找到的节点；不存在返回 <c>null</c>。</returns>
    internal WzNode? FindChildRaw(string name)
    {
        foreach (WzNode child in this._children)
        {
            if (string.Equals(child.Name, name, StringComparison.Ordinal))
            {
                return child;
            }
        }

        return null;
    }

    /// <summary>添加子节点（解析期内部使用）。</summary>
    /// <param name="child">子节点。</param>
    internal void AddChild(WzNode child)
    {
        child.Parent = this;
        this._children.Add(child);
    }

    /// <summary>清空子节点（调试/重建用）。</summary>
    internal void ClearChildren()
    {
        this._children.Clear();
    }

    /// <summary>把节点标记为已解析（img 解析完成后调用）。</summary>
    internal void MarkParsed()
    {
        this._parsed = true;
    }

    private void EnsureParsed()
    {
        if (this._parsed)
        {
            return;
        }

        lock (this._parseGate)
        {
            if (this._parsed)
            {
                return;
            }

            WzImageInfo? info = this.ImageInfo;
            if (info == null)
            {
                this._parsed = true;
                return;
            }

            info.OwnerFile.EnsureImageParsed(this);
            this._parsed = true;
        }
    }

    /// <summary>按名称查找直接子节点。</summary>
    /// <param name="name">子节点名。</param>
    /// <param name="ignoreCase">是否忽略大小写。</param>
    /// <returns>找到的节点；不存在返回 <c>null</c>。</returns>
    public WzNode? FindChild(string name, bool ignoreCase = false)
    {
        StringComparison comparison = ignoreCase
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        foreach (WzNode child in this.Children)
        {
            if (string.Equals(child.Name, name, comparison))
            {
                return child;
            }
        }

        return null;
    }

    /// <summary>按相对路径查找后代节点（不接受 <c>..</c>，请先用 <see cref="WzPath.ResolveRelative"/> 规范化）。</summary>
    /// <param name="relativePath">相对路径，允许以 <see cref="WzPath.Separator"/> 开头。</param>
    /// <param name="ignoreCase">是否忽略大小写。</param>
    /// <returns>找到的节点；路径断裂返回 <c>null</c>。</returns>
    public WzNode? FindPath(string relativePath, bool ignoreCase = false)
    {
        string normalized = WzPath.Normalize(relativePath);
        if (normalized.Length == 0)
        {
            return this;
        }

        WzNode? current = this;
        foreach (string segment in WzPath.Split(normalized))
        {
            current = current?.FindChild(segment, ignoreCase);
            if (current == null)
            {
                return null;
            }
        }

        return current;
    }

    /// <summary>获取直接子节点中的向量值（用于 <c>origin</c>、<c>map</c> 等锚点）。</summary>
    /// <param name="childName">子节点名。</param>
    /// <returns>向量；不存在或类型不符返回 <c>null</c>。</returns>
    public WzVector? GetVector(string childName)
    {
        return this.FindChild(childName)?.Value as WzVector?;
    }

    /// <summary>获取直接子节点中的整数值（<c>int</c>/<c>short</c>/<c>long</c> 均转 int）。</summary>
    /// <param name="childName">子节点名。</param>
    /// <returns>整数；不存在或类型不符返回 <c>null</c>。</returns>
    public int? GetInt(string childName)
    {
        object? value = this.FindChild(childName)?.Value;
        return value switch
        {
            int i => i,
            short s => s,
            long l => unchecked((int)l),
            byte b => b,
            _ => null,
        };
    }

    /// <summary>获取直接子节点中的字符串值。</summary>
    /// <param name="childName">子节点名。</param>
    /// <returns>字符串；不存在或类型不符返回 <c>null</c>。</returns>
    public string? GetString(string childName)
    {
        return this.FindChild(childName)?.Value as string;
    }

    /// <summary>返回可读摘要（用于日志）。</summary>
    /// <returns>形如 <c>name (Canvas 30x40)</c> 的文本。</returns>
    public override string ToString()
    {
        return this.Value switch
        {
            WzProperty => $"{this.Name} (Property)",
            Codecs.WzCanvas canvas => $"{this.Name} (Canvas {canvas.Width}x{canvas.Height})",
            WzVector vector => $"{this.Name} (Vector {vector})",
            WzUol uol => $"{this.Name} (UOL {uol.Path})",
            null => $"{this.Name} (Null)",
            _ => $"{this.Name} ({this.Value.GetType().Name})",
        };
    }
}

/// <summary>
/// img 条目的原始位置信息。用于导出原始块、校验 checksum，以及延迟解析。
/// </summary>
public sealed class WzImageInfo
{
    /// <summary>所属包文件（内部解析入口）。</summary>
    internal WzPackageFile OwnerFile { get; }

    /// <summary>创建位置信息。</summary>
    /// <param name="ownerFile">所属包文件。</param>
    /// <param name="offset">img 块在文件中的绝对偏移。</param>
    /// <param name="size">img 块字节长度。</param>
    /// <param name="checksum">目录条目记录的 checksum。</param>
    internal WzImageInfo(WzPackageFile ownerFile, long offset, int size, int checksum)
    {
        this.OwnerFile = ownerFile;
        this.Offset = offset;
        this.Size = size;
        this.Checksum = checksum;
    }

    /// <summary>img 块在文件中的绝对偏移。</summary>
    public long Offset { get; }

    /// <summary>img 块字节长度。</summary>
    public int Size { get; }

    /// <summary>目录条目记录的 checksum（字节直接求和）。</summary>
    public int Checksum { get; }

    /// <summary>来源文件完整路径。</summary>
    public string SourceFile => this.OwnerFile.FilePath;
}
