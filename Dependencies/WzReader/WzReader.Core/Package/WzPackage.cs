using WzReader.Core.Diagnostics;
using WzReader.Core.Dom;

namespace WzReader.Core.Package;

/// <summary>
/// 一个逻辑 WZ 包：由入口文件（<c>Name.wz</c>）与若干分包（<c>Name_000.wz</c> …）合并而成。
/// </summary>
/// <remarks>
/// <para>
/// 合并规则（与参考实现一致）：
/// <list type="bullet">
/// <item>目录节点同名时递归合并子节点；</item>
/// <item>img 节点同名时保留 <b>字节长度更大</b> 的那个（分包通常存放真实数据，入口文件多为占位）；</item>
/// <item>类型不同（目录 vs img）时保留先出现的节点，并记录告警。</item>
/// </list>
/// </para>
/// <para>
/// <see cref="Root"/> 的完整路径就是 <see cref="RootPath"/>（例如 <c>Character/Cap</c>），
/// 因此拼接出的节点路径可以直接作为全局查找键。
/// </para>
/// </remarks>
public sealed class WzPackage : IDisposable
{
    private readonly List<WzPackageFile> _files = new();
    private readonly WzDiagnostics _diagnostics;
    private bool _disposed;

    private WzPackage(string rootPath, WzOpenOptions options)
    {
        this.RootPath = rootPath;
        this._diagnostics = options.GetDiagnostics();

        string name = WzReader.Core.Common.WzPath.GetName(rootPath);
        if (name.Length == 0)
        {
            name = "Data";
        }

        this.Root = WzNode.CreateRoot(name, rootPath, WzProperty.Instance);
    }

    /// <summary>包在全局命名空间中的根路径（以 <c>/</c> 分隔；Data.wz 为空字符串）。</summary>
    public string RootPath { get; }

    /// <summary>包根节点。</summary>
    public WzNode Root { get; }

    /// <summary>组成该包的所有文件路径（入口文件在前，分包按索引升序）。</summary>
    public IReadOnlyList<string> FilePaths
    {
        get
        {
            List<string> paths = new(this._files.Count);
            foreach (WzPackageFile file in this._files)
            {
                paths.Add(file.FilePath);
            }

            return paths;
        }
    }

    /// <summary>包内 img 元素数量（不触发 img 解析）。</summary>
    public int ImageCount => this._files.Sum(static file => file.Scan.Images.Count);

    /// <summary>打开一个逻辑包。</summary>
    /// <param name="rootPath">包的全局根路径。</param>
    /// <param name="filePaths">入口文件与分包路径（顺序：入口在前）。</param>
    /// <param name="options">打开选项。</param>
    /// <returns>已合并的包。</returns>
    public static WzPackage Open(string rootPath, IEnumerable<string> filePaths, WzOpenOptions? options = null)
    {
        WzOpenOptions actualOptions = options ?? new WzOpenOptions();
        WzPackage package = new(WzReader.Core.Common.WzPath.Normalize(rootPath), actualOptions);

        foreach (string path in filePaths)
        {
            if (!File.Exists(path))
            {
                package._diagnostics.Warn("WZ 文件不存在，已跳过。", path);
                continue;
            }

            try
            {
                WzPackageFile file = WzPackageFile.Open(path, actualOptions, package._diagnostics);
                package._files.Add(file);
                Merge(package.Root, file.TreeRoot, package._diagnostics);
            }
            catch (WzException ex)
            {
                package._diagnostics.Warn($"打开 WZ 文件失败：{ex.Message}", path);
            }
            catch (IOException ex)
            {
                package._diagnostics.Warn($"读取 WZ 文件失败：{ex.Message}", path);
            }
        }

        return package;
    }

    /// <summary>打开单个 <c>.wz</c> 文件（不含分包），根路径取文件名（不含扩展名）。</summary>
    /// <param name="filePath">文件路径。</param>
    /// <param name="options">打开选项。</param>
    /// <returns>包。</returns>
    public static WzPackage OpenSingle(string filePath, WzOpenOptions? options = null)
    {
        string rootPath = Path.GetFileNameWithoutExtension(filePath);
        return Open(rootPath, new[] { filePath }, options);
    }

    /// <summary>
    /// 枚举包内所有 img 节点（只遍历目录树，不解析 img 内容）。
    /// </summary>
    /// <returns>img 节点序列。</returns>
    public IEnumerable<WzNode> EnumerateImages()
    {
        Stack<WzNode> stack = new();
        stack.Push(this.Root);

        while (stack.Count > 0)
        {
            WzNode node = stack.Pop();
            foreach (WzNode child in node.RawChildren)
            {
                if (child.IsImage)
                {
                    yield return child;
                }
                else if (child.IsContainer)
                {
                    stack.Push(child);
                }
            }
        }
    }

    /// <summary>
    /// 反解析包内所有已解析的 img 节点，释放其画布数据与子节点树。
    /// </summary>
    /// <remarks>
    /// 只遍历目录树（不触发解析），仅对已解析过的 img（<c>RawChildren</c> 非空）生效；
    /// 未解析的 img 保持原状，下次访问时按需解析。
    /// </remarks>
    public void UnparseImages()
    {
        foreach (WzNode image in this.EnumerateImages())
        {
            if (image.RawChildren.Count > 0)
            {
                image.UnparseImage();
            }
        }
    }

    /// <summary>按包内相对路径查找节点（不触发 img 解析，路径段逐级精确匹配）。</summary>
    /// <param name="relativePath">相对路径。</param>
    /// <param name="ignoreCase">是否忽略大小写。</param>
    /// <returns>节点；不存在返回 <c>null</c>。</returns>
    public WzNode? FindPath(string relativePath, bool ignoreCase = true)
    {
        return this.Root.FindPath(relativePath, ignoreCase);
    }

    /// <summary>返回诊断收集器（便于导出时汇总告警）。</summary>
    public WzDiagnostics Diagnostics => this._diagnostics;

    /// <inheritdoc />
    public void Dispose()
    {
        if (this._disposed)
        {
            return;
        }

        foreach (WzPackageFile file in this._files)
        {
            file.Dispose();
        }

        this._files.Clear();
        this._disposed = true;
    }

    private static void Merge(WzNode target, WzNode source, WzDiagnostics diagnostics)
    {
        // 用快照遍历：合并过程中会修改 target 的子节点集合。
        List<WzNode> children = new(source.RawChildren);
        foreach (WzNode child in children)
        {
            WzNode? existing = target.FindChildRaw(child.Name);
            if (existing == null)
            {
                child.Reparent(target);
                target.AddChild(child);
                continue;
            }

            if (child.IsImage)
            {
                if (existing.IsImage && child.ImageInfo!.Size > existing.ImageInfo!.Size)
                {
                    existing.ReplaceImageInfo(child.ImageInfo!);
                }
                else if (!existing.IsImage)
                {
                    diagnostics.Warn("分包中的 img 与已有目录重名，保留目录节点。", child.Path);
                }

                continue;
            }

            if (child.IsContainer && existing.IsContainer)
            {
                Merge(existing, child, diagnostics);
            }
            else
            {
                diagnostics.Warn("分包中的目录与已有 img 重名，保留 img 节点。", child.Path);
            }
        }
    }
}
