using Avatar.Core.Assets;
using Avatar.Core.Models;

namespace Avatar.Core.Layout;

/// <summary>
/// 帽子对头发的遮盖类型（对应参考实现的 CapType 0–3）。
/// </summary>
internal enum CapHairCover
{
    /// <summary>不遮盖头发（<c>Cp</c>/<c>CpH5</c>、无可见外观的透明帽、未戴帽）：头发按原图层绘制。</summary>
    None = 0,

    /// <summary>部分遮盖（<c>CpH1H5</c>）：保留帽下 <c>hair</c>，隐藏帽上头发。</summary>
    Partial = 1,

    /// <summary>遮盖大部分（<c>CpH1H3…</c>）：隐藏“帽上”头发，仅保留帽下头发。</summary>
    Most = 2,

    /// <summary>完全遮盖（<c>Hx</c> 头套 / 大帽）：隐藏全部头发，呈光头造型。</summary>
    All = 3,
}

/// <summary>
/// 按帽子 <c>info/vslot</c> 判定其头发遮盖类型，并给出头发图层取舍规则。
/// </summary>
/// <remarks>
/// <para>
/// 判定依据是帽子 <c>info/vslot</c> 中“占据了几个发型槽位”，而不是整串字符串是否等于某个固定值。
/// vslot 由两位一组的槽位码拼接而成（<c>Cp</c> = 帽子，<c>H1…H6</c>/<c>Hf</c>/<c>Hs</c>/<c>Hb</c> = 发型槽位，
/// <c>Ay</c>/<c>As</c>/<c>Ae</c> 等 = 饰品槽位）。因此同一个遮盖语义会有多种写法：
/// </para>
/// <list type="bullet">
/// <item><c>CpH1H5</c>、<c>CpH1H5AyAs</c>、<c>CpH1H5AfAyAsAe</c> 都是“遮住 H1+H5 两处发型”；</item>
/// <item><c>CpH1H4</c>、<c>CpH2H5</c>、<c>CpH1H5Hb</c> 同样是“遮住少量发型槽位”。</item>
/// </list>
/// <para>
/// 参考实现（MapleNecrocer 的 <c>CapType</c>）用 <c>VSlot == "CpH1H5"</c> 精确匹配 + <c>Length &gt; 12</c> 分档，
/// 一旦帽子带上饰品槽位（如 <c>CpH1H5AyAs</c>）就会漏判成“不遮盖”，
/// 导致 <c>hairOverHead</c>(z=89) 被画到 <c>cap</c>(z=86) 之上、头发压住帽子。
/// 这里改为按槽位集合计数，规则如下：
/// </para>
/// <list type="number">
/// <item>帽子没有任何可渲染动作帧（只有 <c>info/icon</c>）→ <see cref="CapHairCover.None"/>（透明帽，如 1007097/1007610/1007612）；</item>
/// <item>vslot 含 <c>Hx</c> → <see cref="CapHairCover.All"/>（头套/光头，如 1007340）；</item>
/// <item>不含任何发型槽位（<c>Cp</c>）→ <see cref="CapHairCover.None"/>；</item>
/// <item>只含 <c>H5</c>（<c>CpH5</c>/<c>CpH5Ae</c>）→ <see cref="CapHairCover.None"/>（H5 是“帽上发”槽，不构成遮盖）；</item>
/// <item>发型槽位 ≤ 4 个 → <see cref="CapHairCover.Partial"/>（<c>CpH1H5</c>、<c>CpH1H5AyAs</c>、<c>CpH1H4</c> 等）；</item>
/// <item>长串（&gt; 12 字符）以 <c>CpH1H3</c> 开头 → <see cref="CapHairCover.Most"/>，否则 → <see cref="CapHairCover.All"/>
/// （如 1007078/1007284/1007285 的 <c>CpH1H2H3…</c>）；</item>
/// <item>其余 → <see cref="CapHairCover.None"/>。</item>
/// </list>
/// <para>
/// 本数据集 <c>Base/zmap.img</c> 层序：<c>hairShade(74) &lt; face(77) &lt; capBelowAccessory(79)
/// &lt; hair(85) &lt; cap(86) &lt; hairOverHead(89)</c>。
/// 因此“帽下头发”= <c>hair</c>，“帽上头发”= <c>hairOverHead</c>。
/// </para>
/// </remarks>
internal static class CapHairRule
{
    /// <summary>“帽上”头发图层（会被遮盖帽隐藏，或在部分遮盖时启用）。</summary>
    private static readonly HashSet<string> OverHairLayers = new(StringComparer.OrdinalIgnoreCase)
    {
        "hairOverHead",
        "backHair",
        "backHairOverCape",
    };

    /// <summary>“帽下/身后”头发图层（遮盖大部分时一并隐藏）。</summary>
    private static readonly HashSet<string> BelowHairLayers = new(StringComparer.OrdinalIgnoreCase)
    {
        "hairBelowBody",
        "backHairBelowCap",
    };

