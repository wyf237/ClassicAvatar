using WzReader.Core.Common;
using WzReader.Core.Diagnostics;
using WzReader.Core.Dom;

namespace WzReader.Core.Links;

/// <summary>
/// WZ 链接解析器：把“链接节点”解析为它最终指向的真实节点或值。
/// </summary>
/// <remarks>
/// <para>
/// WZ 中存在四类间接引用，语义各不相同，必须分别处理：
/// <list type="table">
/// <listheader><term>种类</term><description>存放位置与基准路径</description></listheader>
/// <item><term><c>uol</c></term><description>节点值本身（<see cref="WzUol"/>），相对<b>父属性容器</b>，可含 <c>..</c> 跨出 img</description></item>
/// <item><term><c>_inlink</c></term><description>节点下的字符串子属性，相对<b>所属 img 根</b></description></item>
/// <item><term><c>_outlink</c></term><description>节点下的字符串子属性，相对<b>整个 Data 根</b>，路径中通常包含 <c>xxx.img</c></description></item>
/// <item><term><c>source</c></term><description>节点下的字符串子属性，优先按 Data 根解释，失败时再按 img 根解释</description></item>
/// </list>
/// </para>
/// <para>
/// 解析优先级：<c>source</c> → <c>_inlink</c> → <c>_outlink</c> → <c>uol</c>。
/// 一个节点可能连续多次间接引用（A 链接到 B，B 又链接到 C），解析器会逐跳追踪，
/// 并用“路径集合 + 节点引用集合”双重环检测，避免损坏数据导致死循环。
/// </para>
/// <para>
/// 本类无状态、线程安全。所有查找通过外部传入的委托完成，因此既可以用于
/// <see cref="Package.WzDataRoot"/> 全局命名空间，也可以用于单个 <see cref="Package.WzPackage"/>。
/// </para>
/// </remarks>
public static class WzLinkResolver
{
    /// <summary>默认最大跳数。</summary>
    public const int DefaultMaxHops = 16;

    /// <summary>判断节点本身是否为链接（UOL 值或含链接字符串子属性）。</summary>
    /// <param name="node">待判断节点。</param>
    /// <returns>是链接返回 <c>true</c>。</returns>
    public static bool IsLink(WzNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.Value is WzUol
            || HasLinkString(node, "source")
            || HasLinkString(node, "_inlink")
            || HasLinkString(node, "_outlink");
    }

    /// <summary>解析链接并返回最终目标节点（非链接节点原样返回）。</summary>
    /// <param name="node">起始节点。</param>
    /// <param name="lookup">按绝对路径查找节点的委托（路径已规范化，不含首尾分隔符）。</param>
    /// <param name="diagnostics">诊断收集器；可为 <c>null</c>。</param>
    /// <param name="maxHops">最大跳数。</param>
    /// <returns>最终目标节点；解析失败返回 <c>null</c>。</returns>
    public static WzNode? Resolve(
        WzNode node,
        Func<string, WzNode?> lookup,
        WzDiagnostics? diagnostics = null,
        int maxHops = DefaultMaxHops)
    {
        return ResolveDetailed(node, lookup, diagnostics, maxHops).Target;
    }

    /// <summary>解析链接并返回最终目标节点的值（非链接节点直接返回其自身值）。</summary>
    /// <param name="node">起始节点。</param>
    /// <param name="lookup">按绝对路径查找节点的委托。</param>
    /// <param name="diagnostics">诊断收集器；可为 <c>null</c>。</param>
    /// <param name="maxHops">最大跳数。</param>
    /// <returns>最终值（可能是 <see cref="Codecs.WzCanvas"/>、<see cref="WzVector"/>、<c>int</c>、<see cref="WzProperty"/> 等）；失败返回 <c>null</c>。</returns>
    public static object? ResolveValue(
        WzNode node,
        Func<string, WzNode?> lookup,
        WzDiagnostics? diagnostics = null,
        int maxHops = DefaultMaxHops)
    {
        WzNode? target = Resolve(node, lookup, diagnostics, maxHops);
        return target?.Value;
    }

    /// <summary>解析链接，返回包含跳转链与错误信息的结构（便于日志与调试）。</summary>
    /// <param name="node">起始节点。</param>
    /// <param name="lookup">按绝对路径查找节点的委托。</param>
    /// <param name="diagnostics">诊断收集器；可为 <c>null</c>。</param>
    /// <param name="maxHops">最大跳数。</param>
    /// <returns>解析结果。</returns>
    public static WzLinkResolution ResolveDetailed(
        WzNode node,
        Func<string, WzNode?> lookup,
        WzDiagnostics? diagnostics = null,
        int maxHops = DefaultMaxHops)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(lookup);

