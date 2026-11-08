using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace VControls.Controls;

/// <summary>
/// Describes one segment in a <see cref="VSegmentedControl"/>. A segment can
/// show text, an icon, or both.
/// </summary>
public class VSegmentedItem : DependencyObject
{
    /// <summary>Gets or sets the segment label.</summary>
    [Category("Content")]
    [Description("Segment label.")]
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(
            nameof(Text),
            typeof(string),
            typeof(VSegmentedItem),
            new FrameworkPropertyMetadata(string.Empty));

    /// <summary>Gets or sets an optional monochrome icon geometry.</summary>
    [Category("Icon")]
    [Description("Optional icon geometry.")]
    public Geometry? Icon
    {
        get => (Geometry?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(
            nameof(Icon),
            typeof(Geometry),
            typeof(VSegmentedItem),
            new FrameworkPropertyMetadata(null));

    /// <summary>Gets or sets the square icon size in device-independent pixels.</summary>
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
            typeof(VSegmentedItem),
            new FrameworkPropertyMetadata(
                15d,
                null,
                static (_, baseValue) => Math.Max(0d, (double)baseValue)));

    /// <summary>Gets or sets the spacing around the icon. The default leaves 6px before the label.</summary>
    [Category("Icon")]
    [Description("Icon spacing. Defaults to 6px between icon and text.")]
    public Thickness IconMargin
    {
        get => (Thickness)GetValue(IconMarginProperty);
        set => SetValue(IconMarginProperty, value);
    }

    public static readonly DependencyProperty IconMarginProperty =
        DependencyProperty.Register(
            nameof(IconMargin),
            typeof(Thickness),
            typeof(VSegmentedItem),
            new FrameworkPropertyMetadata(new Thickness(0, 0, 6, 0)));

    /// <summary>Gets or sets an optional tooltip; icon-only segments use the label as a fallback.</summary>
    [Category("Content")]
    [Description("Optional segment tooltip.")]
    public string ToolTip
    {
        get => (string)GetValue(ToolTipProperty);
        set => SetValue(ToolTipProperty, value);
    }

    public static readonly DependencyProperty ToolTipProperty =
        DependencyProperty.Register(
            nameof(ToolTip),
            typeof(string),
            typeof(VSegmentedItem),
            new FrameworkPropertyMetadata(string.Empty));

    /// <summary>Gets or sets optional application data associated with the segment.</summary>
    [Category("Data")]
    [Description("Optional application data.")]
    public object? Tag
    {
        get => GetValue(TagProperty);
        set => SetValue(TagProperty, value);
    }

    public static readonly DependencyProperty TagProperty =
        DependencyProperty.Register(
            nameof(Tag),
            typeof(object),
            typeof(VSegmentedItem),
            new FrameworkPropertyMetadata(null));
}
