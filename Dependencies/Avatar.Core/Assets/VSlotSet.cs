namespace Avatar.Core.Assets;

/// <summary>
/// 可视槽集合（<c>info/vslot</c>）。WZ 把多个两位槽位码直接拼成一个字符串，例如：
/// <c>"WpSi"</c>（武器+盾）、<c>"MaPn"</c>（上衣+裤裙）、<c>"CpH1H5"</c>。
/// </summary>
/// <remarks>
/// 冲突判定完全依赖“集合包含”而不是 islot 一对一：
/// 长袍 <c>vslot=MaPn</c> ⊇ 上衣 <c>Ma</c> / 裤裙 <c>Pn</c>，因此穿长袍时后两者必须隐藏；
/// 双手武器 <c>WpSi</c> ⊇ 盾 <c>Si</c>，因此装备双手武器会卸盾；
/// 而普通帽子 <c>CpH1H5</c> <b>不</b>包含发型 <c>H1…Hb</c>，所以头发保留，仅由图层顺序被帽子遮挡。
/// </remarks>
public sealed class VSlotSet
{
    private readonly HashSet<string> _tokens;

    private VSlotSet(HashSet<string> tokens, string raw)
    {
        this._tokens = tokens;
        this.Raw = raw;
    }

    /// <summary>空集合。</summary>
    public static VSlotSet Empty { get; } = new(new HashSet<string>(StringComparer.OrdinalIgnoreCase), string.Empty);

    /// <summary>原始字符串。</summary>
    public string Raw { get; }

    /// <summary>槽位码数量。</summary>
    public int Count => this._tokens.Count;

    /// <summary>全部槽位码（两位一组）。</summary>
    public IReadOnlyCollection<string> Tokens => this._tokens;

    /// <summary>
    /// 解析 vslot 字符串。空串返回 <see cref="Empty"/>；长度不是 2 的倍数时按两位切分并忽略尾部残留。
    /// </summary>
    /// <param name="raw">原始字符串。</param>
    /// <returns>集合。</returns>
    public static VSlotSet Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Empty;
        }

        string text = raw.Trim();
        HashSet<string> tokens = new(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index + 1 < text.Length; index += 2)
        {
            tokens.Add(text.Substring(index, 2));
        }

        // 对于异常长度（例如奇数），把最后一个字符也记录下来，避免信息丢失。
        if (text.Length % 2 != 0)
        {
            tokens.Add(text[^1].ToString());
        }

        return new VSlotSet(tokens, text);
    }

    /// <summary>是否包含指定槽位码。</summary>
    /// <param name="token">两位槽位码，如 <c>Ma</c>。</param>
    /// <returns>包含返回 <c>true</c>。</returns>
    public bool Contains(string token) => this._tokens.Contains(token);

    /// <summary>
    /// 判断本集合是否为 <paramref name="other"/> 的超集（含相等）。
    /// </summary>
    /// <param name="other">另一个集合。</param>
    /// <returns>是超集返回 <c>true</c>。</returns>
    public bool IsSupersetOf(VSlotSet other)
    {
        if (other._tokens.Count == 0)
        {
            return false;
        }

        return this._tokens.IsSupersetOf(other._tokens);
    }

    /// <inheritdoc />
    public override string ToString() => this.Raw;
}
