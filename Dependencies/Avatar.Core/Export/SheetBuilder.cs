using Avatar.Core.Models;
using Avatar.Core.Rendering;
using WzReader.Core.Codecs;

namespace Avatar.Core.Export;

/// <summary>
/// 雪碧图构建器：把同一动作的多帧拼成一张透明底 PNG。
/// </summary>
/// <remarks>
/// 对齐策略：每格取所有帧的最大宽高，帧在格内 <b>水平居中、底部对齐</b>——
/// 角色动画的“脚底”是最稳定的参考线，底部对齐可避免行走/攻击时整体上下抖动。
/// </remarks>
public static class SheetBuilder
{
    /// <summary>
    /// 构建雪碧图。
    /// </summary>
    /// <param name="frames">同一动作的帧（允许尺寸不一致）。</param>
    /// <param name="columns">每行帧数（0 或 1 表示单行）。</param>
    /// <param name="spacing">帧间距（像素）。</param>
    /// <returns>拼好的像素缓冲区。</returns>
    public static WzPixelBuffer Build(IReadOnlyList<AvatarFrame> frames, int columns = 8, int spacing = 0)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (frames.Count == 0)
        {
            return PixelCompositor.CreateCanvas(1, 1);
        }

        int count = frames.Count;
        int columnCount = columns <= 1 ? count : Math.Min(columns, count);
        int rowCount = (count + columnCount - 1) / columnCount;

        int cellWidth = frames.Max(static frame => frame.Width);
        int cellHeight = frames.Max(static frame => frame.Height);
        int gap = Math.Max(0, spacing);

        int sheetWidth = (columnCount * cellWidth) + ((columnCount - 1) * gap);
        int sheetHeight = (rowCount * cellHeight) + ((rowCount - 1) * gap);

        WzPixelBuffer sheet = PixelCompositor.CreateCanvas(sheetWidth, sheetHeight);

        for (int index = 0; index < count; index++)
        {
            AvatarFrame frame = frames[index];
            int column = index % columnCount;
            int row = index / columnCount;

            int cellLeft = column * (cellWidth + gap);
            int cellTop = row * (cellHeight + gap);

            int offsetX = cellLeft + ((cellWidth - frame.Width) / 2);
            int offsetY = cellTop + (cellHeight - frame.Height);

            PixelCompositor.Draw(sheet, frame.Pixels, offsetX, offsetY);
        }

        return sheet;
    }
}
