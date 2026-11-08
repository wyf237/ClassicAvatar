using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VControls.Controls;

/// <summary>
/// A shadcn-style tooltip that supports plain text and icon + text content.
/// Background, foreground and placement are inherited from
/// <see cref="ToolTip"/>, so every instance can be themed or positioned without
/// a custom template. The default look uses the near-black tooltip surface.
/// </summary>
public class VToolTip : ToolTip
{
    static VToolTip()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(VToolTip),
            new FrameworkPropertyMetadata(typeof(VToolTip)));
    }

    #region Content

    /// <summary>Gets or sets an optional monochrome icon shown before the text.</summary>
    [Category("Icon")]
    [Description("Optional icon geometry shown before the text.")]
    public Geometry? Icon
    {
        get => (Geometry?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(
            nameof(Icon),
            typeof(Geometry),
            typeof(VToolTip),
            new FrameworkPropertyMetadata(null));

    /// <summary>Gets or sets the square icon size.</summary>
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
            typeof(VToolTip),
            new FrameworkPropertyMetadata(
                14d,
                null,
                static (_, baseValue) => Math.Max(0d, (double)baseValue)));

    /// <summary>Gets or sets the spacing around the icon.</summary>
    [Category("Icon")]
    [Description("Spacing around the icon.")]
    public Thickness IconMargin
    {
        get => (Thickness)GetValue(IconMarginProperty);
        set => SetValue(IconMarginProperty, value);
    }

    public static readonly DependencyProperty IconMarginProperty =
        DependencyProperty.Register(
            nameof(IconMargin),
            typeof(Thickness),
            typeof(VToolTip),
            new FrameworkPropertyMetadata(new Thickness(0, 0, 6, 0)));

    #endregion

    #region Appearance

    /// <summary>Gets or sets the tooltip corner radius.</summary>
    [Category("Appearance")]
    [Description("Tooltip corner radius.")]
    public CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.Register(
            nameof(CornerRadius),
            typeof(CornerRadius),
            typeof(VToolTip),
            new FrameworkPropertyMetadata(new CornerRadius(6)));

    /// <summary>Gets or sets whether the small placement arrow is drawn.</summary>
    [Category("Appearance")]
    [Description("Draws the placement arrow.")]
    public bool ShowArrow
    {
        get => (bool)GetValue(ShowArrowProperty);
        set => SetValue(ShowArrowProperty, value);
    }

    public static readonly DependencyProperty ShowArrowProperty =
        DependencyProperty.Register(
            nameof(ShowArrow),
            typeof(bool),
            typeof(VToolTip),
            new FrameworkPropertyMetadata(true));

    #endregion
}
