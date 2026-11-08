#nullable enable
// File menu, save/export commands and undo/redo entry points.
// Kept in its own partial file so the code-behind stays navigable.
using Avatar.Core.Assets;
using Avatar.Core.Export;
using Avatar.Core.Models;
using Avatar.Core.Rendering;
using Avatar.Core.Rules;
using ClassicAvatar.Services;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VControls.Controls;
using VControls.Theming;
using WzReader.Core.Codecs;


namespace ClassicAvatar
{
    public partial class MainWindow
    {
        #region 文件菜单
        private void MenuOpen_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new()
            {
                Title = "打开搭配",
                Filter = "Avatar 搭配 (*.avatar)|*.avatar|所有文件 (*.*)|*.*",
                InitialDirectory = Directory.Exists(this._libraryFolder) ? this._libraryFolder : null,
            };
            if (dialog.ShowDialog(this) == true)
            {
                this.LoadAvatarFile(dialog.FileName);
            }
        }

        private void MenuSave_Click(object sender, RoutedEventArgs e)
        {
            if (this._build == null)
            {
                this.ShowAlert("请先打开数据目录并搭配角色。", "提示");
                return;
            }

            string initial = string.IsNullOrWhiteSpace(this._activeDocument?.Title)
                ? this.FindString("VLoc.Doc.Untitled")
                : this._activeDocument!.Title;
            NameInputDialog dialog = new(
                this.C2("C2Loc.Save.Title", "保存搭配"),
                this.C2("C2Loc.Save.Message", "给这套搭配起个名字："),
                initial,
                this.C2("C2Loc.Save.Confirm", "保存"));
            if (!dialog.ShowDialog(this)) return;

            string title = dialog.Value;
            string path = Path.Combine(this._libraryFolder, SanitizeFileName(title) + ".avatar");
            if (File.Exists(path)
                && !string.Equals(path, this._documentPath, StringComparison.OrdinalIgnoreCase)
                && !this.ShowConfirm(
                    $"“{title}”已存在，是否覆盖？",
                    "覆盖搭配",
                    "覆盖",
                    this.C2("VLoc.Common.Cancel", "取消")))
            {
                return;
            }

            this.SetActiveTitle(title);
            try
            {
                Directory.CreateDirectory(this._libraryFolder);
                this.SaveTo(path);
            }
            catch (Exception ex)
            {
                this.ShowAlert(ex.Message, "保存失败");
            }
        }

        private void RenameCurrent()
        {
            if (this._activeDocument == null) return;

            NameInputDialog dialog = new(
                this.C2("C2Loc.Rename.Title", "重命名搭配"),
                this.C2("C2Loc.Rename.Message", "输入新的搭配名称："),
                this._activeDocument.Title,
                this.C2("C2Loc.Rename.Confirm", "重命名"));
            if (!dialog.ShowDialog(this)) return;

            string title = dialog.Value;
            if (string.Equals(title, this._activeDocument.Title, StringComparison.Ordinal))
            {
                return;
            }

            this.SetActiveTitle(title);
            this._activeDocument.IsDirty = true;
            this.SetDirty(true);
            this.UpdateTabHeader(this._activeDocument);
        }

        private void SetActiveTitle(string title)
        {
            if (this._activeDocument != null)
            {
                this._activeDocument.Title = title;
            }

            this.DocName.Text = title;
            if (this._editSide == EditSide.Left)
            {
                this.StageDocName.Text = title;
            }
            else
            {
                this.CompareDocName.Text = title;
            }
        }

        private void MenuSaveAs_Click(object sender, RoutedEventArgs e) => this.SaveAs();

        private void SaveAs()
        {
            if (this._build == null) return;
            Directory.CreateDirectory(this._libraryFolder);
            string title = string.IsNullOrWhiteSpace(this._activeDocument?.Title)
                ? this.FindString("VLoc.Doc.Untitled")
                : this._activeDocument!.Title;
            SaveFileDialog dialog = new()
            {
                Title = "另存搭配",
                Filter = "Avatar 搭配 (*.avatar)|*.avatar",
                InitialDirectory = this._libraryFolder,
                FileName = SanitizeFileName(title) + ".avatar",
            };
            if (dialog.ShowDialog(this) == true)
            {
                this.SaveTo(dialog.FileName);
            }
        }

