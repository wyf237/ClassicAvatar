using Avatar.Core.Assets;
using Avatar.Core.Models;
using WzReader.Core.Codecs;
using WzReader.Core.Dom;
using WzReader.Core.Links;

namespace Avatar.Core.Layout;

/// <summary>
/// 一帧中的一个已定位部件。
/// </summary>
internal sealed class CollectedPart
{
    /// <summary>来源物品。</summary>
    public required ResolvedItem Item { get; init; }

    /// <summary>部件定义。</summary>
    public required PartDefinition Part { get; init; }

    /// <summary>该部件实际使用的帧下标（已按回退策略处理）。</summary>
    public required int FrameIndex { get; init; }

    /// <summary>收集序号（用于同层稳定排序）。</summary>
    public required int Sequence { get; init; }

    /// <summary>
    /// 本帧实际使用的图层名：优先用 WZ 声明的 z，未声明时按部件名 / 槽位默认层兜底。
    /// </summary>
    public required string Layer { get; init; }

    /// <summary>画布左上角（未翻转坐标）。</summary>
    public PointI TopLeft { get; set; }

    /// <summary>实际使用的锚点名。</summary>
    public string? AnchorName { get; set; }

    /// <summary>是否因缺少锚点而降级。</summary>
    public bool UsedFallback { get; set; }
}

/// <summary>
/// 采集完成的一帧（尚未合成像素）。
/// </summary>
internal sealed class CollectedFrame
{
    /// <summary>实际动作名。</summary>
    public required string Action { get; init; }

    /// <summary>实际帧下标。</summary>
    public required int FrameIndex { get; init; }

    /// <summary>帧时长（毫秒）。</summary>
    public required int DelayMs { get; init; }

    /// <summary>身体帧表情索引。</summary>
    public required int BodyFaceIndex { get; init; }

    /// <summary>是否局部覆盖动作（身体帧缺少 body 或 arm）。</summary>
    public required bool IsOverlayAction { get; init; }

    /// <summary>按绘制顺序排好的部件。</summary>
    public required IReadOnlyList<CollectedPart> Parts { get; init; }

    /// <summary>锚点表（未翻转坐标）。</summary>
    public required IReadOnlyDictionary<string, PointI> Anchors { get; init; }

    /// <summary>渲染提示。</summary>
    public required IReadOnlyList<string> Notes { get; init; }
}

/// <summary>
/// 逐帧部件收集器：解析“物品 → 动作 → 帧 → 部件”，并按锚点框架完成定位。
/// </summary>
/// <remarks>
/// 兼容本数据集的全部结构差异：
/// <list type="bullet">
/// <item>动作帧直接包含多个部件（body/arm/…），或整个动作就是单部件；</item>
/// <item>部件可能是 <c>uol</c>/<c>_inlink</c>/<c>_outlink</c>（头部 <c>../../front/head</c>）；</item>
/// <item>帧内有“子动画容器”（头发 <c>hairShade</c> 有 0..11 共 12 个帧）；</item>
/// <item>头部只有 <c>front</c>/<c>back</c> 两组静态部件，需要按动作/朝向选择；</item>
/// <item>脸饰/眼饰/耳饰只有 <c>default</c>；背向动作需要 <c>backDefault</c>（由 <see cref="Rules.ActionResolver"/> 选择）。</item>
/// </list>
/// </remarks>
internal static class FramePartCollector
{
    /// <summary>最大递归深度（防御损坏数据）。</summary>
    private const int MaxDepth = 8;

    /// <summary>头部只渲染的部件名：<c>head</c> 与人类耳朵；其余（精灵耳/高耳等）是角色创建时的耳型变体。</summary>
    private static readonly HashSet<string> HeadParts = new(StringComparer.OrdinalIgnoreCase)
    {
        "head",
        "humanEar",
    };

