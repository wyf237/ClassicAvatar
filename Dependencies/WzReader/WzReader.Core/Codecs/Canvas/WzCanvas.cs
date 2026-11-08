using WzReader.Core.Crypto;
using WzReader.Core.Diagnostics;

namespace WzReader.Core.Codecs;

/// <summary>
/// 解码后的画布节点值：保存画布头参数、压缩像素数据，并按需解码为 <see cref="WzPixelBuffer"/>。
/// </summary>
/// <remarks>
/// <para>
/// 画布的像素数据可能很大，因此不在解析 img 时立即解码，而是首次访问 <see cref="Frames"/> 时解码并缓存。
/// 同一 img 可能被多个链接节点共享，缓存可避免重复解压。
/// </para>
/// <para>
/// 动画画布（<c>pages &gt; 1</c>）的每一页尺寸相同，按页顺序存放在同一段压缩流中。
/// <see cref="FrameCount"/> 在 <c>pages &lt;= 0</c> 时返回 1。
/// </para>
/// </remarks>
public sealed class WzCanvas
{
    private readonly object _decodeGate = new();
    private IReadOnlyList<WzPixelBuffer>? _frames;

    /// <summary>创建画布值。</summary>
    /// <param name="width">声明宽度（像素）。</param>
    /// <param name="height">声明高度（像素）。</param>
    /// <param name="format">像素格式。</param>
    /// <param name="scale">缩放指数（实际缩放 = 1 &lt;&lt; scale）。</param>
    /// <param name="pages">动画页数（&lt;=0 视为 1）。</param>
    /// <param name="unknown1">KMST1198+ 的保留字段。</param>
    /// <param name="compressedData">原始压缩数据（已含首字节与可能的 zlib 头）。</param>
    /// <param name="key">分块 XOR 解密密钥；明文数据可为 <c>null</c>。</param>
    /// <param name="diagnostics">诊断收集器。</param>
    public WzCanvas(
        int width,
        int height,
        WzPixelFormat format,
        int scale,
        int pages,
        int unknown1,
        byte[] compressedData,
        IWzXorKey? key,
        WzDiagnostics? diagnostics)
    {
        this.Width = width;
        this.Height = height;
        this.Format = format;
        this.Scale = scale;
        this.Pages = pages;
        this.Unknown1 = unknown1;
        this.CompressedData = compressedData ?? Array.Empty<byte>();
        this.Key = key;
        this.Diagnostics = diagnostics;
    }

    /// <summary>声明宽度（像素）。</summary>
    public int Width { get; }

    /// <summary>声明高度（像素）。</summary>
    public int Height { get; }

    /// <summary>像素格式。</summary>
    public WzPixelFormat Format { get; }

    /// <summary>缩放指数字段（原始值）。</summary>
    public int Scale { get; }

    /// <summary>动画页数字段（原始值）。</summary>
    public int Pages { get; }

    /// <summary>保留字段。</summary>
    public int Unknown1 { get; }

    /// <summary>原始压缩数据。</summary>
    public byte[] CompressedData { get; }

    /// <summary>分块 XOR 密钥。</summary>
    public IWzXorKey? Key { get; }

    /// <summary>诊断收集器。</summary>
    public WzDiagnostics? Diagnostics { get; }

    /// <summary>实际页数（<c>pages &lt;= 0</c> 时按 1 处理）。</summary>
    public int FrameCount => this.Pages > 0 ? this.Pages : 1;

    /// <summary>实际缩放倍数（<c>1 &lt;&lt; scale</c>；scale&lt;=0 时为 1）。</summary>
    public int ActualScale => this.Scale > 0 ? 1 << this.Scale : 1;

    /// <summary>解码后的全部帧（惰性解码并缓存，线程安全）。</summary>
    public IReadOnlyList<WzPixelBuffer> Frames
    {
        get
        {
            if (this._frames != null)
            {
                return this._frames;
            }

            lock (this._decodeGate)
            {
                this._frames ??= WzCanvasCodec.Decode(this);
                return this._frames;
            }
        }
    }

    /// <summary>获取指定帧。</summary>
    /// <param name="index">帧下标。</param>
    /// <returns>像素缓冲区。</returns>
    public WzPixelBuffer GetFrame(int index)
    {
        IReadOnlyList<WzPixelBuffer> frames = this.Frames;
        if (index < 0 || index >= frames.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index), $"帧下标必须在 0..{frames.Count - 1} 之间。");
        }

        return frames[index];
    }

    /// <summary>
    /// 放弃已解码的帧缓存；下次访问 <see cref="Frames"/> 时会重新从压缩数据解码。
    /// </summary>
    /// <remarks>
    /// 若有外部代码仍持有上次解码出来的 <see cref="WzPixelBuffer"/>，缓存清除不会失效那些引用，
    /// 只是让本画布实例重新解码一份新的像素数组，调用方应自行确保不再需要旧数据。
    /// </remarks>
    public void ClearFrames()
    {
        lock (this._decodeGate)
        {
            this._frames = null;
        }
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"Canvas {this.Width}x{this.Height} {this.Format.GetDisplayName()} " +
               $"scale={this.ActualScale} frames={this.FrameCount}";
    }
}
