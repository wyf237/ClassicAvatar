using System.Buffers.Binary;
using System.Text;
using WzReader.Core.Crypto;
using WzReader.Core.Diagnostics;

namespace WzReader.Core.Common;

/// <summary>
/// 面向 WZ 二进制结构的读取器：在普通 <see cref="Stream"/> 之上封装压缩整数、加密字符串与 img 内部字符串。
/// </summary>
/// <remarks>
/// <para>
/// WZ 的“奇怪”之处都集中在这里，读懂本类即可理解字符串层：
/// <list type="bullet">
/// <item>压缩整数用 <c>sbyte</c> 哨兵 <c>-128</c> 表示“后面跟 4/8 字节真实值”；</item>
/// <item>字符串长度前缀也是 <c>sbyte</c>：负数表示 ASCII 字节数，正数表示 UTF-16 字符数，
/// 分别用 <c>-128</c>/<c>127</c> 作为逃逸哨兵；</item>
/// <item>字符串解密必须<b>先</b>异或密钥表，<b>再</b>异或位置掩码（<c>0xAA+i</c> / <c>0xAAAA+i</c>）；</item>
/// <item>img 内部的字符串引用偏移以 <b>img 流起点</b> 为 0 基准（本类不做重定位）。</item>
/// </list>
/// </para>
/// <para>
/// 本类不做线程同步：每个读取任务应使用独立实例。包级别的目录读取只在打开时执行一次；
/// img 解析时为每张 img 创建新的 <see cref="MemoryStream"/> 与读取器。
/// </para>
/// </remarks>
public sealed class WzBinaryReader
{
    private readonly Stream _stream;

