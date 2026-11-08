namespace Avatar.Core.Models;

/// <summary>
/// 结构化冲突/诊断。每一次“隐藏 / 替换 / 回退 / 缺失”都会产生一条，便于 UI 提示与问题定位。
/// </summary>
public sealed class AvatarConflict
{
    /// <summary>创建一条诊断。</summary>
    /// <param name="kind">种类。</param>
    /// <param name="slot">涉及槽位。</param>
    /// <param name="itemId">涉及物品 ID（无具体物品时为 0）。</param>
    /// <param name="message">可读说明。</param>
    /// <param name="relatedItemId">关联物品 ID（如覆盖者/被覆盖者）。</param>
    public AvatarConflict(
        ConflictKind kind,
        EquipSlot slot,
        int itemId,
        string message,
        int? relatedItemId = null)
    {
        this.Kind = kind;
        this.Slot = slot;
        this.ItemId = itemId;
        this.Message = message;
        this.RelatedItemId = relatedItemId;
    }

    /// <summary>种类。</summary>
    public ConflictKind Kind { get; }

    /// <summary>涉及槽位。</summary>
    public EquipSlot Slot { get; }

    /// <summary>涉及物品 ID；与具体物品无关时为 0。</summary>
    public int ItemId { get; }

    /// <summary>关联物品 ID（如“被谁覆盖”）。</summary>
    public int? RelatedItemId { get; }

    /// <summary>可读说明。</summary>
    public string Message { get; }

    /// <summary>物品的八位 ID 文本（便于按文件名反查）。</summary>
    public string ItemCode => this.ItemId > 0 ? this.ItemId.ToString("D8") : "-";

    /// <inheritdoc />
    public override string ToString() =>
        $"[{this.Kind}] {EquipSlotInfo.DisplayNameOf(this.Slot)}({this.ItemCode})：{this.Message}";
}
