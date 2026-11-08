using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Avatar.Core.Assets;
using Avatar.Core.Models;
using Avatar.Core.Rendering;
using WzReader.Core.Codecs;
using WzReader.Export.Imaging;

namespace Avatar.Core.Export;

/// <summary>套装文件读取结果（内容已全部读入内存，与 WZ 数据无关）。</summary>
public sealed class AvatarPackContent
{
    /// <summary>创建读取结果。</summary>
    internal AvatarPackContent(
        CharacterProfile profile,
        IReadOnlyList<AvatarPackItem> items,
        byte[]? previewPng,
        AvatarPackManifest? manifest,
        IReadOnlyList<string> warnings)
    {
        this.Profile = profile;
        this.Items = items;
        this.PreviewPng = previewPng;
        this.Manifest = manifest;
        this.Warnings = warnings;
    }

    /// <summary>角色档案（还原的唯一权威来源，已 Normalize）。</summary>
    public CharacterProfile Profile { get; }

    /// <summary>槽位搭配清单（展示用，含本地化名称）。</summary>
    public IReadOnlyList<AvatarPackItem> Items { get; }

    /// <summary>形象预览 PNG 字节；未包含或校验失败为 <c>null</c>。</summary>
    public byte[]? PreviewPng { get; }

    /// <summary>清单；旧文件缺失时为 <c>null</c>。</summary>
    public AvatarPackManifest? Manifest { get; }

    /// <summary>读取期提示（校验失败、条目缺失等，不致命）。</summary>
    public IReadOnlyList<string> Warnings { get; }
}

/// <summary>
/// .avatar 套装文件：把角色形象图与物品搭配数据打包为单个 ZIP 容器，可独立读取还原。
/// </summary>
/// <remarks>
/// <para>
/// 文件结构（ZIP，Deflate 压缩）：
/// <c>manifest.json</c>（清单/校验）、<c>profile.json</c>（还原权威）、
/// <c>items.json</c>（展示清单）、<c>images/*.png</c>（预览/雪碧图/缩略图）。
/// </para>
/// <para>
/// 读取分两级：有 WZ 数据时读 <c>profile.json</c> → <see cref="Rules.AvatarAssembler.Build"/>
/// 完整还原；无数据时读 <c>images/preview.png</c> 与 <c>items.json</c> 即可展示外观与清单。
/// </para>
/// <para>
/// 写入采用临时文件 + 原子替换，避免中断损坏原档。线程安全：全部静态方法，无共享状态。
/// </para>
/// </remarks>
public static class AvatarPack
{
    /// <summary>格式标识。</summary>
    public const string FormatName = "avatar-pack";

    /// <summary>当前格式版本。</summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>默认文件扩展名（含点）。</summary>
    public const string DefaultExtension = ".avatar";

    private const string ManifestPath = "manifest.json";
    private const string ProfilePath = "profile.json";
    private const string ItemsPath = "items.json";
    private const string PreviewPath = "images/preview.png";
    private const string ThumbnailPath = "images/thumb.png";
    private const int ThumbnailSize = 96;

