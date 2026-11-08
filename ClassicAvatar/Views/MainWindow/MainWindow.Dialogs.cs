#nullable enable
// In-workbench VDialog helpers for alerts and confirmations.
using VControls.Controls;

namespace ClassicAvatar
{
    public partial class MainWindow
    {
        internal void ShowFatalError(Exception exception)
        {
            try
            {
                this.ShowAlert(
                    $"发生错误：{exception.Message}\n\n详细信息已写入本地日志。",
                    "程序错误");
            }
            catch (Exception)
            {
            }
        }

        private void ShowAlert(string message, string title)
        {
            VDialog dialog = new()
            {
                Title = title,
                Message = message,
                ConfirmText = this.C2("VLoc.Common.OK", "确定"),
                ShowCancelButton = false,
                DialogWidth = 420,
                CloseOnScrimClick = true,
                CloseOnEscape = true,
            };
            try
            {
                dialog.ShowDialog(this);
            }
            catch (Exception)
            {
                // Never let an alert failure take down the workbench.
            }
        }

        private bool ShowConfirm(
            string message,
            string title,
            string confirmText,
            string cancelText)
        {
            VDialog dialog = new()
            {
                Title = title,
                Message = message,
                ConfirmText = confirmText,
                CancelText = cancelText,
                ShowCancelButton = true,
                ShowConfirmButton = true,
                DialogWidth = 440,
                CloseOnScrimClick = false,
                CloseOnEscape = true,
            };
            try
            {
                return dialog.ShowDialog(this);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