    /// <summary>包装一个可定位的流。</summary>
    /// <param name="stream">底层流；生命周期由调用方管理。</param>
    public WzBinaryReader(Stream stream)
    {
        this._stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    /// <summary>底层流。</summary>
    public Stream BaseStream => this._stream;

    /// <summary>当前流位置（相对于传入流的起点）。</summary>
    public long Position
    {
        get => this._stream.Position;
        set => this._stream.Position = value;
    }

    /// <summary>流长度。</summary>
    public long Length => this._stream.Length;

    /// <summary>剩余可读字节数。</summary>
    public long Remaining => this._stream.Length - this._stream.Position;

    /// <summary>读取 1 字节；越界抛出 <see cref="WzFormatException"/>。</summary>
    /// <returns>字节值。</returns>
    public byte ReadByte()
    {
        int value = this._stream.ReadByte();
        if (value < 0)
        {
            throw new WzFormatException($"读取越界（位置 {this._stream.Position}，长度 {this._stream.Length}）。");
        }

        return (byte)value;
    }

    /// <summary>读取 1 字节有符号数。</summary>
    /// <returns>有符号字节。</returns>
    public sbyte ReadSByte()
    {
        return unchecked((sbyte)this.ReadByte());
    }

    /// <summary>读取指定长度字节数组。</summary>
    /// <param name="count">字节数，必须非负。</param>
    /// <returns>新分配的字节数组。</returns>
    public byte[] ReadBytes(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        byte[] buffer = new byte[count];
        this.ReadExactly(buffer);
        return buffer;
    }

    /// <summary>精确读满目标缓冲区；提前结束抛出异常。</summary>
    /// <param name="buffer">目标缓冲区。</param>
    public void ReadExactly(Span<byte> buffer)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            int n = this._stream.Read(buffer[read..]);
            if (n <= 0)
            {
                throw new WzFormatException(
                    $"读取越界：需要 {buffer.Length} 字节，仅读到 {read} 字节（位置 {this._stream.Position}）。");
            }

            read += n;
        }
    }

    /// <summary>读取小端 16 位有符号整数。</summary>
    /// <returns>读取结果。</returns>
    public short ReadInt16()
    {
        Span<byte> buffer = stackalloc byte[2];
        this.ReadExactly(buffer);
        return BinaryPrimitives.ReadInt16LittleEndian(buffer);
    }

    /// <summary>读取小端 32 位有符号整数。</summary>
    /// <returns>读取结果。</returns>
    public int ReadInt32()
    {
        Span<byte> buffer = stackalloc byte[4];
        this.ReadExactly(buffer);
        return BinaryPrimitives.ReadInt32LittleEndian(buffer);
    }

    /// <summary>读取小端 32 位无符号整数。</summary>
    /// <returns>读取结果。</returns>
    public uint ReadUInt32()
    {
        Span<byte> buffer = stackalloc byte[4];
        this.ReadExactly(buffer);
        return BinaryPrimitives.ReadUInt32LittleEndian(buffer);
    }

    /// <summary>读取小端 64 位有符号整数。</summary>
    /// <returns>读取结果。</returns>
    public long ReadInt64()
    {
        Span<byte> buffer = stackalloc byte[8];
        this.ReadExactly(buffer);
        return BinaryPrimitives.ReadInt64LittleEndian(buffer);
    }

    /// <summary>读取小端 32 位浮点数。</summary>
    /// <returns>读取结果。</returns>
    public float ReadSingle()
    {
        Span<byte> buffer = stackalloc byte[4];
        this.ReadExactly(buffer);
        return BinaryPrimitives.ReadSingleLittleEndian(buffer);
    }

    /// <summary>读取小端 64 位浮点数。</summary>
    /// <returns>读取结果。</returns>
    public double ReadDouble()
    {
        Span<byte> buffer = stackalloc byte[8];
        this.ReadExactly(buffer);
        return BinaryPrimitives.ReadDoubleLittleEndian(buffer);
    }

    /// <summary>跳过指定字节数（允许移动到流末尾，但之后读取会越界抛异常）。</summary>
    /// <param name="count">字节数。</param>
    public void Skip(long count)
    {
        this._stream.Position += count;
    }

    /// <summary>
    /// 读取 WZ 压缩 32 位整数：首字节 <c>-128</c> 时再读 4 字节，否则就是该 <c>sbyte</c> 值。
    /// </summary>
    /// <returns>整数值。</returns>
    public int ReadCompressedInt32()
    {
        sbyte value = this.ReadSByte();
        return value == -128 ? this.ReadInt32() : value;
    }

    /// <summary>
    /// 读取 WZ 压缩 64 位整数：首字节 <c>-128</c> 时再读 8 字节，否则就是该 <c>sbyte</c> 值。
    /// </summary>
    /// <returns>整数值。</returns>
    public long ReadCompressedInt64()
    {
        sbyte value = this.ReadSByte();
        return value == -128 ? this.ReadInt64() : value;
    }

    /// <summary>
    /// 读取 WZ 压缩浮点数：首字节 <c>-128</c> 时再读 4 字节 IEEE754，否则是该 <c>sbyte</c> 转 float。
    /// </summary>
    /// <returns>浮点值。</returns>
    public float ReadCompressedSingle()
    {
        sbyte value = this.ReadSByte();
        return value == -128 ? this.ReadSingle() : value;
    }

    /// <summary>
    /// 读取一个 WZ 字符串（含解密与位置掩码）。
    /// </summary>
    /// <param name="key">XOR 密钥；明文文件传 <see cref="WzNoOpKey.Instance"/>。</param>
    /// <returns>解密后的字符串。</returns>
    public string ReadString(IWzXorKey key)
    {
        sbyte size = this.ReadSByte();
        if (size == 0)
        {
            return string.Empty;
        }

        if (size > 0)
        {
            // UTF-16 段：count = 字符数；127 为逃逸哨兵。
            int charCount = size == 127 ? this.ReadInt32() : size;
            if (charCount < 0)
            {
                throw new WzFormatException($"非法的 UTF-16 字符串长度：{charCount}。");
            }

            byte[] raw = this.ReadBytes(checked(charCount * 2));
            key.Decrypt(raw, 0);

            char[] chars = new char[charCount];
            for (int i = 0; i < charCount; i++)
            {
                ushort value = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(i * 2, 2));
                chars[i] = (char)(value ^ unchecked((ushort)(0xAAAA + i)));
            }

            return new string(chars);
        }

        // ASCII/单字节段：n = 字节数；-128 为逃逸哨兵。
        int byteCount = size == -128 ? this.ReadInt32() : -size;
        if (byteCount < 0)
        {
            throw new WzFormatException($"非法的 ASCII 字符串长度：{byteCount}。");
        }

        byte[] ascii = this.ReadBytes(byteCount);
        key.Decrypt(ascii, 0);

        char[] result = new char[byteCount];
        for (int i = 0; i < byteCount; i++)
        {
            result[i] = (char)(ascii[i] ^ unchecked((byte)(0xAA + i)));
        }

        return new string(result);
    }

    /// <summary>
    /// 在指定偏移处读取字符串，然后恢复原位置。
    /// </summary>
    /// <param name="offset">相对于当前流起点的绝对偏移。</param>
    /// <param name="key">XOR 密钥。</param>
    /// <returns>字符串。</returns>
    public string ReadStringAt(long offset, IWzXorKey key)
    {
        long saved = this.Position;
        if (offset < 0 || offset >= this.Length)
        {
            throw new WzFormatException($"字符串偏移越界：{offset}（流长度 {this.Length}）。");
        }

        try
        {
            this.Position = offset;
            return this.ReadString(key);
        }
        finally
        {
            this.Position = saved;
        }
    }

    /// <summary>
    /// 读取 PKG2 目录名（KMST1198+）：负数长度表示 UTF-16 字符数，且<b>不施加</b> <c>0xAAAA</c> 掩码。
    /// </summary>
    /// <param name="key">PKG2 目录名专用密钥。</param>
    /// <returns>目录名。</returns>
    public string ReadPkg2DirString(IWzXorKey key)
    {
        sbyte size = this.ReadSByte();
        if (size == 0)
        {
            return string.Empty;
        }

        if (size > 0)
        {
            throw new WzFormatException($"PKG2 目录名长度必须为非正数，实际为 {size}。");
        }

        int charCount = -size;
        byte[] raw = this.ReadBytes(checked(charCount * 2));
        key.Decrypt(raw, 0);
        return Encoding.Unicode.GetString(raw);
    }

    /// <summary>
    /// 读取 img 顶层对象 tag：flag <c>0x73</c> 为内联字符串，<c>0x1B</c> 为偏移引用。
    /// </summary>
    /// <param name="key">XOR 密钥。</param>
    /// <returns>对象 tag（如 <c>Property</c>、<c>Canvas</c>）。</returns>
    public string ReadImageObjectTypeName(IWzXorKey key)
    {
        byte flag = this.ReadByte();
        return flag switch
        {
            0x73 => this.ReadString(key),
            0x1B => this.ReadStringAt(this.ReadInt32(), key),
            _ => throw new WzFormatException($"未知的 img 对象 tag 标志：0x{flag:X2}。"),
        };
    }

    /// <summary>
    /// 读取 img 内部字符串：flag <c>0x00</c> 内联、<c>0x01</c> 偏移引用、<c>0x04</c> 跳过 8 字节并返回 <c>null</c>。
    /// </summary>
    /// <param name="key">XOR 密钥。</param>
    /// <returns>字符串；<c>0x04</c> 情形返回 <c>null</c>。</returns>
    public string? ReadImageString(IWzXorKey key)
    {
        byte flag = this.ReadByte();
        switch (flag)
        {
            case 0x00:
                return this.ReadString(key);
            case 0x01:
                return this.ReadStringAt(this.ReadInt32(), key);
            case 0x04:
                this.Skip(8);
                return null;
            default:
                throw new WzFormatException($"未知的 img 字符串标志：0x{flag:X2}。");
        }
    }

    /// <summary>
    /// 只做“结构跳读”读取字符串负载（不解密），用于版本预扫描。
    /// </summary>
    /// <returns>原始负载与是否为 UTF-16 段。</returns>
    public (byte[] Payload, bool IsUtf16) ReadStringPayload()
    {
        sbyte size = this.ReadSByte();
        if (size == 0)
        {
            return (Array.Empty<byte>(), false);
        }

        if (size > 0)
        {
            int charCount = size == 127 ? this.ReadInt32() : size;
            return (this.ReadBytes(checked(charCount * 2)), true);
        }

        int byteCount = size == -128 ? this.ReadInt32() : -size;
        return (this.ReadBytes(byteCount), false);
    }

    /// <summary>跳过字符串（不读取内容，仅移动位置）。</summary>
    public void SkipString()
    {
        _ = this.ReadStringPayload();
    }

    /// <summary>跳过 PKG2 目录名（首条目风格，长度恒为非正数）。</summary>
    public void SkipPkg2DirString()
    {
        sbyte size = this.ReadSByte();
        if (size < 0)
        {
            this.Skip(-size * 2);
        }
        else if (size > 0)
        {
            throw new WzFormatException($"PKG2 目录名长度必须为非正数，实际为 {size}。");
        }
    }
}
