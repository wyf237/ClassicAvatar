using System.Buffers.Binary;
using WzReader.Core.Diagnostics;

namespace WzReader.Core.Codecs;

/// <summary>
/// 各像素格式 → BGRA32 的解码器集合。
/// </summary>
/// <remarks>
/// <para>
/// 所有解码结果统一为 BGRA32（非预乘、行优先、自上而下），这样 WPF（<c>Bgra32</c>）
/// 与 SkiaSharp（<c>Bgra8888</c>）都能直接使用。
/// </para>
/// <para>
/// 位布局说明：
/// <list type="bullet">
/// <item>ARGB4444：低字节为 <c>GGGGBBBB</c>，高字节为 <c>AAAARRRR</c>；</item>
/// <item>ARGB1555：bit15=A，bit14..10=R，bit9..5=G，bit4..0=B；</item>
/// <item>RGB565：bit15..11=R，bit10..5=G，bit4..0=B；</item>
/// <item>DXT/BC 系列按 4×4 块顺序解码。</item>
/// </list>
/// </para>
/// </remarks>
public static class WzPixelDecoder
{
    /// <summary>解码单页像素。</summary>
    /// <param name="source">解压后的完整数据。</param>
    /// <param name="offset">本页起始偏移。</param>
    /// <param name="width">原始宽度。</param>
    /// <param name="height">原始高度。</param>
    /// <param name="format">像素格式。</param>
    /// <returns>BGRA32 像素缓冲区。</returns>
    public static WzPixelBuffer DecodePage(byte[] source, int offset, int width, int height, WzPixelFormat format)
    {
        return format switch
        {
            WzPixelFormat.Argb4444 => DecodeArgb4444(source, offset, width, height),
            WzPixelFormat.Argb8888 => DecodeArgb8888(source, offset, width, height),
            WzPixelFormat.Argb1555 => DecodeArgb1555(source, offset, width, height),
            WzPixelFormat.Rgb565 => DecodeRgb565(source, offset, width, height),
            WzPixelFormat.Rgb888 or WzPixelFormat.Rgb888Variant => DecodeRgb888(source, offset, width, height),
            WzPixelFormat.R16 => DecodeR16(source, offset, width, height),
            WzPixelFormat.A8 => DecodeA8(source, offset, width, height),
            WzPixelFormat.Rgba1010102 => DecodeRgba1010102(source, offset, width, height),
            WzPixelFormat.Rgba32Float => DecodeRgba32Float(source, offset, width, height),
            WzPixelFormat.Dxt1 => BuildDxtBuffer(source, offset, width, height, dxt1: true, hasExplicitAlpha: false),
            WzPixelFormat.Dxt3 => BuildDxtBuffer(source, offset, width, height, dxt1: false, hasExplicitAlpha: true),
            WzPixelFormat.Dxt5 => BuildDxtBuffer(source, offset, width, height, dxt1: false, hasExplicitAlpha: false),
            WzPixelFormat.Bc7 => throw new WzUnsupportedException(
                "BC7 纹理暂未实现解码；请使用 DXT/ARGB 版本的客户端资源，或在导出时跳过该节点。"),
            _ => throw new WzUnsupportedException($"不支持的画布像素格式：{format.GetDisplayName()}。"),
        };
    }

    /// <summary>最近邻放大（保持像素内容不变，用于 <c>scale</c> 字段）。</summary>
    /// <param name="source">源缓冲区。</param>
    /// <param name="targetWidth">目标宽度。</param>
    /// <param name="targetHeight">目标高度。</param>
    /// <param name="factor">放大倍数。</param>
    /// <returns>放大后的缓冲区。</returns>
    public static WzPixelBuffer UpscaleNearest(WzPixelBuffer source, int targetWidth, int targetHeight, int factor)
    {
        int stride = targetWidth * 4;
        byte[] pixels = new byte[stride * targetHeight];

        for (int y = 0; y < targetHeight; y++)
        {
            int sourceY = y / factor;
            int sourceRow = sourceY * source.Stride;
            int targetRow = y * stride;
            for (int x = 0; x < targetWidth; x++)
            {
                int sourceOffset = sourceRow + ((x / factor) * 4);
                int targetOffset = targetRow + (x * 4);
                pixels[targetOffset] = source.Pixels[sourceOffset];
                pixels[targetOffset + 1] = source.Pixels[sourceOffset + 1];
                pixels[targetOffset + 2] = source.Pixels[sourceOffset + 2];
                pixels[targetOffset + 3] = source.Pixels[sourceOffset + 3];
            }
        }

        return new WzPixelBuffer(targetWidth, targetHeight, stride, pixels);
    }

