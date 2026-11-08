using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VControls.Controls;

/// <summary>Interaction modes supported by <see cref="VSlider"/>.</summary>
public enum VSliderMode
{
    /// <summary>Free movement between the minimum and maximum.</summary>
    Continuous,

    /// <summary>Snaps to <see cref="VSlider.Step"/> increments and shows tick marks.</summary>
    Segmented
}

/// <summary>
/// A themeable slider with continuous or stepped behaviour, horizontal or
/// vertical orientation, and five thumb silhouettes. It derives from
/// <see cref="Slider"/> so keyboard, automation and value binding stay native.
/// </summary>
public class VSlider : Slider
{
    private bool _isSnapping;
    private double? _continuousSmallChange;
    private double? _continuousLargeChange;

    static VSlider()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(VSlider),
            new FrameworkPropertyMetadata(typeof(VSlider)));
    }

    public VSlider()
    {
        IsMoveToPointEnabled = true;
        UpdateMode();
    }

    #region Mode & step

    /// <summary>Gets or sets whether the slider is continuous or stepped.</summary>
    [Category("Behaviour")]
    [Description("Continuous free movement or segmented step snapping.")]
    public VSliderMode Mode
    {
        get => (VSliderMode)GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    public static readonly DependencyProperty ModeProperty =
        DependencyProperty.Register(
            nameof(Mode),
            typeof(VSliderMode),
            typeof(VSlider),
            new FrameworkPropertyMetadata(
                VSliderMode.Continuous,
                OnModeChanged));

    /// <summary>Gets or sets the step used by segmented mode and tick marks.</summary>
    [Category("Behaviour")]
    [Description("Step used by segmented mode.")]
    public double Step
    {
        get => (double)GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    public static readonly DependencyProperty StepProperty =
        DependencyProperty.Register(
            nameof(Step),
            typeof(double),
            typeof(VSlider),
            new FrameworkPropertyMetadata(
                10d,
                OnStepChanged,
                static (_, baseValue) => Math.Max(0.0001d, (double)baseValue)));

    /// <summary>
    /// Gets or sets whether tick markers are drawn. The default style also
    /// enables them automatically in segmented mode.
    /// </summary>
    [Category("Appearance")]
    [Description("Draws step markers along the track.")]
    public bool ShowTicks
    {
        get => (bool)GetValue(ShowTicksProperty);
        set => SetValue(ShowTicksProperty, value);
    }

    public static readonly DependencyProperty ShowTicksProperty =
        DependencyProperty.Register(
            nameof(ShowTicks),
            typeof(bool),
            typeof(VSlider),
            new FrameworkPropertyMetadata(false));

    #endregion

    #region Thumb & track

    /// <summary>Gets or sets the thumb silhouette.</summary>
    [Category("Appearance")]
    [Description("Thumb silhouette.")]
    public VSliderThumbShape ThumbShape
    {
        get => (VSliderThumbShape)GetValue(ThumbShapeProperty);
        set => SetValue(ThumbShapeProperty, value);
    }

    public static readonly DependencyProperty ThumbShapeProperty =
        DependencyProperty.Register(
            nameof(ThumbShape),
            typeof(VSliderThumbShape),
            typeof(VSlider),
            new FrameworkPropertyMetadata(VSliderThumbShape.Circle));

    /// <summary>Gets or sets the thumb diameter.</summary>
    [Category("Appearance")]
    [Description("Thumb diameter.")]
    public double ThumbSize
    {
        get => (double)GetValue(ThumbSizeProperty);
        set => SetValue(ThumbSizeProperty, value);
    }

    public static readonly DependencyProperty ThumbSizeProperty =
        DependencyProperty.Register(
            nameof(ThumbSize),
            typeof(double),
            typeof(VSlider),
            new FrameworkPropertyMetadata(
                15d,
                null,
                static (_, baseValue) => Math.Max(2d, (double)baseValue)));

    /// <summary>Gets or sets the round-square thumb corner radius.</summary>
    [Category("Appearance")]
    [Description("Rounded square thumb corner radius.")]
    public CornerRadius ThumbCornerRadius
    {
        get => (CornerRadius)GetValue(ThumbCornerRadiusProperty);
        set => SetValue(ThumbCornerRadiusProperty, value);
    }

    public static readonly DependencyProperty ThumbCornerRadiusProperty =
        DependencyProperty.Register(
            nameof(ThumbCornerRadius),
            typeof(CornerRadius),
            typeof(VSlider),
            new FrameworkPropertyMetadata(new CornerRadius(3)));

    /// <summary>Gets or sets the thumb fill.</summary>
    [Category("Brushes")]
    [Description("Thumb fill.")]
    public Brush? ThumbFill
    {
        get => (Brush?)GetValue(ThumbFillProperty);
        set => SetValue(ThumbFillProperty, value);
    }

    public static readonly DependencyProperty ThumbFillProperty =
        RegisterBrush(nameof(ThumbFill));

    /// <summary>Gets or sets the thumb stroke.</summary>
    [Category("Brushes")]
    [Description("Thumb stroke.")]
    public Brush? ThumbStroke
    {
        get => (Brush?)GetValue(ThumbStrokeProperty);
        set => SetValue(ThumbStrokeProperty, value);
    }

    public static readonly DependencyProperty ThumbStrokeProperty =
        RegisterBrush(nameof(ThumbStroke));

    /// <summary>Gets or sets the thumb stroke thickness.</summary>
    [Category("Appearance")]
    [Description("Thumb stroke thickness.")]
    public double ThumbStrokeThickness
    {
        get => (double)GetValue(ThumbStrokeThicknessProperty);
        set => SetValue(ThumbStrokeThicknessProperty, value);
    }

    public static readonly DependencyProperty ThumbStrokeThicknessProperty =
        DependencyProperty.Register(
            nameof(ThumbStrokeThickness),
            typeof(double),
            typeof(VSlider),
            new FrameworkPropertyMetadata(
                0d,
                null,
                static (_, baseValue) => Math.Max(0d, (double)baseValue)));

    /// <summary>Gets or sets the active portion of the track.</summary>
    [Category("Brushes")]
    [Description("Active track brush.")]
    public Brush? ActiveTrackBrush
    {
        get => (Brush?)GetValue(ActiveTrackBrushProperty);
        set => SetValue(ActiveTrackBrushProperty, value);
    }

    public static readonly DependencyProperty ActiveTrackBrushProperty =
        RegisterBrush(nameof(ActiveTrackBrush));

    /// <summary>Gets or sets the inactive portion of the track.</summary>
    [Category("Brushes")]
    [Description("Inactive track brush.")]
    public Brush? TrackBrush
    {
        get => (Brush?)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public static readonly DependencyProperty TrackBrushProperty =
        RegisterBrush(nameof(TrackBrush));

    /// <summary>Gets or sets the tick marker brush.</summary>
    [Category("Brushes")]
    [Description("Tick marker brush.")]
    public Brush? TickBrush
    {
        get => (Brush?)GetValue(TickBrushProperty);
        set => SetValue(TickBrushProperty, value);
    }

    public static readonly DependencyProperty TickBrushProperty =
        RegisterBrush(nameof(TickBrush));

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
            typeof(VSlider),
            new FrameworkPropertyMetadata(
                6d,
                null,
                static (_, baseValue) => Math.Max(1d, (double)baseValue)));

    #endregion

    /// <inheritdoc />
    protected override void OnValueChanged(double oldValue, double newValue)
    {
        base.OnValueChanged(oldValue, newValue);

        if (Mode != VSliderMode.Segmented || _isSnapping)
        {
            return;
        }

        var snapped = SnapValue(newValue);
        if (Math.Abs(snapped - newValue) > 0.000001d)
        {
            _isSnapping = true;
            SetCurrentValue(ValueProperty, snapped);
            _isSnapping = false;
        }
    }

    /// <inheritdoc />
    protected override void OnMinimumChanged(double oldMinimum, double newMinimum)
    {
        base.OnMinimumChanged(oldMinimum, newMinimum);
        UpdateMode();
    }

    /// <inheritdoc />
    protected override void OnMaximumChanged(double oldMaximum, double newMaximum)
    {
        base.OnMaximumChanged(oldMaximum, newMaximum);
        UpdateMode();
    }

    private static void OnModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((VSlider)d).UpdateMode();
    }

    private static void OnStepChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((VSlider)d).UpdateMode();
    }

    private void UpdateMode()
    {
        if (Mode == VSliderMode.Segmented)
        {
            var step = Math.Max(0.0001d, Step);

            // Preserve the continuous-mode keyboard increments so switching
            // back does not leave the slider moving in large step jumps.
            _continuousSmallChange ??= SmallChange;
            _continuousLargeChange ??= LargeChange;

            IsSnapToTickEnabled = true;
            TickFrequency = step;
            SmallChange = step;
            LargeChange = step;

            var snapped = SnapValue(Value);
            if (Math.Abs(snapped - Value) > 0.000001d)
            {
                _isSnapping = true;
                SetCurrentValue(ValueProperty, snapped);
                _isSnapping = false;
            }
        }
        else
        {
            IsSnapToTickEnabled = false;

            if (_continuousSmallChange is { } smallChange)
            {
                SmallChange = smallChange;
                _continuousSmallChange = null;
            }

            if (_continuousLargeChange is { } largeChange)
            {
                LargeChange = largeChange;
                _continuousLargeChange = null;
            }
        }
    }

    private double SnapValue(double value)
    {
        var range = Maximum - Minimum;
        var step = Step;
        if (range <= 0 || step <= 0)
        {
            return Math.Clamp(value, Minimum, Maximum);
        }

        var steps = Math.Round((value - Minimum) / step, MidpointRounding.AwayFromZero);
        return Math.Clamp(Minimum + (steps * step), Minimum, Maximum);
    }

    private static DependencyProperty RegisterBrush(string name)
    {
        return DependencyProperty.Register(
            name,
            typeof(Brush),
            typeof(VSlider),
            new FrameworkPropertyMetadata(null));
    }
}
