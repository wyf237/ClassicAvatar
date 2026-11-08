using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace VControls.Controls;

/// <summary>
/// Controls whether a <see cref="VTabItem"/> icon is shown. <see cref="Inherit"/>
/// follows <see cref="VTabControl.ShowIcons"/>; the other values override the
/// global setting for that individual tab.
/// </summary>
public enum VTabIconVisibility
{
    /// <summary>Use the owning <see cref="VTabControl.ShowIcons"/> value.</summary>
    Inherit,

    /// <summary>Show the icon for this tab, even when the global setting is off.</summary>
    Visible,

    /// <summary>Hide the icon for this tab, even when the global setting is on.</summary>
    Collapsed
}

/// <summary>
/// A tab header with a status dot, an optional icon, a text label, and a close
/// button on the right. Add instances directly to <see cref="VTabControl.Items"/>
/// or use <see cref="VTabControl.AddTab"/>.
/// </summary>
[TemplatePart(Name = CloseButtonPart, Type = typeof(Button))]
public class VTabItem : TabItem
{
    private const string CloseButtonPart = "PART_CloseButton";

    private Button? _closeButton;
    private Point _dragStartPoint;
    private bool _canStartDrag;

    static VTabItem()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(VTabItem),
            new FrameworkPropertyMetadata(typeof(VTabItem)));
    }

    public VTabItem()
    {
        UpdateIsIconVisible();
        Loaded += (_, _) => UpdateIsIconVisible();
        PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
        PreviewMouseMove += OnPreviewMouseMove;
        PreviewMouseLeftButtonUp += OnPreviewMouseLeftButtonUp;
    }

    /// <summary>Raised when the close button is clicked. Bubbles to <see cref="VTabControl"/>.</summary>
    public static readonly RoutedEvent CloseRequestedEvent =
        EventManager.RegisterRoutedEvent(
            nameof(CloseRequested),
            RoutingStrategy.Bubble,
            typeof(RoutedEventHandler),
            typeof(VTabItem));

    /// <summary>Raised when the close button is clicked.</summary>
    public event RoutedEventHandler CloseRequested
    {
        add => AddHandler(CloseRequestedEvent, value);
        remove => RemoveHandler(CloseRequestedEvent, value);
    }

    /// <summary>Gets or sets the optional tab icon geometry.</summary>
    [Category("Icon")]
    [Description("Optional tab icon geometry.")]
    public Geometry? Icon
    {
        get => (Geometry?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(
            nameof(Icon),
            typeof(Geometry),
            typeof(VTabItem),
            new FrameworkPropertyMetadata(null, OnIconSourceChanged));

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
            typeof(VTabItem),
            new FrameworkPropertyMetadata(
                14d,
                null,
                static (_, baseValue) => Math.Max(0d, (double)baseValue)));

    /// <summary>
    /// Gets or sets per-tab icon visibility. The default inherits the owning
    /// <see cref="VTabControl.ShowIcons"/> value; Visible/Collapsed override it
    /// for this tab only.
    /// </summary>
    [Category("Icon")]
    [Description("Per-tab icon visibility override.")]
    public VTabIconVisibility IconVisibility
    {
        get => (VTabIconVisibility)GetValue(IconVisibilityProperty);
        set => SetValue(IconVisibilityProperty, value);
    }

    public static readonly DependencyProperty IconVisibilityProperty =
        DependencyProperty.Register(
            nameof(IconVisibility),
            typeof(VTabIconVisibility),
            typeof(VTabItem),
            new FrameworkPropertyMetadata(
                VTabIconVisibility.Inherit,
                OnIconSourceChanged));

    #region Read-only effective icon state

    private static readonly DependencyPropertyKey IsIconVisiblePropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(IsIconVisible),
            typeof(bool),
            typeof(VTabItem),
            new FrameworkPropertyMetadata(false));

    /// <summary>True when an icon exists and the effective visibility allows it.</summary>
    [Browsable(false)]
    public static readonly DependencyProperty IsIconVisibleProperty =
        IsIconVisiblePropertyKey.DependencyProperty;

    /// <summary>Effective icon visibility used by the template.</summary>
    [Browsable(false)]
    public bool IsIconVisible => (bool)GetValue(IsIconVisibleProperty);

    #endregion

    /// <summary>Gets or sets whether this tab shows its close button.</summary>
    [Category("Appearance")]
    [Description("Shows the close button for this tab.")]
    public bool ShowCloseButton
    {
        get => (bool)GetValue(ShowCloseButtonProperty);
        set => SetValue(ShowCloseButtonProperty, value);
    }

    public static readonly DependencyProperty ShowCloseButtonProperty =
        DependencyProperty.Register(
            nameof(ShowCloseButton),
            typeof(bool),
            typeof(VTabItem),
            new FrameworkPropertyMetadata(true));

    /// <inheritdoc />
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (_closeButton is not null)
        {
            _closeButton.Click -= OnCloseButtonClick;
        }

        _closeButton = GetTemplateChild(CloseButtonPart) as Button;
        if (_closeButton is not null)
        {
            _closeButton.Click += OnCloseButtonClick;
        }
    }

    private void OnCloseButtonClick(object sender, RoutedEventArgs e)
    {
        RaiseEvent(new RoutedEventArgs(CloseRequestedEvent, this));
        e.Handled = true;
    }

    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _canStartDrag = false;

        // Clicking the close button must not select the tab or start a drag.
        if (IsButtonSource(e.OriginalSource))
        {
            return;
        }

        var owner = FindOwner();
        if (owner is null)
        {
            return;
        }

        // A partially clipped close button can lose part of its hit area.
        // Treat the rightmost close-button-sized strip as a fallback close
        // target so closing near the visible edge remains reliable.
        var position = e.GetPosition(this);
        if (ShowCloseButton
            && owner.ShowCloseButtons
            && ActualWidth > 0
            && position.X >= ActualWidth - 34)
        {
            _canStartDrag = false;
            RaiseEvent(new RoutedEventArgs(CloseRequestedEvent, this));
            e.Handled = true;
            return;
        }

        if (!IsSelected)
        {
            IsSelected = true;
        }

        _dragStartPoint = position;
        _canStartDrag = owner.AllowTabDrag && owner.ItemsSource is null;
    }

    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_canStartDrag || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _canStartDrag = false;

        var owner = FindOwner();
        if (owner is null || !owner.AllowTabDrag || owner.ItemsSource is not null)
        {
            return;
        }

        var data = new VTabDragData(owner, this);
        DragDrop.DoDragDrop(
            this,
            new DataObject(VTabControl.TabDragDataFormat, data),
            DragDropEffects.Move);
    }

    private void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _canStartDrag = false;
    }

    private static bool IsButtonSource(object? source)
    {
        var current = source as DependencyObject;
        while (current is not null)
        {
            if (current is Button)
            {
                return true;
            }

            current = current is Visual
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return false;
    }

    /// <inheritdoc />
    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        UpdateIsIconVisible();
    }

    internal void RefreshIconVisibility() => UpdateIsIconVisible();

    private static void OnIconSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((VTabItem)d).UpdateIsIconVisible();
    }

    private void UpdateIsIconVisible()
    {
        var global = FindOwner()?.ShowIcons ?? true;
        var visible = IconVisibility switch
        {
            VTabIconVisibility.Visible => true,
            VTabIconVisibility.Collapsed => false,
            _ => global
        };

        SetValue(IsIconVisiblePropertyKey, Icon is not null && visible);
    }

    private VTabControl? FindOwner()
    {
        if (ItemsControl.ItemsControlFromItemContainer(this) is VTabControl owner)
        {
            return owner;
        }

        DependencyObject? current = VisualTreeHelper.GetParent(this);
        while (current is not null)
        {
            if (current is VTabControl tabControl)
            {
                return tabControl;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
