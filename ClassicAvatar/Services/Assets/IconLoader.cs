using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Avatar.Core.Assets;
using Avatar.Core.Models;
using WzReader.Core.Codecs;
using WzReader.Core.Dom;
using WzReader.Core.Links;

namespace ClassicAvatar.Services;

/// <summary>
/// 装备图标加载器：从物品 img 的 <c>info/iconRaw</c>（发型/脸型取 <c>default/</c> 部件画布）读取画布并转换为 WPF 位图。
/// </summary>
/// <remarks>
/// 图标按“img 路径”做全局缓存（冻结的 <see cref="BitmapSource"/> 可跨窗口共享）：
/// <list type="bullet">
/// <item>同一物品被多个窗口/多个槽位列表读取时只解码一次，避免重复内存占用；</item>
/// <item>需要主动回收时调用 <see cref="ClearCache"/>（见主界面的“清理缓存”按钮）。</item>
/// </list>
/// </remarks>
internal static class IconLoader
{
    /// <summary>图标缓存：键为物品 img 路径（不同槽位共享）。</summary>
    private static readonly ConcurrentDictionary<string, BitmapSource?> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>是否启用常驻缓存（关闭后每次读取都重新解码）。</summary>
    public static bool UseCache { get; set; } = true;

    /// <summary>当前缓存的图标数量。</summary>
    public static int CachedCount => Cache.Count;

    /// <summary>
    /// 加载物品图标。
    /// </summary>
    /// <param name="data">数据入口。</param>
    /// <param name="slot">槽位。</param>
    /// <param name="itemId">物品 ID。</param>
    /// <returns>图标位图；失败返回 <c>null</c>。</returns>
    public static BitmapSource? LoadIcon(AvatarData data, EquipSlot slot, int itemId)
    {
        string imagePath = ItemPathResolver.GetImagePath(slot, itemId);

        if (UseCache && Cache.TryGetValue(imagePath, out BitmapSource? cached))
        {
            return cached;
        }

        BitmapSource? icon = null;
        try
        {
            icon = LoadIconCore(data, slot, itemId);
        }
        catch (Exception)
        {
            // A malformed or partially decompressed image must never crash the
            // browser; the card simply shows an empty icon slot.
            icon = null;
        }

        if (UseCache)
        {
            Cache[imagePath] = icon;
        }

        return icon;
    }

    /// <summary>清空图标缓存（方案 b：手动回收）。</summary>
    public static void ClearCache() => Cache.Clear();

    /// <summary>
    /// 清空指定槽位的图标缓存（其他槽位的图标保留）。
    /// </summary>
    /// <param name="slot">槽位。</param>
    public static void ClearCache(EquipSlot slot)
    {
        string packagePath = ItemPathResolver.GetPackagePath(slot);

        foreach (string key in Cache.Keys.ToArray())
        {
            if (BelongsToSlot(key, packagePath))
            {
                Cache.TryRemove(key, out _);
            }
        }
    }

