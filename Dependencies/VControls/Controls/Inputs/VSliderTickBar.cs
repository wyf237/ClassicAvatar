using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VControls.Controls;

/// <summary>
/// Draws step markers for segmented <see cref="VSlider"/> instances. Markers are
/// aligned with the thumb center line and omitted when the step count would be
/// too dense to read.
/// </summary>
public class VSliderTickBar : FrameworkElement
{
    /// <summary>Gets or sets the minimum value.</summary>
    [Category("Common")]
    [Description("Minimum value.")]
    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(
            nameof(Minimum),
            typeof(double),
            typeof(VSliderTickBar),
            new FrameworkPropertyMetadata(
                0d,
                FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Gets or sets the maximum value.</summary>
    [Category("Common")]
    [Description("Maximum value.")]
    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(
            nameof(Maximum),
            typeof(double),
            typeof(VSliderTickBar),
            new FrameworkPropertyMetadata(
                100d,
                FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Gets or sets the step between markers.</summary>
    [Category("Common")]
    [Description("Step between markers.")]
    public double Step
    {
        get => (double)GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    public static readonly DependencyProperty StepProperty =
        DependencyProperty.Register(
            nameof(Step),
            typeof(double),
            typeof(VSliderTickBar),
            new FrameworkPropertyMetadata(
                10d,
                FrameworkPropertyMetadataOptions.AffectsRender,
                null,
                static (_, baseValue) => Math.Max(0.0001d, (double)baseValue)));

    /// <summary>Gets or sets the layout orientation.</summary>
    [Category("Layout")]
    [Description("Marker orientation.")]
    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    public static readonly DependencyProperty OrientationProperty =
        DependencyProperty.Register(
            nameof(Orientation),
            typeof(Orientation),
            typeof(VSliderTickBar),
            new FrameworkPropertyMetadata(
                Orientation.Horizontal,
                FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Gets or sets the marker brush.</summary>
    [Category("Brushes")]
    [Description("Marker brush.")]
    public Brush? TickBrush
    {
        get => (Brush?)GetValue(TickBrushProperty);
        set => SetValue(TickBrushProperty, value);
    }

    public static readonly DependencyProperty TickBrushProperty =
        DependencyProperty.Register(
            nameof(TickBrush),
            typeof(Brush),
            typeof(VSliderTickBar),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Gets or sets the marker diameter.</summary>
    [Category("Appearance")]
    [Description("Marker diameter.")]
    public double TickSize
    {
        get => (double)GetValue(TickSizeProperty);
        set => SetValue(TickSizeProperty, value);
    }

    public static readonly DependencyProperty TickSizeProperty =
        DependencyProperty.Register(
            nameof(TickSize),
            typeof(double),
            typeof(VSliderTickBar),
            new FrameworkPropertyMetadata(
                3d,
                FrameworkPropertyMetadataOptions.AffectsRender,
                null,
                static (_, baseValue) => Math.Max(1d, (double)baseValue)));

    /// <summary>Gets or sets the thumb size used to inset the marker line.</summary>
    [Category("Appearance")]
    [Description("Thumb size used to inset the marker line.")]
    public double ThumbSize
    {
        get => (double)GetValue(ThumbSizeProperty);
        set => SetValue(ThumbSizeProperty, value);
    }

    public static readonly DependencyProperty ThumbSizeProperty =
        DependencyProperty.Register(
            nameof(ThumbSize),
            typeof(double),
            typeof(VSliderTickBar),
            new FrameworkPropertyMetadata(
                16d,
                FrameworkPropertyMetadataOptions.AffectsRender,
                null,
                static (_, baseValue) => Math.Max(0d, (double)baseValue)));

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var width = ActualWidth;
        var height = ActualHeight;
        var range = Maximum - Minimum;
        var step = Step;

        if (width <= 0 || height <= 0 || range <= 0 || step <= 0)
        {
            return;
        }

        var count = (int)Math.Floor((range / step) + 0.0001d) + 1;
        if (count < 2 || count > 64)
        {
            return;
        }

        var brush = TickBrush ?? Brushes.Gray;
        var radius = Math.Max(1, TickSize) / 2;
        var pad = Math.Max(ThumbSize / 2, radius);
        var length = (Orientation == Orientation.Horizontal ? width : height) - (pad * 2);

        if (length <= 0)
        {
            return;
        }

        for (var i = 0; i < count; i++)
        {
            var value = Minimum + (i * step);
            if (value > Maximum + 0.0001d)
            {
                break;
            }

            var fraction = (value - Minimum) / range;
            Point center;
            if (Orientation == Orientation.Horizontal)
            {
                center = new Point(pad + (fraction * length), height / 2);
            }
            else
            {
                center = new Point(width / 2, height - pad - (fraction * length));
            }

            drawingContext.DrawEllipse(brush, null, center, radius, radius);
        }
    }
}
