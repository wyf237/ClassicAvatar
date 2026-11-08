using Avatar.Core.Assets;
using WzReader.Core.Codecs;

namespace Avatar.Core.Models;

/// <summary>
/// 武器档案：把“槽位语义”和“动作语义”分开建模。
/// </summary>
/// <param name="Category">动作类别。</param>
/// <param name="ItemId">武器 ID（空手为 0）。</param>
/// <param name="StandVariant">站姿变体（<c>stand1</c>/<c>stand2</c> 的数字）。</param>
/// <param name="WalkVariant">行走变体（<c>walk1</c>/<c>walk2</c> 的数字）。</param>
/// <param name="OccupiesShieldSlot">是否占用盾槽（<c>vslot</c> 含 <c>Si</c>）。</param>
/// <param name="IsCash">是否现金武器。</param>
/// <param name="PreferredActions">该类别首选动作序列（第一个为回退首选）。</param>
public sealed record WeaponProfile(
    WeaponCategory Category,
    int ItemId,
    int StandVariant,
    int WalkVariant,
    bool OccupiesShieldSlot,
    bool IsCash,
    IReadOnlyList<string> PreferredActions)
{
    /// <summary>空手档案（stand1/walk1，单手挥砍动作）。</summary>
    public static WeaponProfile Empty { get; } = new(
        WeaponCategory.None,
        0,
        1,
        1,
        false,
        false,
        new[] { "swingO1", "swingO2", "swingO3" });

    /// <summary>回退首选动作。</summary>
    public string PrimaryAction => this.PreferredActions.Count > 0 ? this.PreferredActions[0] : "swingO1";

    /// <summary>是否为双手动作体系（stand2/walk2 与 swingT/P 系）。</summary>
    public bool IsTwoHandedMotion => this.Category is
        WeaponCategory.TwoHandSword or WeaponCategory.TwoHandAxe or WeaponCategory.TwoHandBlunt or
        WeaponCategory.Spear or WeaponCategory.Polearm or WeaponCategory.Crossbow;
}

/// <summary>
/// 当前生效的一件外观物品（已经通过需求校验与冲突消解）。
/// </summary>
public sealed class ResolvedItem
{
    /// <summary>创建生效物品。</summary>
    /// <param name="slot">槽位。</param>
    /// <param name="itemId">物品 ID。</param>
    /// <param name="descriptor">物品元数据。</param>
    /// <param name="dye">染色参数（可为空）。</param>
    public ResolvedItem(EquipSlot slot, int itemId, ItemDescriptor descriptor, DyeSpec? dye)
    {
        this.Slot = slot;
        this.ItemId = itemId;
        this.Descriptor = descriptor;
        this.Dye = dye;
    }

    /// <summary>槽位。</summary>
    public EquipSlot Slot { get; }

    /// <summary>物品 ID。</summary>
    public int ItemId { get; }

    /// <summary>物品元数据。</summary>
    public ItemDescriptor Descriptor { get; }

    /// <summary>染色参数。</summary>
    public DyeSpec? Dye { get; }
}

/// <summary>
/// 按槽位索引的生效物品集合。
/// </summary>
public sealed class AvatarResolvedItems
{
    private readonly Dictionary<EquipSlot, ResolvedItem> _bySlot;

    /// <summary>创建集合。</summary>
    /// <param name="items">按渲染顺序排好序的物品列表。</param>
    internal AvatarResolvedItems(IReadOnlyList<ResolvedItem> items)
    {
        this.All = items;
        this._bySlot = new Dictionary<EquipSlot, ResolvedItem>();
        foreach (ResolvedItem item in items)
        {
            // 戒指允许多枚，其余槽位冲突消解后应唯一；这里保留第一个即可。
            this._bySlot.TryAdd(item.Slot, item);
        }
    }

    /// <summary>全部生效物品（按渲染顺序）。</summary>
    public IReadOnlyList<ResolvedItem> All { get; }

    /// <summary>按槽位取物品；不存在返回 <c>null</c>。</summary>
    /// <param name="slot">槽位。</param>
    /// <returns>物品或 <c>null</c>。</returns>
    public ResolvedItem? Get(EquipSlot slot) => this._bySlot.GetValueOrDefault(slot);

    /// <summary>身体。</summary>
    public ResolvedItem? Body => this.Get(EquipSlot.Body);

