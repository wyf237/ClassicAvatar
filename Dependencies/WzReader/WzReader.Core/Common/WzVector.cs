using System.Globalization;

namespace WzReader.Core.Common;

/// <summary>
/// WZ 中使用的二维整数向量（对应 <c>Shape2D#Vector2D</c>）。
/// </summary>
/// <remarks>
/// 典型用途是画布的 <c>origin</c>（锚点）、部件的 <c>map/neck</c> 等锚点数据、以及传送门坐标。
/// 采用 <c>int</c> 而非浮点，是因为 WZ 内该类型只存 32 位整数。
/// </remarks>
public readonly record struct WzVector(int X, int Y)
{
    /// <summary>零向量。</summary>
    public static readonly WzVector Zero = new(0, 0);

    /// <summary>取反（X、Y 同时取负）。</summary>
    public WzVector Negate() => new(-this.X, -this.Y);

    /// <summary>水平镜像（保持 Y 不变）。</summary>
    public WzVector MirrorX() => new(-this.X, this.Y);

    /// <summary>与另一个向量相加。</summary>
    public WzVector Add(WzVector other) => new(this.X + other.X, this.Y + other.Y);

    /// <summary>与另一个向量相减（<c>this - other</c>）。</summary>
    public WzVector Subtract(WzVector other) => new(this.X - other.X, this.Y - other.Y);

    /// <summary>返回 <c>(X * factor, Y * factor)</c>。</summary>
    public WzVector Scale(int factor) => new(this.X * factor, this.Y * factor);

    /// <inheritdoc />
    public override string ToString()
    {
        return string.Format(CultureInfo.InvariantCulture, "({0}, {1})", this.X, this.Y);
    }
}