        List<string> chain = new();
        HashSet<string> visitedPaths = new(StringComparer.OrdinalIgnoreCase);
        HashSet<WzNode> visitedNodes = new(ReferenceEqualityComparer.Instance);

        WzNode current = node;
        for (int hop = 0; hop < maxHops; hop++)
        {
            if (!TryDescribeLink(current, out LinkDescriptor link))
            {
                // 不是链接（或链接路径为空）：当前节点即最终目标。
                return new WzLinkResolution(node, current, chain, null);
            }

            if (!visitedNodes.Add(current))
            {
                string message = $"链接出现循环：{DescribeChain(chain)}";
                diagnostics?.Warn(message, node.Path);
                return new WzLinkResolution(node, null, chain, message);
            }

            WzNode? target = null;
            string? usedPath = null;
            foreach (string candidate in link.Candidates)
            {
                if (candidate.Length == 0)
                {
                    continue;
                }

                if (!visitedPaths.Add(candidate))
                {
                    string message = $"链接路径重复，疑似循环：{candidate}";
                    diagnostics?.Warn(message, node.Path);
                    return new WzLinkResolution(node, null, chain, message);
                }

                target = LookupWithImageFallback(lookup, candidate);
                if (target != null)
                {
                    usedPath = candidate;
                    break;
                }
            }

            if (target == null)
            {
                string message = $"链接目标不存在（{link.Kind}）：{DescribeCandidates(link.Candidates)}";
                diagnostics?.Warn(message, node.Path);
                return new WzLinkResolution(node, null, chain, message);
            }

            chain.Add($"{link.Kind} → {usedPath}");
            current = target;
        }

