using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VControls.Controls;

/// <summary>Selects how <see cref="VVirtualList"/> lays out and scrolls its items.</summary>
public enum VVirtualListDisplayMode
{
    /// <summary>One full-width row per item, arranged vertically.</summary>
    Vertical,

    /// <summary>Fixed-width cards arranged in one horizontal row.</summary>
    Horizontal,

    /// <summary>Fixed rows per column, with columns filling the available width.</summary>
    Grid
}

/// <summary>
/// A virtualizing list with vertical, horizontal and fixed-row grid layouts,
/// a slim auto-hiding scrollbar, and fully bindable item/selection colors.
/// </summary>
public class VVirtualList : ListBox
{
    private const string VerticalPanelKey = "VVirtualList.Panel.Vertical";
    private const string HorizontalPanelKey = "VVirtualList.Panel.Horizontal";
    private const string GridPanelKey = "VVirtualList.Panel.Grid";
    private const string VerticalItemStyleKey = "VVirtualListItem.Vertical";
    private const string HorizontalItemStyleKey = "VVirtualListItem.Horizontal";
    private const string GridItemStyleKey = "VVirtualListItem.Grid";

    private static readonly Lazy<ResourceDictionary> ControlResources = new(() =>
        new ResourceDictionary
        {
            Source = new Uri(
                "pack://application:,,,/VControls;component/Themes/Controls/VVirtualList.xaml",
                UriKind.Absolute)
        });

    private Style? _appliedContainerStyle;