        private static string SanitizeFileName(string name)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(invalid, '_');
            }
            return string.IsNullOrWhiteSpace(name) ? "Outfit" : name.Trim();
        }

        private void SaveTo(string path)
        {
            if (this._build == null) return;
            try
            {
                AvatarPack.Save(this._build, path);
                this._documentPath = path;
                this.DocName.Text = Path.GetFileNameWithoutExtension(path);
                this.LoadLibrary();
                this.SetDirty(false);
                this.CommitActiveDocument();
            }
            catch (Exception ex)
            {
                this.ShowAlert(ex.Message, "保存失败");
            }
        }

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            if (this.BtnExport.ContextMenu is ContextMenu menu)
            {
                menu.PlacementTarget = this.BtnExport;
                menu.IsOpen = true;
            }
        }

        private void MenuExportPng_Click(object sender, RoutedEventArgs e)
        {
            if (this._build == null) return;
            SaveFileDialog dialog = new()
            {
                Title = "导出 PNG",
                Filter = "PNG 图片 (*.png)|*.png",
                FileName = "avatar.png",
            };
            if (dialog.ShowDialog(this) == true)
            {
                try
                {
                    this.ExportFrameWithInfo(dialog.FileName);
                }
                catch (Exception ex)
                {
                    this.ShowAlert(ex.Message, "导出失败");
                }
            }
        }

        /// <summary>导出当前帧，并在右侧附上当前装备清单。</summary>
        private void ExportFrameWithInfo(string path)
        {
            if (this._build == null || this._data == null)
            {
                return;
            }

            AvatarFrame frame = AvatarRenderer.RenderFrame(this._build, this.CurrentAction, this._actionIndex);
            BitmapSource avatar = ToBitmapSource(frame.Pixels);
            int scale = PreviewZoom;
            int avatarWidth = Math.Max(1, avatar.PixelWidth * scale);
            int avatarHeight = Math.Max(1, avatar.PixelHeight * scale);
            const int panelWidth = 236;
            int width = avatarWidth + panelWidth;
            int height = Math.Max(avatarHeight, 360);

            FontFamily font = this.TryFindResource("VFont.Family") as FontFamily
                ?? new FontFamily("Microsoft YaHei UI");
            var typeface = new Typeface(font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            Brush text = new SolidColorBrush(Color.FromRgb(0x21, 0x1D, 0x2C));
            Brush secondary = new SolidColorBrush(Color.FromRgb(0x6E, 0x68, 0x80));
            Brush muted = new SolidColorBrush(Color.FromRgb(0x9A, 0x94, 0xA8));

            var visual = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xF5, 0xF2, 0xFB)), null, new Rect(0, 0, avatarWidth, height));
                dc.DrawImage(avatar, new Rect(0, 0, avatarWidth, avatarHeight));
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xFB, 0xFA, 0xFE)), null,
                    new Rect(avatarWidth, 0, panelWidth, height));

                dc.DrawText(new FormattedText(
                    "CLASSIC AVATAR", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    typeface, 14, text, 1.0), new Point(avatarWidth + 16, 18));
                dc.DrawText(new FormattedText(
                    this.FindString("VLoc.Equip.Title"), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    typeface, 10, secondary, 1.0), new Point(avatarWidth + 16, 40));

                double y = 66;
                foreach (EquipSlot slot in PanelSlots)
                {
                    int? id = this._slotSelections.TryGetValue(slot, out int? value) ? value : null;
                    string name = id is int itemId
                        ? this.ItemName(itemId)
                        : this.FindString("VLoc.Equip.NoSelection");
                    string line = $"{this.SlotName(slot)}  {name}";
                    if (id is int shownId)
                    {
                        line += $"  #{shownId:D5}";
                    }

                    FormattedText itemText = new(
                        line,
                        CultureInfo.CurrentUICulture,
                        FlowDirection.LeftToRight,
                        typeface,
                        9,
                        id.HasValue ? text : muted,
                        1.0)
                    {
                        MaxTextWidth = panelWidth - 32,
                        MaxTextHeight = 14,
                        Trimming = TextTrimming.CharacterEllipsis,
                    };
                    dc.DrawText(itemText, new Point(avatarWidth + 16, y));
                    y += 17;
                    if (y > height - 24)
                    {
                        break;
                    }
                }
            }

            RenderTargetBitmap bitmap = new(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            PngBitmapEncoder encoder = new();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using FileStream stream = File.Create(path);
            encoder.Save(stream);
        }

        private void MenuExportFrames_Click(object sender, RoutedEventArgs e)
        {
            if (this._build == null) return;
            Microsoft.WindowsAPICodePack.Dialogs.CommonOpenFileDialog dialog = new()
            {
                IsFolderPicker = true,
                Title = "选择帧图片输出目录",
            };
            if (dialog.ShowDialog() == Microsoft.WindowsAPICodePack.Dialogs.CommonFileDialogResult.Ok)
            {
                try
                {
                    AvatarExporter.ExportAction(this._build, this.CurrentAction, new AvatarExportOptions
                    {
                        OutputDirectory = dialog.FileName,
                        FileNamePrefix = "avatar",
                        Scale = PreviewZoom,
                    });
                }
                catch (Exception ex)
                {
                    this.ShowAlert(ex.Message, "导出失败");
                }
            }
        }

        private void MenuExportAvatar_Click(object sender, RoutedEventArgs e) => this.MenuSaveAs_Click(sender, e);

        /// <summary>把当前动作导出为多帧 GIF。</summary>
        private void MenuExportGif_Click(object sender, RoutedEventArgs e)
        {
            if (this._build == null) return;

            SaveFileDialog dialog = new()
            {
                Title = "导出 GIF 动图",
                Filter = "GIF 动图 (*.gif)|*.gif",
                FileName = "avatar.gif",
            };
            if (dialog.ShowDialog(this) != true) return;

            try
            {
                AvatarSequence sequence = AvatarRenderer.RenderSequence(this._build, this.CurrentAction);
                if (sequence.Frames.Count == 0)
                {
                    this.ShowAlert("当前动作没有可导出的帧。", "导出失败");
                    return;
                }

                GifBitmapEncoder encoder = new();
                foreach (AvatarFrame frame in sequence.Frames)
                {
                    encoder.Frames.Add(BitmapFrame.Create(ToBitmapSource(frame.Pixels)));
                }

                using FileStream stream = File.Create(dialog.FileName);
                encoder.Save(stream);
            }
            catch (Exception ex)
            {
                this.ShowAlert(ex.Message, "导出失败");
            }
        }

        /// <summary>把当前搭配导出为可读文本。</summary>
        private void MenuExportText_Click(object sender, RoutedEventArgs e)
        {
            if (this._data == null || this._build == null) return;

            SaveFileDialog dialog = new()
            {
                Title = "导出搭配文本",
                Filter = "文本文件 (*.txt)|*.txt",
                FileName = "avatar.txt",
            };
            if (dialog.ShowDialog(this) != true) return;

            try
            {
                List<string> lines =
                [
                    "Classic Avatar 搭配",
                    $"性别：{this.CurrentGender}",
                    $"肤色：{SkinPresets[this._skinIndex].Name}",
                    string.Empty,
                ];

                foreach (EquipSlot slot in PanelSlots)
                {
                    int? id = this._slotSelections.TryGetValue(slot, out int? value) ? value : null;
                    string name = id is int itemId ? this.ItemName(itemId) : "（无）";
                    string idText = id is int v ? $"#{v:D5}" : "—";
                    lines.Add($"{this.SlotName(slot)}：{name} {idText}");
                }

                lines.Add(string.Empty);
                lines.Add($"动作：{this.CurrentAction} · 缩放：{PreviewZoom}× · 导出时间：{DateTime.Now:yyyy-MM-dd HH:mm}");
                File.WriteAllLines(dialog.FileName, lines, System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                this.ShowAlert(ex.Message, "导出失败");
            }
        }

        #endregion
    }
}





