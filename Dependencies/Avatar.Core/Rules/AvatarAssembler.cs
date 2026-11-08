using Avatar.Core.Assets;
using Avatar.Core.Models;

namespace Avatar.Core.Rules;

/// <summary>
/// 组装器：把一份 <see cref="CharacterProfile"/> 变成可直接渲染的 <see cref="AvatarBuild"/>。
/// </summary>
/// <remarks>
/// 组装是“纯规则”阶段，不产生像素：
/// <list type="number">
/// <item>规范化档案（长袍优先、肤色夹取、戒指去重）；</item>
/// <item>按槽位加载物品描述（结果带缓存）；</item>
/// <item>需求校验（性别/职业/等级/属性）→ <see cref="RequirementValidator"/>；</item>
/// <item>冲突消解（现金优先、可视槽覆盖、双手武器卸盾）→ <see cref="ConflictResolver"/>；</item>
/// <item>生成武器档案与可用动作表。</item>
/// </list>
/// 同一个 <see cref="AvatarBuild"/> 可在多个动作/多个线程间复用；换装只需重新 Build。
/// </remarks>
public static class AvatarAssembler
{
    /// <summary>
    /// 组装角色。
    /// </summary>
    /// <param name="data">数据入口。</param>
    /// <param name="profile">角色档案（内部会规范化，不修改原对象）。</param>
    /// <param name="options">选项；为 <c>null</c> 时使用默认值。</param>
    /// <returns>组装结果。</returns>
    public static AvatarBuild Build(AvatarData data, CharacterProfile profile, AvatarOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(profile);

        AvatarOptions effectiveOptions = options ?? AvatarOptions.Default;
        CharacterProfile original = profile;
        CharacterProfile normalized = profile.Normalize();

        List<AvatarConflict> conflicts = new();
        List<ResolvedItem> candidates = new();

        // 规范化会丢弃被长袍覆盖的上衣/裤裙；这里显式记录一条诊断，避免用户困惑。
        if (original.LongcoatId.HasValue)
        {
            if (original.CoatId.HasValue)
            {
                conflicts.Add(new AvatarConflict(
                    ConflictKind.VSlotCovered,
                    EquipSlot.Coat,
                    original.CoatId.Value,
                    $"已装备长袍（{original.LongcoatId.Value:D8}），上衣被覆盖。",
                    original.LongcoatId.Value));
            }

            if (original.PantsId.HasValue)
            {
                conflicts.Add(new AvatarConflict(
                    ConflictKind.VSlotCovered,
                    EquipSlot.Pants,
                    original.PantsId.Value,
                    $"已装备长袍（{original.LongcoatId.Value:D8}），裤裙被覆盖。",
                    original.LongcoatId.Value));
            }
        }

        // 身体/头部由肤色决定，永远存在。
        Add(EquipSlot.Body, ItemPathResolver.BodyIdOf(normalized.SkinIndex), null);
        Add(EquipSlot.Head, ItemPathResolver.HeadIdOf(normalized.SkinIndex), null);
        Add(EquipSlot.Hair, normalized.HairId, normalized.HairDye);
        Add(EquipSlot.Face, normalized.FaceId, ResolveDye(normalized, EquipSlot.Face, normalized.FaceId));

        Add(EquipSlot.Cap, normalized.CapId, ResolveDye(normalized, EquipSlot.Cap, normalized.CapId));
        Add(EquipSlot.Coat, normalized.CoatId, ResolveDye(normalized, EquipSlot.Coat, normalized.CoatId));
        Add(EquipSlot.Longcoat, normalized.LongcoatId, ResolveDye(normalized, EquipSlot.Longcoat, normalized.LongcoatId));
        Add(EquipSlot.Pants, normalized.PantsId, ResolveDye(normalized, EquipSlot.Pants, normalized.PantsId));
        Add(EquipSlot.Shoes, normalized.ShoesId, ResolveDye(normalized, EquipSlot.Shoes, normalized.ShoesId));
        Add(EquipSlot.Glove, normalized.GloveId, ResolveDye(normalized, EquipSlot.Glove, normalized.GloveId));
        Add(EquipSlot.Shield, normalized.ShieldId, ResolveDye(normalized, EquipSlot.Shield, normalized.ShieldId));
        Add(EquipSlot.Cape, normalized.CapeId, ResolveDye(normalized, EquipSlot.Cape, normalized.CapeId));
        Add(EquipSlot.Weapon, normalized.WeaponId, ResolveDye(normalized, EquipSlot.Weapon, normalized.WeaponId));

        Add(EquipSlot.FaceAccessory, normalized.FaceAccessoryId,
            ResolveDye(normalized, EquipSlot.FaceAccessory, normalized.FaceAccessoryId));
        Add(EquipSlot.EyeAccessory, normalized.EyeAccessoryId,
            ResolveDye(normalized, EquipSlot.EyeAccessory, normalized.EyeAccessoryId));
        Add(EquipSlot.EarAccessory, normalized.EarAccessoryId,
            ResolveDye(normalized, EquipSlot.EarAccessory, normalized.EarAccessoryId));

        for (int index = 0; index < normalized.RingIds.Count; index++)
        {
            Add(EquipSlot.Ring, normalized.RingIds[index], ResolveDye(normalized, EquipSlot.Ring, normalized.RingIds[index]));
        }

        RequirementReport requirements = RequirementValidator.Validate(normalized, effectiveOptions, candidates);
        ConflictResolution resolution = ConflictResolver.Resolve(candidates, requirements);

        conflicts.AddRange(resolution.Conflicts);

        ItemDescriptor? weaponDescriptor = resolution.Items.Weapon?.Descriptor;
        WeaponProfile weapon = WeaponProfiles.Create(weaponDescriptor);

        IReadOnlyList<string> bodyActions =
            resolution.Items.Body?.Descriptor.Actions ?? Array.Empty<string>();

        // 动作集合取“所有会参与身体动画的装备”的交集：不同武器/防具的动作集合不同，
        // 若列出某件装备不存在的动作，渲染时该装备只能被隐藏或回退到错误姿态。
        // 因此只保留交集，并在换装后重新计算（动作列表是动态结果）。
        //
        // 两类槽位不参与交集：
        // 1) 脸与脸饰/眼饰/耳饰/戒指使用表情或 default 动作（default/blink/smile…），
        //    这些槽位在所有动作下都按 default 兜底绘制，不构成动作约束；
        // 2) 武器/盾的动作只决定攻击与姿态变体，缺帧时武器隐藏即可，不应把爬梯/绳索等
        //    纯身体动作从列表中剔除。
        HashSet<string> sharedActions = new(bodyActions, StringComparer.OrdinalIgnoreCase);
        HashSet<string>? weaponActions = null;
        foreach (ResolvedItem item in resolution.Items.All)
        {
            switch (item.Slot)
            {
                case EquipSlot.Face:
                case EquipSlot.FaceAccessory:
                case EquipSlot.EyeAccessory:
                case EquipSlot.EarAccessory:
                case EquipSlot.Ring:
                    continue;

                case EquipSlot.Weapon:
                case EquipSlot.Shield:
                    if (weaponActions == null)
                    {
                        weaponActions = new HashSet<string>(item.Descriptor.Actions, StringComparer.OrdinalIgnoreCase);
                    }
                    else
                    {
                        weaponActions.IntersectWith(item.Descriptor.Actions);
                    }

                    continue;

                default:
                    sharedActions.IntersectWith(item.Descriptor.Actions);
                    break;
            }
        }

        IReadOnlyList<string> availableActions = bodyActions
            .Where(sharedActions.Contains)
            .Where(action => !IsWeaponBoundAction(action) || (weaponActions?.Contains(action) ?? true))
            .ToList();

        // 兜底：理论上不会为空（stand1 等基础动作所有物品都有）；
        // 极端数据缺失时仍保留身体动作，避免动作列表为空。
        if (availableActions.Count == 0)
        {
            availableActions = bodyActions;
        }

        return new AvatarBuild(
            data,
            normalized,
            effectiveOptions,
            resolution.Items,
            weapon,
            conflicts,
            availableActions);

        // 局部函数：按槽位/ID 加载物品，缺失时写诊断。
        void Add(EquipSlot slot, int? itemId, DyeSpec? dye)
        {
            if (itemId is not > 0)
            {
                return;
            }

            ItemDescriptor? descriptor = data.Catalog.Get(slot, itemId.Value);
            if (descriptor == null)
            {
                conflicts.Add(new AvatarConflict(
                    ConflictKind.MissingAsset,
                    slot,
                    itemId.Value,
                    $"素材不存在：{ItemPathResolver.GetImagePath(slot, itemId.Value)}"));
                return;
            }

            candidates.Add(new ResolvedItem(slot, itemId.Value, descriptor, dye));
        }
    }

