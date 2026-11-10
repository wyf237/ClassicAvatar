#nullable enable
// Avatar assembly, preview rendering, inspector and stage controls.
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
        #region 动画控制部分
        private void OnTimerTick(object? sender, EventArgs e)
        {
            if (this._actionCount <= 1) return;
            this._actionIndex = (this._actionIndex + 1) % this._actionCount;
            this._timer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(this._currentDelayMs, 30, 2000));
            this.RenderCurrent();
            this.UpdateStatus();
        }
        #endregion

        #region 组装控制部分
        private static int DefaultHairIdOf(Gender gender) => gender == Gender.Female ? 31000 : 30000;
        private static int DefaultFaceIdOf(Gender gender) => gender == Gender.Female ? 21000 : 20000;

        private void OnActionChanged(object sender, SelectionChangedEventArgs e)
        {            if (this._build?.Items.Body != null)
            {
                this._actionCount = Math.Max(1, this._build.Items.Body.Descriptor.GetFrameCount(this.CurrentAction));
            }
            if (this._suspendEvents || !this._uiReady || this._build == null)
            {
                return;
            }

            this._actionIndex = 0;
            this._pendingAction = this.CurrentAction;
            this.Rebuild();
        }

        /// <summary>Rebuilds the action dropdown with localized display names.</summary>
        private void BuildActionItems(string? selectedKey)
        {
            this.ActionBox.Items.Clear();
            this._actionDisplayToKey.Clear();
            if (this._build == null) return;

            foreach (string key in this._build.AvailableActions)
            {
                string display = this.ActionDisplay(key);
                this._actionDisplayToKey[display] = key;
                this.ActionBox.Items.Add(display);
            }

            if (this.ActionBox.Items.Count == 0) return;

            int index = 0;
            if (!string.IsNullOrEmpty(selectedKey))
            {
                for (int i = 0; i < this.ActionBox.Items.Count; i++)
                {
                    string display = (string)this.ActionBox.Items[i]!;
                    if (string.Equals(this._actionDisplayToKey[display], selectedKey, StringComparison.OrdinalIgnoreCase))
                    {
                        index = i;
                        break;
                    }
                }
            }
            this.ActionBox.SelectedIndex = index;
        }

        /// <summary>
        /// Chinese action names: stand/walk keep their numeric suffix, other
        /// actions use a translated name plus the original key as suffix.
        /// </summary>
        private string ActionDisplay(string key)
        {
            if (C2Appearance.Language == VLanguage.EnUS)
            {
                return key;
            }

            bool tw = C2Appearance.Language == VLanguage.ZhTW;
            string lower = key.ToLowerInvariant();
            string? text = lower switch
            {
                "stand1" => "站立1",
                "stand2" => "站立2",
                "walk1" => "行走1",
                "walk2" => "行走2",
                "alert" => tw ? "警戒" : "警戒",
                "jump" => tw ? "跳躍" : "跳跃",
                "fly" => tw ? "飛行" : "飞行",
                "ladder" => "爬梯",
                "rope" => tw ? "繩索" : "绳索",
                "prone" => "趴下",
                "pronestab" => tw ? "趴刺" : "趴刺",
                "heal" => tw ? "治療" : "治疗",
                "shoot1" => tw ? "射擊1" : "射击1",
                "shoot2" => tw ? "射擊2" : "射击2",
                "swingo1" => tw ? "揮砍1" : "挥砍1",
                "swingo2" => tw ? "揮砍2" : "挥砍2",
                "swingo3" => tw ? "揮砍3" : "挥砍3",
                "swingof" => tw ? "揮砍F" : "挥砍F",
                "swingpf" => tw ? "刺擊F" : "刺击F",
                "swingp1" => tw ? "刺擊1" : "刺击1",
                "swingp2" => tw ? "刺擊2" : "刺击2",
                "swingp3" => tw ? "刺擊3" : "刺击3",
                "swingt1" => tw ? "重揮1" : "重挥1",
                "swingt2" => tw ? "重揮2" : "重挥2",
                "swingt3" => tw ? "重揮3" : "重挥3",
                "stabo1" => tw ? "刺擊1" : "刺击1",
                "stabo2" => tw ? "刺擊2" : "刺击2",
                "stabt1" => tw ? "重刺1" : "重刺1",
                "stabt2" => tw ? "重刺2" : "重刺2",
                _ => null,
            };

            return text ?? (tw ? "動作" : "动作") + $" ({key})";
        }

        /// <summary>设置某槽位的选中物品。</summary>
        public void SetSlotSelection(EquipSlot slot, int? id)
        {
            this._slotSelections[slot] = id;
            if (id is int value)
            {
                this._recentIds.Remove(value);
                this._recentIds.Insert(0, value);
                if (this._recentIds.Count > 60) this._recentIds.RemoveAt(this._recentIds.Count - 1);
            }
        }

        /// <summary>重置为默认新角色。</summary>
        private void InitAvatar()
        {
            this._suspendEvents = true;
            this._timer.Stop();
            this._build = null;
            this._currentDelayMs = 60;
            this.Preview.Source = null;
            this.MiniPreview.Source = null;
            IconLoader.ClearCache();
            foreach (EquipSlot slot in PanelSlots)
            {
                this._slotSelections[slot] = null;
            }

            Gender gender = this.CurrentGender;
            this.SetSlotSelection(EquipSlot.Hair, DefaultHairIdOf(gender));
            this.SetSlotSelection(EquipSlot.Face, DefaultFaceIdOf(gender));

            // A new preview should look like a dressed starter character rather
            // than a bare mannequin. Use the classic basic set when available;
            // female characters get the female top/bottom instead of the male ones.
            if (this._data != null)
            {
                bool female = gender == Gender.Female;
                foreach ((EquipSlot slot, int id) in new[]
                {
                    (EquipSlot.Coat, female ? 1041000 : 1040000),
                    (EquipSlot.Pants, female ? 1061000 : 1060000),
                    (EquipSlot.Shoes, 1072000),
                })
                {
                    try
                    {
                        if (this._data.Catalog.Get(slot, id) != null)
                        {
                            this.SetSlotSelection(slot, id);
                        }
                    }
                    catch (Exception) { }
                }
            }

            this._skinIndex = 0;
            this.ListSkin.SelectedIndex = this._skinIndex;
            this.SkinName.Text = this.Loc($"VLoc.Skin.{this._skinIndex}", SkinPresets[this._skinIndex].Name);
            this._suspendEvents = false;

            this.Rebuild();
            this._timer.Start();
            this.PlayIcon.Data = (Geometry)this.FindResource("C2Icon.Pause");
        }

        /// <summary>按当前 UI 状态重新组装角色。</summary>
        public void Rebuild()
        {
            if (this._suspendEvents || this._data == null)
            {
                return;
            }

            CharacterProfile profile = this.CollectProfile();
            try
            {
                this._build = AvatarAssembler.Build(this._data, profile, PreviewOptions);
            }
            catch (Exception)
            {
                this._build = null;
                this.Preview.Source = null;
                    return;
            }

            string? previousKey = this._pendingAction ?? this.CurrentAction;
            this._pendingAction = null;
            this._suspendEvents = true;
            this.BuildActionItems(previousKey ?? profile.DefaultAction);
            this._suspendEvents = false;

            if (this._build.Items.Body != null)
            {
                this._actionCount = Math.Max(1, this._build.Items.Body.Descriptor.GetFrameCount(this.CurrentAction));
                if (this._actionIndex >= this._actionCount) this._actionIndex = 0;
            }

            this.RenderCurrent();
            this.UpdateInspector();
            this.UpdateStatus();
            this.MarkDirty();
        }

        /// <summary>把 UI 状态收集为角色档案。</summary>
        private CharacterProfile CollectProfile()
        {
            int? SelectedId(EquipSlot slot) =>
                this._slotSelections.TryGetValue(slot, out int? id) ? id : null;

            return new CharacterProfile
            {
                Id = "preview",
                Gender = this.CurrentGender,
                SkinIndex = this._skinIndex,
                HairId = SelectedId(EquipSlot.Hair) ?? DefaultHairIdOf(this.CurrentGender),
                FaceId = SelectedId(EquipSlot.Face) ?? DefaultFaceIdOf(this.CurrentGender),
                CapId = SelectedId(EquipSlot.Cap),
                CoatId = SelectedId(EquipSlot.Coat),
                LongcoatId = SelectedId(EquipSlot.Longcoat),
                PantsId = SelectedId(EquipSlot.Pants),
                ShoesId = SelectedId(EquipSlot.Shoes),
                GloveId = SelectedId(EquipSlot.Glove),
                ShieldId = SelectedId(EquipSlot.Shield),
                CapeId = SelectedId(EquipSlot.Cape),
                WeaponId = SelectedId(EquipSlot.Weapon),
                FaceAccessoryId = SelectedId(EquipSlot.FaceAccessory),
                EyeAccessoryId = SelectedId(EquipSlot.EyeAccessory),
                EarAccessoryId = SelectedId(EquipSlot.EarAccessory),
                HairDye = null,
                DefaultAction = this.CurrentAction,
            };
        }
        #endregion

        #region 预览渲染
        private void OnPreviewHostSizeChanged(object sender, SizeChangedEventArgs e) => this.UpdatePreviewPlacement();

        private void UpdatePreviewPlacement()
        {
            // Fixed canvas means canvas.Left/Top never change when equipment
            // changes. Mapping character (0,0) to the feet anchor keeps the
            // character rock-solid across items and animation frames.
            this.PositionSprite(this.Preview, this.PreviewHost, this._leftCanvas);
            this.PositionSprite(this.ComparePreview, this.ComparePreviewHost, this._rightCanvas);
        }

        private void UpdateCanvasInfo()
        {
            TextBlock? target = this._editSide == EditSide.Left
                ? this.CanvasInfo
                : this.CompareCanvasInfo;
            if (target == null) return;

            AvatarCanvas? canvas = this._editSide == EditSide.Left
                ? this._leftCanvas
                : this._rightCanvas;
            target.Text = canvas is AvatarCanvas value
                ? $"{value.Width} × {value.Height} · 3×"
                : "200 × 140 · 3×";
        }

        private static BitmapSource ToBitmapSource(WzPixelBuffer pixels)
        {
            WriteableBitmap bitmap = new(pixels.Width, pixels.Height, 96, 96, PixelFormats.Bgra32, null);
            bitmap.WritePixels(new Int32Rect(0, 0, pixels.Width, pixels.Height), pixels.Pixels, pixels.Stride, 0);
            bitmap.Freeze();
            return bitmap;
        }

        private void RenderCurrent()
        {
            if (this._build == null) return;
            try
            {
                AvatarFrame frame = AvatarRenderer.RenderFrame(this._build, this.CurrentAction, this._actionIndex);
                BitmapSource source = ToBitmapSource(frame.Pixels);
                if (this._editSide == EditSide.Left)
                {
                    this.Preview.Source = source;
                    this.MiniPreview.Source = source;
                    this._leftCanvas = frame.Canvas;
                    this.StageDocName.Text = this.DocName.Text;
                    this.SceneTitleLine.Text = this.DocName.Text;
                    this.SceneActionLine.Text = this.ActionDisplay(this.CurrentAction);
                }
                else
                {
                    this.ComparePreview.Source = source;
                    this.CompareMini.Source = source;
                    this._rightCanvas = frame.Canvas;
                    this.CompareDocName.Text = this.DocName.Text;
                    this.CompareSceneTitle.Text = this.DocName.Text;
                    this.CompareSceneAction.Text = this.ActionDisplay(this.CurrentAction);
                }

                this._currentDelayMs = frame.DelayMs;
                this.UpdatePreviewPlacement();
                this.UpdateCanvasInfo();
                this.UpdateFrameUi();
            }
            catch (Exception) { }
        }

        /// <summary>Updates the read-only frame progress and its timecode.</summary>
        private void UpdateFrameUi()
        {
            if (this.FrameProgress == null) return;

            int count = Math.Max(1, this._actionCount);
            int last = Math.Max(0, count - 1);
            this.FrameProgress.Maximum = Math.Max(1, last);
            this.FrameProgress.Value = Math.Clamp(this._actionIndex, 0, last);

            // Frame position / frame count, rendered in the design's mm:ss form.
            this.FrameTimeText.Text =
                $"{FormatTime(this._actionIndex + 1)} / {FormatTime(count)}";
        }

        private static string FormatTime(double seconds)
        {
            TimeSpan span = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return $"{(int)span.TotalMinutes:00}:{span.Seconds:00}";
        }
        #endregion

        #region 检视面板刷新
        private void UpdateInspector()
        {
            int? hairId = this._slotSelections.TryGetValue(EquipSlot.Hair, out int? h) ? h : null;
            int? faceId = this._slotSelections.TryGetValue(EquipSlot.Face, out int? f) ? f : null;

            this.HairThumb.Source = hairId is int hi && this._data != null
                ? IconLoader.LoadIcon(this._data, EquipSlot.Hair, hi)
                : null;
            this.HairName.Text = hairId is int hn ? this.ItemName(hn) : "—";
            this.HairIdText.Text = hairId is int hid ? $"#{hid:D5}" : "—";
            this.HairChip.Visibility = hairId.HasValue ? Visibility.Visible : Visibility.Collapsed;

            this.FaceThumb.Source = faceId is int fi && this._data != null
                ? IconLoader.LoadIcon(this._data, EquipSlot.Face, fi)
                : null;
            this.FaceName.Text = faceId is int fn ? this.ItemName(fn) : "—";
            this.FaceIdText.Text = faceId is int fid ? $"#{fid:D5}" : "—";
            this.FaceChip.Visibility = faceId.HasValue ? Visibility.Visible : Visibility.Collapsed;

            this.SkinName.Text = this.Loc($"VLoc.Skin.{this._skinIndex}", SkinPresets[this._skinIndex].Name);

            if (this.SceneTitleLine != null)
            {
                string title = string.IsNullOrWhiteSpace(this.DocName.Text)
                    ? this.FindString("VLoc.AppName")
                    : this.DocName.Text;
                if (this._editSide == EditSide.Left)
                {
                    this.StageDocName.Text = title;
                    this.SceneTitleLine.Text = title;
                    this.SceneActionLine.Text = this.ActionDisplay(this.CurrentAction);
                }
                else
                {
                    this.CompareDocName.Text = title;
                    this.CompareSceneTitle.Text = title;
                    this.CompareSceneAction.Text = this.ActionDisplay(this.CurrentAction);
                }
            }

            this.UpdateOutfitSummary();
            this.UpdateFrameUi();
        }

        /// <summary>Feeds the right-hand five-slot current-outfit summary.</summary>
        private void UpdateOutfitSummary()
        {
            int count = 0;
            foreach (EquipSlot slot in PanelSlots)
            {
                if (this._slotSelections.TryGetValue(slot, out int? id) && id != null) count++;
            }

            this.OutfitCount.Text = $"{count} {this.FindString("VLoc.Common.Items")}";
            if (this.MiniEquipCountText != null)
            {
                this.MiniEquipCountText.Text = string.Format(
                    this.C2("C2Loc.Stage.EquippedCount", "已装备 {0} 件"),
                    count);
            }

            bool hasLongcoat = this._slotSelections.TryGetValue(EquipSlot.Longcoat, out int? coatId)
                && coatId is int;
            EquipSlot[] keySlots =
            {
                EquipSlot.Hair,
                EquipSlot.Face,
                hasLongcoat ? EquipSlot.Longcoat : EquipSlot.Coat,
                EquipSlot.Pants,
                EquipSlot.Shoes,
            };

            var summary = new List<OutfitLineVm>();
            foreach (EquipSlot slot in keySlots)
            {
                string value = this._slotSelections.TryGetValue(slot, out int? slotId)
                    && slotId is int valueId
                    ? this.ItemName(valueId)
                    : "—";
                summary.Add(new OutfitLineVm(this.SlotName(slot), value));
            }

            this.OutfitSummaryList.ItemsSource = summary;
        }

        private static int CountEquipped(CharacterProfile profile)
        {
            int count = 2; // hair + face always present
            if (profile.CapId.HasValue) count++;
            if (profile.CoatId.HasValue) count++;
            if (profile.LongcoatId.HasValue) count++;
            if (profile.PantsId.HasValue) count++;
            if (profile.ShoesId.HasValue) count++;
            if (profile.GloveId.HasValue) count++;
            if (profile.ShieldId.HasValue) count++;
            if (profile.CapeId.HasValue) count++;
            if (profile.WeaponId.HasValue) count++;
            if (profile.FaceAccessoryId.HasValue) count++;
            if (profile.EyeAccessoryId.HasValue) count++;
            if (profile.EarAccessoryId.HasValue) count++;
            return count;
        }

        private void UpdateStatus()
        {
            if (this.StatusLeft == null || this.StatusRight == null) return;

            string source = this._data == null
                ? this.C2("C2Loc.Status.Disconnected", "未连接")
                : this.C2("C2Loc.Status.Connected", "已连接");
            this.StatusLeft.Text =
                $"{this.C2("C2Loc.Status.Source", "数据源")} · {source}";

            int fps = Math.Clamp((int)Math.Round(1000.0 / Math.Max(30, this._currentDelayMs)), 1, 60);
            this.StatusRight.Text = $"{this.C2("C2Loc.Status.Preview", "像素 1:1 · 3× 预览")} · {fps} FPS";
            if (this.FpsText != null) this.FpsText.Text = $"{fps} FPS";
        }

        private void UpdatePartHeader()
        {
            string name = this.SlotName(this._equipSlot);
            if (this.EquipSlotName != null) this.EquipSlotName.Text = name;
            if (this.CurrentPartText != null)
            {
                this.CurrentPartText.Text = this.C2("C2Loc.Part.Current", "当前：") + name;
            }
        }

        private string FindString(string key) => this.TryFindResource(key) as string ?? key;

        private string Loc(string key, string fallback) => this.TryFindResource(key) as string ?? fallback;

        private string C2(string key, string fallback) => this.TryFindResource(key) as string ?? fallback;

        /// <summary>Localized equipment slot label (also used by the dialog/export).</summary>
        private string SlotName(EquipSlot slot) =>
            this.C2($"C2Loc.Slot.{slot}", EquipSlotInfo.DisplayNameOf(slot));
        #endregion

        #region 性别与肤色
        private Gender CurrentGender
        {
            get
            {
                if (this.Male.IsChecked == true) return Gender.Male;
                if (this.Female.IsChecked == true) return Gender.Female;
                return Gender.Unisex;
            }
        }

        private void Gender_Checked(object sender, RoutedEventArgs e)
        {
            if (!this._uiReady || this._loadingDocument || this._suspendEvents) return;

            this.Dispatcher.BeginInvoke(
                new Action(this.LoadEquipList),
                DispatcherPriority.Background);
        }

        private void ChooseSex(object sender, MouseButtonEventArgs e)
        {
            if (sender is not RadioButton rb) return;
            Gender gender = (rb.Tag as string) switch
            {
                "Male" => Gender.Male,
                "Female" => Gender.Female,
                _ => Gender.Unisex,
            };
            if (gender == this.CurrentGender) return;

            this.PushUndo();
            this._timer.Stop();
            this._suspendEvents = true;

            // MouseLeftButtonDown runs before the radio button toggles itself,
            // so apply the checked state here; otherwise Rebuild would collect
            // the old gender while the new hair/face are already selected.
            this.Male.IsChecked = gender == Gender.Male;
            this.Female.IsChecked = gender == Gender.Female;
            this.SetSlotSelection(EquipSlot.Hair, DefaultHairIdOf(gender));
            this.SetSlotSelection(EquipSlot.Face, DefaultFaceIdOf(gender));
            this.ConvertGearForGender(gender);
            this._suspendEvents = false;
            this.Rebuild();
            this._timer.Start();
            this.PlayIcon.Data = (Geometry)this.FindResource("C2Icon.Pause");

            // The radio handlers are muted while switching, so refresh the
            // equipment browser explicitly for the new gender.
            this.Dispatcher.BeginInvoke(new Action(this.LoadEquipList), DispatcherPriority.Background);
        }

        /// <summary>
        /// Applies the gender switch rules: unisex gear stays on, gender-restricted
        /// gear is taken off, and the basic coat/pants swap to the matching set.
        /// </summary>
        private void ConvertGearForGender(Gender gender)
        {
            bool female = gender == Gender.Female;
            int coatFrom = female ? 1040000 : 1041000;
            int coatTo = female ? 1041000 : 1040000;
            int pantsFrom = female ? 1060000 : 1061000;
            int pantsTo = female ? 1061000 : 1060000;

            foreach (EquipSlot slot in PanelSlots)
            {
                // Hair and face are gender-specific by design and already
                // replaced with the new default above.
                if (slot is EquipSlot.Hair or EquipSlot.Face) continue;
                if (!this._slotSelections.TryGetValue(slot, out int? selection) || selection is not int id) continue;

                // The starter top/bottom migrate to the other gender's pair.
                if (slot == EquipSlot.Coat && id == coatFrom)
                {
                    this.SetSlotIfExists(slot, coatTo);
                    continue;
                }
                if (slot == EquipSlot.Pants && id == pantsFrom)
                {
                    this.SetSlotIfExists(slot, pantsTo);
                    continue;
                }

                // Unisex pieces are shared, everything else comes off.
                if (this.ItemGenderOf(slot, id) == Gender.Unisex) continue;
                this.SetSlotSelection(slot, null);
            }
        }

        /// <summary>Effective gender of one item, matching the equipment browser filter.</summary>
        private Gender ItemGenderOf(EquipSlot slot, int itemId)
        {
            Gender inferred = ItemDescriptor.InferGender(slot, itemId);
            if (inferred != Gender.Unisex) return inferred;
            return this._data?.Catalog.Get(slot, itemId)?.EffectiveGender ?? Gender.Unisex;
        }

        /// <summary>Selects an item, or clears the slot when the data set lacks it.</summary>
        private void SetSlotIfExists(EquipSlot slot, int itemId)
        {
            bool exists = this._data == null || this._data.Catalog.Get(slot, itemId) != null;
            this.SetSlotSelection(slot, exists ? itemId : null);
        }

        private void ListSkin_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!this._uiReady || this._suspendEvents || this.ListSkin.SelectedIndex < 0) return;
            if (this.ListSkin.SelectedIndex == this._skinIndex) return;

            this.PushUndo();
            this._skinIndex = this.ListSkin.SelectedIndex;
            this.SkinName.Text = this.Loc($"VLoc.Skin.{this._skinIndex}", SkinPresets[this._skinIndex].Name);
            this.Rebuild();
        }
        #endregion

        #region 角色面板按钮
        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            this.PushUndo();
            this.InitAvatar();
        }

        /// <summary>Rolls a complete random outfit (all equipment slots) plus appearance.</summary>
        private async void BtnRandomAppearance_Click(object sender, RoutedEventArgs e)
        {
            if (this._data == null)
            {
                this.ShowAlert("请先打开数据目录。", "提示");
                return;
            }

            OutfitDocument? target = this._activeDocument;
            EditSide targetSide = this._editSide;
            this.Cursor = Cursors.Wait;
            try
            {
                Gender gender = this.CurrentGender;
                if (gender == Gender.Unisex)
                {
                    gender = Random.Shared.Next(2) == 0 ? Gender.Male : Gender.Female;
                }

                // Enumerating every slot can touch thousands of WZ images; keep
                // it off the UI thread so the window stays responsive.
                AvatarData data = this._data;
                var roll = await Task.Run(() =>
                {
                    List<int>? Pick(EquipSlot slot)
                    {
                        List<int> ids = data.EnumerateIds(slot, gender).ToList();
                        return ids.Count == 0 ? null : ids;
                    }

                    int? Choose(List<int>? ids) =>
                        ids == null || ids.Count == 0 ? null : ids[Random.Shared.Next(ids.Count)];

                    bool longcoat = Random.Shared.Next(4) == 0;
                    Dictionary<EquipSlot, int?> slots = new()
                    {
                        [EquipSlot.Hair] = Choose(Pick(EquipSlot.Hair)),
                        [EquipSlot.Face] = Choose(Pick(EquipSlot.Face)),
                        [EquipSlot.Longcoat] = longcoat ? Choose(Pick(EquipSlot.Longcoat)) : null,
                        [EquipSlot.Coat] = longcoat ? null : Choose(Pick(EquipSlot.Coat)),
                        [EquipSlot.Pants] = longcoat ? null : Choose(Pick(EquipSlot.Pants)),
                        [EquipSlot.Cap] = Choose(Pick(EquipSlot.Cap)),
                        [EquipSlot.Shoes] = Choose(Pick(EquipSlot.Shoes)),
                        [EquipSlot.Glove] = Choose(Pick(EquipSlot.Glove)),
                        [EquipSlot.Shield] = Choose(Pick(EquipSlot.Shield)),
                        [EquipSlot.Cape] = Choose(Pick(EquipSlot.Cape)),
                        [EquipSlot.Weapon] = Choose(Pick(EquipSlot.Weapon)),
                        [EquipSlot.FaceAccessory] = Choose(Pick(EquipSlot.FaceAccessory)),
                        [EquipSlot.EyeAccessory] = Choose(Pick(EquipSlot.EyeAccessory)),
                        [EquipSlot.EarAccessory] = Choose(Pick(EquipSlot.EarAccessory)),
                    };

                    return (slots, Skin: Random.Shared.Next(SkinPresets.Length));
                });

                // The user may switch tabs while the roll is being prepared;
                // never paste it into a different document.
                if (target == null
                    || !ReferenceEquals(this._activeDocument, target)
                    || this._editSide != targetSide)
                {
                    return;
                }

                this.PushUndo();
                this._suspendEvents = true;
                this.Female.IsChecked = gender == Gender.Female;
                this.Male.IsChecked = gender == Gender.Male;
                foreach ((EquipSlot slot, int? id) in roll.slots)
                {
                    this.SetSlotSelection(slot, id);
                }
                this._skinIndex = roll.Skin;
                this.ListSkin.SelectedIndex = this._skinIndex;
                this.SkinName.Text = this.Loc($"VLoc.Skin.{this._skinIndex}", SkinPresets[this._skinIndex].Name);
                this._suspendEvents = false;
            }
            catch (Exception)
            {
                this._suspendEvents = false;
            }
            finally
            {
                this.Cursor = null;
            }

            this.Rebuild();
            this.ApplyEquipFilter();
            this.UpdateUndoState();
        }

        private void BtnChangeHair_Click(object sender, RoutedEventArgs e) => this.OpenEquipPicker(EquipSlot.Hair);

        private void BtnChangeFace_Click(object sender, RoutedEventArgs e) => this.OpenEquipPicker(EquipSlot.Face);

        /// <summary>在左侧装备库中定位到指定部位。</summary>
        private void OpenEquipPicker(EquipSlot slot)
        {
            if (this._data == null)
            {
                this.ShowAlert("请先打开数据目录。", "提示");
                return;
            }

            this.EquipmentTab.IsChecked = true;
            SlotVm? slotVm = (this.ListSlots.ItemsSource as IEnumerable<SlotVm>)
                ?.FirstOrDefault(s => s.Slot == slot);
            if (slotVm != null)
            {
                this.ListSlots.SelectedItem = slotVm;
                this.ListSlots.ScrollIntoView(slotVm);
            }
            else
            {
                this._equipSlot = slot;
                this.UpdatePartHeader();
                this.LoadEquipList();
            }
        }
        #endregion

        #region 舞台底部按钮
        private void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            if (this._timer.IsEnabled)
            {
                this._timer.Stop();
                this.PlayIcon.Data = (Geometry)this.FindResource("C2Icon.Play");
            }
            else
            {
                this._timer.Start();
                this.PlayIcon.Data = (Geometry)this.FindResource("C2Icon.Pause");
            }
        }
        #endregion

        #region 背景
        private void LoadBackgrounds()
        {
            Directory.CreateDirectory(this._backgroundFolder);
            List<BackgroundItem> items = [];
            try
            {
                foreach (string file in Directory.EnumerateFiles(this._backgroundFolder)
                             .Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                                      || f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                                      || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
                             .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                {
                    BitmapImage image = new();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.DecodePixelWidth = 200;
                    image.UriSource = new Uri(file);
                    image.EndInit();
                    image.Freeze();
                    items.Add(new BackgroundItem(Path.GetFileNameWithoutExtension(file), file, image));
                }
            }
            catch (Exception) { }

            this._suspendEvents = true;
            this.BgList.ItemsSource = items;
            if (this.BgInlineList != null)
            {
                this.BgInlineList.ItemsSource = items;
            }
            this._suspendEvents = false;

            BackgroundItem? selected = items.FirstOrDefault(i => i.Path == this._backgroundPath);
            if (selected != null)
            {
                this.ApplyBackground(selected);
            }
        }

        private void BtnBackGround_Click(object sender, RoutedEventArgs e)
        {
            this.LoadBackgrounds();
            this.BgPopup.IsOpen = true;
        }

        private void BtnBgPopupClose_Click(object sender, RoutedEventArgs e) => this.BgPopup.IsOpen = false;

        private void BtnBgNone_Click(object sender, RoutedEventArgs e)
        {
            this._backgroundPath = null;
            this.BackgroundImage.Source = null;
            this.CompareBackgroundImage.Source = null;
            this.BgChipThumb.Source = null;
            this.BgChipName.Text = this.FindString("VLoc.Stage.NoBackground");
            this._suspendEvents = true;
            this.BgList.SelectedItem = null;
            if (this.BgInlineList != null) this.BgInlineList.SelectedItem = null;
            this._suspendEvents = false;
            this.BgPopup.IsOpen = false;
        }

        private void BtnBgFolder_Click(object sender, RoutedEventArgs e) => this.MenuBackgroundFolder_Click(sender, e);

        private void BgList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (this._suspendEvents) return;
            if (this.BgList.SelectedItem is not BackgroundItem item) return;
            this.ApplyBackground(item);
            this.BgPopup.IsOpen = false;
        }

        private void BgInlineList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (this._suspendEvents) return;
            if (this.BgInlineList.SelectedItem is not BackgroundItem item) return;
            this.ApplyBackground(item);
        }

        private void ApplyBackground(BackgroundItem item)
        {
            BitmapImage image = new();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(item.Path);
            image.EndInit();
            image.Freeze();

            this.BackgroundImage.Source = image;
            this.CompareBackgroundImage.Source = image;
            this._backgroundPath = item.Path;
            this.BgChipThumb.Source = item.Preview;
            this.BgChipName.Text = item.Name;

            this._suspendEvents = true;
            this.BgList.SelectedItem = item;
            if (this.BgInlineList != null) this.BgInlineList.SelectedItem = item;
            this._suspendEvents = false;
        }
        #endregion
    }
}







