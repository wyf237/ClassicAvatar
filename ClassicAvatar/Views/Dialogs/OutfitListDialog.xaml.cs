#nullable enable
// VControls modal: five-row outfit list with text/image clipboard export.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VControls.Controls;

namespace ClassicAvatar
{
    public partial class OutfitListDialog : VDialog
    {
        private readonly string _outfitName;
        private readonly IReadOnlyList<OutfitRowVm> _rows;

        public OutfitListDialog(
            string outfitName,
            IReadOnlyList<OutfitRowVm> rows,
            ImageSource? preview = null,
            ImageSource? portrait = null)
        {
            this.InitializeComponent();

            this._outfitName = string.IsNullOrWhiteSpace(outfitName)
                ? this.TryFindResource("VLoc.Doc.Untitled") as string ?? "Outfit"
                : outfitName;
            this._rows = rows;

            this.OutfitTitleText.Text = this._outfitName;
            this.OutfitRows.ItemsSource = rows;

            if (preview != null)
            {
                this.OutfitPreview.Source = preview;
            }
            else
            {
                this.PreviewFrame.Visibility = Visibility.Collapsed;
            }

            if (portrait != null)
            {
                this.OutfitPortrait.Source = portrait;
            }
            else
            {
                this.PortraitFrame.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnCopyText_Click(object sender, RoutedEventArgs e)
        {
            StringBuilder text = new();
            text.AppendLine(this._outfitName);
            foreach (OutfitRowVm row in this._rows)
            {
                text.AppendLine($"{row.SlotName}\t{row.IdText}\t{row.Name}");
            }

            this.Copy(() => Clipboard.SetText(text.ToString()));
        }

        private void BtnCopyImage_Click(object sender, RoutedEventArgs e)
        {
            // Capture the whole content area: the slot list and both previews.
            this.ContentRoot.UpdateLayout();

            int width = (int)Math.Ceiling(this.ContentRoot.ActualWidth);
            int height = (int)Math.Ceiling(this.ContentRoot.ActualHeight);
            if (width <= 0 || height <= 0)
            {
                return;
            }

            RenderTargetBitmap bitmap = new(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(this.ContentRoot);
            this.Copy(() => Clipboard.SetImage(bitmap));
        }

        /// <summary>Clipboard can be briefly locked by another process; retry.</summary>
        private void Copy(Action action)
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                try
                {
                    action();
                    this.FeedbackText.Text = this.TryFindResource("C2Loc.Outfit.Copied") as string ?? "Copied";
                    return;
                }
                catch (ExternalException)
                {
                    Thread.Sleep(50);
                }
            }

            this.FeedbackText.Text = this.TryFindResource("C2Loc.Outfit.CopyFailed") as string ?? "Copy failed";
        }

        private void BtnDialogClose_Click(object sender, RoutedEventArgs e) => this.Close(false);
    }
}
