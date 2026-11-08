using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VControls.Controls;

/// <summary>
/// Paints the inactive track and the active value range for <see cref="VSlider"/>.
/// The interactive <see cref="System.Windows.Controls.Primitives.Track"/> sits on
/// top and only supplies the thumb; this element owns the visuals so horizontal
/// and vertical layouts share the same rounded geometry.
/// </summary>
public class VSliderTrack : FrameworkElement
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
            typeof(VSliderTrack),
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
            typeof(VSliderTrack),
            new FrameworkPropertyMetadata(
                100d,
                FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Gets or sets the current value.</summary>
    [Category("Common")]
    [Description("Current value.")]
    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(
            nameof(Value),
            typeof(double),
            typeof(VSliderTrack),
            new FrameworkPropertyMetadata(
                0d,
                FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Gets or sets the layout orientation.</summary>
    [Category("Layout")]
    [Description("Track orientation.")]
    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    public static readonly DependencyProperty OrientationProperty =
        DependencyProperty.Register(
            nameof(Orientation),
            typeof(Orientation),
            typeof(VSliderTrack),
            new FrameworkPropertyMetadata(
                Orientation.Horizontal,
                FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Gets or sets the inactive track brush.</summary>
    [Category("Brushes")]
    [Description("Inactive track brush.")]
    public Brush? TrackBrush
    {
        get => (Brush?)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public static readonly DependencyProperty TrackBrushProperty =
        DependencyProperty.Register(
            nameof(TrackBrush),
            typeof(Brush),
            typeof(VSliderTrack),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Gets or sets the active range brush.</summary>
    [Category("Brushes")]
    [Description("Active range brush.")]
    public Brush? ActiveTrackBrush
    {
        get => (Brush?)GetValue(ActiveTrackBrushProperty);
        set => SetValue(ActiveTrackBrushProperty, value);
    }

    public static readonly DependencyProperty ActiveTrackBrushProperty =
        DependencyProperty.Register(
            nameof(ActiveTrackBrush),
            typeof(Brush),
            typeof(VSliderTrack),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Gets or sets the track thickness.</summary>
    [Category("Appearance")]
    [Description("Track thickness.")]
    public double TrackThickness
    {
        get => (double)GetValue(TrackThicknessProperty);
        set => SetValue(TrackThicknessProperty, value);
    }

    public static readonly DependencyProperty TrackThicknessProperty =
        DependencyProperty.Register(
            nameof(TrackThickness),
            typeof(double),
            typeof(VSliderTrack),
            new FrameworkPropertyMetadata(
                6d,
                FrameworkPropertyMetadataOptions.AffectsRender,
                null,
                static (_, baseValue) => Math.Max(1d, (double)baseValue)));

    /// <summary>Gets or sets the thumb size used to inset the track ends.</summary>
    [Category("Appearance")]
    [Description("Thumb size used to inset the track ends.")]
    public double ThumbSize
    {
        get => (double)GetValue(ThumbSizeProperty);
        set => SetValue(ThumbSizeProperty, value);
    }

    public static readonly DependencyProperty ThumbSizeProperty =
        DependencyProperty.Register(
            nameof(ThumbSize),
            typeof(double),
            typeof(VSliderTrack),
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
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var range = Maximum - Minimum;
        var fraction = range > 0 ? Math.Clamp((Value - Minimum) / range, 0d, 1d) : 0d;
        var thickness = Math.Max(1, TrackThickness);
        var pad = Math.Max(ThumbSize / 2, thickness / 2);
        var radius = thickness / 2;
        var inactive = TrackBrush ?? Brushes.Transparent;
        var active = ActiveTrackBrush ?? Brushes.Transparent;

        if (Orientation == Orientation.Horizontal)
        {
            var length = Math.Max(0, width - (pad * 2));
            if (length <= 0)
            {
                return;
            }

            var centerY = height / 2;
            var inactiveRect = new Rect(pad, centerY - radius, length, thickness);
            drawingContext.DrawRoundedRectangle(inactive, null, inactiveRect, radius, radius);

            var activeLength = length * fraction;
            if (activeLength > 0)
            {
                var activeRect = new Rect(pad, centerY - radius, activeLength, thickness);
                drawingContext.DrawRoundedRectangle(active, null, activeRect, radius, radius);
            }
        }
        else
        {
            var length = Math.Max(0, height - (pad * 2));
            if (length <= 0)
            {
                return;
            }

            var centerX = width / 2;
            var inactiveRect = new Rect(centerX - radius, pad, thickness, length);
            drawingContext.DrawRoundedRectangle(inactive, null, inactiveRect, radius, radius);

            var activeLength = length * fraction;
            if (activeLength > 0)
            {
                // Vertical sliders grow upward from the bottom.
                var activeRect = new Rect(
                    centerX - radius,
                    height - pad - activeLength,
                    thickness,
                    activeLength);
                drawingContext.DrawRoundedRectangle(active, null, activeRect, radius, radius);
            }
        }
    }
}
