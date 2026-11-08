using WzReader.Core.Common;
using WzReader.Core.Diagnostics;

namespace WzReader.Core.Package;

/// <summary>
/// 预扫描得到的目录条目（只含结构字段，不含名称）。
/// </summary>
/// <remarks>
/// 预扫描的目的：在不解密名称的前提下确定目录树终点与所有 image 条目的
/// <c>(hashOffset 位置, hashOffset 值, size)</c>，用于版本哈希的边界校验。
/// </remarks>
public sealed class WzScanEntry
{
    /// <summary>节点类型：<c>0x02</c>/<c>0x04</c> 为 image，<c>0x03</c> 为目录。</summary>
    public byte NodeType { get; set; }

    /// <summary>img 块字节长度（目录条目忽略）。</summary>
    public int Size { get; set; }

    /// <summary>checksum（compressed int32）。</summary>
    public int Checksum { get; set; }

    /// <summary><c>hashOffset</c> 字段自身的<b>绝对文件位置</b>。</summary>
    /// <remarks>
    /// 实测校准：PKG1 的偏移公式必须代入绝对位置（代入相对 DataStart 的位置会全部越界）。
    /// PKG2 公式的基准尚无实测样本，<see cref="WzVersionDetector"/> 在校验时自行换算。
    /// </remarks>
    public long HashPosition { get; set; }

    /// <summary><c>hashOffset</c> 字段值。</summary>
    public uint HashedOffset { get; set; }

    /// <summary>子目录条目（仅 <c>0x03</c> 使用）。</summary>
    public List<WzScanEntry> Children { get; } = new();

    /// <summary>是否为 image 条目。</summary>
    public bool IsImage => this.NodeType is 0x02 or 0x04;

    /// <summary>是否为目录条目。</summary>
    public bool IsDirectory => this.NodeType == 0x03;
}

/// <summary>
/// 预扫描结果。
/// </summary>
public sealed class WzScanResult
{
    /// <summary>虚拟根（其 <see cref="WzScanEntry.Children"/> 为顶层条目）。</summary>
    public WzScanEntry Root { get; } = new() { NodeType = 0x03 };

    /// <summary>整个目录树结束后的绝对流位置（img 数据应从此处之后开始）。</summary>
    public long DirectoryEnd { get; set; }

    /// <summary>第一个内联字符串的原始负载（用于密钥探测）。</summary>
    public byte[]? FirstStringPayload { get; set; }

    /// <summary>第一个内联字符串是否为 UTF-16。</summary>
    public bool FirstStringUtf16 { get; set; }

    /// <summary>展平后的所有 image 条目。</summary>
    public List<WzScanEntry> Images { get; } = new();
}

/// <summary>
/// 目录树的结构化预扫描器。
/// </summary>
/// <remarks>
/// <para>
/// WZ 的字符串长度前缀是自描述的，因此<b>不解密也能跳过</b>名称并准确得到每条目字段。
/// 预扫描利用这一点，先拿到目录树终点、所有 image 的 (位置, hashOffset, size)，
/// 再用这些信息验证候选 hashVersion，最后才做真正的解密读取。
/// </para>
/// <para>
/// 该方法也用于 PKG2：PKG2 的条目计数是加密的，因此必须先用 V1 profile 解出计数再扫描。
/// 若后续需要支持 KMST1198+ 的 PKG2 profile，可在此扩展新的计数解密与名称跳读策略。
/// </para>
/// </remarks>
public static class WzDirectoryScanner
{
    private const int MaxEntriesPerLevel = 1_000_000;
    private const int MaxTotalEntries = 4_000_000;

    private sealed class ScanContext
    {
        public byte[]? FirstPayload;
        public bool FirstUtf16;
        public bool Captured;
        public int TotalEntries;
    }

    /// <summary>扫描 PKG1 目录树。</summary>
    /// <param name="reader">文件读取器。</param>
    /// <param name="header">已解析的头。</param>
    /// <param name="maximumDepth">最大递归深度。</param>
    /// <returns>扫描结果。</returns>
    public static WzScanResult ScanPkg1(WzBinaryReader reader, WzPackageHeader header, int maximumDepth)
    {
        ScanContext context = new();
        WzScanResult result = new();

        reader.Position = header.DataStart;
        ScanPkg1Level(reader, result.Root, header.DataStart, context, result, 0, maximumDepth);

        result.DirectoryEnd = reader.Position;
        result.FirstStringPayload = context.FirstPayload;
        result.FirstStringUtf16 = context.FirstUtf16;
        return result;
    }

    /// <summary>扫描 PKG2 目录树（V1 profile）。</summary>
    /// <param name="reader">文件读取器。</param>
    /// <param name="header">已解析的头。</param>
    /// <param name="hashVersion">已反解的 hashVersion。</param>
    /// <param name="maximumDepth">最大递归深度。</param>
    /// <returns>扫描结果。</returns>
    public static WzScanResult ScanPkg2(
        WzBinaryReader reader,
        WzPackageHeader header,
        uint hashVersion,
        int maximumDepth)
    {
        ScanContext context = new();
        WzScanResult result = new();

        reader.Position = header.DataStart;
        ScanPkg2Level(reader, result.Root, header.DataStart, header, hashVersion, context, result, 0, maximumDepth);

        result.DirectoryEnd = reader.Position;
        result.FirstStringPayload = context.FirstPayload;
        result.FirstStringUtf16 = context.FirstUtf16;
        return result;
    }

