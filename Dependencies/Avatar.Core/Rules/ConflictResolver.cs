using Avatar.Core.Assets;
using Avatar.Core.Models;

namespace Avatar.Core.Rules;

/// <summary>
/// 冲突消解结果：最终生效物品 + 诊断。
/// </summary>
public sealed class ConflictResolution
{
    /// <summary>创建结果。</summary>
    /// <param name="items">生效物品（按渲染顺序）。</param>
    /// <param name="conflicts">诊断。</param>
    internal ConflictResolution(AvatarResolvedItems items, IReadOnlyList<AvatarConflict> conflicts)
    {
        this.Items = items;
        this.Conflicts = conflicts;
    }

    /// <summary>生效物品。</summary>
    public AvatarResolvedItems Items { get; }

    /// <summary>诊断列表。</summary>
    public IReadOnlyList<AvatarConflict> Conflicts { get; }
}

/// <summary>
/// 装备冲突消解：把候选物品裁剪成“最终真正显示”的一组。
/// </summary>
/// <remarks>
/// 实现的是需求第 3、4 点的规则，全部由数据（<c>info/vslot</c> 与 <c>info/cash</c>）驱动：
/// <list type="number">
/// <item><b>双手武器卸盾</b>：<c>vslot</c> 含 <c>Si</c> 的武器会先把盾剔除（明确的游戏语义，优先于现金规则）；</item>
/// <item><b>现金优先</b>：排序时现金件在前，后续与之共享任意可视槽的普通件被覆盖；</item>
/// <item><b>套装覆盖</b>：可视槽集合的包含关系（长袍 <c>MaPn</c> ⊇ 上衣 <c>Ma</c>/裤裙 <c>Pn</c>）；</item>
/// <item><b>同槽去重</b>：同槽同可视槽只保留优先级最高的一件。</item>
/// </list>
/// 头发/脸/身体/头部属于“基础外观”，永远不会被装备的可视槽覆盖（帽子只在图层上遮挡头发，不隐藏头发）。
/// 戒指无外观且允许多枚，不参与冲突。
/// </remarks>
public static class ConflictResolver
{
    /// <summary>
    /// 消解冲突。
    /// </summary>
    /// <param name="candidates">候选物品（描述必须非空）。</param>
    /// <param name="requirements">需求校验结果（用于剔除不符合需求的物品）。</param>
    /// <returns>消解结果。</returns>
    public static ConflictResolution Resolve(
        IReadOnlyList<ResolvedItem> candidates,
        RequirementReport requirements)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(requirements);

        List<AvatarConflict> conflicts = new(requirements.Conflicts);

        // 1) 先剔除需求不符的物品。
        List<ResolvedItem> active = new();
        for (int index = 0; index < candidates.Count; index++)
        {
            ResolvedItem item = candidates[index];
            if (!requirements.IsRejected(item.Slot, item.ItemId))
            {
                active.Add(item);
            }
        }

        // 2) 双手武器直接卸盾，这条规则优先于“现金优先”。
        //    不能只看 vslot 的 Si：部分长枪（如 1432006）的 vslot 只有 Wp，
        //    但 WeaponCategory 已明确为双手类别。
        ResolvedItem? weapon = active.FirstOrDefault(static item => item.Slot == EquipSlot.Weapon);
        if (weapon != null && IsTwoHandedWeapon(weapon.Descriptor))
        {
            List<ResolvedItem> shields = active.Where(static item => item.Slot == EquipSlot.Shield).ToList();
            foreach (ResolvedItem shield in shields)
            {
                active.Remove(shield);
                conflicts.Add(new AvatarConflict(
                    ConflictKind.ShieldUnequipped,
                    EquipSlot.Shield,
                    shield.ItemId,
                    $"装备双手武器（{weapon.ItemId:D8}）后，盾牌（{shield.ItemId:D8}）被卸下。",
                    weapon.ItemId));
            }
        }

        // 3) 基础外观（身体/头部/头发/脸）与戒指先放行，不参与可视槽覆盖。
        List<ResolvedItem> accepted = new();
        List<ResolvedItem> equipment = new();
        for (int index = 0; index < active.Count; index++)
        {
            ResolvedItem item = active[index];
            if (IsBaseAppearance(item.Slot))
            {
                accepted.Add(item);
            }
            else
            {
                equipment.Add(item);
            }
        }

        // 4) 装备按优先级排序：现金 → 可视槽数量多 → 槽位渲染顺序 → 原始顺序。
        //    使用稳定的 OrderBy 链，保证“优先级完全相同时保留调用方给出的先后顺序”。
        equipment = equipment
            .OrderByDescending(static item => item.Descriptor.IsCash)
            .ThenByDescending(static item => item.Descriptor.VSlot.Count)
            .ThenBy(static item => EquipSlotInfo.RenderOrderOf(item.Slot))
            .ToList();

        // 5) 贪心接受：后处理的物品若被已接受物品覆盖，则记录诊断并跳过。
        List<ResolvedItem> acceptedEquipment = new();
        foreach (ResolvedItem candidate in equipment)
        {
            ResolvedItem? covering = FindCovering(acceptedEquipment, candidate);
            if (covering == null)
            {
                acceptedEquipment.Add(candidate);
                continue;
            }

            conflicts.Add(CreateCoveredConflict(covering, candidate));
        }

