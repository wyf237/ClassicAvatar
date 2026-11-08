using Avatar.Core.Assets;
using Avatar.Core.Models;

namespace Avatar.Core.Rules;

/// <summary>
/// 需求校验结果：诊断列表 + 被剔除的物品集合。
/// </summary>
public sealed class RequirementReport
{
    private readonly HashSet<(EquipSlot Slot, int ItemId)> _rejected;

    /// <summary>创建结果。</summary>
    /// <param name="conflicts">诊断列表。</param>
    /// <param name="rejected">被剔除的物品。</param>
    internal RequirementReport(IReadOnlyList<AvatarConflict> conflicts, HashSet<(EquipSlot, int)> rejected)
    {
        this.Conflicts = conflicts;
        this._rejected = rejected;
    }

    /// <summary>诊断列表（性别/职业/等级/属性）。</summary>
    public IReadOnlyList<AvatarConflict> Conflicts { get; }

    /// <summary>判断某件物品是否因不满足需求而被剔除。</summary>
    /// <param name="slot">槽位。</param>
    /// <param name="itemId">物品 ID。</param>
    /// <returns>被剔除返回 <c>true</c>。</returns>
    public bool IsRejected(EquipSlot slot, int itemId) => this._rejected.Contains((slot, itemId));
}

/// <summary>
/// 装备需求校验：性别、职业、等级、四维属性。
/// </summary>
/// <remarks>
/// 与客户端一致的三档策略：
/// <list type="bullet">
/// <item><see cref="ValidationMode.Warn"/>（默认）：只产生诊断，不阻止渲染，适合试衣间/资料站；</item>
/// <item><see cref="ValidationMode.Error"/>：不满足需求的物品直接剔除；</item>
/// <item><see cref="GenderPolicy"/> 单独控制性别严格程度，便于“只看外观”场景放行。</item>
/// </list>
/// 基础外观（身体/头部）与未声明需求的物品永远不会被剔除。
/// </remarks>
public static class RequirementValidator
{
    /// <summary>
    /// 校验全部候选物品。
    /// </summary>
    /// <param name="profile">角色档案。</param>
    /// <param name="options">选项（校验模式与性别策略）。</param>
    /// <param name="candidates">候选物品（已解析出描述）。</param>
    /// <returns>校验结果。</returns>
    public static RequirementReport Validate(
        CharacterProfile profile,
        AvatarOptions options,
        IReadOnlyList<ResolvedItem> candidates)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(candidates);

        List<AvatarConflict> conflicts = new();
        HashSet<(EquipSlot, int)> rejected = new();

        bool removeInvalid = options.Validation == ValidationMode.Error;

        foreach (ResolvedItem item in candidates)
        {
            // 身体/头部是渲染基底，任何需求校验都不应剔除它们。
            if (item.Slot is EquipSlot.Body or EquipSlot.Head)
            {
                continue;
            }

            ItemDescriptor descriptor = item.Descriptor;

            CheckGender(profile, options, item, conflicts, removeInvalid, rejected);
            CheckJob(profile, item, conflicts, removeInvalid, rejected);
            CheckLevel(profile, item, conflicts, removeInvalid, rejected);
            CheckStats(profile, item, conflicts, removeInvalid, rejected);
        }

