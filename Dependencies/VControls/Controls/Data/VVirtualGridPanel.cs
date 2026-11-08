using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace VControls.Controls;

/// <summary>
/// Virtualizing row-major grid panel used by <see cref="VVirtualList"/>.
/// Items flow left-to-right across a row and wrap to the next row; rows grow
/// downward and scroll vertically. Only visible rows are generated.
/// </summary>
public class VVirtualGridPanel : VirtualizingPanel, IScrollInfo
{
    private Size _extent;
    private Size _viewport;
    private Point _offset;
    private bool _canHorizontallyScroll;
    private bool _canVerticallyScroll;
    private ScrollViewer? _scrollOwner;

    private VVirtualList? _list;
    private int _columns = 1;
    private int _totalRows;
    private double _itemWidth = 1;
    private double _itemHeight = 96;
    private double _gap = 8;
    private double _stepX = 1;
    private double _stepY = 96;

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        _list = ItemsControl.GetItemsOwner(this) as VVirtualList;
        if (_list is null)
        {
            return default;
        }

        _gap = Math.Max(0, _list.Gap);
        _itemHeight = _list.GridItemHeight > 0 && !double.IsNaN(_list.GridItemHeight)
            ? _list.GridItemHeight
            : 96;

        var oldViewport = _viewport;
        _viewport = new Size(
            GetViewport(availableSize.Width, true),
            GetViewport(availableSize.Height, false));

        var count = _list.Items.Count;
        if (count == 0)
        {
            _columns = 1;
            _totalRows = 0;
            _extent = new Size(0, 0);
            _offset = new Point(0, 0);

            if (oldViewport != _viewport)
            {
                _scrollOwner?.InvalidateScrollInfo();
            }

            RemoveAllChildren();
            return _viewport;
        }

        var minItemWidth = _list.GridMinItemWidth;
        if (double.IsNaN(minItemWidth) || minItemWidth <= 0)
        {
            minItemWidth = 180;
        }

        var autoColumns = Math.Max(
            1,
            (int)Math.Floor((_viewport.Width + _gap) / (minItemWidth + _gap)));
        _columns = _list.GridColumns > 0
            ? _list.GridColumns
            : Math.Min(count, autoColumns);
        _columns = Math.Max(1, _columns);

        _totalRows = (int)Math.Ceiling((double)count / _columns);
        // Leave a small right gutter so the final card's border is not clipped
        // by the ScrollViewer viewport or covered by the scrollbar.
        const double rightGutter = 8;
        _itemWidth = Math.Max(
            1,
            (_viewport.Width - (_gap * (_columns - 1)) - rightGutter) / _columns);
        _stepX = _itemWidth + _gap;
        _stepY = _itemHeight + _gap;

        var oldExtent = _extent;
        _extent = new Size(
            _viewport.Width,
            (_totalRows * _stepY) - _gap);

        _offset.X = 0;
        CoerceOffsets();

        if (oldExtent.Width != _extent.Width
            || oldExtent.Height != _extent.Height
            || oldViewport.Width != _viewport.Width
            || oldViewport.Height != _viewport.Height)
        {
            _scrollOwner?.InvalidateScrollInfo();
        }

        RealizeItems(count);
        return _viewport;
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        for (var childIndex = 0; childIndex < InternalChildren.Count; childIndex++)
        {
            var child = InternalChildren[childIndex];
            var itemIndex = ItemContainerGenerator.IndexFromGeneratorPosition(
                new GeneratorPosition(childIndex, 0));

            if (itemIndex < 0)
            {
                continue;
            }

            var row = itemIndex / _columns;
            var column = itemIndex % _columns;
            var x = column * _stepX;
            var y = (row * _stepY) - _offset.Y;

            child.Arrange(new Rect(x, y, _itemWidth, _itemHeight));
        }

