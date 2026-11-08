using Avatar.Core.Models;

namespace Avatar.Core.Assets;

/// <summary>
/// 物品 ID → WZ img 路径的统一映射。所有素材定位都经过这里，避免各处拼字符串。
/// </summary>
public static class ItemPathResolver
{
    /// <summary>身体 img 的 ID 基址（<c>00002000</c>）。</summary>
    public const int BodyBaseId = 2000;

    /// <summary>头部 img 的 ID 基址（<c>00012000</c>）。</summary>
    public const int HeadBaseId = 12000;

    /// <summary>可选肤色数量（0..6）。</summary>
    public const int SkinCount = 7;

    /// <summary>按肤色取身体 ID（00002000 + skin）。</summary>
    /// <param name="skinIndex">肤色下标 0..6。</param>
    /// <returns>身体 ID。</returns>
    public static int BodyIdOf(int skinIndex) => BodyBaseId + Math.Clamp(skinIndex, 0, SkinCount - 1);

    /// <summary>按肤色取头部 ID（00012000 + skin）。</summary>
    /// <param name="skinIndex">肤色下标 0..6。</param>
    /// <returns>头部 ID。</returns>
    public static int HeadIdOf(int skinIndex) => HeadBaseId + Math.Clamp(skinIndex, 0, SkinCount - 1);

    /// <summary>
    /// 取物品 img 的绝对路径（相对 Data 根）。
    /// </summary>
    /// <example>
    /// <c>GetImagePath(EquipSlot.Hair, 30000)</c> → <c>Character/Hair/00030000.img</c>；
    /// <c>GetImagePath(EquipSlot.Body, 2000)</c> → <c>Character/00002000.img</c>。
    /// </example>
    /// <param name="slot">槽位。</param>
    /// <param name="itemId">物品 ID。</param>
    /// <returns>img 路径。</returns>
    public static string GetImagePath(EquipSlot slot, int itemId)
    {
        string folder = EquipSlotInfo.FolderOf(slot);
        string fileName = $"{itemId:D8}.img";
        return folder.Length == 0 ? $"Character/{fileName}" : $"Character/{folder}/{fileName}";
    }

    /// <summary>取某槽位对应的包根路径（例如 <c>Character/Hair</c>）。</summary>
    /// <param name="slot">槽位。</param>
    /// <returns>包根路径。</returns>
    public static string GetPackagePath(EquipSlot slot)
    {
        string folder = EquipSlotInfo.FolderOf(slot);
        return folder.Length == 0 ? "Character" : $"Character/{folder}";
    }

    /// <summary>从八位文件名（如 <c>01040000.img</c>）解析物品 ID。</summary>
    /// <param name="imageName">img 节点名。</param>
    /// <param name="itemId">解析结果。</param>
    /// <returns>解析成功返回 <c>true</c>。</returns>
    public static bool TryParseItemId(string imageName, out int itemId)
    {
        itemId = 0;
        string name = imageName.EndsWith(".img", StringComparison.OrdinalIgnoreCase)
            ? imageName[..^4]
            : imageName;

        return name.Length is > 0 and <= 8 &&
               int.TryParse(name, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out itemId);
    }
}
