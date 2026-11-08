using System.Buffers.Binary;
using System.IO.Compression;
using WzReader.Core.Codecs;

namespace WzReader.Export.Imaging;

/// <summary>
/// 极简 PNG 编码器（不依赖任何第三方库）：把 <see cref="WzPixelBuffer"/> 编码为 PNG 字节流或直接写盘。
/// </summary>
/// <remarks>
/// 实现要点：
/// <list type="bullet">
/// <item>输出 8 位 RGBA（color type 6），无交错；像素源为 BGRA32 非预乘，编码时交换 R/B 通道即可。</item>
/// <item>每行使用 filter type 0（None），这是最简单且对游戏素材足够有效的选择；</item>
/// <item>IDAT 使用 <see cref="ZLibStream"/>（带 zlib 头），符合 PNG 规范。</item>
/// </list>
/// 本类线程安全（无共享可变状态）。
/// </remarks>
public static class PngEncoder
{
    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary>把像素缓冲区编码为 PNG 字节数组。</summary>
    /// <param name="buffer">像素缓冲区（BGRA32）。</param>
    /// <returns>完整的 PNG 文件字节。</returns>
    public static byte[] Encode(WzPixelBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        using MemoryStream output = new();
        output.Write(PngSignature);

        // IHDR：width, height, bitDepth=8, colorType=6(RGBA), compression=0, filter=0, interlace=0
        byte[] ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(0, 4), buffer.Width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4, 4), buffer.Height);
        ihdr[8] = 8;
        ihdr[9] = 6;
        ihdr[10] = 0;
        ihdr[11] = 0;
        ihdr[12] = 0;
        WriteChunk(output, "IHDR", ihdr);

        // IDAT：每行前缀 1 字节 filter(0)，随后 RGBA 像素
        int rowBytes = buffer.Width * 4;
        byte[] raw = new byte[(rowBytes + 1) * buffer.Height];
        int writeIndex = 0;
        for (int y = 0; y < buffer.Height; y++)
        {
            raw[writeIndex++] = 0;
            int sourceRow = y * buffer.Stride;
            for (int x = 0; x < buffer.Width; x++)
            {
                int sourceOffset = sourceRow + (x * 4);
                raw[writeIndex++] = buffer.Pixels[sourceOffset + 2]; // R
                raw[writeIndex++] = buffer.Pixels[sourceOffset + 1]; // G
                raw[writeIndex++] = buffer.Pixels[sourceOffset];     // B
                raw[writeIndex++] = buffer.Pixels[sourceOffset + 3]; // A
            }
        }

        using MemoryStream compressed = new();
        using (ZLibStream zlib = new(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(raw, 0, raw.Length);
        }

        WriteChunk(output, "IDAT", compressed.ToArray());
        WriteChunk(output, "IEND", Array.Empty<byte>());

        return output.ToArray();
    }

    /// <summary>把像素缓冲区保存为 PNG 文件（自动创建目录）。</summary>
    /// <param name="buffer">像素缓冲区。</param>
    /// <param name="filePath">目标文件路径，建议以 <c>.png</c> 结尾。</param>
    public static void Save(WzPixelBuffer buffer, string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(filePath, Encode(buffer));
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);

        byte[] typeBytes = { (byte)type[0], (byte)type[1], (byte)type[2], (byte)type[3] };
        stream.Write(typeBytes);
        stream.Write(data);

        uint crc = Crc32(typeBytes, data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        stream.Write(crcBytes);
    }

    private static uint Crc32(byte[] type, byte[] data)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (byte b in type)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        foreach (byte b in data)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFFu;
    }

    private static uint[] BuildCrcTable()
    {
        uint[] table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
