namespace WzReader.Export;

/// <summary>
/// JSON 导出选项：控制输出目录、缩进、链接记录与目录镜像。
/// </summary>
/// <remarks>
/// 批量导出会在输出根目录额外生成 <c>_index.json</c>，列出所有 img 的全局路径、
/// 来源文件与对应 JSON 文件位置，便于外部工具建立索引。
/// </remarks>
public sealed class JsonExportOptions
{
    /// <summary>创建 JSON 导出选项。</summary>
    /// <param name="outputDirectory">输出根目录（不存在会自动创建）。</param>
    /// <exception cref="ArgumentException">目录为空。</exception>
    public JsonExportOptions(string outputDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        this.OutputDirectory = Path.GetFullPath(outputDirectory);
    }

    /// <summary>输出根目录（绝对路径）。</summary>
    public string OutputDirectory { get; }

    /// <summary>是否缩进输出（便于人工阅读），默认 <c>true</c>。</summary>
    public bool WriteIndented { get; init; } = true;

    /// <summary>是否记录链接对应关系（UOL/_inlink/_outlink/source），默认 <c>true</c>。</summary>
    public bool IncludeLinks { get; init; } = true;

    /// <summary>
    /// 是否解析链接目标的真实存在性，默认 <c>true</c>。
    /// 批量导出大量数据时，解析会触发跨包加载；若只关心静态结构可设为 <c>false</c>。
    /// </summary>
    public bool ResolveLinkTargets { get; init; } = true;

    /// <summary>是否按包路径建立子目录，默认 <c>true</c>。</summary>
    public bool MirrorPackageStructure { get; init; } = true;

    /// <summary>并行度，默认等于 CPU 核心数；设为 1 可关闭并行。</summary>
    public int MaxDegreeOfParallelism { get; init; } = Environment.ProcessorCount;

    /// <summary>批量导出时是否生成 <c>_index.json</c>，默认 <c>true</c>。</summary>
    public bool WriteIndexFile { get; init; } = true;
}
