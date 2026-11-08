using Avatar.Core.Models;
using Avatar.Core.Rendering;
using WzReader.Core.Codecs;
using WzReader.Export.Imaging;

namespace Avatar.Core.Export;

/// <summary>
/// 一次动作导出结果（只记录“写了哪些文件”，不保留像素，便于批量导出时低内存）。
/// </summary>
/// <param name="RequestedAction">请求动作名。</param>
/// <param name="Action">实际动作名。</param>
/// <param name="FrameFiles">逐帧 PNG 路径。</param>
/// <param name="SheetFile">雪碧图路径（未导出为 <c>null</c>）。</param>
/// <param name="MetaFile">结构 JSON 路径（未导出为 <c>null</c>）。</param>
/// <param name="Warnings">渲染/导出提示（缺失动作、跳过覆盖等）。</param>
public sealed record AvatarExportResult(
    string RequestedAction,
    string Action,
    IReadOnlyList<string> FrameFiles,
    string? SheetFile,
    string? MetaFile,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Avatar 导出器：把渲染结果写成 PNG 单帧、雪碧图与结构 JSON。
/// </summary>
/// <remarks>
/// <para>
/// 命名规则见 <see cref="AvatarExportOptions"/>。所有文件都写入同一个输出目录，
/// 通过“角色 ID 前缀 + 动作名”保证唯一，批量导出多个角色/动作时可直接平铺。
/// </para>
/// <para>
/// 线程安全：只读 <see cref="AvatarBuild"/>；<see cref="ExportActions"/> 使用并行循环，
/// 依赖 <see cref="Assets.AvatarData"/> 内部缓存（ConcurrentDictionary）与 <c>WzCanvas.Frames</c> 解码缓存。
/// </para>
/// </remarks>
public static class AvatarExporter
{
    /// <summary>
    /// 导出一个动作（逐帧/雪碧图/JSON 由选项决定）。
    /// </summary>
    /// <param name="build">组装结果。</param>
    /// <param name="action">动作名（支持别名）；<c>null</c> 使用默认动作。</param>
    /// <param name="options">导出选项。</param>
    /// <returns>导出结果。</returns>
    public static AvatarExportResult ExportAction(
        AvatarBuild build,
        string? action,
        AvatarExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.OutputDirectory);

        Directory.CreateDirectory(options.OutputDirectory);

        string prefix = Sanitize(options.FileNamePrefix ?? build.Profile.Id ?? "avatar");
        AvatarSequence sequence = AvatarRenderer.RenderSequence(build, action);
        string actionKey = Sanitize(sequence.Action);

        List<string> warnings = new();
        List<string> frameFiles = new();

        if (options.Layout is AvatarExportLayout.Frames or AvatarExportLayout.Both)
        {
            foreach (AvatarFrame frame in sequence.Frames)
            {
                string path = Path.Combine(
                    options.OutputDirectory,
                    $"{prefix}_{actionKey}_{frame.FrameIndex:D2}.png");

                if (WritePng(frame.Pixels, path, options, warnings))
                {
                    frameFiles.Add(path);
                }
            }
        }

        string? sheetFile = null;
        if (options.Layout is AvatarExportLayout.Sheet or AvatarExportLayout.Both)
        {
            WzPixelBuffer sheet = SheetBuilder.Build(
                sequence.Frames,
                options.SheetColumns,
                options.SheetSpacing);

            string path = Path.Combine(options.OutputDirectory, $"{prefix}_{actionKey}_sheet.png");
            if (WritePng(sheet, path, options, warnings))
            {
                sheetFile = path;
            }
        }

        string? metaFile = null;
        if (options.WriteMeta)
        {
            string path = Path.Combine(options.OutputDirectory, $"{prefix}_{actionKey}.json");
            if (AllowWrite(path, options, warnings))
            {
                AvatarMetaDocument.Create(build, sequence).Save(path);
                metaFile = path;
            }
        }

        if (options.WriteProfile)
        {
            string path = Path.Combine(options.OutputDirectory, $"{prefix}_profile.json");
            if (AllowWrite(path, options, warnings))
            {
                CharacterProfileJson.Save(build.Profile, path);
            }
        }

        foreach (AvatarFrame frame in sequence.Frames)
        {
            foreach (string note in frame.Notes)
            {
                if (!warnings.Contains(note, StringComparer.Ordinal))
                {
                    warnings.Add(note);
                }
            }
        }

        return new AvatarExportResult(
            sequence.RequestedAction,
            sequence.Action,
            frameFiles,
            sheetFile,
            metaFile,
            warnings);
    }

    /// <summary>
    /// 批量导出多个动作（并行）。
    /// </summary>
    /// <param name="build">组装结果。</param>
    /// <param name="actions">动作名集合；为 <c>null</c> 时导出身体自带的全部动作。</param>
    /// <param name="options">导出选项。</param>
    /// <returns>每个动作的导出结果（按输入顺序）。</returns>
    public static IReadOnlyList<AvatarExportResult> ExportActions(
        AvatarBuild build,
        IEnumerable<string>? actions,
        AvatarExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(options);

        IEnumerable<string> source = actions ?? build.AvailableActions;
        string[] list = source
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (list.Length == 0)
        {
            return Array.Empty<AvatarExportResult>();
        }

        int parallelism = Math.Clamp(options.MaxParallelism, 1, 32);
        AvatarExportResult[] results = new AvatarExportResult[list.Length];

        Parallel.For(
            0,
            list.Length,
            new ParallelOptions { MaxDegreeOfParallelism = parallelism },
            index => results[index] = ExportAction(build, list[index], options));

        return results;
    }

    /// <summary>
    /// 把一帧写到指定路径（供自定义命名/嵌入其他流程）。
    /// </summary>
    /// <param name="build">组装结果。</param>
    /// <param name="action">动作名。</param>
    /// <param name="frameIndex">帧下标。</param>
    /// <param name="filePath">目标 PNG 路径。</param>
    /// <param name="scale">最近邻放大倍数。</param>
    /// <returns>实际写入的路径。</returns>
    public static string ExportFrame(
        AvatarBuild build,
        string? action,
        int frameIndex,
        string filePath,
        int scale = 1)
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        AvatarFrame frame = AvatarRenderer.RenderFrame(build, action, frameIndex);
        WzPixelBuffer pixels = scale <= 1 ? frame.Pixels : PixelCompositor.Scale(frame.Pixels, scale);
        PngEncoder.Save(pixels, filePath);
        return filePath;
    }

    /// <summary>写 PNG（遵守覆盖策略）。</summary>
    private static bool WritePng(
        WzPixelBuffer pixels,
        string path,
        AvatarExportOptions options,
        List<string> warnings)
    {
        if (!AllowWrite(path, options, warnings))
        {
            return false;
        }

        WzPixelBuffer output = options.Scale <= 1
            ? pixels
            : PixelCompositor.Scale(pixels, options.Scale);

        PngEncoder.Save(output, path);
        return true;
    }

    /// <summary>按覆盖策略判断是否允许写入。</summary>
    private static bool AllowWrite(string path, AvatarExportOptions options, List<string> warnings)
    {
        if (!File.Exists(path))
        {
            return true;
        }

        switch (options.Overwrite)
        {
            case FileOverwriteMode.Skip:
                warnings.Add($"已存在，跳过：{path}");
                return false;

            case FileOverwriteMode.Fail:
                throw new IOException($"文件已存在（Overwrite=Fail）：{path}");

            default:
                return true;
        }
    }

    /// <summary>把动作名/角色 ID 清洗为安全文件名。</summary>
    private static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "avatar";
        }

        char[] invalid = Path.GetInvalidFileNameChars();
        char[] buffer = value.Trim().ToCharArray();
        for (int index = 0; index < buffer.Length; index++)
        {
            if (Array.IndexOf(invalid, buffer[index]) >= 0 || buffer[index] == ' ')
            {
                buffer[index] = '_';
            }
        }

        return new string(buffer);
    }
}
