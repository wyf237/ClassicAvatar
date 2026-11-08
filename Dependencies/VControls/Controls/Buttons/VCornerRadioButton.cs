using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace VControls.Controls;

/// <summary>
/// A borderless, themeable <see cref="RadioButton"/> that behaves like a button.
/// It supports independent corner radii, state-specific brushes, an optional
/// monochrome SVG-derived icon, and centered text/icon content.
/// </summary>
public class VCornerRadioButton : RadioButton
{
    private static readonly Brush DefaultCheckedBackground = CreateFrozenBrush(0xFF, 0xFF, 0xFF);
    private static readonly Brush DefaultUncheckedBackground = CreateFrozenBrush(0xFF, 0xFF, 0xFF);
    private static readonly Brush DefaultCheckedForeground = CreateFrozenBrush(0x18, 0x18, 0x1B);
    private static readonly Brush DefaultUncheckedForeground = CreateFrozenBrush(0x52, 0x52, 0x5B);
    private static readonly Brush DefaultCheckedIconBrush = CreateFrozenBrush(0x18, 0x18, 0x1B);
    private static readonly Brush DefaultUncheckedIconBrush = CreateFrozenBrush(0x71, 0x71, 0x7A);

    private static readonly TimeSpan ColorTransitionDuration = TimeSpan.FromMilliseconds(320);
    private static readonly TimeSpan TextIconTransitionDuration = TimeSpan.FromMilliseconds(70);
    private static readonly TimeSpan TextIconTransitionBeginTime = TimeSpan.FromMilliseconds(130);

    private readonly SolidColorBrush _currentBackgroundBrush = new();
    private readonly SolidColorBrush _currentForegroundBrush = new();
    private readonly SolidColorBrush _currentIconBrush = new();

