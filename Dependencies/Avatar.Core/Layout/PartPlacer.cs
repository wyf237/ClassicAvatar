using Avatar.Core.Assets;
using Avatar.Core.Models;
using WzReader.Core.Common;

namespace Avatar.Core.Layout;

/// <summary>
/// 锚点定位请求：与具体图像来源解耦，既能描述 WZ 部件，也能描述外部传入的自定义图像。
/// </summary>
/// <param name="PartName">部件名（仅用于诊断）。</param>
/// <param name="Origin">画布锚点（图像内“角色参考点”的位置）。</param>
/// <param name="Map">挂点表（名称 → 相对 <paramref name="Origin"/> 的偏移）。</param>
/// <param name="IsRoot">是否为根部件（原点直接落在角色空间 (0,0)）。</param>
public sealed record NodePlacementRequest(
    string PartName,
    PointI Origin,
    IReadOnlyDictionary<string, WzVector> Map,
    bool IsRoot);

/// <summary>
/// 定位结果。
/// </summary>
/// <param name="TopLeft">画布左上角在角色空间中的坐标（未翻转坐标）。</param>
/// <param name="AnchorName">实际使用的锚点名（根部件或无可用户锚点时为空）。</param>
/// <param name="UsedFallback">是否因缺少锚点而降级（此时按角色原点放置）。</param>
public readonly record struct PartPlacement(PointI TopLeft, string? AnchorName, bool UsedFallback);

/// <summary>
/// 锚点定位求解器：输入部件元数据与当前锚点表，输出画布左上角坐标。
/// </summary>
/// <remarks>
/// 默认实现见 <see cref="DefaultAnchorPlacementSolver"/>。地图拼合、NPC 拼装等场景可以传入自定义实现，
/// 例如“同 Z 层内固定按数组顺序偏移”或“强制吸附到屏幕网格”。
/// </remarks>
public interface IAnchorPlacementSolver
{
    /// <summary>计算部件左上角坐标。</summary>
    /// <param name="request">部件元数据。</param>
    /// <param name="anchors">当前锚点表（角色空间坐标）。</param>
    /// <returns>定位结果。</returns>
    PartPlacement Place(NodePlacementRequest request, IReadOnlyDictionary<string, PointI> anchors);
}

/// <summary>
/// 默认锚点求解器：<c>TopLeft = 锚点坐标 - map 偏移 - origin</c>。
/// </summary>
/// <remarks>
/// 选择 map 项的策略：按 WZ 子节点顺序，取<b>第一个已在锚点表中存在</b>的挂点。
/// 这样 <c>stand2</c> 中同时提供 <c>navel</c> 与 <c>hand</c> 的部件不会被错误地挂到手部。
/// 若一个都不存在，则降级为角色原点并标记 <see cref="PartPlacement.UsedFallback"/>（不抛异常）。
/// </remarks>
public sealed class DefaultAnchorPlacementSolver : IAnchorPlacementSolver
{
    /// <summary>共享实例（无状态，可安全复用）。</summary>
    public static DefaultAnchorPlacementSolver Instance { get; } = new();

    /// <inheritdoc />
    public PartPlacement Place(NodePlacementRequest request, IReadOnlyDictionary<string, PointI> anchors)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(anchors);

        PointI origin = request.Origin;

        if (request.IsRoot)
        {
            return new PartPlacement(new PointI(-origin.X, -origin.Y), null, false);
        }

        string? firstMapName = null;

        foreach ((string name, WzVector offset) in request.Map)
        {
            firstMapName ??= name;

            if (!anchors.TryGetValue(name, out PointI anchorPoint))
            {
                continue;
            }

            PointI absoluteOrigin = anchorPoint.Subtract(new PointI(offset.X, offset.Y));
            return new PartPlacement(absoluteOrigin.Subtract(origin), name, false);
        }

        // 缺少可用锚点：按角色原点放置，交由上层记录 MissingAnchor 诊断。
        return new PartPlacement(new PointI(-origin.X, -origin.Y), firstMapName, true);
    }
}

/// <summary>
/// 部件定位入口：把 <see cref="PartDefinition"/> 或自定义图像元数据转换为左上角坐标。
/// </summary>
public static class PartPlacer
{
    /// <summary>
    /// 为 WZ 部件计算左上角。
    /// </summary>
    /// <param name="part">部件定义。</param>
    /// <param name="isRoot">是否根部件。</param>
    /// <param name="anchors">当前锚点表。</param>
    /// <param name="solver">自定义求解器；为 <c>null</c> 时使用默认实现。</param>
    /// <returns>定位结果。</returns>
    public static PartPlacement Place(
        PartDefinition part,
        bool isRoot,
        IReadOnlyDictionary<string, PointI> anchors,
        IAnchorPlacementSolver? solver = null)
    {
        ArgumentNullException.ThrowIfNull(part);
        return Place(FromPart(part, isRoot), anchors, solver);
    }

    /// <summary>
    /// 计算任意图像元数据的左上角（“传入图像与坐标即可自动贴图”的入口）。
    /// </summary>
    /// <param name="request">元数据（origin + map）。</param>
    /// <param name="anchors">当前锚点表。</param>
    /// <param name="solver">自定义求解器；为 <c>null</c> 时使用默认实现。</param>
    /// <returns>定位结果。</returns>
    public static PartPlacement Place(
        NodePlacementRequest request,
        IReadOnlyDictionary<string, PointI> anchors,
        IAnchorPlacementSolver? solver = null) =>
        (solver ?? DefaultAnchorPlacementSolver.Instance).Place(request, anchors);

    /// <summary>把部件转换为定位请求。</summary>
    /// <param name="part">部件定义。</param>
    /// <param name="isRoot">是否根部件。</param>
    /// <returns>定位请求。</returns>
    public static NodePlacementRequest FromPart(PartDefinition part, bool isRoot)
    {
        ArgumentNullException.ThrowIfNull(part);
        return new NodePlacementRequest(part.Name, part.Origin, part.Map, isRoot);
    }
}
