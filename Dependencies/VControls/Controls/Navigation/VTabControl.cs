using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace VControls.Controls;

/// <summary>
/// Controls when the left/right tab scroll buttons are visible.
/// </summary>
public enum VTabScrollButtonVisibility
{
    /// <summary>Show the buttons only while the tab strip overflows.</summary>
    Overflow,

    /// <summary>Show the buttons only while the pointer is over the tab strip and it overflows.</summary>
    Hover
}

/// <summary>
/// Raised when a tab's close button is clicked. Set <see cref="RoutedEventArgs.Handled"/>
/// to true to keep the tab open and handle removal yourself (required when
/// <see cref="VTabControl.ItemsSource"/> is used).
/// </summary>
public sealed class VTabCloseRequestedEventArgs : RoutedEventArgs
{
    public VTabCloseRequestedEventArgs(VTabItem tab)
        : base(VTabControl.TabCloseRequestedEvent, tab)
    {
        Tab = tab;
    }

    /// <summary>The tab whose close button was clicked.</summary>
    public VTabItem Tab { get; }
}

/// <summary>Payload used while dragging a tab between VTabControl instances.</summary>
internal sealed class VTabDragData
{
    public VTabDragData(VTabControl source, VTabItem item)
    {
        Source = source;
        Item = item;
    }

    public VTabControl Source { get; }

    public VTabItem Item { get; }

    public bool Moved { get; set; }
}


/// <summary>
/// A closable tab strip with an optional per-tab icon, a green/gray active
/// status dot, and an add button that follows the last tab until the strip
/// overflows, then pins itself to the right edge.
/// </summary>
[TemplatePart(Name = TabsScrollPart, Type = typeof(ScrollViewer))]
[TemplatePart(Name = InlineAddButtonPart, Type = typeof(Button))]
[TemplatePart(Name = PinnedAddButtonPart, Type = typeof(Button))]
[TemplatePart(Name = TabStripPart, Type = typeof(Border))]
[TemplatePart(Name = ScrollLeftButtonPart, Type = typeof(Button))]
[TemplatePart(Name = ScrollRightButtonPart, Type = typeof(Button))]
public class VTabControl : TabControl
{
    internal const string TabDragDataFormat = "VControls.VTabItem";

    private const string TabsScrollPart = "PART_TabsScroll";
    private const string InlineAddButtonPart = "PART_InlineAddButton";
    private const string PinnedAddButtonPart = "PART_PinnedAddButton";
    private const string TabStripPart = "TabStrip";
    private const string ScrollLeftButtonPart = "PART_ScrollLeftButton";
    private const string ScrollRightButtonPart = "PART_ScrollRightButton";

    private ScrollViewer? _tabsScroll;
    private Button? _inlineAddButton;
    private Button? _pinnedAddButton;
    private Border? _tabStrip;
    private Button? _scrollLeftButton;
    private Button? _scrollRightButton;
    private bool _isTabStripHovered;
    private int _untitledCounter;
    private bool _autoWidthCandidate;
    private bool _widthLocked;