        accepted.AddRange(acceptedEquipment);

        // 6) 输出按槽位渲染顺序排序（同一槽位保持原始先后，便于戒指多枚）。
        IReadOnlyList<ResolvedItem> ordered = accepted
            .OrderBy(static item => EquipSlotInfo.RenderOrderOf(item.Slot))
            .ToArray();

        return new ConflictResolution(new AvatarResolvedItems(ordered), conflicts);
    }

    /// <summary>基础外观：永远保留。</summary>
    private static bool IsBaseAppearance(EquipSlot slot) =>
        slot is EquipSlot.Body or EquipSlot.Head or EquipSlot.Hair or EquipSlot.Face or EquipSlot.Ring;

    /// <summary>
    /// 判断已接受的某件装备是否覆盖候选。
    /// </summary>
    private static bool IsTwoHandedWeapon(ItemDescriptor descriptor)
    {
        // WpSi is authoritative when present. Some spear/polearm entries use
        // plain Wp, so also trust the numeric weapon category.
        if (descriptor.VSlot.Contains("Si"))
        {
            return true;
        }

        return descriptor.WeaponCategory is
            WeaponCategory.TwoHandSword or
            WeaponCategory.TwoHandAxe or
            WeaponCategory.TwoHandBlunt or
            WeaponCategory.Spear or
            WeaponCategory.Polearm or
            WeaponCategory.Bow or
            WeaponCategory.Crossbow;
    }

    private static ResolvedItem? FindCovering(List<ResolvedItem> accepted, ResolvedItem candidate)
    {
        foreach (ResolvedItem current in accepted)
        {
            if (Covers(current, candidate))
            {
                return current;
            }
        }

        return null;
    }

    /// <summary>覆盖判定：同槽重复、可视槽包含、现金共享可视槽。</summary>
    private static bool Covers(ResolvedItem covering, ResolvedItem candidate)
    {
        VSlotSet coverSet = covering.Descriptor.VSlot;
        VSlotSet candidateSet = candidate.Descriptor.VSlot;

        // 同槽同可视槽：重复装备，保留先接受者。
        if (covering.Slot == candidate.Slot &&
            string.Equals(coverSet.Raw, candidateSet.Raw, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 可视槽包含：长袍 ⊇ 上衣/裤裙，双手武器 ⊇ 盾。
        if (coverSet.IsSupersetOf(candidateSet))
        {
            return true;
        }

        // 现金优先：只要与现金件共享任意可视槽，普通件即被覆盖。
        if (covering.Descriptor.IsCash &&
            !candidate.Descriptor.IsCash &&
            SharesToken(coverSet, candidateSet))
        {
            return true;
        }

        return false;
    }

    /// <summary>两个可视槽集合是否有交集。</summary>
    private static bool SharesToken(VSlotSet left, VSlotSet right)
    {
        if (left.Count == 0 || right.Count == 0)
        {
            return false;
        }

        foreach (string token in right.Tokens)
        {
            if (left.Contains(token))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>按覆盖原因生成对应诊断。</summary>
    private static AvatarConflict CreateCoveredConflict(ResolvedItem covering, ResolvedItem candidate)
    {
        string coveringName = $"{EquipSlotInfo.DisplayNameOf(covering.Slot)}({covering.ItemId:D8})";
        string candidateName = EquipSlotInfo.DisplayNameOf(candidate.Slot);

        if (covering.Slot == candidate.Slot &&
            string.Equals(covering.Descriptor.VSlot.Raw, candidate.Descriptor.VSlot.Raw, StringComparison.OrdinalIgnoreCase))
        {
            return new AvatarConflict(
                ConflictKind.DuplicateSlot,
                candidate.Slot,
                candidate.ItemId,
                $"同一槽位存在多件物品，保留「{coveringName}」，隐藏本件。",
                covering.ItemId);
        }

        if (covering.Descriptor.IsCash && !candidate.Descriptor.IsCash)
        {
            return new AvatarConflict(
                ConflictKind.CashOverrides,
                candidate.Slot,
                candidate.ItemId,
                $"现金装备「{coveringName}」优先显示，普通{candidateName}被隐藏。",
                covering.ItemId);
        }

        if (candidate.Slot == EquipSlot.Shield && covering.Slot == EquipSlot.Weapon)
        {
            return new AvatarConflict(
                ConflictKind.ShieldUnequipped,
                candidate.Slot,
                candidate.ItemId,
                $"武器「{coveringName}」占用副手槽，{candidateName}被隐藏。",
                covering.ItemId);
        }

        return new AvatarConflict(
            ConflictKind.VSlotCovered,
            candidate.Slot,
            candidate.ItemId,
            $"可视槽 [{candidate.Descriptor.VSlot.Raw}] 被「{coveringName}」的可视槽 [{covering.Descriptor.VSlot.Raw}] 覆盖。",
            covering.ItemId);
    }
}
