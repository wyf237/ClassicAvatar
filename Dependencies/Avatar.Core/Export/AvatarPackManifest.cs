using System.Text.Json;
using System.Text.Json.Serialization;

namespace Avatar.Core.Export;

/// <summary>套装文件（.avatar）中单个条目的清单记录。</summary>
/// <param name="Path">ZIP 内相对路径。</param>
/// <param name="Kind">条目种类（profile/items/preview/spritesheet/thumbnail）。</param>
/// <param name="Sha256">内容 SHA-256（小写十六进制），用于损坏检测。</param>
/// <param name="Size">内容字节数。</param>
public sealed record AvatarPackEntry(string Path, string Kind, string Sha256, long Size)
{
    /// <summary>动作名（图片条目可填）。</summary>
    public string? Action { get; init; }

    /// <summary>帧下标（preview 条目可填）。</summary>
    public int? Frame { get; init; }

    /// <summary>帧数（spritesheet 条目可填）。</summary>
    public int? Frames { get; init; }

    /// <summary>动作总时长（毫秒，spritesheet 条目可填）。</summary>
    public int? DelayMs { get; init; }
}

/// <summary>
/// 套装文件清单（manifest.json）：格式版本、生成器信息与全部条目校验。
/// </summary>
/// <remarks>
/// 读取方规则：<see cref="FormatVersion"/> 高于自身支持版本时，忽略未知字段并尽量读取
/// 已知条目；JSON 反序列化容忍未知属性，保证旧读取方能打开新文件。
/// </remarks>
public sealed record AvatarPackManifest
{
    /// <summary>格式标识，固定为 <c>avatar-pack</c>。</summary>
    public string Format { get; init; } = AvatarPack.FormatName;

    /// <summary>格式版本（当前为 1）。</summary>
    public int FormatVersion { get; init; } = AvatarPack.CurrentFormatVersion;

    /// <summary>生成程序名。</summary>
    public string? App { get; init; }

    /// <summary>生成程序版本。</summary>
    public string? AppVersion { get; init; }

    /// <summary>生成时间。</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;

    /// <summary>角色 ID（来自 CharacterProfile.Id）。</summary>
    public string? ProfileId { get; init; }

    /// <summary>条目清单（manifest.json 自身不在其中）。</summary>
    public IReadOnlyList<AvatarPackEntry> Contents { get; init; } = Array.Empty<AvatarPackEntry>();

    /// <summary>序列化选项（小驼峰、忽略 null、容忍未知属性）。</summary>
    internal static JsonSerializerOptions JsonOptions { get; } = CreateOptions();

    /// <summary>创建序列化选项。</summary>
    private static JsonSerializerOptions CreateOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

/// <summary>套装文件 items.json 中的一个槽位条目（纯展示用途，不参与还原）。</summary>
/// <param name="Slot">槽位名（EquipSlot 枚举名）。</param>
/// <param name="ItemId">物品 ID；该槽位为空为 <c>null</c>。</param>
/// <param name="Name">本地化名称（来自 AssetNameResolver）；未索引到为 <c>null</c>。</param>
public sealed record AvatarPackItem(string Slot, int? ItemId, string? Name);
