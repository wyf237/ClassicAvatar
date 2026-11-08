namespace Avatar.Core.Models;

/// <summary>
/// 角色性别。WZ 的 <c>info/gender</c> 取值为 0/1/2，这里与之对齐。
/// </summary>
public enum Gender
{
    /// <summary>不限（0）：男女均可，也用于“无法判断”的默认值。</summary>
    Unisex = 0,

    /// <summary>男（1）。</summary>
    Male = 1,

    /// <summary>女（2）。</summary>
    Female = 2,
}

/// <summary>
/// 角色外观槽位。命名与 WZ 的 <c>info/islot</c> 一一对应，另加内部使用的 <see cref="Body"/> / <see cref="Head"/>。
/// </summary>
public enum EquipSlot
{
    /// <summary>身体（Bd，肤色决定具体 img）。</summary>
    Body,

    /// <summary>头部（Hd，肤色决定具体 img）。</summary>
    Head,

    /// <summary>发型（Hr）。</summary>
    Hair,

    /// <summary>脸/表情（Fc）。</summary>
    Face,

    /// <summary>帽子（Cp）。</summary>
    Cap,

    /// <summary>上衣（Ma）。</summary>
    Coat,

    /// <summary>长袍/套服（MaPn），同时占用上衣与裤裙槽。</summary>
    Longcoat,

    /// <summary>裤裙（Pn）。</summary>
    Pants,

    /// <summary>鞋（So）。</summary>
    Shoes,

    /// <summary>手套（Gv）。</summary>
    Glove,

    /// <summary>盾/副手（Si）。</summary>
    Shield,

    /// <summary>披风（Sr）。</summary>
    Cape,

    /// <summary>武器（Wp 或 WpSi）。</summary>
    Weapon,

    /// <summary>脸饰（Af）。</summary>
    FaceAccessory,

    /// <summary>眼饰（Ay）。</summary>
    EyeAccessory,

    /// <summary>耳饰（Ae）。</summary>
    EarAccessory,

    /// <summary>戒指（Ri，无外观，仅参与校验）。</summary>
    Ring,
}

/// <summary>
/// 槽位的静态元数据（目录名、中文名、渲染顺序）。集中在一处，避免各模块重复硬编码。
/// </summary>
public static class EquipSlotInfo
{
    /// <summary>WZ 中该槽位对应的目录名；<see cref="EquipSlot.Body"/> 与 <see cref="EquipSlot.Head"/> 位于 <c>Character</c> 根目录，返回空串。</summary>
    public static string FolderOf(EquipSlot slot) => slot switch
    {
        EquipSlot.Body or EquipSlot.Head => string.Empty,
        EquipSlot.Hair => "Hair",
        EquipSlot.Face => "Face",
        EquipSlot.Cap => "Cap",
        EquipSlot.Coat => "Coat",
        EquipSlot.Longcoat => "Longcoat",
        EquipSlot.Pants => "Pants",
        EquipSlot.Shoes => "Shoes",
        EquipSlot.Glove => "Glove",
        EquipSlot.Shield => "Shield",
        EquipSlot.Cape => "Cape",
        EquipSlot.Weapon => "Weapon",
        EquipSlot.FaceAccessory or EquipSlot.EyeAccessory or EquipSlot.EarAccessory => "Accessory",
        EquipSlot.Ring => "Ring",
        _ => string.Empty,
    };

    /// <summary>中文显示名（用于诊断与文档）。</summary>
    public static string DisplayNameOf(EquipSlot slot) => slot switch
    {
        EquipSlot.Body => "身体",
        EquipSlot.Head => "头部",
        EquipSlot.Hair => "发型",
        EquipSlot.Face => "脸",
        EquipSlot.Cap => "帽子",
        EquipSlot.Coat => "上衣",
        EquipSlot.Longcoat => "长袍",
        EquipSlot.Pants => "裤裙",
        EquipSlot.Shoes => "鞋",
        EquipSlot.Glove => "手套",
        EquipSlot.Shield => "盾",
        EquipSlot.Cape => "披风",
        EquipSlot.Weapon => "武器",
        EquipSlot.FaceAccessory => "脸饰",
        EquipSlot.EyeAccessory => "眼饰",
        EquipSlot.EarAccessory => "耳饰",
        EquipSlot.Ring => "戒指",
        _ => slot.ToString(),
    };

    /// <summary>
    /// 渲染/锚点注册顺序（从小到大）。该顺序同时决定“同名锚点的覆盖优先级”：
    /// 后处理的部件提供同名锚点时覆盖先前的值，从而让武器可以跟随长袍袖口等。
    /// </summary>
    public static int RenderOrderOf(EquipSlot slot) => slot switch
    {
        EquipSlot.Body => 0,
        EquipSlot.Head => 1,
        EquipSlot.Face => 2,
        EquipSlot.Hair => 3,
        EquipSlot.Cap => 4,
        EquipSlot.FaceAccessory => 5,
        EquipSlot.EyeAccessory => 6,
        EquipSlot.EarAccessory => 7,
        EquipSlot.Coat => 8,
        EquipSlot.Longcoat => 9,
        EquipSlot.Pants => 10,
        EquipSlot.Shoes => 11,
        EquipSlot.Glove => 12,
        EquipSlot.Shield => 13,
        EquipSlot.Cape => 14,
        EquipSlot.Weapon => 15,
        EquipSlot.Ring => 16,
        _ => 99,
    };

    /// <summary>该槽位是否会在画面上产生像素。</summary>
    public static bool IsVisible(EquipSlot slot) => slot is not EquipSlot.Ring;

