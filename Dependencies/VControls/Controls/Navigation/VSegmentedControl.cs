using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace VControls.Controls;

/// <summary>
/// An animated segmented control (shadcn Toggle Group style) with an equal
/// width sliding thumb. It derives from <see cref="ListBox"/>, so selection,
/// keyboard navigation and <see cref="Selector.SelectedIndex"/> behave like a
/// native selector. Add <see cref="VSegmentedItem"/> children to describe text,
/// icon, or icon + text segments.
/// </summary>
public class VSegmentedControl : ListBox
{
    private Border? _thumb;
    private FrameworkElement? _host;
    private TranslateTransform? _thumbTransform;
    private bool _thumbReady;
    private readonly List<VSegmentedItem> _defaultSegments = new();
    private bool _suppressDefaultGeneration;

    static VSegmentedControl()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(VSegmentedControl),
            new FrameworkPropertyMetadata(typeof(VSegmentedControl)));

        PaddingProperty.OverrideMetadata(
            typeof(VSegmentedControl),
            new FrameworkPropertyMetadata(
                new Thickness(3),
                FrameworkPropertyMetadataOptions.AffectsMeasure,
                OnMetricChanged));

        // Placeholder segments live in Items, so they must be cleared before
        // ItemsControl switches the item source. Coercion runs before the
        // ItemsSource transition, unlike OnItemsSourceChanged.
        ItemsControl.ItemsSourceProperty.OverrideMetadata(
            typeof(VSegmentedControl),
            new FrameworkPropertyMetadata(
                null,
                null,
                static (d, baseValue) =>
                {
                    var control = (VSegmentedControl)d;
                    if (baseValue is not null && control._defaultSegments.Count > 0)
                    {
                        control.RemoveDefaultSegments();
                    }

                    return baseValue;
                }));
    }

    public VSegmentedControl()
    {
        Loaded += OnLoaded;
        SelectionMode = SelectionMode.Single;
        UpdateContainerCornerRadius();
    }

    #region Appearance

    /// <summary>
    /// Gets or sets the segment radius. The default is 4px, matching the
    /// requested 2-5px initial corner treatment.
    /// </summary>
    [Category("Appearance")]
    [Description("Segment corner radius. Defaults to 4px.")]
    public CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.Register(
            nameof(CornerRadius),
            typeof(CornerRadius),
            typeof(VSegmentedControl),
            new FrameworkPropertyMetadata(new CornerRadius(4), OnMetricChanged));

    private static readonly DependencyPropertyKey ContainerCornerRadiusPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(ContainerCornerRadius),
            typeof(CornerRadius),
            typeof(VSegmentedControl),
            new FrameworkPropertyMetadata(new CornerRadius(7)));

    [Browsable(false)]
    public static readonly DependencyProperty ContainerCornerRadiusProperty =
        ContainerCornerRadiusPropertyKey.DependencyProperty;

    /// <summary>Outer track radius, derived from <see cref="CornerRadius"/> plus the padding.</summary>
    [Browsable(false)]
    public CornerRadius ContainerCornerRadius => (CornerRadius)GetValue(ContainerCornerRadiusProperty);

    /// <summary>Gets or sets whether the sliding thumb animates between segments.</summary>
    [Category("Behaviour")]
    [Description("Animates the sliding selection thumb.")]
    public bool AnimateSelection
    {
        get => (bool)GetValue(AnimateSelectionProperty);
        set => SetValue(AnimateSelectionProperty, value);
    }

    public static readonly DependencyProperty AnimateSelectionProperty =
        DependencyProperty.Register(
            nameof(AnimateSelection),
            typeof(bool),
            typeof(VSegmentedControl),
            new FrameworkPropertyMetadata(true));

    /// <summary>Gets or sets whether the first segment is selected automatically.</summary>
    [Category("Behaviour")]
    [Description("Selects the first segment when no selection exists.")]
    public bool AutoSelectFirst
    {
        get => (bool)GetValue(AutoSelectFirstProperty);
        set => SetValue(AutoSelectFirstProperty, value);
    }

    public static readonly DependencyProperty AutoSelectFirstProperty =
        DependencyProperty.Register(
            nameof(AutoSelectFirst),
            typeof(bool),
            typeof(VSegmentedControl),
            new FrameworkPropertyMetadata(true));

    /// <summary>
    /// Gets or sets how many placeholder segments are generated when no items
    /// are supplied. The default is 2, so an empty control still renders as a
    /// two-segment switch.
    /// </summary>
    [Category("Content")]
    [Description("Number of placeholder segments generated when Items is empty.")]
    public int SegmentCount
    {
        get => (int)GetValue(SegmentCountProperty);
        set => SetValue(SegmentCountProperty, value);
    }

    public static readonly DependencyProperty SegmentCountProperty =
        DependencyProperty.Register(
            nameof(SegmentCount),
            typeof(int),
            typeof(VSegmentedControl),
            new FrameworkPropertyMetadata(
                2,
                OnSegmentCountChanged,
                static (_, baseValue) => Math.Clamp((int)baseValue, 0, 24)));

    /// <summary>Gets or sets the format used for generated placeholder labels.</summary>
    [Category("Content")]
    [Description("Format string used for generated placeholder labels.")]
    public string SegmentTextFormat
    {
        get => (string)GetValue(SegmentTextFormatProperty);
        set => SetValue(SegmentTextFormatProperty, value);
    }

    public static readonly DependencyProperty SegmentTextFormatProperty =
        DependencyProperty.Register(
            nameof(SegmentTextFormat),
            typeof(string),
            typeof(VSegmentedControl),
            new FrameworkPropertyMetadata("选项 {0}"));

    /// <summary>Gets or sets the track background.</summary>
    [Category("Brushes")]
    [Description("Track background.")]
    public Brush? TrackBackground
    {
        get => (Brush?)GetValue(TrackBackgroundProperty);
        set => SetValue(TrackBackgroundProperty, value);
    }

    public static readonly DependencyProperty TrackBackgroundProperty =
        DependencyProperty.Register(
            nameof(TrackBackground),
            typeof(Brush),
            typeof(VSegmentedControl),
            new FrameworkPropertyMetadata(null));

    /// <summary>Gets or sets the sliding thumb background.</summary>
    [Category("Brushes")]
    [Description("Sliding thumb background.")]
    public Brush? ThumbBackground
    {
        get => (Brush?)GetValue(ThumbBackgroundProperty);
        set => SetValue(ThumbBackgroundProperty, value);
    }

    public static readonly DependencyProperty ThumbBackgroundProperty =
        DependencyProperty.Register(
            nameof(ThumbBackground),
            typeof(Brush),
            typeof(VSegmentedControl),
            new FrameworkPropertyMetadata(null));

    /// <summary>Gets or sets the sliding thumb border color.</summary>
    [Category("Brushes")]
    [Description("Sliding thumb border color.")]
    public Brush? ThumbBorderBrush
    {
        get => (Brush?)GetValue(ThumbBorderBrushProperty);
        set => SetValue(ThumbBorderBrushProperty, value);
    }

    public static readonly DependencyProperty ThumbBorderBrushProperty =
        DependencyProperty.Register(
            nameof(ThumbBorderBrush),
            typeof(Brush),
            typeof(VSegmentedControl),
            new FrameworkPropertyMetadata(null));

    /// <summary>Gets or sets the label color of the selected segment.</summary>
    [Category("Brushes")]
    [Description("Selected segment foreground.")]
    public Brush? SelectedForeground
    {
        get => (Brush?)GetValue(SelectedForegroundProperty);
        set => SetValue(SelectedForegroundProperty, value);
    }

    public static readonly DependencyProperty SelectedForegroundProperty =
        DependencyProperty.Register(
            nameof(SelectedForeground),
            typeof(Brush),
            typeof(VSegmentedControl),
            new FrameworkPropertyMetadata(null));

    /// <summary>Gets or sets the label color of unselected segments.</summary>
    [Category("Brushes")]
    [Description("Unselected segment foreground.")]
    public Brush? UnselectedForeground
    {
        get => (Brush?)GetValue(UnselectedForegroundProperty);
        set => SetValue(UnselectedForegroundProperty, value);
    }

    public static readonly DependencyProperty UnselectedForegroundProperty =
        DependencyProperty.Register(
            nameof(UnselectedForeground),
            typeof(Brush),
            typeof(VSegmentedControl),
            new FrameworkPropertyMetadata(null));

    #endregion

    #region Selection API

    /// <summary>Gets the selected <see cref="VSegmentedItem"/>, when the items are segment descriptors.</summary>
    [Browsable(false)]
    public VSegmentedItem? SelectedSegment => SelectedItem as VSegmentedItem;

    /// <summary>Raised after <see cref="Selector.SelectedIndex"/> changes.</summary>
    public event EventHandler<int>? SelectedIndexChanged;

    /// <summary>Selects the segment at <paramref name="index"/>.</summary>
    public void Select(int index)
    {
        if (index < 0 || index >= Items.Count)
        {
            SelectedIndex = -1;
            return;
        }

        SelectedIndex = index;
    }

    /// <summary>Selects the first segment.</summary>
    public void SelectFirst() => Select(0);

    #endregion

    #region Template & thumb

    /// <inheritdoc />
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (_host is not null)
        {
            _host.SizeChanged -= OnHostSizeChanged;
        }

        _thumb = GetTemplateChild("PART_Thumb") as Border;
        _host = GetTemplateChild("PART_Host") as FrameworkElement;

        if (_thumb is not null)
        {
            // Template freezables can be frozen by WPF, so replace a frozen
            // translate transform with a local, animatable instance.
            if (_thumb.RenderTransform is TranslateTransform existing && !existing.IsFrozen)
            {
                _thumbTransform = existing;
            }
            else
            {
                _thumbTransform = new TranslateTransform();
                _thumb.RenderTransform = _thumbTransform;
            }
        }

        if (_host is not null)
        {
            _host.SizeChanged += OnHostSizeChanged;
        }

        _thumbReady = _thumb is not null && _host is not null;
        UpdateThumb(animate: false);
    }

    /// <inheritdoc />
    protected override void OnSelectionChanged(SelectionChangedEventArgs e)
    {
        base.OnSelectionChanged(e);
        UpdateThumb(animate: IsLoaded && AnimateSelection);
        SelectedIndexChanged?.Invoke(this, SelectedIndex);
    }

    /// <inheritdoc />
    protected override void OnItemsChanged(NotifyCollectionChangedEventArgs e)
    {
        base.OnItemsChanged(e);

        // If real items arrive after the placeholders were generated, remove
        // the placeholders once the current collection notification finishes.
        if (!_suppressDefaultGeneration
            && _defaultSegments.Count > 0
            && e.Action == NotifyCollectionChangedAction.Add
            && e.NewItems is not null
            && e.NewItems.Cast<object>().Any(
                item => item is not VSegmentedItem segment || !_defaultSegments.Contains(segment)))
        {
            Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() =>
                {
                    RemoveDefaultSegments();
                    if (AutoSelectFirst && SelectedIndex < 0 && Items.Count > 0)
                    {
                        SelectedIndex = 0;
                    }

                    UpdateThumb(animate: false);
                }));
        }

        if (AutoSelectFirst && SelectedIndex < 0 && Items.Count > 0)
        {
            SelectedIndex = 0;
        }

        UpdateThumb(animate: false);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        EnsureDefaultSegments();

        if (AutoSelectFirst && SelectedIndex < 0 && Items.Count > 0)
        {
            SelectedIndex = 0;
        }

        UpdateThumb(animate: false);
    }

    private void EnsureDefaultSegments()
    {
        if (_suppressDefaultGeneration
            || ItemsSource is not null
            || Items.Count > 0
            || SegmentCount <= 0)
        {
            return;
        }

        var count = Math.Clamp(SegmentCount, 0, 24);
        _suppressDefaultGeneration = true;

        for (var i = 1; i <= count; i++)
        {
            var item = new VSegmentedItem
            {
                Text = string.Format(CultureInfo.CurrentCulture, SegmentTextFormat, i)
            };

            _defaultSegments.Add(item);
            Items.Add(item);
        }

        _suppressDefaultGeneration = false;

        if (AutoSelectFirst && count > 0 && SelectedIndex < 0)
        {
            SelectedIndex = 0;
        }
    }

    private void RemoveDefaultSegments()
    {
        if (_defaultSegments.Count == 0)
        {
            return;
        }

        _suppressDefaultGeneration = true;
        foreach (var item in _defaultSegments)
        {
            Items.Remove(item);
        }

        _defaultSegments.Clear();
        _suppressDefaultGeneration = false;
    }

    private static void OnSegmentCountChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (VSegmentedControl)d;
        if (control._defaultSegments.Count == 0)
        {
            return;
        }

        control.RemoveDefaultSegments();
        control.EnsureDefaultSegments();
    }

    private void OnHostSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateThumb(animate: false);
    }

    private void UpdateThumb(bool animate)
    {
        if (!_thumbReady || _thumb is null || _host is null)
        {
            return;
        }

        var count = Items.Count;
        var index = SelectedIndex;

        if (count <= 0 || index < 0 || _host.ActualWidth <= 0)
        {
            _thumb.Visibility = Visibility.Collapsed;
            return;
        }

        index = Math.Min(index, count - 1);

        var cellWidth = _host.ActualWidth / count;
        var targetWidth = Math.Max(0, cellWidth - 4);
        var targetX = (index * cellWidth) + 2;

        _thumb.Visibility = Visibility.Visible;

        if (!animate || !IsLoaded)
        {
            _thumb.BeginAnimation(FrameworkElement.WidthProperty, null);
            _thumb.Width = targetWidth;
            if (_thumbTransform is not null)
            {
                _thumbTransform.BeginAnimation(TranslateTransform.XProperty, null);
                _thumbTransform.X = targetX;
            }

            return;
        }

        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(220);

        _thumb.BeginAnimation(
            FrameworkElement.WidthProperty,
            new DoubleAnimation(targetWidth, duration) { EasingFunction = easing },
            HandoffBehavior.SnapshotAndReplace);

        _thumbTransform?.BeginAnimation(
            TranslateTransform.XProperty,
            new DoubleAnimation(targetX, duration) { EasingFunction = easing },
            HandoffBehavior.SnapshotAndReplace);
    }

    #endregion

    private static void OnMetricChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((VSegmentedControl)d).UpdateContainerCornerRadius();
    }

    private void UpdateContainerCornerRadius()
    {
        var radius = CornerRadius;
        var padding = Padding;

        SetValue(
            ContainerCornerRadiusPropertyKey,
            new CornerRadius(
                radius.TopLeft + padding.Left,
                radius.TopRight + padding.Top,
                radius.BottomRight + padding.Right,
                radius.BottomLeft + padding.Bottom));
    }
}