    static VCornerRadioButton()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(VCornerRadioButton),
            new FrameworkPropertyMetadata(typeof(VCornerRadioButton)));
    }

    public VCornerRadioButton()
    {
        Checked += OnCheckedStateChanged;
        Unchecked += OnCheckedStateChanged;
        Indeterminate += OnCheckedStateChanged;
        UpdateCurrentBrushes(animate: false);
        UpdateHasText(Content);
        Loaded += (_, _) =>
        {
            if (ToolTip is null && Content is string text && !string.IsNullOrWhiteSpace(text))
            {
                ToolTip = text;
            }
        };
    }

    /// <summary>
    /// Optional inset for the checked-state background. Use a non-zero value
    /// when the selected highlight should be smaller than the control bounds.
    /// </summary>
    [Category("Appearance")]
    [Description("Inset of the checked background inside the control bounds.")]
    public Thickness CheckedBackgroundMargin
    {
        get => (Thickness)GetValue(CheckedBackgroundMarginProperty);
        set => SetValue(CheckedBackgroundMarginProperty, value);
    }

    public static readonly DependencyProperty CheckedBackgroundMarginProperty =
        DependencyProperty.Register(
            nameof(CheckedBackgroundMargin),
            typeof(Thickness),
            typeof(VCornerRadioButton),
            new FrameworkPropertyMetadata(default(Thickness)));

    #region Corner radius

    /// <summary>
    /// Gets or sets the four corner radii in the order top-left, top-right,
    /// bottom-right, bottom-left. Zero means a square corner.
    /// </summary>
    [Category("Appearance")]
    [Description("Top-left, top-right, bottom-right, bottom-left radius. Zero = square corner.")]
    public CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.Register(
            nameof(CornerRadius),
            typeof(CornerRadius),
            typeof(VCornerRadioButton),
            new FrameworkPropertyMetadata(new CornerRadius(6)));

    #endregion

    #region Icon

    /// <summary>
    /// Gets or sets the icon geometry. In XAML this can be SVG path data or a
    /// Geometry resource. The control fills the Geometry with a state brush.
    /// </summary>
    [Category("Icon")]
    [Description("Monochrome icon geometry, usually converted from an SVG path.")]
    public Geometry? Icon
    {
        get => (Geometry?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(
            nameof(Icon),
            typeof(Geometry),
            typeof(VCornerRadioButton),
            new FrameworkPropertyMetadata(null));

    /// <summary>Gets or sets the icon width and height in device-independent pixels.</summary>
    [Category("Icon")]
    [Description("Square icon size in device-independent pixels.")]
    public double IconSize
    {
        get => (double)GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    public static readonly DependencyProperty IconSizeProperty =
        DependencyProperty.Register(
            nameof(IconSize),
            typeof(double),
            typeof(VCornerRadioButton),
            new FrameworkPropertyMetadata(
                18d,
                null,
                static (_, baseValue) => Math.Max(0d, (double)baseValue)));

    /// <summary>
    /// Gets or sets the margin around the icon. The default leaves 8px between
    /// the icon and text; icon-only buttons remove this gap in the template.
    /// </summary>
    [Category("Icon")]
    [Description("Icon margin. The default adds spacing between icon and text.")]
    public Thickness IconMargin
    {
        get => (Thickness)GetValue(IconMarginProperty);
        set => SetValue(IconMarginProperty, value);
    }

    public static readonly DependencyProperty IconMarginProperty =
        DependencyProperty.Register(
            nameof(IconMargin),
            typeof(Thickness),
            typeof(VCornerRadioButton),
            new FrameworkPropertyMetadata(new Thickness(0, 0, 8, 0)));

    #endregion

    #region State brushes

    /// <summary>Background used while <see cref="ToggleButton.IsChecked"/> is true.</summary>
    [Category("Brushes")]
    [Description("Background used when the button is selected.")]
    public Brush? CheckedBackground
    {
        get => (Brush?)GetValue(CheckedBackgroundProperty);
        set => SetValue(CheckedBackgroundProperty, value);
    }

    public static readonly DependencyProperty CheckedBackgroundProperty =
        RegisterBrush(nameof(CheckedBackground), DefaultCheckedBackground);

    /// <summary>Background used while <see cref="ToggleButton.IsChecked"/> is false or null.</summary>
    [Category("Brushes")]
    [Description("Background used when the button is not selected.")]
    public Brush? UncheckedBackground
    {
        get => (Brush?)GetValue(UncheckedBackgroundProperty);
        set => SetValue(UncheckedBackgroundProperty, value);
    }

    public static readonly DependencyProperty UncheckedBackgroundProperty =
        RegisterBrush(nameof(UncheckedBackground), DefaultUncheckedBackground);

    /// <summary>Text color used while the button is selected.</summary>
    [Category("Brushes")]
    [Description("Text color used when the button is selected.")]
    public Brush? CheckedForeground
    {
        get => (Brush?)GetValue(CheckedForegroundProperty);
        set => SetValue(CheckedForegroundProperty, value);
    }

    public static readonly DependencyProperty CheckedForegroundProperty =
        RegisterBrush(nameof(CheckedForeground), DefaultCheckedForeground);

    /// <summary>Text color used while the button is not selected.</summary>
    [Category("Brushes")]
    [Description("Text color used when the button is not selected.")]
    public Brush? UncheckedForeground
    {
        get => (Brush?)GetValue(UncheckedForegroundProperty);
        set => SetValue(UncheckedForegroundProperty, value);
    }

    public static readonly DependencyProperty UncheckedForegroundProperty =
        RegisterBrush(nameof(UncheckedForeground), DefaultUncheckedForeground);

    /// <summary>Icon fill used while the button is selected.</summary>
    [Category("Brushes")]
    [Description("Icon fill used when the button is selected.")]
    public Brush? CheckedIconBrush
    {
        get => (Brush?)GetValue(CheckedIconBrushProperty);
        set => SetValue(CheckedIconBrushProperty, value);
    }

    public static readonly DependencyProperty CheckedIconBrushProperty =
        RegisterBrush(nameof(CheckedIconBrush), DefaultCheckedIconBrush);

    /// <summary>Icon fill used while the button is not selected.</summary>
    [Category("Brushes")]
    [Description("Icon fill used when the button is not selected.")]
    public Brush? UncheckedIconBrush
    {
        get => (Brush?)GetValue(UncheckedIconBrushProperty);
        set => SetValue(UncheckedIconBrushProperty, value);
    }

    public static readonly DependencyProperty UncheckedIconBrushProperty =
        RegisterBrush(nameof(UncheckedIconBrush), DefaultUncheckedIconBrush);

    #endregion

    #region Read-only content state used by the template

    private static readonly DependencyPropertyKey HasTextPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(HasText),
            typeof(bool),
            typeof(VCornerRadioButton),
            new FrameworkPropertyMetadata(false));

    /// <summary>
    /// True when <see cref="ContentControl.Content"/> contains visible text.
    /// Used to hide the text presenter and icon gap for icon-only buttons.
    /// </summary>
    [Browsable(false)]
    public static readonly DependencyProperty HasTextProperty =
        HasTextPropertyKey.DependencyProperty;

    [Browsable(false)]
    public bool HasText => (bool)GetValue(HasTextProperty);

    #endregion

    #region Read-only effective brushes used by the template

    private static readonly DependencyPropertyKey CurrentBackgroundPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(CurrentBackground),
            typeof(Brush),
            typeof(VCornerRadioButton),
            new FrameworkPropertyMetadata(Brushes.Transparent));

    [Browsable(false)]
    public static readonly DependencyProperty CurrentBackgroundProperty =
        CurrentBackgroundPropertyKey.DependencyProperty;

    [Browsable(false)]
    public Brush? CurrentBackground => (Brush?)GetValue(CurrentBackgroundProperty);

    private static readonly DependencyPropertyKey CurrentForegroundPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(CurrentForeground),
            typeof(Brush),
            typeof(VCornerRadioButton),
            new FrameworkPropertyMetadata(Brushes.Black));

    [Browsable(false)]
    public static readonly DependencyProperty CurrentForegroundProperty =
        CurrentForegroundPropertyKey.DependencyProperty;

    [Browsable(false)]
    public Brush? CurrentForeground => (Brush?)GetValue(CurrentForegroundProperty);

    private static readonly DependencyPropertyKey CurrentIconBrushPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(CurrentIconBrush),
            typeof(Brush),
            typeof(VCornerRadioButton),
            new FrameworkPropertyMetadata(Brushes.Black));

    [Browsable(false)]
    public static readonly DependencyProperty CurrentIconBrushProperty =
        CurrentIconBrushPropertyKey.DependencyProperty;

    [Browsable(false)]
    public Brush? CurrentIconBrush => (Brush?)GetValue(CurrentIconBrushProperty);

    #endregion

    #region Layout & selection indicator

    /// <summary>
    /// Gets or sets the orientation of the icon/text stack. Use
    /// <see cref="Orientation.Vertical"/> for rail buttons where the icon sits
    /// above the label. Defaults to <see cref="Orientation.Horizontal"/>.
    /// </summary>
    [Category("Layout")]
    [Description("Vertical stacks the icon above the label (used by navigation rails).")]
    public Orientation ContentOrientation
    {
        get => (Orientation)GetValue(ContentOrientationProperty);
        set => SetValue(ContentOrientationProperty, value);
    }

    public static readonly DependencyProperty ContentOrientationProperty =
        DependencyProperty.Register(
            nameof(ContentOrientation),
            typeof(Orientation),
            typeof(VCornerRadioButton),
            new FrameworkPropertyMetadata(Orientation.Horizontal));

    /// <summary>
    /// Gets or sets whether a short accent bar is drawn on the left edge while
    /// the button is selected.
    /// </summary>
    [Category("Brushes")]
    [Description("Draws a 2px accent bar on the left edge when selected.")]
    public bool ShowCheckedIndicator
    {
        get => (bool)GetValue(ShowCheckedIndicatorProperty);
        set => SetValue(ShowCheckedIndicatorProperty, value);
    }

    public static readonly DependencyProperty ShowCheckedIndicatorProperty =
        DependencyProperty.Register(
            nameof(ShowCheckedIndicator),
            typeof(bool),
            typeof(VCornerRadioButton),
            new FrameworkPropertyMetadata(false));

    /// <summary>Gets or sets the brush of the selected indicator bar.</summary>
    [Category("Brushes")]
    [Description("Fill of the selected indicator bar.")]
    public Brush? CheckedIndicatorBrush
    {
        get => (Brush?)GetValue(CheckedIndicatorBrushProperty);
        set => SetValue(CheckedIndicatorBrushProperty, value);
    }

    public static readonly DependencyProperty CheckedIndicatorBrushProperty =
        RegisterBrush(nameof(CheckedIndicatorBrush), DefaultCheckedBackground);

    #endregion

    /// <inheritdoc />
    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);
        UpdateHasText(newContent);
    }

    private void UpdateHasText(object? content)
    {
        var hasText = content switch
        {
            null => false,
            string text => !string.IsNullOrWhiteSpace(text),
            _ => true
        };

        SetValue(HasTextPropertyKey, hasText);
    }

    private void OnCheckedStateChanged(object sender, RoutedEventArgs e)
    {
        UpdateCurrentBrushes(animate: true);
    }

    private static DependencyProperty RegisterBrush(string name, Brush defaultValue)
    {
        return DependencyProperty.Register(
            name,
            typeof(Brush),
            typeof(VCornerRadioButton),
            new FrameworkPropertyMetadata(defaultValue, OnBrushPropertyChanged));
    }

    private static void OnBrushPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((VCornerRadioButton)d).UpdateCurrentBrushes(animate: true);
    }

    private void UpdateCurrentBrushes(bool animate)
    {
        // Do not animate the initial XAML state. This avoids a flash from the
        // fallback color while the control is first loaded.
        animate &= IsLoaded;

        var isChecked = IsChecked == true;

        TransitionBrush(
            CurrentBackgroundPropertyKey,
            _currentBackgroundBrush,
            (isChecked ? CheckedBackground : UncheckedBackground) ?? Brushes.Transparent,
            animate);

        TransitionBrush(
            CurrentForegroundPropertyKey,
            _currentForegroundBrush,
            (isChecked ? CheckedForeground : UncheckedForeground) ?? Brushes.Black,
            animate,
            TextIconTransitionDuration,
            TextIconTransitionBeginTime);

        TransitionBrush(
            CurrentIconBrushPropertyKey,
            _currentIconBrush,
            (isChecked ? CheckedIconBrush : UncheckedIconBrush) ?? Brushes.Black,
            animate,
            TextIconTransitionDuration,
            TextIconTransitionBeginTime);
    }

    /// <summary>
    /// Animates a state brush while keeping a single brush instance in the visual tree.
    /// Solid colors cross-fade; other brush types (for example gradients) are swapped.
    /// </summary>
    private void TransitionBrush(
        DependencyPropertyKey currentPropertyKey,
        SolidColorBrush transitionBrush,
        Brush targetBrush,
        bool animate,
        TimeSpan? duration = null,
        TimeSpan? beginTime = null)
    {
        var currentBrush = GetValue(currentPropertyKey.DependencyProperty);

        if (targetBrush is not SolidColorBrush solidTarget)
        {
            transitionBrush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            SetValue(currentPropertyKey, targetBrush);
            return;
        }

        var targetColor = GetEffectiveColor(solidTarget);

        if (!ReferenceEquals(currentBrush, transitionBrush))
        {
            transitionBrush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            transitionBrush.Color = targetColor;
            SetValue(currentPropertyKey, transitionBrush);
            return;
        }

        var transitionDuration = duration ?? ColorTransitionDuration;

        if (!animate || transitionDuration <= TimeSpan.Zero || transitionBrush.Color == targetColor)
        {
            transitionBrush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            transitionBrush.Color = targetColor;
            return;
        }

        var animation = new ColorAnimation
        {
            From = transitionBrush.Color,
            To = targetColor,
            // TimeSpan.Zero rather than null: a null BeginTime prevents this
            // template-bound brush animation from starting in WPF.
            BeginTime = beginTime ?? TimeSpan.Zero,
            Duration = new Duration(transitionDuration),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };

        transitionBrush.BeginAnimation(
            SolidColorBrush.ColorProperty,
            animation,
            HandoffBehavior.SnapshotAndReplace);
    }

    private static Color GetEffectiveColor(SolidColorBrush brush)
    {
        var color = brush.Color;
        if (brush.Opacity >= 1d)
        {
            return color;
        }

        var alpha = (byte)Math.Round(color.A * brush.Opacity);
        return Color.FromArgb(alpha, color.R, color.G, color.B);
    }

    private static Brush CreateFrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
