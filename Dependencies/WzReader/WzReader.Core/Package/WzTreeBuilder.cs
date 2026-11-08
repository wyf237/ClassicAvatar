using WzReader.Core.Common;
using WzReader.Core.Crypto;
using WzReader.Core.Diagnostics;
using WzReader.Core.Dom;

namespace WzReader.Core.Package;

/// <summary>
/// 目录树“解密读取”构建器：把预扫描确认过的结构读取成真实的 <see cref="WzNode"/> 树。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="WzDirectoryScanner"/> 的分工：扫描器只跳读结构、确定版本；
/// 本构建器使用已确定的 key 与 hashVersion 逐字段读取名称并创建节点。
/// </para>
/// <para>
/// 目录条目的子节点紧跟在本层所有条目之后，按目录条目出现顺序递归，因此必须“先读完本层，
/// 再递归子目录”，否则位置会错。
/// </para>
/// </remarks>
internal static class WzTreeBuilder
{
    /// <summary>构建 PKG1 目录树（返回临时根节点）。</summary>
    /// <param name="reader">文件读取器。</param>
    /// <param name="header">文件头。</param>
    /// <param name="hashVersion">已确认的 hashVersion。</param>
    /// <param name="key">字符串密钥。</param>
    /// <param name="owner">所属包文件。</param>
    /// <param name="diagnostics">诊断收集器。</param>
    /// <returns>临时根节点（子节点为顶层条目）。</returns>
    public static WzNode BuildPkg1(
        WzBinaryReader reader,
        WzPackageHeader header,
        uint hashVersion,
        IWzXorKey key,
        WzPackageFile owner,
        WzDiagnostics diagnostics)
    {
        WzNode root = new(string.Empty, null, WzProperty.Instance);
        reader.Position = header.DataStart;
        BuildPkg1Level(reader, root, header, hashVersion, key, owner, diagnostics, 0);
        return root;
    }

    private static void BuildPkg1Level(
        WzBinaryReader reader,
        WzNode parent,
        WzPackageHeader header,
        uint hashVersion,
        IWzXorKey key,
        WzPackageFile owner,
        WzDiagnostics diagnostics,
        int depth)
    {
        if (depth > 64)
        {
            throw new WzFormatException("目录树递归深度超过 64。");
        }

        int count = reader.ReadCompressedInt32();
        if (count < 0 || count > 1_000_000)
        {
            throw new WzFormatException($"非法的目录条目数：{count}。");
        }

        List<WzNode> directories = new(count);

        for (int i = 0; i < count; i++)
        {
            byte nodeType = reader.ReadByte();
            string name;

            switch (nodeType)
            {
                case 0x02:
                {
                    int nameOffset = reader.ReadInt32();
                    // 历史兼容修正：encver 缺失时 +2，否则 -1。
                    int correction = header.IsEncverMissing ? 2 : -1;
                    name = reader.ReadStringAt(nameOffset + correction, key);
                    break;
                }

                case 0x03:
                case 0x04:
                    name = reader.ReadString(key);
                    break;

                default:
                    throw new WzFormatException($"非法的目录条目类型：0x{nodeType:X2}。");
            }

            int size = reader.ReadCompressedInt32();
            int checksum = reader.ReadCompressedInt32();
            // PKG1 偏移公式需要绝对位置，这里不要减 DataStart。
            long hashPosition = reader.Position;
            uint hashedOffset = reader.ReadUInt32();

            if (nodeType == 0x03)
            {
                WzNode directory = new(name, parent, WzProperty.Instance);
                parent.AddChild(directory);
                directories.Add(directory);
            }
            else
            {
                uint offset = WzOffsetCalculator.CalcPkg1(
                    (uint)hashPosition,
                    hashedOffset,
                    (uint)header.HeaderSize,
                    hashVersion);

                if ((long)offset + size > reader.Length)
                {
                    diagnostics.Warn(
                        $"img 块超出文件边界（offset={offset}, size={size}），节点仍保留但解析可能失败。",
                        name);
                }

                WzNode image = new(name, parent, null);
                image.MarkImagePending(new WzImageInfo(owner, offset, size, checksum));
                parent.AddChild(image);
            }
        }

        foreach (WzNode directory in directories)
        {
            BuildPkg1Level(reader, directory, header, hashVersion, key, owner, diagnostics, depth + 1);
        }
    }

