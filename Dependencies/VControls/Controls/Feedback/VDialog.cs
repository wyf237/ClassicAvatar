using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace VControls.Controls;

/// <summary>
/// A shadcn-style alert dialog card. The control contains the visual card and
/// its footer buttons; <see cref="VDialogService"/> hosts it in a modal scrim
/// over the owner window. The default footer shows a neutral cancel button and
/// a primary confirm button.
/// </summary>
public class VDialog : ContentControl
{
    private const string ConfirmButtonPart = "PART_ConfirmButton";
    private const string CancelButtonPart = "PART_CancelButton";

    private Button? _confirmButton;
    private Button? _cancelButton;
    private bool _isClosing;

    static VDialog()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(VDialog),
            new FrameworkPropertyMetadata(typeof(VDialog)));
    }

    public VDialog()
    {
        Focusable = true;
        FocusVisualStyle = null;
        Loaded += OnLoaded;
    }

    #region Content

    /// <summary>Gets or sets the dialog title.</summary>
    [Category("Content")]
    [Description("Dialog title.")]
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(
            nameof(Title),
            typeof(string),
            typeof(VDialog),
            new FrameworkPropertyMetadata(string.Empty));

    /// <summary>Gets or sets the supporting copy shown below the title.</summary>
    [Category("Content")]
    [Description("Supporting copy shown below the title.")]
    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public static readonly DependencyProperty MessageProperty =
        DependencyProperty.Register(
            nameof(Message),
            typeof(string),
            typeof(VDialog),
            new FrameworkPropertyMetadata(string.Empty));

    /// <summary>Gets or sets the cancel button label. Defaults to "取消".</summary>
    [Category("Content")]
    [Description("Cancel button label.")]
    public string CancelText
    {
        get => (string)GetValue(CancelTextProperty);
        set => SetValue(CancelTextProperty, value);
    }

    public static readonly DependencyProperty CancelTextProperty =
        DependencyProperty.Register(
            nameof(CancelText),
            typeof(string),
            typeof(VDialog),
            new FrameworkPropertyMetadata("取消"));

    /// <summary>Gets or sets the confirm button label. Defaults to "保存".</summary>
    [Category("Content")]
    [Description("Confirm button label.")]
    public string ConfirmText
    {
        get => (string)GetValue(ConfirmTextProperty);
        set => SetValue(ConfirmTextProperty, value);
    }

    public static readonly DependencyProperty ConfirmTextProperty =
        DependencyProperty.Register(
            nameof(ConfirmText),
            typeof(string),
            typeof(VDialog),
            new FrameworkPropertyMetadata("保存"));

    #endregion

    #region Layout

    /// <summary>Gets or sets the card width.</summary>
    [Category("Appearance")]
    [Description("Dialog card width.")]
    public double DialogWidth
    {
        get => (double)GetValue(DialogWidthProperty);
        set => SetValue(DialogWidthProperty, value);
    }

    public static readonly DependencyProperty DialogWidthProperty =
        DependencyProperty.Register(
            nameof(DialogWidth),
            typeof(double),
            typeof(VDialog),
            new FrameworkPropertyMetadata(
                440d,
                null,
                static (_, baseValue) => Math.Max(240d, (double)baseValue)));

    /// <summary>Gets or sets the card corner radius. Kept intentionally small.</summary>
    [Category("Appearance")]
    [Description("Dialog card corner radius.")]
    public CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.Register(
            nameof(CornerRadius),
            typeof(CornerRadius),
            typeof(VDialog),
            new FrameworkPropertyMetadata(new CornerRadius(6)));

    /// <summary>Gets or sets whether the cancel button is visible.</summary>
    [Category("Appearance")]
    [Description("Shows the cancel button.")]
    public bool ShowCancelButton
    {
        get => (bool)GetValue(ShowCancelButtonProperty);
        set => SetValue(ShowCancelButtonProperty, value);
    }

    public static readonly DependencyProperty ShowCancelButtonProperty =
        DependencyProperty.Register(
            nameof(ShowCancelButton),
            typeof(bool),
            typeof(VDialog),
            new FrameworkPropertyMetadata(true));

    /// <summary>Gets or sets whether the confirm button is visible.</summary>
    [Category("Appearance")]
    [Description("Shows the confirm button.")]
    public bool ShowConfirmButton
    {
        get => (bool)GetValue(ShowConfirmButtonProperty);
        set => SetValue(ShowConfirmButtonProperty, value);
    }

    public static readonly DependencyProperty ShowConfirmButtonProperty =
        DependencyProperty.Register(
            nameof(ShowConfirmButton),
            typeof(bool),
            typeof(VDialog),
            new FrameworkPropertyMetadata(true));

    #endregion

    #region Behaviour

    /// <summary>Gets or sets whether clicking the scrim closes the dialog with a cancel result.</summary>
    [Category("Behaviour")]
    [Description("Clicking outside the card cancels the dialog.")]
    public bool CloseOnScrimClick
    {
        get => (bool)GetValue(CloseOnScrimClickProperty);
        set => SetValue(CloseOnScrimClickProperty, value);
    }

    public static readonly DependencyProperty CloseOnScrimClickProperty =
        DependencyProperty.Register(
            nameof(CloseOnScrimClick),
            typeof(bool),
            typeof(VDialog),
            new FrameworkPropertyMetadata(true));

    /// <summary>Gets or sets whether the Escape key cancels the dialog.</summary>
    [Category("Behaviour")]
    [Description("Escape cancels the dialog.")]
    public bool CloseOnEscape
    {
        get => (bool)GetValue(CloseOnEscapeProperty);
        set => SetValue(CloseOnEscapeProperty, value);
    }

    public static readonly DependencyProperty CloseOnEscapeProperty =
        DependencyProperty.Register(
            nameof(CloseOnEscape),
            typeof(bool),
            typeof(VDialog),
            new FrameworkPropertyMetadata(true));

    /// <summary>Gets the last result raised by this dialog.</summary>
    [Browsable(false)]
    public bool? Result { get; private set; }

    /// <summary>Raised when a footer button, Escape or the scrim requests a close.</summary>
    public event EventHandler<bool>? CloseRequested;

    /// <summary>Shows the dialog modally over <paramref name="owner"/> and blocks until it closes.</summary>
    public bool ShowDialog(Window owner) => VDialogService.Show(owner, this);

    /// <summary>Shows the dialog over <paramref name="owner"/> without blocking.</summary>
    public Task<bool> ShowDialogAsync(Window owner) => VDialogService.ShowAsync(owner, this);

    internal void RequestClose(bool confirmed)
    {
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        Result = confirmed;
        CloseRequested?.Invoke(this, confirmed);
    }

    /// <summary>Closes the dialog programmatically with the supplied result.</summary>
    public void Close(bool confirmed = false) => RequestClose(confirmed);

    #endregion

    #region Button brushes

    /// <summary>Gets or sets the confirm button background.</summary>
    [Category("Brushes")]
    [Description("Confirm button background.")]
    public Brush? ConfirmBackground
    {
        get => (Brush?)GetValue(ConfirmBackgroundProperty);
        set => SetValue(ConfirmBackgroundProperty, value);
    }

    public static readonly DependencyProperty ConfirmBackgroundProperty =
        RegisterBrush(nameof(ConfirmBackground));

    /// <summary>Gets or sets the confirm button text color.</summary>
    [Category("Brushes")]
    [Description("Confirm button text color.")]
    public Brush? ConfirmForeground
    {
        get => (Brush?)GetValue(ConfirmForegroundProperty);
        set => SetValue(ConfirmForegroundProperty, value);
    }

    public static readonly DependencyProperty ConfirmForegroundProperty =
        RegisterBrush(nameof(ConfirmForeground));

    /// <summary>Gets or sets the confirm button border color.</summary>
    [Category("Brushes")]
    [Description("Confirm button border color.")]
    public Brush? ConfirmBorderBrush
    {
        get => (Brush?)GetValue(ConfirmBorderBrushProperty);
        set => SetValue(ConfirmBorderBrushProperty, value);
    }

    public static readonly DependencyProperty ConfirmBorderBrushProperty =
        RegisterBrush(nameof(ConfirmBorderBrush));

    /// <summary>Gets or sets the cancel button background.</summary>
    [Category("Brushes")]
    [Description("Cancel button background.")]
    public Brush? CancelBackground
    {
        get => (Brush?)GetValue(CancelBackgroundProperty);
        set => SetValue(CancelBackgroundProperty, value);
    }

    public static readonly DependencyProperty CancelBackgroundProperty =
        RegisterBrush(nameof(CancelBackground));

    /// <summary>Gets or sets the cancel button text color.</summary>
    [Category("Brushes")]
    [Description("Cancel button text color.")]
    public Brush? CancelForeground
    {
        get => (Brush?)GetValue(CancelForegroundProperty);
        set => SetValue(CancelForegroundProperty, value);
    }

    public static readonly DependencyProperty CancelForegroundProperty =
        RegisterBrush(nameof(CancelForeground));

    /// <summary>Gets or sets the cancel button border color.</summary>
    [Category("Brushes")]
    [Description("Cancel button border color.")]
    public Brush? CancelBorderBrush
    {
        get => (Brush?)GetValue(CancelBorderBrushProperty);
        set => SetValue(CancelBorderBrushProperty, value);
    }

    public static readonly DependencyProperty CancelBorderBrushProperty =
        RegisterBrush(nameof(CancelBorderBrush));

    #endregion

    #region Template state

    // ContentControl already exposes a read-only HasContent property; the
    // dialog template uses it to hide the custom body slot when Content is null.

    #endregion

    /// <inheritdoc />
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (_confirmButton is not null)
        {
            _confirmButton.Click -= OnConfirmClick;
        }

        if (_cancelButton is not null)
        {
            _cancelButton.Click -= OnCancelClick;
        }

        _confirmButton = GetTemplateChild(ConfirmButtonPart) as Button;
        _cancelButton = GetTemplateChild(CancelButtonPart) as Button;

        if (_confirmButton is not null)
        {
            _confirmButton.Click += OnConfirmClick;
        }

        if (_cancelButton is not null)
        {
            _cancelButton.Click += OnCancelClick;
        }
    }

    /// <inheritdoc />
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        if (e.Key == Key.Escape && CloseOnEscape)
        {
            RequestClose(false);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            var triggeredCancel = !ShowConfirmButton
                || (ShowCancelButton
                    && _cancelButton is not null
                    && ReferenceEquals(Keyboard.FocusedElement, _cancelButton));

            RequestClose(!triggeredCancel);
            e.Handled = true;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // A dialog instance can be shown again after a previous close.
        _isClosing = false;
        Result = null;

        if (_confirmButton is { IsVisible: true })
        {
            _confirmButton.Focus();
        }
        else if (_cancelButton is { IsVisible: true })
        {
            _cancelButton.Focus();
        }
        else
        {
            Focus();
        }
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e) => RequestClose(true);

    private void OnCancelClick(object sender, RoutedEventArgs e) => RequestClose(false);

    private static DependencyProperty RegisterBrush(string name)
    {
        return DependencyProperty.Register(
            name,
            typeof(Brush),
            typeof(VDialog),
            new FrameworkPropertyMetadata(null));
    }
}
