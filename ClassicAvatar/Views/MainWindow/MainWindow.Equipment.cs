#nullable enable
// Equipment browser, filters, part selector and context menu.
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
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
        #region 装备面板
        private void ListSlots_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!this._uiReady || this._suspendEvents) return;
            if (this.ListSlots.SelectedItem is not SlotVm slot) return;

            this._equipSlot = slot.Slot;
            this.UpdatePartHeader();

            // A leftover keyword from the previous part would make the new slot
            // look empty; reset it when the browse context changes.
            if (!string.IsNullOrEmpty(this.EquipSearch?.Text))
            {
                this._suspendEvents = true;
                this.EquipSearch.Text = string.Empty;
                this._suspendEvents = false;
            }

            this.LoadEquipList();
        }

        private void ReleaseSlotCaches(EquipSlot slot)
        {
            // Icon bitmaps and parsed WZ canvases for the previous list are no
            // longer referenced by any visible card. Reclaim them before the
            // next slot starts decoding, otherwise every tab switch grows RSS.
            IconLoader.ClearCache(slot);

            bool equipped = this.IsSlotPackageInUse(slot);
            if (!equipped && this._data != null)
            {
                try
                {
                    this._data.ReleaseSlotPackage(slot);
                }
                catch (Exception)
                {
                }
            }

            this.Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    GC.Collect(2, GCCollectionMode.Optimized, blocking: false, compacting: false);
                }),
                DispatcherPriority.ApplicationIdle);
        }

        private void ScheduleHeavyRelease(Task iconsTask)
        {
            this._heavyReleaseCts?.Cancel();
            CancellationTokenSource cts = new();
            this._heavyReleaseCts = cts;
            CancellationToken token = cts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    // Never clear parsed canvases while the icon worker is still
                    // reading them; wait for the current decode pass first.
                    await iconsTask.WaitAsync(token).ConfigureAwait(false);
                    await Task.Delay(1500, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                if (token.IsCancellationRequested) return;
                _ = this.Dispatcher.BeginInvoke(
                    new Action(() =>
                    {
                        if (token.IsCancellationRequested || !this.IsLoaded) return;
                        this.ClearIdleCaches();
                    }),
                    DispatcherPriority.ApplicationIdle);

                // A second, later pass catches WPF/WIC pages that are only
                // released after the first GC. It is still cancellable by the
                // next slot click.
                await Task.Delay(2500).ConfigureAwait(false);
                if (token.IsCancellationRequested) return;
                _ = this.Dispatcher.BeginInvoke(
                    new Action(() =>
                    {
                        if (token.IsCancellationRequested || !this.IsLoaded) return;
                        this.ClearIdleCaches();
                    }),
                    DispatcherPriority.ApplicationIdle);
            }, token);
        }

        private void ClearIdleCaches() => this.ClearAllCaches();

        /// <summary>
        /// Enumerates the slot on a worker, appends items to the bound collection
        /// in small UI batches, then fills icons in parallel so the list is usable
        /// immediately instead of waiting for every icon to decode.
        /// </summary>
        private void LoadEquipList()
        {
            if (this._data == null) return;

            this._equipLoadCts?.Cancel();
            this._heavyReleaseCts?.Cancel();
            var cts = new CancellationTokenSource();
            this._equipLoadCts = cts;
            CancellationToken token = cts.Token;

            EquipSlot previousSlot = this._equipListSlot;
            if (previousSlot != this._equipSlot)
            {
                this.ReleaseSlotCaches(previousSlot);
            }

            this._suspendEvents = true;
            this._equipItems.Clear();
            this.ListEquip.ItemsSource = null;
            this._suspendEvents = false;

            EquipSlot slot = this._equipSlot;
            Gender? gender = this.CurrentGender == Gender.Unisex ? null : this.CurrentGender;
            this._equipListGender = gender;
            this._equipListSlot = slot;
            AvatarData data = this._data;
            const int batchSize = 36;

            this.EquipLoading.Visibility = Visibility.Visible;
            TaskCompletionSource iconDone = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Task.Run(() =>
            {
                var batch = new List<EquipItemVm>(batchSize);
                try
                {
                    foreach (int id in data.EnumerateIds(slot, gender))
                    {
                        token.ThrowIfCancellationRequested();
                        batch.Add(new EquipItemVm(id, data.GetItemName(id)));
                        if (batch.Count < batchSize) continue;

                        EquipItemVm[] send = batch.ToArray();
                        batch.Clear();
                        this.Dispatcher.Invoke(() =>
                        {
                            if (token.IsCancellationRequested) return;

                            if (!ReferenceEquals(this.ListEquip.ItemsSource, this._equipItems))
                            {
                                this._suspendEvents = true;
                                this.ListEquip.ItemsSource = this._equipItems;
                                this._suspendEvents = false;
                            }

                            foreach (EquipItemVm item in send)
                            {
                                this._equipItems.Add(item);
                            }

                            this.EquipLoading.Visibility = Visibility.Collapsed;
                            if (this.EquipCount != null)
                            {
                                this.EquipCount.Text =
                                    $"{this._equipItems.Count} {this.FindString("VLoc.Common.Items")}";
                            }
                        });
                    }

                    if (batch.Count > 0)
                    {
                        EquipItemVm[] send = batch.ToArray();
                        this.Dispatcher.Invoke(() =>
                        {
                            if (token.IsCancellationRequested) return;
                            if (!ReferenceEquals(this.ListEquip.ItemsSource, this._equipItems))
                            {
                                this._suspendEvents = true;
                                this.ListEquip.ItemsSource = this._equipItems;
                                this._suspendEvents = false;
                            }
                            foreach (EquipItemVm item in send)
                            {
                                this._equipItems.Add(item);
                            }
                        });
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception)
                {
                }

                this.Dispatcher.Invoke(() =>
                {
                    if (token.IsCancellationRequested) return;
                    this.EquipLoading.Visibility = Visibility.Collapsed;
                    this.ApplyEquipFilter();
                });

                // Names are on screen now; decode icons in parallel. Each result
                // is pushed to its VM individually, so icons appear one by one.
                List<EquipItemVm> items = this._equipItems.ToList();
                try
                {
                    Parallel.ForEach(
                        items,
                        new ParallelOptions
                        {
                            MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 4, 8),
                            CancellationToken = token,
                        },
                        item =>
                        {
                            if (token.IsCancellationRequested || item.Icon != null) return;
                            try
                            {
                                BitmapSource? icon = IconLoader.LoadIcon(data, slot, item.Id);
                                if (icon != null && !token.IsCancellationRequested)
                                {
                                    this.Dispatcher.BeginInvoke(
                                        new Action(() => item.Icon = icon),
                                        DispatcherPriority.Background);
                                }
                            }
                            catch (Exception)
                            {
                            }
                        });
                }
                catch (OperationCanceledException)
                {
                }

                iconDone.TrySetResult();
            }, token);

            this.ScheduleHeavyRelease(iconDone.Task);
        }

        private void EquipSearch_TextChanged(object sender, TextChangedEventArgs e) => this.ApplyEquipFilter();

        private void EquipFilter_Changed(object sender, RoutedEventArgs e) => this.ApplyEquipFilter();

        private void EquipFilter_Changed(object sender, SelectionChangedEventArgs e) => this.ApplyEquipFilter();

        private void ApplyEquipFilter()
        {
            if (this.ListEquip == null) return;

            string keyword = this.EquipSearch?.Text?.Trim() ?? string.Empty;
            int cashFilter = this.EquipCashFilter?.SelectedIndex ?? 0;
            int? equipped = this._slotSelections.TryGetValue(this._equipSlot, out int? id) ? id : null;

            IEnumerable<EquipItemVm> query = this._equipItems;

            if (this.FilterFavorites?.IsChecked == true)
            {
                query = query.Where(i => this._favoriteIds.Contains(i.Id));
            }
            else if (this.FilterRecent?.IsChecked == true)
            {
                HashSet<int> recent = this._recentIds.ToHashSet();
                query = query.Where(i => recent.Contains(i.Id))
                             .OrderBy(i => this._recentIds.IndexOf(i.Id));
            }

            if (!string.IsNullOrEmpty(keyword))
            {
                query = query.Where(i =>
                    i.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || i.Id.ToString().Contains(keyword, StringComparison.Ordinal));
            }

            if (this._data != null)
            {
                EquipSlot slot = this._equipSlot;
                if (cashFilter == 1)
                {
                    query = query.Where(i => !this.IsCashItem(slot, i.Id));
                }
                else if (cashFilter == 2)
                {
                    query = query.Where(i => this.IsCashItem(slot, i.Id));
                }
            }

            List<EquipItemVm> result = query.ToList();
            this._suspendEvents = true;
            this.ListEquip.ItemsSource = result;

            string unit = this.FindString("VLoc.Common.Items");
            if (this.EquipCount != null) this.EquipCount.Text = $"{result.Count} {unit}";

            this.UpdateEquipEmptyState(result.Count, keyword);

            if (equipped is int current)
            {
                EquipItemVm? selected = result.FirstOrDefault(i => i.Id == current);
                if (selected != null)
                {
                    this.ListEquip.SelectedItem = selected;
                    this.ListEquip.ScrollIntoView(selected);
                }
                this.UpdateSelectedBar(current);
            }
            else
            {
                this.UpdateSelectedBar(null);
            }
            this._suspendEvents = false;
        }

        /// <summary>Shows a hint instead of a blank list when the current filter yields nothing.</summary>
        private void UpdateEquipEmptyState(int count, string keyword)
        {
            if (this.EquipEmpty == null || this.EquipEmptyText == null) return;

            if (count > 0)
            {
                this.EquipEmpty.Visibility = Visibility.Collapsed;
                return;
            }

            string key = this.FilterFavorites?.IsChecked == true
                ? "C2Loc.Equip.EmptyFavorites"
                : this.FilterRecent?.IsChecked == true
                    ? "C2Loc.Equip.EmptyRecent"
                    : !string.IsNullOrEmpty(keyword)
                        ? "C2Loc.Equip.EmptySearch"
                        : "C2Loc.Equip.EmptyList";

            this.EquipEmptyText.Text = this.FindString(key);
            this.EquipEmpty.Visibility = Visibility.Visible;
        }

        private void ListEquip_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!this._uiReady || this._suspendEvents) return;
            if (this.ListEquip.SelectedItem is not EquipItemVm item) return;

            this.PushUndo();
            this.SetSlotSelection(this._equipSlot, item.Id);
            this.UpdateSelectedBar(item.Id);
            this.Rebuild();
            this.ListEquip.ScrollIntoView(item);
        }

        private bool IsCashItem(EquipSlot slot, int itemId)
        {
            try
            {
                return this._data?.Catalog.Get(slot, itemId)?.IsCash == true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void UpdateSelectedBar(int? id)
        {
            if (id is int value)
            {
                this.EquipSelectedThumb.Source = this._data != null
                    ? IconLoader.LoadIcon(this._data, this._equipSlot, value)
                    : null;
                this.EquipSelectedName.Text = this.ItemName(value);
                this.EquipSelectedId.Text = $"#{value:D5} · {this.SlotName(this._equipSlot)}";
            }
            else
            {
                this.EquipSelectedThumb.Source = null;
                this.EquipSelectedName.Text = this.FindString("VLoc.Equip.NoSelection");
                this.EquipSelectedId.Text = "—";
            }
        }

        private void BtnEquipRemove_Click(object sender, RoutedEventArgs e)
        {
            if (this._slotSelections.TryGetValue(this._equipSlot, out int? id) && id != null)
            {
                this.PushUndo();
                this.SetSlotSelection(this._equipSlot, null);
                this.Rebuild();
                this.ApplyEquipFilter();
            }
        }

        #region 装备右键菜单
        private EquipItemVm? _contextItem;

        private EquipItemVm? ContextItem =>
            this._contextItem ?? this.ListEquip.SelectedItem as EquipItemVm;

        /// <summary>
        /// Right-click only targets the item under the cursor. It must not change
        /// selection, because selecting an item applies it and rebuilds the avatar.
        /// </summary>
        private void ListEquip_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            this._contextItem = null;
            for (DependencyObject? current = e.OriginalSource as DependencyObject;
                 current != null;
                 current = VisualTreeHelper.GetParent(current))
            {
                if (current is ListBoxItem { DataContext: EquipItemVm item } listItem)
                {
                    this._contextItem = item;
                    e.Handled = true;

                    // Open the menu ourselves so the ListBoxItem never selects
                    // (selection would apply the item and rebuild the avatar).
                    if (this.ListEquip.ContextMenu is ContextMenu menu)
                    {
                        menu.PlacementTarget = listItem;
                        menu.Placement = PlacementMode.MousePoint;
                        menu.IsOpen = true;
                    }
                    return;
                }
            }
        }

        private void ListEquipContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            if (this._contextItem is { } item)
            {
                if (this.MenuFavorite != null) this.MenuFavorite.Tag = item;
                if (this.MenuUnfavorite != null) this.MenuUnfavorite.Tag = item;
            }

            bool favorites = this.FilterFavorites?.IsChecked == true;
            if (this.MenuFavorite != null)
            {
                this.MenuFavorite.Visibility = favorites ? Visibility.Collapsed : Visibility.Visible;
            }
            if (this.MenuUnfavorite != null)
            {
                this.MenuUnfavorite.Visibility = favorites ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        // Keep the last right-clicked item until the next right-click: a stale
        // Closed event from a previous menu must not erase the new target.
        private void ListEquipContextMenu_Closed(object sender, RoutedEventArgs e)
        {
        }

        private void MenuCopyId_Click(object sender, RoutedEventArgs e)
        {
            if (this.ContextItem is not { } item) return;
            TrySetClipboardText(item.Id.ToString());
        }

        private void MenuCopyName_Click(object sender, RoutedEventArgs e)
        {
            if (this.ContextItem is not { } item) return;
            TrySetClipboardText(item.Name);
        }

        private static void TrySetClipboardText(string text)
        {
            // Clipboard.SetText blocks while another process owns the clipboard.
            // Run the retry loop on a background STA thread so the list never
            // freezes when the user copies an item id/name.
            Thread thread = new(() =>
            {
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        Clipboard.SetText(text);
                        return;
                    }
                    catch (ExternalException)
                    {
                        Thread.Sleep(30);
                    }
                }
            })
            {
                IsBackground = true,
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        private void MenuFavorite_Click(object sender, RoutedEventArgs e)
        {
            EquipItemVm? item = (sender as MenuItem)?.Tag as EquipItemVm ?? this.ContextItem;
            if (item == null) return;
            if (!this._favoriteIds.Add(item.Id))
            {
                this._favoriteIds.Remove(item.Id);
            }

            // Let the context menu close before rebuilding the filtered list.
            this.Dispatcher.BeginInvoke(
                new Action(this.ApplyEquipFilter),
                DispatcherPriority.Background);
        }

        private void MenuUnfavorite_Click(object sender, RoutedEventArgs e)
        {
            EquipItemVm? item = (sender as MenuItem)?.Tag as EquipItemVm ?? this.ContextItem;
            if (item == null) return;
            this._favoriteIds.Remove(item.Id);
            this.Dispatcher.BeginInvoke(
                new Action(this.ApplyEquipFilter),
                DispatcherPriority.Background);
        }
        #endregion
        #endregion
    }
}