    /// <summary>判断某 img 路径是否属于指定槽位的包。</summary>
    private static bool BelongsToSlot(string imagePath, string packagePath)
    {
        // 非 Body/Head 槽位：路径形如 Character/Cap/01002000.img，
        // 以 "Character/Cap/" 开头即可。
        if (packagePath.Length > "Character".Length)
        {
            return imagePath.StartsWith(packagePath + "/", StringComparison.OrdinalIgnoreCase);
        }

        // Body/Head 槽位：图片直接挂在 Character 下（Character/00002000.img），
        // 必须排除带下级目录的其他槽位（如 Character/Cap/...）。
        const string characterPrefix = "Character/";
        if (!imagePath.StartsWith(characterPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        int nextSlash = imagePath.IndexOf('/', characterPrefix.Length);
        return nextSlash < 0;
    }

    /// <summary>
    /// 只查询缓存，不触发解码（用于重新打开列表时立即显示已加载过的图标）。
    /// </summary>
    /// <param name="slot">槽位。</param>
    /// <param name="itemId">物品 ID。</param>
    /// <returns>已缓存的图标；未缓存返回 <c>null</c>。</returns>
    public static BitmapSource? GetCachedIcon(EquipSlot slot, int itemId)
    {
        string imagePath = ItemPathResolver.GetImagePath(slot, itemId);
        return Cache.TryGetValue(imagePath, out BitmapSource? icon) ? icon : null;
    }

    /// <summary>实际解码：按槽位选取多个候选路径，逐个尝试。</summary>
    private static BitmapSource? LoadIconCore(AvatarData data, EquipSlot slot, int itemId)
    {
        string imagePath = ItemPathResolver.GetImagePath(slot, itemId);
        WzNode? image = data.FindNode(imagePath);

        // Some cash items live in a paired "_Canvas" package (e.g. 1702302);
        // their info/iconRaw points at a local info/icon that is absent.
        if (image == null)
        {
            image = data.FindNode(CanvasMirrorPath(imagePath));
        }

        if (image == null)
        {
            return null;
        }

        BitmapSource? icon = TryReadInfoIcon(data, image, slot);
        if (icon != null)
        {
            return icon;
        }

        // Fallback for cash/effect weapons whose iconRaw is a 1×1 placeholder:
        // render the first weapon part canvas instead of a grey square.
        icon = TryReadPartIcon(data, image);
        return icon ?? CreatePlaceholderIcon();
    }

    /// <summary>Try the standard info/iconRaw (and a few alternates) on one image.</summary>
    private static BitmapSource? TryReadInfoIcon(AvatarData data, WzNode image, EquipSlot slot)
    {
        string[] candidates = slot switch
        {
            EquipSlot.Hair => new[]
            {
                "default/hairOverHead", "default/hairOverHead_m", "default/hairOverHead_f",
                "default/hair", "default/hair_m", "default/hair_f",
                "default/backHair", "default/backHair_m", "default/backHair_f",
                "default/backHairBelowCap",
                "info/iconRaw", "info/icon",
            },
            EquipSlot.Face => new[]
            {
                "default/face", "default/face_m", "default/face_f",
                "default/backFace", "info/iconRaw", "info/icon",
            },
            _ => new[] { "info/iconRaw", "info/icon", "info/preview" },
        };

        foreach (string candidate in candidates)
        {
            WzNode? node = FindNodeByPath(image, candidate);
            if (node == null)
            {
                continue;
            }

            // Linked canvases (_outlink/_inlink) resolve to the real pixels.
            WzNode? canvasNode = WzLinkResolver.Resolve(node, path => data.FindNode(path));
            if (canvasNode?.Value is not WzCanvas canvas)
            {
                continue;
            }

            IReadOnlyList<WzPixelBuffer> frames = canvas.Frames;
            if (frames.Count == 0)
            {
                continue;
            }

            WzPixelBuffer first = frames[0];
            if (first.Width <= 1 && first.Height <= 1)
            {
                continue;
            }

            return ToBitmapSource(first);
        }

        return null;
    }

    /// <summary>
    /// Renders the first available weapon part canvas as an icon. Used when a
    /// cash weapon only stores a 1×1 info/iconRaw placeholder.
    /// </summary>
    private static BitmapSource? TryReadPartIcon(AvatarData data, WzNode image)
    {
        var groups = image.Children.Where(c => c.IsContainer).ToList();
        foreach (WzNode group in groups)
        {
            foreach (WzNode action in group.Children.Where(c => c.IsContainer))
            {
                WzNode? frame = action.Children.FirstOrDefault(c => c.IsContainer);
                WzNode? part = frame?.FindChild("weapon");
                if (part == null)
                {
                    continue;
                }

                WzNode? canvasNode = WzLinkResolver.Resolve(part, path => data.FindNode(path));
                if (canvasNode?.Value is not WzCanvas canvas || canvas.Frames.Count == 0)
                {
                    continue;
                }

                WzPixelBuffer pixels = canvas.Frames[0];
                if (pixels.Width <= 1 && pixels.Height <= 1)
                {
                    continue;
                }

                WzPixelBounds? bounds = pixels.FindOpaqueBounds();
                return ToBitmapSource(bounds is { } box ? pixels.Crop(box) : pixels);
            }
        }

        return null;
    }

    /// <summary>Builds the paired "_Canvas" image path for a character item.</summary>
    private static string CanvasMirrorPath(string imagePath)
    {
        int slash = imagePath.LastIndexOf('/');
        return slash < 0
            ? imagePath
            : imagePath[..slash] + "/_Canvas/" + imagePath[(slash + 1)..];
    }

    /// <summary>
    /// 32×32 空白占位图标（浅灰底 + 边框，冻结后可跨线程共享）。
    /// </summary>
    /// <remarks>
    /// 必须“可见”：光头发型没有任何像素资源，若用全透明图，列表行看起来是空的，
    /// 用户无法确认该发型是否存在。浅灰方块表示“该物品无外观资源但条目有效”。
    /// </remarks>
    private static BitmapSource CreatePlaceholderIcon()
    {
        const int size = 32;
        const int border = 2;
        byte[] pixels = new byte[size * size * 4];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int offset = (y * size + x) * 4;
                bool onBorder = x < border || y < border || x >= size - border || y >= size - border;
                byte gray = (byte)(onBorder ? 0xB0 : 0xE8);

                // BGRA 顺序
                pixels[offset] = gray;
                pixels[offset + 1] = gray;
                pixels[offset + 2] = gray;
                pixels[offset + 3] = 0xFF;
            }
        }

        WriteableBitmap bitmap = new(size, size, 96, 96, PixelFormats.Bgra32, null);
        bitmap.WritePixels(new Int32Rect(0, 0, size, size), pixels, size * 4, 0);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>
    /// 按相对路径查找节点；支持 <c>default/hairOverHead</c> 这类多段路径，不存在返回 <c>null</c>。
    /// </summary>
    private static WzNode? FindNodeByPath(WzNode root, string relativePath)
    {
        string[] segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        WzNode? current = root;
        foreach (string segment in segments)
        {
            current = current?.FindChild(segment);
            if (current == null)
            {
                return null;
            }
        }

        return current;
    }

    /// <summary>BGRA32 → WPF 位图。</summary>
    private static BitmapSource ToBitmapSource(WzPixelBuffer pixels)
    {
        WriteableBitmap bitmap = new(
            pixels.Width, pixels.Height, 96, 96, PixelFormats.Bgra32, null);
        bitmap.WritePixels(
            new Int32Rect(0, 0, pixels.Width, pixels.Height),
            pixels.Pixels,
            pixels.Stride,
            0);
        bitmap.Freeze();
        return bitmap;
    }
}
