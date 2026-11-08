using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace WzReader.Core.Crypto;

/// <summary>
/// WZ 字符串/画布数据使用的 XOR 密钥类型。
/// </summary>
/// <remarks>
/// 注意区分：
/// <list type="bullet">
/// <item><see cref="Bms"/> 是“空操作密钥”，索引恒为 0，用于明文 WZ；</item>
/// <item><see cref="Kms"/> / <see cref="Gms"/> 由固定 AES 密钥链式生成 64KB 密钥表；</item>
/// <item><see cref="Kmst1198"/> 是 PKG2 目录名专用的 8 字节循环密钥（不是 64KB 表）。</item>
/// </list>
/// 密钥类型由 <see cref="WzKeyDetector"/> 根据文件名/字符串内容探测，不能写死。
/// </remarks>
public enum WzKeyType
{
    /// <summary>BMS：等价于不加密。</summary>
    Bms = 0,

    /// <summary>KMS：韩服密钥。</summary>
    Kms = 1,

    /// <summary>GMS：国际服密钥。</summary>
    Gms = 2,

    /// <summary>KMST1198+ 的 PKG2 目录名字符串密钥。</summary>
    Kmst1198 = 3,
}

/// <summary>
/// XOR 密钥的统一抽象：既可按下标取值，也可就地解密一段数据。
/// </summary>
/// <remarks>
/// 字符串读取在每个字符串内部都把 <c>keyOffset</c> 重置为 0；
/// 画布的分块加密流则在每个 chunk 内部把 <c>keyOffset</c> 重置为 0。
/// 调用方必须遵守这一约定，否则解密结果会整体错位。
/// </remarks>
public interface IWzXorKey
{
    /// <summary>获取密钥表中指定位置的字节（循环密钥会自动取模）。</summary>
    /// <param name="index">密钥表下标。</param>
    /// <returns>密钥字节。</returns>
    byte this[int index] { get; }

    /// <summary>就地异或解密。</summary>
    /// <param name="data">待解密数据。</param>
    /// <param name="keyOffset">密钥表起始偏移；字符串/画布块内固定传 0。</param>
    void Decrypt(Span<byte> data, int keyOffset = 0);
}

/// <summary>
/// 空操作密钥（BMS）：所有字节保持不变。
/// </summary>
public sealed class WzNoOpKey : IWzXorKey
{
    /// <summary>共享实例（无状态）。</summary>
    public static readonly WzNoOpKey Instance = new();

    private WzNoOpKey()
    {
    }

    /// <inheritdoc />
    public byte this[int index] => 0;

    /// <inheritdoc />
    public void Decrypt(Span<byte> data, int keyOffset = 0)
    {
        // 明文 WZ：无需处理。
    }
}

/// <summary>
/// 64KB 级别的大密钥表：用于 PKG1 字符串、img 属性名与画布分块流。
/// </summary>
public sealed class WzXorKey : IWzXorKey
{
    private readonly byte[] _keys;

    /// <summary>使用已生成的密钥表创建实例。</summary>
    /// <param name="keys">密钥表（长度应足够覆盖所有解密请求）。</param>
    /// <param name="name">可读名称，仅用于诊断。</param>
    public WzXorKey(byte[] keys, string name)
    {
        this._keys = keys ?? throw new ArgumentNullException(nameof(keys));
        this.Name = name;
    }

    /// <summary>可读名称（例如 <c>KMS</c>）。</summary>
    public string Name { get; }

    /// <summary>密钥表长度。</summary>
    public int Length => this._keys.Length;

    /// <inheritdoc />
    public byte this[int index] => this._keys[index % this._keys.Length];

    /// <inheritdoc />
    public void Decrypt(Span<byte> data, int keyOffset = 0)
    {
        if (keyOffset + data.Length > this._keys.Length)
        {
            // 理论上不会发生：表固定 0x10000，单个 img 字符串不会超过该长度。
            // 若发生，按循环取模处理，保证不越界崩溃。
            for (int i = 0; i < data.Length; i++)
            {
                data[i] ^= this._keys[(keyOffset + i) % this._keys.Length];
            }

            return;
        }

        ReadOnlySpan<byte> keySpan = this._keys.AsSpan(keyOffset, data.Length);
        for (int i = 0; i < data.Length; i++)
        {
            data[i] ^= keySpan[i];
        }
    }
}

/// <summary>
/// PKG2 目录名专用密钥：8 字节循环表。
/// </summary>
/// <remarks>
/// 该密钥只用于 KMST1198+ 的“首个目录名”，因此长度固定为 8；
/// 解密时按 8 取模循环，且调用方不得再施加 <c>0xAAAA+i</c> 掩码。
/// </remarks>
public sealed class WzPkg2DirStringKey : IWzXorKey
{
    private readonly byte[] _keys = new byte[8];

