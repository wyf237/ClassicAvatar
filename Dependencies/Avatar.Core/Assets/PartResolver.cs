using Avatar.Core.Models;
using WzReader.Core.Codecs;
using WzReader.Core.Common;
using WzReader.Core.Dom;
using WzReader.Core.Links;

namespace Avatar.Core.Assets;

/// <summary>
/// 解析完成的部件元数据：画布尺寸、锚点（origin）、挂点表（map）、图层名（z）与真实像素节点。
/// </summary>
public sealed class PartDefinition
{
    /// <summary>创建部件定义。</summary>
    internal PartDefinition(
        string name,
        string layer,
        string group,
        WzVector origin,
        IReadOnlyDictionary<string, WzVector> map,
        WzNode metadataNode,
        WzNode canvasNode,
        WzCanvas canvas)
    {
        this.Name = name;
        this.Layer = layer;
        this.Group = group;
        this.Origin = new PointI(origin.X, origin.Y);
        this.Map = map;
        this.MetadataNode = metadataNode;
        this.CanvasNode = canvasNode;
        this.Canvas = canvas;
    }

    /// <summary>WZ 部件节点名（body/arm/head/mail/weapon…）。</summary>
    public string Name { get; }

    /// <summary>图层名（<c>z</c>）；缺失时为空串。</summary>
    public string Layer { get; }

    /// <summary>分组（<c>group</c>，通常是 skin 或空）。</summary>
    public string Group { get; }

    /// <summary>画布锚点（origin），即画布内“角色参考点”的位置。</summary>
    public PointI Origin { get; }

    /// <summary>挂点表（map）：名称 → 相对本部件 origin 的偏移。</summary>
    public IReadOnlyDictionary<string, WzVector> Map { get; }

    /// <summary>携带 origin/map/z 等元数据的节点（UOL 解析后的目标）。</summary>
    public WzNode MetadataNode { get; }

    /// <summary>真实像素节点（_outlink 解析后的目标）。</summary>
    public WzNode CanvasNode { get; }

    /// <summary>真实画布（可能多页）。</summary>
    public WzCanvas Canvas { get; }

    /// <summary>画布宽度。</summary>
    public int Width => this.Canvas.Width;

    /// <summary>画布高度。</summary>
    public int Height => this.Canvas.Height;

    /// <summary>是否为背向图层。</summary>
    public bool IsBack => this.Layer.StartsWith("back", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public override string ToString() => $"{this.Name} z={this.Layer} origin={this.Origin} {this.Width}x{this.Height}";
}

/// <summary>
/// 部件解析器：把 WZ 中形态各异的部件节点统一解析为 <see cref="PartDefinition"/>。
/// </summary>
/// <remarks>
/// WZ 角色素材有三种常见写法，必须全部兼容：
/// <list type="number">
/// <item><b>普通画布</b>：节点值就是 <see cref="WzCanvas"/>，元数据是它的子节点；</item>
/// <item><b>UOL 指向元数据</b>：例如头部 <c>stand1/0/head = uol ../../front/head</c>，目标节点才带 origin/map；</item>
/// <item><b>占位画布 + _outlink</b>：元数据节点自带 1×1 占位画布，真实像素在 <c>Character/&lt;部位&gt;/_Canvas/…</c>。</item>
/// </list>
/// 因此解析分两步：先沿非 <c>_outlink</c> 链接找到“元数据节点”，再完整解析链接取得真实画布。
/// </remarks>
public static class PartResolver
{
    /// <summary>最大追踪跳数（防御损坏数据导致的死循环）。</summary>
    public const int MaxHops = 12;

    /// <summary>
    /// 解析部件节点。
    /// </summary>
    /// <param name="partNode">部件节点（可能是 UOL/占位画布）。</param>
    /// <param name="lookup">按绝对路径查节点的委托（通常是 <c>WzDataRoot.FindNode</c>）。</param>
    /// <param name="part">解析结果。</param>
    /// <param name="error">失败原因。</param>
    /// <returns>成功返回 <c>true</c>。</returns>
    public static bool TryResolve(
        WzNode partNode,
        Func<string, WzNode?> lookup,
        out PartDefinition? part,
        out string? error)
    {
        part = null;
        error = null;

        WzNode? metadata = ResolveMetadataNode(partNode, lookup);
        if (metadata == null)
        {
            error = $"链接无法解析：{partNode.Path}";
            return false;
        }

        WzNode? canvasNode = WzLinkResolver.Resolve(metadata, lookup);
        if (canvasNode == null)
        {
            error = $"像素链接无法解析：{metadata.Path}";
            return false;
        }

        if (canvasNode.Value is not WzCanvas canvas)
        {
            error = $"目标不是画布（{canvasNode.Value?.GetType().Name ?? "null"}）：{canvasNode.Path}";
            return false;
        }

        WzVector origin = metadata.GetVector("origin") ?? WzVector.Zero;
        string layer = metadata.GetString("z") ?? string.Empty;
        string group = metadata.GetString("group") ?? string.Empty;

        Dictionary<string, WzVector> map = new(StringComparer.OrdinalIgnoreCase);
        WzNode? mapNode = metadata.FindChild("map", ignoreCase: true);
        if (mapNode != null)
        {
            foreach (WzNode child in mapNode.Children)
            {
                if (child.Value is WzVector vector)
                {
                    map[child.Name] = vector;
                }
            }
        }

        part = new PartDefinition(metadata.Name, layer, group, origin, map, metadata, canvasNode, canvas);
        return true;
    }

    /// <summary>
    /// 沿 <c>uol</c>/<c>_inlink</c>/<c>source</c> 找到携带元数据的节点，遇到 <c>_outlink</c> 即停止。
    /// </summary>
    /// <param name="node">起始节点。</param>
    /// <param name="lookup">查节点委托。</param>
    /// <returns>元数据节点；无法解析时返回 <c>null</c>。</returns>
    private static WzNode? ResolveMetadataNode(WzNode node, Func<string, WzNode?> lookup)
    {
        WzNode current = node;
        for (int hop = 0; hop < MaxHops; hop++)
        {
            WzLinkInfo? link = WzLinkResolver.GetLink(current, lookup);
            if (link == null)
            {
                return current;
            }

            // _outlink 只指向像素，元数据仍在当前节点上。
            if (string.Equals(link.Kind, "_outlink", StringComparison.Ordinal))
            {
                return current;
            }

            if (link.Target == null)
            {
                return current;
            }

            current = link.Target;
        }

        return current;
    }
}
