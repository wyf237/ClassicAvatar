#nullable enable
// Window chrome, browser tabs, colorways and language handlers.
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
        #region 顶部窗口按钮
        private void WindowBorder_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (this.WorkSpace.ActualWidth <= 0 || this.WorkSpace.ActualHeight <= 0) return;

            // Clip the content (not the outer border) so the rounded corners do
            // not also clip the drop shadow rendered by WindowBorder.Effect.
            this.WorkSpace.Clip = new RectangleGeometry(
                new Rect(0, 0, this.WorkSpace.ActualWidth, this.WorkSpace.ActualHeight),
                4,
                4);
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            if (IsInteractiveSource(e.OriginalSource as DependencyObject)) return;

            try
            {
                this.DragMove();
            }
            catch (InvalidOperationException)
            {
            }
        }

        private static bool IsInteractiveSource(DependencyObject? source)
        {
            while (source != null)
            {
                if (source is Button or Menu or MenuItem or ComboBox or TextBox or RadioButton)
                {
                    return true;
                }
                source = VisualTreeHelper.GetParent(source);
            }
            return false;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => this.Close();

        private void BtnMin_Click(object sender, RoutedEventArgs e) => this.WindowState = WindowState.Minimized;

        #endregion

        #region 配色
        private void BtnSkin_Click(object sender, RoutedEventArgs e)
        {
            this.SkinPopup.IsOpen = !this.SkinPopup.IsOpen;
        }

        /// <summary>Opens the persistent settings dialog.</summary>
        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            SettingsDialog dialog = new(
                C2Appearance.DataFolder,
                C2Appearance.SaveFolder,
                C2Appearance.Language)
            {
                ClearCaches = this.ClearAllCaches,
            };

            if (!dialog.ShowDialog(this)) return;

            bool dataChanged = !string.Equals(
                dialog.DataFolder, C2Appearance.DataFolder, StringComparison.OrdinalIgnoreCase);

            C2Appearance.SetSaveFolder(dialog.SaveFolder);

            if (dataChanged && !string.IsNullOrWhiteSpace(dialog.DataFolder))
            {
                this.OpenDataDirectory(dialog.DataFolder, showError: true);
            }
            else
            {
                C2Appearance.SetDataFolder(dialog.DataFolder);
            }

            if (dialog.SelectedLanguage != C2Appearance.Language)
            {
                this.ApplyLanguage(dialog.SelectedLanguage);
            }

            this.LoadLibrary();
        }

        /// <summary>Builds the Light/Dark bases and the 20 colorway choices.</summary>
        private void BuildPaletteChoices()
        {
            this.BasePaletteGrid.Children.Clear();
            this.PaletteGrid.Children.Clear();

            foreach (C2PaletteInfo info in C2Appearance.Catalog)
            {
                RadioButton choice = new()
                {
                    GroupName = "Palette",
                    Style = (Style)this.FindResource("C2.PaletteItem"),
                    Tag = info.Palette.ToString(),
                    Content = this.BuildPaletteContent(info),
                };
                choice.Checked += this.Palette_Checked;
                choice.ToolTip = this.PaletteDisplayName(info);
                System.Windows.Automation.AutomationProperties.SetName(
                    choice, this.PaletteDisplayName(info));

                if (info.IsBase)
                {
                    this.BasePaletteGrid.Children.Add(choice);
                }
                else
                {
                    this.PaletteGrid.Children.Add(choice);
                }
            }
        }

        private UIElement BuildPaletteContent(C2PaletteInfo info)
        {
            Grid grid = new();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            StackPanel dots = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (string hex in info.Swatches)
            {
                SolidColorBrush brush = new((Color)ColorConverter.ConvertFromString(hex));
                brush.Freeze();
                dots.Children.Add(new Border
                {
                    Width = 10,
                    Height = 10,
                    CornerRadius = new CornerRadius(5),
                    Background = brush,
                    Margin = new Thickness(0, 0, 3, 0),
                });
            }

            TextBlock name = new()
            {
                Text = this.PaletteDisplayName(info),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(5, 0, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            name.SetResourceReference(TextBlock.ForegroundProperty, "VBrush.TextPrimary");

            Grid.SetColumn(dots, 0);
            Grid.SetColumn(name, 1);
            grid.Children.Add(dots);
            grid.Children.Add(name);
            return grid;
        }

        private string PaletteDisplayName(C2PaletteInfo info) => info.Palette switch
        {
            C2Palette.Light => this.C2("C2Loc.Theme.Light", "浅色"),
            C2Palette.Dark => this.C2("C2Loc.Theme.Dark", "深色"),
            _ => info.DisplayName,
        };

        private void Palette_Checked(object sender, RoutedEventArgs e)
        {
            if (!this._uiReady) return;
            if (sender is not RadioButton { Tag: string name }) return;
            if (!Enum.TryParse(name, out C2Palette palette)) return;

            if (palette == C2Appearance.Palette)
            {
                if (this.SkinPopup != null) this.SkinPopup.IsOpen = false;
                return;
            }

            this.ApplyPalette(palette);
        }

        /// <summary>Swaps the ResourceDictionary only; the page is never rebuilt.</summary>
        private void ApplyPalette(C2Palette palette)
        {
            if (!this._uiReady) return;

            C2Appearance.ApplyPalette(palette);
            if (this.SkinPopup != null) this.SkinPopup.IsOpen = false;
        }
        #endregion

        #region 语言
        private void LangZhCN_Checked(object sender, RoutedEventArgs e) => this.ApplyLanguage(VLanguage.ZhCN);

        private void LangZhTW_Checked(object sender, RoutedEventArgs e) => this.ApplyLanguage(VLanguage.ZhTW);

        private void LangEnUS_Checked(object sender, RoutedEventArgs e) => this.ApplyLanguage(VLanguage.EnUS);

        private void ApplyLanguage(VLanguage language)
        {
            if (!this._uiReady) return;

            C2Appearance.ApplyLanguage(language);
            this.RefreshLocalizedPresetNames();
        }

        /// <summary>语言切换后刷新肤色/发色与由代码写入的标签。</summary>
        private void RefreshLocalizedPresetNames()
        {
            this._suspendEvents = true;

            var skins = new List<SkinVm>();
            for (int i = 0; i < SkinPresets.Length; i++)
            {
                skins.Add(new SkinVm(i, this.Loc($"VLoc.Skin.{i}", SkinPresets[i].Name), SkinPresets[i].Color));
            }
            this.ListSkin.ItemsSource = skins;
            this.ListSkin.SelectedIndex = this._skinIndex;
            this.SkinName.Text = this.Loc($"VLoc.Skin.{this._skinIndex}", SkinPresets[this._skinIndex].Name);

            var slots = new List<SlotVm>();
            foreach (EquipSlot slot in PanelSlots)
            {
                string key = SlotIconKeys.TryGetValue(slot, out string? iconKey) ? iconKey : "PIcon.Coat";
                Geometry icon = this.TryFindResource(key) as Geometry ?? Geometry.Empty;
                slots.Add(new SlotVm(slot, this.SlotName(slot), icon));
            }
            this.ListSlots.ItemsSource = slots;
            this.ListSlots.SelectedIndex = Array.IndexOf(PanelSlots, this._equipSlot);

            this._suspendEvents = false;

            if (this.SaveState != null)
            {
                this.SaveState.Text = this.Loc(
                    this._dirty ? "VLoc.Status.Unsaved" : "VLoc.Status.Saved",
                    this._dirty ? "未保存" : "已保存");
            }

            string actionKey = this.CurrentAction;
            this._suspendEvents = true;
            this.BuildActionItems(actionKey);
            this._suspendEvents = false;
            this.BtnCompare.Content = this._comparing
                ? this.C2("C2Loc.CompareExit", "结束对比")
                : this.C2("C2Loc.Compare", "对比");
            if (this._comparing)
            {
                this.RenderInactiveSide(this._editSide);
            }
            this.UpdatePartHeader();
            this.UpdateInspector();
            this.UpdateStatus();
            this.ApplyEquipFilter();
            this.ApplyLibraryFilter();
        }
        #endregion

        #region 帮助菜单
        private void MenuUsage_Click(object sender, RoutedEventArgs e) => this.HelpOverlay.Visibility = Visibility.Visible;

        private void BtnHelpClose_Click(object sender, RoutedEventArgs e) => this.HelpOverlay.Visibility = Visibility.Collapsed;

        private void MenuBackgroundFolder_Click(object sender, RoutedEventArgs e)
        {
            Directory.CreateDirectory(this._backgroundFolder);
            Process.Start(new ProcessStartInfo
            {
                FileName = this._backgroundFolder,
                UseShellExecute = true,
            });
        }
        #endregion

        #region 左侧浏览器
        /// <summary>Switches the 320 px browser between wardrobe, outfits and scenes.</summary>
        private void BrowserTab_Checked(object sender, RoutedEventArgs e)
        {
            if (!this._uiReady || this.EquipmentPane == null) return;

            bool equipment = this.EquipmentTab.IsChecked == true;
            bool outfits = this.OutfitsTab.IsChecked == true;

            this.EquipmentPane.Visibility = equipment ? Visibility.Visible : Visibility.Collapsed;
            this.OutfitsPane.Visibility = outfits ? Visibility.Visible : Visibility.Collapsed;
            this.BackgroundsPane.Visibility = equipment || outfits ? Visibility.Collapsed : Visibility.Visible;

            if (outfits)
            {
                this.LoadLibrary();
            }
            else if (!equipment && this.BgInlineList != null)
            {
                this.LoadBackgrounds();
            }
        }

        private void BtnCollapseBrowser_Click(object sender, RoutedEventArgs e)
        {
            this.BrowserColumn.Width = new GridLength(0);
            this.RevealColumn.Width = new GridLength(20);
            this.BrowserReveal.Visibility = Visibility.Visible;
        }

        private void BtnExpandBrowser_Click(object sender, RoutedEventArgs e)
        {
            this.BrowserColumn.Width = new GridLength(320);
            this.RevealColumn.Width = new GridLength(0);
            this.BrowserReveal.Visibility = Visibility.Collapsed;
        }

        /// <summary>Recent tab shortcut opens the local outfit browser.</summary>
        private void BtnRecent_Click(object sender, RoutedEventArgs e)
        {
            this.OutfitsTab.IsChecked = true;
        }

        /// <summary>Opens the full 14-slot outfit list in a modal dialog.</summary>
        private void BtnOutfitDetail_Click(object sender, RoutedEventArgs e)
        {
            // All 14 positions; an empty slot keeps its row with "—".
            var rows = new List<OutfitRowVm>();
            foreach (EquipSlot slot in PanelSlots)
            {
                bool has = this._slotSelections.TryGetValue(slot, out int? id) && id is int;
                int value = has ? id!.Value : 0;
                ImageSource? icon = has && this._data != null
                    ? IconLoader.LoadIcon(this._data, slot, value)
                    : null;

                rows.Add(new OutfitRowVm(
                    this.SlotName(slot),
                    has ? $"#{value:D5}" : "—",
                    has ? this.ItemName(value) : "—",
                    icon));
            }

            OutfitListDialog dialog = new(
                this.DocName.Text,
                rows,
                this.MiniPreview.Source,
                this.RenderOutfitPortrait());
            dialog.ShowDialog(this);
        }

        /// <summary>
        /// Tight render of the current outfit used as the large portrait in the
        /// outfit list dialog. The fixed 200×140 stage canvas is deliberately
        /// skipped so the character fills more of the available area.
        /// </summary>
        private ImageSource? RenderOutfitPortrait()
        {
            if (this._data == null) return null;
            try
            {
                CharacterProfile profile = this.CollectProfile();
                AvatarBuild build = AvatarAssembler.Build(this._data, profile, new AvatarOptions
                {
                    Padding = 2,
                    CollectDebugAnchors = false,
                });
                AvatarFrame frame = AvatarRenderer.RenderFrame(build, this.CurrentAction, 0);
                return ToBitmapSource(frame.Pixels);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Closes the current tab/window, preserving the unsaved prompt.</summary>
        #endregion
    }
}





