using System.Windows;
using System.Windows.Controls;

namespace VControls.Controls
{
    /// <summary>
    /// Attached properties used by the shared <see cref="TextBox"/> styles.
    /// </summary>
    public static class VTextBox
    {
        /// <summary>
        /// Placeholder shown while the box is empty and not focused. The template
        /// renders it inline, so it disappears as soon as the user clicks in.
        /// </summary>
        public static readonly DependencyProperty PlaceholderProperty =
            DependencyProperty.RegisterAttached(
                "Placeholder",
                typeof(string),
                typeof(VTextBox),
                new FrameworkPropertyMetadata(string.Empty));

        public static string GetPlaceholder(DependencyObject element) =>
            (string)element.GetValue(PlaceholderProperty);

        public static void SetPlaceholder(DependencyObject element, string value) =>
            element.SetValue(PlaceholderProperty, value);
    }
}
