using Avatar.Core.Assets;
using Avatar.Core.Models;

namespace Avatar.Core.Rules;

/// <summary>
/// 动作解析结果。
/// </summary>
/// <param name="Requested">调用方请求的动作名。</param>
/// <param name="Action">实际渲染使用的动作名。</param>
/// <param name="IsFallback">是否发生回退。</param>
/// <param name="Reason">回退原因（无回退时为 <c>null</c>）。</param>
public sealed record ActionResolution(string Requested, string Action, bool IsFallback, string? Reason);

/// <summary>
/// 动作解析：处理“站/走别名”“武器动作模板回退”“部件动作回退”三层规则。
/// </summary>
/// <remarks>
/// <para>
/// 第一层（本类）：请求动作 → 身体真实存在的动作。例如请求 <c>stand</c> 时按武器变体展开为 <c>stand1</c>/<c>stand2</c>；
/// 单手武器请求 <c>swingT1</c> 时回退到 <c>swingO1</c>。
/// </para>
/// <para>
/// 第二层（<see cref="SelectItemAction"/>）：每件装备在自己的 img 里选动作节点。装备通常只包含部分动作；
/// 脸饰/眼饰/耳饰只有 <c>default</c>，背向动作（爬梯/绳索）用 <c>backDefault</c>。
/// </para>
/// <para>
/// 第三层（渲染期）：部件帧数少于身体帧数时按 <see cref="FrameFallbackMode"/> 取帧。
/// </para>
/// </remarks>
public static class ActionResolver
{
    /// <summary>是否为攻击类动作（用于模板回退判断）。</summary>
    /// <param name="action">动作名。</param>
    /// <returns>是攻击动作返回 <c>true</c>。</returns>
    public static bool IsAttackAction(string action) =>
        action.StartsWith("swing", StringComparison.OrdinalIgnoreCase) ||
        action.StartsWith("stab", StringComparison.OrdinalIgnoreCase) ||
        action.StartsWith("shoot", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 解析请求动作，返回身体真实可渲染的动作名。
    /// </summary>
    /// <param name="build">组装结果（提供身体动作集合与武器档案）。</param>
    /// <param name="requested">请求动作；为 <c>null</c>/空时使用档案默认动作。</param>
    /// <returns>解析结果。</returns>
    public static ActionResolution Resolve(AvatarBuild build, string? requested)
    {
        ArgumentNullException.ThrowIfNull(build);

        string original = string.IsNullOrWhiteSpace(requested)
            ? (string.IsNullOrWhiteSpace(build.Profile.DefaultAction) ? "stand1" : build.Profile.DefaultAction.Trim())
            : requested.Trim();

        string expanded = ExpandAlias(build, original);
        string? canonical = FindAction(build, expanded);
        if (canonical != null)
        {
            return new ActionResolution(original, canonical, expanded != original, null);
        }

        // 攻击动作与武器模板不匹配：优先使用模板首选动作（数据版本差异时也能给出合理动作）。
        if (IsAttackAction(expanded))
        {
            foreach (string preferred in build.Weapon.PreferredActions)
            {
                string? action = FindAction(build, preferred);
                if (action != null)
                {
                    return new ActionResolution(
                        original,
                        action,
                        true,
                        $"动作「{expanded}」不存在，已回退到武器首选动作「{action}」。");
                }
            }
        }

        string? stand = FindAction(build, "stand1");
        if (stand != null)
        {
            return new ActionResolution(
                original,
                stand,
                true,
                $"动作「{expanded}」不存在，已回退到「{stand}」。");
        }

        string? any = build.AvailableActions.FirstOrDefault();
        if (any != null)
        {
            return new ActionResolution(
                original,
                any,
                true,
                $"动作「{expanded}」不存在且缺少 stand1，已回退到「{any}」。");
        }

        return new ActionResolution(original, expanded, true, "角色没有任何可渲染动作。");
    }

    /// <summary>
    /// 为某件装备选择动作节点名。
    /// </summary>
    /// <param name="descriptor">物品描述。</param>
    /// <param name="bodyAction">身体实际使用的动作名。</param>
    /// <param name="backFacing">当前帧是否为背向（身体帧出现 <c>back*</c> 图层）。</param>
    /// <returns>动作节点名；该物品完全不参与此动作时返回 <c>null</c>。</returns>
    /// <remarks>
    /// 选择顺序：同名动作 → 背向 <c>backDefault</c> → <c>default</c> → 背向 <c>back</c> → <c>front</c> → <c>back</c>。
    /// 该顺序同时覆盖了头部（只有 <c>front</c>/<c>back</c>）、头发/帽子（<c>default</c>/<c>backDefault</c>）、
    /// 脸饰（只有 <c>default</c>）与普通装备（按动作同名）。
    /// </remarks>
    public static string? SelectItemAction(ItemDescriptor descriptor, string bodyAction, bool backFacing)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        if (descriptor.HasAction(bodyAction))
        {
            return bodyAction;
        }

        if (backFacing && descriptor.HasAction("backDefault"))
        {
            return "backDefault";
        }

        if (descriptor.HasAction("default"))
        {
            return "default";
        }

        if (backFacing && descriptor.HasAction("back"))
        {
            return "back";
        }

        if (descriptor.HasAction("front"))
        {
            return "front";
        }

        if (descriptor.HasAction("back"))
        {
            return "back";
        }

        return null;
    }

    /// <summary>
    /// 展开简短别名：<c>stand</c>→<c>stand{n}</c>、<c>walk</c>→<c>walk{n}</c>、<c>attack</c>/<c>swing</c>→武器首选攻击动作。
    /// </summary>
    /// <param name="build">组装结果。</param>
    /// <param name="action">原始动作名。</param>
    /// <returns>展开后的动作名。</returns>
    public static string ExpandAlias(AvatarBuild build, string action)
    {
        if (string.Equals(action, "stand", StringComparison.OrdinalIgnoreCase))
        {
            return $"stand{build.Weapon.StandVariant}";
        }

        if (string.Equals(action, "walk", StringComparison.OrdinalIgnoreCase))
        {
            return $"walk{build.Weapon.WalkVariant}";
        }

        if (string.Equals(action, "attack", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(action, "swing", StringComparison.OrdinalIgnoreCase))
        {
            return build.Weapon.PrimaryAction;
        }

        return action;
    }

    /// <summary>在身体动作集合中忽略大小写查找，返回数据中的真实写法。</summary>
    private static string? FindAction(AvatarBuild build, string action) =>
        build.AvailableActions.FirstOrDefault(name => string.Equals(name, action, StringComparison.OrdinalIgnoreCase));
}
