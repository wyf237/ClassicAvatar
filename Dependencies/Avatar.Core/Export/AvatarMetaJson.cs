using System.Text.Json;
using System.Text.Json.Serialization;
using Avatar.Core.Models;

namespace Avatar.Core.Export;

/// <summary>动作结构 JSON 的画布信息。</summary>
/// <param name="Width">画布宽度。</param>
/// <param name="Height">画布高度。</param>
/// <param name="Left">画布左上角在角色空间的 X。</param>
/// <param name="Top">画布左上角在角色空间的 Y。</param>
public sealed record AvatarMetaCanvas(int Width, int Height, int Left, int Top);

/// <summary>动作结构 JSON 中的单个部件。</summary>
/// <param name="Slot">槽位。</param>
/// <param name="ItemId">物品 ID。</param>
/// <param name="Part">WZ 部件名。</param>
/// <param name="Layer">WZ 图层名（z）。</param>
/// <param name="X">绘制位置 X（角色空间，已含翻转）。</param>
/// <param name="Y">绘制位置 Y（角色空间）。</param>
/// <param name="Width">像素宽。</param>
/// <param name="Height">像素高。</param>
/// <param name="Source">真实像素节点路径（解析链接后）。</param>
/// <param name="Metadata">携带 origin/map/z 的元数据节点路径。</param>
public sealed record AvatarMetaPart(
    string Slot,
    int ItemId,
    string Part,
    string Layer,
    int X,
    int Y,
    int Width,
    int Height,
    string Source,
    string Metadata);

/// <summary>动作结构 JSON 中的单帧。</summary>
/// <param name="Index">帧下标。</param>
/// <param name="DelayMs">帧时长。</param>
/// <param name="Overlay">是否为局部覆盖动作帧。</param>
/// <param name="BodyFaceIndex">身体帧表情索引。</param>
/// <param name="Parts">部件列表（按绘制顺序）。</param>
/// <param name="Notes">该帧的渲染提示。</param>
public sealed record AvatarMetaFrame(
    int Index,
    int DelayMs,
    bool Overlay,
    int BodyFaceIndex,
    IReadOnlyList<AvatarMetaPart> Parts,
    IReadOnlyList<string> Notes);

/// <summary>
/// 动作结构 JSON 文档：记录“img 结构与对应关系”，可用于外部工具复现同一张图。
/// </summary>
/// <param name="SchemaVersion">结构版本。</param>
/// <param name="Id">角色 ID。</param>
/// <param name="RequestedAction">请求动作名。</param>
/// <param name="Action">实际动作名。</param>
/// <param name="TotalDelayMs">总时长。</param>
/// <param name="FlipX">是否水平翻转。</param>
/// <param name="Canvas">画布。</param>
/// <param name="Frames">帧列表。</param>
public sealed record AvatarMetaDocument(
    int SchemaVersion,
    string? Id,
    string RequestedAction,
    string Action,
    int TotalDelayMs,
    bool FlipX,
    AvatarMetaCanvas Canvas,
    IReadOnlyList<AvatarMetaFrame> Frames)
{
    /// <summary>标准序列化选项。</summary>
    public static JsonSerializerOptions JsonOptions { get; } = CreateOptions();

    /// <summary>从渲染序列创建文档。</summary>
    /// <param name="build">组装结果。</param>
    /// <param name="sequence">渲染序列。</param>
    /// <returns>结构文档。</returns>
    public static AvatarMetaDocument Create(AvatarBuild build, AvatarSequence sequence)
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(sequence);

        AvatarCanvas canvas = sequence.Frames.Count > 0
            ? sequence.Frames[0].Canvas
            : new AvatarCanvas(0, 0, 0, 0);

        List<AvatarMetaFrame> frames = new(sequence.Frames.Count);
        foreach (AvatarFrame frame in sequence.Frames)
        {
            List<AvatarMetaPart> parts = new(frame.Parts.Count);
            foreach (PlacedPart part in frame.Parts)
            {
                parts.Add(new AvatarMetaPart(
                    part.Slot.ToString(),
                    part.ItemId,
                    part.PartName,
                    part.Layer,
                    part.X,
                    part.Y,
                    part.Width,
                    part.Height,
                    part.SourcePath,
                    part.MetadataPath));
            }

            frames.Add(new AvatarMetaFrame(
                frame.FrameIndex,
                frame.DelayMs,
                frame.IsOverlayAction,
                frame.BodyFaceIndex,
                parts,
                frame.Notes));
        }

        return new AvatarMetaDocument(
            1,
            build.Profile.Id,
            sequence.RequestedAction,
            sequence.Action,
            sequence.TotalDelayMs,
            build.FlipX,
            new AvatarMetaCanvas(canvas.Width, canvas.Height, canvas.Left, canvas.Top),
            frames);
    }

    /// <summary>序列化为 JSON 文本。</summary>
    /// <returns>JSON 文本。</returns>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>保存到文件（自动创建目录）。</summary>
    /// <param name="filePath">目标路径。</param>
    public void Save(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(filePath, this.ToJson());
    }

    private static JsonSerializerOptions CreateOptions()
    {
        JsonSerializerOptions options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
