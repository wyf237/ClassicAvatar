using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;
using WzReader.Core.Diagnostics;

namespace WzReader.Core.Package;

/// <summary>
/// 版本哈希探测：把文件头里的 encver / hash1+hash2 转换成可用的 <c>(wzVersion, hashVersion)</c>。
/// </summary>
/// <remarks>
/// <para>
/// <b>PKG1：</b>encver 是 <c>EncryptedVersion(CalcHashVersion(version))</c> 的结果。由于多个版本可能命中同一 encver，
/// 必须用目录条目的偏移边界校验来筛选。encver 缺失时，参考实现固定使用版本 777（hashVersion=59192）。
/// </para>
/// <para>
/// <b>PKG2：</b>本实现支持 KMST1196/1197 的 V1 profile：<c>hashVersion = ROL(hash1, 7) ^ hash2</c>。
/// 更新 profile（V2~V5）需要额外的回溯/暴力搜索，当前未实现，会抛出 <see cref="WzUnsupportedException"/>。
/// </para>
/// <para>
/// 校验条件（实测）：所有 image 条目算出的偏移必须满足
/// <c>DataStart &lt;= offset &lt;= fileLength</c> 且 <c>offset + size &lt;= fileLength</c>，
/// 并至少有一个条目落在目录树终点之后。
/// PKG1 用 <c>hashOffset</c> 字段的绝对位置代入公式；PKG2 沿用相对 DataStart 的基准。
/// </para>
/// </remarks>
public static class WzVersionDetector
{
    private static readonly ConcurrentDictionary<int, int[]> CandidateCache = new();

    /// <summary>计算指定客户端版本的 hashVersion。</summary>
    /// <param name="wzVersion">客户端版本号（十进制字符串逐字符参与运算）。</param>
    /// <returns>32 位哈希。</returns>
    /// <remarks>
    /// 注意：参与运算的是字符的 <b>ASCII 码</b>（例如 '7'=55），不是字符代表的数值（7）。
    /// </remarks>
    public static uint CalcHashVersion(int wzVersion)
    {
        unchecked
        {
            uint sum = 0;
            string text = wzVersion.ToString(CultureInfo.InvariantCulture);
            foreach (char ch in text)
            {
                sum = (sum << 5) + ch + 1;
            }

            return sum;
        }
    }

    /// <summary>由 hashVersion 计算 encver（0..255）。</summary>
    /// <param name="hashVersion">hashVersion。</param>
    /// <returns>encver。</returns>
    public static int CalcEncryptedVersion(uint hashVersion)
    {
        unchecked
        {
            return (byte)(0xFF ^ ((hashVersion >> 24) & 0xFF) ^ ((hashVersion >> 16) & 0xFF) ^
                          ((hashVersion >> 8) & 0xFF) ^ (hashVersion & 0xFF));
        }
    }

    /// <summary>生成所有可能命中指定 encver 的客户端版本候选。</summary>
    /// <param name="encryptedVersion">文件中的 encver。</param>
    /// <returns>候选版本号（升序）。</returns>
    public static int[] GetPkg1Candidates(int encryptedVersion)
    {
        return CandidateCache.GetOrAdd(encryptedVersion, static encver =>
        {
            List<int> candidates = new();
            for (int version = 0; version <= short.MaxValue; version++)
            {
                if (CalcEncryptedVersion(CalcHashVersion(version)) == encver)
                {
                    candidates.Add(version);
                }
            }

            return candidates.ToArray();
        });
    }

    /// <summary>探测 PKG1 的版本与 hashVersion。</summary>
    /// <param name="header">文件头。</param>
    /// <param name="scan">目录树预扫描结果。</param>
    /// <param name="fileLength">文件总长度。</param>
    /// <param name="options">打开选项（可强制指定 hashVersion）。</param>
    /// <param name="diagnostics">诊断收集器。</param>
    /// <returns>版本信息。</returns>
    /// <exception cref="WzFormatException">所有候选都无法通过边界校验。</exception>
    public static (int WzVersion, uint HashVersion) DetectPkg1(
        WzPackageHeader header,
        WzScanResult scan,
        long fileLength,
        WzOpenOptions options,
        WzDiagnostics diagnostics)
    {
        if (options.ForceHashVersion.HasValue)
        {
            uint forced = options.ForceHashVersion.Value;
            header.HashVersion = forced;
            header.WzVersion = 0;
            header.VersionResolved = true;
            diagnostics.Info($"使用强制 hashVersion=0x{forced:X8}。", "PKG1");
            return (0, forced);
        }

        int[] versions;
        if (header.EncryptedVersion.HasValue)
        {
            versions = GetPkg1Candidates(header.EncryptedVersion.Value);
        }
        else
        {
            // encver 缺失：固定哨兵版本 777。
            versions = new[] { 777 };
        }

        if (versions.Length == 0)
        {
            throw new WzFormatException($"找不到任何客户端版本能产生 encver={header.EncryptedVersion}。");
        }

        // 文件没有 image 条目时无法用偏移校验，只能采用第一个候选。
        if (scan.Images.Count == 0)
        {
            int version = versions[0];
            uint hashVersion = CalcHashVersion(version);
            header.WzVersion = version;
            header.HashVersion = hashVersion;
            header.VersionResolved = true;
            diagnostics.Info($"无 image 条目，采用首个候选版本 {version}（无法做偏移校验）。", "PKG1");
            return (version, hashVersion);
        }

        foreach (int version in versions)
        {
            uint hashVersion = CalcHashVersion(version);
            if (ValidatePkg1(scan, header, hashVersion, fileLength))
            {
                header.WzVersion = version;
                header.HashVersion = hashVersion;
                header.VersionResolved = true;
                diagnostics.Info(
                    $"版本探测命中：version={version} hashVersion=0x{hashVersion:X8}。",
                    "PKG1");
                return (version, hashVersion);
            }
        }

        throw new WzFormatException(
            $"PKG1 版本探测失败：encver={header.EncryptedVersion?.ToString() ?? "缺失"}，" +
            $"共尝试 {versions.Length} 个候选版本，全部未通过偏移校验。");
    }