    /// <summary>
    /// 动作是否必须由武器/盾决定：攻击动作与站/走姿态变体。
    /// </summary>
    /// <remarks>
    /// 这些动作的帧由武器提供，武器缺少时必须从可用动作中剔除；
    /// 其余动作（爬梯/绳索/坐下等）在武器缺帧时只是不绘制武器，仍可正常播放。
    /// </remarks>
    /// <param name="action">动作名。</param>
    /// <returns>属于武器相关动作返回 <c>true</c>。</returns>
    private static bool IsWeaponBoundAction(string action)
    {
        if (ActionResolver.IsAttackAction(action))
        {
            return true;
        }

        if (action.StartsWith("stand", StringComparison.OrdinalIgnoreCase))
        {
            return action.Length > 5 && char.IsAsciiDigit(action[^1]);
        }

        if (action.StartsWith("walk", StringComparison.OrdinalIgnoreCase))
        {
            return action.Length > 4 && char.IsAsciiDigit(action[^1]);
        }

        return false;
    }

    /// <summary>
    /// 从 <see cref="CharacterProfile.ItemDyes"/> 中查找指定物品的染色参数。
    /// </summary>
    /// <remarks>
    /// 键支持四种写法（按优先级）：<c>"Slot:01002000"</c>、<c>"01002000"</c>、<c>"Slot:2000"</c>、<c>"Slot"</c>。
    /// 键比较忽略大小写。
    /// </remarks>
    /// <param name="profile">角色档案。</param>
    /// <param name="slot">槽位。</param>
    /// <param name="itemId">物品 ID（可为 <c>null</c>）。</param>
    /// <returns>染色参数或 <c>null</c>。</returns>
    public static DyeSpec? ResolveDye(CharacterProfile profile, EquipSlot slot, int? itemId)
    {
        if (profile.ItemDyes == null || profile.ItemDyes.Count == 0 || itemId is not > 0)
        {
            return null;
        }

        string slotName = slot.ToString();
        string[] keys =
        {
            $"{slotName}:{itemId.Value:D8}",
            $"{itemId.Value:D8}",
            $"{slotName}:{itemId.Value}",
            slotName,
        };

        foreach ((string key, DyeSpec value) in profile.ItemDyes)
        {
            foreach (string candidate in keys)
            {
                if (string.Equals(key, candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return value;
                }
            }
        }

        return null;
    }
}
