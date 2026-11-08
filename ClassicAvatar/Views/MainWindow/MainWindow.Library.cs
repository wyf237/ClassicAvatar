#nullable enable
// Outfit library and .avatar open/drag-drop flows.
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
        #region 搭配库
        private readonly Dictionary<string, (DateTime WriteTime, LibraryItemVm Item)> _libraryCache =
            new(StringComparer.OrdinalIgnoreCase);

        private void LoadLibrary()
        {
            if (this.LibraryList == null) return;

            try
            {
                Directory.CreateDirectory(this._libraryFolder);
            }
            catch (Exception)
            {
                // 目录不可写时仍可显示空列表，不阻断启动。
                this.ApplyLibraryFilter();
                return;
            }

            this._libraryItems.Clear();

            foreach (string cachedPath in this._libraryCache.Keys.ToList())
            {
                if (!File.Exists(cachedPath))
                {
                    this._libraryCache.Remove(cachedPath);
                }
            }

            try
            {
                foreach (string file in Directory.EnumerateFiles(this._libraryFolder, "*.avatar")
                             .OrderByDescending(File.GetLastWriteTime))
                {
                    DateTime writeTime = File.GetLastWriteTimeUtc(file);
                    if (this._libraryCache.TryGetValue(file, out var cached)
                        && cached.WriteTime == writeTime)
                    {
                        this._libraryItems.Add(cached.Item);
                        continue;
                    }

                    ImageSource? preview = null;
                    if (this._data != null)
                    {
                        // The stored preview is the full 200x140 canvas; render a
                        // tight card image so the character fills the tile.
                        try
                        {
                            CharacterProfile profile = AvatarPack.LoadProfile(file);
                            AvatarBuild build = AvatarAssembler.Build(
                                this._data,
                                profile,
                                new AvatarOptions
                                {
                                    Padding = 2,
                                    CollectDebugAnchors = false,
                                });
                            AvatarFrame frame = AvatarRenderer.RenderFrame(build, "stand1", 0);
                            preview = ToBitmapSource(frame.Pixels);
                        }
                        catch (Exception) { }
                    }

                    if (preview == null)
                    {
                        try
                        {
                            byte[]? bytes = AvatarPack.ReadPreview(file);
                            if (bytes is { Length: > 0 })
                            {
                                BitmapImage image = new();
                                using MemoryStream stream = new(bytes);
                                image.BeginInit();
                                image.CacheOption = BitmapCacheOption.OnLoad;
                                image.StreamSource = stream;
                                image.EndInit();
                                image.Freeze();
                                preview = image;
                            }
                        }
                        catch (Exception) { }
                    }

                    LibraryItemVm libraryItem = new(
                        Path.GetFileNameWithoutExtension(file),
                        file,
                        preview,
                        File.GetLastWriteTime(file).ToString("MM-dd HH:mm"),
                        file);
                    this._libraryItems.Add(libraryItem);
                    this._libraryCache[file] = (writeTime, libraryItem);
                }
            }
            catch (Exception) { }

            this.ApplyLibraryFilter();
        }

        private void ApplyLibraryFilter()
        {
            if (this.LibraryList == null) return;

            string keyword = this.LibrarySearch?.Text?.Trim() ?? string.Empty;
            IEnumerable<LibraryItemVm> query = this._libraryItems;

            if (this.LibFilterRecent?.IsChecked == true)
            {
                query = query.Take(9);
            }

            if (!string.IsNullOrEmpty(keyword))
            {
                query = query.Where(i => i.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            }

            List<LibraryItemVm> result = query.ToList();
            this.LibraryList.ItemsSource = result;
            this.LibraryEmpty.Visibility = result.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            this.LibraryCount.Text = this._libraryItems.Count == 0
                ? string.Empty
                : $"{this._libraryItems.Count} {this.FindString("VLoc.Library.CountSuffix")}";
        }

        private void LibrarySearch_TextChanged(object sender, TextChangedEventArgs e) => this.ApplyLibraryFilter();

        private void LibFilter_Changed(object sender, RoutedEventArgs e) => this.ApplyLibraryFilter();

        private void LibraryCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: LibraryItemVm item })
            {
                this.LoadAvatarFile(item.Path);
            }
        }

        private void BtnLibraryNew_Click(object sender, RoutedEventArgs e)
        {
            this.AddNewDocument(select: true);
            this.EquipmentTab.IsChecked = true;
        }

        private void BtnLibraryImport_Click(object sender, RoutedEventArgs e) => this.MenuOpen_Click(sender, e);
        #endregion

        #region 打开 / 拖入 .avatar
        private void OnAvatarDragOver(object sender, DragEventArgs e)
        {
            e.Effects = this.TryGetDroppedAvatar(e) != null ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void OnAvatarDrop(object sender, DragEventArgs e)
        {
            string? file = this.TryGetDroppedAvatar(e);
            if (file != null)
            {
                this.LoadAvatarFile(file);
            }
            e.Handled = true;
        }

        private string? TryGetDroppedAvatar(DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return null;
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return null;
            return files[0].EndsWith(".avatar", StringComparison.OrdinalIgnoreCase) ? files[0] : null;
        }

        private void LoadAvatarFile(string path)
        {
            try
            {
                if (this._data == null)
                {
                    this.ShowAlert("请先在设置中选择游戏 Data 目录，再载入 .avatar 文件。", "提示");
                    return;
                }

                CharacterProfile profile = AvatarPack.LoadProfile(path);
                OutfitDocument document = new()
                {
                    Title = Path.GetFileNameWithoutExtension(path),
                    Path = path,
                    Profile = profile,
                    Action = profile.DefaultAction,
                };
                this._documents.Add(document);
                this.AddDocumentTab(document, select: true);
                this.EquipmentTab.IsChecked = true;
            }
            catch (Exception ex)
            {
                this.ShowAlert(ex.Message, "载入失败");
            }
        }

        /// <summary>把档案写回 UI。</summary>
        private void ApplyProfile(CharacterProfile profile)
        {
            this._suspendEvents = true;

            this._skinIndex = Math.Clamp(profile.SkinIndex, 0, SkinPresets.Length - 1);
            this.ListSkin.SelectedIndex = this._skinIndex;
            this.SkinName.Text = this.Loc($"VLoc.Skin.{this._skinIndex}", SkinPresets[this._skinIndex].Name);

            this._slotSelections[EquipSlot.Hair] = profile.HairId;
            this._slotSelections[EquipSlot.Face] = profile.FaceId;
            this._slotSelections[EquipSlot.Cap] = profile.CapId;
            this._slotSelections[EquipSlot.Coat] = profile.CoatId;
            this._slotSelections[EquipSlot.Longcoat] = profile.LongcoatId;
            this._slotSelections[EquipSlot.Pants] = profile.PantsId;
            this._slotSelections[EquipSlot.Shoes] = profile.ShoesId;
            this._slotSelections[EquipSlot.Glove] = profile.GloveId;
            this._slotSelections[EquipSlot.Shield] = profile.ShieldId;
            this._slotSelections[EquipSlot.Cape] = profile.CapeId;
            this._slotSelections[EquipSlot.Weapon] = profile.WeaponId;
            this._slotSelections[EquipSlot.FaceAccessory] = profile.FaceAccessoryId;
            this._slotSelections[EquipSlot.EyeAccessory] = profile.EyeAccessoryId;
            this._slotSelections[EquipSlot.EarAccessory] = profile.EarAccessoryId;

            if (profile.Gender == Gender.Female) this.Female.IsChecked = true;
            else this.Male.IsChecked = true;

            this._pendingAction = profile.DefaultAction;
            this._actionIndex = 0;
            this._suspendEvents = false;

            this.Rebuild();
        }
        #endregion
    }
}


