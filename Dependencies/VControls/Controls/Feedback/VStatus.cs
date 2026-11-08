using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VControls.Controls;

/// <summary>
/// Semantic status presets used by <see cref="VStatus"/>. Each preset maps to a
/// pair of semantic brushes in the active theme.
/// </summary>
public enum VStatusKind
{
    /// <summary>Uses <see cref="VStatus.DotBrush"/> exactly as supplied.</summary>
    Custom,

    /// <summary>Service is available (green).</summary>
    Online,

    /// <summary>Service is unavailable (red).</summary>
    Offline,

    /// <summary>Temporarily away (amber).</summary>
    Away,

    /// <summary>Busy / do not disturb (red).</summary>
    Busy,

    /// <summary>Scheduled maintenance (blue).</summary>
    Maintenance,

    /// <summary>Partially degraded service (amber).</summary>
    Degraded
}

/// <summary>
/// A compact status badge: a semantic dot plus a label, modelled after the
/// Kibo UI status component. The dot color and label can either come from a
/// <see cref="Status"/> preset or be supplied directly through
/// <see cref="DotBrush"/> and <see cref="Text"/>.
/// </summary>
public class VStatus : Control
{
    static VStatus()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(VStatus),
            new FrameworkPropertyMetadata(typeof(VStatus)));
    }

    public VStatus()
    {
        UpdateDisplayText();
    }

    #region Status

    /// <summary>Gets or sets the status preset that drives the dot color and default label.</summary>
    [Category("Appearance")]
    [Description("Status preset: Online, Offline, Away, Busy, Maintenance, Degraded or Custom.")]
    public VStatusKind Status
    {
        get => (VStatusKind)GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    public static readonly DependencyProperty StatusProperty =
        DependencyProperty.Register(
            nameof(Status),
            typeof(VStatusKind),
            typeof(VStatus),
            new FrameworkPropertyMetadata(
                VStatusKind.Online,
                OnTextSourceChanged));

    /// <summary>Gets or sets the visible label. When empty, the preset name is shown.</summary>
    [Category("Content")]
    [Description("Visible label. Leave empty to use the status preset name.")]
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(
            nameof(Text),
            typeof(string),
            typeof(VStatus),
            new FrameworkPropertyMetadata(
                string.Empty,
                OnTextSourceChanged));

    #endregion

    #region Brushes

    /// <summary>Gets or sets the dot color. Presets provide a default; a local value always wins.</summary>
    [Category("Brushes")]
    [Description("Status dot color. Presets set this value unless a local value is supplied.")]
    public Brush? DotBrush
    {
        get => (Brush?)GetValue(DotBrushProperty);
        set => SetValue(DotBrushProperty, value);
    }

    public static readonly DependencyProperty DotBrushProperty =
        DependencyProperty.Register(
            nameof(DotBrush),
            typeof(Brush),
            typeof(VStatus),
            new FrameworkPropertyMetadata(null));

    /// <summary>Gets or sets the label color.</summary>
    [Category("Brushes")]
    [Description("Label text color.")]
    public Brush? TextBrush
    {
        get => (Brush?)GetValue(TextBrushProperty);
        set => SetValue(TextBrushProperty, value);
    }

    public static readonly DependencyProperty TextBrushProperty =
        DependencyProperty.Register(
            nameof(TextBrush),
            typeof(Brush),
            typeof(VStatus),
            new FrameworkPropertyMetadata(null));

    /// <summary>Gets or sets the badge background.</summary>
    [Category("Brushes")]
    [Description("Badge background.")]
    public Brush? BadgeBackground
    {
        get => (Brush?)GetValue(BadgeBackgroundProperty);
        set => SetValue(BadgeBackgroundProperty, value);
    }

    public static readonly DependencyProperty BadgeBackgroundProperty =
        DependencyProperty.Register(
            nameof(BadgeBackground),
            typeof(Brush),
            typeof(VStatus),
            new FrameworkPropertyMetadata(null));

    /// <summary>Gets or sets the badge border color.</summary>
    [Category("Brushes")]
    [Description("Badge border color.")]
    public Brush? BadgeBorderBrush
    {
        get => (Brush?)GetValue(BadgeBorderBrushProperty);
        set => SetValue(BadgeBorderBrushProperty, value);
    }

    public static readonly DependencyProperty BadgeBorderBrushProperty =
        DependencyProperty.Register(
            nameof(BadgeBorderBrush),
            typeof(Brush),
            typeof(VStatus),
            new FrameworkPropertyMetadata(null));

    #endregion

    #region Layout

    /// <summary>Gets or sets the badge corner radius.</summary>
    [Category("Appearance")]
    [Description("Badge corner radius.")]
    public CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.Register(
            nameof(CornerRadius),
            typeof(CornerRadius),
            typeof(VStatus),
            new FrameworkPropertyMetadata(new CornerRadius(6)));

    /// <summary>Gets or sets the dot diameter in device-independent pixels.</summary>
    [Category("Appearance")]
    [Description("Dot diameter in device-independent pixels.")]
    public double DotSize
    {
        get => (double)GetValue(DotSizeProperty);
        set => SetValue(DotSizeProperty, value);
    }

    public static readonly DependencyProperty DotSizeProperty =
        DependencyProperty.Register(
            nameof(DotSize),
            typeof(double),
            typeof(VStatus),
            new FrameworkPropertyMetadata(
                7d,
                null,
                static (_, baseValue) => Math.Max(2d, (double)baseValue)));

    /// <summary>
    /// Gets or sets whether a soft ping ring animates around the dot. Turn it
    /// off for static lists where continuous motion would be distracting.
    /// </summary>
    [Category("Appearance")]
    [Description("Animates a soft ping ring around the status dot.")]
    public bool ShowPulse
    {
        get => (bool)GetValue(ShowPulseProperty);
        set => SetValue(ShowPulseProperty, value);
    }

    public static readonly DependencyProperty ShowPulseProperty =
        DependencyProperty.Register(
            nameof(ShowPulse),
            typeof(bool),
            typeof(VStatus),
            new FrameworkPropertyMetadata(true));

    #endregion

    #region Read-only display text

    private static readonly DependencyPropertyKey DisplayTextPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(DisplayText),
            typeof(string),
            typeof(VStatus),
            new FrameworkPropertyMetadata("Online"));

    [Browsable(false)]
    public static readonly DependencyProperty DisplayTextProperty =
        DisplayTextPropertyKey.DependencyProperty;

    /// <summary>Text actually rendered by the template (custom text or preset name).</summary>
    [Browsable(false)]
    public string DisplayText => (string)GetValue(DisplayTextProperty);

    #endregion

    private static void OnTextSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((VStatus)d).UpdateDisplayText();
    }

    private void UpdateDisplayText()
    {
        var custom = Text;
        var display = string.IsNullOrWhiteSpace(custom)
            ? Status switch
            {
                VStatusKind.Online => "Online",
                VStatusKind.Offline => "Offline",
                VStatusKind.Away => "Away",
                VStatusKind.Busy => "Busy",
                VStatusKind.Maintenance => "Maintenance",
                VStatusKind.Degraded => "Degraded",
                _ => "Custom"
            }
            : custom;

        SetValue(DisplayTextPropertyKey, display);
    }
}