    /// <summary>部件元数据键（不作为子部件继续下钻）。</summary>
    private static readonly HashSet<string> MetadataKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "origin",
        "map",
        "z",
        "group",
        "_outlink",
        "_inlink",
        "source",
    };

    /// <summary>
    /// 收集指定动作/帧的全部部件并完成定位。
    /// </summary>
    /// <param name="build">组装结果。</param>
    /// <param name="action">实际动作名（已经过 <see cref="Rules.ActionResolver.Resolve"/>）。</param>
    /// <param name="frameIndex">期望帧下标。</param>
    /// <param name="solver">自定义定位求解器（可为 <c>null</c>）。</param>
    /// <returns>采集结果。</returns>
    public static CollectedFrame Collect(
        AvatarBuild build,
        string action,
        int frameIndex,
        IAnchorPlacementSolver? solver = null)
    {
        ArgumentNullException.ThrowIfNull(build);

        List<string> notes = new();
        ResolvedItem? body = build.Items.Body;
        if (body == null)
        {
            notes.Add("没有任何身体素材，无法渲染。");
            return Empty(action, frameIndex, notes);
        }

        if (!body.Descriptor.TryGetFrameNode(
                action,
                frameIndex,
                build.Options.FrameFallback,
                out WzNode? bodyFrame,
                out int usedIndex) ||
            bodyFrame == null)
        {
            notes.Add($"身体动作「{action}」缺少帧 {frameIndex}。");
            return Empty(action, frameIndex, notes);
        }

        int delayMs = bodyFrame.GetInt("delay") ?? 100;
        int bodyFaceIndex = bodyFrame.GetInt("face") ?? 0;

        List<CollectedPart> parts = new();
        List<CollectedPart> bodyParts = new();
        int sequence = 0;

        CollectItem(
            build,
            body,
            bodyFrame,
            usedIndex,
            bodyParts,
            notes,
            null,
            ref sequence);

        parts.AddRange(bodyParts);

        bool backFacing = bodyParts.Any(static part => part.Part.IsBack);
        bool isOverlay =
            !bodyParts.Any(static part => string.Equals(part.Part.Name, "body", StringComparison.OrdinalIgnoreCase)) ||
            !bodyParts.Any(static part => string.Equals(part.Part.Name, "arm", StringComparison.OrdinalIgnoreCase));

        // 帽子对头发的遮盖类型：决定头发取“帽下”还是“帽上”图层。
        CapHairCover capCover = CapHairRule.Resolve(build);

        foreach (ResolvedItem item in build.Items.All)
        {
            if (ReferenceEquals(item, body) || item.Slot == EquipSlot.Ring)
            {
                continue;
            }

            string? selectedAction = Rules.ActionResolver.SelectItemAction(item.Descriptor, action, backFacing);
            if (selectedAction == null)
            {
                if (EquipSlotInfo.IsVisible(item.Slot))
                {
                    notes.Add($"{EquipSlotInfo.DisplayNameOf(item.Slot)}({item.ItemId:D8}) 在动作「{action}」下没有部件。");
                }

                continue;
            }

            if (!item.Descriptor.TryGetFrameNode(
                    selectedAction,
                    usedIndex,
                    build.Options.FrameFallback,
                    out WzNode? frameNode,
                    out int itemFrameIndex) ||
                frameNode == null)
            {
                notes.Add($"{EquipSlotInfo.DisplayNameOf(item.Slot)}({item.ItemId:D8}) 缺少动作「{selectedAction}」的帧 {usedIndex}。");
                continue;
            }

            Func<PartDefinition, bool>? filter = item.Slot switch
            {
                EquipSlot.Head => static part => HeadParts.Contains(part.Name),
                EquipSlot.Hair => part => CapHairRule.IsVisible(part, capCover),

                // 背向动作（爬梯/绳索等）只能看到后脑，正脸、眼镜与眼部饰品不应绘制，
                // 否则会出现“眼睛长在后脑上”。（wz 中不存在 backFace/backAccessoryEye 图层）
                EquipSlot.Face or EquipSlot.FaceAccessory or EquipSlot.EyeAccessory when backFacing =>
                    static _ => false,

                _ => null,
            };

            CollectItem(build, item, frameNode, itemFrameIndex, parts, notes, filter, ref sequence);
        }

        // 定位与锚点注册必须按“身体 → 头/发/脸 → 衣服 → 武器”的先后顺序进行。
        AnchorRegistry registry = new();
        bool rootAssigned = false;
        foreach (CollectedPart part in parts)
        {
            bool isRoot = !rootAssigned &&
                          part.Item.Slot == EquipSlot.Body &&
                          string.Equals(part.Part.Name, "body", StringComparison.OrdinalIgnoreCase);

            PartPlacement placement = PartPlacer.Place(part.Part, isRoot, registry.Anchors, solver);
            part.TopLeft = placement.TopLeft;
            part.AnchorName = placement.AnchorName;
            part.UsedFallback = placement.UsedFallback;

            if (isRoot)
            {
                rootAssigned = true;
            }

            registry.Register(part.Part, part.TopLeft.Add(part.Part.Origin));

            if (placement.UsedFallback)
            {
                notes.Add($"部件 {part.Part.Name}({part.Item.ItemId:D8}) 缺少锚点「{placement.AnchorName ?? "-"}」，已按角色原点放置。");
            }
        }

        if (!rootAssigned && parts.Count > 0)
        {
            notes.Add("没有找到身体根部件（body），所有部件按原点放置。");
        }

        // 逐帧求解 Z 序：优先使用 Base/zmap.img 的权威层序表。
        LayerSortResult layerSort = LayerOrder.SortLayers(
            parts.Select(static part => part.Layer),
            build.Data.ZMap.Ranks);
        if (layerSort.HasCycle)
        {
            notes.Add("图层约束出现循环，已按基础层序兜底排序。");
        }

        foreach (string unknown in layerSort.UnknownLayers)
        {
            notes.Add($"未收录的图层名「{unknown}」，已按身体之上兜底。");
        }

        List<CollectedPart> ordered = parts
            .OrderBy(part => layerSort.RankOf(part.Layer))
            .ThenBy(part => EquipSlotInfo.RenderOrderOf(part.Item.Slot))
            .ThenBy(static part => part.Sequence)
            .ToList();

        return new CollectedFrame
        {
            Action = action,
            FrameIndex = usedIndex,
            DelayMs = delayMs,
            BodyFaceIndex = bodyFaceIndex,
            IsOverlayAction = isOverlay,
            Parts = ordered,
            Anchors = registry.ToDictionary(),
            Notes = notes,
        };
    }

    /// <summary>
    /// 收集某件物品一个帧节点下的全部部件。
    /// </summary>
    private static void CollectItem(
        AvatarBuild build,
        ResolvedItem item,
        WzNode frameNode,
        int frameIndex,
        List<CollectedPart> sink,
        List<string> notes,
        Func<PartDefinition, bool>? filter,
        ref int sequence)
    {
        CollectNode(build, item, frameNode, frameIndex, 0, sink, notes, filter, ref sequence);
    }

    /// <summary>
    /// 递归收集节点下的部件。
    /// </summary>
    private static void CollectNode(
        AvatarBuild build,
        ResolvedItem item,
        WzNode node,
        int frameIndex,
        int depth,
        List<CollectedPart> sink,
        List<string> notes,
        Func<PartDefinition, bool>? filter,
        ref int sequence)
    {
        if (depth > MaxDepth)
        {
            notes.Add($"部件层级过深，已停止递归：{node.Path}");
            return;
        }

        AvatarData data = build.Data;

        // 1) UOL：仅跟随“结构性别名”链路（帧别名 → 默认帧/正面帧），
        //    解析到目标后按其真实类型继续处理。
        //    注意：不能用 WzLinkResolver.Resolve 做多跳解析——它会把目标部件自带的
        //    _outlink（像素链接）也一并追踪，导致拿到 _Canvas 裸画布而丢失 origin/map/z
        //    元数据；像素链接由 PartResolver 负责，此处只剥 UOL。
        if (node.Value is WzUol)
        {
            WzNode? target = ResolveUolChain(node, data, notes);
            if (target == null)
            {
                return;
            }

            CollectNode(build, item, target, frameIndex, depth + 1, sink, notes, filter, ref sequence);
            return;
        }

        // 2) 画布：一个独立部件。
        if (node.Value is WzCanvas)
        {
            AddPart(data, item, node, frameIndex, sink, notes, filter, ref sequence);
            return;
        }

        if (!node.IsContainer)
        {
            // Int16/Int32/String 等（face/delay/origin 等）不是部件。
            return;
        }

        // 3) 子动画容器（如头发 default/hairShade 下的 0..11）：按当前帧号挑一张。
        if (TryPickAnimationFrame(node, frameIndex, out WzNode? animationFrame) && animationFrame != null)
        {
            CollectNode(build, item, animationFrame, frameIndex, depth + 1, sink, notes, filter, ref sequence);
            return;
        }

        // 4) 带 _outlink/_inlink/source 的容器本身可能是部件（元数据容器 + 真实像素）。
        if (HasPixelLink(node))
        {
            AddPart(data, item, node, frameIndex, sink, notes, filter, ref sequence);
            return;
        }

        // 5) 普通容器：下钻子节点（跳过 origin/map/z/group 等元数据键）。
        foreach (WzNode child in node.Children)
        {
            if (MetadataKeys.Contains(child.Name))
            {
                continue;
            }

            CollectNode(build, item, child, frameIndex, depth + 1, sink, notes, filter, ref sequence);
        }
    }

    /// <summary>解析并追加一个部件。</summary>
    private static void AddPart(
        AvatarData data,
        ResolvedItem item,
        WzNode node,
        int frameIndex,
        List<CollectedPart> sink,
        List<string> notes,
        Func<PartDefinition, bool>? filter,
        ref int sequence)
    {
        if (!data.TryGetPart(node, out PartDefinition? part, out string? error) || part == null)
        {
            notes.Add($"{EquipSlotInfo.DisplayNameOf(item.Slot)}({item.ItemId:D8}) 部件解析失败：{error}");
            return;
        }

        if (filter != null && !filter(part))
        {
            return;
        }

        // WZ 未声明 z 时优先用部件名兜底（如 hairShade 本身没有 z，但 zmap 里有同名图层），
        // 否则用槽位默认层。若直接留空，会被 zmap 排序推到最前方，造成整片遮挡。
        string layer = part.Layer;
        if (string.IsNullOrWhiteSpace(layer))
        {
            layer = data.ZMap.TryGetRank(part.Name, out _)
                ? part.Name
                : EquipSlotInfo.DefaultLayerOf(item.Slot);
        }

        if (string.IsNullOrEmpty(layer))
        {
            layer = "cap";
        }

        sink.Add(new CollectedPart
        {
            Item = item,
            Part = part,
            FrameIndex = frameIndex,
            Sequence = sequence++,
            Layer = layer,
        });
    }

    /// <summary>节点是否带像素链接（此时容器本身可解析为部件）。</summary>
    private static bool HasPixelLink(WzNode node) =>
        node.FindChild("_outlink", ignoreCase: true) != null ||
        node.FindChild("_inlink", ignoreCase: true) != null ||
        node.FindChild("source", ignoreCase: true) != null;

    /// <summary>
    /// 仅沿 <c>uol</c> 链路解析，直到得到一个非 UOL 节点。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 角色素材里的 UOL 是“结构性别名”：动作帧里的 <c>head</c> 指向 <c>../../front/head</c>，
    /// 头发 <c>stand1/0/hair</c> 指向 <c>../../default/hair</c>。跟随这类别名不会改变部件本身。
    /// </para>
    /// <para>
    /// 目标节点如果自己带 <c>_outlink</c>/<c>_inlink</c>/<c>source</c>（像素链接），
    /// <b>必须停在该节点</b>：它才是携带 <c>origin/map/z/group</c> 的元数据占位节点，
    /// 继续追踪会拿到 <c>_Canvas</c> 下的裸画布，锚点与图层信息全部丢失。
    /// </para>
    /// <para>
    /// 使用 <see cref="WzLinkResolver.GetLink(WzNode, Func{string, WzNode?})"/> 只做单跳解析，
    /// 以节点引用集合做环检测。
    /// </para>
    /// </remarks>
    /// <param name="start">起始 UOL 节点。</param>
    /// <param name="data">素材数据（提供按路径查找）。</param>
    /// <param name="notes">渲染提示收集器。</param>
    /// <returns>链路终点的非 UOL 节点；解析失败返回 <c>null</c>。</returns>
    private static WzNode? ResolveUolChain(WzNode start, AvatarData data, List<string> notes)
    {
        HashSet<WzNode> visited = new(ReferenceEqualityComparer.Instance);
        WzNode current = start;

        while (current.Value is WzUol)
        {
            if (!visited.Add(current))
            {
                notes.Add($"UOL 链路出现循环：{start.Path}");
                return null;
            }

            WzLinkInfo? link = WzLinkResolver.GetLink(current, data.FindNode);
            if (link?.Target == null)
            {
                notes.Add($"链接无法解析：{current.Path}（{link?.TargetPath ?? "空路径"}）");
                return null;
            }

            current = link.Target;
        }

        return current;
    }

    /// <summary>
    /// 子动画容器判定：全部子节点都是数字命名，且至少有一个。
    /// 选中规则：精确帧号 → 小于等于帧号的最大帧 → 最小帧。
    /// </summary>
    private static bool TryPickAnimationFrame(WzNode node, int frameIndex, out WzNode? frame)
    {
        frame = null;
        List<(int Index, WzNode Node)> frames = ItemDescriptor.GetNumberedChildren(node);
        if (frames.Count == 0 || frames.Count != node.Children.Count)
        {
            return false;
        }

        (int Index, WzNode Node) selected = frames[0];
        foreach ((int index, WzNode candidate) in frames)
        {
            if (index == frameIndex)
            {
                selected = (index, candidate);
                break;
            }

            if (index <= frameIndex && index > selected.Index)
            {
                selected = (index, candidate);
            }
        }

        frame = selected.Node;
        return true;
    }

    /// <summary>空帧。</summary>
    private static CollectedFrame Empty(string action, int frameIndex, List<string> notes) =>
        new()
        {
            Action = action,
            FrameIndex = frameIndex,
            DelayMs = 100,
            BodyFaceIndex = 0,
            IsOverlayAction = true,
            Parts = Array.Empty<CollectedPart>(),
            Anchors = new Dictionary<string, PointI>(),
            Notes = notes,
        };
}
