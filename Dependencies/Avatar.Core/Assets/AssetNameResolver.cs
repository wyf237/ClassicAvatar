using System.Collections.Concurrent;
using System.Globalization;
using Avatar.Core.Models;
using WzReader.Core.Dom;
using WzReader.Core.Package;

namespace Avatar.Core.Assets;

/// <summary>
/// 从 String.wz 的 Eqp.img / Cash.img 等目录按物品 ID 预索引本地化名称。
/// </summary>
/// <remarks>
/// <para>
/// 名称数据来自 <c>String.wz</c> 包：
/// </para>
/// <list type="bullet">
///   <item>
///     <c>Eqp.img/ClassicWorld/&lt;SlotName&gt;/&lt;ItemId&gt;</c>
///     —— 普通装备 / 饰品 / 发型 / 脸 / 皮肤 / 武器 / 披风 / 盾牌等。
///   </item>
///   <item>
///     <c>Cash.img/&lt;CategoryDir&gt;/&lt;ItemId&gt;</c>
///     —— 现金道具（点装），按大类分层，需要递归遍历。
///   </item>
/// </list>
/// <para>
/// 构建索引需要一次完整遍历（几千个节点，毫秒到秒级），之后按 ID O(1) 查询。
/// <c>Lazy</c> 保证线程安全且首次调用才工作；索引本身是不可变只读结构。
/// </para>
/// </remarks>
public sealed class AssetNameResolver
{
    /// <summary>EquipSlot → String.wz 下对应的目录名。</summary>
    private static readonly Dictionary<EquipSlot, string> EqpSlotDirs = new()
    {
        { EquipSlot.Cap, "Cap" },
        { EquipSlot.Coat, "Coat" },
        { EquipSlot.Longcoat, "Longcoat" },
        { EquipSlot.Pants, "Pants" },
        { EquipSlot.Shoes, "Shoes" },
        { EquipSlot.Glove, "Glove" },
        { EquipSlot.Shield, "Shield" },
        { EquipSlot.Cape, "Cape" },
        { EquipSlot.Weapon, "Weapon" },
        { EquipSlot.Hair, "Hair" },
        { EquipSlot.Face, "Face" },
        { EquipSlot.FaceAccessory, "Accessory" },
        { EquipSlot.EyeAccessory, "Accessory" },
        { EquipSlot.EarAccessory, "Accessory" },
        { EquipSlot.Ring, "Ring" },
    };

    /// <summary>打开数据时预构建的索引（itemId → displayName）。</summary>
    private readonly Lazy<IReadOnlyDictionary<int, string>> _index;

    /// <summary>创建名称解析器。</summary>
    /// <param name="root">底层数据根。</param>
    public AssetNameResolver(WzDataRoot root)
    {
        this._index = new Lazy<IReadOnlyDictionary<int, string>>(() => Build(root), isThreadSafe: true);
    }

    /// <summary>总索引条目数（调试/日志用）。</summary>
    public int Count => this._index.Value.Count;

    /// <summary>按物品 ID 取本地化名称；未索引到返回 <c>null</c>。</summary>
    public string? TryGetName(int itemId)
    {
        return itemId > 0 && this._index.Value.TryGetValue(itemId, out string? name) && name.Length > 0
            ? name
            : null;
    }

    /// <summary>
    /// 按物品 ID 取本地化名称，兜底回退为格式化 ID（例如 <c>"Brown Skullcap (1002000)"</c>）。
    /// </summary>
    public string GetDisplayName(int itemId)
    {
        string? name = this.TryGetName(itemId);
        return name != null
            ? $"{name} ({itemId})"
            : $"#{itemId:D8}";
    }

    private static IReadOnlyDictionary<int, string> Build(WzDataRoot root)
    {
        ConcurrentDictionary<int, string> names = new();

        WzPackage? strPackage = root.GetPackage("String");
        if (strPackage == null)
        {
            return names;
        }

        // 1) Eqp.img/ClassicWorld/<Slot>/<Id> —— 单层，直接精确读取
        WzNode? eqp = strPackage.FindPath("Eqp.img");
        WzNode? classic = eqp?.FindChild("ClassicWorld");
        if (classic != null)
        {
            foreach (WzNode slotDir in classic.Children)
            {
                foreach (WzNode itemNode in slotDir.Children)
                {
                    TryAdd(names, itemNode);
                }
            }
        }

        // 2) Cash.img/<Category>/<Id> —— 两层结构，顶层也带 name，跳过非 id 目录即可
        WzNode? cash = strPackage.FindPath("Cash.img");
        if (cash != null)
        {
            foreach (WzNode catDir in cash.Children)
            {
                foreach (WzNode itemNode in catDir.Children)
                {
                    TryAdd(names, itemNode);
                }
            }
        }

        return names;
    }

    private static void TryAdd(ConcurrentDictionary<int, string> names, WzNode node)
    {
        // 节点名必须是整数才是物品 id，跳过 desc / name / 其他非 id 子节点
        if (!int.TryParse(node.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out int itemId)
            || itemId <= 0)
        {
            return;
        }

        string? name = node.GetString("name");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        names.TryAdd(itemId, name);
    }
}
