namespace WzReader.Export;

/// <summary>
/// 导出结果汇总：包含成功写出的文件、失败项与跳过项数量。
/// </summary>
/// <remarks>
/// 导出过程遵循“局部失败不中断整体”的策略：单个 img/画布损坏只会记录到 <see cref="Errors"/>，
/// 其余文件继续导出。调用方可用 <see cref="IsSuccess"/> 快速判断是否全部成功。
/// </remarks>
public sealed class WzExportResult
{
    /// <summary>创建导出结果。</summary>
    /// <param name="files">成功写出的文件路径。</param>
    /// <param name="errors">失败项描述。</param>
    /// <param name="skipped">跳过项数量（例如全透明画布）。</param>
    public WzExportResult(IEnumerable<string> files, IEnumerable<string> errors, int skipped)
    {
        this.Files = files.ToArray();
        this.Errors = errors.ToArray();
        this.SkippedCount = skipped;
    }

    /// <summary>成功写出的文件路径（绝对路径）。</summary>
    public IReadOnlyList<string> Files { get; }

    /// <summary>失败项描述（每项形如“路径：原因”）。</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>成功写出的文件数量。</summary>
    public int FileCount => this.Files.Count;

    /// <summary>失败项数量。</summary>
    public int FailedCount => this.Errors.Count;

    /// <summary>跳过项数量。</summary>
    public int SkippedCount { get; }

    /// <summary>是否全部成功（无失败项）。</summary>
    public bool IsSuccess => this.Errors.Count == 0;

    /// <summary>生成一行摘要文本，适合打印到控制台。</summary>
    /// <returns>摘要。</returns>
    public override string ToString()
    {
        return $"导出完成：成功 {this.FileCount} 个文件，跳过 {this.SkippedCount} 个，失败 {this.FailedCount} 个。";
    }
}
