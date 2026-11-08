namespace Avatar.Core.Layout;

/// <summary>
/// 图层排序结果。
/// </summary>
public sealed class LayerSortResult
{
    private readonly IReadOnlyDictionary<string, int> _ranks;

    /// <summary>创建结果。</summary>
    /// <param name="ordered">从后到前排序的图层名（含约束表达式拆分出的子节点）。</param>
    /// <param name="ranks">图层名 → 绘制序号（越大越靠前）。</param>
    /// <param name="unknownLayers">未收录在基础层序表中的图层名。</param>
    /// <param name="hasCycle">是否存在无法满足的循环约束。</param>
    internal LayerSortResult(
        IReadOnlyList<string> ordered,
        IReadOnlyDictionary<string, int> ranks,
        IReadOnlyList<string> unknownLayers,
        bool hasCycle)
    {
        this.Ordered = ordered;
        this._ranks = ranks;
        this.UnknownLayers = unknownLayers;
        this.HasCycle = hasCycle;
    }

    /// <summary>从后到前排序的全部图层名。</summary>
    public IReadOnlyList<string> Ordered { get; }

    /// <summary>未收录的图层名（已按“身体之上”兜底处理）。</summary>
    public IReadOnlyList<string> UnknownLayers { get; }

    /// <summary>是否存在循环约束（数据异常，已尽量降级排序）。</summary>
    public bool HasCycle { get; }

    /// <summary>取图层绘制序号（越大越靠前）；未知图层返回身体层序号 + 1。</summary>
    /// <param name="layer">图层名。</param>
    /// <returns>序号。</returns>
    public int RankOf(string layer) => this._ranks.TryGetValue(layer, out int rank) ? rank : this._ranks.Count;
}

/// <summary>
/// 图层顺序求解：基础层序表 + <c>XOverY</c>/<c>XBelowY</c> 约束拓扑排序。
/// </summary>
/// <remarks>
/// 为什么不能只维护一张固定表：WZ 的 <c>z</c> 逐帧变化（<c>arm</c> / <c>armOverHair</c> /
/// <c>armOverHairBelowWeapon</c>），固定表无法覆盖组合。这里把命名里的语义解析成偏序约束，
/// 再用拓扑排序得到每一帧的实际顺序；基础表只负责“无修饰名”的默认先后来兜底。
/// <para>
/// 同层平局规则：基础表序号 → 层名字典序，保证输出稳定可复现。
/// </para>
/// </remarks>
public static class LayerOrder
{
    /// <summary>
    /// 基础层序表（从后到前）。覆盖本数据集实测的全部 z 名称，新增数据只需补表。
    /// </summary>
    public static IReadOnlyList<string> BaseOrder { get; } = new[]
    {
        // ---------- 背向（爬梯/绳索等）：整体最靠后 ----------
        "backCape",
        "backShield",
        "backGlove",
        "backMailChestBelowPants",
        "backPantsBelowShoes",
        "backShoes",
        "backMailChest",
        "backMailChestOverPants",
        "backBody",
        "backArm",
        "backWeapon",
        "backHairShade",
        "backHead",
        "backHairBelowCap",
        "backHair",
        "backCap",
        "backAccessoryOverHead",
        "backCapAccessory",

        // ---------- 身体/装备的“背后层” ----------
        "capeBelowBody",
        "shieldBelowBody",
        "gloveBelowBody",
        "capAccessoryBelowBody",
        "capBelowBody",
        "mailChestBelowPants",
        "pantsBelowShoes",
        "pants",
        "shoes",
        "shoesOverPants",
        "shoesTop",
        "mailChest",
        "mailArm",
        "cape",
        "glove",
        "gloveOverBody",
        "gloveBelowWeapon",
        "shield",

        // ---------- 身体本体 ----------
        "body",
        "arm",

        // ---------- 手/袖在头部附近的穿插 ----------
        "gloveBelowHead",
        "gloveBelowMailArm",
        "mailArmBelowHead",
        "mailArmBelowHeadOverMailChest",
        "armBelowHead",
        "armBelowHeadOverMailChest",

        // ---------- 头部区域 ----------
        "hairShade",
        "head",
        "face",
        "accessoryFaceBelowFace",
        "hair",
        "hairOverHead",
        "accessoryFace",
        "accessoryOverHair",
        "accessoryEar",
        "cap",
        "accessoryEyeOverCap",

        // ---------- 武器 ----------
        "handBelowWeapon",
        "weaponBelowBody",
        "weaponBelowArm",
        "weaponOverBody",
        "weapon",
        "weaponOverGlove",
        "weaponOverHand",

        // ---------- 最前方（抬手/武器遮挡） ----------
        "gloveOverHair",
        "gloveWristOverHair",
        "handOverHair",
        "armOverHair",
        "mailArmOverHair",
        "mailArmOverHairBelowWeapon",
        "armOverHairBelowWeapon",
        "capeOverHead",
        "shieldOverHair",
        "weaponOverArm",
    };

