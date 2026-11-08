using System.Buffers.Binary;
using WzReader.Core.Codecs;
using WzReader.Core.Common;
using WzReader.Core.Crypto;
using WzReader.Core.Diagnostics;
using WzReader.Core.Dom;

namespace WzReader.Core.Parsing;

/// <summary>
/// img 块解析器：把一段 img 字节流解析成 <see cref="WzNode"/> 属性树。
/// </summary>
/// <remarks>
/// <para>
/// 解析规则（实测）：
/// <list type="bullet">
/// <item>顶层是“tag + 对象”，tag 是字符串（<c>Property</c>/<c>Canvas</c>/<c>UOL</c> 等），不是数字常量；</item>
/// <item><c>Property</c> 先跳过 2 字节，再读压缩整数计数，随后是若干“名称 + 类型 flag + 值”；</item>
/// <item>类型 <c>0x09</c> 的值是“int32 长度 + 一个完整对象”，不是子项计数；</item>
/// <item>画布/声音/视频可以在数据前携带一个“mini Property”子属性块；</item>
/// <item>所有名称与字符串值都通过 <see cref="WzBinaryReader.ReadImageString"/> 读取（再带一层 flag）。</item>
/// </list>
/// </para>
/// <para>
/// 本类不做链接解析：<c>_inlink</c>/<c>_outlink</c>/UOL 都按普通值保存，解析交给 <c>WzLinkResolver</c>。
/// </para>
/// </remarks>
public static class WzImageParser
{
    private static readonly Guid MediaTypeStream = new("e436eb83-524f-11ce-9f53-0020af0ba770");
    private static readonly Guid MediaSubtypeMpeg1Audio = new("e436eb87-524f-11ce-9f53-0020af0ba770");
    private static readonly Guid MediaSubtypeWave = new("e436eb8b-524f-11ce-9f53-0020af0ba770");

    /// <summary>
    /// 解析 img 字节并把结果写入指定节点。
    /// </summary>
    /// <param name="node">目标节点（名称/父节点已设置）。</param>
    /// <param name="imageBytes">img 块完整字节。</param>
    /// <param name="key">img 字符串密钥。</param>
    /// <param name="diagnostics">诊断收集器。</param>
    public static void Parse(WzNode node, byte[] imageBytes, IWzXorKey key, WzDiagnostics? diagnostics)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(imageBytes);

        using MemoryStream stream = new(imageBytes, writable: false);
        WzBinaryReader reader = new(stream);

