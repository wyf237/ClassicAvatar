using System.Text;

namespace WzReader.Core.Common;

/// <summary>
/// WZ 节点路径工具：负责规范化、拼接、拆分与相对路径解析。
/// </summary>
/// <remarks>
/// WZ 中的路径使用 <c>/</c> 分隔，形如 <c>Character/Cap/01002000.img/stand1/0/cap</c>。
/// 所有对外暴露的路径都应先经过本类规范化，避免大小写、重复分隔符、尾部斜杠导致的查找失败。
/// 本类为纯函数工具，无状态、线程安全。
/// </remarks>
public static class WzPath
{
    /// <summary>路径分隔符。</summary>
    public const char Separator = '/';

    /// <summary>判断 <paramref name="segment"/> 是否为“向上一级”标记。</summary>
    /// <param name="segment">路径段。</param>
    /// <returns>是 <c>..</c> 返回 <c>true</c>。</returns>
    public static bool IsParentSegment(string segment)
    {
        return segment == "..";
    }

    /// <summary>
    /// 规范化路径：统一分隔符、去除首尾分隔符、压缩连续的 <c>..</c> 与空段。
    /// </summary>
    /// <param name="path">原始路径，允许为 <c>null</c> 或空。</param>
    /// <returns>规范化后的路径；空路径返回空字符串。</returns>
    /// <example>
    /// <c>"/Map//Map/Map0/"</c> → <c>"Map/Map/Map0"</c>；
    /// <c>"a/../b"</c> → <c>"b"</c>。
    /// </example>
    public static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        string[] rawSegments = path.Replace('\\', Separator).Split(Separator, StringSplitOptions.RemoveEmptyEntries);
        if (rawSegments.Length == 0)
        {
            return string.Empty;
        }

        List<string> segments = new(rawSegments.Length);
        foreach (string segment in rawSegments)
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count > 0)
                {
                    segments.RemoveAt(segments.Count - 1);
                }

                continue;
            }

            segments.Add(segment);
        }

        return string.Join(Separator, segments);
    }

    /// <summary>
    /// 以 <paramref name="basePath"/> 为基准解析相对路径。
    /// </summary>
    /// <param name="basePath">基准路径（通常是上级节点或 img 根的路径）。</param>
    /// <param name="relativePath">相对路径，允许包含 <c>..</c>。</param>
    /// <returns>规范化后的绝对路径。</returns>
    /// <remarks>
    /// 这是实现 UOL（<c>..</c> 相对）与 <c>_inlink</c>（相对 img 根）解析的基础方法。
    /// 注意：必须以“基准段 + 相对段”的顺序逐段折叠，<b>不能先规范化相对路径</b>，
    /// 否则开头的 <c>..</c> 会因为栈为空而被丢弃。
    /// </remarks>
    public static string ResolveRelative(string basePath, string relativePath)
    {
        string trimmed = (relativePath ?? string.Empty).Replace('\\', Separator).Trim();

        // 以分隔符开头视为绝对路径。
        if (trimmed.StartsWith(Separator))
        {
            return Normalize(trimmed);
        }

        string[] relativeSegments = trimmed.Split(Separator, StringSplitOptions.RemoveEmptyEntries);
        if (relativeSegments.Length == 0)
        {
            return Normalize(basePath);
        }

        List<string> segments = new(Split(basePath));
        foreach (string segment in relativeSegments)
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                // 越过根部时直接忽略，避免产生非法路径。
                if (segments.Count > 0)
                {
                    segments.RemoveAt(segments.Count - 1);
                }

                continue;
            }

            segments.Add(segment);
        }

        return string.Join(Separator, segments);
    }

    /// <summary>获取父路径；根路径返回空字符串。</summary>
    /// <param name="path">路径。</param>
    /// <returns>父路径。</returns>
    public static string GetParent(string path)
    {
        string normalized = Normalize(path);
        int index = normalized.LastIndexOf(Separator);
        return index <= 0 ? string.Empty : normalized[..index];
    }

    /// <summary>获取最后一段名称。</summary>
    /// <param name="path">路径。</param>
    /// <returns>最后一段；空路径返回空字符串。</returns>
    public static string GetName(string path)
    {
        string normalized = Normalize(path);
        int index = normalized.LastIndexOf(Separator);
        return index < 0 ? normalized : normalized[(index + 1)..];
    }

    /// <summary>按 <c>/</c> 拆分路径段。</summary>
    /// <param name="path">路径。</param>
    /// <returns>路径段数组；空路径返回空数组。</returns>
    public static string[] Split(string path)
    {
        string normalized = Normalize(path);
        return normalized.Length == 0
            ? Array.Empty<string>()
            : normalized.Split(Separator);
    }

    /// <summary>把多个路径段拼接并规范化。</summary>
    /// <param name="segments">路径段。</param>
    /// <returns>拼接后的路径。</returns>
    public static string Combine(params string?[] segments)
    {
        StringBuilder builder = new();
        foreach (string? segment in segments)
        {
            if (string.IsNullOrWhiteSpace(segment))
            {
                continue;
            }

            string normalized = Normalize(segment);
            if (normalized.Length == 0)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(Separator);
            }

            builder.Append(normalized);
        }

        return Normalize(builder.ToString());
    }

    /// <summary>
    /// 判断路径是否以指定前缀开头（不区分大小写，按段比较，避免 <c>Map/Map0</c> 误匹配 <c>Map/Map01</c>）。
    /// </summary>
    /// <param name="path">完整路径。</param>
    /// <param name="prefix">前缀路径。</param>
    /// <returns>匹配返回 <c>true</c>。</returns>
    public static bool StartsWithSegment(string path, string prefix)
    {
        string[] pathSegments = Split(path);
        string[] prefixSegments = Split(prefix);
        if (prefixSegments.Length > pathSegments.Length)
        {
            return false;
        }

        for (int i = 0; i < prefixSegments.Length; i++)
        {
            if (!string.Equals(pathSegments[i], prefixSegments[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}
