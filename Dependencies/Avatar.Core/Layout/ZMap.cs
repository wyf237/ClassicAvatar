using WzReader.Core.Dom;

namespace Avatar.Core.Layout;

/// <summary>
/// 从 <c>Base/zmap.img</c> 读取的图层顺序表。
/// </summary>
/// <remarks>
/// <para>
/// zmap.img 的节点顺序是官方的绘制优先级，方向为<b>从前到后</b>（front-to-back）：
/// 越靠前的节点（<c>emotionOverBody</c>、<c>capeOverHead</c> 等）越在画面上层；
/// 越靠后的节点（<c>body</c>、<c>backCape</c> 等）越在画面下层。
/// </para>
/// <para>
/// 本类对外统一为“序号越大越靠前”的约定（与 <see cref="LayerOrder"/> 一致），
/// 因此加载时把节点顺序反向编号：最靠后的节点序号 0，最靠前的节点序号最大。
/// </para>
/// <para>
/// 范围界定：仅使用 <c>characterStart</c> 与 <c>characterEnd</c> 之间的节点作为角色图层；
/// 范围之外的是坐骑（saddle/taming）、怪物装备（mobEquip）等非角色图层，忽略。
/// </para>
/// <para>
/// 注意：<c>zmap.img</c> 的根节点是 img 条目（其 <see cref="WzNode.Value"/> 是 Null 而不是
/// <see cref="WzProperty"/>），不能用 <see cref="WzNode.IsContainer"/> 判定，必须看子节点。
/// </para>
/// </remarks>
public sealed class ZMap
{
    /// <summary>角色图层起始标记。</summary>
    private const string StartMarker = "characterStart";

    /// <summary>角色图层结束标记。</summary>
    private const string EndMarker = "characterEnd";

    /// <summary>空层序表（Base 包不可用时的兜底）。</summary>
    public static ZMap Empty { get; } = new(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));

    private readonly Dictionary<string, int> _ranks;

    /// <summary>创建层序表。</summary>
    /// <param name="ranks">图层名 → 绘制序号（越大越靠前）。</param>
    private ZMap(Dictionary<string, int> ranks)
    {
        this._ranks = ranks;
    }

    /// <summary>全部图层名及其序号（只读）。</summary>
    public IReadOnlyDictionary<string, int> Ranks => this._ranks;

    /// <summary>图层总数。</summary>
    public int Count => this._ranks.Count;

    /// <summary>
    /// 取图层绘制序号。
    /// </summary>
    /// <param name="layer">图层名（即部件的 <c>z</c> 值）。</param>
    /// <param name="rank">绘制序号（越大越靠前）。</param>
    /// <returns>图层在 zmap 中存在返回 <c>true</c>。</returns>
    public bool TryGetRank(string layer, out int rank) =>
        this._ranks.TryGetValue(layer, out rank);

    /// <summary>
    /// 从 zmap.img 节点加载层序表。
    /// </summary>
    /// <param name="zmapNode">zmap.img 根节点（img 条目或属性容器均可）。</param>
    /// <returns>层序表；节点不存在或没有角色图层时返回 <see cref="Empty"/>。</returns>
    public static ZMap Load(WzNode? zmapNode)
    {
        if (zmapNode == null)
        {
            return Empty;
        }

        // 先按文件顺序收集角色图层名（front → back）。
        List<string> layers = new();
        bool inRange = false;

        foreach (WzNode child in zmapNode.Children)
        {
            string name = child.Name;

            if (string.Equals(name, StartMarker, StringComparison.OrdinalIgnoreCase))
            {
                inRange = true;
                continue;
            }

            if (string.Equals(name, EndMarker, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (inRange && !string.IsNullOrWhiteSpace(name))
            {
                layers.Add(name);
            }
        }

        if (layers.Count == 0)
        {
            return Empty;
        }

        // 反向编号：列表中越靠后 → 序号越小（越靠画面下层）。
        Dictionary<string, int> ranks = new(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < layers.Count; index++)
        {
            ranks[layers[index]] = layers.Count - 1 - index;
        }

        return new ZMap(ranks);
    }
}