    /// <summary>构建 PKG2 目录树（V1 profile）。</summary>
    /// <param name="reader">文件读取器。</param>
    /// <param name="header">文件头。</param>
    /// <param name="hashVersion">已反解的 hashVersion。</param>
    /// <param name="propertyKey">属性名使用的 PKG1 密钥。</param>
    /// <param name="dirKey">首条目目录名使用的 PKG2 密钥。</param>
    /// <param name="owner">所属包文件。</param>
    /// <param name="diagnostics">诊断收集器。</param>
    /// <returns>临时根节点。</returns>
    public static WzNode BuildPkg2(
        WzBinaryReader reader,
        WzPackageHeader header,
        uint hashVersion,
        IWzXorKey propertyKey,
        IWzXorKey dirKey,
        WzPackageFile owner,
        WzDiagnostics diagnostics)
    {
        WzNode root = new(string.Empty, null, WzProperty.Instance);
        reader.Position = header.DataStart;
        BuildPkg2Level(reader, root, header, hashVersion, propertyKey, dirKey, owner, diagnostics, 0);
        return root;
    }

    private static void BuildPkg2Level(
        WzBinaryReader reader,
        WzNode parent,
        WzPackageHeader header,
        uint hashVersion,
        IWzXorKey propertyKey,
        IWzXorKey dirKey,
        WzPackageFile owner,
        WzDiagnostics diagnostics,
        int depth)
    {
        if (depth > 64)
        {
            throw new WzFormatException("目录树递归深度超过 64。");
        }

        int encryptedCount = reader.ReadCompressedInt32();
        int count = WzOffsetCalculator.DecryptEntryCountPkg2V1(encryptedCount, header.Hash1, hashVersion);
        if (count < 0 || count > 1_000_000)
        {
            throw new WzFormatException($"非法的 PKG2 目录条目数：{count}。");
        }

        List<(byte Type, string Name, int Size, int Checksum)> pending = new(count);
        for (int i = 0; i < count; i++)
        {
            byte nodeType = reader.ReadByte();
            if (nodeType != 0x03 && nodeType != 0x04)
            {
                throw new WzFormatException($"非法的 PKG2 目录条目类型：0x{nodeType:X2}。");
            }

            // 当前仅支持 KMST1196/1197（V1 profile）：所有目录名都按普通字符串读取。
            // KMST1198+ 的首条目需改用 ReadPkg2DirString（无掩码）——在扩展 profile 时再分支。
            string name = reader.ReadString(propertyKey);
            int size = reader.ReadCompressedInt32();
            int checksum = reader.ReadCompressedInt32();
            pending.Add((nodeType, name, size, checksum));
        }

        int encryptedOffsetCount = reader.ReadCompressedInt32();
        if (encryptedOffsetCount != encryptedCount)
        {
            throw new WzFormatException("PKG2 的 offset 数量字段与条目数不一致。");
        }

        List<WzNode> directories = new();
        foreach ((byte type, string name, int size, int checksum) in pending)
        {
            long hashPosition = reader.Position - header.DataStart;
            uint hashedOffset = reader.ReadUInt32();
            uint offset = WzOffsetCalculator.CalcPkg2V1(
                (uint)hashPosition,
                hashedOffset,
                (uint)header.HeaderSize,
                header.Hash1,
                hashVersion);

            if (type == 0x03)
            {
                WzNode directory = new(name, parent, WzProperty.Instance);
                parent.AddChild(directory);
                directories.Add(directory);
            }
            else
            {
                if ((long)offset + size > reader.Length)
                {
                    diagnostics.Warn($"PKG2 img 块超出文件边界（offset={offset}, size={size}）。", name);
                }

                WzNode image = new(name, parent, null);
                image.MarkImagePending(new WzImageInfo(owner, offset, size, checksum));
                parent.AddChild(image);
            }
        }

        foreach (WzNode directory in directories)
        {
            BuildPkg2Level(reader, directory, header, hashVersion, propertyKey, dirKey, owner, diagnostics, depth + 1);
        }
    }
}
