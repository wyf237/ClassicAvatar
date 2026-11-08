using System.Windows;
using System.Windows.Controls;

namespace VControls.Controls;

/// <summary>
/// Template root panel for <see cref="VTabControl"/>. It caps its desired width
/// to a finite layout slot so a wide tab strip cannot make the template root
/// overflow the control (which would visually resize the tab strip).
/// </summary>
public class VTabRootPanel : Grid
{
    /// <inheritdoc />
    protected override Size MeasureOverride(Size constraint)
    {
        var desired = base.MeasureOverride(constraint);

        if (!double.IsInfinity(constraint.Width) && desired.Width > constraint.Width)
        {
            desired.Width = Math.Max(0, constraint.Width);
        }

        return desired;
    }
}
