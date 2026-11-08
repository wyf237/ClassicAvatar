using Avatar.Core.Assets;
using Avatar.Core.Models;

namespace Avatar.Core.Rules;

/// <summary>
/// 武器档案工厂：把“槽位语义”和“动作语义”分开建模。
/// </summary>
/// <remarks>
/// 关键点（对应需求第 3 点）：
/// <list type="bullet">
/// <item><b>槽位</b>决定是否卸盾：<c>vslot</c> 含 <c>Si</c> 即占用盾槽（双手武器、弓、弩、拳套）；</item>
/// <item><b>类别</b>决定动作模板：单手武器用 <c>swingO</c>/<c>stand1</c>，双手用 <c>swingT</c>/<c>stand2</c>，
///       长枪/长矛用 <c>swingP</c>，<b>拳套（claw）虽然卸盾，但动作仍是单手 <c>swingO</c></b>；</item>
/// <item><c>info/stand</c>、<c>info/walk</c> 若存在则优先于模板（数据可能因版本不同而调整）。</item>
/// </list>
/// </remarks>
public static class WeaponProfiles
{
    /// <summary>
    /// 根据武器物品描述构造档案；武器为空时返回 <see cref="WeaponProfile.Empty"/>。
    /// </summary>
    /// <param name="weapon">武器描述（可为 <c>null</c>）。</param>
    /// <returns>武器档案。</returns>
    public static WeaponProfile Create(ItemDescriptor? weapon)
    {
        if (weapon == null)
        {
            return WeaponProfile.Empty;
        }

        WeaponCategory category = weapon.WeaponCategory;
        return new WeaponProfile(
            category,
            weapon.ItemId,
            weapon.StandVariant ?? DefaultStandVariant(category),
            weapon.WalkVariant ?? DefaultWalkVariant(category),
            weapon.VSlot.Contains("Si"),
            weapon.IsCash,
            PreferredActionsOf(category));
    }

    /// <summary>类别的默认站姿变体（1=stand1，2=stand2）。</summary>
    /// <param name="category">类别。</param>
    /// <returns>变体号。</returns>
    public static int DefaultStandVariant(WeaponCategory category) => category switch
    {
        WeaponCategory.TwoHandSword or WeaponCategory.TwoHandAxe or WeaponCategory.TwoHandBlunt or
            WeaponCategory.Spear or WeaponCategory.Polearm or WeaponCategory.Crossbow => 2,
        _ => 1,
    };

    /// <summary>类别的默认行走变体（1=walk1，2=walk2）。</summary>
    /// <param name="category">类别。</param>
    /// <returns>变体号。</returns>
    public static int DefaultWalkVariant(WeaponCategory category) => category switch
    {
        WeaponCategory.TwoHandAxe or WeaponCategory.TwoHandBlunt or WeaponCategory.Spear or
            WeaponCategory.Polearm or WeaponCategory.Crossbow => 2,
        _ => 1,
    };

    /// <summary>
    /// 类别的首选动作序列（第一个为回退首选）。
    /// </summary>
    /// <remarks>
    /// 该序列只用于“请求的动作在身体/武器中不存在”时的回退目标，渲染仍以数据中真实存在的节点为准。
    /// </remarks>
    /// <param name="category">类别。</param>
    /// <returns>动作名数组（只读）。</returns>
    public static IReadOnlyList<string> PreferredActionsOf(WeaponCategory category) => category switch
    {
        WeaponCategory.TwoHandSword or WeaponCategory.TwoHandAxe or WeaponCategory.TwoHandBlunt =>
            new[] { "swingT1", "swingT2", "swingT3", "swingTF" },

        WeaponCategory.Spear or WeaponCategory.Polearm =>
            new[] { "swingP1", "swingP2", "swingPF", "swingT1" },

        WeaponCategory.Bow =>
            new[] { "shoot1", "shootF", "swingT1" },

        WeaponCategory.Crossbow =>
            new[] { "shoot2", "shootF", "swingT1" },

        WeaponCategory.Wand or WeaponCategory.Staff =>
            new[] { "swingO1", "swingO2", "swingO3", "shoot1" },

        WeaponCategory.Mixed =>
            new[] { "swingO1", "swingT1", "swingP1" },

        // 拳套：动作与单手剑一致（这是最容易写错的地方）。
        WeaponCategory.Claw =>
            new[] { "swingO1", "swingO2", "swingO3", "swingOF" },

        _ =>
            new[] { "swingO1", "swingO2", "swingO3", "swingOF" },
    };
}
