using Avatar.Core.Assets;
using Avatar.Core.Layout;
using Avatar.Core.Models;
using Avatar.Core.Rules;
using WzReader.Core.Codecs;

namespace Avatar.Core.Rendering;

/// <summary>
/// Avatar 渲染器：把 <see cref="AvatarBuild"/> 组装结果渲染为位图帧/动作序列。
/// </summary>
/// <remarks>
/// 渲染是纯函数式的：输入 <see cref="AvatarBuild"/>（不可变）+ 动作 + 帧号，输出新的 <see cref="AvatarFrame"/>。
/// <list type="number">
/// <item><see cref="FramePartCollector"/> 解析部件并完成锚点定位（角色空间坐标）；</item>
/// <item>本类统计所有部件的并集包围盒，决定输出画布；</item>
/// <item><see cref="PixelCompositor"/> 按已排好的 Z 序逐部件叠加像素。</item>
/// </list>
/// 动画序列（<see cref="RenderSequence"/>）会为所有帧共用同一个画布，保证播放时不抖动。
/// </remarks>
public static class AvatarRenderer
{
    /// <summary>局部覆盖动作中允许叠加的槽位（眨眼时只覆盖脸与眼饰，不重画头/发/身体）。</summary>
    private static readonly HashSet<EquipSlot> OverlaySlots = new()
    {
        EquipSlot.Face,
        EquipSlot.FaceAccessory,
        EquipSlot.EyeAccessory,
        EquipSlot.EarAccessory,
    };

    /// <summary>空锚点表（关闭调试锚点时复用）。</summary>
    private static readonly IReadOnlyDictionary<string, PointI> EmptyAnchors =
        new Dictionary<string, PointI>();

    /// <summary>
    /// 渲染单帧。
    /// </summary>
    /// <param name="build">组装结果。</param>
    /// <param name="action">动作名（支持 <c>stand</c>/<c>walk</c>/<c>attack</c> 别名）；<c>null</c> 使用档案默认动作。</param>
    /// <param name="frameIndex">帧下标（0 开始）。</param>
    /// <param name="solver">自定义定位求解器（通常为 <c>null</c>）。</param>
    /// <returns>渲染帧。</returns>
    public static AvatarFrame RenderFrame(
        AvatarBuild build,
        string? action = null,
        int frameIndex = 0,
        IAnchorPlacementSolver? solver = null)
    {
        ArgumentNullException.ThrowIfNull(build);

        ActionResolution resolution = ActionResolver.Resolve(build, action);
        CollectedFrame collected = FramePartCollector.Collect(build, resolution.Action, frameIndex, solver);
        AvatarCanvas canvas = build.Options.FixedCanvas ?? ComputeCanvas(build, new[] { collected });
        return Compose(build, collected, resolution, canvas);
    }

    /// <summary>
    /// 渲染整段动作序列（所有帧共用同一画布）。
    /// </summary>
    /// <param name="build">组装结果。</param>
    /// <param name="action">动作名（支持别名）；<c>null</c> 使用档案默认动作。</param>
    /// <param name="solver">自定义定位求解器（通常为 <c>null</c>）。</param>
    /// <returns>动作序列；动作不存在时也会返回“回退后”的序列。</returns>
    public static AvatarSequence RenderSequence(
        AvatarBuild build,
        string? action = null,
        IAnchorPlacementSolver? solver = null)
    {
        ArgumentNullException.ThrowIfNull(build);

        ActionResolution resolution = ActionResolver.Resolve(build, action);

        int frameCount = build.Items.Body?.Descriptor.GetFrameCount(resolution.Action) ?? 0;
        if (frameCount <= 0)
        {
            frameCount = 1;
        }

        List<CollectedFrame> collected = new(frameCount);
        for (int index = 0; index < frameCount; index++)
        {
            collected.Add(FramePartCollector.Collect(build, resolution.Action, index, solver));
        }

        AvatarCanvas canvas = build.Options.FixedCanvas ?? ComputeCanvas(build, collected);

        List<AvatarFrame> frames = new(collected.Count);
        foreach (CollectedFrame frame in collected)
        {
            frames.Add(Compose(build, frame, resolution, canvas));
        }

        return new AvatarSequence(resolution.Requested, resolution.Action, frames);
    }

