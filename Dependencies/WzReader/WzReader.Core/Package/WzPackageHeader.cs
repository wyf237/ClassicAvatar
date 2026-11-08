using System.Text;
using WzReader.Core.Common;
using WzReader.Core.Diagnostics;

namespace WzReader.Core.Package;

/// <summary>WZ 容器格式。</summary>
public enum WzPackageFormat
{
    /// <summary>未知。</summary>
    Unknown = 0,

    /// <summary>经典 PKG1（带 encver 或 777 哨兵版本）。</summary>
    Pkg1 = 1,

    /// <summary>PKG2（KMST1196+，目录条目计数加密）。</summary>
    Pkg2 = 2,
}

/// <summary>
/// WZ 文件头。负责解析 PKG1/PKG2 签名、数据区大小、头部长度、版权字段与版本相关参数。
/// </summary>
/// <remarks>
/// <para>
/// PKG1 头结构：<c>"PKG1" | int64 dataSize | int32 headerSize | copyright[headerSize-16] | uint16 encver?</c>。
/// encver 只有在“数据区至少 2 字节、值不超过 0xFF、且不满足 0x80 特殊歧义”时才存在；
/// 缺失时使用固定哨兵版本 777（其 hashVersion 为 59192）。
/// </para>
/// <para>
/// PKG2 头结构：<c>"PKG2" | int64 dataSize | int32 headerSize | copyright[...] | uint32 hash1 | uint32 hash2</c>，
/// 没有 encver，hashVersion 必须由 hash1/hash2 反解。
/// </para>
/// <para>
/// 本类只解析头部字段，不做密钥/版本探测；后者由 <see cref="WzVersionDetector"/> 完成。
/// </para>
/// </remarks>
public sealed class WzPackageHeader
{
    private WzPackageHeader()
    {
    }

    /// <summary>容器格式。</summary>
    public WzPackageFormat Format { get; private set; }

    /// <summary>4 字节签名（PKG1 / PKG2）。</summary>
    public string Signature { get; private set; } = string.Empty;

    /// <summary>头部记录的数据区长度（仅用于 encver 判定，不校验与文件长度一致）。</summary>
    public long DataSize { get; private set; }

    /// <summary>头部总字节数（含 copyright），也是偏移公式中的 headerLen。</summary>
    public int HeaderSize { get; private set; }

    /// <summary>版权字符串（ASCII，已在第一个 NUL 处截断）。</summary>
    public string Copyright { get; private set; } = string.Empty;

    /// <summary>PKG1 的 encver；缺失或 PKG2 时为 <c>null</c>。</summary>
    public int? EncryptedVersion { get; private set; }

    /// <summary>是否缺少 encver（KMST1132+ 或数据区过短）。</summary>
    public bool IsEncverMissing { get; private set; }

    /// <summary>目录树起点（PKG1 = headerSize + encver?2:0；PKG2 = headerSize + 8）。</summary>
    public long DataStart { get; private set; }

    /// <summary>PKG2 的 hash1；PKG1 为 0。</summary>
    public uint Hash1 { get; private set; }

    /// <summary>PKG2 的 hash2；PKG1 为 0。</summary>
    public uint Hash2 { get; private set; }

    /// <summary>探测出的 hashVersion（PKG1 由 encver 反推，PKG2 由 profile 反解）。</summary>
    public uint HashVersion { get; internal set; }

    /// <summary>探测出的客户端版本；encver 缺失时为 777 哨兵值。</summary>
    public int WzVersion { get; internal set; }

    /// <summary>标识版本是否已成功探测（仅用于诊断输出）。</summary>
    public bool VersionResolved { get; internal set; }

