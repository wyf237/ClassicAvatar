using System.Text.RegularExpressions;
using WzReader.Core.Diagnostics;
using WzReader.Core.Dom;

namespace WzReader.Core.Package;

/// <summary>
/// 数据根（通常对应客户端 <c>Data</c> 目录）：把 16 个分类目录中的所有 WZ 包聚合为一个全局命名空间。
/// </summary>
/// <remarks>
/// <para>
/// 目录约定（实测）：
/// <code>
/// Data/
///   Character/
///     Character.wz        ← 入口（目录树 + 少量占位 img）
///     Character_000.wz    ← 分包（真实数据）
///     Cap/
///       Cap.wz
///       Cap_000.wz
///       _Canvas/
///         _Canvas.wz
///         _Canvas_000.wz
/// </code>
/// 每个目录的“包根路径”就是该目录相对 Data 的路径，例如 <c>Character/Cap/_Canvas</c>。
/// 因此 <c>_outlink = "Character/Cap/_Canvas/01002000.img/info/icon"</c> 可以直接按全局路径解析。
/// </para>
/// <para>
/// 包采用<b>惰性加载</b>：<see cref="Open"/> 只扫描文件布局，真正读取目录树发生在首次按路径命中的时候。
/// 这样可以快速启动 Avatar/地图查看器，并避免一次性打开数百个分包。
/// </para>
/// </remarks>
public sealed class WzDataRoot : IDisposable
{
    private sealed class PackageSlot
    {
        public string RootPath = string.Empty;
        public string[] Files = Array.Empty<string>();
        public Lazy<WzPackage?> Package = null!;
    }

    private readonly List<PackageSlot> _slots = new();
    private readonly List<PackageSlot> _orderedSlots = new();
    private readonly WzOpenOptions _options;
    private readonly WzDiagnostics _diagnostics;
    private bool _disposed;

    private WzDataRoot(string dataDirectory, WzOpenOptions options, WzDiagnostics diagnostics)
    {
        this.DataDirectory = dataDirectory;
        this._options = options;
        this._diagnostics = diagnostics;
    }

    /// <summary>Data 目录的绝对路径。</summary>
    public string DataDirectory { get; }

    /// <summary>已发现（未必已加载）的包数量。</summary>
    public int PackageCount => this._slots.Count;

    /// <summary>诊断收集器。</summary>
    public WzDiagnostics Diagnostics => this._diagnostics;

    /// <summary>打开数据根。</summary>
    /// <param name="dataDirectory">Data 目录。</param>
    /// <param name="options">打开选项。</param>
    /// <returns>数据根。</returns>
    /// <exception cref="DirectoryNotFoundException">目录不存在。</exception>
    public static WzDataRoot Open(string dataDirectory, WzOpenOptions? options = null)
    {
        if (!Directory.Exists(dataDirectory))
        {
            throw new DirectoryNotFoundException($"数据目录不存在：{dataDirectory}");
        }

        WzOpenOptions actualOptions = options ?? new WzOpenOptions();
        WzDiagnostics diagnostics = actualOptions.GetDiagnostics();
        WzDataRoot root = new(
            Path.GetFullPath(dataDirectory),
            actualOptions.WithDiagnostics(diagnostics),
            diagnostics);

        // Data.wz（老版本单文件）作为全局根包。
        string dataWz = Path.Combine(root.DataDirectory, "Data.wz");
        if (File.Exists(dataWz))
        {
            root.AddSlot(string.Empty, new[] { dataWz });
        }

        foreach (string directory in EnumerateDirectories(root.DataDirectory))
        {
            root.DiscoverPackage(directory);
        }

        root._orderedSlots.AddRange(root._slots.OrderByDescending(static slot => slot.RootPath.Length));
        diagnostics.Info($"已发现 {root._slots.Count} 个 WZ 包。", root.DataDirectory);
        return root;
    }

    /// <summary>按全局路径查找节点（会按需加载命中的包）。</summary>
    /// <param name="path">全局路径，例如 <c>Character/Cap/_Canvas/01002000.img/info/icon</c>。</param>
    /// <param name="ignoreCase">是否忽略大小写（默认忽略，方便调用方）。</param>
    /// <returns>节点；路径不存在返回 <c>null</c>。</returns>
    public WzNode? FindNode(string path, bool ignoreCase = true)
    {
        string normalized = WzReader.Core.Common.WzPath.Normalize(path);
        if (normalized.Length == 0)
        {
            return null;
        }

        foreach (PackageSlot slot in this._orderedSlots)
        {
            string rootPath = slot.RootPath;
            if (rootPath.Length == 0)
            {
                WzPackage? globalPackage = this.Load(slot);
                WzNode? found = globalPackage?.FindPath(normalized, ignoreCase);
                if (found != null)
                {
                    return found;
                }

                continue;
            }

            if (normalized.Length == rootPath.Length &&
                string.Equals(normalized, rootPath, StringComparison.OrdinalIgnoreCase))
            {
                return this.Load(slot)?.Root;
            }

            if (normalized.Length > rootPath.Length &&
                normalized.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase) &&
                normalized[rootPath.Length] == WzReader.Core.Common.WzPath.Separator)
            {
                WzPackage? package = this.Load(slot);
                if (package == null)
                {
                    continue;
                }

                string relative = normalized[(rootPath.Length + 1)..];
                WzNode? node = package.FindPath(relative, ignoreCase);
                if (node != null)
                {
                    return node;
                }
            }
        }

