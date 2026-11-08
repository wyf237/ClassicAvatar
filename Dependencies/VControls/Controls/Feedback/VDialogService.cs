using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace VControls.Controls;

/// <summary>
/// Hosts <see cref="VDialog"/> cards in a modal scrim over an owner window.
/// The dialog is rendered inside the owner's adorner layer, so it follows the
/// window, participates in theme changes and never spawns a second HWND.
/// </summary>
public static class VDialogService
{
    /// <summary>
    /// Shows <paramref name="dialog"/> modally and blocks until it closes.
    /// Returns true when the confirm button or Enter was used.
    /// </summary>
    public static bool Show(Window owner, VDialog dialog)
    {
        var result = false;
        var frame = new DispatcherFrame();
        ShowCore(owner, dialog, value =>
        {
            result = value;
            frame.Continue = false;
        });

        Dispatcher.PushFrame(frame);
        return result;
    }

    /// <summary>
    /// Shows <paramref name="dialog"/> modally without blocking. The returned
    /// task completes with the dialog result.
    /// </summary>
    public static Task<bool> ShowAsync(Window owner, VDialog dialog)
    {
        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        ShowCore(owner, dialog, value => completion.TrySetResult(value));
        return completion.Task;
    }

    /// <summary>Convenience overload that builds a plain alert dialog.</summary>
    public static bool Show(
        Window owner,
        string title,
        string message,
        string confirmText = "保存",
        string cancelText = "取消")
    {
        return Show(owner, new VDialog
        {
            Title = title,
            Message = message,
            ConfirmText = confirmText,
            CancelText = cancelText
        });
    }

    /// <summary>Convenience overload that builds a plain alert dialog.</summary>
    public static Task<bool> ShowAsync(
        Window owner,
        string title,
        string message,
        string confirmText = "保存",
        string cancelText = "取消")
    {
        return ShowAsync(owner, new VDialog
        {
            Title = title,
            Message = message,
            ConfirmText = confirmText,
            CancelText = cancelText
        });
    }

    private static void ShowCore(Window owner, VDialog dialog, Action<bool> onClosed)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(dialog);

        if (!owner.IsLoaded)
        {
            throw new InvalidOperationException(
                "VDialogService requires a loaded owner window. Show the window before opening a dialog.");
        }

        var content = owner.Content as Visual;
        if (content is not FrameworkElement contentElement)
        {
            throw new InvalidOperationException(
                "VDialogService requires the owner window to expose a FrameworkElement as Content.");
        }

        dialog.MaxWidth = Math.Max(260, contentElement.ActualWidth - 48);

        var host = new VDialogHost(dialog);
        var layer = AdornerLayer.GetAdornerLayer(contentElement);

        if (layer is not null)
        {
            var adorner = new VDialogAdorner(contentElement, host);
            layer.Add(adorner);
            host.CloseRequested += (_, result) =>
            {
                layer.Remove(adorner);
                onClosed(result);
            };
        }
        else if (contentElement is Panel panel)
        {
            panel.Children.Add(host);
            host.CloseRequested += (_, result) =>
            {
                panel.Children.Remove(host);
                onClosed(result);
            };
        }
        else
        {
            throw new InvalidOperationException(
                "VDialogService could not find an AdornerLayer. Add an AdornerDecorator to the window content.");
        }

        host.Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() =>
            {
                host.Focus();
                Keyboard.Focus(dialog);
            }));
    }
}

/// <summary>
/// Full-surface overlay (scrim + centered card) shared by the adorner and the
/// panel fallback hosts.
/// </summary>
internal sealed class VDialogHost : Grid
{
    private readonly VDialog _dialog;
    private readonly Border _scrim;
    private readonly Grid _cardHost;
    private readonly IInputElement? _previousFocus;
    private bool _isClosing;

    public VDialogHost(VDialog dialog)
    {
        _dialog = dialog;
        _previousFocus = Keyboard.FocusedElement;

        Focusable = true;
        FocusVisualStyle = null;
        Background = Brushes.Transparent;
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
        KeyboardNavigation.SetDirectionalNavigation(this, KeyboardNavigationMode.Cycle);

        _scrim = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        _scrim.SetResourceReference(BackgroundProperty, "VBrush.Scrim");
        _scrim.MouseLeftButtonDown += OnScrimMouseDown;
        Children.Add(_scrim);

        _cardHost = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(24)
        };
        _cardHost.Children.Add(dialog);
        Children.Add(_cardHost);

        dialog.CloseRequested += OnDialogCloseRequested;

        Loaded += OnLoaded;
    }

    /// <summary>Raised after the exit animation when the overlay should be removed.</summary>
    public event EventHandler<bool>? CloseRequested;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Opacity = 0;
        var transform = new TransformGroup();
        var scale = new ScaleTransform(0.98, 0.98);
        var translate = new TranslateTransform(0, 8);
        transform.Children.Add(scale);
        transform.Children.Add(translate);
        _cardHost.RenderTransform = transform;
        _cardHost.RenderTransformOrigin = new Point(0.5, 0.5);

        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)) { EasingFunction = easing });
        scale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.98, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = easing });
        scale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.98, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = easing });
        translate.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = easing });
    }

    private void OnScrimMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_dialog.CloseOnScrimClick)
        {
            _dialog.RequestClose(false);
        }

        e.Handled = true;
    }

    private void OnDialogCloseRequested(object? sender, bool result)
    {
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        IsHitTestVisible = false;
        _dialog.CloseRequested -= OnDialogCloseRequested;

        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(120))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        fade.Completed += (_, _) =>
        {
            if (_previousFocus is UIElement element && element.IsVisible)
            {
                element.Focus();
            }

            CloseRequested?.Invoke(this, result);
        };

        BeginAnimation(OpacityProperty, fade);
    }
}

/// <summary>Adorner wrapper that lets <see cref="VDialogHost"/> cover the window content.</summary>
internal sealed class VDialogAdorner : Adorner
{
    private readonly VDialogHost _host;

    public VDialogAdorner(UIElement adornedElement, VDialogHost host)
        : base(adornedElement)
    {
        _host = host;
        IsHitTestVisible = true;
        ClipToBounds = true;
        AddVisualChild(host);
        AddLogicalChild(host);
    }

    protected override int VisualChildrenCount => 1;

    protected override Visual GetVisualChild(int index) => _host;

    protected override Size MeasureOverride(Size constraint)
    {
        // The adorner layer can be constrained to the whole window even when
        // the decorated element is an inner rounded frame. Use the adorned
        // element's actual size so the scrim can never extend past the frame.
        Size size = this.AdornedElement.RenderSize;
        _host.Measure(size);
        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Size size = this.AdornedElement.RenderSize;
        _host.Arrange(new Rect(size));
        return size;
    }
}