    /// <summary>
    /// 打包角色为单个 .avatar 文件（渲染图片 + 序列化档案 + 写 ZIP）。
    /// </summary>
    /// <param name="build">组装结果（渲染来源，须已打开 WZ 数据）。</param>
    /// <param name="filePath">目标文件路径（建议 .avatar 扩展名）。</param>
    /// <param name="options">打包选项；<c>null</c> 使用默认。</param>
    /// <returns>写入的清单（含各条目校验信息）。</returns>
    public static AvatarPackManifest Save(AvatarBuild build, string filePath, AvatarPackOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        options ??= new AvatarPackOptions();
        CharacterProfile profile = build.Profile.Normalize();

        // 1) 先渲染/序列化全部内容（内存完成，失败不落半成品文件）。
        List<(string Path, string Kind, byte[] Bytes, AvatarPackEntry Meta)> entries = new();

        byte[] profileBytes = ToUtf8(CharacterProfileJson.Serialize(profile));
        entries.Add((ProfilePath, "profile", profileBytes, new AvatarPackEntry(ProfilePath, "profile", string.Empty, 0)));

        byte[] itemsBytes = ToUtf8(SerializeItems(BuildItems(build)));
        entries.Add((ItemsPath, "items", itemsBytes, new AvatarPackEntry(ItemsPath, "items", string.Empty, 0)));

        if (options.IncludePreview)
        {
            AvatarFrame preview = AvatarRenderer.RenderFrame(build, options.PreviewAction, 0);
            WzPixelBuffer pixels = options.PreviewScale <= 1
                ? preview.Pixels
                : PixelCompositor.Scale(preview.Pixels, options.PreviewScale);

            entries.Add((PreviewPath, "preview", PngEncoder.Encode(pixels),
                new AvatarPackEntry(PreviewPath, "preview", string.Empty, 0)
                {
                    Action = preview.Action,
                    Frame = preview.FrameIndex,
                }));

            if (options.IncludeThumbnail)
            {
                WzPixelBuffer thumb = CreateThumbnail(preview.Pixels, ThumbnailSize);
                entries.Add((ThumbnailPath, "thumbnail", PngEncoder.Encode(thumb),
                    new AvatarPackEntry(ThumbnailPath, "thumbnail", string.Empty, 0)));
            }
        }

        IReadOnlyList<string> sheetActions = options.SheetActions ?? new[] { "stand1", "walk1" };
        foreach (string action in sheetActions)
        {
            if (string.IsNullOrWhiteSpace(action))
            {
                continue;
            }

            AvatarSequence sequence = AvatarRenderer.RenderSequence(build, action);
            WzPixelBuffer sheet = SheetBuilder.Build(sequence.Frames, options.SheetColumns);
            string path = $"images/{Sanitize(sequence.Action)}_sheet.png";

            entries.Add((path, "spritesheet", PngEncoder.Encode(sheet),
                new AvatarPackEntry(path, "spritesheet", string.Empty, 0)
                {
                    Action = sequence.Action,
                    Frames = sequence.Frames.Count,
                    DelayMs = sequence.TotalDelayMs,
                }));
        }

        // 2) 计算校验并组装清单。
        AvatarPackManifest manifest = new()
        {
            App = options.App,
            AppVersion = options.AppVersion,
            CreatedAt = DateTimeOffset.Now,
            ProfileId = profile.Id,
            Contents = entries
                .Select(entry => entry.Meta with { Sha256 = Sha256Hex(entry.Bytes), Size = entry.Bytes.Length })
                .ToArray(),
        };

        // 3) 原子写入：临时文件 + 替换。
        string? directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string tempPath = filePath + ".tmp";
        try
        {
            using (FileStream stream = new(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (ZipArchive zip = new(stream, ZipArchiveMode.Create))
            {
                foreach ((string path, _, byte[] bytes, _) in entries)
                {
                    WriteEntry(zip, path, bytes, options.Compression);
                }

                WriteEntry(zip, ManifestPath, ToUtf8(JsonSerializer.Serialize(manifest, AvatarPackManifest.JsonOptions)), options.Compression);
            }

            File.Move(tempPath, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }

        return manifest;
    }

    /// <summary>
    /// 读取 .avatar 套装文件（档案 + 清单 + 预览图，一次性读入内存）。
    /// </summary>
    /// <param name="filePath">文件路径。</param>
    /// <returns>读取结果；profile.json 缺失或损坏时抛异常。</returns>
    /// <exception cref="InvalidDataException">不是有效的套装文件。</exception>
    public static AvatarPackContent Load(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        using FileStream stream = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using ZipArchive zip = new(stream, ZipArchiveMode.Read);

        List<string> warnings = new();
        Dictionary<string, AvatarPackEntry> manifestEntries = new(StringComparer.OrdinalIgnoreCase);

        AvatarPackManifest? manifest = ReadJsonEntry<AvatarPackManifest>(zip, ManifestPath, AvatarPackManifest.JsonOptions);
        if (manifest != null)
        {
            foreach (AvatarPackEntry entry in manifest.Contents)
            {
                manifestEntries[entry.Path] = entry;
            }
        }
        else
        {
            warnings.Add("缺少 manifest.json，跳过完整性校验。");
        }

        CharacterProfile profile = ReadProfileCore(zip, manifestEntries, warnings);
        IReadOnlyList<AvatarPackItem> items = ReadItems(zip, manifestEntries, warnings);
        byte[]? preview = ReadVerifiedEntry(zip, PreviewPath, manifestEntries, warnings);

        return new AvatarPackContent(profile, items, preview, manifest, warnings);
    }

    /// <summary>快速通道：只读取角色档案（不解压图片）。</summary>
    /// <param name="filePath">文件路径。</param>
    /// <returns>角色档案（已 Normalize）。</returns>
    /// <exception cref="InvalidDataException">不是有效的套装文件。</exception>
    public static CharacterProfile LoadProfile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        using FileStream stream = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using ZipArchive zip = new(stream, ZipArchiveMode.Read);
        return ReadProfileCore(zip, manifestEntries: null, warnings: null);
    }

    /// <summary>快速通道：只读取形象预览 PNG 字节（用于文件浏览器缩略图）。</summary>
    /// <param name="filePath">文件路径。</param>
    /// <returns>PNG 字节；未包含返回 <c>null</c>。</returns>
    public static byte[]? ReadPreview(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        using FileStream stream = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using ZipArchive zip = new(stream, ZipArchiveMode.Read);
        return ReadEntry(zip, PreviewPath);
    }

    /// <summary>
    /// 校验套装文件完整性（manifest 存在性与各条目 SHA-256）。
    /// </summary>
    /// <param name="filePath">文件路径。</param>
    /// <returns>诊断列表；为空表示完全有效。</returns>
    public static IReadOnlyList<string> Validate(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        List<string> diagnostics = new();
        try
        {
            using FileStream stream = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using ZipArchive zip = new(stream, ZipArchiveMode.Read);

            AvatarPackManifest? manifest = ReadJsonEntry<AvatarPackManifest>(zip, ManifestPath, AvatarPackManifest.JsonOptions);
            if (manifest == null)
            {
                diagnostics.Add("缺少或无法解析 manifest.json。");
                return diagnostics;
            }

            if (!string.Equals(manifest.Format, FormatName, StringComparison.Ordinal))
            {
                diagnostics.Add($"格式标识不符：{manifest.Format}（期望 {FormatName}）。");
            }

            foreach (AvatarPackEntry entry in manifest.Contents)
            {
                byte[]? bytes = ReadEntry(zip, entry.Path);
                if (bytes == null)
                {
                    diagnostics.Add($"条目缺失：{entry.Path}");
                    continue;
                }

                if (bytes.Length != entry.Size)
                {
                    diagnostics.Add($"大小不符：{entry.Path}（{bytes.Length} != {entry.Size}）");
                }

                if (!string.Equals(Sha256Hex(bytes), entry.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics.Add($"校验失败：{entry.Path}");
                }
            }
        }
        catch (InvalidDataException ex)
        {
            diagnostics.Add($"无法读取 ZIP 容器：{ex.Message}");
        }

        return diagnostics;
    }

    /// <summary>按档案生成展示用槽位清单（含本地化名称）。</summary>
    private static IReadOnlyList<AvatarPackItem> BuildItems(AvatarBuild build)
    {
        CharacterProfile profile = build.Profile;
        AssetNameResolver names = build.Data.Names;
        List<AvatarPackItem> items = new();

        void Add(EquipSlot slot, int? itemId)
        {
            items.Add(new AvatarPackItem(
                slot.ToString(),
                itemId,
                itemId is int id ? names.TryGetName(id) : null));
        }

        Add(EquipSlot.Hair, profile.HairId);
        Add(EquipSlot.Face, profile.FaceId);
        Add(EquipSlot.Cap, profile.CapId);
        Add(EquipSlot.Coat, profile.CoatId);
        Add(EquipSlot.Longcoat, profile.LongcoatId);
        Add(EquipSlot.Pants, profile.PantsId);
        Add(EquipSlot.Shoes, profile.ShoesId);
        Add(EquipSlot.Glove, profile.GloveId);
        Add(EquipSlot.Shield, profile.ShieldId);
        Add(EquipSlot.Cape, profile.CapeId);
        Add(EquipSlot.Weapon, profile.WeaponId);
        Add(EquipSlot.FaceAccessory, profile.FaceAccessoryId);
        Add(EquipSlot.EyeAccessory, profile.EyeAccessoryId);
        Add(EquipSlot.EarAccessory, profile.EarAccessoryId);

        if (profile.RingIds.Count > 0)
        {
            foreach (int ringId in profile.RingIds)
            {
                items.Add(new AvatarPackItem(EquipSlot.Ring.ToString(), ringId, names.TryGetName(ringId)));
            }
        }
        else
        {
            items.Add(new AvatarPackItem(EquipSlot.Ring.ToString(), null, null));
        }

        return items;
    }

    /// <summary>读取并校验 profile.json（必需，失败抛异常）。</summary>
    private static CharacterProfile ReadProfileCore(
        ZipArchive zip,
        IReadOnlyDictionary<string, AvatarPackEntry>? manifestEntries,
        List<string>? warnings)
    {
        byte[]? bytes = ReadVerifiedEntry(zip, ProfilePath, manifestEntries, warnings);
        if (bytes == null)
        {
            throw new InvalidDataException("套装文件缺少 profile.json，无法还原角色档案。");
        }

        try
        {
            return CharacterProfileJson.Parse(System.Text.Encoding.UTF8.GetString(bytes));
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"profile.json 格式错误：{ex.Message}", ex);
        }
    }

    /// <summary>读取 items.json（可选，失败降级为空清单）。</summary>
    private static IReadOnlyList<AvatarPackItem> ReadItems(
        ZipArchive zip,
        IReadOnlyDictionary<string, AvatarPackEntry> manifestEntries,
        List<string> warnings)
    {
        byte[]? bytes = ReadVerifiedEntry(zip, ItemsPath, manifestEntries, warnings);
        if (bytes == null)
        {
            return Array.Empty<AvatarPackItem>();
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes);
            if (document.RootElement.TryGetProperty("slots", out JsonElement slots)
                && slots.ValueKind == JsonValueKind.Array)
            {
                return JsonSerializer.Deserialize<List<AvatarPackItem>>(slots.GetRawText(), AvatarPackManifest.JsonOptions)
                       ?? new List<AvatarPackItem>();
            }

            // 容忍顶层直接是数组的形态。
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                return JsonSerializer.Deserialize<List<AvatarPackItem>>(bytes, AvatarPackManifest.JsonOptions)
                       ?? new List<AvatarPackItem>();
            }
        }
        catch (JsonException ex)
        {
            warnings.Add($"items.json 解析失败：{ex.Message}");
        }

        return Array.Empty<AvatarPackItem>();
    }