        return null;
    }

    /// <summary>按包根路径获取包（会加载）。</summary>
    /// <param name="rootPath">包根路径（例如 <c>Character/Cap</c>）。</param>
    /// <returns>包；不存在或加载失败返回 <c>null</c>。</returns>
    public WzPackage? GetPackage(string rootPath)
    {
        string normalized = WzReader.Core.Common.WzPath.Normalize(rootPath);
        foreach (PackageSlot slot in this._slots)
        {
            if (string.Equals(slot.RootPath, normalized, StringComparison.OrdinalIgnoreCase))
            {
                return this.Load(slot);
            }
        }

        return null;
    }

    /// <summary>
    /// Disposes a single package and resets its lazy slot so the next access
    /// reopens it on demand. Used to release a browsed equipment slot without
    /// throwing away still-needed character packages.
    /// </summary>
    /// <param name="rootPath">Package root path (for example <c>Character/Cap</c>).</param>
    /// <returns><c>true</c> when a loaded package was closed.</returns>
    public bool ClosePackage(string rootPath)
    {
        string normalized = WzReader.Core.Common.WzPath.Normalize(rootPath);
        foreach (PackageSlot slot in this._slots)
        {
            if (!string.Equals(slot.RootPath, normalized, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!slot.Package.IsValueCreated)
            {
                return false;
            }

            WzPackage? package = slot.Package.Value;
            package?.UnparseImages();
            package?.Dispose();
            slot.Package = this.CreatePackage(slot);
            return package != null;
        }

        return false;
    }

    /// <summary>加载所有包（会读取全部目录树，适合批量导出）。</summary>
    /// <returns>包列表。</returns>
    public IReadOnlyList<WzPackage> LoadAllPackages()
    {
        List<WzPackage> packages = new(this._slots.Count);
        foreach (PackageSlot slot in this._slots)
        {
            WzPackage? package = this.Load(slot);
            if (package != null)
            {
                packages.Add(package);
            }
        }

        return packages;
    }

    /// <summary>枚举全部包中的所有 img 节点（加载所有包，但不解析 img 内容）。</summary>
    /// <returns>img 节点序列。</returns>
    public IEnumerable<WzNode> EnumerateImages()
    {
        foreach (WzPackage package in this.LoadAllPackages())
        {
            foreach (WzNode image in package.EnumerateImages())
            {
                yield return image;
            }
        }
    }

    /// <summary>
    /// 反解析所有已加载包中的 img 节点（不触发未加载包的打开）。
    /// </summary>
    /// <remarks>
    /// 用于释放已解析的 <c>WzCanvas</c> 与 <c>CompressedData</c> 内存，
    /// 使数据根回到接近"刚打开"的状态；再次访问时按需重新解析。
    /// </remarks>
    public void UnparseAllImages()
    {
        foreach (PackageSlot slot in this._slots)
        {
            if (slot.Package.IsValueCreated)
            {
                slot.Package.Value?.UnparseImages();
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (this._disposed)
        {
            return;
        }

        foreach (PackageSlot slot in this._slots)
        {
            if (slot.Package.IsValueCreated)
            {
                slot.Package.Value?.Dispose();
            }
        }

        this._slots.Clear();
        this._orderedSlots.Clear();
        this._disposed = true;
    }

    private void AddSlot(string rootPath, string[] files)
    {
        PackageSlot slot = new()
        {
            RootPath = rootPath,
            Files = files,
        };

        slot.Package = this.CreatePackage(slot);

        this._slots.Add(slot);
    }

    private Lazy<WzPackage?> CreatePackage(PackageSlot slot) =>
        new(
            () =>
            {
                try
                {
                    return WzPackage.Open(slot.RootPath, slot.Files, this._options);
                }
                catch (WzException ex)
                {
                    this._diagnostics.Warn($"加载包失败：{ex.Message}", slot.RootPath);
                    return null;
                }
                catch (IOException ex)
                {
                    this._diagnostics.Warn($"加载包 IO 失败：{ex.Message}", slot.RootPath);
                    return null;
                }
            },
            isThreadSafe: true);

    private WzPackage? Load(PackageSlot slot)
    {
        return slot.Package.Value;
    }

    private void DiscoverPackage(string directory)
    {
        string directoryName = Path.GetFileName(directory);
        if (directoryName.Length == 0)
        {
            return;
        }

        string mainFile = Path.Combine(directory, directoryName + ".wz");
        List<string> files = new();
        if (File.Exists(mainFile))
        {
            files.Add(mainFile);
        }

        // 分包：<Name>_000.wz、<Name>_001.wz ...，按索引升序。
        string pattern = "^" + Regex.Escape(directoryName) + "_(\\d{3})\\.wz$";
        Regex regex = new(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        List<(int Index, string Path)> extensions = new();
        foreach (string candidate in Directory.EnumerateFiles(directory, directoryName + "_*.wz"))
        {
            Match match = regex.Match(Path.GetFileName(candidate));
            if (match.Success && int.TryParse(match.Groups[1].Value, out int index))
            {
                extensions.Add((index, candidate));
            }
        }

        extensions.Sort(static (left, right) => left.Index.CompareTo(right.Index));
        files.AddRange(extensions.Select(static item => item.Path));

        if (files.Count == 0)
        {
            return;
        }

        string rootPath = Path.GetRelativePath(this.DataDirectory, directory).Replace('\\', '/');
        if (rootPath == ".")
        {
            rootPath = string.Empty;
        }

        this.AddSlot(WzReader.Core.Common.WzPath.Normalize(rootPath), files.ToArray());
    }

    private static IEnumerable<string> EnumerateDirectories(string root)
    {
        Stack<string> stack = new();
        stack.Push(root);

        while (stack.Count > 0)
        {
            string current = stack.Pop();
            foreach (string child in Directory.EnumerateDirectories(current))
            {
                yield return child;
                stack.Push(child);
            }
        }
    }
}