    /// <summary>头部。</summary>
    public ResolvedItem? Head => this.Get(EquipSlot.Head);

    /// <summary>发型。</summary>
    public ResolvedItem? Hair => this.Get(EquipSlot.Hair);

    /// <summary>脸。</summary>
    public ResolvedItem? Face => this.Get(EquipSlot.Face);

    /// <summary>武器。</summary>
    public ResolvedItem? Weapon => this.Get(EquipSlot.Weapon);

    /// <summary>是否包含某槽位。</summary>
    /// <param name="slot">槽位。</param>
    /// <returns>包含返回 <c>true</c>。</returns>
    public bool Contains(EquipSlot slot) => this._bySlot.ContainsKey(slot);
}

/// <summary>
/// 角色组装结果：包含档案、生效物品、武器档案、可用动作与全部诊断。
/// </summary>
/// <remarks>
/// <see cref="AvatarBuild"/> 创建后不可变（物品集合与诊断列表都不再改变），
/// 因此可以在多个线程/多个动作之间安全复用；渲染只读访问它。
/// </remarks>
public sealed class AvatarBuild
{
    /// <summary>创建组装结果。</summary>
    /// <param name="data">数据根。</param>
    /// <param name="profile">规范化后的档案。</param>
    /// <param name="options">选项。</param>
    /// <param name="items">生效物品。</param>
    /// <param name="weapon">武器档案。</param>
    /// <param name="conflicts">诊断。</param>
    /// <param name="availableActions">身体提供的动作名。</param>
    internal AvatarBuild(
        AvatarData data,
        CharacterProfile profile,
        AvatarOptions options,
        AvatarResolvedItems items,
        WeaponProfile weapon,
        IReadOnlyList<AvatarConflict> conflicts,
        IReadOnlyList<string> availableActions)
    {
        this.Data = data;
        this.Profile = profile;
        this.Options = options;
        this.Items = items;
        this.Weapon = weapon;
        this.Conflicts = conflicts;
        this.AvailableActions = availableActions;
    }

    /// <summary>数据根。</summary>
    public AvatarData Data { get; }

    /// <summary>规范化后的档案。</summary>
    public CharacterProfile Profile { get; }

    /// <summary>选项。</summary>
    public AvatarOptions Options { get; }

    /// <summary>生效物品。</summary>
    public AvatarResolvedItems Items { get; }

    /// <summary>武器档案（空手时为 <see cref="WeaponProfile.Empty"/>）。</summary>
    public WeaponProfile Weapon { get; }

    /// <summary>组装期诊断（覆盖、缺失、性别等）。</summary>
    public IReadOnlyList<AvatarConflict> Conflicts { get; }

    /// <summary>身体提供、可渲染的动作名。</summary>
    public IReadOnlyList<string> AvailableActions { get; }

    /// <summary>是否启用水平翻转。</summary>
    public bool FlipX => this.Options.FlipX || this.Profile.FlipX;