        return new RequirementReport(conflicts, rejected);
    }

    /// <summary>检查性别限制。</summary>
    private static void CheckGender(
        CharacterProfile profile,
        AvatarOptions options,
        ResolvedItem item,
        List<AvatarConflict> conflicts,
        bool removeInvalid,
        HashSet<(EquipSlot, int)> rejected)
    {
        if (options.GenderPolicy == GenderPolicy.Ignore)
        {
            return;
        }

        // 档案性别未知时不作判断；物品性别未知（Unisex）时放行。
        if (profile.Gender == Gender.Unisex || item.Descriptor.EffectiveGender == Gender.Unisex)
        {
            return;
        }

        if (item.Descriptor.EffectiveGender == profile.Gender)
        {
            return;
        }

        conflicts.Add(new AvatarConflict(
            ConflictKind.GenderMismatch,
            item.Slot,
            item.ItemId,
            $"该物品限定 {GenderName(item.Descriptor.EffectiveGender)}，当前角色为 {GenderName(profile.Gender)}。"));

        if (removeInvalid && options.GenderPolicy == GenderPolicy.Strict)
        {
            rejected.Add((item.Slot, item.ItemId));
        }
    }

    /// <summary>检查职业限制（位掩码）。</summary>
    private static void CheckJob(
        CharacterProfile profile,
        ResolvedItem item,
        List<AvatarConflict> conflicts,
        bool removeInvalid,
        HashSet<(EquipSlot, int)> rejected)
    {
        int reqJob = item.Descriptor.ReqJob;
        if (reqJob <= 0)
        {
            // 0=不限；负数在部分版本表示“全职业通用/特殊规则”，一律放行。
            return;
        }

        if ((profile.Job & reqJob) != 0)
        {
            return;
        }

        conflicts.Add(new AvatarConflict(
            ConflictKind.JobMismatch,
            item.Slot,
            item.ItemId,
            $"职业不符：需要职业掩码 {reqJob}，当前为 {profile.Job}。"));

        if (removeInvalid)
        {
            rejected.Add((item.Slot, item.ItemId));
        }
    }

    /// <summary>检查等级限制。</summary>
    private static void CheckLevel(
        CharacterProfile profile,
        ResolvedItem item,
        List<AvatarConflict> conflicts,
        bool removeInvalid,
        HashSet<(EquipSlot, int)> rejected)
    {
        int reqLevel = item.Descriptor.ReqLevel;
        if (reqLevel <= 0 || profile.Level >= reqLevel)
        {
            return;
        }

        conflicts.Add(new AvatarConflict(
            ConflictKind.LevelTooLow,
            item.Slot,
            item.ItemId,
            $"等级不足：需要 {reqLevel} 级，当前 {profile.Level} 级。"));

        if (removeInvalid)
        {
            rejected.Add((item.Slot, item.ItemId));
        }
    }

    /// <summary>检查四维属性限制；档案未提供属性时跳过。</summary>
    private static void CheckStats(
        CharacterProfile profile,
        ResolvedItem item,
        List<AvatarConflict> conflicts,
        bool removeInvalid,
        HashSet<(EquipSlot, int)> rejected)
    {
        Stats? stats = profile.Stats;
        if (stats == null)
        {
            return;
        }

        ItemDescriptor descriptor = item.Descriptor;
        List<string> failures = new();

        if (descriptor.ReqSTR > stats.STR)
        {
            failures.Add($"STR {descriptor.ReqSTR}>{stats.STR}");
        }

        if (descriptor.ReqDEX > stats.DEX)
        {
            failures.Add($"DEX {descriptor.ReqDEX}>{stats.DEX}");
        }

        if (descriptor.ReqINT > stats.INT)
        {
            failures.Add($"INT {descriptor.ReqINT}>{stats.INT}");
        }

        if (descriptor.ReqLUK > stats.LUK)
        {
            failures.Add($"LUK {descriptor.ReqLUK}>{stats.LUK}");
        }

        if (failures.Count == 0)
        {
            return;
        }

        conflicts.Add(new AvatarConflict(
            ConflictKind.StatTooLow,
            item.Slot,
            item.ItemId,
            $"属性不足：{string.Join("，", failures)}。"));

        if (removeInvalid)
        {
            rejected.Add((item.Slot, item.ItemId));
        }
    }

    /// <summary>性别中文名。</summary>
    private static string GenderName(Gender gender) => gender switch
    {
        Gender.Male => "男",
        Gender.Female => "女",
        _ => "不限",
    };
}
