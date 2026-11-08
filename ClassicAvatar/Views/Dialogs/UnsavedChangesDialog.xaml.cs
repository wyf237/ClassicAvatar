#nullable enable
// VControls modal for the three-way unsaved-changes prompt.
using System.Windows;
using VControls.Controls;

namespace ClassicAvatar
{
    public enum UnsavedChoice
    {
        Cancel,
        Save,
        Discard,
    }

    public partial class UnsavedChangesDialog : VDialog
    {
        public UnsavedChangesDialog(string title, string message)
        {
            this.InitializeComponent();
            this.Title = title;
            this.Message = message;
        }

        public UnsavedChoice Choice { get; private set; } = UnsavedChoice.Cancel;

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.Choice = UnsavedChoice.Cancel;
            this.Close(false);
        }

        private void BtnDiscard_Click(object sender, RoutedEventArgs e)
        {
            this.Choice = UnsavedChoice.Discard;
            this.Close(false);
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            this.Choice = UnsavedChoice.Save;
            this.Close(true);
        }
    }
}
