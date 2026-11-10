using Avatar.Core.Models;
using WzReader.Core.Dom;
using WzReader.Core.Diagnostics;

namespace Avatar.Core.Assets;

/// <summary>
/// 一件物品的元数据（缓存对象）：解析 <c>info</c> 字段、可视槽、动作表、性别与武器类别。
/// </summary>
/// <remarks>
/// 该类只做“数据读取与整理”，不做任何规则判定；规则（覆盖、性别、职业、动作回退）在 Rules 层。
/// 实例创建后不可变，可安全并行复用。
/// </remarks>
public sealed class ItemDescriptor
{
    private readonly HashSet<string> _actions;

    private ItemDescriptor(
        EquipSlot slot,
        int itemId,
        string imagePath,
        WzNode imageNode,
        string islot,
        VSlotSet vslot,
        bool isCash,
        int reqJob,
        int reqLevel,
        int reqStr,
        int reqDex,
        int reqInt,
        int reqLuk,
        int? stand,
        int? walk,
        string? afterImage,
        Gender declaredGender,
        Gender inferredGender,
        WeaponCategory weaponCategory,
        IReadOnlyList<string> actions)
    {
        this.Slot = slot;
        this.ItemId = itemId;
        this.ImagePath = imagePath;
        this.ImageNode = imageNode;
        this.ISlot = islot;
        this.VSlot = vslot;
        this.IsCash = isCash;
        this.ReqJob = reqJob;
        this.ReqLevel = reqLevel;
        this.ReqSTR = reqStr;
        this.ReqDEX = reqDex;
        this.ReqINT = reqInt;
        this.ReqLUK = reqLuk;
        this.StandVariant = stand;
        this.WalkVariant = walk;
        this.AfterImage = afterImage;
        this.DeclaredGender = declaredGender;
        this.InferredGender = inferredGender;
        this.WeaponCategory = weaponCategory;
        this.Actions = actions;
        this._actions = new HashSet<string>(actions, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>槽位。</summary>
    public EquipSlot Slot { get; }

    /// <summary>物品 ID。</summary>
    public int ItemId { get; }

    /// <summary>img 绝对路径。</summary>
    public string ImagePath { get; }

    /// <summary>img 根节点。</summary>
    public WzNode ImageNode { get; }

    /// <summary>原始 <c>islot</c>（如 <c>MaPn</c>）。</summary>
    public string ISlot { get; }

    /// <summary>可视槽集合（如 <c>MaPn</c> → {Ma, Pn}）。</summary>
    public VSlotSet VSlot { get; }

    /// <summary>是否现金装备（<c>info/cash</c>）。</summary>
    public bool IsCash { get; }

    /// <summary>职业位掩码需求（0=不限）。</summary>
    public int ReqJob { get; }

    /// <summary>等级需求。</summary>
    public int ReqLevel { get; }

    /// <summary>力量需求。</summary>
    public int ReqSTR { get; }

    /// <summary>敏捷需求。</summary>
    public int ReqDEX { get; }

    /// <summary>智力需求。</summary>
    public int ReqINT { get; }

    /// <summary>运气需求。</summary>
    public int ReqLUK { get; }

    /// <summary><c>info/stand</c>（站姿变体）。</summary>
    public int? StandVariant { get; }

    /// <summary><c>info/walk</c>（行走变体）。</summary>
    public int? WalkVariant { get; }

    /// <summary><c>info/afterImage</c>（残影键，仅记录）。</summary>
    public string? AfterImage { get; }

    /// <summary><c>info/gender</c> 声明值；本数据集通常缺失。</summary>
    public Gender DeclaredGender { get; }

    /// <summary>按 ID 区间推断的性别（发型、脸、上装、长袍、裤裙有效）。</summary>
    public Gender InferredGender { get; }

    /// <summary>最终性别：优先声明值，否则推断值。</summary>
    public Gender EffectiveGender => this.DeclaredGender != Gender.Unisex ? this.DeclaredGender : this.InferredGender;

    /// <summary>武器动作类别（非武器槽为 <see cref="WeaponCategory.None"/>）。</summary>
    public WeaponCategory WeaponCategory { get; }

    /// <summary>顶层动作名列表（已剥离现金武器的数字包裹层）。</summary>
    public IReadOnlyList<string> Actions { get; }

    /// <summary>是否拥有指定动作（忽略大小写）。</summary>
    /// <param name="action">动作名。</param>
    /// <returns>存在返回 <c>true</c>。</returns>
    public bool HasAction(string action) => this._actions.Contains(action);

    /// <summary>
    /// 取动作节点。现金武器顶层是数字包裹层（本数据集为 <c>33</c>），会自动下钻。
    /// </summary>
    /// <param name="action">动作名。</param>
    /// <returns>动作节点；不存在返回 <c>null</c>。</returns>
    public WzNode? GetActionNode(string action)
    {
        WzNode? direct = this.ImageNode.FindChild(action, ignoreCase: true);
        if (direct != null && IsRenderableNode(direct))
        {
            return direct;
        }

        foreach (WzNode child in this.ImageNode.Children)
        {
            if (!IsNumericWrapper(child))
            {
                continue;
            }

            WzNode? nested = child.FindChild(action, ignoreCase: true);
            if (nested != null && IsRenderableNode(nested))
            {
                return nested;
            }
        }

        return null;
    }

    /// <summary>
    /// 取动作的帧数。单帧动作（节点本身是部件）返回 1；数字帧容器返回最大帧号 + 1。
    /// </summary>
    /// <param name="action">动作名。</param>
    /// <returns>帧数；动作不存在返回 0。</returns>
    public int GetFrameCount(string action)
    {
        WzNode? node = this.GetActionNode(action);
        if (node == null)
        {
            return 0;
        }

        if (IsPartLike(node))
        {
            return 1;
        }

        List<(int Index, WzNode Node)> frames = GetNumberedChildren(node);
        return frames.Count == 0 ? 1 : frames[^1].Index + 1;
    }

    /// <summary>
    /// 取指定动作的帧节点，并按 <paramref name="fallback"/> 处理越界。
    /// </summary>
    /// <param name="action">动作名。</param>
    /// <param name="frameIndex">帧下标。</param>
    /// <param name="fallback">越界策略。</param>
    /// <param name="frameNode">帧节点。</param>
    /// <param name="usedIndex">实际使用的帧下标。</param>
    /// <returns>成功返回 <c>true</c>；动作不存在或按 <see cref="FrameFallbackMode.Skip"/> 跳过时返回 <c>false</c>。</returns>
    public bool TryGetFrameNode(
        string action,
        int frameIndex,
        FrameFallbackMode fallback,
        out WzNode? frameNode,
        out int usedIndex)
    {
        frameNode = null;
        usedIndex = frameIndex;

        WzNode? node = this.GetActionNode(action);
        if (node == null)
        {
            return false;
        }

        if (IsPartLike(node))
        {
            frameNode = node;
            usedIndex = 0;
            return true;
        }

        List<(int Index, WzNode Node)> frames = GetNumberedChildren(node);
        if (frames.Count == 0)
        {
            frameNode = node;
            usedIndex = 0;
            return true;
        }

        int position;
        switch (fallback)
        {
            case FrameFallbackMode.Loop:
                position = ((frameIndex % frames.Count) + frames.Count) % frames.Count;
                break;

            case FrameFallbackMode.Skip:
                if (frameIndex < 0 || frameIndex >= frames.Count)
                {
                    return false;
                }

                position = frameIndex;
                break;

            default:
                position = Math.Clamp(frameIndex, 0, frames.Count - 1);
                break;
        }

        frameNode = frames[position].Node;
        usedIndex = frames[position].Index;
        return true;
    }

    /// <summary>
    /// 从 img 根节点创建描述对象。
    /// </summary>
    /// <param name="imageNode">img 根节点。</param>
    /// <param name="slot">槽位。</param>
    /// <param name="itemId">物品 ID。</param>
    /// <param name="diagnostics">诊断收集器（可为空）。</param>
    /// <returns>描述对象。</returns>
    public static ItemDescriptor Create(
        WzNode imageNode,
        EquipSlot slot,
        int itemId,
        WzDiagnostics? diagnostics = null)
    {
        WzNode? info = imageNode.FindChild("info", ignoreCase: true);
        if (info == null)
        {
            diagnostics?.Warn("物品缺少 info 节点，按默认值处理。", imageNode.Path);
        }

        string islot = info?.GetString("islot") ?? string.Empty;
        VSlotSet vslot = VSlotSet.Parse(info?.GetString("vslot"));
        bool isCash = (info?.GetInt("cash") ?? 0) != 0;
        int reqJob = info?.GetInt("reqJob") ?? 0;
        int reqLevel = info?.GetInt("reqLevel") ?? 0;
        int reqStr = info?.GetInt("reqSTR") ?? 0;
        int reqDex = info?.GetInt("reqDEX") ?? 0;
        int reqInt = info?.GetInt("reqINT") ?? 0;
        int reqLuk = info?.GetInt("reqLUK") ?? 0;
        int? stand = info?.GetInt("stand");
        int? walk = info?.GetInt("walk");
        string? afterImage = info?.GetString("afterImage");

        Gender declared = (info?.GetInt("gender") ?? 0) switch
        {
            1 => Gender.Male,
            2 => Gender.Female,
            _ => Gender.Unisex,
        };

        Gender inferred = InferGender(slot, itemId);
        WeaponCategory weapon = slot == EquipSlot.Weapon ? WeaponCategoryOf(itemId) : WeaponCategory.None;

        List<string> actions = CollectActionNames(imageNode);

        return new ItemDescriptor(
            slot,
            itemId,
            ItemPathResolver.GetImagePath(slot, itemId),
            imageNode,
            islot,
            vslot,
            isCash,
            reqJob,
            reqLevel,
            reqStr,
            reqDex,
            reqInt,
            reqLuk,
            stand,
            walk,
            afterImage,
            declared,
            inferred,
            weapon,
            actions);
    }

    /// <summary>
    /// 按 ID 区间推断性别（本数据集没有 <c>info/gender</c>）。
    /// </summary>
    /// <remarks>
    /// 经典数据使用 ID 中的千位区分性别：上衣 1040/1045 男性、1041/1046 女性；
    /// 长袍 1050/1055 男性、1051/1056 女性；裤裙 1060 男性、1061 女性；
    /// 发型 00030xxx 男性、00031xxx 女性；脸 00020xxx 男性、00021xxx 女性。
    /// 其余部位（以及现金/特殊区间）返回 <see cref="Gender.Unisex"/>（放行）。
    /// </remarks>
    /// <param name="slot">槽位。</param>
    /// <param name="itemId">物品 ID。</param>
    /// <returns>推断性别。</returns>
    public static Gender InferGender(EquipSlot slot, int itemId) => slot switch
    {
        EquipSlot.Hair when itemId is >= 30000 and < 31000 => Gender.Male,
        EquipSlot.Hair when itemId is >= 31000 and < 32000 => Gender.Female,
        EquipSlot.Face when itemId is >= 20000 and < 21000 => Gender.Male,
        EquipSlot.Face when itemId is >= 21000 and < 22000 => Gender.Female,
        EquipSlot.Coat when itemId is >= 1040000 and < 1041000 => Gender.Male,
        EquipSlot.Coat when itemId is >= 1041000 and < 1042000 => Gender.Female,
        EquipSlot.Coat when itemId is >= 1045000 and < 1046000 => Gender.Male,
        EquipSlot.Coat when itemId is >= 1046000 and < 1047000 => Gender.Female,
        EquipSlot.Longcoat when itemId is >= 1050000 and < 1051000 => Gender.Male,
        EquipSlot.Longcoat when itemId is >= 1051000 and < 1052000 => Gender.Female,
        EquipSlot.Longcoat when itemId is >= 1055000 and < 1056000 => Gender.Male,
        EquipSlot.Longcoat when itemId is >= 1056000 and < 1057000 => Gender.Female,
        EquipSlot.Pants when itemId is >= 1060000 and < 1061000 => Gender.Male,
        EquipSlot.Pants when itemId is >= 1061000 and < 1062000 => Gender.Female,
        _ => Gender.Unisex,
    };

    /// <summary>
    /// 按四位前缀判定武器动作类别。
    /// </summary>
    /// <param name="itemId">武器 ID。</param>
    /// <returns>类别。</returns>
    public static WeaponCategory WeaponCategoryOf(int itemId) => (itemId / 10000) switch
    {
        130 => WeaponCategory.OneHandSword,
        131 => WeaponCategory.OneHandAxe,
        132 => WeaponCategory.OneHandBlunt,
        133 => WeaponCategory.Dagger,
        137 => WeaponCategory.Wand,
        138 => WeaponCategory.Staff,
        139 => WeaponCategory.Mixed,
        140 => WeaponCategory.TwoHandSword,
        141 => WeaponCategory.TwoHandAxe,
        142 => WeaponCategory.TwoHandBlunt,
        143 => WeaponCategory.Spear,
        144 => WeaponCategory.Polearm,
        145 => WeaponCategory.Bow,
        146 => WeaponCategory.Crossbow,
        147 => WeaponCategory.Claw,
        170 => WeaponCategory.Cash,
        _ => WeaponCategory.Cash,
    };

    /// <summary>节点是否为“部件形态”（自带 origin 或 z）。</summary>
    /// <param name="node">节点。</param>
    /// <returns>是部件返回 <c>true</c>。</returns>
    public static bool IsPartLike(WzNode node)
    {
        if (node.Value is WzReader.Core.Dom.WzUol)
        {
            return true;
        }

        return node.FindChild("origin", ignoreCase: true) != null ||
               node.FindChild("z", ignoreCase: true) != null;
    }

    /// <summary>节点是否可作为动作容器（属性容器、画布或链接）。</summary>
    /// <param name="node">节点。</param>
    /// <returns>可用返回 <c>true</c>。</returns>
    public static bool IsRenderableNode(WzNode node)
    {
        return node.Value is WzProperty or WzReader.Core.Codecs.WzCanvas or WzReader.Core.Dom.WzUol ||
               node.FindChild("_outlink", ignoreCase: true) != null ||
               node.FindChild("_inlink", ignoreCase: true) != null ||
               node.FindChild("source", ignoreCase: true) != null;
    }

    /// <summary>节点是否为纯数字命名的包裹层（现金武器的 <c>33</c>）。</summary>
    /// <param name="node">节点。</param>
    /// <returns>是包裹层返回 <c>true</c>。</returns>
    public static bool IsNumericWrapper(WzNode node)
    {
        if (!node.IsContainer || node.Name.Length == 0)
        {
            return false;
        }

        foreach (char ch in node.Name)
        {
            if (!char.IsAsciiDigit(ch))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>取出数字命名的子节点（帧），按帧号升序。</summary>
    /// <param name="node">动作节点。</param>
    /// <returns>(帧号, 节点) 列表。</returns>
    public static List<(int Index, WzNode Node)> GetNumberedChildren(WzNode node)
    {
        List<(int Index, WzNode Node)> result = new();
        foreach (WzNode child in node.Children)
        {
            if (int.TryParse(child.Name, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out int index))
            {
                result.Add((index, child));
            }
        }

        result.Sort(static (left, right) => left.Index.CompareTo(right.Index));
        return result;
    }

    /// <summary>收集顶层动作名，自动下钻数字包裹层。</summary>
    private static List<string> CollectActionNames(WzNode imageNode)
    {
        List<string> actions = new();
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        void Add(string name)
        {
            if (seen.Add(name))
            {
                actions.Add(name);
            }
        }

        foreach (WzNode child in imageNode.Children)
        {
            if (string.Equals(child.Name, "info", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (IsNumericWrapper(child))
            {
                foreach (WzNode nested in child.Children)
                {
                    if (IsRenderableNode(nested))
                    {
                        Add(nested.Name);
                    }
                }

                continue;
            }

            if (IsRenderableNode(child))
            {
                Add(child.Name);
            }
        }

        return actions;
    }
}
