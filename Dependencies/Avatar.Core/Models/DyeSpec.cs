namespace Avatar.Core.Models;

/// <summary>RGB 颜色（0..255），不包含 Alpha（染色不改变透明度）。</summary>
/// <param name="R">红。</param>
/// <param name="G">绿。</param>
/// <param name="B">蓝。</param>
public readonly record struct RgbColor(byte R, byte G, byte B)
{
    /// <summary>由 24 位整数 <c>0xRRGGBB</c> 创建颜色。</summary>
    /// <param name="rgb">颜色值。</param>
    /// <returns>颜色。</returns>
    public static RgbColor FromRgb(int rgb) =>
        new((byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));

    /// <inheritdoc />
    public override string ToString() => $"#{this.R:X2}{this.G:X2}{this.B:X2}";
}

/// <summary>
/// 调色板映射中的一对颜色：把接近 <paramref name="Source"/> 的像素替换为 <paramref name="Target"/>。
/// </summary>
/// <param name="Source">源颜色（素材中的颜色）。</param>
/// <param name="Target">目标颜色（染色后的颜色）。</param>
public readonly record struct ColorPair(RgbColor Source, RgbColor Target);

/// <summary>
/// 染色参数。所有模式都不会改变 Alpha，以保持边缘抗锯齿。
/// </summary>
/// <param name="Mode">染色算法。</param>
/// <param name="R">目标红色分量。</param>
/// <param name="G">目标绿色分量。</param>
/// <param name="B">目标蓝色分量。</param>
/// <param name="Strength">混合强度 0..1（1 表示完全采用染色结果）。</param>
/// <param name="Tolerance"><see cref="DyeMode.PaletteMap"/> 的 RGB 距离容差（欧氏距离）。</param>
/// <param name="MinLightness"><see cref="DyeMode.HueReplace"/> 的暗部抬升比例，避免纯黑不上色。</param>
/// <param name="Palette"><see cref="DyeMode.PaletteMap"/> 使用的颜色映射表。</param>
public sealed record DyeSpec(
    DyeMode Mode,
    byte R,
    byte G,
    byte B,
    double Strength = 1.0,
    byte Tolerance = 12,
    double MinLightness = 0.06,
    IReadOnlyList<ColorPair>? Palette = null)
{
    /// <summary>目标颜色。</summary>
    public RgbColor Target => new(this.R, this.G, this.B);

    /// <summary>快捷构造：色相替换。</summary>
    /// <param name="color">目标颜色。</param>
    /// <param name="strength">混合强度。</param>
    /// <returns>染色参数。</returns>
    public static DyeSpec Hue(RgbColor color, double strength = 1.0) =>
        new(DyeMode.HueReplace, color.R, color.G, color.B, strength);

    /// <summary>快捷构造：乘法压色。</summary>
    /// <param name="color">乘法颜色。</param>
    /// <param name="strength">混合强度。</param>
    /// <returns>染色参数。</returns>
    public static DyeSpec MultiplyBy(RgbColor color, double strength = 1.0) =>
        new(DyeMode.Multiply, color.R, color.G, color.B, strength);

    /// <summary>快捷构造：调色板映射。</summary>
    /// <param name="palette">颜色映射表。</param>
    /// <param name="tolerance">容差。</param>
    /// <param name="strength">混合强度。</param>
    /// <returns>染色参数。</returns>
    public static DyeSpec PaletteMap(IReadOnlyList<ColorPair> palette, byte tolerance = 12, double strength = 1.0) =>
        new(DyeMode.PaletteMap, 0, 0, 0, strength, tolerance, 0.06, palette);

    /// <summary>
    /// 生成稳定的缓存键（用于染色结果缓存）。List 字段逐个展开，避免引用相等导致的缓存失效。
    /// </summary>
    /// <returns>可比较的字符串键。</returns>
    public string ToCacheKey()
    {
        string palette = this.Palette == null
            ? "-"
            : string.Join(";", this.Palette.Select(static p =>
                $"{p.Source.R:X2}{p.Source.G:X2}{p.Source.B:X2}>{p.Target.R:X2}{p.Target.G:X2}{p.Target.B:X2}"));

        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{this.Mode}|{this.R}|{this.G}|{this.B}|{this.Strength:F3}|{this.Tolerance}|{this.MinLightness:F3}|{palette}");
    }
}
