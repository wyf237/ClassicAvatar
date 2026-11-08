using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace VControls.Controls;

/// <summary>Thumb silhouettes supported by <see cref="VSlider"/>.</summary>
public enum VSliderThumbShape
{
    /// <summary>Hollow ring: surface fill with an accent stroke.</summary>
    Ring,

    /// <summary>Solid circle (default).</summary>
    Circle,

    /// <summary>Small square with a transparent center and an accent stroke.</summary>
    HollowSquare,

    /// <summary>Small solid square.</summary>
    SolidSquare,

    /// <summary>Small solid square with softened corners.</summary>
    RoundedSquare
}

/// <summary>
/// Slider thumb that renders its silhouette directly, so all five shapes share
/// one control and one style. Fill, stroke and geometry are data-bound to the
/// owning <see cref="VSlider"/>.
/// </summary>
public class VSliderThumb : Thumb
{
    static VSliderThumb()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(VSliderThumb),
            new FrameworkPropertyMetadata(typeof(VSliderThumb)));
    }

    /// <summary>Gets or sets the silhouette.</summary>
    [Category("Appearance")]
    [Description("Thumb silhouette: Ring, Circle, HollowSquare, SolidSquare or RoundedSquare.")]
    public VSliderThumbShape Shape
    {
        get => (VSliderThumbShape)GetValue(ShapeProperty);
        set => SetValue(ShapeProperty, value);
    }

    public static readonly DependencyProperty ShapeProperty =
        DependencyProperty.Register(
            nameof(Shape),
            typeof(VSliderThumbShape),
            typeof(VSliderThumb),
            new FrameworkPropertyMetadata(
                VSliderThumbShape.Circle,
                FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Gets or sets the thumb fill.</summary>
    [Category("Brushes")]
    [Description("Thumb fill.")]
    public Brush? Fill
    {
        get => (Brush?)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public static readonly DependencyProperty FillProperty =
        DependencyProperty.Register(
            nameof(Fill),
            typeof(Brush),
            typeof(VSliderThumb),
            new FrameworkPropertyMetadata(
                Brushes.Black,
                FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Gets or sets the thumb stroke.</summary>
    [Category("Brushes")]
    [Description("Thumb stroke.")]
    public Brush? Stroke
    {
        get => (Brush?)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public static readonly DependencyProperty StrokeProperty =
        DependencyProperty.Register(
            nameof(Stroke),
            typeof(Brush),
            typeof(VSliderThumb),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Gets or sets the thumb stroke thickness.</summary>
    [Category("Appearance")]
    [Description("Thumb stroke thickness.")]
    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public static readonly DependencyProperty StrokeThicknessProperty =
        DependencyProperty.Register(
            nameof(StrokeThickness),
            typeof(double),
            typeof(VSliderThumb),
            new FrameworkPropertyMetadata(
                0d,
                FrameworkPropertyMetadataOptions.AffectsRender,
                null,
                static (_, baseValue) => Math.Max(0d, (double)baseValue)));

    /// <summary>Gets or sets the corner radius used by the rounded square shape.</summary>
    [Category("Appearance")]
    [Description("Corner radius for the rounded square silhouette.")]
    public CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.Register(
            nameof(CornerRadius),
            typeof(CornerRadius),
            typeof(VSliderThumb),
            new FrameworkPropertyMetadata(
                new CornerRadius(3),
                FrameworkPropertyMetadataOptions.AffectsRender));

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var strokeThickness = Math.Max(0, StrokeThickness);
        var inset = strokeThickness / 2;
        var fill = Fill;
        var pen = strokeThickness > 0 && Stroke is { } stroke
            ? new Pen(stroke, strokeThickness)
            : null;

        switch (Shape)
        {
            case VSliderThumbShape.Ring:
            {
                var radius = (Math.Min(width, height) / 2) - inset;
                if (radius > 0)
                {
                    drawingContext.DrawEllipse(
                        fill,
                        pen,
                        new Point(width / 2, height / 2),
                        radius,
                        radius);
                }

                break;
            }

            case VSliderThumbShape.Circle:
            {
                var radius = (Math.Min(width, height) / 2) - inset;
                if (radius > 0)
                {
                    drawingContext.DrawEllipse(
                        fill,
                        pen,
                        new Point(width / 2, height / 2),
                        radius,
                        radius);
                }

                break;
            }

            case VSliderThumbShape.HollowSquare:
            case VSliderThumbShape.SolidSquare:
            case VSliderThumbShape.RoundedSquare:
            {
                var rect = new Rect(
                    inset,
                    inset,
                    Math.Max(0, width - (inset * 2)),
                    Math.Max(0, height - (inset * 2)));
                if (rect.Width <= 0 || rect.Height <= 0)
                {
                    return;
                }

                var radius = Shape == VSliderThumbShape.RoundedSquare
                    ? Math.Max(0, Math.Min(CornerRadius.TopLeft, Math.Min(rect.Width, rect.Height) / 2))
                    : Shape == VSliderThumbShape.HollowSquare
                        ? 1
                        : 0;

                drawingContext.DrawRoundedRectangle(fill, pen, rect, radius, radius);
                break;
            }
        }
    }
}
