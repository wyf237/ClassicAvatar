using System.Collections.Concurrent;
using Avatar.Core.Models;
using WzReader.Core.Dom;
using WzReader.Core.Package;

namespace Avatar.Core.Assets;

/// <summary>
/// 物品目录：按槽位/ID 提供 <see cref="ItemDescriptor"/>，并支持按目录枚举（供试衣间列表/搜索使用）。
/// </summary>
/// <remarks>
/// 描述对象带缓存且不可变，重复查询同一物品不会重复解析 <c>info</c>。
/// 枚举只读取包目录（不解析 img 内容之外的部分），大目录下也足够快。
/// </remarks>
public sealed class ItemCatalog
{
    private readonly WzDataRoot _root;
    private readonly Func<EquipSlot, int, WzNode?> _imageLoader;
    private readonly ConcurrentDictionary<(EquipSlot Slot, int Id), ItemDescriptor?> _cache = new();

    /// <summary>创建目录。</summary>
    /// <param name="root">数据根。</param>
    /// <param name="imageLoader">按槽位/ID 取 img 节点的委托。</param>
    internal ItemCatalog(WzDataRoot root, Func<EquipSlot, int, WzNode?> imageLoader)
    {
        this._root = root;
        this._imageLoader = imageLoader;
    }

    /// <summary>
    /// 取物品描述；img 不存在时返回 <c>null</c>（结果也会缓存）。
    /// </summary>
    /// <param name="slot">槽位。</param>
    /// <param name="itemId">物品 ID。</param>
    /// <returns>描述或 <c>null</c>。</returns>
    public ItemDescriptor? Get(EquipSlot slot, int itemId)
    {
        return this._cache.GetOrAdd((slot, itemId), key =>
        {
            WzNode? image = this._imageLoader(key.Slot, key.Id);
            return image == null ? null : ItemDescriptor.Create(image, key.Slot, key.Id, this._root.Diagnostics);
        });
    }

    /// <summary>
    /// 枚举某槽位目录下的全部物品 ID（按 ID 升序）。
    /// </summary>
    /// <remarks>
    /// 只读取包目录条目，不解析 img 内容，因此适合“先列出条目、再逐项加载图标/详情”的增量界面。
    /// </remarks>
    /// <param name="slot">槽位。</param>
    /// <returns>物品 ID 列表。</returns>
    public IReadOnlyList<int> EnumerateIds(EquipSlot slot)
    {
        WzPackage? package = this._root.GetPackage(ItemPathResolver.GetPackagePath(slot));
        if (package == null)
        {
            return Array.Empty<int>();
        }

        List<int> ids = new();
        foreach (WzNode image in package.Root.Children)
        {
            if (!image.IsImage || !ItemPathResolver.TryParseItemId(image.Name, out int id))
            {
                continue;
            }

            if (this.IsSlotOf(slot, id))
            {
                ids.Add(id);
            }
        }

        ids.Sort();
        return ids;
    }

    /// <summary>
    /// 枚举某槽位目录下的全部物品描述。
    /// </summary>
    /// <param name="slot">槽位。</param>
    /// <param name="maxCount">最多返回数量；0 表示不限制。</param>
    /// <returns>描述列表（按 ID 升序）。</returns>
    public IReadOnlyList<ItemDescriptor> Enumerate(EquipSlot slot, int maxCount = 0)
    {
        List<ItemDescriptor> result = new();
        foreach (int id in this.EnumerateIds(slot))
        {
            ItemDescriptor? descriptor = this.Get(slot, id);
            if (descriptor != null)
            {
                result.Add(descriptor);
            }

            if (maxCount > 0 && result.Count >= maxCount)
            {
                break;
            }
        }

        return result;
    }

    /// <summary>
    /// 移除某槽位的全部缓存条目。
    /// </summary>
    /// <param name="slot">槽位。</param>
    public void Clear(EquipSlot slot)
    {
        foreach (var key in this._cache.Keys)
        {
            if (key.Slot == slot)
            {
                this._cache.TryRemove(key, out _);
            }
        }
    }

    /// <summary>
    /// 清空全部描述缓存。下次 <see cref="Get"/> 会重新解析 <c>info</c>。
    /// </summary>
    public void Clear() => this._cache.Clear();

