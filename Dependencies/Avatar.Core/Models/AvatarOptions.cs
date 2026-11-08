namespace Avatar.Core.Models;

/// <summary>
/// 组装与渲染选项。所有字段都有合理默认值，调用方只改需要的项。
/// </summary>
public sealed class AvatarOptions
{
    /// <summary>默认选项（单例，只读使用）。</summary>
    public static AvatarOptions Default { get; } = new();

    /// <summary>需求（性别/职业/等级/属性）校验模式。</summary>
    public ValidationMode Validation { get; init; } = ValidationMode.Warn;

    /// <summary>性别策略。</summary>
    public GenderPolicy GenderPolicy { get; init; } = GenderPolicy.InferOnly;

    /// <summary>输出包围盒四周留白（像素）。</summary>
    public int Padding { get; init; } = 1;

    /// <summary>部件帧数不足时的取帧策略。</summary>
    public FrameFallbackMode FrameFallback { get; init; } = FrameFallbackMode.Clamp;

    /// <summary>是否应用染色。</summary>
    public bool ApplyDye { get; init; } = true;

    /// <summary>是否水平翻转（与 <see cref="CharacterProfile.FlipX"/> 取或）。</summary>
    public bool FlipX { get; init; }

    /// <summary>固定输出画布；为 <c>null</c> 时按部件并集自动计算。</summary>
    public AvatarCanvas? FixedCanvas { get; init; }

    /// <summary>是否记录每个部件的放置明细（<c>AvatarFrame.Parts</c> 始终记录，此项仅影响调试输出）。</summary>
    public bool CollectDebugAnchors { get; init; } = true;
}