        string overflow = $"链接跳数超过上限 {maxHops}：{DescribeChain(chain)}";
        diagnostics?.Warn(overflow, node.Path);
        return new WzLinkResolution(node, null, chain, overflow);
    }

    /// <summary>
    /// 获取节点的单跳链接信息（不追踪多跳链），用于导出时记录“链接对应关系”。
    /// </summary>
    /// <param name="node">待检查节点。</param>
    /// <param name="lookup">按绝对路径查找节点的委托。</param>
    /// <returns>链接信息；节点不是链接时返回 <c>null</c>。</returns>
    public static WzLinkInfo? GetLink(WzNode node, Func<string, WzNode?> lookup)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(lookup);

        if (!TryDescribeLink(node, out LinkDescriptor link))
        {
            return null;
        }

        string resolvedPath = link.Candidates.FirstOrDefault(static candidate => candidate.Length > 0) ?? string.Empty;
        WzNode? target = null;
        foreach (string candidate in link.Candidates)
        {
            if (candidate.Length == 0)
            {
                continue;
            }

            target = LookupWithImageFallback(lookup, candidate);
            if (target != null)
            {
                resolvedPath = candidate;
                break;
            }
        }

        return new WzLinkInfo(link.Kind, resolvedPath, target);
    }

    /// <summary>
    /// 获取节点的单跳链接信息，但<b>不解析目标是否存在</b>（避免批量导出时触发跨包加载）。
    /// </summary>
    /// <param name="node">待检查节点。</param>
    /// <returns>链接信息；节点不是链接时返回 <c>null</c>。</returns>
    public static WzLinkInfo? GetLink(WzNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (!TryDescribeLink(node, out LinkDescriptor link))
        {
            return null;
        }

        string resolvedPath = link.Candidates.FirstOrDefault(static candidate => candidate.Length > 0) ?? string.Empty;
        return new WzLinkInfo(link.Kind, resolvedPath, null);
    }

    /// <summary>使用 <see cref="Package.WzDataRoot"/> 作为全局命名空间解析链接。</summary>
    /// <param name="node">起始节点。</param>
    /// <param name="root">数据根。</param>
    /// <param name="diagnostics">诊断收集器；为 <c>null</c> 时使用数据根自带的收集器。</param>
    /// <param name="maxHops">最大跳数。</param>
    /// <returns>最终目标节点；失败返回 <c>null</c>。</returns>
    public static WzNode? Resolve(
        WzNode node,
        Package.WzDataRoot root,
        WzDiagnostics? diagnostics = null,
        int maxHops = DefaultMaxHops)
    {
        ArgumentNullException.ThrowIfNull(root);
        return Resolve(node, path => root.FindNode(path), diagnostics ?? root.Diagnostics, maxHops);
    }

    /// <summary>使用单个 <see cref="Package.WzPackage"/> 作为命名空间解析链接（跨包链接会失败并记录告警）。</summary>
    /// <param name="node">起始节点。</param>
    /// <param name="package">包。</param>
    /// <param name="diagnostics">诊断收集器；为 <c>null</c> 时使用包自带的收集器。</param>
    /// <param name="maxHops">最大跳数。</param>
    /// <returns>最终目标节点；失败返回 <c>null</c>。</returns>
    public static WzNode? Resolve(
        WzNode node,
        Package.WzPackage package,
        WzDiagnostics? diagnostics = null,
        int maxHops = DefaultMaxHops)
    {
        ArgumentNullException.ThrowIfNull(package);
        string rootPath = package.RootPath;
        return Resolve(
            node,
            path =>
            {
                if (rootPath.Length == 0)
                {
                    return package.FindPath(path);
                }

                if (!WzPath.StartsWithSegment(path, rootPath))
                {
                    return null;
                }

                return package.FindPath(path[(rootPath.Length + 1)..]);
            },
            diagnostics ?? package.Diagnostics,
            maxHops);
    }

    /// <summary>
    /// 按路径查找节点：直接命中失败时，尝试给最后一段补 <c>.img</c> 后缀。
    /// </summary>
    /// <remarks>
    /// 部分 WZ 数据中的 UOL 会省略 img 扩展名（例如 <c>../01002000</c> 实际指 <c>01002000.img</c>）。
    /// </remarks>
    private static WzNode? LookupWithImageFallback(Func<string, WzNode?> lookup, string path)
    {
        WzNode? node = lookup(path);
        if (node != null)
        {
            return node;
        }

        string name = WzPath.GetName(path);
        if (name.EndsWith(".img", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return lookup(path + ".img");
    }

    /// <summary>识别节点的链接信息；非链接返回 <c>false</c>。</summary>
    private static bool TryDescribeLink(WzNode node, out LinkDescriptor link)
    {
        link = default;

        // 1) source：优先按 Data 根解释，失败再尝试 img 根。
        string? source = node.GetString("source");
        if (!string.IsNullOrWhiteSpace(source))
        {
            link = new LinkDescriptor("source", BuildCandidates(node, source, dataRoot: true, imageRoot: true));
            return true;
        }

        // 2) _inlink：相对所属 img 根。
        string? inlink = node.GetString("_inlink");
        if (!string.IsNullOrWhiteSpace(inlink))
        {
            link = new LinkDescriptor("_inlink", BuildCandidates(node, inlink, dataRoot: false, imageRoot: true));
            return true;
        }

        // 3) _outlink：相对整个 Data 根。
        string? outlink = node.GetString("_outlink");
        if (!string.IsNullOrWhiteSpace(outlink))
        {
            link = new LinkDescriptor("_outlink", BuildCandidates(node, outlink, dataRoot: true, imageRoot: false));
            return true;
        }

        // 4) uol：相对父属性容器。
        if (node.Value is WzUol uol && !string.IsNullOrWhiteSpace(uol.Path))
        {
            string basePath = node.Parent?.Path ?? string.Empty;
            link = new LinkDescriptor("uol", new[] { WzPath.ResolveRelative(basePath, uol.Path) });
            return true;
        }

        return false;
    }

    /// <summary>按基准路径生成候选绝对路径（Data 根 / img 根）。</summary>
    private static string[] BuildCandidates(WzNode node, string rawPath, bool dataRoot, bool imageRoot)
    {
        List<string> candidates = new(2);
        if (dataRoot)
        {
            candidates.Add(WzPath.ResolveRelative(string.Empty, rawPath));
        }

        if (imageRoot)
        {
            string? imagePath = FindImageRootPath(node);
            if (imagePath != null)
            {
                candidates.Add(WzPath.ResolveRelative(imagePath, rawPath));
            }
        }

        return candidates.ToArray();
    }

    /// <summary>向上查找所属 img 节点的完整路径；不在 img 内返回 <c>null</c>。</summary>
    private static string? FindImageRootPath(WzNode node)
    {
        WzNode? current = node;
        while (current != null)
        {
            if (current.IsImage)
            {
                return current.Path;
            }

            current = current.Parent;
        }

        return null;
    }

    /// <summary>判断节点是否含有非空的指定字符串子属性（不触发 img 解析以外的工作）。</summary>
    private static bool HasLinkString(WzNode node, string childName)
    {
        return !string.IsNullOrWhiteSpace(node.GetString(childName));
    }

    private static string DescribeChain(IReadOnlyList<string> chain)
    {
        return chain.Count == 0 ? "(空)" : string.Join(" → ", chain);
    }

    private static string DescribeCandidates(IReadOnlyList<string> candidates)
    {
        return candidates.Count == 0 ? "(空)" : string.Join(" 或 ", candidates);
    }

    /// <summary>单跳链接描述：种类与候选绝对路径。</summary>
    private readonly struct LinkDescriptor
    {
        public LinkDescriptor(string kind, string[] candidates)
        {
            this.Kind = kind;
            this.Candidates = candidates;
        }

        /// <summary>链接种类（<c>source</c>/<c>_inlink</c>/<c>_outlink</c>/<c>uol</c>）。</summary>
        public string Kind { get; }

        /// <summary>候选绝对路径（依次尝试）。</summary>
        public string[] Candidates { get; }
    }
}

/// <summary>
/// 单跳链接信息：链接种类、解析出的目标路径与目标节点。
/// </summary>
/// <remarks>
/// 与 <see cref="WzLinkResolution"/> 的区别：本类型只描述“当前节点直接指向哪里”，不做多跳追踪，
/// 适合在导出 JSON 时记录原始的链接对应关系。
/// </remarks>
public sealed class WzLinkInfo
{
    /// <summary>创建链接信息。</summary>
    /// <param name="kind">链接种类（<c>source</c>/<c>_inlink</c>/<c>_outlink</c>/<c>uol</c>）。</param>
    /// <param name="targetPath">解析后的目标绝对路径。</param>
    /// <param name="target">目标节点；目标不存在时为 <c>null</c>。</param>
    internal WzLinkInfo(string kind, string targetPath, WzNode? target)
    {
        this.Kind = kind;
        this.TargetPath = targetPath;
        this.Target = target;
    }

    /// <summary>链接种类。</summary>
    public string Kind { get; }

    /// <summary>解析后的目标绝对路径。</summary>
    public string TargetPath { get; }

    /// <summary>目标节点；不存在时为 <c>null</c>。</summary>
    public WzNode? Target { get; }

    /// <summary>目标是否存在。</summary>
    public bool Exists => this.Target != null;

    /// <inheritdoc />
    public override string ToString() => $"{this.Kind} → {this.TargetPath}";
}

/// <summary>
/// 链接解析结果：包含最终目标、跳转链与错误信息，便于日志与问题定位。
/// </summary>
public sealed class WzLinkResolution
{
    /// <summary>创建解析结果。</summary>
    /// <param name="origin">起始节点。</param>
    /// <param name="target">最终目标节点；失败为 <c>null</c>。</param>
    /// <param name="chain">跳转链。</param>
    /// <param name="error">错误信息；成功为 <c>null</c>。</param>
    internal WzLinkResolution(WzNode origin, WzNode? target, IReadOnlyList<string> chain, string? error)
    {
        this.Origin = origin;
        this.Target = target;
        this.Chain = chain;
        this.Error = error;
    }

    /// <summary>起始节点。</summary>
    public WzNode Origin { get; }

    /// <summary>最终目标节点；解析失败为 <c>null</c>。</summary>
    public WzNode? Target { get; }

    /// <summary>逐跳解析出的路径描述列表。</summary>
    public IReadOnlyList<string> Chain { get; }

    /// <summary>失败原因；成功为 <c>null</c>。</summary>
    public string? Error { get; }

    /// <summary>起始节点是否为链接。</summary>
    public bool IsLink => this.Chain.Count > 0;

    /// <summary>是否解析成功。</summary>
    public bool Succeeded => this.Target != null;

    /// <summary>最终值（<see cref="Target"/> 的值）。</summary>
    public object? Value => this.Target?.Value;

    /// <inheritdoc />
    public override string ToString()
    {
        return this.Succeeded
            ? $"链接 {this.Origin.Path} → {this.Target!.Path}"
            : $"链接解析失败 {this.Origin.Path}：{this.Error}";
    }
}
