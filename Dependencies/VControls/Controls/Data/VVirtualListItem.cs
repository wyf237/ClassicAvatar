using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace VControls.Controls;

/// <summary>
/// Container used by <see cref="VVirtualList"/>. It adds optional leading-icon
/// and trailing-content slots on top of the normal ListBoxItem content.
/// </summary>
public class VVirtualListItem : ListBoxItem
{
    static VVirtualListItem()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(VVirtualListItem),
            new FrameworkPropertyMetadata(typeof(VVirtualListItem)));
    }

    /// <summary>Gets or sets the leading icon content.</summary>
    [Category("Content")]
    public object? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(
            nameof(Icon),
            typeof(object),
            typeof(VVirtualListItem),
            new FrameworkPropertyMetadata(null));

    /// <summary>Gets or sets an optional template for <see cref="Icon"/>.</summary>
    [Category("Content")]
    public DataTemplate? IconTemplate
    {
        get => (DataTemplate?)GetValue(IconTemplateProperty);
        set => SetValue(IconTemplateProperty, value);
    }

    public static readonly DependencyProperty IconTemplateProperty =
        DependencyProperty.Register(
            nameof(IconTemplate),
            typeof(DataTemplate),
            typeof(VVirtualListItem),
            new FrameworkPropertyMetadata(null));

    /// <summary>Gets or sets the content shown at the right/bottom of the item.</summary>
    [Category("Content")]
    public object? TrailingContent
    {
        get => GetValue(TrailingContentProperty);
        set => SetValue(TrailingContentProperty, value);
    }

    public static readonly DependencyProperty TrailingContentProperty =
        DependencyProperty.Register(
            nameof(TrailingContent),
            typeof(object),
            typeof(VVirtualListItem),
            new FrameworkPropertyMetadata(null));

    /// <summary>Gets or sets an optional template for <see cref="TrailingContent"/>.</summary>
    [Category("Content")]
    public DataTemplate? TrailingContentTemplate
    {
        get => (DataTemplate?)GetValue(TrailingContentTemplateProperty);
        set => SetValue(TrailingContentTemplateProperty, value);
    }

    public static readonly DependencyProperty TrailingContentTemplateProperty =
        DependencyProperty.Register(
            nameof(TrailingContentTemplate),
            typeof(DataTemplate),
            typeof(VVirtualListItem),
            new FrameworkPropertyMetadata(null));
}