    static VTabControl()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(VTabControl),
            new FrameworkPropertyMetadata(typeof(VTabControl)));
    }

    public VTabControl()
    {
        AddHandler(VTabItem.CloseRequestedEvent, new RoutedEventHandler(OnItemCloseRequested));
        DragOver += OnDragOver;
        Drop += OnDrop;
        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;
    }

    #region Public API

    /// <summary>Raised before a default tab is added. Subscribe to supply custom tabs.</summary>
    public event EventHandler? AddTabRequested;

    /// <summary>Raised when a tab close is requested. Set Handled to keep the tab.</summary>
    public static readonly RoutedEvent TabCloseRequestedEvent =
        EventManager.RegisterRoutedEvent(
            nameof(TabCloseRequested),
            RoutingStrategy.Bubble,
            typeof(RoutedEventHandler),
            typeof(VTabControl));

    /// <summary>Raised when a tab close is requested.</summary>
    public event RoutedEventHandler TabCloseRequested
    {
        add => AddHandler(TabCloseRequestedEvent, value);
        remove => RemoveHandler(TabCloseRequestedEvent, value);
    }

    /// <summary>
    /// Adds a default tab when <see cref="ItemsSource"/> is not in use. When an
    /// add handler is subscribed, the handler owns tab creation instead.
    /// </summary>
    public bool AddTab()
    {
        if (ItemsSource is not null)
        {
            return false;
        }

        _untitledCounter++;
        var item = new VTabItem
        {
            Header = $"标签 {_untitledCounter}",
            Content = $"标签 {_untitledCounter} 内容"
        };

        Items.Add(item);
        SelectedItem = item;
        return true;
    }

    /// <summary>
    /// Removes a tab when <see cref="ItemsSource"/> is not in use and selects
    /// the nearest remaining tab when the closed tab was selected.
    /// </summary>
    public bool CloseTab(VTabItem tab)
    {
        if (ItemsSource is not null)
        {
            return false;
        }

        var index = Items.IndexOf(tab);
        if (index < 0)
        {
            return false;
        }

        var wasSelected = ReferenceEquals(SelectedItem, tab);
        Items.Remove(tab);

        if (wasSelected && Items.Count > 0)
        {
            SelectedIndex = Math.Min(index, Items.Count - 1);
        }

        return true;
    }

    #endregion

    #region Properties

    /// <summary>Gets or sets whether tab icons are shown by default.</summary>
    [Category("Appearance")]
    [Description("Global default for tab icon visibility.")]
    public bool ShowIcons
    {
        get => (bool)GetValue(ShowIconsProperty);
        set => SetValue(ShowIconsProperty, value);
    }

    public static readonly DependencyProperty ShowIconsProperty =
        DependencyProperty.Register(
            nameof(ShowIcons),
            typeof(bool),
            typeof(VTabControl),
            new FrameworkPropertyMetadata(true, OnShowIconsChanged));

    /// <summary>Gets or sets whether all tab close buttons are shown.</summary>
    [Category("Appearance")]
    [Description("Global default for tab close button visibility.")]
    public bool ShowCloseButtons
    {
        get => (bool)GetValue(ShowCloseButtonsProperty);
        set => SetValue(ShowCloseButtonsProperty, value);
    }

    public static readonly DependencyProperty ShowCloseButtonsProperty =
        DependencyProperty.Register(
            nameof(ShowCloseButtons),
            typeof(bool),
            typeof(VTabControl),
            new FrameworkPropertyMetadata(true));

    /// <summary>Gets or sets whether the add button is available.</summary>
    [Category("Appearance")]
    [Description("Shows the add-tab button.")]
    public bool ShowAddButton
    {
        get => (bool)GetValue(ShowAddButtonProperty);
        set => SetValue(ShowAddButtonProperty, value);
    }

    public static readonly DependencyProperty ShowAddButtonProperty =
        DependencyProperty.Register(
            nameof(ShowAddButton),
            typeof(bool),
            typeof(VTabControl),
            new FrameworkPropertyMetadata(true, OnShowAddButtonChanged));

    /// <summary>
    /// Gets or sets whether the left/right scroll buttons appear only while
    /// overflowing, or also only while the pointer is over the tab strip.
    /// </summary>
    [Category("Appearance")]
    [Description("Show scroll buttons when overflowing or only while hovering the tab strip.")]
    public VTabScrollButtonVisibility ScrollButtonVisibility
    {
        get => (VTabScrollButtonVisibility)GetValue(ScrollButtonVisibilityProperty);
        set => SetValue(ScrollButtonVisibilityProperty, value);
    }

    public static readonly DependencyProperty ScrollButtonVisibilityProperty =
        DependencyProperty.Register(
            nameof(ScrollButtonVisibility),
            typeof(VTabScrollButtonVisibility),
            typeof(VTabControl),
            new FrameworkPropertyMetadata(
                VTabScrollButtonVisibility.Overflow,
                OnScrollButtonVisibilityChanged));

    /// <summary>Gets or sets whether tabs can be dragged between VTabControl instances.</summary>
    [Category("Behaviour")]
    [Description("Allows tabs to be dragged between VTabControl instances.")]
    public bool AllowTabDrag
    {
        get => (bool)GetValue(AllowTabDragProperty);
        set => SetValue(AllowTabDragProperty, value);
    }

    public static readonly DependencyProperty AllowTabDragProperty =
        DependencyProperty.Register(
            nameof(AllowTabDrag),
            typeof(bool),
            typeof(VTabControl),
            new FrameworkPropertyMetadata(true));

    /// <summary>
    /// Gets or sets whether the control locks its first arranged width when it
    /// is placed in an auto-sized container. The default keeps the tab strip
    /// width stable while tabs are added or removed.
    /// </summary>
    [Category("Layout")]
    [Description("Locks the first arranged width in auto-sized containers.")]
    public bool LockAutoWidth
    {
        get => (bool)GetValue(LockAutoWidthProperty);
        set => SetValue(LockAutoWidthProperty, value);
    }

    public static readonly DependencyProperty LockAutoWidthProperty =
        DependencyProperty.Register(
            nameof(LockAutoWidth),
            typeof(bool),
            typeof(VTabControl),
            new FrameworkPropertyMetadata(true));

    /// <summary>Gets or sets the dot color used by the selected tab.</summary>
    [Category("Brushes")]
    [Description("Selected tab status dot color.")]
    public Brush? ActiveIndicatorBrush
    {
        get => (Brush?)GetValue(ActiveIndicatorBrushProperty);
        set => SetValue(ActiveIndicatorBrushProperty, value);
    }

    public static readonly DependencyProperty ActiveIndicatorBrushProperty =
        DependencyProperty.Register(
            nameof(ActiveIndicatorBrush),
            typeof(Brush),
            typeof(VTabControl),
            new FrameworkPropertyMetadata(null));

    /// <summary>Gets or sets the dot color used by unselected tabs.</summary>
    [Category("Brushes")]
    [Description("Unselected tab status dot color.")]
    public Brush? InactiveIndicatorBrush
    {
        get => (Brush?)GetValue(InactiveIndicatorBrushProperty);
        set => SetValue(InactiveIndicatorBrushProperty, value);
    }

    public static readonly DependencyProperty InactiveIndicatorBrushProperty =
        DependencyProperty.Register(
            nameof(InactiveIndicatorBrush),
            typeof(Brush),
            typeof(VTabControl),
            new FrameworkPropertyMetadata(null));

    #endregion

    #region Template

    /// <inheritdoc />
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (_tabsScroll is not null)
        {
            _tabsScroll.ScrollChanged -= OnTabsScrollChanged;
            _tabsScroll.PreviewMouseWheel -= OnTabsPreviewMouseWheel;
        }

        if (_inlineAddButton is not null)
        {
            _inlineAddButton.Click -= OnAddButtonClick;
        }

        if (_pinnedAddButton is not null)
        {
            _pinnedAddButton.Click -= OnAddButtonClick;
        }

        if (_scrollLeftButton is not null)
        {
            _scrollLeftButton.Click -= OnScrollLeftClick;
        }

        if (_scrollRightButton is not null)
        {
            _scrollRightButton.Click -= OnScrollRightClick;
        }

        if (_tabStrip is not null)
        {
            _tabStrip.MouseEnter -= OnTabStripMouseEnter;
            _tabStrip.MouseLeave -= OnTabStripMouseLeave;
        }

        _tabsScroll = GetTemplateChild(TabsScrollPart) as ScrollViewer;
        _inlineAddButton = GetTemplateChild(InlineAddButtonPart) as Button;
        _pinnedAddButton = GetTemplateChild(PinnedAddButtonPart) as Button;
        _scrollLeftButton = GetTemplateChild(ScrollLeftButtonPart) as Button;
        _scrollRightButton = GetTemplateChild(ScrollRightButtonPart) as Button;
        _tabStrip = GetTemplateChild(TabStripPart) as Border;

        if (_tabsScroll is not null)
        {
            _tabsScroll.ScrollChanged += OnTabsScrollChanged;
            _tabsScroll.PreviewMouseWheel += OnTabsPreviewMouseWheel;
        }

        if (_inlineAddButton is not null)
        {
            _inlineAddButton.Click += OnAddButtonClick;
        }

        if (_pinnedAddButton is not null)
        {
            _pinnedAddButton.Click += OnAddButtonClick;
        }

        if (_scrollLeftButton is not null)
        {
            _scrollLeftButton.Click += OnScrollLeftClick;
        }

        if (_scrollRightButton is not null)
        {
            _scrollRightButton.Click += OnScrollRightClick;
        }

        if (_tabStrip is not null)
        {
            _tabStrip.MouseEnter += OnTabStripMouseEnter;
            _tabStrip.MouseLeave += OnTabStripMouseLeave;
        }

        // TabControl can force the selected-content host to Center. Force the
        // content area to fill the available space so pages can stretch.
        if (GetTemplateChild("PART_SelectedContentHost") is ContentPresenter contentHost)
        {
            contentHost.HorizontalAlignment = HorizontalAlignment.Stretch;
            contentHost.VerticalAlignment = VerticalAlignment.Stretch;
        }

        UpdateAddButtonPlacement();
    }

    /// <inheritdoc />
    protected override void OnItemsChanged(NotifyCollectionChangedEventArgs e)
    {
        base.OnItemsChanged(e);

        if (SelectedIndex < 0 && Items.Count > 0)
        {
            SelectedIndex = 0;
        }

        ScheduleAddButtonUpdate();
    }

    /// <inheritdoc />
    protected override void OnSelectionChanged(SelectionChangedEventArgs e)
    {
        base.OnSelectionChanged(e);

        if (SelectedItem is VTabItem item)
        {
            Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(() => item.BringIntoView()));
        }
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size constraint)
    {
        // In auto-sized containers (horizontal StackPanel, Grid Auto column,
        // or an explicit non-Stretch alignment) a TabControl normally resizes
        // itself as tabs are added or removed. Lock the first arranged width
        // so the control and its tab strip stay stable; overflowing tabs are
        // handled by scrolling instead.
        _autoWidthCandidate = LockAutoWidth
            && double.IsNaN(Width)
            && Items.Count > 0
            && (double.IsInfinity(constraint.Width)
                || HorizontalAlignment != HorizontalAlignment.Stretch);

        var desired = base.MeasureOverride(constraint);

        // In a finite stretched slot, report the minimum width rather than the
        // tab-content width. This keeps star/auto grid columns from being
        // skewed by a tab strip that happens to contain many tabs.
        if (LockAutoWidth
            && double.IsNaN(Width)
            && HorizontalAlignment == HorizontalAlignment.Stretch
            && !double.IsInfinity(constraint.Width)
            && MinWidth > 0
            && desired.Width > MinWidth)
        {
            desired.Width = MinWidth;
        }

        // In a finite layout slot, never report a desired width larger than the
        // slot. Otherwise a tab strip with many tabs can force its Grid/Auto
        // column wider and shrink neighboring controls.
        if (!double.IsInfinity(constraint.Width) && desired.Width > constraint.Width)
        {
            desired.Width = Math.Max(0, constraint.Width);
        }

        return desired;
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var result = base.ArrangeOverride(finalSize);

        var rootOverflows = VisualTreeHelper.GetChildrenCount(this) > 0
            && VisualTreeHelper.GetChild(this, 0) is FrameworkElement root
            && root.DesiredSize.Width > finalSize.Width + 0.5;

        if (LockAutoWidth
            && double.IsNaN(Width)
            && Items.Count > 0
            && (_autoWidthCandidate || rootOverflows)
            && !_widthLocked
            && finalSize.Width > 0)
        {
            _widthLocked = true;
            var lockedWidth = finalSize.Width;

            Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(() =>
                {
                    if (double.IsNaN(Width) && Items.Count > 0)
                    {
                        SetCurrentValue(WidthProperty, lockedWidth);
                    }
                }));
        }

        return result;
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.Property == WidthProperty)
        {
            _widthLocked = false;
        }
    }

    /// <inheritdoc />
    protected override DependencyObject GetContainerForItemOverride() => new VTabItem();

    /// <inheritdoc />
    protected override bool IsItemItsOwnContainerOverride(object item) => item is VTabItem;

    /// <inheritdoc />
    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
    {
        base.PrepareContainerForItemOverride(element, item);

        // For ItemsSource usage, make simple data items render as tab headers
        // and selected content instead of producing blank tabs.
        if (element is VTabItem tab && item is not VTabItem)
        {
            tab.Header ??= item;
            tab.Content ??= item;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateAddButtonPlacement();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateAddButtonPlacement();
    }

    private void OnTabsScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        UpdateAddButtonPlacement();
    }

    private void OnTabsPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_tabsScroll is null || e.Delta == 0)
        {
            return;
        }

        if (_tabsScroll.ExtentWidth <= _tabsScroll.ViewportWidth + 0.5)
        {
            return;
        }

        _tabsScroll.ScrollToHorizontalOffset(_tabsScroll.HorizontalOffset - e.Delta);
        e.Handled = true;
    }

    private void OnAddButtonClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if (AddTabRequested is { } handler)
        {
            handler(this, EventArgs.Empty);
            return;
        }

        AddTab();
    }

    private void OnScrollLeftClick(object sender, RoutedEventArgs e)
    {
        ScrollTabs(-1);
        e.Handled = true;
    }

    private void OnScrollRightClick(object sender, RoutedEventArgs e)
    {
        ScrollTabs(1);
        e.Handled = true;
    }

    private void ScrollTabs(int direction)
    {
        if (_tabsScroll is null)
        {
            return;
        }

        var step = Math.Max(80, _tabsScroll.ViewportWidth * 0.85);
        var target = Math.Clamp(
            _tabsScroll.HorizontalOffset + (direction * step),
            0,
            _tabsScroll.ScrollableWidth);

        _tabsScroll.ScrollToHorizontalOffset(target);
    }

    private void OnTabStripMouseEnter(object sender, MouseEventArgs e)
    {
        _isTabStripHovered = true;
        UpdateScrollButtons();
    }

    private void OnTabStripMouseLeave(object sender, MouseEventArgs e)
    {
        _isTabStripHovered = false;
        UpdateScrollButtons();
    }

    private static void OnShowAddButtonChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((VTabControl)d).UpdateAddButtonPlacement();
    }

    private static void OnScrollButtonVisibilityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((VTabControl)d).UpdateScrollButtons();
    }

    private static void OnShowIconsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (VTabControl)d;
        for (var i = 0; i < control.Items.Count; i++)
        {
            if (control.ItemContainerGenerator.ContainerFromIndex(i) is VTabItem container)
            {
                container.RefreshIconVisibility();
            }
            else if (control.Items[i] is VTabItem item)
            {
                item.RefreshIconVisibility();
            }
        }
    }

    private void ScheduleAddButtonUpdate()
    {
        if (!IsLoaded)
        {
            return;
        }

        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(UpdateAddButtonPlacement));
    }

    private void UpdateAddButtonPlacement()
    {
        if (_tabsScroll is null || _inlineAddButton is null || _pinnedAddButton is null)
        {
            UpdateScrollButtons();
            return;
        }

        if (!ShowAddButton)
        {
            _inlineAddButton.Visibility = Visibility.Collapsed;
            _pinnedAddButton.Visibility = Visibility.Collapsed;
            UpdateScrollButtons();
            return;
        }

        // When the inline button is collapsed on overflow, extent is the tabs
        // width and the pinned button shrinks the viewport, so this comparison
        // remains stable at the switch threshold.
        var overflow = IsOverflowing();

        _inlineAddButton.Visibility = overflow ? Visibility.Collapsed : Visibility.Visible;
        _pinnedAddButton.Visibility = overflow ? Visibility.Visible : Visibility.Collapsed;
        UpdateScrollButtons();
    }

    private bool IsOverflowing()
    {
        if (_tabsScroll is null)
        {
            return false;
        }

        // Compare the tabs plus the add button against the full inner width,
        // regardless of whether the arrows/pinned button currently occupy
        // columns. This avoids a sticky overflow state after tabs are closed.
        var fullViewport = _tabsScroll.ViewportWidth;
        if (_scrollLeftButton?.Visibility == Visibility.Visible)
        {
            fullViewport += GetReservedWidth(_scrollLeftButton);
        }

        if (_scrollRightButton?.Visibility == Visibility.Visible)
        {
            fullViewport += GetReservedWidth(_scrollRightButton);
        }

        if (_pinnedAddButton?.Visibility == Visibility.Visible)
        {
            fullViewport += GetReservedWidth(_pinnedAddButton);
        }

        var contentWidth = _tabsScroll.ExtentWidth;
        if (ShowAddButton
            && _inlineAddButton is not null
            && _inlineAddButton.Visibility != Visibility.Visible)
        {
            contentWidth += GetReservedWidth(_inlineAddButton);
        }

        return contentWidth > fullViewport + 0.5;
    }

    private static double GetReservedWidth(FrameworkElement element)
    {
        var width = element.ActualWidth > 0 ? element.ActualWidth : element.Width;
        if (double.IsNaN(width) || double.IsInfinity(width))
        {
            width = 0;
        }

        return width + element.Margin.Left + element.Margin.Right;
    }

    private void UpdateScrollButtons()
    {
        if (_tabsScroll is null || _scrollLeftButton is null || _scrollRightButton is null)
        {
            return;
        }

        var overflow = IsOverflowing();
        var show = overflow
            && (ScrollButtonVisibility == VTabScrollButtonVisibility.Overflow || _isTabStripHovered);

        var canScrollLeft = _tabsScroll.HorizontalOffset > 0.5;
        var canScrollRight =
            _tabsScroll.HorizontalOffset + _tabsScroll.ViewportWidth < _tabsScroll.ExtentWidth - 0.5;

        var visibility = show ? Visibility.Visible : Visibility.Collapsed;
        _scrollLeftButton.Visibility = visibility;
        _scrollRightButton.Visibility = visibility;
        _scrollLeftButton.IsEnabled = canScrollLeft;
        _scrollRightButton.IsEnabled = canScrollRight;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (!AllowTabDrag || e.Data.GetData(TabDragDataFormat) is not VTabDragData data)
        {
            return;
        }

        if (ItemsSource is not null
            || data.Source.ItemsSource is not null
            || !data.Source.Items.Contains(data.Item))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (!AllowTabDrag || e.Data.GetData(TabDragDataFormat) is not VTabDragData data)
        {
            return;
        }

        if (ItemsSource is not null || data.Source.ItemsSource is not null)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var item = data.Item;
        var source = data.Source;
        var oldIndex = source.Items.IndexOf(item);
        if (oldIndex < 0)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var insertionIndex = GetInsertionIndex(e);
        var sourceWasSelected = ReferenceEquals(source.SelectedItem, item);

        if (ReferenceEquals(source, this))
        {
            if (insertionIndex > oldIndex)
            {
                insertionIndex--;
            }

            if (insertionIndex == oldIndex)
            {
                e.Effects = DragDropEffects.Move;
                e.Handled = true;
                return;
            }
        }

        source.Items.Remove(item);
        if (!ReferenceEquals(source, this) && sourceWasSelected && source.Items.Count > 0)
        {
            source.SelectedIndex = Math.Min(oldIndex, source.Items.Count - 1);
        }

        insertionIndex = Math.Clamp(insertionIndex, 0, Items.Count);
        Items.Insert(insertionIndex, item);
        SelectedItem = item;
        data.Moved = true;

        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private int GetInsertionIndex(DragEventArgs e)
    {
        var hit = InputHitTest(e.GetPosition(this)) as DependencyObject;
        var tab = hit is null
            ? null
            : ItemsControl.ContainerFromElement(this, hit) as VTabItem;

        if (tab is null && e.OriginalSource is DependencyObject source)
        {
            tab = ItemsControl.ContainerFromElement(this, source) as VTabItem;
        }

        if (tab is not null && Items.Contains(tab))
        {
            var index = Items.IndexOf(tab);
            var position = e.GetPosition(tab);
            return index + (position.X > tab.ActualWidth / 2 ? 1 : 0);
        }

        return Items.Count;
    }

    private void OnItemCloseRequested(object sender, RoutedEventArgs e)
    {
        var tab = e.OriginalSource as VTabItem ?? e.Source as VTabItem;
        if (tab is null)
        {
            return;
        }

        var args = new VTabCloseRequestedEventArgs(tab);
        RaiseEvent(args);

        if (!args.Handled)
        {
            CloseTab(tab);
        }

        e.Handled = true;
    }

    #endregion
}