    private static WzPixelBuffer DecodeArgb4444(byte[] source, int offset, int width, int height)
    {
        int stride = width * 4;
        byte[] pixels = new byte[stride * height];

        for (int i = 0, count = width * height; i < count; i++)
        {
            int sourceOffset = offset + (i * 2);
            byte low = source[sourceOffset];
            byte high = source[sourceOffset + 1];

            int target = i * 4;
            pixels[target] = Expand4(low & 0x0F);          // B
            pixels[target + 1] = Expand4((low >> 4) & 0x0F); // G
            pixels[target + 2] = Expand4(high & 0x0F);     // R
            pixels[target + 3] = Expand4((high >> 4) & 0x0F); // A
        }

        return new WzPixelBuffer(width, height, stride, pixels);
    }

    private static WzPixelBuffer DecodeArgb8888(byte[] source, int offset, int width, int height)
    {
        int stride = width * 4;
        byte[] pixels = new byte[stride * height];

        // 源文件本身即为 B,G,R,A 顺序，直接拷贝。
        Buffer.BlockCopy(source, offset, pixels, 0, Math.Min(pixels.Length, source.Length - offset));
        return new WzPixelBuffer(width, height, stride, pixels);
    }

    private static WzPixelBuffer DecodeArgb1555(byte[] source, int offset, int width, int height)
    {
        int stride = width * 4;
        byte[] pixels = new byte[stride * height];

        for (int i = 0, count = width * height; i < count; i++)
        {
            ushort value = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(offset + (i * 2), 2));
            int target = i * 4;
            pixels[target] = Expand5(value & 0x1F);              // B
            pixels[target + 1] = Expand5((value >> 5) & 0x1F);   // G
            pixels[target + 2] = Expand5((value >> 10) & 0x1F);  // R
            pixels[target + 3] = ((value >> 15) & 1) != 0 ? (byte)0xFF : (byte)0x00;
        }

