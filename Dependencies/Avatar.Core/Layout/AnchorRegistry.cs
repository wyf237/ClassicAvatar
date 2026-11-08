using Avatar.Core.Assets;
using Avatar.Core.Models;
using WzReader.Core.Common;

namespace Avatar.Core.Layout;

/// <summary>
/// 锚点表：记录“挂点名 → 角色空间坐标”，供后续部件定位使用。
/// </summary>
/// <remarks>
/// 规则：后注册者覆盖同名锚点。这样武器可以自动跟随长袍袖口重新定义的 <c>hand</c>，
/// 无需为每种装备写特判。锚点名比较忽略大小写。
/// </remarks>
public sealed class AnchorRegistry
{
    private readonly Dictionary<string, PointI> _anchors = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>当前锚点表（只读视图）。</summary>
    public IReadOnlyDictionary<string, PointI> Anchors => this._anchors;

    /// <summary>
    /// 注册某个部件提供的全部锚点。
    /// </summary>
    /// <param name="part">已解析部件（提供 <c>map</c>）。</param>
    /// <param name="absoluteOrigin">该部件 <c>origin</c> 在角色空间中的位置。</param>
    public void Register(PartDefinition part, PointI absoluteOrigin)
    {
        ArgumentNullException.ThrowIfNull(part);
        this.Register(absoluteOrigin, part.Map);
    }

    /// <summary>
    /// 注册一组锚点（供自定义部件接入：只需提供绝对锚点与挂点表）。
    /// </summary>
    /// <param name="absoluteOrigin">部件 <c>origin</c> 在角色空间中的位置。</param>
    /// <param name="map">挂点名 → 相对 <c>origin</c> 的偏移。</param>
    public void Register(PointI absoluteOrigin, IEnumerable<KeyValuePair<string, WzVector>> map)
    {
        ArgumentNullException.ThrowIfNull(map);

        foreach ((string name, WzVector offset) in map)
        {
            this._anchors[name] = absoluteOrigin.Add(new PointI(offset.X, offset.Y));
        }
    }

    /// <summary>
    /// 尝试取锚点。
    /// </summary>
    /// <param name="name">锚点名（如 <c>navel</c>/<c>hand</c>/<c>neck</c>/<c>brow</c>）。</param>
    /// <param name="point">角色空间坐标。</param>
    /// <returns>存在返回 <c>true</c>。</returns>
    public bool TryGet(string name, out PointI point) => this._anchors.TryGetValue(name, out point);

    /// <summary>清空锚点（通常只在开始新的一帧时调用）。</summary>
    public void Clear() => this._anchors.Clear();

    /// <summary>
    /// 导出为普通字典（用于 <see cref="Models.AvatarFrame.Anchors"/>）。
    /// </summary>
    /// <param name="mirrorX">是否水平镜像（X 取负）。</param>
    /// <returns>只读字典。</returns>
    public IReadOnlyDictionary<string, PointI> ToDictionary(bool mirrorX = false)
    {
        Dictionary<string, PointI> copy = new(this._anchors.Count, StringComparer.OrdinalIgnoreCase);
        foreach ((string name, PointI point) in this._anchors)
        {
            copy[name] = mirrorX ? point.MirrorX() : point;
        }

        return copy;
    }
}
