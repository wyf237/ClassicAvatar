using System.Collections.Concurrent;
using Avatar.Core.Dyeing;
using Avatar.Core.Layout;
using Avatar.Core.Models;
using WzReader.Core.Codecs;
using WzReader.Core.Diagnostics;
using WzReader.Core.Dom;
using WzReader.Core.Package;

namespace Avatar.Core.Assets;

/// <summary>
/// Avatar 数据入口：封装一个 WZ <c>Data</c> 目录，并提供“物品目录 + 部件解析 + 像素/染色缓存”。
/// </summary>
/// <remarks>
/// 该类是 Assets 层唯一的对外门面，上层（Rules/Layout/Rendering/Export）只依赖它，不直接接触 <see cref="WzDataRoot"/>。
/// <para>
/// 线程安全：内部所有缓存都是 <see cref="ConcurrentDictionary{TKey,TValue}"/>；
/// <see cref="WzCanvas.Frames"/> 本身带解码缓存，因此“同一个画布被多个部件/多个线程使用”是安全的。
/// 渲染可以按角色/动作并行，但同一次 <see cref="Dispose"/> 之后不可再使用。
/// </para>
/// </remarks>
public sealed class AvatarData : IDisposable
{
    private readonly WzDataRoot _root;
    private readonly bool _ownsRoot;

    /// <summary>部件解析缓存：键为部件节点绝对路径。</summary>
    private readonly ConcurrentDictionary<string, PartDefinition?> _partCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>染色结果缓存：键为（源像素, 染色参数键）。源像素引用相等即可命中。</summary>
    private readonly ConcurrentDictionary<(WzPixelBuffer Source, string DyeKey), WzPixelBuffer> _dyeCache = new();

    /// <summary>已报告过的失败路径，避免同一路径反复写诊断。</summary>
    private readonly ConcurrentDictionary<string, byte> _reported = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>层序表（懒加载自 Base/zmap.img）。</summary>
    private ZMap? _zmap;

    /// <summary>物品本地化名称索引（String.wz 预构建，首次查询才真正工作）。</summary>
    private readonly AssetNameResolver _nameResolver;

    private AvatarData(WzDataRoot root, bool ownsRoot)
    {
        this._root = root;
        this._ownsRoot = ownsRoot;
        this.Catalog = new ItemCatalog(root, this.LoadImageNode);
        this._nameResolver = new AssetNameResolver(root);
    }

    /// <summary>
    /// 打开一个 <c>Data</c> 目录。
    /// </summary>
    /// <param name="dataDirectory">数据目录（包含 <c>Character</c> 等子目录）。</param>
    /// <param name="options">包读取选项；为 <c>null</c> 时使用默认值。</param>
    /// <returns>可释放的数据入口。</returns>
    public static AvatarData Open(string dataDirectory, WzOpenOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        WzDataRoot root = WzDataRoot.Open(dataDirectory, options);
        return new AvatarData(root, ownsRoot: true);
    }

    /// <summary>
    /// 复用已有的 <see cref="WzDataRoot"/>（例如地图工程已经打开同一目录时），避免重复打开文件。
    /// </summary>
    /// <param name="root">已打开的数据根。</param>
    /// <param name="ownsRoot">本对象是否负责释放 <paramref name="root"/>。</param>
    /// <returns>数据入口。</returns>
    public static AvatarData FromRoot(WzDataRoot root, bool ownsRoot = false)
    {
        ArgumentNullException.ThrowIfNull(root);
        return new AvatarData(root, ownsRoot);
    }

    /// <summary>底层 WZ 数据根（地图拼合等高级场景可直接使用）。</summary>
    public WzDataRoot Root => this._root;

    /// <summary>WZ 读取诊断收集器。</summary>
    public WzDiagnostics Diagnostics => this._root.Diagnostics;

    /// <summary>物品目录（按槽位/ID 取元数据、按目录枚举）。</summary>
    public ItemCatalog Catalog { get; }

    /// <summary>本地化名称解析器（按物品 ID → 游戏名）。</summary>
    public AssetNameResolver Names => this._nameResolver;

    /// <summary>按物品 ID 取本地化名称；未索引到返回 <c>null</c>。</summary>
    /// <param name="itemId">物品 ID。</param>
    public string? TryGetItemName(int itemId) => this._nameResolver.TryGetName(itemId);

    /// <summary>
    /// 按物品 ID 取本地化名称，兜底端出 <c>"名称 (ID)"</c> 或 <c>"#ID"</c>。
    /// </summary>
    /// <param name="itemId">物品 ID。</param>
    public string GetItemName(int itemId) => this._nameResolver.GetDisplayName(itemId);

    /// <summary>
    /// 图层顺序表（从 <c>Base/zmap.img</c> 加载）。
    /// </summary>
    /// <remarks>
    /// 懒加载：首次访问时读取 zmap.img；若 Base 包不可用则返回 <see cref="ZMap.Empty"/>，
    /// 此时 <see cref="Layout.LayerOrder"/> 会回退到内置基础层序表。
    /// </remarks>
    public ZMap ZMap
    {
        get
        {
            if (this._zmap == null)
            {
                WzNode? node = this._root.FindNode("Base/zmap.img");
                this._zmap = ZMap.Load(node);
            }

            return this._zmap;
        }
    }