        return new WzPixelBuffer(width, height, stride, pixels);
    }

    private static WzPixelBuffer DecodeRgb565(byte[] source, int offset, int width, int height)
    {
        int stride = width * 4;
        byte[] pixels = new byte[stride * height];

        for (int i = 0, count = width * height; i < count; i++)
        {
            ushort value = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(offset + (i * 2), 2));
            int target = i * 4;
            pixels[target] = Expand5(value & 0x1F);              // B
            pixels[target + 1] = Expand6((value >> 5) & 0x3F);   // G
            pixels[target + 2] = Expand5((value >> 11) & 0x1F);  // R
            pixels[target + 3] = 0xFF;
        }

        return new WzPixelBuffer(width, height, stride, pixels);
    }

    private static WzPixelBuffer DecodeRgb888(byte[] source, int offset, int width, int height)
    {
        int stride = width * 4;
        byte[] pixels = new byte[stride * height];

        for (int i = 0, count = width * height; i < count; i++)
        {
            int sourceOffset = offset + (i * 3);
            int target = i * 4;
            pixels[target] = source[sourceOffset];          // B
            pixels[target + 1] = source[sourceOffset + 1];  // G
            pixels[target + 2] = source[sourceOffset + 2];  // R
            pixels[target + 3] = 0xFF;
        }

        return new WzPixelBuffer(width, height, stride, pixels);
    }

    private static WzPixelBuffer DecodeR16(byte[] source, int offset, int width, int height)
    {
        int stride = width * 4;
        byte[] pixels = new byte[stride * height];

        for (int i = 0, count = width * height; i < count; i++)
        {
            ushort value = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(offset + (i * 2), 2));
            int target = i * 4;
            pixels[target] = 0;
            pixels[target + 1] = 0;
            pixels[target + 2] = (byte)(value >> 8); // 16 位红色通道压缩到 8 位
            pixels[target + 3] = 0xFF;
        }

        return new WzPixelBuffer(width, height, stride, pixels);
    }

    private static WzPixelBuffer DecodeA8(byte[] source, int offset, int width, int height)
    {
        int stride = width * 4;
        byte[] pixels = new byte[stride * height];

        for (int i = 0, count = width * height; i < count; i++)
        {
            byte value = source[offset + i];
            int target = i * 4;
            pixels[target] = value;
            pixels[target + 1] = value;
            pixels[target + 2] = value;
            pixels[target + 3] = 0xFF;
        }

        return new WzPixelBuffer(width, height, stride, pixels);
    }

    private static WzPixelBuffer DecodeRgba1010102(byte[] source, int offset, int width, int height)
    {
        int stride = width * 4;
        byte[] pixels = new byte[stride * height];

        for (int i = 0, count = width * height; i < count; i++)
        {
            uint value = BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(offset + (i * 4), 4));
            uint r10 = value & 0x3FF;
            uint g10 = (value >> 10) & 0x3FF;
            uint b10 = (value >> 20) & 0x3FF;
            uint a2 = (value >> 30) & 0x3;

            int target = i * 4;
            pixels[target] = (byte)(b10 >> 2);
            pixels[target + 1] = (byte)(g10 >> 2);
            pixels[target + 2] = (byte)(r10 >> 2);
            pixels[target + 3] = (byte)(a2 * 85);
        }

        return new WzPixelBuffer(width, height, stride, pixels);
    }

    private static WzPixelBuffer DecodeRgba32Float(byte[] source, int offset, int width, int height)
    {
        int stride = width * 4;
        byte[] pixels = new byte[stride * height];

        for (int i = 0, count = width * height; i < count; i++)
        {
            int sourceOffset = offset + (i * 16);
            float r = BinaryPrimitives.ReadSingleLittleEndian(source.AsSpan(sourceOffset, 4));
            float g = BinaryPrimitives.ReadSingleLittleEndian(source.AsSpan(sourceOffset + 4, 4));
            float b = BinaryPrimitives.ReadSingleLittleEndian(source.AsSpan(sourceOffset + 8, 4));
            float a = BinaryPrimitives.ReadSingleLittleEndian(source.AsSpan(sourceOffset + 12, 4));

            int target = i * 4;
            pixels[target] = ToByte(b);
            pixels[target + 1] = ToByte(g);
            pixels[target + 2] = ToByte(r);
            pixels[target + 3] = ToByte(a);
        }

        return new WzPixelBuffer(width, height, stride, pixels);
    }

    private static WzPixelBuffer BuildDxtBuffer(
        byte[] source,
        int offset,
        int width,
        int height,
        bool dxt1,
        bool hasExplicitAlpha)
    {
        int stride = width * 4;
        byte[] pixels = new byte[stride * height];
        int blockWidth = (width + 3) / 4;
        int blockHeight = (height + 3) / 4;

        for (int blockY = 0; blockY < blockHeight; blockY++)
        {
            for (int blockX = 0; blockX < blockWidth; blockX++)
            {
                int blockOffset = offset + (((blockY * blockWidth) + blockX) * 16);
                DecodeDxtBlock(
                    source,
                    blockOffset,
                    pixels,
                    stride,
                    width,
                    height,
                    blockX * 4,
                    blockY * 4,
                    dxt1,
                    hasExplicitAlpha);
            }
        }

        return new WzPixelBuffer(width, height, stride, pixels);
    }

    private static void DecodeDxtBlock(
        byte[] source,
        int offset,
        byte[] pixels,
        int stride,
        int width,
        int height,
        int baseX,
        int baseY,
        bool dxt1,
        bool hasExplicitAlpha)
    {
        // 颜色部分：color0/color1 位于块内 +8。
        ushort color0 = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(offset + 8, 2));
        ushort color1 = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(offset + 10, 2));

        (byte b, byte g, byte r, byte a) c0 = Rgb565ToBgra(color0, 0xFF);
        (byte b, byte g, byte r, byte a) c1 = Rgb565ToBgra(color1, 0xFF);
        (byte b, byte g, byte r, byte a) c2;
        (byte b, byte g, byte r, byte a) c3;

        if (color0 > color1)
        {
            // 四色模式。
            c2 = ((byte)((2 * c0.b + c1.b + 1) / 3), (byte)((2 * c0.g + c1.g + 1) / 3),
                  (byte)((2 * c0.r + c1.r + 1) / 3), (byte)0xFF);
            c3 = ((byte)((c0.b + 2 * c1.b + 1) / 3), (byte)((c0.g + 2 * c1.g + 1) / 3),
                  (byte)((c0.r + 2 * c1.r + 1) / 3), (byte)0xFF);
        }
        else if (dxt1)
        {
            // 三色 + 透明模式。
            c2 = ((byte)((c0.b + c1.b) / 2), (byte)((c0.g + c1.g) / 2),
                  (byte)((c0.r + c1.r) / 2), (byte)0xFF);
            c3 = (0, 0, 0, 0);
        }
        else
        {
            c2 = ((byte)((c0.b + c1.b) / 2), (byte)((c0.g + c1.g) / 2),
                  (byte)((c0.r + c1.r) / 2), (byte)0xFF);
            c3 = (0, 0, 0, 0xFF);
        }

        Span<(byte b, byte g, byte r, byte a)> colors = stackalloc (byte, byte, byte, byte)[4];
        colors[0] = c0;
        colors[1] = c1;
        colors[2] = c2;
        colors[3] = c3;

        // Alpha 表
        Span<byte> alphas = stackalloc byte[16];
        if (hasExplicitAlpha)
        {
            // DXT3：8 字节，每字节两个 4 位 alpha，低 nibble 在前。
            for (int i = 0; i < 16; i++)
            {
                byte value = source[offset + (i / 2)];
                int nibble = (i % 2 == 0) ? value & 0x0F : (value >> 4) & 0x0F;
                alphas[i] = Expand4(nibble);
            }
        }
        else if (!dxt1)
        {
            // DXT5：a0/a1 + 48 位索引。
            byte a0 = source[offset];
            byte a1 = source[offset + 1];
            Span<byte> table = stackalloc byte[8];
            table[0] = a0;
            table[1] = a1;

            if (a0 > a1)
            {
                for (int i = 2; i < 8; i++)
                {
                    table[i] = (byte)((((8 - i) * a0) + ((i - 1) * a1) + 3) / 7);
                }
            }
            else
            {
                for (int i = 2; i < 6; i++)
                {
                    table[i] = (byte)((((6 - i) * a0) + ((i - 1) * a1) + 2) / 5);
                }

                table[6] = 0;
                table[7] = 255;
            }

            for (int i = 0; i < 16; i++)
            {
                int bitIndex = i * 3;
                int byteIndex = 2 + (bitIndex / 8);
                int shift = bitIndex % 8;
                int value = source[offset + byteIndex] >> shift;
                if (shift > 5)
                {
                    value |= source[offset + byteIndex + 1] << (8 - shift);
                }

                alphas[i] = table[value & 0x7];
            }
        }
        else
        {
            alphas.Fill(0xFF);
        }

        // 颜色索引：块内 +12，共 16 个 2 位索引，低位先。
        for (int i = 0; i < 16; i++)
        {
            int byteIndex = 12 + (i / 4);
            int shift = (i % 4) * 2;
            int index = (source[offset + byteIndex] >> shift) & 0x03;
            (byte b, byte g, byte r, byte a) color = colors[index];

            int x = baseX + (i % 4);
            int y = baseY + (i / 4);
            if (x >= width || y >= height)
            {
                continue;
            }

            int target = (y * stride) + (x * 4);
            pixels[target] = color.b;
            pixels[target + 1] = color.g;
            pixels[target + 2] = color.r;
            pixels[target + 3] = hasExplicitAlpha || !dxt1 ? alphas[i] : color.a;
        }
    }

    private static (byte b, byte g, byte r, byte a) Rgb565ToBgra(ushort value, byte alpha)
    {
        int r5 = (value >> 11) & 0x1F;
        int g6 = (value >> 5) & 0x3F;
        int b5 = value & 0x1F;

        byte r = (byte)((r5 << 3) | (r5 >> 2));
        byte g = (byte)((g6 << 2) | (g6 >> 4));
        byte b = (byte)((b5 << 3) | (b5 >> 2));
        return (b, g, r, alpha);
    }

    private static byte Expand4(int value) => (byte)((value << 4) | value);

    private static byte Expand5(int value) => (byte)((value << 3) | (value >> 2));

    private static byte Expand6(int value) => (byte)((value << 2) | (value >> 4));

    private static byte ToByte(float value)
    {
        if (float.IsNaN(value) || value <= 0)
        {
            return 0;
        }

        if (value >= 1)
        {
            return 255;
        }

        return (byte)(value * 255f + 0.5f);
    }
}
