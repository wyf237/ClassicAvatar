using System.Collections.Concurrent;
using System.Text.Json;
using WzReader.Core.Codecs;
using WzReader.Core.Common;
using WzReader.Core.Diagnostics;
using WzReader.Core.Dom;
using WzReader.Core.Links;
using WzReader.Core.Package;
using WzReader.Export.Imaging;

namespace WzReader.Export;

/// <summary>
/// JSON 导出器：把 img 的完整结构与链接对应关系序列化为 JSON。
/// </summary>
/// <remarks>
/// <para>
/// 单个 img 的输出结构（节选）：
/// <code>
/// {
///   "path": "Character/Cap/01002000.img",
///   "name": "01002000.img",
///   "type": "image",
///   "image": { "sourceFile": "...", "offset": 123, "size": 456, "checksum": 789 },
///   "children": [
///     { "name": "info", "type": "property", "children": [
///         { "name": "icon", "type": "canvas", "width": 27, "height": 30,
///           "format": "ARGB4444", "formatCode": 1, "scale": 1, "pages": 1, "frames": 1 }
///     ] },
///     { "name": "link", "type": "property", "link": { "kind": "_inlink", "target": "...", "exists": true } }
///   ],
///   "links": [ { "from": "...", "kind": "_inlink", "target": "...", "exists": true } ]
/// }
/// </code>
/// </para>
/// <para>
/// 批量导出时每个 img 生成一个 JSON 文件（路径见 <see cref="ImageExportOptions"/> 的命名规则），
/// 并在输出根目录生成 <c>_index.json</c>。链接目标是否需要真实存在由
/// <see cref="JsonExportOptions.ResolveLinkTargets"/> 控制。
/// </para>
/// </remarks>
public sealed class WzJsonExporter
{
    private readonly WzDataRoot _root;
    private readonly JsonExportOptions _options;
    private readonly WzDiagnostics _diagnostics;

    /// <summary>创建 JSON 导出器。</summary>
    /// <param name="root">数据根。</param>
    /// <param name="options">导出选项。</param>
    public WzJsonExporter(WzDataRoot root, JsonExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(options);

        this._root = root;
        this._options = options;
        this._diagnostics = root.Diagnostics;
    }

    /// <summary>批量导出全部 img 的结构 JSON，并生成索引文件。</summary>
    /// <returns>导出结果。</returns>
    public WzExportResult ExportAll()
    {
        List<WzNode> images = this._root.EnumerateImages().ToList();
        ConcurrentBag<string> files = new();
        ConcurrentBag<string> errors = new();
        ConcurrentDictionary<WzNode, string> imageFiles = new();

        ParallelOptions parallelOptions = new()
        {
            MaxDegreeOfParallelism = Math.Max(1, this._options.MaxDegreeOfParallelism),
        };

        Parallel.ForEach(
            images,
            parallelOptions,
            image =>
            {
                string filePath = this.GetImageFilePath(image);
                try
                {
                    this.SaveImage(image, filePath);
                    files.Add(filePath);
                    imageFiles[image] = filePath;
                }
                catch (WzException ex)
                {
                    errors.Add($"{image.Path}：{ex.Message}");
                }
                catch (IOException ex)
                {
                    errors.Add($"{image.Path}：{ex.Message}");
                }
            });

        if (this._options.WriteIndexFile)
        {
            string indexPath = Path.Combine(this._options.OutputDirectory, "_index.json");
            this.SaveIndex(images, imageFiles, indexPath);
            files.Add(indexPath);
        }

        return new WzExportResult(files, errors, 0);
    }

