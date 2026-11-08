using WzReader.Core.Common;
using WzReader.Core.Dom;

namespace WzReader.Export.Imaging;

/// <summary>
/// 导出路径规则：把 WZ 节点路径映射为“基于 img ID”的文件相对路径。
/// </summary>
/// <remarks>
/// 规则详见 <see cref="ImageExportOptions"/> 的注释。本类只负责生成相对路径，
/// 不负责创建目录，也不依赖当前工作目录，便于并行导出与单元测试。
/// </remarks>
internal static class WzExportPaths
{
    /// <summary>
    /// 为节点生成导出用的相对路径（不含扩展名），例如 <c>Character/Cap/01002000/stand1.0.cap</c>。
    /// </summary>
    /// <param name="node">画布节点（或指向画布的链接节点）。</param>
    /// <param name="includePackageDirectory">是否包含包路径前缀。</param>
    /// <returns>使用 <c>/</c> 分隔的相对路径。</returns>
    public static string BuildCanvasRelativePath(WzNode node, bool includePackageDirectory)
    {
        ArgumentNullException.ThrowIfNull(node);

        List<WzNode> chain = new();
        WzNode? image = null;
        for (WzNode? current = node; current != null; current = current.Parent)
        {
            chain.Add(current);
            if (current.IsImage)
            {
                image = current;
                break;
            }
        }

        string imageId;
        string packageDirectory;
        List<string> propertySegments = new();

        if (image != null)
        {
            chain.Reverse();
            imageId = StripImageExtension(image.Name);
            packageDirectory = image.Parent?.Path ?? string.Empty;

            // chain[0] 是 img，其后依次是属性层级。
            for (int i = 1; i < chain.Count; i++)
            {
                propertySegments.Add(chain[i].Name);
            }
        }
        else
        {
            // 兜底：节点不在任何 img 内（非常规数据），用完整路径拆出目录与名字。
            imageId = StripImageExtension(WzPath.GetName(node.Path));
            packageDirectory = WzPath.GetParent(node.Path);
        }

        string propertyPath = propertySegments.Count == 0
            ? "root"
            : string.Join(".", propertySegments.Select(SanitizeSegment));

        string fileName = SanitizeSegment(imageId) + "/" + propertyPath;
        if (!includePackageDirectory || packageDirectory.Length == 0)
        {
            return fileName;
        }

        return string.Join("/", packageDirectory.Split(WzPath.Separator).Select(SanitizeSegment)) + "/" + fileName;
    }

    /// <summary>
    /// 为 img 生成 JSON 相对路径（不含扩展名），例如 <c>Character/Cap/01002000</c>。
    /// </summary>
    /// <param name="image">img 节点。</param>
    /// <param name="includePackageDirectory">是否包含包路径前缀。</param>
    /// <returns>使用 <c>/</c> 分隔的相对路径。</returns>
    public static string BuildImageRelativePath(WzNode image, bool includePackageDirectory)
    {
        ArgumentNullException.ThrowIfNull(image);

        string imageId = SanitizeSegment(StripImageExtension(image.Name));
        string packageDirectory = image.Parent?.Path ?? string.Empty;
        if (!includePackageDirectory || packageDirectory.Length == 0)
        {
            return imageId;
        }

        return string.Join("/", packageDirectory.Split(WzPath.Separator).Select(SanitizeSegment)) + "/" + imageId;
    }

    /// <summary>去掉 <c>.img</c> 扩展名（不区分大小写）。</summary>
    public static string StripImageExtension(string name)
    {
        return name.EndsWith(".img", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    /// <summary>把路径段转换为合法的 Windows 文件名片段。</summary>
    public static string SanitizeSegment(string segment)
    {
        if (string.IsNullOrEmpty(segment))
        {
            return "_";
        }

        char[] invalid = Path.GetInvalidFileNameChars();
        char[] buffer = segment.ToCharArray();
        for (int i = 0; i < buffer.Length; i++)
        {
            if (Array.IndexOf(invalid, buffer[i]) >= 0)
            {
                buffer[i] = '_';
            }
        }

        return new string(buffer);
    }
}
