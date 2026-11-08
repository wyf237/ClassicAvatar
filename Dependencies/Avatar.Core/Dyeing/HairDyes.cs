using Avatar.Core.Models;

namespace Avatar.Core.Dyeing;

/// <summary>
/// 常用染色预设。数值来自 MapleStory Classic World 常见发色，可按需直接使用或作为自定义染色的起点。
/// </summary>
/// <remarks>
/// 预设只提供“整体换色”的默认观感；需要精确还原官方染色券时，应使用
/// <see cref="DyeSpec.PaletteMap(IReadOnlyList{ColorPair}, byte, double)"/> 的调色板映射（金属件、发饰不会被误染）。
/// </remarks>
public static class HairDyes
{
    /// <summary>乌黑（乘法压色，保留高光）。</summary>
    public static DyeSpec Black { get; } = DyeSpec.MultiplyBy(new RgbColor(40, 38, 40));

    /// <summary>深棕。</summary>
    public static DyeSpec Brown { get; } = DyeSpec.Hue(new RgbColor(112, 68, 38));

    /// <summary>亚麻金。</summary>
    public static DyeSpec Blonde { get; } = DyeSpec.Hue(new RgbColor(226, 192, 110));

    /// <summary>红。</summary>
    public static DyeSpec Red { get; } = DyeSpec.Hue(new RgbColor(196, 52, 44));

    /// <summary>蓝。</summary>
    public static DyeSpec Blue { get; } = DyeSpec.Hue(new RgbColor(58, 106, 196));

    /// <summary>绿。</summary>
    public static DyeSpec Green { get; } = DyeSpec.Hue(new RgbColor(56, 156, 88));

    /// <summary>紫。</summary>
    public static DyeSpec Purple { get; } = DyeSpec.Hue(new RgbColor(138, 80, 188));

    /// <summary>银白（保留亮度的去饱和处理）。</summary>
    public static DyeSpec White { get; } = DyeSpec.Hue(new RgbColor(238, 238, 238));

    /// <summary>浅棕（近视发色）。</summary>
    public static DyeSpec LightBrown { get; } = DyeSpec.Hue(new RgbColor(176, 124, 74));

    /// <summary>粉。</summary>
    public static DyeSpec Pink { get; } = DyeSpec.Hue(new RgbColor(228, 128, 168));
}