    /// <summary>导出单张 img 到指定文件。</summary>
    /// <param name="image">img 节点。</param>
    /// <param name="filePath">目标 JSON 文件路径。</param>
    /// <returns>写出的文件路径。</returns>
    public string ExportImage(WzNode image, string filePath)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        this.SaveImage(image, filePath);
        return filePath;
    }

    /// <summary>导出单张 img，文件名由默认命名规则生成。</summary>
    /// <param name="image">img 节点。</param>
    /// <returns>写出的文件路径。</returns>
    public string ExportImage(WzNode image)
    {
        return this.ExportImage(image, this.GetImageFilePath(image));
    }

    /// <summary>静态便捷入口：批量导出全部 img 的 JSON。</summary>
    /// <param name="root">数据根。</param>
    /// <param name="options">导出选项。</param>
    /// <returns>导出结果。</returns>
    public static WzExportResult ExportAll(WzDataRoot root, JsonExportOptions options)
    {
        return new WzJsonExporter(root, options).ExportAll();
    }

    /// <summary>按命名规则计算 img 的 JSON 文件绝对路径。</summary>
    private string GetImageFilePath(WzNode image)
    {
        string relative = WzExportPaths.BuildImageRelativePath(image, this._options.MirrorPackageStructure);
        string native = relative.Replace(WzReader.Core.Common.WzPath.Separator, Path.DirectorySeparatorChar);
        return Path.Combine(this._options.OutputDirectory, native + ".json");
    }

    /// <summary>写出单个 img 的 JSON 文件。</summary>
    private void SaveImage(WzNode image, string filePath)
    {
        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using FileStream stream = new(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        using Utf8JsonWriter writer = new(
            stream,
            new JsonWriterOptions { Indented = this._options.WriteIndented });

        List<(string From, WzLinkInfo Link)> links = new();
        writer.WriteStartObject();
        writer.WriteString("path", image.Path);
        writer.WriteString("name", image.Name);
        writer.WriteString("type", "image");

        WzImageInfo? info = image.ImageInfo;
        if (info != null)
        {
            writer.WriteStartObject("image");
            writer.WriteString("sourceFile", info.SourceFile);
            writer.WriteNumber("offset", info.Offset);
            writer.WriteNumber("size", info.Size);
            writer.WriteNumber("checksum", info.Checksum);
            writer.WriteEndObject();
        }

        writer.WritePropertyName("children");
        this.WriteChildren(writer, image, links);

        if (this._options.IncludeLinks)
        {
            writer.WriteStartArray("links");
            foreach ((string from, WzLinkInfo link) in links)
            {
                writer.WriteStartObject();
                writer.WriteString("from", from);
                writer.WriteString("kind", link.Kind);
                writer.WriteString("target", link.TargetPath);
                writer.WriteBoolean("exists", link.Exists);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }

    /// <summary>写出索引文件（所有 img 的全局路径与 JSON 位置）。</summary>
    private void SaveIndex(
        IReadOnlyList<WzNode> images,
        IReadOnlyDictionary<WzNode, string> imageFiles,
        string indexPath)
    {
        string? directory = Path.GetDirectoryName(indexPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using FileStream stream = new(indexPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using Utf8JsonWriter writer = new(
            stream,
            new JsonWriterOptions { Indented = this._options.WriteIndented });

        writer.WriteStartObject();
        writer.WriteString("dataDirectory", this._root.DataDirectory);
        writer.WriteNumber("imageCount", images.Count);
        writer.WriteStartArray("images");
        foreach (WzNode image in images)
        {
            writer.WriteStartObject();
            writer.WriteString("path", image.Path);
            writer.WriteBoolean("exported", imageFiles.ContainsKey(image));
            if (imageFiles.TryGetValue(image, out string? file))
            {
                writer.WriteString("json", file);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    /// <summary>写出一组子节点。</summary>
    private void WriteChildren(
        Utf8JsonWriter writer,
        WzNode parent,
        List<(string From, WzLinkInfo Link)> links)
    {
        writer.WriteStartArray();
        foreach (WzNode child in parent.Children)
        {
            this.WriteNode(writer, child, links);
        }

        writer.WriteEndArray();
    }

    /// <summary>写出单个节点（含递归子节点）。</summary>
    private void WriteNode(
        Utf8JsonWriter writer,
        WzNode node,
        List<(string From, WzLinkInfo Link)> links)
    {
        writer.WriteStartObject();
        writer.WriteString("name", node.Name);

        if (node.IsImage)
        {
            // 嵌套 img 不在本 img 的解析范围内，只记录可定位的引用信息。
            writer.WriteString("type", "image");
            writer.WriteString("path", node.Path);
            writer.WriteEndObject();
            return;
        }

        this.WriteValue(writer, node, links);

        IReadOnlyList<WzNode> children = node.Children;
        if (children.Count > 0)
        {
            writer.WritePropertyName("children");
            this.WriteChildren(writer, node, links);
        }

        writer.WriteEndObject();
    }

    /// <summary>写出节点的类型与值字段。</summary>
    private void WriteValue(
        Utf8JsonWriter writer,
        WzNode node,
        List<(string From, WzLinkInfo Link)> links)
    {
        switch (node.Value)
        {
            case WzProperty:
                writer.WriteString("type", "property");
                break;

            case WzCanvas canvas:
                writer.WriteString("type", "canvas");
                writer.WriteNumber("width", canvas.Width);
                writer.WriteNumber("height", canvas.Height);
                writer.WriteString("format", canvas.Format.GetDisplayName());
                writer.WriteNumber("formatCode", (int)canvas.Format);
                writer.WriteNumber("scale", canvas.Scale);
                writer.WriteNumber("actualScale", canvas.ActualScale);
                writer.WriteNumber("pages", canvas.Pages);
                writer.WriteNumber("frames", canvas.FrameCount);
                break;

            case WzVector vector:
                writer.WriteString("type", "vector");
                writer.WriteNumber("x", vector.X);
                writer.WriteNumber("y", vector.Y);
                break;

            case WzUol uol:
                writer.WriteString("type", "uol");
                writer.WriteString("value", uol.Path);
                break;

            case WzConvex convex:
                writer.WriteString("type", "convex2d");
                writer.WriteStartArray("points");
                foreach (WzVector point in convex.Points)
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("x", point.X);
                    writer.WriteNumber("y", point.Y);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                break;

            case WzSound sound:
                writer.WriteString("type", "sound");
                this.WriteSound(writer, sound);
                break;

            case WzRawData rawData:
                writer.WriteString("type", "rawdata");
                writer.WriteNumber("version", rawData.Version);
                writer.WriteBoolean("isVideo", rawData.IsVideo);
                writer.WriteNumber("length", rawData.Data.Length);
                break;

            case string text:
                writer.WriteString("type", "string");
                writer.WriteString("value", text);
                break;

            case int intValue:
                writer.WriteString("type", "int");
                writer.WriteNumber("value", intValue);
                break;

            case short shortValue:
                writer.WriteString("type", "int");
                writer.WriteNumber("value", shortValue);
                break;

            case byte byteValue:
                writer.WriteString("type", "int");
                writer.WriteNumber("value", byteValue);
                break;

            case long longValue:
                writer.WriteString("type", "long");
                writer.WriteNumber("value", longValue);
                break;

            case float floatValue:
                writer.WriteString("type", "float");
                writer.WriteNumber("value", floatValue);
                break;

            case double doubleValue:
                writer.WriteString("type", "float");
                writer.WriteNumber("value", doubleValue);
                break;

            case bool boolValue:
                writer.WriteString("type", "bool");
                writer.WriteBoolean("value", boolValue);
                break;

            case null:
                writer.WriteString("type", "null");
                break;

            default:
                writer.WriteString("type", "unknown");
                writer.WriteString("value", node.Value.ToString());
                break;
        }

        this.TryWriteLink(writer, node, links);
    }

    /// <summary>写出声音节点细节。</summary>
    private void WriteSound(Utf8JsonWriter writer, WzSound sound)
    {
        writer.WriteStartObject("sound");
        writer.WriteString("kind", sound.Kind.ToString());
        writer.WriteNumber("durationMs", sound.DurationMs);
        writer.WriteNumber("dataLength", sound.Data.Length);
        if (sound.Format is { } format)
        {
            writer.WriteStartObject("format");
            writer.WriteNumber("formatTag", format.FormatTag);
            writer.WriteNumber("channels", format.Channels);
            writer.WriteNumber("samplesPerSec", format.SamplesPerSec);
            writer.WriteNumber("avgBytesPerSec", format.AvgBytesPerSec);
            writer.WriteNumber("blockAlign", format.BlockAlign);
            writer.WriteNumber("bitsPerSample", format.BitsPerSample);
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }

    /// <summary>若节点是链接，写出内联链接字段并记录到汇总列表。</summary>
    private void TryWriteLink(
        Utf8JsonWriter writer,
        WzNode node,
        List<(string From, WzLinkInfo Link)> links)
    {
        if (!this._options.IncludeLinks || !MightBeLink(node))
        {
            return;
        }

        WzLinkInfo? link = this._options.ResolveLinkTargets
            ? WzLinkResolver.GetLink(node, path => this._root.FindNode(path))
            : WzLinkResolver.GetLink(node);

        if (link == null)
        {
            return;
        }

        writer.WriteStartObject("link");
        writer.WriteString("kind", link.Kind);
        writer.WriteString("target", link.TargetPath);
        writer.WriteBoolean("exists", link.Exists);
        writer.WriteEndObject();

        links.Add((node.Path, link));
    }

    /// <summary>低成本判断节点是否可能是链接（避免对每个节点做多次子节点查找）。</summary>
    private static bool MightBeLink(WzNode node)
    {
        if (node.Value is WzUol)
        {
            return true;
        }

        foreach (WzNode child in node.Children)
        {
            if (child.Name is "source" or "_inlink" or "_outlink")
            {
                return true;
            }
        }

        return false;
    }
}