    /// <summary>
    /// 按绝对路径查找节点（转发到 <see cref="WzDataRoot.FindNode"/>）。
    /// </summary>
    /// <param name="path">相对 Data 根的路径。</param>
    /// <returns>节点或 <c>null</c>。</returns>
    public WzNode? FindNode(string path) => this._root.FindNode(path);

    /// <summary>
    /// 解析部件节点为 <see cref="PartDefinition"/>，结果按节点路径缓存。
    /// </summary>
    /// <param name="node">部件节点（画布、UOL 或占位画布）。</param>
    /// <param name="part">解析结果。</param>
    /// <param name="error">失败原因（首次失败时写入诊断）。</param>
    /// <returns>成功返回 <c>true</c>。</returns>
    public bool TryGetPart(WzNode node, out PartDefinition? part, out string? error)
    {
        ArgumentNullException.ThrowIfNull(node);

        string key = node.Path;
        if (this._partCache.TryGetValue(key, out PartDefinition? cached))
        {
            part = cached;
            error = cached == null ? "部件链接无法解析（已缓存失败）。" : null;
            return cached != null;
        }

        bool ok = PartResolver.TryResolve(node, path => this._root.FindNode(path), out PartDefinition? resolved, out string? resolveError);
        this._partCache[key] = ok ? resolved : null;

        if (!ok && this._reported.TryAdd(key, 0))
        {
            this.Diagnostics.Warn(resolveError ?? "部件解析失败。", key);
        }

        part = resolved;
        error = resolveError;
        return ok;
    }

    /// <summary>
    /// 取部件某个帧的像素（已按需染色）。
    /// </summary>
    /// <param name="part">部件定义。</param>
    /// <param name="frameIndex">期望帧下标。</param>
    /// <param name="fallback">越界策略。</param>
    /// <param name="dye">染色参数；<c>null</c> 表示原样返回。</param>
    /// <param name="note">失败或降级说明。</param>
    /// <returns>像素缓冲区；失败返回 <c>null</c>。</returns>
    /// <remarks>
    /// 返回的缓冲区可能是 <see cref="WzCanvas.Frames"/> 中的共享对象（未染色时），
    /// 调用方 <b>不得修改</b>；渲染层只做只读合成。
    /// </remarks>
    public WzPixelBuffer? GetPartFrame(
        PartDefinition part,
        int frameIndex,
        FrameFallbackMode fallback,
        DyeSpec? dye,
        out string? note)
    {
        ArgumentNullException.ThrowIfNull(part);
        note = null;

        IReadOnlyList<WzPixelBuffer> frames = part.Canvas.Frames;
        if (frames.Count == 0)
        {
            note = $"画布没有可用帧：{part.CanvasNode.Path}";
            return null;
        }

        int index = SelectFrameIndex(frameIndex, frames.Count, fallback);
        if (index < 0)
        {
            note = $"帧 {frameIndex} 超出范围（共 {frames.Count} 帧，策略 Skip）：{part.CanvasNode.Path}";
            return null;
        }

        WzPixelBuffer source;
        try
        {
            source = frames[index];
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            note = $"像素解码失败：{exception.Message}";
            if (this._reported.TryAdd($"frame:{part.CanvasNode.Path}", 0))
            {
                this.Diagnostics.Warn(note, part.CanvasNode.Path);
            }

            return null;
        }

        if (dye == null)
        {
            return source;
        }

        string dyeKey = dye.ToCacheKey();
        return this._dyeCache.GetOrAdd((source, dyeKey), _ => DyeEngine.Apply(source, dye));
    }

    /// <summary>
    /// 按槽位枚举物品（转发到 <see cref="ItemCatalog.Enumerate(Avatar.Core.Models.EquipSlot, int)"/>）。
    /// </summary>
    /// <param name="slot">槽位。</param>
    /// <param name="maxCount">最多数量；0 表示不限制。</param>
    /// <returns>物品描述列表。</returns>
    public IReadOnlyList<ItemDescriptor> Enumerate(EquipSlot slot, int maxCount = 0) =>
        this.Catalog.Enumerate(slot, maxCount);

    /// <summary>
    /// 按槽位枚举物品 ID（不解析 img 内容，适合增量加载的列表）。
    /// </summary>
    /// <param name="slot">槽位。</param>
    /// <returns>物品 ID 列表（按 ID 升序）。</returns>
    public IReadOnlyList<int> EnumerateIds(EquipSlot slot) => this.Catalog.EnumerateIds(slot);

    /// <summary>
    /// 按槽位枚举物品 ID，并按性别过滤。
    /// </summary>
    /// <param name="slot">槽位。</param>
    /// <param name="gender">性别过滤：<c>null</c> 返回全部；
    /// <see cref="Gender.Unisex"/> 仅返回不限性别物品；
    /// <see cref="Gender.Male"/> / <see cref="Gender.Female"/> 返回对应性别 + 不限性别物品。</param>
    /// <returns>物品 ID 列表（按 ID 升序）。</returns>
    public IReadOnlyList<int> EnumerateIds(EquipSlot slot, Gender? gender) => this.Catalog.EnumerateIds(slot, gender);