    /// <summary>头套/光头标记。</summary>
    private const string HideHairToken = "Hx";

    /// <summary>“帽上发”槽位：单独出现时不构成对头发的遮盖（<c>CpH5</c>）。</summary>
    private const string OverHairSlot = "H5";

    /// <summary>短发槽位数量的判定上限（含饰品码的短 vslot 走“部分遮盖”）。</summary>
    private const int PartialHairSlotLimit = 4;

    /// <summary>判定当前装配结果的帽子遮盖类型。</summary>
    /// <param name="build">组装结果。</param>
    /// <returns>遮挡类型。</returns>
    public static CapHairCover Resolve(AvatarBuild build)
    {
        ArgumentNullException.ThrowIfNull(build);

        ResolvedItem? cap = build.Items.All.FirstOrDefault(static item => item.Slot == EquipSlot.Cap);
        if (cap == null)
        {
            return CapHairCover.None;
        }

        ItemDescriptor descriptor = cap.Descriptor;

        // 只有 info/icon、没有任何可渲染动作帧的帽子不产生像素，等同不戴帽。
        if (!HasVisibleVisual(descriptor))
        {
            return CapHairCover.None;
        }

        VSlotSet vslot = descriptor.VSlot;
        if (vslot.Contains(HideHairToken))
        {
            return CapHairCover.All;
        }

        // 按“占据了几个发型槽位”计数，而不是整串精确匹配：
        // 帽子的 vslot 会附带饰品槽位（Ay/As/Ae/Fc…），精确匹配会把这些帽子误判成“不遮盖”。
        int hairSlots = vslot.Tokens.Count(IsHairSlot);

        if (hairSlots == 0)
        {
            return CapHairCover.None;
        }

        // CpH5：只占据“帽上发”槽位，帽子里并没有覆盖头发，等同不遮盖。
        if (hairSlots == 1 && vslot.Contains(OverHairSlot))
        {
            return CapHairCover.None;
        }

        if (hairSlots <= PartialHairSlotLimit)
        {
            return CapHairCover.Partial;
        }

        // 长串（占据了大部分发型槽位）沿用参考实现的分档。
        if (vslot.Raw.Length <= 12)
        {
            return CapHairCover.None;
        }

        return vslot.Raw.StartsWith("CpH1H3", StringComparison.OrdinalIgnoreCase)
            ? CapHairCover.Most
            : CapHairCover.All;
    }

    /// <summary>是否为发型槽位码（<c>H1</c>–<c>H6</c>、<c>Hf</c>、<c>Hs</c>、<c>Hb</c> 等，排除 <c>Hx</c>）。</summary>
    private static bool IsHairSlot(string token) =>
        token.Length == 2
        && (token[0] == 'H' || token[0] == 'h')
        && !token.Equals(HideHairToken, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 头发部件是否应参与本帧渲染。
    /// </summary>
    /// <param name="part">头发部件。</param>
    /// <param name="cover">帽子遮盖类型。</param>
    /// <returns>应绘制返回 <c>true</c>。</returns>
    /// <remarks>
    /// 与参考实现一致：只决定“是否绘制已有图层”，不改变头发图层的 z。
    /// <list type="bullet">
    /// <item><see cref="CapHairCover.Partial"/>：隐藏帽上头发（<c>hairOverHead</c>/<c>backHair</c>），保留帽下 <c>hair</c>；</item>
    /// <item><see cref="CapHairCover.Most"/>：在此基础上再隐藏身体后方头发（<c>hairBelowBody</c>/<c>backHairBelowCap</c>），仅保留 <c>hair</c>；</item>
    /// <item><see cref="CapHairCover.All"/>：全部隐藏（光头造型）。</item>
    /// </list>
    /// </remarks>
    public static bool IsVisible(PartDefinition part, CapHairCover cover) => cover switch
    {
        CapHairCover.All => false,
        CapHairCover.Most => !IsOverLayer(part.Layer) && !IsBelowHairLayer(part.Layer),
        CapHairCover.Partial => !IsOverLayer(part.Layer),
        _ => true,
    };

    /// <summary>是否为“帽上”头发图层。</summary>
    public static bool IsOverLayer(string? layer) =>
        layer != null && OverHairLayers.Contains(layer);

    /// <summary>是否为“帽下/身后”头发图层。</summary>
    public static bool IsBelowHairLayer(string? layer) =>
        layer != null && BelowHairLayers.Contains(layer);

    /// <summary>帽子是否存在可渲染的动作帧（排除仅 icon 的透明道具）。</summary>
    private static bool HasVisibleVisual(ItemDescriptor descriptor)
    {
        foreach (string action in descriptor.Actions)
        {
            if (descriptor.GetFrameCount(action) > 0)
            {
                return true;
            }
        }

        return false;
    }
}
