using WzReader.Core.Codecs;

namespace Avatar.Core.Rendering;

/// <summary>
/// 像素合成器：把部件画布按“source-over（非预乘直通 Alpha）”叠加到目标画布，支持水平镜像。
/// </summary>
/// <remarks>
/// 所有缓冲区都是 BGRA32 非预乘。合成公式（0..255 整数近似，无浮点）：
/// <code>
/// outA255 = sa*255 + da*(255-sa)
/// outC    = (srcC*sa*255 + dstC*da*(255-sa)) / outA255
/// </code>
/// 目标画布初始为全透明，因此绝大多数像素走“直接拷贝”快路径，性能接近 <c>Buffer.BlockCopy</c>。
/// 本类无状态、线程安全，只读源缓冲区。
/// </remarks>
public static class PixelCompositor
{
    /// <summary>
    /// 创建全透明 BGRA32 画布。
    /// </summary>
    /// <param name="width">宽度（必须大于 0）。</param>
    /// <param name="height">高度（必须大于 0）。</param>
    /// <returns>新的像素缓冲区。</returns>
    /// <exception cref="ArgumentOutOfRangeException">尺寸非法。</exception>
    public static WzPixelBuffer CreateCanvas(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "画布尺寸必须大于 0。");
        }

        return new WzPixelBuffer(width, height, width * 4, new byte[width * 4 * height]);
    }

    /// <summary>
    /// 把源画布叠加到目标画布。
    /// </summary>
    /// <param name="destination">目标画布（会被修改）。</param>
    /// <param name="source">源画布（只读）。</param>
    /// <param name="x">源画布左上角在目标画布中的 X（可为负，自动裁剪）。</param>
    /// <param name="y">源画布左上角在目标画布中的 Y（可为负，自动裁剪）。</param>
    /// <param name="mirrorX">是否把源画布水平镜像后再叠加。</param>
    /// <remarks>
    /// 镜像只翻转源像素（不改变 <paramref name="x"/> 的含义）；
    /// 调用方需要用 <see cref="MirrorLeft"/> 计算镜像后的左上角。
    /// </remarks>
    public static void Draw(
        WzPixelBuffer destination,
        WzPixelBuffer source,
        int x,
        int y,
        bool mirrorX = false)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(source);

        int left = Math.Max(0, x);
        int top = Math.Max(0, y);
        int right = Math.Min(destination.Width, x + source.Width);
        int bottom = Math.Min(destination.Height, y + source.Height);
        if (left >= right || top >= bottom)
        {
            return;
        }

        byte[] dst = destination.Pixels;
        byte[] src = source.Pixels;
        int dstStride = destination.Stride;
        int srcStride = source.Stride;
        int srcWidth = source.Width;

        for (int destY = top; destY < bottom; destY++)
        {
            int srcY = destY - y;
            int srcRow = srcY * srcStride;
            int dstRow = destY * dstStride;

            for (int destX = left; destX < right; destX++)
            {
                int srcX = destX - x;
                if (mirrorX)
                {
                    srcX = srcWidth - 1 - srcX;
                }

                int srcOffset = srcRow + (srcX * 4);
                int dstOffset = dstRow + (destX * 4);

                int srcAlpha = src[srcOffset + 3];
                if (srcAlpha == 0)
                {
                    continue;
                }

                if (srcAlpha == 255)
                {
                    dst[dstOffset] = src[srcOffset];
                    dst[dstOffset + 1] = src[srcOffset + 1];
                    dst[dstOffset + 2] = src[srcOffset + 2];
                    dst[dstOffset + 3] = 255;
                    continue;
                }

                int dstAlpha = dst[dstOffset + 3];
                if (dstAlpha == 0)
                {
                    dst[dstOffset] = src[srcOffset];
                    dst[dstOffset + 1] = src[srcOffset + 1];
                    dst[dstOffset + 2] = src[srcOffset + 2];
                    dst[dstOffset + 3] = (byte)srcAlpha;
                    continue;
                }

                // 非预乘 Alpha 的标准 source-over 合成（整数版）。
                int weightSrc = srcAlpha * 255;
                int weightDst = dstAlpha * (255 - srcAlpha);
                int outAlpha255 = weightSrc + weightDst;
                if (outAlpha255 <= 0)
                {
                    continue;
                }

                int blue = ((src[srcOffset] * weightSrc) + (dst[dstOffset] * weightDst)) / outAlpha255;
                int green = ((src[srcOffset + 1] * weightSrc) + (dst[dstOffset + 1] * weightDst)) / outAlpha255;
                int red = ((src[srcOffset + 2] * weightSrc) + (dst[dstOffset + 2] * weightDst)) / outAlpha255;

                dst[dstOffset] = (byte)blue;
                dst[dstOffset + 1] = (byte)green;
                dst[dstOffset + 2] = (byte)red;
                dst[dstOffset + 3] = (byte)Math.Min(255, (outAlpha255 + 127) / 255);
            }
        }
    }

    /// <summary>
    /// 计算水平镜像后的左上角 X。
    /// </summary>
    /// <param name="normalLeft">未镜像时的左上角 X（角色空间）。</param>
    /// <param name="width">部件宽度。</param>
    /// <returns>镜像后的左上角 X。</returns>
    /// <remarks>
    /// 镜像轴是角色空间的 <c>x = 0.5</c>（像素列 <c>x</c> ↔ <c>-x</c>），
    /// 因此 <c>[left, left+width-1]</c> 映射为 <c>[-(left+width-1), -left]</c>，新左边界为 <c>1-width-left</c>。
    /// </remarks>
    public static int MirrorLeft(int normalLeft, int width) => 1 - width - normalLeft;

    /// <summary>
    /// 复制一份画布（深拷贝）。
    /// </summary>
    /// <param name="source">源画布。</param>
    /// <returns>新的像素缓冲区。</returns>
    public static WzPixelBuffer Clone(WzPixelBuffer source)
    {
        ArgumentNullException.ThrowIfNull(source);
        byte[] copy = new byte[source.Stride * source.Height];
        Buffer.BlockCopy(source.Pixels, 0, copy, 0, copy.Length);
        return new WzPixelBuffer(source.Width, source.Height, source.Stride, copy);
    }

    /// <summary>
    /// 整数倍最近邻放大（像素画放大首选，保留硬边缘）。
    /// </summary>
    /// <param name="source">源画布。</param>
    /// <param name="scale">放大倍数（1 表示原样返回）。</param>
    /// <returns>放大后的新画布。</returns>
    public static WzPixelBuffer Scale(WzPixelBuffer source, int scale)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (scale <= 1)
        {
            return Clone(source);
        }

        int width = source.Width * scale;
        int height = source.Height * scale;
        byte[] pixels = new byte[width * 4 * height];
        int sourceStride = source.Stride;

        for (int y = 0; y < source.Height; y++)
        {
            int sourceRow = y * sourceStride;
            for (int x = 0; x < source.Width; x++)
            {
                int sourceOffset = sourceRow + (x * 4);
                for (int dy = 0; dy < scale; dy++)
                {
                    int targetRow = (((y * scale) + dy) * width) * 4;
                    for (int dx = 0; dx < scale; dx++)
                    {
                        int targetOffset = targetRow + (((x * scale) + dx) * 4);
                        pixels[targetOffset] = source.Pixels[sourceOffset];
                        pixels[targetOffset + 1] = source.Pixels[sourceOffset + 1];
                        pixels[targetOffset + 2] = source.Pixels[sourceOffset + 2];
                        pixels[targetOffset + 3] = source.Pixels[sourceOffset + 3];
                    }
                }
            }
        }

        return new WzPixelBuffer(width, height, width * 4, pixels);
    }
}
