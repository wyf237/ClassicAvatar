using Avatar.Core.Models;
using WzReader.Core.Codecs;

namespace Avatar.Core.Dyeing;

/// <summary>
/// 染色引擎：把 <see cref="DyeSpec"/> 应用到一张 BGRA32 像素缓冲区，返回新的缓冲区。
/// </summary>
/// <remarks>
/// 统一约定：
/// <list type="bullet">
/// <item><b>只改颜色，不改 Alpha</b>——保持边缘抗锯齿与半透明过渡；</item>
/// <item>透明像素（A=0）原样复制，不做无用计算；</item>
/// <item>输出新对象，绝不修改输入（输入通常是 <see cref="WzCanvas"/> 的共享缓存帧）。</item>
/// </list>
/// </remarks>
public static class DyeEngine
{
    /// <summary>
    /// 对整张像素缓冲区染色。
    /// </summary>
    /// <param name="source">源像素（BGRA32 非预乘）。</param>
    /// <param name="spec">染色参数。</param>
    /// <returns>新的像素缓冲区。</returns>
    public static WzPixelBuffer Apply(WzPixelBuffer source, DyeSpec spec)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(spec);

        int byteCount = source.Stride * source.Height;
        byte[] output = new byte[byteCount];
        Buffer.BlockCopy(source.Pixels, 0, output, 0, byteCount);

        RgbColor target = spec.Target;
        var (targetHue, targetSaturation, _) = ColorMath.RgbToHsl(target.R, target.G, target.B);
        double strength = Math.Clamp(spec.Strength, 0, 1);
        double minLightness = Math.Clamp(spec.MinLightness, 0, 1);

        // PaletteMap 只比较颜色距离，与明暗无关；预先展开以避免逐像素遍历 List。
        (RgbColor Source, RgbColor Target)[] palette = spec.Mode == DyeMode.PaletteMap && spec.Palette != null
            ? spec.Palette.Select(static pair => (pair.Source, pair.Target)).ToArray()
            : Array.Empty<(RgbColor, RgbColor)>();
        int toleranceSquared = spec.Tolerance * spec.Tolerance;

        for (int y = 0; y < source.Height; y++)
        {
            int row = y * source.Stride;
            for (int x = 0; x < source.Width; x++)
            {
                int offset = row + (x * 4);
                byte alpha = source.Pixels[offset + 3];
                if (alpha == 0)
                {
                    continue;
                }

                byte b = source.Pixels[offset];
                byte g = source.Pixels[offset + 1];
                byte r = source.Pixels[offset + 2];

                RgbColor dyed = spec.Mode switch
                {
                    DyeMode.Multiply => Multiply(r, g, b, target, strength),
                    DyeMode.PaletteMap => MapPalette(r, g, b, palette, toleranceSquared, strength),
                    DyeMode.GrayscaleHue => RecolorByLuminance(r, g, b, targetHue, targetSaturation, minLightness, strength),
                    _ => RecolorByLightness(r, g, b, targetHue, targetSaturation, minLightness, strength),
                };

                output[offset] = dyed.B;
                output[offset + 1] = dyed.G;
                output[offset + 2] = dyed.R;
                output[offset + 3] = alpha;
            }
        }

        return new WzPixelBuffer(source.Width, source.Height, source.Stride, output);
    }

    /// <summary>
    /// 色相替换：保留原亮度，替换色相/饱和度；暗部按 <paramref name="minLightness"/> 抬升。
    /// </summary>
    private static RgbColor RecolorByLightness(
        byte r,
        byte g,
        byte b,
        double hue,
        double saturation,
        double minLightness,
        double strength)
    {
        (_, _, double lightness) = ColorMath.RgbToHsl(r, g, b);

        // 纯黑/极暗像素直接替换会变成“死黑”，抬升后再上色才能保留轮廓细节。
        double lifted = minLightness + (lightness * (1 - minLightness));
        RgbColor recolored = ColorMath.HslToRgb(hue, saturation, lifted);

        return Blend(r, g, b, recolored, strength);
    }

    /// <summary>
    /// 先灰度化再上色：先用感知亮度重建明度，再套用目标色相/饱和度。
    /// </summary>
    private static RgbColor RecolorByLuminance(
        byte r,
        byte g,
        byte b,
        double hue,
        double saturation,
        double minLightness,
        double strength)
    {
        double luminance = ColorMath.Luminance(r, g, b);
        double lifted = minLightness + (luminance * (1 - minLightness));
        RgbColor recolored = ColorMath.HslToRgb(hue, saturation, lifted);

        return Blend(r, g, b, recolored, strength);
    }

    /// <summary>
    /// 乘法压色：<c>C' = C * Tint / 255</c>，适合整体加深或快速偏色。
    /// </summary>
    private static RgbColor Multiply(byte r, byte g, byte b, RgbColor tint, double strength)
    {
        byte nr = (byte)((r * tint.R) / 255);
        byte ng = (byte)((g * tint.G) / 255);
        byte nb = (byte)((b * tint.B) / 255);
        return Blend(r, g, b, new RgbColor(nr, ng, nb), strength);
    }

    /// <summary>
    /// 调色板映射：在容差内找最接近的源色，替换为目标色。
    /// </summary>
    private static RgbColor MapPalette(
        byte r,
        byte g,
        byte b,
        (RgbColor Source, RgbColor Target)[] palette,
        int toleranceSquared,
        double strength)
    {
        int bestDistance = int.MaxValue;
        RgbColor best = default;
        bool matched = false;

        foreach ((RgbColor source, RgbColor target) in palette)
        {
            int dr = r - source.R;
            int dg = g - source.G;
            int db = b - source.B;
            int distance = (dr * dr) + (dg * dg) + (db * db);
            if (distance <= toleranceSquared && distance < bestDistance)
            {
                bestDistance = distance;
                best = target;
                matched = true;
            }
        }

        return matched
            ? Blend(r, g, b, best, strength)
            : new RgbColor(r, g, b);
    }

    /// <summary>把原色按比例混合到目标色。</summary>
    private static RgbColor Blend(byte r, byte g, byte b, RgbColor target, double strength) =>
        new(
            ColorMath.Lerp(r, target.R, strength),
            ColorMath.Lerp(g, target.G, strength),
            ColorMath.Lerp(b, target.B, strength));
}