    /// <summary>基础表索引（忽略大小写）。</summary>
    private static readonly Dictionary<string, int> BaseRanks = BuildBaseRanks();

    /// <summary>
    /// WZ 中出现的别名层 → zmap 中的标准层。老版本 zmap 没有
    /// <c>capBelowBody</c>，但它的语义与 <c>capAccessoryBelowBody</c> 相同
    /// （例如 1007298 关羽头饰的长须部件）。
    /// </summary>
    private static readonly Dictionary<string, string> LayerAliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["capBelowBody"] = "capAccessoryBelowBody",
        };

    /// <summary>身体层在基础表中的序号，用于未知层兜底。</summary>
    private static readonly int BodyRank = BaseRanks["body"];

    /// <summary>
    /// 对一组图层名求解绘制顺序。
    /// </summary>
    /// <param name="layers">出现的图层名（可含重复）。</param>
    /// <param name="externalRanks">外部层序表（如 <c>Base/zmap.img</c>）；为 <c>null</c> 时使用内置 <see cref="BaseOrder"/>。</param>
    /// <returns>排序结果。</returns>
    public static LayerSortResult SortLayers(IEnumerable<string> layers, IReadOnlyDictionary<string, int>? externalRanks = null)
    {
        ArgumentNullException.ThrowIfNull(layers);

        Dictionary<string, Node> nodes = new(StringComparer.OrdinalIgnoreCase);
        SortedSet<string> unknown = new(StringComparer.OrdinalIgnoreCase);

        foreach (string layer in layers)
        {
            if (string.IsNullOrWhiteSpace(layer))
            {
                continue;
            }

            Ensure(layer);
        }

        // Kahn 拓扑排序：每次从入度为 0 的节点中取“基础序号最小、名字最小”者。
        List<string> ready = nodes.Values
            .Where(static node => node.InDegree == 0)
            .Select(static node => node.Name)
            .ToList();

        List<string> ordered = new(nodes.Count);
        while (ready.Count > 0)
        {
            int bestIndex = 0;
            for (int index = 1; index < ready.Count; index++)
            {
                if (CompareNodes(nodes[ready[index]], nodes[ready[bestIndex]]) < 0)
                {
                    bestIndex = index;
                }
            }

            string current = ready[bestIndex];
            ready.RemoveAt(bestIndex);
            ordered.Add(current);

            foreach (string next in nodes[current].Next)
            {
                Node target = nodes[next];
                target.InDegree--;
                if (target.InDegree == 0)
                {
                    ready.Add(next);
                }
            }
        }

        bool hasCycle = ordered.Count != nodes.Count;
        if (hasCycle)
        {
            // 数据出现环时不能让渲染失败：剩余节点按基础序号兜底追加。
            IEnumerable<string> leftovers = nodes.Values
                .Where(node => !ordered.Contains(node.Name, StringComparer.OrdinalIgnoreCase))
                .OrderBy(static node => node.Rank)
                .ThenBy(static node => node.Name, StringComparer.OrdinalIgnoreCase)
                .Select(static node => node.Name);

            ordered.AddRange(leftovers);
        }

        Dictionary<string, int> ranks = new(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < ordered.Count; index++)
        {
            ranks[ordered[index]] = index;
        }

        return new LayerSortResult(ordered, ranks, unknown.ToList(), hasCycle);

        // 确保节点存在，并递归登记约束表达式。
        void Ensure(string name)
        {
            string key = name.Trim();
            if (nodes.ContainsKey(key))
            {
                return;
            }

            double rank = RankOfBase(key, unknown, externalRanks);
            Node node = new(key, rank);
            nodes[key] = node;

            // zmap 已经给出权威的完整顺序：精确命中时不再拆修饰词，
            // 否则名字里的 Over/Below 约束会与 zmap 的全序冲突而打乱顺序。
            if (externalRanks != null && externalRanks.ContainsKey(key))
            {
                return;
            }

            if (!TryParseModifiers(key, out List<(string Operand, bool IsOver)> modifiers))
            {
                return;
            }

            // 约束语义：主语是“第一个修饰词之前的名字”，所有修饰词都相对主语：
            //   X Over Y  → Y 在 X 之前（更靠后）
            //   X Below Y → X 在 Y 之前（更靠后）
            // 因此 "mailArmOverHairBelowWeapon" 拆成 (hair, Over) 与 (weapon, Below)，
            // 得到 hair → mailArmOverHairBelowWeapon → weapon，正好对应“盖住头发、被武器盖住”。
            foreach ((string operand, bool isOver) in modifiers)
            {
                Ensure(operand);

                if (isOver)
                {
                    AddEdge(operand, key);
                }
                else
                {
                    AddEdge(key, operand);
                }
            }
        }

        void AddEdge(string from, string to)
        {
            if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (nodes[from].Next.Add(to))
            {
                nodes[to].InDegree++;
            }
        }
    }

    /// <summary>
    /// 解析全部修饰词：主语是第一个 <c>Over</c>/<c>Below</c> 之前的片段，修饰词执行到下一个修饰词之前。
    /// </summary>
    /// <example>
    /// <c>mailArmOverHairBelowWeapon</c> → [(<c>hair</c>, Over), (<c>weapon</c>, Below)]。
    /// </example>
    /// <param name="name">图层名。</param>
    /// <param name="modifiers">解析出的（操作数, 是否 Over）列表。</param>
    /// <returns>至少解析出一个修饰词时返回 <c>true</c>。</returns>
    private static bool TryParseModifiers(string name, out List<(string Operand, bool IsOver)> modifiers)
    {
        modifiers = new List<(string Operand, bool IsOver)>();
        int index = 0;

        while (index < name.Length)
        {
            int overIndex = name.IndexOf("Over", index, StringComparison.Ordinal);
            int belowIndex = name.IndexOf("Below", index, StringComparison.Ordinal);

            int marker;
            int markerLength;
            bool isOver;
            if (overIndex > 0 && (belowIndex <= 0 || overIndex < belowIndex))
            {
                marker = overIndex;
                markerLength = "Over".Length;
                isOver = true;
            }
            else if (belowIndex > 0 && (overIndex <= 0 || belowIndex < overIndex))
            {
                marker = belowIndex;
                markerLength = "Below".Length;
                isOver = false;
            }
            else
            {
                break;
            }

            int operandStart = marker + markerLength;
            int operandEnd = name.Length;

            int nextOver = name.IndexOf("Over", operandStart, StringComparison.Ordinal);
            if (nextOver > 0)
            {
                operandEnd = Math.Min(operandEnd, nextOver);
            }

            int nextBelow = name.IndexOf("Below", operandStart, StringComparison.Ordinal);
            if (nextBelow > 0)
            {
                operandEnd = Math.Min(operandEnd, nextBelow);
            }

            string operand = name[operandStart..operandEnd];
            if (operand.Length > 0)
            {
                modifiers.Add((operand, isOver));
            }

            index = operandEnd;
        }

        return modifiers.Count > 0;
    }

    /// <summary>取约束表达式的主语（第一个修饰词之前的名字）。</summary>
    /// <param name="name">图层名。</param>
    /// <returns>主语名（无修饰词时返回原名）。</returns>
    private static string CanonicalBase(string name)
    {
        int overIndex = name.IndexOf("Over", 1, StringComparison.Ordinal);
        int belowIndex = name.IndexOf("Below", 1, StringComparison.Ordinal);

        int marker = int.MaxValue;
        if (overIndex > 0)
        {
            marker = Math.Min(marker, overIndex);
        }

        if (belowIndex > 0)
        {
            marker = Math.Min(marker, belowIndex);
        }

        return marker == int.MaxValue ? name : name[..marker];
    }

    /// <summary>
    /// 计算基础序号。优先使用外部层序表（zmap），未命中时回退到内置 <see cref="BaseOrder"/>，
    /// 仍未命中则按“背向最底”或“身体之上”兜底。
    /// </summary>
    private static double RankOfBase(string name, SortedSet<string> unknown, IReadOnlyDictionary<string, int>? externalRanks)
    {
        if (externalRanks is { Count: > 0 })
        {
            // 老 zmap 没有 capBelowBody 的条目，按等义的 capAccessoryBelowBody 取序号。
            string externalName = LayerAliases.TryGetValue(name, out string? alias) ? alias : name;

            // 有 zmap 时全部使用同一套序号尺度，避免与内置表的序号混排。
            if (externalRanks.TryGetValue(externalName, out int extRank))
            {
                return extRank;
            }

            string extBase = CanonicalBase(externalName);
            if (externalRanks.TryGetValue(extBase, out int extCanonical))
            {
                return extCanonical;
            }

            if (extBase.StartsWith("back", StringComparison.OrdinalIgnoreCase))
            {
                // 背向图层整体在身体之后。
                return externalRanks.TryGetValue("backBody", out int backRank)
                    ? backRank - 1
                    : -1;
            }

            unknown.Add(name);
            return (externalRanks.TryGetValue("body", out int bodyRank) ? bodyRank : 0) + 0.5;
        }

        // 内置基础表精确命中。
        if (BaseRanks.TryGetValue(name, out int exact))
        {
            return exact;
        }

        string baseName = CanonicalBase(name);
        if (BaseRanks.TryGetValue(baseName, out int rank))
        {
            return rank;
        }

        if (baseName.StartsWith("back", StringComparison.OrdinalIgnoreCase))
        {
            return -1000;
        }

        unknown.Add(name);
        return BodyRank + 0.5;
    }

    /// <summary>节点比较：基础序号 → 名称（忽略大小写）。</summary>
    private static int CompareNodes(Node left, Node right)
    {
        int rank = left.Rank.CompareTo(right.Rank);
        return rank != 0 ? rank : string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>建立基础序号表。</summary>
    private static Dictionary<string, int> BuildBaseRanks()
    {
        Dictionary<string, int> ranks = new(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < BaseOrder.Count; index++)
        {
            ranks[BaseOrder[index]] = index;
        }

        return ranks;
    }

    /// <summary>拓扑图节点。</summary>
    private sealed class Node
    {
        public Node(string name, double rank)
        {
            this.Name = name;
            this.Rank = rank;
        }

        public string Name { get; }

        public double Rank { get; }

        public HashSet<string> Next { get; } = new(StringComparer.OrdinalIgnoreCase);

        public int InDegree { get; set; }
    }
}