    /// <summary>
    /// 在已渲染帧上叠加“局部覆盖动作”（典型用法：闭眼 <c>blink</c>）。
    /// </summary>
    /// <param name="build">组装结果。</param>
    /// <param name="baseFrame">底图帧（如 <c>stand1</c> 的某一帧）。</param>
    /// <param name="action">覆盖动作名（如 <c>blink</c>）。</param>
    /// <param name="frameIndex">覆盖动作的帧下标。</param>
    /// <param name="solver">自定义定位求解器（通常为 <c>null</c>）。</param>
    /// <returns>叠加后的新帧（画布与底图一致）。</returns>
    /// <remarks>
    /// 只叠加脸与眼/耳饰槽位的部件，避免把“闭眼头部贴图”覆盖到头发之上。
    /// 定位仍走同一套锚点框架，因此与底图天然对齐。
    /// </remarks>
    public static AvatarFrame RenderOverlay(
        AvatarBuild build,
        AvatarFrame baseFrame,
        string action,
        int frameIndex = 0,
        IAnchorPlacementSolver? solver = null)
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(baseFrame);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);

        ActionResolution resolution = ActionResolver.Resolve(build, action);
        CollectedFrame collected = FramePartCollector.Collect(build, resolution.Action, frameIndex, solver);

        AvatarCanvas canvas = baseFrame.Canvas;
        WzPixelBuffer pixels = PixelCompositor.Clone(baseFrame.Pixels);
        List<string> notes = new(collected.Notes);
        if (resolution.IsFallback && resolution.Reason != null)
        {
            notes.Add(resolution.Reason);
        }

        List<PlacedPart> placed = new();
        foreach (CollectedPart part in collected.Parts)
        {
            if (!OverlaySlots.Contains(part.Item.Slot))
            {
                continue;
            }

            DrawPart(build, part, pixels, canvas, placed, notes);
        }

        return new AvatarFrame(
            resolution.Requested,
            collected.Action,
            collected.FrameIndex,
            baseFrame.DelayMs,
            collected.BodyFaceIndex,
            true,
            pixels,
            canvas,
            placed,
            build.Options.CollectDebugAnchors ? MirrorAnchors(collected.Anchors, build.FlipX) : EmptyAnchors,
            notes);
    }

    /// <summary>
    /// 按已采集的部件合成一帧。
    /// </summary>
    private static AvatarFrame Compose(
        AvatarBuild build,
        CollectedFrame collected,
        ActionResolution resolution,
        AvatarCanvas canvas)
    {
        WzPixelBuffer pixels = PixelCompositor.CreateCanvas(canvas.Width, canvas.Height);
        List<PlacedPart> placed = new(collected.Parts.Count);
        List<string> notes = new(collected.Notes);

        if (resolution.IsFallback && resolution.Reason != null)
        {
            notes.Insert(0, resolution.Reason);
        }

        foreach (CollectedPart part in collected.Parts)
        {
            DrawPart(build, part, pixels, canvas, placed, notes);
        }

        return new AvatarFrame(
            resolution.Requested,
            collected.Action,
            collected.FrameIndex,
            collected.DelayMs,
            collected.BodyFaceIndex,
            collected.IsOverlayAction,
            pixels,
            canvas,
            placed,
            build.Options.CollectDebugAnchors ? MirrorAnchors(collected.Anchors, build.FlipX) : EmptyAnchors,
            notes);
    }

    /// <summary>绘制单个部件（含染色取帧、翻转、记录放置明细）。</summary>
    private static void DrawPart(
        AvatarBuild build,
        CollectedPart part,
        WzPixelBuffer destination,
        AvatarCanvas canvas,
        List<PlacedPart> placed,
        List<string> notes)
    {
        DyeSpec? dye = build.Options.ApplyDye ? part.Item.Dye : null;

        WzPixelBuffer? source = build.Data.GetPartFrame(
            part.Part,
            part.FrameIndex,
            build.Options.FrameFallback,
            dye,
            out string? note);

        if (!string.IsNullOrEmpty(note))
        {
            notes.Add(note);
        }

        if (source == null)
        {
            return;
        }

        int width = source.Width;
        int height = source.Height;
        int drawX = DrawLeft(build.FlipX, part.TopLeft.X, width);
        int drawY = part.TopLeft.Y;

        PixelCompositor.Draw(destination, source, drawX - canvas.Left, drawY - canvas.Top, build.FlipX);

        placed.Add(new PlacedPart(
            part.Item.ItemId,
            part.Item.Slot,
            part.Part.Name,
            part.Part.Layer,
            part.Part.Group,
            drawX,
            drawY,
            width,
            height,
            part.Part.CanvasNode.Path,
            part.Part.IsBack)
        {
            MetadataPath = part.Part.MetadataNode.Path,
        });
    }

    /// <summary>
    /// 计算一组帧的并集包围盒（角色空间），并加上留白。
    /// </summary>
    private static AvatarCanvas ComputeCanvas(AvatarBuild build, IEnumerable<CollectedFrame> frames)
    {
        int minX = int.MaxValue;
        int minY = int.MaxValue;
        int maxX = int.MinValue;
        int maxY = int.MinValue;
        bool any = false;

        foreach (CollectedFrame frame in frames)
        {
            foreach (CollectedPart part in frame.Parts)
            {
                int width = part.Part.Width;
                int height = part.Part.Height;
                if (width <= 0 || height <= 0)
                {
                    continue;
                }

                int left = DrawLeft(build.FlipX, part.TopLeft.X, width);
                int top = part.TopLeft.Y;

                minX = Math.Min(minX, left);
                minY = Math.Min(minY, top);
                maxX = Math.Max(maxX, left + width - 1);
                maxY = Math.Max(maxY, top + height - 1);
                any = true;
            }
        }

        if (!any)
        {
            // 没有任何可渲染部件时不输出 0 尺寸画布，统一退化为 1×1 透明帧。
            return new AvatarCanvas(1, 1, 0, 0);
        }

        int padding = Math.Max(0, build.Options.Padding);
        return new AvatarCanvas(
            maxX - minX + 1 + (padding * 2),
            maxY - minY + 1 + (padding * 2),
            minX - padding,
            minY - padding);
    }

    /// <summary>根据是否翻转计算绘制左上角 X（角色空间）。</summary>
    private static int DrawLeft(bool flipX, int normalLeft, int width) =>
        flipX ? PixelCompositor.MirrorLeft(normalLeft, width) : normalLeft;

    /// <summary>镜像锚点表（翻转时 X 取负）。</summary>
    private static IReadOnlyDictionary<string, PointI> MirrorAnchors(
        IReadOnlyDictionary<string, PointI> anchors,
        bool flipX)
    {
        if (!flipX || anchors.Count == 0)
        {
            return anchors;
        }

        Dictionary<string, PointI> mirrored = new(anchors.Count, StringComparer.OrdinalIgnoreCase);
        foreach ((string name, PointI point) in anchors)
        {
            mirrored[name] = point.MirrorX();
        }

        return mirrored;
    }
}
