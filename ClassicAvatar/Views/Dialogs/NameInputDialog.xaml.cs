#nullable enable
// Small VDialog used by Save / Save As / F2 rename.
using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using VControls.Controls;

namespace ClassicAvatar
{
    public partial class NameInputDialog : VDialog
    {
        public NameInputDialog(string title, string message, string initialText, string confirmText)
        {
            this.InitializeComponent();
            this.Title = title;
            this.Message = message;
            this.BtnConfirm.Content = confirmText;
            this.Input.Text = initialText;
            this.Loaded += this.OnLoaded;
        }

        public string Value => this.Input.Text.Trim();

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            this.Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    this.Input.Focus();
                    this.Input.SelectAll();
                }),
                DispatcherPriority.Input);
        }

        private void Input_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                this.TryConfirm();
                e.Handled = true;
            }
        }

        private void BtnConfirm_Click(object sender, RoutedEventArgs e) => this.TryConfirm();

        private void BtnCancel_Click(object sender, RoutedEventArgs e) => this.Close(false);

        private void TryConfirm()
        {
            if (string.IsNullOrWhiteSpace(this.Input.Text))
            {
                this.ErrorText.Visibility = Visibility.Visible;
                this.Input.Focus();
                return;
            }

            this.Close(true);
        }
    }
}
