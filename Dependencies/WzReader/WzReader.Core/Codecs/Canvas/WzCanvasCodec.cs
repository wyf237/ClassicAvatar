using System.IO.Compression;
using WzReader.Core.Crypto;
using WzReader.Core.Diagnostics;

namespace WzReader.Core.Codecs;

/// <summary>
/// 画布压缩流处理：解压、分块 XOR 解密、按页切分，并把每页交给 <see cref="WzPixelDecoder"/>。
/// </summary>
/// <remarks>
/// <para>
/// WZ 画布像素数据的实际布局（实测）：
/// <c>[1 字节前缀][2 字节 zlib 头 78 9C][raw deflate 流]</c>。
/// .NET 的 <see cref="DeflateStream"/> 直接按 raw deflate 处理，因此从第 3 字节开始喂给它即可；
/// 不要尝试用 <c>ZLibStream</c>，文件里没有 adler32 尾部。
/// </para>
/// <para>
/// 若第 2、3 字节不是 <c>78 9C</c>，则走“4 字节长度 + 分块 XOR（每块 keyOffset 归零）”流，
/// 解密后前 2 字节是 zlib 头（丢弃），其余是 raw deflate。
/// </para>
/// </remarks>
public static class WzCanvasCodec
{
    /// <summary>解码画布全部帧。</summary>
    /// <param name="canvas">画布。</param>
    /// <returns>每帧的 BGRA32 像素缓冲区。</returns>
    /// <exception cref="WzUnsupportedException">像素格式未实现。</exception>
    /// <exception cref="WzFormatException">压缩数据损坏或尺寸不符。</exception>
    public static IReadOnlyList<WzPixelBuffer> Decode(WzCanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        int actualScale = canvas.ActualScale;
        int rawWidth = canvas.Width;
        int rawHeight = canvas.Height;

        if (actualScale > 1)
        {
            if (canvas.Width % actualScale != 0 || canvas.Height % actualScale != 0)
            {
                throw new WzFormatException(
                    $"画布缩放后的尺寸不能整除：{canvas.Width}x{canvas.Height}，scale={actualScale}。");
            }

            rawWidth = canvas.Width / actualScale;
            rawHeight = canvas.Height / actualScale;
        }

        if (rawWidth <= 0 || rawHeight <= 0)
        {
            throw new WzFormatException($"画布尺寸非法：{canvas.Width}x{canvas.Height}。");
        }

        int pageSize = GetPageByteSize(rawWidth, rawHeight, canvas.Format);
        int frameCount = canvas.FrameCount;

        byte[] uncompressed = Decompress(canvas.CompressedData, canvas.Key, canvas.Diagnostics);

        long expected = (long)pageSize * frameCount;
        if (uncompressed.Length < expected)
        {
            throw new WzFormatException(
                $"画布像素数据长度不足：期望 {expected} 字节，实际 {uncompressed.Length} 字节" +
                $"（{canvas.Width}x{canvas.Height} {canvas.Format.GetDisplayName()}，{frameCount} 帧）。");
        }

        List<WzPixelBuffer> frames = new(frameCount);
        for (int frame = 0; frame < frameCount; frame++)
        {
            int offset = frame * pageSize;
            WzPixelBuffer raw = WzPixelDecoder.DecodePage(uncompressed, offset, rawWidth, rawHeight, canvas.Format);

            if (actualScale > 1)
            {
                raw = WzPixelDecoder.UpscaleNearest(raw, canvas.Width, canvas.Height, actualScale);
            }

            frames.Add(raw);
        }

        return frames;
    }

    /// <summary>解压画布像素流（含分块 XOR 分支）。</summary>
    /// <param name="data">原始数据（含 1 字节前缀）。</param>
    /// <param name="key">分块 XOR 密钥。</param>
    /// <param name="diagnostics">诊断收集器。</param>
    /// <returns>解压后的像素字节。</returns>
    public static byte[] Decompress(byte[] data, IWzXorKey? key, WzDiagnostics? diagnostics)
    {
        if (data.Length < 4)
        {
            return Array.Empty<byte>();
        }

        // 首字节含义未公开，固定跳过。
        if (data[1] == 0x78 && data[2] == 0x9C)
        {
            return Inflate(data, 3, data.Length - 3, diagnostics);
        }

        byte[] plain = DecryptChunks(data, 1, key);
        if (plain.Length < 3)
        {
            diagnostics?.Warn("画布分块解密后的数据过短，无法解压。", $"长度 {plain.Length}");
            return Array.Empty<byte>();
        }

        return Inflate(plain, 2, plain.Length - 2, diagnostics);
    }

    private static byte[] DecryptChunks(byte[] source, int start, IWzXorKey? key)
    {
        using MemoryStream output = new(source.Length);
        int position = start;

        while (position + 4 <= source.Length)
        {
            int length = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(position, 4));
            position += 4;
            if (length <= 0)
            {
                break;
            }

            if (position + length > source.Length)
            {
                // 损坏数据：尽力读出剩余部分。
                length = source.Length - position;
            }

            byte[] chunk = new byte[length];
            Buffer.BlockCopy(source, position, chunk, 0, length);
            position += length;

            if (key != null)
            {
                key.Decrypt(chunk, 0);
            }

            output.Write(chunk, 0, chunk.Length);
        }

        return output.ToArray();
    }

    private static byte[] Inflate(byte[] source, int offset, int count, WzDiagnostics? diagnostics)
    {
        if (count <= 0)
        {
            return Array.Empty<byte>();
        }

        try
        {
            using MemoryStream input = new(source, offset, count, writable: false);
            using DeflateStream deflate = new(input, CompressionMode.Decompress);
            using MemoryStream output = new(count * 4);
            deflate.CopyTo(output);
            return output.ToArray();
        }
        catch (Exception ex)
        {
            // 少数私服文件在流尾部有多余填充，DeflateStream 可能抛出；将已解出的部分返回不现实，
            // 这里统一转为格式异常，由上层记录并跳过该节点。
            throw new WzFormatException("画布 deflate 解压失败。", ex);
        }
    }

    /// <summary>
    /// 计算单页未压缩字节数（按格式与缩放后的原始宽高）。
    /// </summary>
    /// <param name="width">原始（缩放前）宽度。</param>
    /// <param name="height">原始（缩放前）高度。</param>
    /// <param name="format">像素格式。</param>
    /// <returns>单页字节数。</returns>
    public static int GetPageByteSize(int width, int height, WzPixelFormat format)
    {
        int blockWidth = (width + 3) / 4;
        int blockHeight = (height + 3) / 4;

        return format switch
        {
            WzPixelFormat.Argb4444 or WzPixelFormat.Argb1555 or WzPixelFormat.Rgb565 or WzPixelFormat.R16
                => width * height * 2,
            WzPixelFormat.Argb8888 or WzPixelFormat.Rgba1010102 => width * height * 4,
            WzPixelFormat.Rgb888 or WzPixelFormat.Rgb888Variant => width * height * 3,
            WzPixelFormat.A8 => width * height,
            WzPixelFormat.Dxt3 or WzPixelFormat.Dxt5 => blockWidth * blockHeight * 16,
            WzPixelFormat.Dxt1 => blockWidth * blockHeight * 8,
            WzPixelFormat.Bc7 => width * (height & ~3),
            WzPixelFormat.Rgba32Float => width * height * 16,
            _ => throw new WzUnsupportedException($"不支持的画布像素格式：{format.GetDisplayName()}。"),
        };
    }
}
