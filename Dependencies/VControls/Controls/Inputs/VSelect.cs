using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VControls.Controls;

/// <summary>
/// A shadcn-style select built on <see cref="ComboBox"/>. It keeps the native
/// selection, keyboard and automation behaviour while exposing a small set of
/// surface knobs: width, trigger/background colors and selected item colors.
/// </summary>
public class VSelect : ComboBox
{
    private bool _syncingBackground;

    static VSelect()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(VSelect),
            new FrameworkPropertyMetadata(typeof(VSelect)));

        // Controls expose Background by convention; mirror it to the trigger
        // brush so both Background and TriggerBackground behave as expected.
        BackgroundProperty.OverrideMetadata(
            typeof(VSelect),
            new FrameworkPropertyMetadata(null, OnBackgroundChanged));
    }

    public VSelect()
    {
        IsEditable = false;
        IsTextSearchEnabled = true;
    }

    #region Content

    /// <summary>
    /// Gets or sets the leading text shown before the selected value, matching
    /// the "Favorite Movie:" pattern from the reference Select 28.
    /// </summary>
    [Category("Content")]
    [Description("Leading text shown before the selected value.")]
    public string Prefix
    {
        get => (string)GetValue(PrefixProperty);
        set => SetValue(PrefixProperty, value);
    }

    public static readonly DependencyProperty PrefixProperty =
        DependencyProperty.Register(
            nameof(Prefix),
            typeof(string),
            typeof(VSelect),
            new FrameworkPropertyMetadata(string.Empty));

    /// <summary>Gets or sets the placeholder shown when nothing is selected.</summary>
    [Category("Content")]
    [Description("Placeholder shown when nothing is selected.")]
    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.Register(
            nameof(Placeholder),
            typeof(string),
            typeof(VSelect),
            new FrameworkPropertyMetadata("请选择"));

    #endregion

    #region Appearance

    /// <summary>Gets or sets the trigger corner radius.</summary>
    [Category("Appearance")]
    [Description("Trigger corner radius.")]
    public CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.Register(
            nameof(CornerRadius),
            typeof(CornerRadius),
            typeof(VSelect),
            new FrameworkPropertyMetadata(new CornerRadius(6)));

    /// <summary>Gets or sets the drop-down panel corner radius.</summary>
    [Category("Appearance")]
    [Description("Drop-down panel corner radius.")]
    public CornerRadius DropDownCornerRadius
    {
        get => (CornerRadius)GetValue(DropDownCornerRadiusProperty);
        set => SetValue(DropDownCornerRadiusProperty, value);
    }

    public static readonly DependencyProperty DropDownCornerRadiusProperty =
        DependencyProperty.Register(
            nameof(DropDownCornerRadius),
            typeof(CornerRadius),
            typeof(VSelect),
            new FrameworkPropertyMetadata(new CornerRadius(8)));

    /// <summary>Gets or sets whether a check mark is drawn next to the selected item.</summary>
    [Category("Appearance")]
    [Description("Draws a check mark next to the selected item.")]
    public bool ShowCheckMark
    {
        get => (bool)GetValue(ShowCheckMarkProperty);
        set => SetValue(ShowCheckMarkProperty, value);
    }

    public static readonly DependencyProperty ShowCheckMarkProperty =
        DependencyProperty.Register(
            nameof(ShowCheckMark),
            typeof(bool),
            typeof(VSelect),
            new FrameworkPropertyMetadata(true));

    #endregion

    #region Brushes

    /// <summary>Gets or sets the trigger background.</summary>
    [Category("Brushes")]
    [Description("Trigger background.")]
    public Brush? TriggerBackground
    {
        get => (Brush?)GetValue(TriggerBackgroundProperty);
        set => SetValue(TriggerBackgroundProperty, value);
    }

    public static readonly DependencyProperty TriggerBackgroundProperty =
        RegisterBrush(nameof(TriggerBackground));

    /// <summary>Gets or sets the drop-down panel background.</summary>
    [Category("Brushes")]
    [Description("Drop-down panel background.")]
    public Brush? DropDownBackground
    {
        get => (Brush?)GetValue(DropDownBackgroundProperty);
        set => SetValue(DropDownBackgroundProperty, value);
    }

    public static readonly DependencyProperty DropDownBackgroundProperty =
        RegisterBrush(nameof(DropDownBackground));

    /// <summary>Gets or sets the drop-down panel border color.</summary>
    [Category("Brushes")]
    [Description("Drop-down panel border.")]
    public Brush? DropDownBorderBrush
    {
        get => (Brush?)GetValue(DropDownBorderBrushProperty);
        set => SetValue(DropDownBorderBrushProperty, value);
    }

    public static readonly DependencyProperty DropDownBorderBrushProperty =
        RegisterBrush(nameof(DropDownBorderBrush));

    /// <summary>Gets or sets the background of the selected drop-down row.</summary>
    [Category("Brushes")]
    [Description("Selected item background.")]
    public Brush? SelectedBackground
    {
        get => (Brush?)GetValue(SelectedBackgroundProperty);
        set => SetValue(SelectedBackgroundProperty, value);
    }

    public static readonly DependencyProperty SelectedBackgroundProperty =
        RegisterBrush(nameof(SelectedBackground));

    /// <summary>Gets or sets the foreground of the selected drop-down row.</summary>
    [Category("Brushes")]
    [Description("Selected item foreground.")]
    public Brush? SelectedForeground
    {
        get => (Brush?)GetValue(SelectedForegroundProperty);
        set => SetValue(SelectedForegroundProperty, value);
    }

    public static readonly DependencyProperty SelectedForegroundProperty =
        RegisterBrush(nameof(SelectedForeground));

    /// <summary>Gets or sets the hover background of drop-down rows.</summary>
    [Category("Brushes")]
    [Description("Drop-down row hover background.")]
    public Brush? ItemHoverBackground
    {
        get => (Brush?)GetValue(ItemHoverBackgroundProperty);
        set => SetValue(ItemHoverBackgroundProperty, value);
    }

    public static readonly DependencyProperty ItemHoverBackgroundProperty =
        RegisterBrush(nameof(ItemHoverBackground));

    /// <summary>Gets or sets the default foreground of drop-down rows.</summary>
    [Category("Brushes")]
    [Description("Drop-down row foreground.")]
    public Brush? ItemForeground
    {
        get => (Brush?)GetValue(ItemForegroundProperty);
        set => SetValue(ItemForegroundProperty, value);
    }

    public static readonly DependencyProperty ItemForegroundProperty =
        RegisterBrush(nameof(ItemForeground));

    /// <summary>Gets or sets the placeholder text color.</summary>
    [Category("Brushes")]
    [Description("Placeholder text color.")]
    public Brush? PlaceholderForeground
    {
        get => (Brush?)GetValue(PlaceholderForegroundProperty);
        set => SetValue(PlaceholderForegroundProperty, value);
    }

    public static readonly DependencyProperty PlaceholderForegroundProperty =
        RegisterBrush(nameof(PlaceholderForeground));

    #endregion

    private static DependencyProperty RegisterBrush(string name)
    {
        return DependencyProperty.Register(
            name,
            typeof(Brush),
            typeof(VSelect),
            new FrameworkPropertyMetadata(null));
    }

    private static void OnBackgroundChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var select = (VSelect)d;
        if (select._syncingBackground)
        {
            return;
        }

        select._syncingBackground = true;
        select.SetCurrentValue(TriggerBackgroundProperty, e.NewValue);
        select._syncingBackground = false;
    }
}
