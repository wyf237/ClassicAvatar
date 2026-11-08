using Avatar.Core.Models;

namespace Avatar.Core.Dyeing;

/// <summary>
/// 颜色转换与插值工具（纯函数，无状态）。
/// </summary>
/// <remarks>
/// 染色需要在 HSL 空间进行：直接对 RGB 做线性混合会把亮部/暗部一起拉平，导致头发失去层次。
/// HSL 中固定亮度（L）、替换色相（H）与饱和度（S），可以保留素材本身的明暗关系。
/// </remarks>
public static class ColorMath
{
    /// <summary>
    /// RGB → HSL。
    /// </summary>
    /// <param name="r">红 0..255。</param>
    /// <param name="g">绿 0..255。</param>
    /// <param name="b">蓝 0..255。</param>
    /// <returns>H 为 0..360，S/L 为 0..1。</returns>
    public static (double H, double S, double L) RgbToHsl(byte r, byte g, byte b)
    {
        double red = r / 255.0;
        double green = g / 255.0;
        double blue = b / 255.0;

        double max = Math.Max(red, Math.Max(green, blue));
        double min = Math.Min(red, Math.Min(green, blue));
        double lightness = (max + min) / 2.0;
        double delta = max - min;

        if (delta <= double.Epsilon)
        {
            // 灰色：色相未定义，用 0 表示。
            return (0, 0, lightness);
        }

        double saturation = lightness > 0.5
            ? delta / (2.0 - max - min)
            : delta / (max + min);

        double hue;
        if (max == red)
        {
            hue = ((green - blue) / delta) + (green < blue ? 6 : 0);
        }
        else if (max == green)
        {
            hue = ((blue - red) / delta) + 2;
        }
        else
        {
            hue = ((red - green) / delta) + 4;
        }

        return (hue * 60.0, saturation, lightness);
    }

    /// <summary>
    /// HSL → RGB。
    /// </summary>
    /// <param name="h">色相 0..360。</param>
    /// <param name="s">饱和度 0..1。</param>
    /// <param name="l">亮度 0..1。</param>
    /// <returns>RGB（各分量 0..255）。</returns>
    public static RgbColor HslToRgb(double h, double s, double l)
    {
        h = NormalizeHue(h);
        s = Math.Clamp(s, 0, 1);
        l = Math.Clamp(l, 0, 1);

        if (s <= double.Epsilon)
        {
            byte gray = ToByte(l * 255.0);
            return new RgbColor(gray, gray, gray);
        }

        double chroma = (1 - Math.Abs((2 * l) - 1)) * s;
        double segment = h / 60.0;
        double secondary = chroma * (1 - Math.Abs((segment % 2) - 1));
        double match = l - (chroma / 2);

        (double r1, double g1, double b1) = segment switch
        {
            < 1 => (chroma, secondary, 0.0),
            < 2 => (secondary, chroma, 0.0),
            < 3 => (0.0, chroma, secondary),
            < 4 => (0.0, secondary, chroma),
            < 5 => (secondary, 0.0, chroma),
            _ => (chroma, 0.0, secondary),
        };

        return new RgbColor(
            ToByte((r1 + match) * 255.0),
            ToByte((g1 + match) * 255.0),
            ToByte((b1 + match) * 255.0));
    }

    /// <summary>
    /// 感知亮度（Rec.601），用于 <see cref="DyeMode.GrayscaleHue"/>。
    /// </summary>
    /// <param name="r">红。</param>
    /// <param name="g">绿。</param>
    /// <param name="b">蓝。</param>
    /// <returns>0..1。</returns>
    public static double Luminance(byte r, byte g, byte b) =>
        ((0.299 * r) + (0.587 * g) + (0.114 * b)) / 255.0;

    /// <summary>
    /// 按比例混合两个字节分量（<paramref name="t"/> 为 0 返回 <paramref name="from"/>，为 1 返回 <paramref name="to"/>）。
    /// </summary>
    /// <param name="from">起始值。</param>
    /// <param name="to">目标值。</param>
    /// <param name="t">混合比例 0..1。</param>
    /// <returns>混合结果。</returns>
    public static byte Lerp(byte from, byte to, double t)
    {
        double value = from + ((to - from) * Math.Clamp(t, 0, 1));
        return ToByte(value);
    }

    /// <summary>
    /// 把浮点颜色分量转换为字节（四舍五入并夹取）。
    /// </summary>
    /// <param name="value">0..255 的浮点值。</param>
    /// <returns>字节值。</returns>
    public static byte ToByte(double value) =>
        (byte)Math.Clamp((int)Math.Round(value), 0, 255);

    /// <summary>
    /// 把色相归一化到 0..360。
    /// </summary>
    /// <param name="hue">任意角度。</param>
    /// <returns>0..360。</returns>
    public static double NormalizeHue(double hue)
    {
        double value = hue % 360.0;
        return value < 0 ? value + 360.0 : value;
    }
}