    /// <summary>
    /// 按槽位枚举物品描述，并按性别过滤。
    /// </summary>
    /// <param name="slot">槽位。</param>
    /// <param name="maxCount">最多数量；0 表示不限制。</param>
    /// <param name="gender">性别过滤，语义同 <see cref="EnumerateIds(Avatar.Core.Models.EquipSlot, Avatar.Core.Models.Gender?)"/>。</param>
    /// <returns>描述列表（按 ID 升序）。</returns>
    public IReadOnlyList<ItemDescriptor> Enumerate(EquipSlot slot, int maxCount, Gender? gender) =>
        this.Catalog.Enumerate(slot, maxCount, gender);

    /// <summary>
    /// 清空全部解析缓存（部件定义、染色结果、物品描述），用于手动回收内存。
    /// </summary>
    /// <remarks>
    /// 底层 WZ 包仍保持打开，因此清理后再次渲染会按需重新解析，
    /// 只是放弃已缓存的对象，不影响功能正确性。
    /// </remarks>
    public void ClearCaches()
    {
        this._partCache.Clear();
        this._dyeCache.Clear();
        this.Catalog.Clear();
    }

    /// <summary>
    /// 清空某槽位相关的缓存（该槽位的物品描述、部件定义、染色结果）。
    /// </summary>
    /// <param name="slot">槽位。</param>
    public void ClearCaches(EquipSlot slot)
    {
        this.Catalog.Clear(slot);
        this._partCache.Clear();
        this._dyeCache.Clear();
    }

    /// <summary>
    /// 释放某槽位相关的全部资源：物品描述、部件/染色缓存，以及该槽位包内已解析的 img 画布。
    /// </summary>
    /// <param name="slot">槽位。</param>
    /// <remarks>
    /// 会调用对应 WZ 包的 <see cref="WzReader.Core.Package.WzPackage.UnparseImages"/>，
    /// 把已解析的 <c>WzCanvas</c>（含 <c>CompressedData</c> 字节数组）与子节点树交还给 GC。
    /// 调用后再次访问该槽位物品会按需重新解析，不影响功能正确性。
    /// </remarks>
    public void ClearImageCaches(EquipSlot slot)
    {
        this.Catalog.Clear(slot);
        this._partCache.Clear();
        this._dyeCache.Clear();

        WzPackage? package = this._root.GetPackage(ItemPathResolver.GetPackagePath(slot));
        package?.UnparseImages();
    }

    /// <summary>
    /// 释放全部缓存并反解析所有已加载包中的 img，回到接近"刚打开"的内存状态。
    /// </summary>
    /// <remarks>
    /// 仅作用于已加载的包，不会触发未打开包的加载；
    /// 再次渲染时按需重新解析 img，不影响功能正确性。
    /// </remarks>
    public void ClearImageCaches()
    {
        this._partCache.Clear();
        this._dyeCache.Clear();
        this.Catalog.Clear();
        this._root.UnparseAllImages();
    }

    /// <summary>
    /// 释放某槽位的物品描述与 WZ 包（若该槽位使用独立包），包会在下次访问时重新打开。
    /// </summary>
    /// <param name="slot">槽位。</param>
    /// <returns>是否实际关闭了一个已加载的包。</returns>
    public bool ReleaseSlotPackage(EquipSlot slot)
    {
        this.Catalog.Clear(slot);
        this._partCache.Clear();
        this._dyeCache.Clear();
        return this._root.ClosePackage(ItemPathResolver.GetPackagePath(slot));
    }

    /// <summary>释放底层数据根（仅当本对象拥有它时）。</summary>
    public void Dispose()
    {
        this._partCache.Clear();
        this._dyeCache.Clear();
        if (this._ownsRoot)
        {
            this._root.Dispose();
        }
    }

    /// <summary>物品 img 定位委托：只接受真正的 img 节点。</summary>
    private WzNode? LoadImageNode(EquipSlot slot, int itemId)
    {
        WzNode? node = this._root.FindNode(ItemPathResolver.GetImagePath(slot, itemId));
        return node is { IsImage: true } ? node : null;
    }

    /// <summary>
    /// 按回退策略选择帧下标；<see cref="FrameFallbackMode.Skip"/> 越界时返回 -1。
    /// </summary>
    private static int SelectFrameIndex(int frameIndex, int frameCount, FrameFallbackMode fallback)
    {
        if (frameCount <= 0)
        {
            return -1;
        }

        return fallback switch
        {
            FrameFallbackMode.Loop => ((frameIndex % frameCount) + frameCount) % frameCount,
            FrameFallbackMode.Skip => frameIndex >= 0 && frameIndex < frameCount ? frameIndex : -1,
            _ => Math.Clamp(frameIndex, 0, frameCount - 1),
        };
    }
}