    /// <summary>由 32 位基准值展开为 8 字节密钥。</summary>
    /// <param name="baseKey">基准值（例如 <c>0xDEADBEEF</c>）。</param>
    public WzPkg2DirStringKey(uint baseKey)
    {
        Span<ushort> words = MemoryMarshal.Cast<byte, ushort>(this._keys.AsSpan());
        for (int i = 0; i < 4; i++)
        {
            words[i] = (ushort)(baseKey >> (8 * i));
        }
    }

    /// <inheritdoc />
    public byte this[int index] => this._keys[index % 8];

    /// <inheritdoc />
    public void Decrypt(Span<byte> data, int keyOffset = 0)
    {
        for (int i = 0; i < data.Length; i++)
        {
            data[i] ^= this._keys[(keyOffset + i) % 8];
        }
    }
}

/// <summary>
/// 密钥表工厂：按类型创建/缓存 XOR 密钥。
/// </summary>
/// <remarks>
/// <para>
/// 本实现只使用一个固定 32 字节 AES 密钥做 <b>AES-256-ECB</b> 链式加密，生成 0x10000 字节密钥表：
/// <c>K[0..15] = AES(IV 重复 4 次)</c>，<c>K[n] = AES(K[n-1])</c>。
/// </para>
/// <para>
/// 密钥表只读，生成一次后可在线程间共享，因此使用 <see cref="Lazy{T}"/> 缓存。
/// </para>
/// </remarks>
public static class WzKeyFactory
{
    private static readonly byte[] AesKey =
    {
        0x13, 0x00, 0x00, 0x00,
        0x08, 0x00, 0x00, 0x00,
        0x06, 0x00, 0x00, 0x00,
        0xB4, 0x00, 0x00, 0x00,
        0x1B, 0x00, 0x00, 0x00,
        0x0F, 0x00, 0x00, 0x00,
        0x33, 0x00, 0x00, 0x00,
        0x52, 0x00, 0x00, 0x00,
    };

    private static readonly byte[] KmsIv = { 0xB9, 0x7D, 0x63, 0xE9 };
    private static readonly byte[] GmsIv = { 0x4D, 0x23, 0xC7, 0x2B };

    private static readonly Lazy<WzXorKey> LazyKms = new(
        () => new WzXorKey(GenerateTable(KmsIv), "KMS"),
        isThreadSafe: true);

    private static readonly Lazy<WzXorKey> LazyGms = new(
        () => new WzXorKey(GenerateTable(GmsIv), "GMS"),
        isThreadSafe: true);

    /// <summary>按类型获取 XOR 密钥实例（大表会惰性生成并缓存）。</summary>
    /// <param name="type">密钥类型。</param>
    /// <returns>可共享的密钥实例。</returns>
    public static IWzXorKey Get(WzKeyType type)
    {
        return type switch
        {
            WzKeyType.Kms => LazyKms.Value,
            WzKeyType.Gms => LazyGms.Value,
            WzKeyType.Bms => WzNoOpKey.Instance,
            WzKeyType.Kmst1198 => new WzPkg2DirStringKey(0xDEADBEEF),
            _ => WzNoOpKey.Instance,
        };
    }

    /// <summary>创建 PKG2 目录名字符串密钥（KMST1198 固定基准）。</summary>
    /// <returns>8 字节循环密钥。</returns>
    public static IWzXorKey CreatePkg2DirStringKey()
    {
        return new WzPkg2DirStringKey(0xDEADBEEF);
    }

    /// <summary>创建 KMST1199 派生的 PKG2 目录名密钥。</summary>
    /// <param name="hash1">PKG2 头的 hash1。</param>
    /// <param name="hashVersion">反解出的 hashVersion。</param>
    /// <returns>8 字节循环密钥。</returns>
    public static IWzXorKey CreatePkg2DirStringKeyV2(uint hash1, uint hashVersion)
    {
        unchecked
        {
            uint baseValue = hash1 ^ hashVersion ^ 0x6D4C3B2A;
            uint baseKey = Mix(Mix(baseValue) ^ 0x4F4CB34A);
            return new WzPkg2DirStringKey(baseKey);
        }
    }

