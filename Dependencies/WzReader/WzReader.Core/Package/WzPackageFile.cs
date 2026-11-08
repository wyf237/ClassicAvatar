using WzReader.Core.Common;
using WzReader.Core.Crypto;
using WzReader.Core.Diagnostics;
using WzReader.Core.Dom;
using WzReader.Core.Parsing;

namespace WzReader.Core.Package;

/// <summary>
/// 单个 WZ 文件（一个 <c>.wz</c> 或 <c>_000.wz</c> 分包）的读取会话。
/// </summary>
/// <remarks>
/// <para>
/// 职责边界：
/// <list type="bullet">
/// <item>打开时完成“头解析 → 结构预扫描 → 密钥探测 → 版本探测 → 目录树构建”；</item>
/// <item>运行期只负责按需读取 img 块字节并交给 <see cref="WzImageParser"/>；</item>
/// <item>不负责分包合并（由 <see cref="WzPackage"/> 负责）。</item>
/// </list>
/// </para>
/// <para>
/// 文件流用锁保护：img 懒解析可能来自多个线程（例如并行导出），随机读必须串行化。
/// 目录树本身在打开时已构建完毕，之后的读操作互不影响。
/// </para>
/// </remarks>
internal sealed class WzPackageFile : IDisposable
{
    private readonly FileStream _stream;
    private readonly object _readGate = new();
    private readonly bool _verifyChecksum;
    private readonly WzDiagnostics _diagnostics;

    private WzPackageFile(
        string filePath,
        FileStream stream,
        WzPackageHeader header,
        WzKeyType keyType,
        IWzXorKey key,
        WzScanResult scan,
        WzOpenOptions options,
        WzDiagnostics diagnostics)
    {
        this.FilePath = filePath;
        this._stream = stream;
        this.Header = header;
        this.KeyType = keyType;
        this.Key = key;
        this.Scan = scan;
        this._verifyChecksum = options.VerifyChecksum;
        this._diagnostics = diagnostics;
        this.TreeRoot = new WzNode(string.Empty, null, WzProperty.Instance);
    }

    /// <summary>文件完整路径。</summary>
    public string FilePath { get; }

    /// <summary>文件头。</summary>
    public WzPackageHeader Header { get; }

    /// <summary>探测到的字符串密钥类型。</summary>
    public WzKeyType KeyType { get; }

    /// <summary>字符串密钥。</summary>
    public IWzXorKey Key { get; }

    /// <summary>结构预扫描结果。</summary>
    public WzScanResult Scan { get; }

    /// <summary>临时根节点（子节点为顶层条目，稍后由包合并接管）。</summary>
    public WzNode TreeRoot { get; private set; }

    /// <summary>打开一个 WZ 文件。</summary>
    /// <param name="filePath">文件路径。</param>
    /// <param name="options">打开选项。</param>
    /// <param name="diagnostics">诊断收集器。</param>
    /// <returns>读取会话。</returns>
    public static WzPackageFile Open(string filePath, WzOpenOptions options, WzDiagnostics diagnostics)
    {
        FileStream stream = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            WzBinaryReader reader = new(stream);
            WzPackageHeader header = WzPackageHeader.Read(reader);

            WzKeyType keyType;
            WzScanResult scan;

            if (header.Format == WzPackageFormat.Pkg1)
            {
                scan = WzDirectoryScanner.ScanPkg1(reader, header, options.MaximumDepth);
                keyType = options.ForceKeyType ?? WzKeyDetector.DetectFromFirstString(
                    scan.FirstStringPayload ?? Array.Empty<byte>(),
                    scan.FirstStringUtf16);

                (int version, uint hashVersion) = WzVersionDetector.DetectPkg1(
                    header,
                    scan,
                    stream.Length,
                    options,
                    diagnostics);

                WzPackageFile file = new(filePath, stream, header, keyType, WzKeyFactory.Get(keyType), scan, options, diagnostics);
                IWzXorKey key = file.Key;
                file.TreeRoot = WzTreeBuilder.BuildPkg1(reader, header, hashVersion, key, file, diagnostics);

                diagnostics.Info(
                    $"已打开 {Path.GetFileName(filePath)}：{header}，版本 {version}，密钥 {keyType}，条目 {scan.Images.Count} 个 img。",
                    filePath);
                return file;
            }
            else
            {
                scan = WzVersionDetector.DetectPkg2(header, reader, stream.Length, options, diagnostics);
                keyType = options.ForceKeyType ?? WzKeyDetector.DetectFromFirstString(
                    scan.FirstStringPayload ?? Array.Empty<byte>(),
                    scan.FirstStringUtf16);

                WzPackageFile file = new(filePath, stream, header, keyType, WzKeyFactory.Get(keyType), scan, options, diagnostics);
                IWzXorKey propertyKey = file.Key;
                IWzXorKey dirKey = WzKeyFactory.CreatePkg2DirStringKey();
                file.TreeRoot = WzTreeBuilder.BuildPkg2(
                    reader,
                    header,
                    header.HashVersion,
                    propertyKey,
                    dirKey,
                    file,
                    diagnostics);

                diagnostics.Info($"已打开 PKG2 {Path.GetFileName(filePath)}：{header}。", filePath);
                return file;
            }
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>按位置读取 img 块原始字节。</summary>
    /// <param name="info">img 位置信息。</param>
    /// <returns>img 块字节。</returns>
    public byte[] ReadImageBytes(WzImageInfo info)
    {
        lock (this._readGate)
        {
            if (info.Offset < 0 || info.Size < 0 || info.Offset + info.Size > this._stream.Length)
            {
                throw new WzFormatException(
                    $"img 块越界：offset={info.Offset}, size={info.Size}, 文件长度={this._stream.Length}。");
            }

            this._stream.Position = info.Offset;
            byte[] buffer = new byte[info.Size];
            int read = 0;
            while (read < buffer.Length)
            {
                int n = this._stream.Read(buffer, read, buffer.Length - read);
                if (n <= 0)
                {
                    throw new WzFormatException($"读取 img 块时提前到达文件末尾（已读 {read}/{buffer.Length}）。");
                }

                read += n;
            }

            return buffer;
        }
    }

    /// <summary>
    /// 解析指定 img 节点（由 <see cref="WzNode"/> 的懒加载触发）。
    /// </summary>
    /// <param name="node">img 节点。</param>
    public void EnsureImageParsed(WzNode node)
    {
        WzImageInfo? info = node.ImageInfo;
        if (info == null)
        {
            return;
        }

        try
        {
            byte[] bytes = this.ReadImageBytes(info);
            if (this._verifyChecksum)
            {
                long sum = 0;
                foreach (byte value in bytes)
                {
                    sum += value;
                }

                if (sum != info.Checksum)
                {
                    this._diagnostics.Warn(
                        $"img checksum 不匹配（记录 {info.Checksum}，实际 {sum}），继续解析。",
                        node.Path);
                }
            }

            node.ClearChildren();
            WzImageParser.Parse(node, bytes, this.Key, this._diagnostics);
        }
        catch (WzException ex)
        {
            // 单张 img 损坏不应中断整包：记录告警并保留空节点。
            this._diagnostics.Warn($"img 解析失败：{ex.Message}", node.Path);
            node.ClearChildren();
            node.Value = null;
            node.MarkParsed();
        }
        catch (IOException ex)
        {
            this._diagnostics.Warn($"img 读取 IO 失败：{ex.Message}", node.Path);
            node.ClearChildren();
            node.Value = null;
            node.MarkParsed();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        this._stream.Dispose();
    }
}
