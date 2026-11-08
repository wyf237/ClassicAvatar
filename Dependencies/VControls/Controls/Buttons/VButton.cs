using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace VControls.Controls;

/// <summary>
/// Button visual variants consumed by the default <see cref="VButton"/> style.
/// </summary>
public enum VButtonVariant
{
    /// <summary>Filled accent background with on-accent text.</summary>
    Primary,

    /// <summary>Subtle surface background with primary text.</summary>
    Secondary,

    /// <summary>Danger background (used by the close window button on hover).</summary>
    Danger,

    /// <summary>Transparent background, used by icon-only window buttons.</summary>
    Ghost
}

/// <summary>
/// A themeable <see cref="Button"/> with corner radius, hover/press overlays,
/// and a <see cref="Variant"/> property that switches between primary, secondary,
/// danger and ghost appearances.
/// </summary>
public class VButton : Button
{
    static VButton()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(VButton),
            new FrameworkPropertyMetadata(typeof(VButton)));
    }

    public VButton()
    {
        Loaded += (_, _) =>
        {
            if (ToolTip is null && Content is string text && !string.IsNullOrWhiteSpace(text))
            {
                ToolTip = text;
            }
        };
    }

    /// <summary>Gets or sets the visual variant of the button.</summary>
    [Category("Appearance")]
    [Description("Visual variant: Primary, Secondary, Danger or Ghost.")]
    public VButtonVariant Variant
    {
        get => (VButtonVariant)GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }

    public static readonly DependencyProperty VariantProperty =
        DependencyProperty.Register(
            nameof(Variant),
            typeof(VButtonVariant),
            typeof(VButton),
            new FrameworkPropertyMetadata(VButtonVariant.Secondary));

    /// <summary>Gets or sets the corner radius of the button border.</summary>
    [Category("Appearance")]
    public CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.Register(
            nameof(CornerRadius),
            typeof(CornerRadius),
            typeof(VButton),
            new FrameworkPropertyMetadata(new CornerRadius(6)));
}