    /// <summary>
    /// 读取并校验文件头。
    /// </summary>
    /// <param name="reader">位于文件起点的读取器。</param>
    /// <returns>解析结果。</returns>
    /// <exception cref="WzNotWzException">签名不是 PKG1/PKG2。</exception>
    /// <exception cref="WzFormatException">头字段非法或越界。</exception>
    public static WzPackageHeader Read(WzBinaryReader reader)
    {
        reader.Position = 0;
        if (reader.Length < 16)
        {
            throw new WzNotWzException($"文件长度 {reader.Length} 小于最小 WZ 头长度 16。");
        }

        string signature = Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (signature != "PKG1" && signature != "PKG2")
        {
            throw new WzNotWzException($"文件签名不是 PKG1/PKG2（实际为“{signature}”）。");
        }

        long dataSize = reader.ReadInt64();
        int headerSize = reader.ReadInt32();
        if (headerSize <= 16 || headerSize > reader.Length)
        {
            throw new WzFormatException($"非法 headerSize：{headerSize}（文件长度 {reader.Length}）。");
        }

        // copyright：恰好 headerSize-16 字节，按 ASCII 解码；内部保留定长语义，对外在 NUL 处截断。
        byte[] copyrightRaw = reader.ReadBytes(headerSize - 16);
        string copyright = DecodeCopyright(copyrightRaw);

        WzPackageHeader header = new()
        {
            Format = signature == "PKG1" ? WzPackageFormat.Pkg1 : WzPackageFormat.Pkg2,
            Signature = signature,
            DataSize = dataSize,
            HeaderSize = headerSize,
            Copyright = copyright,
        };

        if (header.Format == WzPackageFormat.Pkg1)
        {
            header.ReadPkg1VersionFields(reader);
        }
        else
        {
            reader.Position = headerSize;
            header.Hash1 = reader.ReadUInt32();
            header.Hash2 = reader.ReadUInt32();
            header.DataStart = headerSize + 8L;

            // PKG2 没有 encver；版本信息由 hash1/hash2 反解。
            header.IsEncverMissing = true;
            header.EncryptedVersion = null;
        }

        if (header.DataStart > reader.Length)
        {
            throw new WzFormatException($"目录起点 {header.DataStart} 超出文件长度 {reader.Length}。");
        }

        return header;
    }

    private void ReadPkg1VersionFields(WzBinaryReader reader)
    {
        int? encver = null;
        bool missing = false;

        if (this.DataSize < 2)
        {
            // 数据区只有 0/1 字节，不可能存在 encver。
            missing = true;
        }
        else
        {
            reader.Position = this.HeaderSize;
            int rawValue = reader.ReadInt16();
            int value = rawValue & 0xFFFF;
            if (value > 0xFF)
            {
                // encver 恒小于 256，两字节读出大于 0xFF 说明这里已是目录数据。
                missing = true;
            }
            else if (value == 0x80)
            {
                // 0x80 歧义：可能是 encver=0x80，也可能是压缩整数属性计数（count 为 256 的倍数）。
                if (this.DataSize >= 5)
                {
                    reader.Position = this.HeaderSize;
                    int propertyCount = reader.ReadCompressedInt32();
                    if (propertyCount > 0 && (propertyCount & 0xFF) == 0 && propertyCount <= 0xFFFF)
                    {
                        missing = true;
                    }
                    else
                    {
                        encver = value;
                    }
                }
                else
                {
                    // 无法进一步判定，按“存在 encver”处理。
                    encver = value;
                }
            }
            else
            {
                encver = value;
            }
        }

        this.IsEncverMissing = missing;
        this.EncryptedVersion = encver;
        this.DataStart = this.HeaderSize + (missing ? 0L : 2L);
    }

    private static string DecodeCopyright(byte[] raw)
    {
        int length = Array.IndexOf(raw, (byte)0);
        if (length < 0)
        {
            length = raw.Length;
        }

        return Encoding.ASCII.GetString(raw, 0, length);
    }

    /// <summary>返回便于日志输出的单行摘要。</summary>
    /// <returns>摘要字符串。</returns>
    public override string ToString()
    {
        return this.Format == WzPackageFormat.Pkg1
            ? $"PKG1 headerSize={this.HeaderSize} encver={(this.EncryptedVersion?.ToString() ?? "缺失")} " +
              $"wzVersion={this.WzVersion} hashVersion={this.HashVersion}"
            : $"PKG2 headerSize={this.HeaderSize} hash1=0x{this.Hash1:X8} hash2=0x{this.Hash2:X8} " +
              $"hashVersion={this.HashVersion}";
    }
}
