namespace WzReader.Core.Codecs;

/// <summary>
/// 解码后的画布像素缓冲区，统一使用 <b>BGRA32（32 位、非预乘、行优先自上而下）</b> 布局。
/// </summary>
/// <remarks>
/// 选择统一输出 BGRA32 的原因：
/// <list type="bullet">
/// <item>WPF 的 <c>PixelFormats.Bgra32</c> 与 SkiaSharp 的 <c>Bgra8888</c> 都直接支持，无需再转换；</item>
/// <item>非预乘（straight alpha）可以无损表达 WZ 原始像素，预乘会丢失半透明像素的颜色精度；</item>
/// <item>所有解码器（ARGB4444/8888、DXT、BC7）最终都能低成本转换到该布局。</item>
/// </list>
/// 本类型不做线程同步，解码完成后视为只读数据，可安全地在多线程间共享引用。
/// </remarks>
public sealed class WzPixelBuffer
{
    /// <summary>创建像素缓冲区。</summary>
    /// <param name="width">宽度（像素），必须大于 0。</param>
    /// <param name="height">高度（像素），必须大于 0。</param>
    /// <param name="stride">每行字节数，必须等于 <c>width * 4</c> 或更大。</param>
    /// <param name="pixels">BGRA32 像素数据，长度至少为 <c>stride * height</c>。</param>
    /// <exception cref="ArgumentOutOfRangeException">尺寸非法。</exception>
    /// <exception cref="ArgumentException">缓冲区长度不足。</exception>
    public WzPixelBuffer(int width, int height, int stride, byte[] pixels)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "像素缓冲区尺寸必须大于 0。");
        }

        if (stride < width * 4)
        {
            throw new ArgumentOutOfRangeException(nameof(stride), "stride 不能小于 width * 4。");
        }

        if (pixels.Length < stride * (long)height)
        {
            throw new ArgumentException("像素数据长度不足。", nameof(pixels));
        }

        this.Width = width;
        this.Height = height;
        this.Stride = stride;
        this.Pixels = pixels;
    }

    /// <summary>宽度（像素）。</summary>
    public int Width { get; }

    /// <summary>高度（像素）。</summary>
    public int Height { get; }

    /// <summary>每行字节数。</summary>
    public int Stride { get; }

    /// <summary>BGRA32 像素数据（非预乘）。</summary>
    public byte[] Pixels { get; }

    /// <summary>读取指定像素的 BGRA 分量。</summary>
    /// <param name="x">横坐标，从 0 开始。</param>
    /// <param name="y">纵坐标，从 0 开始。</param>
    /// <param name="b">蓝色分量。</param>
    /// <param name="g">绿色分量。</param>
    /// <param name="r">红色分量。</param>
    /// <param name="a">Alpha 分量。</param>
    public void GetPixel(int x, int y, out byte b, out byte g, out byte r, out byte a)
    {
        int offset = (y * this.Stride) + (x * 4);
        b = this.Pixels[offset];
        g = this.Pixels[offset + 1];
        r = this.Pixels[offset + 2];
        a = this.Pixels[offset + 3];
    }

    /// <summary>
    /// 计算非透明像素的包围盒（用于导出裁剪、命中测试与合成剔除）。
    /// </summary>
    /// <param name="threshold">Alpha 阈值：小于等于该值的像素视为透明，默认 0。</param>
    /// <returns>包围盒；整张图全透明时返回 <c>null</c>。</returns>
    public WzPixelBounds? FindOpaqueBounds(byte threshold = 0)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;

        for (int y = 0; y < this.Height; y++)
        {
            int rowStart = y * this.Stride;
            for (int x = 0; x < this.Width; x++)
            {
                if (this.Pixels[rowStart + (x * 4) + 3] > threshold)
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    maxY = y;
                }
            }
        }

        return maxX < 0 ? null : new WzPixelBounds(minX, minY, maxX, maxY);
    }

    /// <summary>
    /// 裁剪出一块子区域，返回新的像素缓冲区（深拷贝）。
    /// </summary>
    /// <param name="bounds">裁剪范围，必须在当前图内。</param>
    /// <returns>新的像素缓冲区。</returns>
    public WzPixelBuffer Crop(WzPixelBounds bounds)
    {
        if (bounds.X < 0 || bounds.Y < 0 || bounds.Right > this.Width || bounds.Bottom > this.Height)
        {
            throw new ArgumentOutOfRangeException(nameof(bounds), "裁剪范围超出图像边界。");
        }

        int w = bounds.Width;
        int h = bounds.Height;
        int stride = w * 4;
        byte[] dst = new byte[stride * h];

        for (int y = 0; y < h; y++)
        {
            Buffer.BlockCopy(
                this.Pixels,
                ((bounds.Y + y) * this.Stride) + (bounds.X * 4),
                dst,
                y * stride,
                stride);
        }

        return new WzPixelBuffer(w, h, stride, dst);
    }
}

/// <summary>
/// 整数像素包围盒（左闭右闭区间）。
/// </summary>
/// <param name="X">左边界。</param>
/// <param name="Y">上边界。</param>
/// <param name="MaxX">右边界（含）。</param>
/// <param name="MaxY">下边界（含）。</param>
public readonly record struct WzPixelBounds(int X, int Y, int MaxX, int MaxY)
{
    /// <summary>宽度。</summary>
    public int Width => this.MaxX - this.X + 1;

    /// <summary>高度。</summary>
    public int Height => this.MaxY - this.Y + 1;

    /// <summary>右边界（不含）。</summary>
    public int Right => this.MaxX + 1;

    /// <summary>下边界（不含）。</summary>
    public int Bottom => this.MaxY + 1;
}
