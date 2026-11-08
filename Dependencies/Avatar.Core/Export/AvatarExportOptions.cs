namespace Avatar.Core.Export;

/// <summary>导出内容形态。</summary>
public enum AvatarExportLayout
{
    /// <summary>只导出逐帧 PNG。</summary>
    Frames,

    /// <summary>只导出雪碧图（所有帧拼成一张）。</summary>
    Sheet,

    /// <summary>逐帧 PNG 与雪碧图都导出。</summary>
    Both,
}

/// <summary>文件已存在时的处理策略。</summary>
public enum FileOverwriteMode
{
    /// <summary>覆盖（默认）。</summary>
    Overwrite,

    /// <summary>跳过已存在文件。</summary>
    Skip,

    /// <summary>已存在则报错。</summary>
    Fail,
}

/// <summary>
/// 导出选项。输出目录必填，其余都有默认值。
/// </summary>
/// <remarks>
/// 文件命名统一以“角色 ID”为前缀，便于批量导出后按角色归档：
/// <list type="bullet">
/// <item>帧：<c>{prefix}_{action}_{frame:00}.png</c></item>
/// <item>雪碧图：<c>{prefix}_{action}_sheet.png</c></item>
/// <item>结构信息：<c>{prefix}_{action}.json</c></item>
/// <item>角色档案：<c>{prefix}_profile.json</c></item>
/// </list>
/// 其中 <c>prefix</c> = <see cref="FileNamePrefix"/> 或 <see cref="Models.CharacterProfile.Id"/> 或 <c>avatar</c>。
/// </remarks>
public sealed class AvatarExportOptions
{
    /// <summary>输出目录（会自动创建）。</summary>
    public required string OutputDirectory { get; init; }

    /// <summary>文件名前缀；为 <c>null</c> 时使用角色 ID。</summary>
    public string? FileNamePrefix { get; init; }

    /// <summary>导出形态。</summary>
    public AvatarExportLayout Layout { get; init; } = AvatarExportLayout.Both;

    /// <summary>覆盖策略。</summary>
    public FileOverwriteMode Overwrite { get; init; } = FileOverwriteMode.Overwrite;

    /// <summary>是否写出动作结构 JSON（帧、部件、画布、锚点关系）。</summary>
    public bool WriteMeta { get; init; } = true;

    /// <summary>是否写出角色档案 JSON（可直接再导入还原）。</summary>
    public bool WriteProfile { get; init; }

    /// <summary>雪碧图每行帧数（0 表示单行）。</summary>
    public int SheetColumns { get; init; } = 8;

    /// <summary>雪碧图帧间距（像素）。</summary>
    public int SheetSpacing { get; init; }

    /// <summary>最近邻放大倍数（1 表示原始像素）。</summary>
    public int Scale { get; init; } = 1;

    /// <summary>批量导出时的最大并行度。</summary>
    public int MaxParallelism { get; init; } = Environment.ProcessorCount;
}