    /// <summary>
    /// 生成 XOR 密钥表（AES-256-ECB 链式加密）。
    /// </summary>
    /// <param name="iv">4 字节初始向量（重复 4 次构成第一个明文块）。</param>
    /// <param name="size">需要的密钥表字节数；默认 0x10000。</param>
    /// <returns>密钥表。</returns>
    public static byte[] GenerateTable(byte[] iv, int size = 0x10000)
    {
        if (iv is null || iv.Length < 4)
        {
            throw new ArgumentException("IV 至少需要 4 字节。", nameof(iv));
        }

        // 按 16 字节对齐，避免最后不足一块。
        size = (size + 15) & ~15;
        byte[] keys = new byte[size];

        using Aes aes = Aes.Create();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        aes.Key = AesKey;

        using ICryptoTransform encryptor = aes.CreateEncryptor();
        byte[] block = new byte[16];
        byte[] output = new byte[16];

        for (int i = 0; i < 16; i++)
        {
            block[i] = iv[i % 4];
        }

        for (int position = 0; position < size; position += 16)
        {
            encryptor.TransformBlock(block, 0, 16, output, 0);
            Buffer.BlockCopy(output, 0, keys, position, 16);
            Buffer.BlockCopy(output, 0, block, 0, 16);
        }

        return keys;
    }

    /// <summary>32 位混合函数（PKG2 密钥派生使用）。</summary>
    /// <param name="value">输入值。</param>
    /// <returns>混合后的值。</returns>
    public static uint Mix(uint value)
    {
        unchecked
        {
            value ^= value >> 16;
            value *= 0x7FEB352D;
            value ^= value >> 15;
            value *= 0x846CA68B;
            value ^= value >> 16;
            return value;
        }
    }
}

/// <summary>
/// 密钥探测：从目录树中“第一个内联字符串”的原始字节反推密钥类型。
/// </summary>
/// <remarks>
/// 探测顺序固定为 <c>BMS → KMS → GMS</c>：
/// BMS 是空操作密钥，因此明文 WZ 会立即命中；加密 WZ 只有正确密钥才能解出合法节点名。
/// 这里的“合法节点名”判定与参考实现一致：非空，且以 <c>.img</c>/<c>.lua</c> 结尾，或所有字符均为可打印 ASCII。
/// </remarks>
public static class WzKeyDetector
{
    /// <summary>从第一个内联字符串的原始负载探测 PKG1 密钥类型。</summary>
    /// <param name="payload">原始负载（不含长度前缀，未解密）。</param>
    /// <param name="isUtf16">是否为 UTF-16 段（长度前缀为正表示 UTF-16）。</param>
    /// <returns>探测到的密钥类型；无法判定时返回 <see cref="WzKeyType.Bms"/>。</returns>
    public static WzKeyType DetectFromFirstString(byte[] payload, bool isUtf16)
    {
        if (payload.Length == 0)
        {
            return WzKeyType.Bms;
        }

        // 第一步：施加“线掩码”（wire mask）。注意这是与写盘时同样的位置相关掩码。
        byte[] masked = (byte[])payload.Clone();
        ApplyWireMask(masked, isUtf16);

        WzKeyType[] candidates = { WzKeyType.Bms, WzKeyType.Kms, WzKeyType.Gms };
        foreach (WzKeyType candidate in candidates)
        {
            byte[] plain = (byte[])masked.Clone();
            WzKeyFactory.Get(candidate).Decrypt(plain, 0);

            string? text = Decode(plain, isUtf16);
            if (IsLegalNodeName(text))
            {
                return candidate;
            }
        }

        return WzKeyType.Bms;
    }

    /// <summary>判断字符串是否像合法的 WZ 节点名。</summary>
    /// <param name="name">候选字符串。</param>
    /// <returns>像节点名返回 <c>true</c>。</returns>
    public static bool IsLegalNodeName(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        if (name.EndsWith(".img", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (char ch in name)
        {
            if (ch < 0x20 || ch > 0x7E)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>对原始负载施加线掩码（ASCII 用 0xAA+i，UTF-16 用 0xAAAA+i）。</summary>
    /// <param name="data">就地处理的数据。</param>
    /// <param name="isUtf16">是否按 UTF-16 处理。</param>
    public static void ApplyWireMask(Span<byte> data, bool isUtf16)
    {
        if (isUtf16)
        {
            int charCount = data.Length / 2;
            for (int i = 0; i < charCount; i++)
            {
                ushort mask = unchecked((ushort)(0xAAAA + i));
                data[i * 2] ^= (byte)(mask & 0xFF);
                data[i * 2 + 1] ^= (byte)(mask >> 8);
            }
        }
        else
        {
            for (int i = 0; i < data.Length; i++)
            {
                data[i] ^= unchecked((byte)(0xAA + i));
            }
        }
    }

    private static string? Decode(byte[] data, bool isUtf16)
    {
        try
        {
            if (isUtf16)
            {
                // 注意：内部字符已经按 0xAAAA+i 掩码处理过，正常不应出现非法代理项。
                return System.Text.Encoding.Unicode.GetString(data);
            }

            char[] chars = new char[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                chars[i] = (char)data[i];
            }

            return new string(chars);
        }
        catch
        {
            return null;
        }
    }
}