    /// <summary>反解并校验 PKG2 的 hashVersion（仅支持 V1 profile）。</summary>
    /// <param name="header">文件头。</param>
    /// <param name="reader">文件读取器（用于试扫描）。</param>
    /// <param name="fileLength">文件总长度。</param>
    /// <param name="options">打开选项。</param>
    /// <param name="diagnostics">诊断收集器。</param>
    /// <returns>扫描结果（用已确定的 hashVersion）。</returns>
    /// <exception cref="WzUnsupportedException">V1 profile 校验失败。</exception>
    public static WzScanResult DetectPkg2(
        WzPackageHeader header,
        WzReader.Core.Common.WzBinaryReader reader,
        long fileLength,
        WzOpenOptions options,
        WzDiagnostics diagnostics)
    {
        uint hashVersion = options.ForceHashVersion
                           ?? unchecked(BitOperations.RotateLeft(header.Hash1, 7) ^ header.Hash2);

        WzScanResult scan;
        try
        {
            scan = WzDirectoryScanner.ScanPkg2(reader, header, hashVersion, options.MaximumDepth);
        }
        catch (WzException ex)
        {
            throw new WzUnsupportedException(
                $"PKG2 结构解析失败，当前实现仅支持 KMST1196/1197（V1 profile）。{ex.Message}");
        }

        if (!ValidatePkg2(scan, header, hashVersion, fileLength))
        {
            throw new WzUnsupportedException(
                $"PKG2 hashVersion=0x{hashVersion:X8} 未通过偏移校验；" +
                "当前实现仅支持 KMST1196/1197（V1 profile），更新版本需要额外 profile 算法。");
        }

        header.HashVersion = hashVersion;
        header.WzVersion = 0;
        header.VersionResolved = true;
        diagnostics.Info($"PKG2 版本探测命中：hashVersion=0x{hashVersion:X8}。", "PKG2");
        return scan;
    }

    private static bool ValidatePkg1(
        WzScanResult scan,
        WzPackageHeader header,
        uint hashVersion,
        long fileLength)
    {
        long dataStart = header.DataStart;
        long directoryEnd = scan.DirectoryEnd;
        bool anyAfterDirectory = false;

        foreach (WzScanEntry entry in scan.Images)
        {
            uint offset = WzOffsetCalculator.CalcPkg1(
                (uint)entry.HashPosition,
                entry.HashedOffset,
                (uint)header.HeaderSize,
                hashVersion);

            long end = (long)offset + entry.Size;
            if (offset < dataStart || end > fileLength)
            {
                return false;
            }

            if (offset >= directoryEnd)
            {
                anyAfterDirectory = true;
            }
        }

        return anyAfterDirectory;
    }

    private static bool ValidatePkg2(
        WzScanResult scan,
        WzPackageHeader header,
        uint hashVersion,
        long fileLength)
    {
        long dataStart = header.DataStart;
        long directoryEnd = scan.DirectoryEnd;
        bool anyAfterDirectory = false;

        foreach (WzScanEntry entry in scan.Images)
        {
            uint offset = WzOffsetCalculator.CalcPkg2V1(
                (uint)(entry.HashPosition - header.DataStart),
                entry.HashedOffset,
                (uint)header.HeaderSize,
                header.Hash1,
                hashVersion);

            long end = (long)offset + entry.Size;
            if (offset < dataStart || end > fileLength)
            {
                return false;
            }

            if (offset >= directoryEnd)
            {
                anyAfterDirectory = true;
            }
        }

        return anyAfterDirectory || scan.Images.Count == 0;
    }
}