        return finalSize;
    }

    /// <inheritdoc />
    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
    {
        base.OnItemsChanged(sender, args);

        if (args.Action == NotifyCollectionChangedAction.Reset)
        {
            RemoveAllChildren();
        }

        InvalidateMeasure();
    }

    /// <inheritdoc />
    protected override void BringIndexIntoView(int index)
    {
        if (index < 0 || index >= (_list?.Items.Count ?? 0))
        {
            return;
        }

        var row = index / _columns;
        SetVerticalOffset(row * _stepY);
    }

    private void RealizeItems(int count)
    {
        var firstRow = Math.Max(0, (int)Math.Floor(_offset.Y / _stepY) - 1);
        var lastRow = Math.Min(
            _totalRows - 1,
            (int)Math.Ceiling((_offset.Y + _viewport.Height) / _stepY));

        firstRow = Math.Min(firstRow, _totalRows - 1);
        lastRow = Math.Max(firstRow, lastRow);

        var firstIndex = firstRow * _columns;
        var lastIndex = Math.Min(count - 1, ((lastRow + 1) * _columns) - 1);
        if (lastIndex < firstIndex)
        {
            RemoveAllChildren();
            return;
        }

        var generator = ItemContainerGenerator;
        if (generator is null)
        {
            Dispatcher.BeginInvoke(new Action(InvalidateMeasure), System.Windows.Threading.DispatcherPriority.Loaded);
            return;
        }

        var startPosition = generator.GeneratorPositionFromIndex(firstIndex);
        var childIndex = startPosition.Offset == 0
            ? startPosition.Index
            : startPosition.Index + 1;

        using (generator.StartAt(startPosition, GeneratorDirection.Forward, true))
        {
            for (var itemIndex = firstIndex; itemIndex <= lastIndex; itemIndex++, childIndex++)
            {
                var child = (UIElement)generator.GenerateNext(out var newlyRealized);
                if (newlyRealized)
                {
                    if (childIndex >= InternalChildren.Count)
                    {
                        AddInternalChild(child);
                    }
                    else
                    {
                        InsertInternalChild(childIndex, child);
                    }

                    generator.PrepareItemContainer(child);
                }

                child.Measure(new Size(_itemWidth, _itemHeight));
            }
        }

        CleanupItems(firstIndex, lastIndex);
    }

    private void CleanupItems(int firstIndex, int lastIndex)
    {
        for (var childIndex = InternalChildren.Count - 1; childIndex >= 0; childIndex--)
        {
            var position = new GeneratorPosition(childIndex, 0);
            var itemIndex = ItemContainerGenerator.IndexFromGeneratorPosition(position);
            if (itemIndex >= firstIndex && itemIndex <= lastIndex)
            {
                continue;
            }

            if (ItemContainerGenerator is IRecyclingItemContainerGenerator recycling)
            {
                recycling.Recycle(position, 1);
            }
            else
            {
                ItemContainerGenerator.Remove(position, 1);
            }

            RemoveInternalChildRange(childIndex, 1);
        }
    }

    private void RemoveAllChildren()
    {
        if (InternalChildren.Count > 0)
        {
            RemoveInternalChildRange(0, InternalChildren.Count);
        }
    }

    private double GetViewport(double available, bool horizontal)
    {
        if (_scrollOwner is not null)
        {
            var value = horizontal ? _scrollOwner.ViewportWidth : _scrollOwner.ViewportHeight;
            if (value > 0)
            {
                return value;
            }
        }

        if (!double.IsInfinity(available) && available > 0)
        {
            return available;
        }

        if (_list is not null)
        {
            var value = horizontal ? _list.ActualWidth : _list.ActualHeight;
            if (value > 0)
            {
                return value;
            }
        }

        return horizontal ? 800 : 600;
    }

    private void CoerceOffsets()
    {
        var maxY = Math.Max(0, _extent.Height - _viewport.Height);
        _offset.X = 0;
        _offset.Y = Math.Max(0, Math.Min(_offset.Y, maxY));
    }

    #region IScrollInfo

    /// <inheritdoc />
    public bool CanHorizontallyScroll
    {
        get => _canHorizontallyScroll;
        set => _canHorizontallyScroll = value;
    }

    /// <inheritdoc />
    public bool CanVerticallyScroll
    {
        get => _canVerticallyScroll;
        set => _canVerticallyScroll = value;
    }

    /// <inheritdoc />
    public double ExtentWidth => _extent.Width;

    /// <inheritdoc />
    public double ExtentHeight => _extent.Height;

    /// <inheritdoc />
    public double ViewportWidth => _viewport.Width;

    /// <inheritdoc />
    public double ViewportHeight => _viewport.Height;

    /// <inheritdoc />
    public double HorizontalOffset => 0;

    /// <inheritdoc />
    public double VerticalOffset => _offset.Y;

    /// <inheritdoc />
    public ScrollViewer ScrollOwner
    {
        get => _scrollOwner!;
        set => _scrollOwner = value;
    }

    /// <inheritdoc />
    public void LineUp() => SetVerticalOffset(_offset.Y - 24);

    /// <inheritdoc />
    public void LineDown() => SetVerticalOffset(_offset.Y + 24);

    /// <inheritdoc />
    public void LineLeft()
    {
    }

    /// <inheritdoc />
    public void LineRight()
    {
    }

    /// <inheritdoc />
    public void PageUp() => SetVerticalOffset(_offset.Y - (_viewport.Height * 0.9));

    /// <inheritdoc />
    public void PageDown() => SetVerticalOffset(_offset.Y + (_viewport.Height * 0.9));

    /// <inheritdoc />
    public void PageLeft()
    {
    }

    /// <inheritdoc />
    public void PageRight()
    {
    }

    /// <inheritdoc />
    public void MouseWheelUp() => LineUp();

    /// <inheritdoc />
    public void MouseWheelDown() => LineDown();

    /// <inheritdoc />
    public void MouseWheelLeft()
    {
    }

    /// <inheritdoc />
    public void MouseWheelRight()
    {
    }

    /// <inheritdoc />
    public void SetHorizontalOffset(double offset)
    {
    }

    /// <inheritdoc />
    public void SetVerticalOffset(double offset)
    {
        var max = Math.Max(0, _extent.Height - _viewport.Height);
        var value = Math.Max(0, Math.Min(offset, max));
        if (Math.Abs(value - _offset.Y) < 0.01)
        {
            return;
        }

        _offset.Y = value;
        InvalidateMeasure();
        _scrollOwner?.InvalidateScrollInfo();
    }

    /// <inheritdoc />
    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        var child = visual as UIElement;
        if (child is null)
        {
            return rectangle;
        }

        var childIndex = InternalChildren.IndexOf(child);
        if (childIndex < 0)
        {
            return rectangle;
        }

        var itemIndex = ItemContainerGenerator.IndexFromGeneratorPosition(
            new GeneratorPosition(childIndex, 0));
        if (itemIndex < 0)
        {
            return rectangle;
        }

        var row = itemIndex / _columns;
        var top = row * _stepY;
        var bottom = top + _itemHeight;

        if (top < _offset.Y)
        {
            SetVerticalOffset(top);
        }
        else if (bottom > _offset.Y + _viewport.Height)
        {
            SetVerticalOffset(bottom - _viewport.Height);
        }

        return rectangle;
    }

    #endregion
}
