namespace Avatar.Core.Models;

/// <summary>
/// 角色属性。仅用于装备需求校验，不做任何数值计算。
/// </summary>
public sealed record Stats
{
    /// <summary>力量。</summary>
    public int STR { get; init; }

    /// <summary>敏捷。</summary>
    public int DEX { get; init; }

    /// <summary>智力。</summary>
    public int INT { get; init; }

    /// <summary>运气。</summary>
    public int LUK { get; init; }
}

/// <summary>
/// 角色外观档案：渲染所需的全部输入集中在一个对象里，便于存档、网络传输与判定。
/// </summary>
/// <remarks>
/// 设计取舍：
/// <list type="bullet">
/// <item>物品用 <b>int ID</b> 而不是路径——与游戏数据一致，路径由 <c>ItemPathResolver</c> 统一生成；</item>
/// <item>发型/脸/肤色独立字段——它们不是普通装备槽（发型在本数据集里 <c>cash=1</c>，脸是表情），单独建模语义清晰；</item>
/// <item>现金装备染色用字典——任意槽位都可能染色，避免为每个槽位增加 Dye 字段；</item>
/// <item><see cref="Job"/> 使用与 <c>info/reqJob</c> 对齐的位掩码（1=战士、2=法师、4=弓手、8=飞侠、16=海盗）。</item>
/// </list>
/// </remarks>
public sealed record CharacterProfile
{
    /// <summary>存档结构版本，用于未来迁移。</summary>
    public int SchemaVersion { get; init; } = 1;

    /// <summary>角色标识（用于导出目录名；为空时使用 <c>avatar</c>）。</summary>
    public string? Id { get; init; }

    /// <summary>角色名（仅展示用途）。</summary>
    public string? Name { get; init; }

    /// <summary>性别。</summary>
    public Gender Gender { get; init; } = Gender.Unisex;

    /// <summary>肤色下标 0..6，对应身体 <c>0000200N</c> 与头部 <c>0001200N</c>。</summary>
    public int SkinIndex { get; init; }

    /// <summary>等级。</summary>
    public int Level { get; init; } = 1;

    /// <summary>职业位掩码（0=新手/不限）。</summary>
    public int Job { get; init; }

    /// <summary>属性（可空，用于 reqXXX 校验）。</summary>
    public Stats? Stats { get; init; }

    /// <summary>发型 ID（如 30000）。</summary>
    public int HairId { get; init; } = 30000;

    /// <summary>脸 ID（如 20000）。</summary>
    public int FaceId { get; init; } = 20000;

    /// <summary>帽子 ID。</summary>
    public int? CapId { get; init; }

    /// <summary>上衣 ID。</summary>
    public int? CoatId { get; init; }

    /// <summary>长袍/套服 ID。</summary>
    public int? LongcoatId { get; init; }

    /// <summary>裤裙 ID。</summary>
    public int? PantsId { get; init; }

    /// <summary>鞋 ID。</summary>
    public int? ShoesId { get; init; }

    /// <summary>手套 ID。</summary>
    public int? GloveId { get; init; }

    /// <summary>盾 ID。</summary>
    public int? ShieldId { get; init; }

    /// <summary>披风 ID。</summary>
    public int? CapeId { get; init; }

    /// <summary>武器 ID。</summary>
    public int? WeaponId { get; init; }

    /// <summary>脸饰 ID（Af）。</summary>
    public int? FaceAccessoryId { get; init; }

    /// <summary>眼饰 ID（Ay）。</summary>
    public int? EyeAccessoryId { get; init; }

    /// <summary>耳饰 ID（Ae）。</summary>
    public int? EarAccessoryId { get; init; }

    /// <summary>戒指 ID 列表（最多 4 枚，无外观）。</summary>
    public IReadOnlyList<int> RingIds { get; init; } = Array.Empty<int>();

    /// <summary>发型染色。</summary>
    public DyeSpec? HairDye { get; init; }

    /// <summary>
    /// 现金装备/普通装备染色表。键支持三种写法（按优先级从高到低）：
    /// <c>"Cap:01002000"</c>（槽位:八位 ID）、<c>"01002000"</c>（八位 ID）、<c>"Cap"</c>（槽位）。
    /// </summary>
    public IReadOnlyDictionary<string, DyeSpec>? ItemDyes { get; init; }

    /// <summary>默认动作（渲染时未显式指定动作则使用）。</summary>
    public string DefaultAction { get; init; } = "stand1";

    /// <summary>是否水平翻转（面向左）。</summary>
    public bool FlipX { get; init; }

    /// <summary>
    /// 规范化：清理互斥槽位与非法值，返回新的档案对象（不修改原对象）。
    /// </summary>
    /// <remarks>
    /// 规则与客户端一致：同时存在长袍与上衣/裤裙时保留长袍；肤色夹在 0..6；戒指去重并限制 4 枚。
    /// </remarks>
    /// <returns>规范化后的档案。</returns>
    public CharacterProfile Normalize()
    {
        int skin = Math.Clamp(this.SkinIndex, 0, 6);
        int hair = this.HairId > 0 ? this.HairId : 30000;
        int face = this.FaceId > 0 ? this.FaceId : 20000;

        bool hasLongcoat = this.LongcoatId.HasValue;
        int? coat = hasLongcoat ? null : this.CoatId;
        int? pants = hasLongcoat ? null : this.PantsId;

        IReadOnlyList<int> rings = this.RingIds
            .Where(static id => id > 0)
            .Distinct()
            .Take(4)
            .ToArray();

        return this with
        {
            SkinIndex = skin,
            HairId = hair,
            FaceId = face,
            CoatId = coat,
            PantsId = pants,
            RingIds = rings,
        };
    }
}