    static VVirtualList()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(VVirtualList),
            new FrameworkPropertyMetadata(typeof(VVirtualList)));
    }

    public VVirtualList()
    {
        SelectionMode = SelectionMode.Single;
        VirtualizingPanel.SetScrollUnit(this, ScrollUnit.Pixel);
        Loaded += (_, _) => ApplyDisplayMode();
    }

    #region Display mode and layout

    /// <summary>Gets or sets the item layout. The default is <see cref="VVirtualListDisplayMode.Vertical"/>.</summary>
    [Category("Layout")]
    [Description("Selects vertical rows, horizontal cards, or a fixed-row grid.")]
    public VVirtualListDisplayMode DisplayMode
    {
        get => (VVirtualListDisplayMode)GetValue(DisplayModeProperty);
        set => SetValue(DisplayModeProperty, value);
    }

    public static readonly DependencyProperty DisplayModeProperty =
        DependencyProperty.Register(
            nameof(DisplayMode),
            typeof(VVirtualListDisplayMode),
            typeof(VVirtualList),
            new FrameworkPropertyMetadata(
                VVirtualListDisplayMode.Vertical,
                FrameworkPropertyMetadataOptions.AffectsMeasure,
                OnDisplayModeChanged));

    /// <summary>Gets or sets the space between items, in device-independent pixels.</summary>
    [Category("Layout")]
    public double Gap
    {
        get => (double)GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    public static readonly DependencyProperty GapProperty =
        DependencyProperty.Register(
            nameof(Gap),
            typeof(double),
            typeof(VVirtualList),
            new FrameworkPropertyMetadata(
                8d,
                FrameworkPropertyMetadataOptions.AffectsMeasure,
                OnGapChanged));

    /// <summary>
    /// Gets or sets the height of vertical rows and grid items. The horizontal
    /// mode ignores this value and stretches cards to the list viewport height.
    /// </summary>
    [Category("Layout")]
    public double ItemHeight
    {
        get => (double)GetValue(ItemHeightProperty);
        set => SetValue(ItemHeightProperty, value);
    }

    public static readonly DependencyProperty ItemHeightProperty =
        DependencyProperty.Register(
            nameof(ItemHeight),
            typeof(double),
            typeof(VVirtualList),
            new FrameworkPropertyMetadata(
                56d,
                FrameworkPropertyMetadataOptions.AffectsMeasure,
                OnItemHeightChanged));

    /// <summary>Gets or sets the width of horizontal cards.</summary>
    [Category("Layout")]
    public double ItemWidth
    {
        get => (double)GetValue(ItemWidthProperty);
        set => SetValue(ItemWidthProperty, value);
    }

    public static readonly DependencyProperty ItemWidthProperty =
        DependencyProperty.Register(
            nameof(ItemWidth),
            typeof(double),
            typeof(VVirtualList),
            new FrameworkPropertyMetadata(
                240d,
                FrameworkPropertyMetadataOptions.AffectsMeasure,
                OnItemWidthChanged));

    /// <summary>Gets or sets the height of items in grid mode.</summary>
    [Category("Layout")]
    public double GridItemHeight
    {
        get => (double)GetValue(GridItemHeightProperty);
        set => SetValue(GridItemHeightProperty, value);
    }

    public static readonly DependencyProperty GridItemHeightProperty =
        DependencyProperty.Register(
            nameof(GridItemHeight),
            typeof(double),
            typeof(VVirtualList),
            new FrameworkPropertyMetadata(96d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>
    /// Gets or sets the number of items per row in grid mode. The default
    /// <c>0</c> derives the column count from <see cref="GridMinItemWidth"/>.
    /// </summary>
    [Category("Layout")]
    public int GridColumns
    {
        get => (int)GetValue(GridColumnsProperty);
        set => SetValue(GridColumnsProperty, value);
    }

    public static readonly DependencyProperty GridColumnsProperty =
        DependencyProperty.Register(
            nameof(GridColumns),
            typeof(int),
            typeof(VVirtualList),
            new FrameworkPropertyMetadata(
                0,
                FrameworkPropertyMetadataOptions.AffectsMeasure,
                null,
                CoerceGridColumns));

    /// <summary>
    /// Gets or sets the preferred minimum width used to decide how many grid
    /// columns fit in the viewport. Items then stretch to fill those columns.
    /// </summary>
    [Category("Layout")]
    public double GridMinItemWidth
    {
        get => (double)GetValue(GridMinItemWidthProperty);
        set => SetValue(GridMinItemWidthProperty, value);
    }

    public static readonly DependencyProperty GridMinItemWidthProperty =
        DependencyProperty.Register(
            nameof(GridMinItemWidth),
            typeof(double),
            typeof(VVirtualList),
            new FrameworkPropertyMetadata(
                180d,
                FrameworkPropertyMetadataOptions.AffectsMeasure,
                null,
                CoerceMinItemWidth));

    #endregion

    #region Appearance

    /// <summary>Gets or sets whether the list itself draws a border.</summary>
    [Category("Appearance")]
    public bool ShowBorder
    {
        get => (bool)GetValue(ShowBorderProperty);
        set => SetValue(ShowBorderProperty, value);
    }

    public static readonly DependencyProperty ShowBorderProperty =
        DependencyProperty.Register(
            nameof(ShowBorder),
            typeof(bool),
            typeof(VVirtualList),
            new FrameworkPropertyMetadata(true, OnShowBorderChanged));

    /// <summary>Gets or sets the list border corner radius.</summary>
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
            typeof(VVirtualList),
            new FrameworkPropertyMetadata(new CornerRadius(8)));

    /// <summary>Gets or sets the normal item background.</summary>
    [Category("Appearance")]
    public Brush? ItemBackground
    {
        get => (Brush?)GetValue(ItemBackgroundProperty);
        set => SetValue(ItemBackgroundProperty, value);
    }

    public static readonly DependencyProperty ItemBackgroundProperty =
        DependencyProperty.Register(
            nameof(ItemBackground),
            typeof(Brush),
            typeof(VVirtualList),
            new FrameworkPropertyMetadata(null));

    /// <summary>Gets or sets the item background while the pointer is over it.</summary>
    [Category("Appearance")]
    public Brush? ItemHoverBackground
    {
        get => (Brush?)GetValue(ItemHoverBackgroundProperty);
        set => SetValue(ItemHoverBackgroundProperty, value);
    }

    public static readonly DependencyProperty ItemHoverBackgroundProperty =
        DependencyProperty.Register(
            nameof(ItemHoverBackground),
            typeof(Brush),
            typeof(VVirtualList),
            new FrameworkPropertyMetadata(null));

    /// <summary>Gets or sets the normal item border color.</summary>
    [Category("Appearance")]
    public Brush? ItemBorderBrush
    {
        get => (Brush?)GetValue(ItemBorderBrushProperty);
        set => SetValue(ItemBorderBrushProperty, value);
    }

    public static readonly DependencyProperty ItemBorderBrushProperty =
        DependencyProperty.Register(
            nameof(ItemBorderBrush),
            typeof(Brush),
            typeof(VVirtualList),
            new FrameworkPropertyMetadata(null));

    /// <summary>Gets or sets the background of selected items.</summary>
    [Category("Appearance")]
    public Brush? ItemSelectedBackground
    {
        get => (Brush?)GetValue(ItemSelectedBackgroundProperty);
        set => SetValue(ItemSelectedBackgroundProperty, value);
    }

    public static readonly DependencyProperty ItemSelectedBackgroundProperty =
        DependencyProperty.Register(
            nameof(ItemSelectedBackground),
            typeof(Brush),
            typeof(VVirtualList),
            new FrameworkPropertyMetadata(null));

    /// <summary>Gets or sets the border color of selected items.</summary>
    [Category("Appearance")]
    public Brush? ItemSelectedBorderBrush
    {
        get => (Brush?)GetValue(ItemSelectedBorderBrushProperty);
        set => SetValue(ItemSelectedBorderBrushProperty, value);
    }

    public static readonly DependencyProperty ItemSelectedBorderBrushProperty =
        DependencyProperty.Register(
            nameof(ItemSelectedBorderBrush),
            typeof(Brush),
            typeof(VVirtualList),
            new FrameworkPropertyMetadata(null));

    /// <summary>Gets or sets the item text color.</summary>
    [Category("Appearance")]
    public Brush? ItemForeground
    {
        get => (Brush?)GetValue(ItemForegroundProperty);
        set => SetValue(ItemForegroundProperty, value);
    }

    public static readonly DependencyProperty ItemForegroundProperty =
        DependencyProperty.Register(
            nameof(ItemForeground),
            typeof(Brush),
            typeof(VVirtualList),
            new FrameworkPropertyMetadata(null));

    /// <summary>Gets or sets the item corner radius.</summary>
    [Category("Appearance")]
    public CornerRadius ItemCornerRadius
    {
        get => (CornerRadius)GetValue(ItemCornerRadiusProperty);
        set => SetValue(ItemCornerRadiusProperty, value);
    }

    public static readonly DependencyProperty ItemCornerRadiusProperty =
        DependencyProperty.Register(
            nameof(ItemCornerRadius),
            typeof(CornerRadius),
            typeof(VVirtualList),
            new FrameworkPropertyMetadata(new CornerRadius(6)));

    /// <summary>Gets or sets the inner padding of each item.</summary>
    [Category("Appearance")]
    public Thickness ItemPadding
    {
        get => (Thickness)GetValue(ItemPaddingProperty);
        set => SetValue(ItemPaddingProperty, value);
    }

    public static readonly DependencyProperty ItemPaddingProperty =
        DependencyProperty.Register(
            nameof(ItemPadding),
            typeof(Thickness),
            typeof(VVirtualList),
            new FrameworkPropertyMetadata(new Thickness(10, 8, 10, 8)));

    /// <summary>Gets or sets the size of the optional leading icon.</summary>
    [Category("Appearance")]
    public double IconSize
    {
        get => (double)GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    public static readonly DependencyProperty IconSizeProperty =
        DependencyProperty.Register(
            nameof(IconSize),
            typeof(double),
            typeof(VVirtualList),
            new FrameworkPropertyMetadata(20d));

    /// <summary>Gets or sets whether selected items show a border.</summary>
    [Category("Appearance")]
    public bool ShowSelectionBorder
    {
        get => (bool)GetValue(ShowSelectionBorderProperty);
        set => SetValue(ShowSelectionBorderProperty, value);
    }

    public static readonly DependencyProperty ShowSelectionBorderProperty =
        DependencyProperty.Register(
            nameof(ShowSelectionBorder),
            typeof(bool),
            typeof(VVirtualList),
            new FrameworkPropertyMetadata(true));

    /// <summary>Gets or sets the scrollbar thumb color.</summary>
    [Category("Appearance")]
    public Brush? ScrollBarThumbBrush
    {
        get => (Brush?)GetValue(ScrollBarThumbBrushProperty);
        set => SetValue(ScrollBarThumbBrushProperty, value);
    }

    public static readonly DependencyProperty ScrollBarThumbBrushProperty =
        DependencyProperty.Register(
            nameof(ScrollBarThumbBrush),
            typeof(Brush),
            typeof(VVirtualList),
            new FrameworkPropertyMetadata(null));

    /// <summary>Gets or sets the scrollbar thumb color while hovered or dragged.</summary>
    [Category("Appearance")]
    public Brush? ScrollBarThumbHoverBrush
    {
        get => (Brush?)GetValue(ScrollBarThumbHoverBrushProperty);
        set => SetValue(ScrollBarThumbHoverBrushProperty, value);
    }

    public static readonly DependencyProperty ScrollBarThumbHoverBrushProperty =
        DependencyProperty.Register(
            nameof(ScrollBarThumbHoverBrush),
            typeof(Brush),
            typeof(VVirtualList),
            new FrameworkPropertyMetadata(null));

    #endregion

    /// <inheritdoc />
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        ApplyDisplayMode();
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size constraint)
    {
        var desired = base.MeasureOverride(constraint);

        // In an auto-sized host a horizontal list would otherwise report the
        // full width of all cards, and switching layout would visibly resize
        // the component. Keep a stable fallback width in that case; a finite
        // slot still fills normally.
        if (double.IsNaN(Width) && MinWidth > 0)
        {
            if (desired.Width > MinWidth)
            {
                desired.Width = MinWidth;
            }

            if (!double.IsInfinity(constraint.Width) && desired.Width > constraint.Width)
            {
                desired.Width = Math.Max(0, constraint.Width);
            }
        }

        return desired;
    }

    /// <inheritdoc />
    protected override DependencyObject GetContainerForItemOverride() => new VVirtualListItem();

    /// <inheritdoc />
    protected override bool IsItemItsOwnContainerOverride(object item) => item is VVirtualListItem;

    /// <inheritdoc />
    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
    {
        base.PrepareContainerForItemOverride(element, item);

        if (element is not VVirtualListItem container)
        {
            return;
        }

        // Set the primary axis before the virtualizing panel's first measure.
        // Relying on a style binding alone can make the panel estimate a
        // content-sized extent and leave the scrollbar thumb too short until
        // the user scrolls.
        if (ItemContainerStyle is not null
            && !ReferenceEquals(ItemContainerStyle, _appliedContainerStyle))
        {
            return;
        }

        switch (DisplayMode)
        {
            case VVirtualListDisplayMode.Vertical:
                container.ClearValue(WidthProperty);
                if (!double.IsNaN(ItemHeight) && ItemHeight > 0)
                {
                    container.Height = ItemHeight;
                }
                else
                {
                    container.ClearValue(HeightProperty);
                }

                container.Margin = new Thickness(0, 0, 0, Math.Max(0, Gap));
                break;

            case VVirtualListDisplayMode.Horizontal:
                if (!double.IsNaN(ItemWidth) && ItemWidth > 0)
                {
                    container.Width = ItemWidth;
                }
                else
                {
                    container.ClearValue(WidthProperty);
                }

                container.ClearValue(HeightProperty);
                container.Margin = new Thickness(0, 0, Math.Max(0, Gap), 0);
                break;

            default:
                container.ClearValue(HeightProperty);
                container.ClearValue(WidthProperty);
                container.Margin = new Thickness(0);
                break;
        }
    }

    private static void OnGapChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var list = (VVirtualList)d;
        list.UpdateRealizedContainerSizes();
        list.InvalidateMeasure();
    }

    private static void OnItemHeightChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var list = (VVirtualList)d;
        list.UpdateRealizedContainerSizes();
    }

    private static void OnItemWidthChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var list = (VVirtualList)d;
        list.UpdateRealizedContainerSizes();
    }

    private void UpdateRealizedContainerSizes()
    {
        if (ItemContainerStyle is not null
            && !ReferenceEquals(ItemContainerStyle, _appliedContainerStyle))
        {
            return;
        }

        if (VisualTreeHelper.GetChildrenCount(this) == 0)
        {
            return;
        }

        UpdateRealizedContainerSizes(this);
    }

    private void UpdateRealizedContainerSizes(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is VVirtualListItem item)
            {
                switch (DisplayMode)
                {
                    case VVirtualListDisplayMode.Vertical:
                        item.ClearValue(WidthProperty);
                        if (!double.IsNaN(ItemHeight))
                        {
                            item.Height = ItemHeight;
                        }
                        else
                        {
                            item.ClearValue(HeightProperty);
                        }

                        item.Margin = new Thickness(0, 0, 0, Math.Max(0, Gap));
                        break;

                    case VVirtualListDisplayMode.Horizontal:
                        if (!double.IsNaN(ItemWidth))
                        {
                            item.Width = ItemWidth;
                        }
                        else
                        {
                            item.ClearValue(WidthProperty);
                        }

                        item.ClearValue(HeightProperty);
                        item.Margin = new Thickness(0, 0, Math.Max(0, Gap), 0);
                        break;

                    default:
                        item.ClearValue(WidthProperty);
                        item.ClearValue(HeightProperty);
                        item.Margin = new Thickness(0);
                        break;
                }
            }

            if (child is VVirtualList)
            {
                continue;
            }

            UpdateRealizedContainerSizes(child);
        }
    }

    private static void OnDisplayModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((VVirtualList)d).ApplyDisplayMode();
    }

    private void ApplyDisplayMode()
    {
        var panelKey = DisplayMode switch
        {
            VVirtualListDisplayMode.Vertical => VerticalPanelKey,
            VVirtualListDisplayMode.Horizontal => HorizontalPanelKey,
            _ => GridPanelKey
        };

        if (LookupResource(panelKey) is ItemsPanelTemplate panel && !ReferenceEquals(ItemsPanel, panel))
        {
            ItemsPanel = panel;
        }

        var styleKey = DisplayMode switch
        {
            VVirtualListDisplayMode.Vertical => VerticalItemStyleKey,
            VVirtualListDisplayMode.Horizontal => HorizontalItemStyleKey,
            _ => GridItemStyleKey
        };

        if (LookupResource(styleKey) is not Style style)
        {
            return;
        }

        if (ItemContainerStyle is null || ReferenceEquals(ItemContainerStyle, _appliedContainerStyle))
        {
            ItemContainerStyle = style;
            _appliedContainerStyle = style;
        }
    }

    private object? LookupResource(string key)
    {
        return TryFindResource(key) ?? ControlResources.Value[key];
    }

    private static void OnShowBorderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var list = (VVirtualList)d;
        list.BorderThickness = (bool)e.NewValue ? new Thickness(1) : new Thickness(0);
    }

    private static object CoerceGridColumns(DependencyObject d, object baseValue)
    {
        return Math.Max(0, (int)baseValue);
    }

    private static object CoerceMinItemWidth(DependencyObject d, object baseValue)
    {
        var width = (double)baseValue;
        return double.IsNaN(width) || double.IsInfinity(width) ? 180d : Math.Max(1d, width);
    }
}