    /// <summary>读取条目并按 manifest 校验（无 manifest 或条目缺失时降级）。</summary>
    private static byte[]? ReadVerifiedEntry(
        ZipArchive zip,
        string path,
        IReadOnlyDictionary<string, AvatarPackEntry>? manifestEntries,
        List<string>? warnings)
    {
        byte[]? bytes = ReadEntry(zip, path);
        if (bytes == null)
        {
            return null;
        }

        if (manifestEntries != null
            && manifestEntries.TryGetValue(path, out AvatarPackEntry? meta)
            && !string.Equals(Sha256Hex(bytes), meta.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            warnings?.Add($"校验失败，已忽略条目：{path}");
            return null;
        }

        return bytes;
    }

    /// <summary>读取条目字节；不存在返回 <c>null</c>。</summary>
    private static byte[]? ReadEntry(ZipArchive zip, string path)
    {
        ZipArchiveEntry? entry = zip.GetEntry(path);
        if (entry == null)
        {
            return null;
        }

        using Stream stream = entry.Open();
        using MemoryStream memory = new();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    /// <summary>读取 JSON 条目；缺失或解析失败返回 <c>null</c>。</summary>
    private static T? ReadJsonEntry<T>(ZipArchive zip, string path, JsonSerializerOptions options) where T : class
    {
        byte[]? bytes = ReadEntry(zip, path);
        if (bytes == null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(bytes, options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>写入一个 ZIP 条目。</summary>
    private static void WriteEntry(ZipArchive zip, string path, byte[] bytes, CompressionLevel compression)
    {
        ZipArchiveEntry entry = zip.CreateEntry(path, compression);
        using Stream stream = entry.Open();
        stream.Write(bytes, 0, bytes.Length);
    }

    /// <summary>生成 items.json 文本。</summary>
    private static string SerializeItems(IReadOnlyList<AvatarPackItem> items)
    {
        using MemoryStream memory = new();
        using (Utf8JsonWriter writer = new(memory, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("slots");
            JsonSerializer.Serialize(writer, items, AvatarPackManifest.JsonOptions);
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(memory.ToArray());
    }

    /// <summary>最近邻重采样生成缩略图（等比缩放，最长边对齐目标尺寸）。</summary>
    private static WzPixelBuffer CreateThumbnail(WzPixelBuffer source, int targetSize)
    {
        double scale = Math.Min((double)targetSize / source.Width, (double)targetSize / source.Height);
        int width = Math.Max(1, (int)Math.Round(source.Width * scale));
        int height = Math.Max(1, (int)Math.Round(source.Height * scale));

        WzPixelBuffer output = PixelCompositor.CreateCanvas(width, height);
        for (int y = 0; y < height; y++)
        {
            int sourceY = Math.Min(source.Height - 1, y * source.Height / height);
            int targetRow = y * output.Stride;
            int sourceRow = sourceY * source.Stride;

            for (int x = 0; x < width; x++)
            {
                int sourceX = Math.Min(source.Width - 1, x * source.Width / width);
                int sourceOffset = sourceRow + (sourceX * 4);
                int targetOffset = targetRow + (x * 4);

                output.Pixels[targetOffset] = source.Pixels[sourceOffset];
                output.Pixels[targetOffset + 1] = source.Pixels[sourceOffset + 1];
                output.Pixels[targetOffset + 2] = source.Pixels[sourceOffset + 2];
                output.Pixels[targetOffset + 3] = source.Pixels[sourceOffset + 3];
            }
        }

        return output;
    }

    /// <summary>SHA-256 小写十六进制。</summary>
    private static string Sha256Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>UTF-8 编码。</summary>
    private static byte[] ToUtf8(string text) => System.Text.Encoding.UTF8.GetBytes(text);

    /// <summary>清洗文件名片段。</summary>
    private static string Sanitize(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        char[] buffer = value.Trim().ToCharArray();
        for (int index = 0; index < buffer.Length; index++)
        {
            if (Array.IndexOf(invalid, buffer[index]) >= 0 || buffer[index] == ' ')
            {
                buffer[index] = '_';
            }
        }

        return new string(buffer);
    }
}
