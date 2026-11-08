using System.IO.Compression;

namespace Avatar.Core.Export;

/// <summary>
/// .avatar 套装文件打包选项（均有默认值）。
/// </summary>
public sealed class AvatarPackOptions
{
    /// <summary>是否包含形象预览图（stand1 第 0 帧）。</summary>
    public bool IncludePreview { get; init; } = true;

    /// <summary>预览图最近邻放大倍数（1 表示原始像素）。</summary>
    public int PreviewScale { get; init; } = 2;

    /// <summary>预览图使用的动作名。</summary>
    public string PreviewAction { get; init; } = "stand1";

    /// <summary>
    /// 附带雪碧图的动作集合；<c>null</c> 表示默认站姿/行走，空数组表示不打雪碧图。
    /// </summary>
    public IReadOnlyList<string>? SheetActions { get; init; }

    /// <summary>雪碧图每行帧数（0 表示单行）。</summary>
    public int SheetColumns { get; init; } = 8;

    /// <summary>是否包含 96×96 缩略图。</summary>
    public bool IncludeThumbnail { get; init; } = true;

    /// <summary>ZIP 压缩级别。</summary>
    public CompressionLevel Compression { get; init; } = CompressionLevel.Optimal;

    /// <summary>生成程序名（写入 manifest）。</summary>
    public string? App { get; init; } = "Avatar.Core";

    /// <summary>生成程序版本（写入 manifest）。</summary>
    public string? AppVersion { get; init; }
}