    /// <summary>
    /// 枚举某槽位目录下的物品 ID，并按性别过滤。
    /// </summary>
    /// <param name="slot">槽位。</param>
    /// <param name="gender">性别过滤：<c>null</c> 返回全部；
    /// <see cref="Gender.Unisex"/> 仅返回不限性别物品；
    /// <see cref="Gender.Male"/> / <see cref="Gender.Female"/> 返回对应性别 + 不限性别物品。</param>
    /// <returns>物品 ID 列表。</returns>
    public IReadOnlyList<int> EnumerateIds(EquipSlot slot, Gender? gender)
    {
        if (gender == null)
        {
            return this.EnumerateIds(slot);
        }

        WzPackage? package = this._root.GetPackage(ItemPathResolver.GetPackagePath(slot));
        if (package == null)
        {
            return Array.Empty<int>();
        }

        List<int> ids = new();
        foreach (WzNode image in package.Root.Children)
        {
            if (!image.IsImage || !ItemPathResolver.TryParseItemId(image.Name, out int id))
            {
                continue;
            }

            if (!this.IsSlotOf(slot, id) || !this.MatchesGender(slot, id, gender.Value))
            {
                continue;
            }

            ids.Add(id);
        }

        ids.Sort();
        return ids;
    }

    /// <summary>
    /// 枚举某槽位目录下的物品描述，并按性别过滤。
    /// </summary>
    /// <param name="slot">槽位。</param>
    /// <param name="maxCount">最多返回数量；0 表示不限制。</param>
    /// <param name="gender">性别过滤，语义同 <see cref="EnumerateIds(EquipSlot, Gender?)"/>。</param>
    /// <returns>描述列表（按 ID 升序）。</returns>
    public IReadOnlyList<ItemDescriptor> Enumerate(EquipSlot slot, int maxCount, Gender? gender)
    {
        if (gender == null)
        {
            return this.Enumerate(slot, maxCount);
        }

        List<ItemDescriptor> result = new();
        foreach (int id in this.EnumerateIds(slot, gender))
        {
            ItemDescriptor? descriptor = this.Get(slot, id);
            if (descriptor != null)
            {
                result.Add(descriptor);
            }

            if (maxCount > 0 && result.Count >= maxCount)
            {
                break;
            }
        }

        return result;
    }

    /// <summary>
    /// 判断某物品在给定性别过滤条件下是否可见。
    /// </summary>
    /// <remarks>
    /// 发型/脸直接用 ID 区间推断（零解析成本）；
    /// 其他部位走 <see cref="ItemDescriptor.EffectiveGender"/>（已走 Catalog 缓存）。
    /// </remarks>
    private bool MatchesGender(EquipSlot slot, int itemId, Gender gender)
    {
        Gender inferred = ItemDescriptor.InferGender(slot, itemId);
        Gender effective = inferred != Gender.Unisex
            ? inferred
            : (this.Get(slot, itemId)?.EffectiveGender ?? Gender.Unisex);

        return gender switch
        {
            Gender.Unisex => effective == Gender.Unisex,
            _ => effective == gender || effective == Gender.Unisex,
        };
    }

    /// <summary>
    /// 判断某 ID 是否属于给定槽位（用于 Accessory 这类混合目录）。
    /// </summary>
    /// <param name="slot">槽位。</param>
    /// <param name="itemId">物品 ID。</param>
    /// <returns>属于返回 <c>true</c>。</returns>
    public bool IsSlotOf(EquipSlot slot, int itemId)
    {
        return slot switch
        {
            EquipSlot.Body => itemId is >= ItemPathResolver.BodyBaseId and < ItemPathResolver.BodyBaseId + ItemPathResolver.SkinCount,
            EquipSlot.Head => itemId is >= ItemPathResolver.HeadBaseId and < ItemPathResolver.HeadBaseId + ItemPathResolver.SkinCount,
            EquipSlot.Hair => itemId is >= 30000 and < 40000,
            EquipSlot.Face => itemId is >= 20000 and < 30000,
            EquipSlot.Cap => itemId is >= 1000000 and < 1010000,
            EquipSlot.Coat => itemId is >= 1040000 and < 1050000,
            EquipSlot.Longcoat => itemId is >= 1050000 and < 1060000,
            EquipSlot.Pants => itemId is >= 1060000 and < 1070000,
            EquipSlot.Shoes => itemId is >= 1070000 and < 1080000,
            EquipSlot.Glove => itemId is >= 1080000 and < 1090000,
            EquipSlot.Shield => itemId is >= 1090000 and < 1100000,
            EquipSlot.Cape => itemId is >= 1100000 and < 1110000,
            EquipSlot.Weapon => itemId is >= 1300000 and < 1800000,
            // 饰品共用 Character/Accessory 目录，按官方 ID 段区分：
            // 0101xxxx 脸饰（Af）、0102xxxx 眼饰（Ay）、0103xxxx 耳饰（Ae）。
            EquipSlot.FaceAccessory => itemId is >= 1010000 and < 1020000,
            EquipSlot.EyeAccessory => itemId is >= 1020000 and < 1030000,
            EquipSlot.EarAccessory => itemId is >= 1030000 and < 1040000,
            EquipSlot.Ring => itemId is >= 1110000 and < 1120000,
            _ => false,
        };
    }
}
