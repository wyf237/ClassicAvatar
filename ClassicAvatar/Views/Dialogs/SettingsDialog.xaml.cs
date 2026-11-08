#nullable enable
// VControls modal settings: WZ data folder, save folder, language, icon cache.
using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using ClassicAvatar.Services;
using Microsoft.WindowsAPICodePack.Dialogs;
using VControls.Controls;
using VControls.Theming;

namespace ClassicAvatar
{
    public partial class SettingsDialog : VDialog
    {
        public SettingsDialog(string? dataFolder, string saveFolder, VLanguage language)
        {
            this.InitializeComponent();

            this.DataFolder = dataFolder;
            this.SaveFolder = saveFolder;
            this.SelectedLanguage = language;

            this.DataPathText.Text = string.IsNullOrWhiteSpace(dataFolder)
                ? this.TryFindResource("C2Loc.Settings.NoData") as string ?? "—"
                : dataFolder;
            this.SavePathText.Text = saveFolder;

            switch (language)
            {
                case VLanguage.ZhTW: this.LangZhTW.IsChecked = true; break;
                case VLanguage.EnUS: this.LangEnUS.IsChecked = true; break;
                default: this.LangZhCN.IsChecked = true; break;
            }

            this.CloseRequested += this.OnCloseRequested;
        }

        public string? DataFolder { get; private set; }

        public string SaveFolder { get; private set; }

        public VLanguage SelectedLanguage { get; private set; }

        private void OnCloseRequested(object? sender, bool confirmed)
        {
            if (!confirmed) return;

            this.SelectedLanguage = this.LangZhTW.IsChecked == true
                ? VLanguage.ZhTW
                : this.LangEnUS.IsChecked == true
                    ? VLanguage.EnUS
                    : VLanguage.ZhCN;
        }

        private void BtnChangeData_Click(object sender, RoutedEventArgs e)
        {
            string? folder = this.PickFolder(this.DataFolder);
            if (folder == null) return;

            this.DataFolder = folder;
            this.DataPathText.Text = folder;
        }

        private void BtnChangeSave_Click(object sender, RoutedEventArgs e)
        {
            string? folder = this.PickFolder(this.SaveFolder);
            if (folder == null) return;

            this.SaveFolder = folder;
            this.SavePathText.Text = folder;
        }

        private void BtnOpenSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(this.SaveFolder);
                Process.Start(new ProcessStartInfo
                {
                    FileName = this.SaveFolder,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                this.StatusText.Text = ex.Message;
            }
        }

        public Action? ClearCaches { get; set; }

        private void BtnClearCache_Click(object sender, RoutedEventArgs e)
        {
            if (this.ClearCaches != null)
            {
                this.ClearCaches();
            }
            else
            {
                IconLoader.ClearCache();
            }
            this.StatusText.Text = this.TryFindResource("C2Loc.Settings.CacheCleared") as string ?? "Cache cleared";
        }

        private string? PickFolder(string? current)
        {
            CommonOpenFileDialog dialog = new()
            {
                IsFolderPicker = true,
                Title = this.TryFindResource("C2Loc.Settings.PickFolder") as string ?? "Choose folder",
            };

            if (!string.IsNullOrWhiteSpace(current) && Directory.Exists(current))
            {
                dialog.InitialDirectory = current;
            }

            return dialog.ShowDialog() == CommonFileDialogResult.Ok ? dialog.FileName : null;
        }
    }
}
