using System.Buffers.Binary;

namespace WzReader.Core.Dom;

/// <summary>
/// 声音负载的类型判定。
/// </summary>
public enum WzSoundKind
{
    /// <summary>无法识别。</summary>
    Unknown = 0,

    /// <summary>MP3（可直接写 .mp3）。</summary>
    Mp3 = 1,

    /// <summary>PCM（需要补 RIFF/WAVE 头）。</summary>
    Pcm = 2,

    /// <summary>未知二进制（按原始字节输出）。</summary>
    Binary = 3,
}

/// <summary>
/// 解码后的 <c>WAVEFORMATEX</c>（含 MPEGLAYER3 扩展时的原始字节）。
/// </summary>
/// <param name="FormatTag">格式标记（1=PCM，0x55=MP3）。</param>
/// <param name="Channels">声道数。</param>
/// <param name="SamplesPerSec">采样率。</param>
/// <param name="AvgBytesPerSec">平均字节率。</param>
/// <param name="BlockAlign">块对齐。</param>
/// <param name="BitsPerSample">位深。</param>
/// <param name="CbSize">扩展字节数。</param>
/// <param name="Raw">解密后的 fmt 原始数据。</param>
public readonly record struct WzSoundFormat(
    ushort FormatTag,
    ushort Channels,
    uint SamplesPerSec,
    uint AvgBytesPerSec,
    ushort BlockAlign,
    ushort BitsPerSample,
    ushort CbSize,
    byte[] Raw);

/// <summary>
/// <c>Sound_DX8</c> 节点值：音频负载与格式信息。
/// </summary>
/// <remarks>
/// WZ 中的音频负载本身不加密；只有 <c>fmt</c> 结构是 XOR 加密的，且必须独立探测密钥
/// （不能复用属性名的密钥结论）。本类保存已解密/解析后的结果。
/// </remarks>
public sealed class WzSound
{
    /// <summary>创建音频节点值。</summary>
    /// <param name="data">负载字节。</param>
    /// <param name="kind">类型。</param>
    /// <param name="durationMs">时长（毫秒）。</param>
    /// <param name="format">格式信息；无 fmt 时为 <c>null</c>。</param>
    public WzSound(byte[] data, WzSoundKind kind, int durationMs, WzSoundFormat? format)
    {
        this.Data = data ?? Array.Empty<byte>();
        this.Kind = kind;
        this.DurationMs = durationMs;
        this.Format = format;
    }

    /// <summary>负载字节。</summary>
    public byte[] Data { get; }

    /// <summary>类型判定。</summary>
    public WzSoundKind Kind { get; }

    /// <summary>时长（毫秒）。</summary>
    public int DurationMs { get; }

    /// <summary>格式信息。</summary>
    public WzSoundFormat? Format { get; }

    /// <summary>
    /// 获取可直接播放/保存的字节：MP3 原样返回；PCM 自动补 44 字节 RIFF/WAVE 头；其它按原始字节返回。
    /// </summary>
    /// <returns>音频字节。</returns>
    public byte[] GetPlayableBytes()
    {
        if (this.Kind != WzSoundKind.Pcm || this.Format is null)
        {
            return this.Data;
        }

        WzSoundFormat format = this.Format.Value;
        int dataLength = this.Data.Length;
        byte[] output = new byte[44 + dataLength];

        // RIFF 头
        WriteAscii(output, 0, "RIFF");
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(4), dataLength + 36);
        WriteAscii(output, 8, "WAVE");
        WriteAscii(output, 12, "fmt ");
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(16), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(20), format.FormatTag);
        BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(22), format.Channels);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(24), format.SamplesPerSec);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(28), format.AvgBytesPerSec);
        BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(32), format.BlockAlign);
        BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(34), format.BitsPerSample);
        WriteAscii(output, 36, "data");
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(40), dataLength);

        Buffer.BlockCopy(this.Data, 0, output, 44, dataLength);
        return output;
    }

    private static void WriteAscii(byte[] buffer, int offset, string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            buffer[offset + i] = (byte)text[i];
        }
    }
}