        ReadObject(reader, node, key, diagnostics);
        node.MarkParsed();
    }

    /// <summary>
    /// 读取一个“tag + 对象”，并填充到节点。
    /// </summary>
    /// <param name="reader">img 流读取器。</param>
    /// <param name="node">目标节点。</param>
    /// <param name="key">字符串密钥。</param>
    /// <param name="diagnostics">诊断收集器。</param>
    public static void ReadObject(WzBinaryReader reader, WzNode node, IWzXorKey key, WzDiagnostics? diagnostics)
    {
        string tag = reader.ReadImageObjectTypeName(key);
        switch (tag)
        {
            case "Property":
                node.Value = WzProperty.Instance;
                ReadPropertyBody(reader, node, key, diagnostics);
                break;

            case "Shape2D#Vector2D":
            {
                int x = reader.ReadCompressedInt32();
                int y = reader.ReadCompressedInt32();
                node.Value = new WzVector(x, y);
                break;
            }

            case "Shape2D#Convex2D":
            {
                int count = reader.ReadCompressedInt32();
                List<WzVector> points = new(count);
                for (int i = 0; i < count; i++)
                {
                    WzNode temporary = new("point", null, null);
                    ReadObject(reader, temporary, key, diagnostics);
                    if (temporary.Value is WzVector vector)
                    {
                        points.Add(vector);
                    }
                    else
                    {
                        diagnostics?.Warn("Convex2D 的子项不是 Vector2D，已跳过。", node.Name);
                    }
                }

                node.Value = new WzConvex(points);
                break;
            }

            case "Canvas":
                node.Value = ReadCanvas(reader, node, key, diagnostics);
                break;

            case "Sound_DX8":
                node.Value = ReadSound(reader, node, key, diagnostics);
                break;

            case "UOL":
            {
                reader.Skip(1);
                string uolPath = reader.ReadImageString(key) ?? string.Empty;
                node.Value = new WzUol(uolPath);
                break;
            }

            case "RawData":
                node.Value = ReadRawData(reader, node, key, diagnostics, isVideo: false);
                break;

            case "Canvas#Video":
                node.Value = ReadRawData(reader, node, key, diagnostics, isVideo: true);
                break;

            default:
                throw new WzFormatException($"未知的 img 对象 tag：{tag}（位置 {reader.Position}）。");
        }
    }

    private static void ReadPropertyBody(
        WzBinaryReader reader,
        WzNode node,
        IWzXorKey key,
        WzDiagnostics? diagnostics)
    {
        reader.Skip(2);
        int count = reader.ReadCompressedInt32();
        if (count < 0 || count > 10_000_000)
        {
            throw new WzFormatException($"非法的属性条目数：{count}。");
        }

        for (int i = 0; i < count; i++)
        {
            ReadValueNode(reader, node, key, diagnostics);
        }
    }

    private static WzNode ReadValueNode(
        WzBinaryReader reader,
        WzNode parent,
        IWzXorKey key,
        WzDiagnostics? diagnostics)
    {
        string name = reader.ReadImageString(key) ?? string.Empty;
        byte flag = reader.ReadByte();
        WzNode node = new(name, parent, null);
        parent.AddChild(node);

        switch (flag)
        {
            case 0x00: // Null
                node.Value = null;
                break;

            case 0x02: // Int16
                node.Value = reader.ReadInt16();
                break;

            case 0x0B: // Int16（布尔语义，但读法相同）
                node.Value = reader.ReadInt16();
                break;

            case 0x03: // Compressed Int32
                node.Value = reader.ReadCompressedInt32();
                break;

            case 0x13: // Compressed Int32（无符号负载的常见写法）
                node.Value = reader.ReadCompressedInt32();
                break;

            case 0x04: // Compressed Float
                node.Value = reader.ReadCompressedSingle();
                break;

            case 0x05: // Double
                node.Value = reader.ReadDouble();
                break;

            case 0x14: // Compressed Int64
                node.Value = reader.ReadCompressedInt64();
                break;

            case 0x08: // String（再带一层 flag）
                node.Value = reader.ReadImageString(key);
                break;

            case 0x09: // Extended Property：int32 长度 + 完整对象
            {
                int length = reader.ReadInt32();
                long end = reader.Position + length;
                if (length < 0 || end > reader.Length)
                {
                    throw new WzFormatException($"0x09 属性的长度越界：{length}（位置 {reader.Position}）。");
                }

                ReadObject(reader, node, key, diagnostics);
                if (reader.Position != end)
                {
                    diagnostics?.Warn(
                        $"0x09 属性解析后位置与声明长度不一致，已强制对齐（多出 {end - reader.Position} 字节）。",
                        node.Path);
                    reader.Position = end;
                }

                break;
            }

            default:
                throw new WzFormatException($"未知的属性值类型 flag：0x{flag:X2}（属性 {name}）。");
        }

        return node;
    }

    private static WzCanvas ReadCanvas(
        WzBinaryReader reader,
        WzNode node,
        IWzXorKey key,
        WzDiagnostics? diagnostics)
    {
        reader.Skip(1);
        byte hasSub = reader.ReadByte();
        if (hasSub == 0x01)
        {
            reader.Skip(2);
            int subCount = reader.ReadCompressedInt32();
            for (int i = 0; i < subCount; i++)
            {
                ReadValueNode(reader, node, key, diagnostics);
            }
        }

        int width = reader.ReadCompressedInt32();
        int height = reader.ReadCompressedInt32();
        int formatValue = reader.ReadCompressedInt32();
        int scale = reader.ReadByte();
        int pages = reader.ReadCompressedInt32();
        int unknown1 = reader.ReadCompressedInt32();
        reader.Skip(2);

        int dataLength = reader.ReadInt32();
        if (dataLength < 0 || reader.Position + dataLength > reader.Length)
        {
            throw new WzFormatException($"画布像素数据长度越界：{dataLength}（位置 {reader.Position}）。");
        }

        byte[] data = reader.ReadBytes(dataLength);
        WzPixelFormat format = (WzPixelFormat)formatValue;
        if (!Enum.IsDefined(format) && formatValue != 0)
        {
            diagnostics?.Warn($"画布使用了未登记的像素格式编号 {formatValue}。", node.Path);
        }

        return new WzCanvas(width, height, format, scale, pages, unknown1, data, key, diagnostics);
    }

    private static WzSound ReadSound(
        WzBinaryReader reader,
        WzNode node,
        IWzXorKey key,
        WzDiagnostics? diagnostics)
    {
        byte version = reader.ReadByte();
        if (version == 1)
        {
            byte hasSub = reader.ReadByte();
            if (hasSub == 0x01)
            {
                reader.Skip(2);
                int subCount = reader.ReadCompressedInt32();
                for (int i = 0; i < subCount; i++)
                {
                    ReadValueNode(reader, node, key, diagnostics);
                }
            }
        }

        int dataLength = reader.ReadCompressedInt32();
        int durationMs = reader.ReadCompressedInt32();
        byte soundDecl = reader.ReadByte();

        Guid majorType = new(reader.ReadBytes(16));
        Guid subType = new(reader.ReadBytes(16));
        _ = reader.ReadByte() != 0; // fixedSizeSamples
        _ = reader.ReadByte() != 0; // temporalCompression
        _ = new Guid(reader.ReadBytes(16)); // formatType

        WzSoundFormat? format = null;
        if (soundDecl == 2)
        {
            int formatLength = reader.ReadCompressedInt32();
            byte[] formatRaw = reader.ReadBytes(formatLength);
            format = TryParseSoundFormat(formatRaw, diagnostics, node.Path);
        }

        if (dataLength < 0 || reader.Position + dataLength > reader.Length)
        {
            throw new WzFormatException($"声音负载长度越界：{dataLength}（位置 {reader.Position}）。");
        }

        byte[] data = reader.ReadBytes(dataLength);
        WzSoundKind kind = DetermineSoundKind(majorType, subType, format);
        return new WzSound(data, kind, durationMs, format);
    }

    private static WzSoundKind DetermineSoundKind(Guid majorType, Guid subType, WzSoundFormat? format)
    {
        if (majorType != MediaTypeStream)
        {
            return WzSoundKind.Unknown;
        }

        if (subType == MediaSubtypeMpeg1Audio)
        {
            return WzSoundKind.Mp3;
        }

        if (subType != MediaSubtypeWave || format is null)
        {
            return WzSoundKind.Unknown;
        }

        return format.Value.FormatTag switch
        {
            0x0055 => WzSoundKind.Mp3,
            0x0001 => WzSoundKind.Pcm,
            _ => WzSoundKind.Unknown,
        };
    }

    private static WzSoundFormat? TryParseSoundFormat(
        byte[] encrypted,
        WzDiagnostics? diagnostics,
        string context)
    {
        if (encrypted.Length < 18)
        {
            return null;
        }

        WzKeyType[] candidates = { WzKeyType.Bms, WzKeyType.Kms, WzKeyType.Gms };
        foreach (WzKeyType candidate in candidates)
        {
            byte[] plain = (byte[])encrypted.Clone();
            WzKeyFactory.Get(candidate).Decrypt(plain, 0);

            ushort formatTag = BinaryPrimitives.ReadUInt16LittleEndian(plain);
            ushort channels = BinaryPrimitives.ReadUInt16LittleEndian(plain.AsSpan(2));
            uint samplesPerSec = BinaryPrimitives.ReadUInt32LittleEndian(plain.AsSpan(4));
            uint avgBytesPerSec = BinaryPrimitives.ReadUInt32LittleEndian(plain.AsSpan(8));
            ushort blockAlign = BinaryPrimitives.ReadUInt16LittleEndian(plain.AsSpan(12));
            ushort bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(plain.AsSpan(14));
            ushort cbSize = BinaryPrimitives.ReadUInt16LittleEndian(plain.AsSpan(16));

            bool lengthMatches = plain.Length == cbSize + 18 ||
                                 (plain.Length == cbSize && formatTag == 0x0055);
            if (!lengthMatches)
            {
                continue;
            }

            if (formatTag != 0x0001 && formatTag != 0x0055)
            {
                continue;
            }

            diagnostics?.Info($"声音 fmt 使用 {candidate} 密钥解密成功。", context);
            return new WzSoundFormat(
                formatTag,
                channels,
                samplesPerSec,
                avgBytesPerSec,
                blockAlign,
                bitsPerSample,
                cbSize,
                plain);
        }

        diagnostics?.Warn("声音 fmt 无法用 BMS/KMS/GMS 任一密钥解密，已忽略格式信息。", context);
        return null;
    }

    private static WzRawData ReadRawData(
        WzBinaryReader reader,
        WzNode node,
        IWzXorKey key,
        WzDiagnostics? diagnostics,
        bool isVideo)
    {
        byte version = reader.ReadByte();

        if (!isVideo)
        {
            if (version == 1)
            {
                long mark = reader.Position;
                if (reader.ReadByte() == 0x01)
                {
                    reader.Skip(1);
                    int subCount = reader.ReadCompressedInt32();
                    for (int i = 0; i < subCount; i++)
                    {
                        ReadValueNode(reader, node, key, diagnostics);
                    }
                }
                else
                {
                    reader.Position = mark;
                }
            }
        }
        else
        {
            reader.Skip(1); // Canvas#Video 自带 1 字节占位
            long mark = reader.Position;
            if (reader.ReadByte() == 0x01)
            {
                reader.Skip(1);
                int subCount = reader.ReadCompressedInt32();
                for (int i = 0; i < subCount; i++)
                {
                    ReadValueNode(reader, node, key, diagnostics);
                }
            }
            else
            {
                reader.Position = mark;
            }

            version = reader.ReadByte();
        }

        int length = reader.ReadCompressedInt32();
        if (length < 0 || reader.Position + length > reader.Length)
        {
            throw new WzFormatException($"RawData 负载长度越界：{length}。");
        }

        byte[] data = reader.ReadBytes(length);
        return new WzRawData(data, version, isVideo);
    }
}
