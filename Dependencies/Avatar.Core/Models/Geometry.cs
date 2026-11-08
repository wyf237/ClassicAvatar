namespace Avatar.Core.Models;

/// <summary>
/// 二维整数坐标点（角色空间或画布空间）。与 <c>WzReader.Core.Common.WzVector</c> 语义一致，
/// 单独定义是为了让 Avatar 层不直接依赖 WZ 的值类型，便于单元测试与自定义部件接入。
/// </summary>
public readonly record struct PointI(int X, int Y)
{
    /// <summary>原点 (0,0)。</summary>
    public static readonly PointI Zero = new(0, 0);

    /// <summary>与另一点相加。</summary>
    public PointI Add(PointI other) => new(this.X + other.X, this.Y + other.Y);

    /// <summary>与另一点相减（<c>this - other</c>）。</summary>
    public PointI Subtract(PointI other) => new(this.X - other.X, this.Y - other.Y);

    /// <summary>水平镜像（Y 保持不变）。</summary>
    public PointI MirrorX() => new(-this.X, this.Y);

    /// <inheritdoc />
    public override string ToString() => $"({this.X}, {this.Y})";
}

/// <summary>
/// 固定输出画布描述（用于动画序列对齐或地图拼合时复用同一块画布）。
/// </summary>
/// <param name="Width">画布宽度。</param>
/// <param name="Height">画布高度。</param>
/// <param name="Left">画布像素 (0,0) 对应的角色空间 X 坐标。</param>
/// <param name="Top">画布像素 (0,0) 对应的角色空间 Y 坐标。</param>
public readonly record struct AvatarCanvas(int Width, int Height, int Left, int Top);