    /// <summary>
    /// 该槽位在 WZ 未声明 <c>z</c> 时的兜底层名（用于避免空图层被排到最前方造成图层错乱）。
    /// </summary>
    /// <param name="slot">装备槽位。</param>
    /// <returns>兜底 z 名；无合适兜底时返回空串。</returns>
    public static string DefaultLayerOf(EquipSlot slot) => slot switch
    {
        EquipSlot.Body => "body",
        EquipSlot.Head => "head",
        EquipSlot.Hair => "hair",
        EquipSlot.Face => "face",
        EquipSlot.Cap => "cap",
        EquipSlot.Coat or EquipSlot.Longcoat => "mailChest",
        EquipSlot.Pants => "pants",
        EquipSlot.Shoes => "shoes",
        EquipSlot.Glove => "glove",
        EquipSlot.Shield => "shield",
        EquipSlot.Cape => "cape",
        EquipSlot.Weapon => "weapon",
        EquipSlot.FaceAccessory or EquipSlot.EyeAccessory => "accessoryFace",
        EquipSlot.EarAccessory => "accessoryEar",
        _ => string.Empty,
    };
}

/// <summary>
/// 武器动作类别。注意：<b>动作类别与槽位语义无关</b>——
/// 拳套（claw）占用 <c>WpSi</c>（会卸盾）但动作是单手 <c>swingO*</c>、站姿 <c>stand1</c>。
/// </summary>
public enum WeaponCategory
{
    /// <summary>空手。</summary>
    None,

    /// <summary>单手剑（0130）。</summary>
    OneHandSword,

    /// <summary>单手斧（0131）。</summary>
    OneHandAxe,

    /// <summary>单手钝器（0132）。</summary>
    OneHandBlunt,

    /// <summary>短剑（0133）。</summary>
    Dagger,

    /// <summary>短杖（0137）。</summary>
    Wand,

    /// <summary>长杖（0138）。</summary>
    Staff,

    /// <summary>混合型特例（0139），动作以数据实际节点为准。</summary>
    Mixed,

    /// <summary>双手剑（0140）。</summary>
    TwoHandSword,

    /// <summary>双手斧（0141）。</summary>
    TwoHandAxe,

    /// <summary>双手钝器（0142）。</summary>
    TwoHandBlunt,

    /// <summary>长枪（0143）。</summary>
    Spear,

    /// <summary>长矛（0144）。</summary>
    Polearm,

    /// <summary>弓（0145）。</summary>
    Bow,

    /// <summary>弩（0146）。</summary>
    Crossbow,

    /// <summary>拳套（0147）。</summary>
    Claw,

    /// <summary>现金武器（0170 及未知前缀）。</summary>
    Cash,
}

/// <summary>染色算法。</summary>
public enum DyeMode
{
    /// <summary>替换色相/饱和度，保留亮度（默认，适合发型与布料整体换色）。</summary>
    HueReplace,

    /// <summary>整体乘法压色（<c>C' = C * Tint</c>），适合加深/快速偏色。</summary>
    Multiply,

    /// <summary>调色板映射：仅把接近 <see cref="ColorPair.Source"/> 的颜色替换为 <see cref="ColorPair.Target"/>，金属件不受影响。</summary>
    PaletteMap,

    /// <summary>先灰度化再替换色相，用于消除原素材的杂色。</summary>
    GrayscaleHue,
}

/// <summary>装备需求校验模式。</summary>
public enum ValidationMode
{
    /// <summary>只产生诊断，不剔除物品（默认，适合试衣间）。</summary>
    Warn,

    /// <summary>不符合需求即剔除物品。</summary>
    Error,
}

/// <summary>性别不匹配时的处理策略。</summary>
public enum GenderPolicy
{
    /// <summary>按 <c>info/gender</c> 或 ID 推断后仅告警（默认）。</summary>
    InferOnly,

    /// <summary>严格校验，不匹配即剔除。</summary>
    Strict,

    /// <summary>完全忽略性别限制。</summary>
    Ignore,
}

/// <summary>部件帧数不一致时的回退策略。</summary>
public enum FrameFallbackMode
{
    /// <summary>越界取最后一帧（默认）。</summary>
    Clamp,

    /// <summary>按下标取模循环。</summary>
    Loop,

    /// <summary>越界直接跳过该部件的这一帧。</summary>
    Skip,
}

/// <summary>冲突/诊断种类。</summary>
public enum ConflictKind
{
    /// <summary>现金装备覆盖普通装备。</summary>
    CashOverrides,

    /// <summary>被可视槽（vslot）包含关系覆盖（如长袍盖住上衣/裤子）。</summary>
    VSlotCovered,

    /// <summary>同一槽位出现多个物品，只保留优先级最高的一件。</summary>
    DuplicateSlot,

    /// <summary>装备双手武器（WpSi）导致盾/副手被卸下。</summary>
    ShieldUnequipped,

    /// <summary>性别不符。</summary>
    GenderMismatch,

    /// <summary>职业不符。</summary>
    JobMismatch,

    /// <summary>等级不足。</summary>
    LevelTooLow,

    /// <summary>属性不足。</summary>
    StatTooLow,

    /// <summary>素材 img 不存在。</summary>
    MissingAsset,

    /// <summary>链接（uol/_outlink/_inlink）解析失败或目标类型错误。</summary>
    MissingLink,

    /// <summary>锚点缺失，已降级放置。</summary>
    MissingAnchor,

    /// <summary>未知图层名，已按基础顺序兜底。</summary>
    UnknownLayer,

    /// <summary>动作不存在，已回退到其他动作。</summary>
    ActionFallback,

    /// <summary>物品在该动作下没有对应部件。</summary>
    PartMissing,

    /// <summary>帧数不足，已按回退策略取帧。</summary>
    FrameFallback,
}
