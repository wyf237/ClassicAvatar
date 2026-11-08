#nullable enable
// First-launch Data-folder picker. No path is pre-filled: the folder must come
// from the user.
using System;
using System.Windows;
using Microsoft.WindowsAPICodePack.Dialogs;
using VControls.Controls;

namespace ClassicAvatar
{
    public partial class FirstRunDialog : VDialog
    {
        public FirstRunDialog()
        {
            this.InitializeComponent();
        }

        public string? SelectedFolder { get; private set; }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            CommonOpenFileDialog dialog = new()
            {
                IsFolderPicker = true,
                Title = this.TryFindResource("C2Loc.FirstRun.FolderTitle") as string
                    ?? "Choose the game Data folder",
            };

            if (dialog.ShowDialog() != CommonFileDialogResult.Ok)
            {
                return;
            }

            this.SelectedFolder = dialog.FileName;
            this.PathText.Text = dialog.FileName;
            this.PathHint.Visibility = Visibility.Collapsed;
            this.BtnContinue.IsEnabled = true;
            this.ErrorText.Visibility = Visibility.Collapsed;
        }

        private void BtnContinue_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(this.SelectedFolder))
            {
                this.ErrorText.Visibility = Visibility.Visible;
                return;
            }

            this.Close(true);
        }

        private void BtnLater_Click(object sender, RoutedEventArgs e) => this.Close(false);
    }
}
