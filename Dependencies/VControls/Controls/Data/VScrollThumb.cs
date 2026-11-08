using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace VControls.Controls;

/// <summary>
/// Custom scrollbar thumb that lays itself out from the owning ScrollBar's
/// value/extent. Unlike the stock Track, it keeps a fixed 24px thumb length
/// and can therefore draw both rounded ends cleanly at either end of the track.
/// </summary>
public class VScrollThumb : Thumb
{
    private ScrollBar? _bar;
    private ScrollViewer? _scrollViewer;
    private bool _attached;
    private double _thumbLength;
    private DependencyPropertyDescriptor? _maximumDescriptor;
    private DependencyPropertyDescriptor? _viewportDescriptor;

    static VScrollThumb()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(VScrollThumb),
            new FrameworkPropertyMetadata(typeof(VScrollThumb)));
    }

    /// <inheritdoc />
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        Attach();
    }

    private void Attach()
    {
        if (_attached)
        {
            return;
        }

        _bar = FindAncestor<ScrollBar>(this);
        if (_bar is null)
        {
            Loaded += OnLoadedRetry;
            return;
        }

        _scrollViewer = FindAncestor<ScrollViewer>(_bar);

        _attached = true;
        _bar.SizeChanged += OnBarSizeChanged;
        _bar.ValueChanged += OnBarValueChanged;
        _bar.MouseEnter += OnBarMouseEnter;
        _bar.MouseLeave += OnBarMouseLeave;
        _bar.PreviewMouseLeftButtonDown += OnBarPreviewMouseDown;
        DragDelta += OnDragDelta;

        _maximumDescriptor = DependencyPropertyDescriptor.FromProperty(
            ScrollBar.MaximumProperty,
            typeof(ScrollBar));
        _viewportDescriptor = DependencyPropertyDescriptor.FromProperty(
            ScrollBar.ViewportSizeProperty,
            typeof(ScrollBar));
        _maximumDescriptor?.AddValueChanged(_bar, OnBarMetricChanged);
        _viewportDescriptor?.AddValueChanged(_bar, OnBarMetricChanged);

        UpdateThumb();
    }

    private void OnLoadedRetry(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoadedRetry;
        Attach();
    }

    private void Detach()
    {
        if (!_attached)
        {
            return;
        }

        _attached = false;
        if (_bar is not null)
        {
            _bar.SizeChanged -= OnBarSizeChanged;
            _bar.ValueChanged -= OnBarValueChanged;
            _bar.MouseEnter -= OnBarMouseEnter;
            _bar.MouseLeave -= OnBarMouseLeave;
            _bar.PreviewMouseLeftButtonDown -= OnBarPreviewMouseDown;
            _maximumDescriptor?.RemoveValueChanged(_bar, OnBarMetricChanged);
            _viewportDescriptor?.RemoveValueChanged(_bar, OnBarMetricChanged);
        }

        DragDelta -= OnDragDelta;
    }

    private void OnBarSizeChanged(object sender, SizeChangedEventArgs e) => UpdateThumb();

    private void OnBarValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateThumb();

    private void OnBarMetricChanged(object? sender, EventArgs e) => UpdateThumb();

    private void OnBarMouseEnter(object sender, System.Windows.Input.MouseEventArgs e) => UpdateThumb();

    private void OnBarMouseLeave(object sender, System.Windows.Input.MouseEventArgs e) => UpdateThumb();

    private void OnBarPreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_bar is null || (e.OriginalSource is DependencyObject source && IsDescendantOf(source, this)))
        {
            return;
        }

        var point = e.GetPosition(_bar);
        var trackLength = _bar.Orientation == Orientation.Vertical ? _bar.ActualHeight : _bar.ActualWidth;
        var thumbStart = Canvas.GetTop(this);
        var thumbEnd = thumbStart + _thumbLength;
        var click = _bar.Orientation == Orientation.Vertical ? point.Y : point.X;

        var direction = click < thumbStart ? -1 : click > thumbEnd ? 1 : 0;
        if (direction == 0)
        {
            return;
        }

        SetScrollValue(_bar.Value + (direction * _bar.ViewportSize));
        e.Handled = true;
    }

    private void OnDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (_bar is null)
        {
            return;
        }

        var trackLength = _bar.Orientation == Orientation.Vertical ? _bar.ActualHeight : _bar.ActualWidth;
        var travel = Math.Max(1, trackLength - _thumbLength);
        var range = Math.Max(0, _bar.Maximum - _bar.Minimum);
        if (range <= 0)
        {
            return;
        }

        var delta = _bar.Orientation == Orientation.Vertical ? e.VerticalChange : e.HorizontalChange;
        var value = _bar.Value + ((delta / travel) * range);
        SetScrollValue(value);
        e.Handled = true;
    }

    private void SetScrollValue(double value)
    {
        if (_bar is null)
        {
            return;
        }

        var clamped = Math.Max(_bar.Minimum, Math.Min(_bar.Maximum, value));
        _bar.Value = clamped;

        if (_scrollViewer is null)
        {
            return;
        }

        if (_bar.Orientation == Orientation.Vertical)
        {
            _scrollViewer.ScrollToVerticalOffset(clamped);
        }
        else
        {
            _scrollViewer.ScrollToHorizontalOffset(clamped);
        }
    }

    private void UpdateThumb()
    {
        if (_bar is null)
        {
            return;
        }

        var vertical = _bar.Orientation == Orientation.Vertical;
        var trackLength = vertical ? _bar.ActualHeight : _bar.ActualWidth;
        if (trackLength <= 0)
        {
            return;
        }

        var viewport = _bar.ViewportSize > 0 ? _bar.ViewportSize : trackLength;
        var maximum = Math.Max(0, _bar.Maximum - _bar.Minimum);
        var extent = maximum + viewport;
        _thumbLength = extent <= 0
            ? trackLength
            : Math.Max(24, Math.Min(trackLength, trackLength * viewport / extent));

        var width = _bar.IsMouseOver ? 8 : 2;
        if (vertical)
        {
            Width = width;
            Height = _thumbLength;
            Canvas.SetLeft(this, Math.Max(0, (_bar.ActualWidth - width) / 2));
            var position = maximum <= 0
                ? 0
                : (_bar.Value - _bar.Minimum) / maximum * (trackLength - _thumbLength);
            Canvas.SetTop(this, Math.Max(0, Math.Min(trackLength - _thumbLength, position)));
        }
        else
        {
            Width = _thumbLength;
            Height = width;
            Canvas.SetTop(this, Math.Max(0, (_bar.ActualHeight - width) / 2));
            var position = maximum <= 0
                ? 0
                : (_bar.Value - _bar.Minimum) / maximum * (trackLength - _thumbLength);
            Canvas.SetLeft(this, Math.Max(0, Math.Min(trackLength - _thumbLength, position)));
        }
    }

    private static bool IsDescendantOf(DependencyObject child, DependencyObject ancestor)
    {
        var current = child;
        while (current is not null)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private static T? FindAncestor<T>(DependencyObject child) where T : DependencyObject
    {
        var current = child;
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
