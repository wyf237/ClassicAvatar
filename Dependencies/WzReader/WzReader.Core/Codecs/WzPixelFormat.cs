namespace WzReader.Core.Codecs;

/// <summary>
/// WZ 画布（Canvas）在文件中记录的像素格式编号。
/// </summary>
/// <remarks>
/// 数值与 WZ 文件中的 <c>format</c> 字段一一对应，<b>不可随意改动</b>。
/// 不同版本/不同来源的客户端可能只使用其中一部分；读取到未知编号时应记录告警并跳过该画布，
/// 而不是让整个 img 解析失败。
/// </remarks>
public enum WzPixelFormat
{
    /// <summary>未知或未支持。</summary>
    Unknown = 0,

    /// <summary>16 位 ARGB4444。</summary>
    Argb4444 = 1,

    /// <summary>32 位 ARGB8888（最常见）。</summary>
    Argb8888 = 2,

    /// <summary>24 位 RGB888。</summary>
    Rgb888 = 3,

    /// <summary>16 位 ARGB1555。</summary>
    Argb1555 = 257,

    /// <summary>16 位 RGB565。</summary>
    Rgb565 = 513,

    /// <summary>24 位 RGB888（部分版本使用该编号）。</summary>
    Rgb888Variant = 517,

    /// <summary>16 位单通道灰度。</summary>
    R16 = 769,

    /// <summary>DXT3（BC2）压缩纹理。</summary>
    Dxt3 = 1026,

    /// <summary>DXT5（BC3）压缩纹理。</summary>
    Dxt5 = 2050,

    /// <summary>8 位单通道（Alpha）。</summary>
    A8 = 2304,

    /// <summary>32 位 RGBA1010102。</summary>
    Rgba1010102 = 2562,

    /// <summary>DXT1（BC1）压缩纹理。</summary>
    Dxt1 = 4097,

    /// <summary>BC7 压缩纹理（新版本客户端使用）。</summary>
    Bc7 = 4098,

    /// <summary>128 位 RGBA32Float。</summary>
    Rgba32Float = 4100,
}

/// <summary>
/// 像素格式的判定与描述辅助方法。
/// </summary>
public static class WzPixelFormatExtensions
{
    /// <summary>判断是否为压缩纹理（DXT/BC 系列）。</summary>
    /// <param name="format">像素格式。</param>
    /// <returns>压缩纹理返回 <c>true</c>。</returns>
    public static bool IsBlockCompressed(this WzPixelFormat format)
    {
        return format is WzPixelFormat.Dxt1 or WzPixelFormat.Dxt3 or WzPixelFormat.Dxt5 or WzPixelFormat.Bc7;
    }

    /// <summary>判断是否为 4bpp 压缩格式（DXT1/BC1）。</summary>
    /// <param name="format">像素格式。</param>
    /// <returns>DXT1 返回 <c>true</c>。</returns>
    public static bool IsFourBitBlockCompressed(this WzPixelFormat format)
    {
        return format == WzPixelFormat.Dxt1;
    }

    /// <summary>获取格式的可读名称（用于日志与 JSON 导出）。</summary>
    /// <param name="format">像素格式。</param>
    /// <returns>格式名称；未知时返回 <c>Unknown(编号)</c>。</returns>
    public static string GetDisplayName(this WzPixelFormat format)
    {
        return Enum.IsDefined(format)
            ? format.ToString()
            : $"Unknown({(int)format})";
    }
}