    private static void ScanPkg1Level(
        WzBinaryReader reader,
        WzScanEntry parent,
        long dataStart,
        ScanContext context,
        WzScanResult result,
        int depth,
        int maximumDepth)
    {
        if (depth > maximumDepth)
        {
            throw new WzFormatException($"目录树递归深度超过 {maximumDepth}。");
        }

        int count = reader.ReadCompressedInt32();
        if (count < 0 || count > MaxEntriesPerLevel)
        {
            throw new WzFormatException($"非法的目录条目数：{count}。");
        }

        context.TotalEntries += count;
        if (context.TotalEntries > MaxTotalEntries)
        {
            throw new WzFormatException("目录条目总数异常，文件可能损坏。");
        }

        List<WzScanEntry> directories = new(count);
        for (int i = 0; i < count; i++)
        {
            byte nodeType = reader.ReadByte();
            switch (nodeType)
            {
                case 0x02:
                    // 名称存放在文件其他位置，预扫描只需跳过这个 int32 偏移。
                    _ = reader.ReadInt32();
                    break;
                case 0x03:
                case 0x04:
                    if (!context.Captured)
                    {
                        (byte[] payload, bool isUtf16) = reader.ReadStringPayload();
                        context.FirstPayload = payload;
                        context.FirstUtf16 = isUtf16;
                        context.Captured = true;
                    }
                    else
                    {
                        reader.SkipString();
                    }

                    break;
                default:
                    throw new WzFormatException($"非法的目录条目类型：0x{nodeType:X2}（位置 {reader.Position}）。");
            }

            int size = reader.ReadCompressedInt32();
            int checksum = reader.ReadCompressedInt32();
            long hashPosition = reader.Position;
            uint hashedOffset = reader.ReadUInt32();

            WzScanEntry entry = new()
            {
                NodeType = nodeType,
                Size = size,
                Checksum = checksum,
                HashPosition = hashPosition,
                HashedOffset = hashedOffset,
            };

            parent.Children.Add(entry);
            if (entry.IsImage)
            {
                result.Images.Add(entry);
            }
            else if (entry.IsDirectory)
            {
                directories.Add(entry);
            }
        }

        // 子目录内容紧跟本层所有条目之后，按出现顺序递归。
        foreach (WzScanEntry directory in directories)
        {
            ScanPkg1Level(reader, directory, dataStart, context, result, depth + 1, maximumDepth);
        }
    }

    private static void ScanPkg2Level(
        WzBinaryReader reader,
        WzScanEntry parent,
        long dataStart,
        WzPackageHeader header,
        uint hashVersion,
        ScanContext context,
        WzScanResult result,
        int depth,
        int maximumDepth)
    {
        if (depth > maximumDepth)
        {
            throw new WzFormatException($"目录树递归深度超过 {maximumDepth}。");
        }

        int encryptedCount = reader.ReadCompressedInt32();
        int count = WzOffsetCalculator.DecryptEntryCountPkg2V1(encryptedCount, header.Hash1, hashVersion);
        if (count < 0 || count > MaxEntriesPerLevel)
        {
            throw new WzFormatException($"非法的 PKG2 目录条目数：{count}（加密值 {encryptedCount}）。");
        }

        context.TotalEntries += count;
        if (context.TotalEntries > MaxTotalEntries)
        {
            throw new WzFormatException("目录条目总数异常，文件可能损坏。");
        }

        List<WzScanEntry> entries = new(count);
        List<WzScanEntry> directories = new();

        for (int i = 0; i < count; i++)
        {
            byte nodeType = reader.ReadByte();
            if (nodeType != 0x03 && nodeType != 0x04)
            {
                throw new WzFormatException($"非法的 PKG2 目录条目类型：0x{nodeType:X2}。");
            }

            if (!context.Captured)
            {
                (byte[] payload, bool isUtf16) = reader.ReadStringPayload();
                context.FirstPayload = payload;
                context.FirstUtf16 = isUtf16;
                context.Captured = true;
            }
            else
            {
                reader.SkipString();
            }

            int entrySize = reader.ReadCompressedInt32();
            int checksum = reader.ReadCompressedInt32();

            WzScanEntry entry = new()
            {
                NodeType = nodeType,
                Size = entrySize,
                Checksum = checksum,
            };
            entries.Add(entry);
        }

        int encryptedOffsetCount = reader.ReadCompressedInt32();
        if (encryptedOffsetCount == encryptedCount && entries.Count > 0)
        {
            foreach (WzScanEntry entry in entries)
            {
                entry.HashPosition = reader.Position;
                entry.HashedOffset = reader.ReadUInt32();
                parent.Children.Add(entry);
                if (entry.IsImage)
                {
                    result.Images.Add(entry);
                }
                else
                {
                    directories.Add(entry);
                }
            }
        }
        else
        {
            throw new WzFormatException(
                $"PKG2 的 offset 数量字段与条目数不一致（{encryptedOffsetCount} != {encryptedCount}）。");
        }

        foreach (WzScanEntry directory in directories)
        {
            ScanPkg2Level(reader, directory, dataStart, header, hashVersion, context, result, depth + 1, maximumDepth);
        }
    }
}
