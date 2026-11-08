using System.Collections.Concurrent;
using WzReader.Core.Codecs;
using WzReader.Core.Diagnostics;
using WzReader.Core.Dom;
using WzReader.Core.Links;
using WzReader.Core.Package;
using WzReader.Export.Imaging;

namespace WzReader.Export;

/// <summary>
/// 图像导出器：把 WZ 画布解码为 PNG 并按“包路径 + img ID + 属性路径”落盘。
/// </summary>
/// <remarks>
/// <para>
/// 三类常用入口：
/// <list type="bullet">
/// <item><see cref="ExportAll()"/>：遍历数据根中的全部 img，批量导出（内部并行）；</item>
/// <item><see cref="ExportImage(WzNode)"/>：导出一张 img 内的所有画布；</item>
/// <item><see cref="ExportCanvas(WzCanvas, string, int)"/>：把单张画布写到指定路径。</item>
/// </list>
/// </para>
/// <para>
/// 每个节点都会尝试按 <see cref="ImageExportOptions.FollowLinks"/> 解析链接，
/// 因此 <c>_inlink</c>/<c>_outlink</c>/UOL 引用的画布也会以“引用位置”的路径导出，
/// 方便调用方按逻辑名称取图而不必自己处理链接。
/// </para>
/// </remarks>
public sealed class WzImageExporter
{
    private readonly WzDataRoot _root;
    private readonly ImageExportOptions _options;
    private readonly WzDiagnostics _diagnostics;

    /// <summary>创建图像导出器。</summary>
    /// <param name="root">数据根。</param>
    /// <param name="options">导出选项。</param>
    public WzImageExporter(WzDataRoot root, ImageExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(options);

        this._root = root;
        this._options = options;
        this._diagnostics = root.Diagnostics;
    }

    /// <summary>批量导出数据根中的全部 img（并行执行）。</summary>
    /// <returns>导出结果。</returns>
    public WzExportResult ExportAll()
    {
        ExportAccumulator accumulator = new();
        ParallelOptions parallelOptions = new()
        {
            MaxDegreeOfParallelism = Math.Max(1, this._options.MaxDegreeOfParallelism),
        };

        Parallel.ForEach(
            this._root.EnumerateImages(),
            parallelOptions,
            image => this.ExportImageCore(image, accumulator));

        return accumulator.ToResult();
    }

    /// <summary>导出单张 img 内的全部画布。</summary>
    /// <param name="image">img 节点。</param>
    /// <returns>导出结果。</returns>
    public WzExportResult ExportImage(WzNode image)
    {
        ArgumentNullException.ThrowIfNull(image);

        ExportAccumulator accumulator = new();
        this.ExportImageCore(image, accumulator);
        return accumulator.ToResult();
    }

    /// <summary>把单张画布保存为 PNG（适用于调用方已自行定位画布的场景）。</summary>
    /// <param name="canvas">画布值。</param>
    /// <param name="filePath">目标文件路径。</param>
    /// <param name="frameIndex">帧下标；默认 0。</param>
    /// <returns>写出的文件路径。</returns>
    public string ExportCanvas(WzCanvas canvas, string filePath, int frameIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        WzPixelBuffer buffer = canvas.GetFrame(frameIndex);
        PngEncoder.Save(buffer, filePath);
        return filePath;
    }

    /// <summary>静态便捷入口：批量导出全部 img。</summary>
    /// <param name="root">数据根。</param>
    /// <param name="options">导出选项。</param>
    /// <returns>导出结果。</returns>
    public static WzExportResult ExportAll(WzDataRoot root, ImageExportOptions options)
    {
        return new WzImageExporter(root, options).ExportAll();
    }

    /// <summary>导出单张 img 的全部画布（内部实现，供并行调用）。</summary>
    private void ExportImageCore(WzNode image, ExportAccumulator accumulator)
    {
        Stack<WzNode> stack = new();
        stack.Push(image);

        while (stack.Count > 0)
        {
            WzNode node = stack.Pop();
            try
            {
                this.TryExportNode(node, accumulator);
            }
            catch (WzException ex)
            {
                accumulator.AddError($"{node.Path}：{ex.Message}");
            }
            catch (IOException ex)
            {
                accumulator.AddError($"{node.Path}：{ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                accumulator.AddError($"{node.Path}：{ex.Message}");
            }

            // 链接节点自身的子节点只是链接字符串；遍历它们没有意义，跳过。
            if (WzLinkResolver.IsLink(node))
            {
                continue;
            }

            foreach (WzNode child in node.Children)
            {
                if (child.IsImage)
                {
                    continue;
                }

                if (child.IsContainer || child.Value is WzCanvas)
                {
                    stack.Push(child);
                }
            }
        }
    }

    /// <summary>尝试导出单个节点（若是画布或指向画布）。</summary>
    private void TryExportNode(WzNode node, ExportAccumulator accumulator)
    {
        WzCanvas? canvas = null;
        if (this._options.FollowLinks && WzLinkResolver.IsLink(node))
        {
            object? resolved = WzLinkResolver.ResolveValue(node, path => this._root.FindNode(path), this._diagnostics);
            canvas = resolved as WzCanvas;
        }

        canvas ??= node.Value as WzCanvas;
        if (canvas == null || canvas.FrameCount <= 0)
        {
            return;
        }

        string relative = WzExportPaths.BuildCanvasRelativePath(node, this._options.MirrorPackageStructure);
        bool appendFrameIndex = this._options.AlwaysAppendFrameIndex || canvas.FrameCount > 1;

        for (int frame = 0; frame < canvas.FrameCount; frame++)
        {
            WzPixelBuffer buffer = canvas.GetFrame(frame);
            if (this._options.SkipTransparentCanvas && buffer.FindOpaqueBounds() == null)
            {
                accumulator.AddSkipped();
                continue;
            }

            string suffix = appendFrameIndex ? $"_f{frame}" : string.Empty;
            string filePath = this.CombinePath(relative + suffix + ".png");

            if (!this._options.OverwriteExisting && File.Exists(filePath))
            {
                accumulator.AddSkipped();
                continue;
            }

            PngEncoder.Save(buffer, filePath);
            accumulator.Files.Add(filePath);
        }
    }

    /// <summary>把相对路径拼接为输出目录下的绝对路径（统一使用 Windows 分隔符）。</summary>
    private string CombinePath(string relativePath)
    {
        string native = relativePath.Replace(WzReader.Core.Common.WzPath.Separator, Path.DirectorySeparatorChar);
        return Path.Combine(this._options.OutputDirectory, native);
    }

    /// <summary>线程安全的导出统计累加器。</summary>
    private sealed class ExportAccumulator
    {
        private int _skipped;

        public ConcurrentBag<string> Files { get; } = new();

        public ConcurrentBag<string> Errors { get; } = new();

        public void AddSkipped()
        {
            Interlocked.Increment(ref this._skipped);
        }

        public void AddError(string message)
        {
            this.Errors.Add(message);
        }

        public WzExportResult ToResult()
        {
            return new WzExportResult(this.Files, this.Errors, Volatile.Read(ref this._skipped));
        }
    }
}