    /// <summary>是否存在指定动作。</summary>
    /// <param name="action">动作名。</param>
    /// <returns>存在返回 <c>true</c>。</returns>
    public bool HasAction(string action) =>
        this.AvailableActions.Any(name => string.Equals(name, action, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// 一个已放置的部件（定位结果）。
/// </summary>
/// <param name="ItemId">来源物品 ID（身体/头部也可有 ID）。</param>
/// <param name="Slot">来源槽位。</param>
/// <param name="PartName">WZ 部件节点名（body/arm/head/mail/weapon…）。</param>
/// <param name="Layer">WZ 的 <c>z</c> 图层名。</param>
/// <param name="Group">WZ 的 <c>group</c>（skin/其他）。</param>
/// <param name="X">部件画布左上角在角色空间的 X。</param>
/// <param name="Y">部件画布左上角在角色空间的 Y。</param>
/// <param name="Width">画布宽度。</param>
/// <param name="Height">画布高度。</param>
/// <param name="SourcePath">真实像素节点的绝对路径（解析 _outlink 之后）。</param>
/// <param name="IsBack">是否背向图层（以 <c>back</c> 开头）。</param>
public sealed record PlacedPart(
    int ItemId,
    EquipSlot Slot,
    string PartName,
    string Layer,
    string Group,
    int X,
    int Y,
    int Width,
    int Height,
    string SourcePath,
    bool IsBack)
{
    /// <summary>携带 <c>origin</c>/<c>map</c>/<c>z</c> 的元数据节点路径（通常与 <see cref="SourcePath"/> 不同）。</summary>
    public string MetadataPath { get; init; } = string.Empty;
}

/// <summary>
/// 渲染出的一帧。
/// </summary>
public sealed class AvatarFrame
{
    /// <summary>创建一帧。</summary>
    internal AvatarFrame(
        string requestedAction,
        string action,
        int frameIndex,
        int delayMs,
        int bodyFaceIndex,
        bool isOverlayAction,
        WzPixelBuffer pixels,
        AvatarCanvas canvas,
        IReadOnlyList<PlacedPart> parts,
        IReadOnlyDictionary<string, PointI> anchors,
        IReadOnlyList<string> notes)
    {
        this.RequestedAction = requestedAction;
        this.Action = action;
        this.FrameIndex = frameIndex;
        this.DelayMs = delayMs;
        this.BodyFaceIndex = bodyFaceIndex;
        this.IsOverlayAction = isOverlayAction;
        this.Pixels = pixels;
        this.Canvas = canvas;
        this.Parts = parts;
        this.Anchors = anchors;
        this.Notes = notes;
    }

    /// <summary>调用方请求的动作名（未展开 stand1/stand2）。</summary>
    public string RequestedAction { get; }

    /// <summary>实际使用的动作名（可能是回退结果）。</summary>
    public string Action { get; }

    /// <summary>帧下标（0 开始）。</summary>
    public int FrameIndex { get; }

    /// <summary>帧时长（毫秒，取自身体帧的 <c>delay</c>）。</summary>
    public int DelayMs { get; }

    /// <summary>身体帧携带的表情索引（<c>face</c>）。</summary>
    public int BodyFaceIndex { get; }

    /// <summary>是否为“局部覆盖动作”（如 <c>blink</c>，身体帧缺少 arm）。</summary>
    public bool IsOverlayAction { get; }

    /// <summary>BGRA32 非预乘像素。</summary>
    public WzPixelBuffer Pixels { get; }

    /// <summary>画布在角色空间中的位置与尺寸。</summary>
    public AvatarCanvas Canvas { get; }

    /// <summary>宽度（<c>Canvas.Width</c> 的快捷方式）。</summary>
    public int Width => this.Canvas.Width;

    /// <summary>高度（<c>Canvas.Height</c> 的快捷方式）。</summary>
    public int Height => this.Canvas.Height;

    /// <summary>已放置部件（按绘制先后顺序）。</summary>
    public IReadOnlyList<PlacedPart> Parts { get; }

    /// <summary>该帧最终锚点表（调试/自定义贴图使用）。</summary>
    public IReadOnlyDictionary<string, PointI> Anchors { get; }

    /// <summary>渲染期提示（缺失动作/锚点等）。</summary>
    public IReadOnlyList<string> Notes { get; }

    /// <summary>把图像像素坐标换算为角色空间 X。</summary>
    /// <param name="imageX">图像 X。</param>
    /// <returns>角色空间 X。</returns>
    public int ToAvatarX(int imageX) => imageX + this.Canvas.Left;

    /// <summary>把图像像素坐标换算为角色空间 Y。</summary>
    /// <param name="imageY">图像 Y。</param>
    /// <returns>角色空间 Y。</returns>
    public int ToAvatarY(int imageY) => imageY + this.Canvas.Top;
}

/// <summary>
/// 一段动作的渲染结果（所有帧共用同一画布尺寸，便于直接播放/出雪碧图）。
/// </summary>
public sealed class AvatarSequence
{
    /// <summary>创建序列。</summary>
    internal AvatarSequence(string requestedAction, string action, IReadOnlyList<AvatarFrame> frames)
    {
        this.RequestedAction = requestedAction;
        this.Action = action;
        this.Frames = frames;
    }

    /// <summary>请求的动作名。</summary>
    public string RequestedAction { get; }

    /// <summary>实际使用的动作名。</summary>
    public string Action { get; }

    /// <summary>帧列表。</summary>
    public IReadOnlyList<AvatarFrame> Frames { get; }

    /// <summary>总时长（毫秒）。</summary>
    public int TotalDelayMs => this.Frames.Sum(static frame => frame.DelayMs);
}
