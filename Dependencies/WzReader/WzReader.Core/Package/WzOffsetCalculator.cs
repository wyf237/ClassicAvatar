using System.Buffers.Binary;
using System.Numerics;

namespace WzReader.Core.Package;

/// <summary>
/// WZ 目录条目偏移计算（PKG1 / PKG2 公式不同，不能混用）。
/// </summary>
/// <remarks>
/// <para>
/// <b>PKG1：</b><c>position</c> 必须代入 <c>hashOffset</c> 字段的<b>绝对文件位置</b>。
/// 这是实测校准结论：代入“相对 DataStart”的位置会全部算出上亿的越界值。
/// </para>
/// <para>
/// <b>PKG2：</b>沿用“相对 DataStart”的基准。该基准目前没有实测样本可校准，
/// 接入 PKG2 数据后应优先复核此处。
/// </para>
/// <para>所有运算必须在 32 位无符号回绕（<c>unchecked</c>）下进行。</para>
/// </remarks>
public static class WzOffsetCalculator
{
    /// <summary>PKG1 偏移公式。</summary>
    /// <param name="position">hashOffset 字段的绝对文件位置。</param>
    /// <param name="hashedOffset">目录条目中的 hashOffset 值。</param>
    /// <param name="headerSize">文件头长度。</param>
    /// <param name="hashVersion">版本哈希。</param>
    /// <returns>img 块在文件中的绝对偏移。</returns>
    public static uint CalcPkg1(uint position, uint hashedOffset, uint headerSize, uint hashVersion)
    {
        unchecked
        {
            uint value = position - headerSize;
            value = ~value;
            value *= hashVersion;
            value -= 0x581C3F6D;
            value = BitOperations.RotateLeft(value, (int)(value & 0x1F));
            value ^= hashedOffset;
            value += headerSize * 2;
            return value;
        }
    }

    /// <summary>PKG2（KMST1196/1197，V1 profile）偏移公式。</summary>
    /// <param name="relativePosition">hashOffset 字段位置（相对 DataStart）。</param>
    /// <param name="hashedOffset">目录条目中的 hashOffset 值。</param>
    /// <param name="headerSize">文件头长度。</param>
    /// <param name="hash1">PKG2 头的 hash1。</param>
    /// <param name="hashVersion">反解出的 hashVersion。</param>
    /// <returns>img 块在文件中的绝对偏移。</returns>
    public static uint CalcPkg2V1(uint relativePosition, uint hashedOffset, uint headerSize, uint hash1, uint hashVersion)
    {
        unchecked
        {
            uint value = relativePosition - headerSize;
            value = ~value;
            value *= hashVersion;
            value -= 0x581C3F6D;
            value ^= hash1 * 0x01010101;
            int distance = (int)((hashVersion ^ hash1) & 0x1F);
            value = BitOperations.RotateLeft(value, distance);
            value ^= hashedOffset;
            value += headerSize;
            return value;
        }
    }

    /// <summary>PKG2（V1 profile）条目计数解密。</summary>
    /// <param name="encryptedCount">文件中的加密计数。</param>
    /// <param name="hash1">PKG2 头的 hash1。</param>
    /// <param name="hashVersion">hashVersion。</param>
    /// <returns>真实条目数。</returns>
    public static int DecryptEntryCountPkg2V1(int encryptedCount, uint hash1, uint hashVersion)
    {
        unchecked
        {
            uint key = (hash1 << 24) + (0x7F4A7C15u * hashVersion);
            return (int)((uint)encryptedCount ^ key);
        }
    }

    /// <summary>读取小端 uint32（辅助方法）。</summary>
    /// <param name="data">数据。</param>
    /// <param name="offset">偏移。</param>
    /// <returns>值。</returns>
    internal static uint ReadUInt32(ReadOnlySpan<byte> data, int offset)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
    }
}
