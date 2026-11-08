using System.Text.Json;
using System.Text.Json.Serialization;
using Avatar.Core.Models;

namespace Avatar.Core.Export;

/// <summary>
/// 角色档案 JSON 读写：用于存档、跨进程传输与“渲染参数快照”。
/// </summary>
/// <remarks>
/// 序列化约定：
/// <list type="bullet">
/// <item>属性名小驼峰（<c>skinIndex</c>/<c>hairId</c>…），与前端/脚本习惯一致；</item>
/// <item>枚举写成字符串（<c>"Male"</c>/<c>"HueReplace"</c>），避免数字含义漂移；</item>
/// <item><c>null</c> 字段不写出，档案更紧凑；</item>
/// <item>反序列化做 <see cref="CharacterProfile.Normalize"/>，自动修正长袍/肤色/戒指等非法组合。</item>
/// </list>
/// </remarks>
public static class CharacterProfileJson
{
    /// <summary>默认序列化选项。</summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    /// <summary>序列化为 JSON 字符串。</summary>
    /// <param name="profile">角色档案。</param>
    /// <param name="indented">是否缩进（默认缩进，便于人工编辑）。</param>
    /// <returns>JSON 文本。</returns>
    public static string Serialize(CharacterProfile profile, bool indented = true)
    {
        ArgumentNullException.ThrowIfNull(profile);
        JsonSerializerOptions options = indented ? Options : CompactOptions;
        return JsonSerializer.Serialize(profile, options);
    }

    /// <summary>从 JSON 字符串解析（自动规范化）。</summary>
    /// <param name="json">JSON 文本。</param>
    /// <returns>角色档案。</returns>
    /// <exception cref="JsonException">格式错误。</exception>
    public static CharacterProfile Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        CharacterProfile? profile = JsonSerializer.Deserialize<CharacterProfile>(json, Options);
        return profile?.Normalize() ?? new CharacterProfile();
    }

    /// <summary>保存到文件（自动创建目录）。</summary>
    /// <param name="profile">角色档案。</param>
    /// <param name="filePath">目标文件路径。</param>
    public static void Save(CharacterProfile profile, string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(filePath, Serialize(profile));
    }

    /// <summary>从文件读取（自动规范化）。</summary>
    /// <param name="filePath">文件路径。</param>
    /// <returns>角色档案。</returns>
    public static CharacterProfile Load(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        return Parse(File.ReadAllText(filePath));
    }

    /// <summary>创建序列化选项。</summary>
    private static JsonSerializerOptions CreateOptions()
    {
        JsonSerializerOptions options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    /// <summary>紧凑输出选项（日志/网络传输用）。</summary>
    private static JsonSerializerOptions CompactOptions { get; } = CreateCompactOptions();

    /// <summary>创建紧凑选项。</summary>
    private static JsonSerializerOptions CreateCompactOptions()
    {
        JsonSerializerOptions options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
