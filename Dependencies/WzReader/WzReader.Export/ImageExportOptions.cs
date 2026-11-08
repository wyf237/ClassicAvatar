namespace WzReader.Export;

/// <summary>
/// 图像导出选项：控制输出目录、目录镜像、链接跟随、透明图跳过等行为。
/// </summary>
/// <remarks>
/// <para>
/// 文件命名规则（基于 img ID，便于反查与增量导出）：
/// <code>
/// {输出目录}/{包路径}/{img 名称去掉 .img}/{属性路径用 . 连接}[_f{帧号}].png
/// </code>
/// 例如 <c>Character/Cap/01002000.img/stand1/0/cap</c> 导出为：
/// <code>
/// {输出目录}/Character/Cap/01002000/stand1.0.cap.png
/// </code>
/// 动画画布（多页）额外追加 <c>_f0</c>、<c>_f1</c>……
/// </para>
/// <para>
/// 选择“包路径 + img ID”作为前缀的原因：WZ 中的 img 名称（如 <c>01002000.img</c>）本身就是物品 ID，
/// 平铺导出会重名；保持目录层级既能唯一化路径，也方便按分类批量取用。
/// </para>
/// </remarks>
public sealed class ImageExportOptions
{
    /// <summary>创建图像导出选项。</summary>
    /// <param name="outputDirectory">输出根目录（不存在会自动创建）。</param>
    /// <exception cref="ArgumentException">目录为空。</exception>
    public ImageExportOptions(string outputDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        this.OutputDirectory = Path.GetFullPath(outputDirectory);
    }

    /// <summary>输出根目录（绝对路径）。</summary>
    public string OutputDirectory { get; }

    /// <summary>是否按包路径（如 <c>Character/Cap</c>）建立子目录，默认 <c>true</c>。</summary>
    public bool MirrorPackageStructure { get; init; } = true;

    /// <summary>是否跟随 UOL/_inlink/_outlink/source 链接导出真实画布，默认 <c>true</c>。</summary>
    public bool FollowLinks { get; init; } = true;

    /// <summary>是否跳过完全透明的画布，默认 <c>false</c>（保持无损，逐像素导出）。</summary>
    public bool SkipTransparentCanvas { get; init; }

    /// <summary>是否覆盖已存在的文件，默认 <c>true</c>；设为 <c>false</c> 时已存在文件计入跳过。</summary>
    public bool OverwriteExisting { get; init; } = true;

    /// <summary>是否始终在文件名中附加帧号（即使只有一帧），默认 <c>false</c>。</summary>
    public bool AlwaysAppendFrameIndex { get; init; }

    /// <summary>并行度，默认等于 CPU 核心数；设为 1 可关闭并行。</summary>
    public int MaxDegreeOfParallelism { get; init; } = Environment.ProcessorCount;
}
