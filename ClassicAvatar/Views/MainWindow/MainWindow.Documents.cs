#nullable enable
// Multi-document tabs and the side-by-side comparison mode built on
// VControls.VTabControl.
using Avatar.Core.Assets;
using Avatar.Core.Models;
using Avatar.Core.Rendering;
using Avatar.Core.Rules;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VControls.Controls;

namespace ClassicAvatar
{
    public partial class MainWindow
    {
        private enum EditSide
        {
            Left,
            Right,
        }

        private readonly List<OutfitDocument> _documents = [];
        private OutfitDocument? _activeDocument;
        private EditSide _editSide = EditSide.Left;
        private AvatarCanvas? _leftCanvas;
        private AvatarCanvas? _rightCanvas;
        private bool _switchingTabs;
        private bool _loadingDocument;
        private bool _comparing;
        private int _untitledCounter = 1;

        private OutfitDocument? LeftSideDocument =>
            (this.MainTabs.SelectedItem as VTabItem)?.Tag as OutfitDocument;

        private OutfitDocument? RightSideDocument =>
            (this.CompareTabs.SelectedItem as VTabItem)?.Tag as OutfitDocument;

        /// <summary>Creates the first tab and wires the add-tab command.</summary>
        private void InitializeDocuments()
        {
            this.MainTabs.AddTabRequested += (_, _) => this.AddNewDocument(select: true);

            OutfitDocument doc = this.CreateDefaultDocument();
            this._documents.Add(doc);
            this.AddDocumentTab(doc, select: true);
        }

        private OutfitDocument CreateDefaultDocument()
        {
            CharacterProfile profile = new()
            {
                Id = Guid.NewGuid().ToString("N"),
                Gender = Gender.Male,
                SkinIndex = 0,
                HairId = 30000,
                FaceId = 20000,
                DefaultAction = "stand1",
            };

            if (this._data != null)
            {
                foreach ((EquipSlot slot, int id) in new[]
                {
                    (EquipSlot.Coat, 1040000),
                    (EquipSlot.Pants, 1060000),
                    (EquipSlot.Shoes, 1072000),
                })
                {
                    try
                    {
                        if (this._data.Catalog.Get(slot, id) != null)
                        {
                            profile = slot switch
                            {
                                EquipSlot.Coat => profile with { CoatId = id },
                                EquipSlot.Pants => profile with { PantsId = id },
                                _ => profile with { ShoesId = id },
                            };
                        }
                    }
                    catch (Exception) { }
                }
            }

            return new OutfitDocument
            {
                Title = this.NextUntitledTitle(),
                Profile = profile,
                Action = "stand1",
            };
        }

        private string NextUntitledTitle()
        {
            int number = this._untitledCounter++;
            string baseName = this.FindString("VLoc.Doc.Untitled");
            return number <= 1 ? baseName : $"{baseName} {number}";
        }

        private void AddDocumentTab(OutfitDocument doc, bool select, VTabControl? target = null)
        {
            target ??= this._comparing ? this.CompareTabs : this.MainTabs;
            VTabItem item = new()
            {
                Header = this.TabTitle(doc),
                Tag = doc,
                ShowCloseButton = true,
            };

            target.Items.Add(item);
            if (select)
            {
                target.SelectedItem = item;
            }
        }

        private void AddNewDocument(bool select)
        {
            this.CommitActiveDocument();
            OutfitDocument doc = this.CreateDefaultDocument();
            this._documents.Add(doc);
            this.AddDocumentTab(doc, select);
        }

        private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (this._switchingTabs || this._loadingDocument) return;
            if (this.MainTabs.SelectedItem is VTabItem { Tag: OutfitDocument doc })
            {
                this.ActivateSide(EditSide.Left, doc);
            }
        }

        private void CompareTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (this._switchingTabs || this._loadingDocument) return;
            if (this.CompareTabs.SelectedItem is VTabItem { Tag: OutfitDocument doc })
            {
                this.ActivateSide(EditSide.Right, doc);
            }
        }

        /// <summary>Clicking a preview pane makes that side the editable character.</summary>
        private void PreviewPane_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!this._comparing || sender is not FrameworkElement { Tag: string tag }) return;
            EditSide side = string.Equals(tag, "Right", StringComparison.Ordinal)
                ? EditSide.Right
                : EditSide.Left;
            OutfitDocument? doc = side == EditSide.Right ? this.RightSideDocument : this.LeftSideDocument;
            if (doc != null)
            {
                this.ActivateSide(side, doc);
            }
        }

        /// <summary>Saves the current UI into the active document.</summary>
        private void CommitActiveDocument()
        {
            if (this._activeDocument == null || this._loadingDocument) return;

            OutfitDocument doc = this._activeDocument;
            doc.Profile = this.CollectProfile();
            doc.Action = this.CurrentAction;
            doc.Title = string.IsNullOrWhiteSpace(this.DocName.Text)
                ? this.FindString("VLoc.Doc.Untitled")
                : this.DocName.Text;
            doc.Path = this._documentPath;
            doc.IsDirty = this._dirty;
            this.UpdateTabHeader(doc);
        }

        /// <summary>Loads a document into the editor for the given side.</summary>
        private void ActivateSide(EditSide side, OutfitDocument doc)
        {
            if (ReferenceEquals(doc, this._activeDocument) && this._editSide == side)
            {
                this.UpdateSceneBorders();
                this.UpdateUndoState();
                return;
            }

            this.CommitActiveDocument();

            this._loadingDocument = true;
            this._editSide = side;
            this._activeDocument = doc;
            this._documentPath = doc.Path;
            this._dirty = doc.IsDirty;
            this.DocName.Text = doc.Title;
            this._pendingAction = doc.Action;
            if (side == EditSide.Left)
            {
                this.StageDocName.Text = doc.Title;
            }
            else
            {
                this.CompareDocName.Text = doc.Title;
            }
            this.ApplyProfile(doc.Profile);
            this._loadingDocument = false;

            this._dirty = doc.IsDirty;
            this.SetDirty(doc.IsDirty);
            this.UpdateUndoState();
            this.UpdateTabHeader(doc);
            this.UpdateSceneBorders();

            if (this._comparing)
            {
                this.RenderInactiveSide(side);
            }

            this.ApplyEquipFilter();

            Gender? genderFilter = this.CurrentGender == Gender.Unisex ? null : this.CurrentGender;
            if (this._equipListSlot != this._equipSlot || this._equipListGender != genderFilter)
            {
                this.LoadEquipList();
            }
        }

        private void UpdateTabHeader(OutfitDocument doc)
        {
            string title = this.TabTitle(doc);
            foreach (VTabControl control in new[] { this.MainTabs, this.CompareTabs })
            {
                VTabItem? item = this.FindTab(control, doc);
                if (item != null)
                {
                    item.Header = title;
                }
            }
        }

        private string TabTitle(OutfitDocument doc) =>
            doc.IsDirty ? $"{doc.Title} *" : doc.Title;

        private VTabItem? FindTab(VTabControl control, OutfitDocument doc)
        {
            foreach (object item in control.Items)
            {
                if (item is VTabItem tab && ReferenceEquals(tab.Tag, doc))
                {
                    return tab;
                }
            }
            return null;
        }

        private void Tabs_TabCloseRequested(object sender, RoutedEventArgs e)
        {
            if (e is not VTabCloseRequestedEventArgs args) return;

            args.Handled = true;
            e.Handled = true;

            if (args.Tab.Tag is OutfitDocument doc)
            {
                this.CloseDocument(doc);
            }
        }

        private void CloseDocument(OutfitDocument doc)
        {
            if (doc.IsDirty
                && !this.ShowConfirm(
                    $"“{doc.Title}”还有未保存的修改，确定关闭吗？",
                    "关闭搭配",
                    this.C2("VLoc.Common.Close", "关闭"),
                    this.C2("VLoc.Common.Cancel", "取消")))
            {
                return;
            }

            bool wasActive = ReferenceEquals(doc, this._activeDocument);
            this._switchingTabs = true;
            VTabItem? mainTab = this.FindTab(this.MainTabs, doc);
            if (mainTab != null) this.MainTabs.Items.Remove(mainTab);
            VTabItem? compareTab = this.FindTab(this.CompareTabs, doc);
            if (compareTab != null) this.CompareTabs.Items.Remove(compareTab);
            this._documents.Remove(doc);
            this._switchingTabs = false;

            if (!wasActive) return;

            this._activeDocument = null;
            EditSide side = this._comparing && this._editSide == EditSide.Right
                ? EditSide.Right
                : EditSide.Left;

            // Comparison always keeps one editable document on the left.
            if (this._comparing && this.MainTabs.Items.Count == 0 && this.CompareTabs.Items.Count > 0)
            {
                object first = this.CompareTabs.Items[0];
                this._switchingTabs = true;
                this.CompareTabs.Items.Remove(first);
                this.MainTabs.Items.Add(first);
                this._switchingTabs = false;
            }

            OutfitDocument? next = side == EditSide.Right
                ? this.RightSideDocument
                : this.LeftSideDocument;

            if (next == null && side == EditSide.Right)
            {
                side = EditSide.Left;
                next = this.LeftSideDocument;
            }

            if (next == null)
            {
                next = this.CreateDefaultDocument();
                this._documents.Add(next);
                this.AddDocumentTab(
                    next,
                    select: false,
                    side == EditSide.Right ? this.CompareTabs : this.MainTabs);
            }

            VTabControl target = side == EditSide.Right ? this.CompareTabs : this.MainTabs;
            VTabItem? nextTab = this.FindTab(target, next);
            if (nextTab != null)
            {
                this._switchingTabs = true;
                target.SelectedItem = nextTab;
                this._switchingTabs = false;
            }

            this.ActivateSide(side, next);
        }

        private void BtnCompare_Click(object sender, RoutedEventArgs e)
        {
            if (this._comparing)
            {
                this.ExitCompare();
            }
            else
            {
                this.EnterCompare();
            }
        }

        /// <summary>Splits the preview: active tab stays left, all others move right.</summary>
        private void EnterCompare()
        {
            if (this._activeDocument == null) return;

            this.CommitActiveDocument();

            if (this._documents.Count < 2)
            {
                OutfitDocument copy = new()
                {
                    Title = this._activeDocument.Title + " 副本",
                    Profile = this._activeDocument.Profile,
                    Action = this._activeDocument.Action,
                };
                this._documents.Add(copy);
                this.AddDocumentTab(copy, select: false);
            }

            OutfitDocument active = this._activeDocument;
            this._switchingTabs = true;
            foreach (OutfitDocument doc in this._documents.Where(d => !ReferenceEquals(d, active)).ToList())
            {
                VTabItem? item = this.FindTab(this.MainTabs, doc);
                if (item != null)
                {
                    this.MainTabs.Items.Remove(item);
                    this.CompareTabs.Items.Add(item);
                }
            }
            this._switchingTabs = false;

            this._comparing = true;
            this._editSide = EditSide.Left;
            this.PreviewCompareColumn.Width = new GridLength(1, GridUnitType.Star);
            this.CompareTabs.Visibility = Visibility.Visible;
            this.CompareHead.Visibility = Visibility.Visible;
            this.CompareScene.Visibility = Visibility.Visible;
            this.BtnCompare.Content = this.C2("C2Loc.CompareExit", "结束对比");

            VTabItem? activeTab = this.FindTab(this.MainTabs, active);
            if (activeTab != null)
            {
                this._switchingTabs = true;
                this.MainTabs.SelectedItem = activeTab;
                this._switchingTabs = false;
            }

            if (this.CompareTabs.Items.Count > 0)
            {
                this._switchingTabs = true;
                this.CompareTabs.SelectedIndex = 0;
                this._switchingTabs = false;

                if (this.CompareTabs.SelectedItem is VTabItem { Tag: OutfitDocument doc })
                {
                    this.RenderDocumentToSide(EditSide.Right, doc);
                }
            }

            this.UpdateSceneBorders();
        }

        /// <summary>Returns every comparison tab to the main strip.</summary>
        private void ExitCompare()
        {
            OutfitDocument? active = this._activeDocument;
            this._switchingTabs = true;
            foreach (object item in this.CompareTabs.Items.Cast<object>().ToList())
            {
                this.CompareTabs.Items.Remove(item);
                this.MainTabs.Items.Add(item);
            }

            if (active != null)
            {
                VTabItem? activeTab = this.FindTab(this.MainTabs, active);
                if (activeTab != null)
                {
                    this.MainTabs.SelectedItem = activeTab;
                }
            }
            this._switchingTabs = false;

            this._comparing = false;
            this._editSide = EditSide.Left;
            this.PreviewCompareColumn.Width = new GridLength(0);
            this.CompareTabs.Visibility = Visibility.Collapsed;
            this.CompareHead.Visibility = Visibility.Collapsed;
            this.CompareScene.Visibility = Visibility.Collapsed;
            this.BtnCompare.Content = this.C2("C2Loc.Compare", "对比");
            this.ComparePreview.Source = null;
            this.CompareMini.Source = null;
            this._rightCanvas = null;

            this.UpdateSceneBorders();
            this.Rebuild();
            this.UpdateUndoState();
        }

        private void OnCompareHostSizeChanged(object sender, SizeChangedEventArgs e) =>
            this.UpdatePreviewPlacement();

        /// <summary>Positions a rendered fixed-canvas sprite on its host.</summary>
        private void PositionSprite(Image image, Canvas host, AvatarCanvas? canvas)
        {
            if (canvas is not AvatarCanvas value)
            {
                return;
            }

            double scale = this.SpriteScale;
            double anchorX = host.ActualWidth / 2;
            double anchorY = Math.Max(0, host.ActualHeight - SpriteBottomGap);
            Canvas.SetLeft(image, anchorX + (value.Left * scale));
            Canvas.SetTop(image, anchorY + (value.Top * scale));
            image.RenderTransformOrigin = new Point(0, 0);
            image.RenderTransform = new ScaleTransform(scale, scale);
        }

        private void UpdateSceneBorders()
        {
            if (this.SceneBorder != null)
            {
                this.SceneBorder.SetResourceReference(
                    Border.BorderBrushProperty,
                    this._editSide == EditSide.Left ? "VBrush.Accent" : "VBrush.Border");
            }

            if (this.CompareScene != null)
            {
                this.CompareScene.SetResourceReference(
                    Border.BorderBrushProperty,
                    this._editSide == EditSide.Right ? "VBrush.Accent" : "VBrush.Border");
            }
        }

        private void RenderInactiveSide(EditSide activeSide)
        {
            EditSide other = activeSide == EditSide.Left ? EditSide.Right : EditSide.Left;
            OutfitDocument? doc = other == EditSide.Left ? this.LeftSideDocument : this.RightSideDocument;
            if (doc != null)
            {
                this.RenderDocumentToSide(other, doc);
            }
        }

        /// <summary>Renders a static first frame for a side that is not being edited.</summary>
        private void RenderDocumentToSide(EditSide side, OutfitDocument doc)
        {
            bool left = side == EditSide.Left;
            Image preview = left ? this.Preview : this.ComparePreview;
            Image mini = left ? this.MiniPreview : this.CompareMini;
            TextBlock miniCount = left ? this.MiniEquipCountText : this.CompareMiniEquipCountText;
            Canvas host = left ? this.PreviewHost : this.ComparePreviewHost;
            TextBlock title = left ? this.SceneTitleLine : this.CompareSceneTitle;
            TextBlock action = left ? this.SceneActionLine : this.CompareSceneAction;
            TextBlock canvasInfo = left ? this.CanvasInfo : this.CompareCanvasInfo;

            if (left)
            {
                this.StageDocName.Text = doc.Title;
            }
            else
            {
                this.CompareDocName.Text = doc.Title;
            }

            title.Text = doc.Title;
            action.Text = this.ActionDisplay(doc.Action);
            canvasInfo.Text = "200 × 140 · 3×";
            if (miniCount != null)
            {
                miniCount.Text = string.Format(
                    this.C2("C2Loc.Stage.EquippedCount", "已装备 {0} 件"),
                    CountEquipped(doc.Profile));
            }

            if (this._data == null)
            {
                preview.Source = null;
                mini.Source = null;
                return;
            }

            try
            {
                AvatarBuild build = AvatarAssembler.Build(this._data, doc.Profile, PreviewOptions);
                AvatarFrame frame = AvatarRenderer.RenderFrame(build, doc.Action, 0);
                var source = ToBitmapSource(frame.Pixels);
                preview.Source = source;
                mini.Source = source;

                if (left)
                {
                    this._leftCanvas = frame.Canvas;
                    this.PreviewBackgroundSync();
                }
                else
                {
                    this._rightCanvas = frame.Canvas;
                    this.CompareBackgroundImage.Source = this.BackgroundImage.Source;
                }

                canvasInfo.Text = $"{frame.Canvas.Width} × {frame.Canvas.Height} · 3×";
                this.PositionSprite(preview, host, frame.Canvas);
            }
            catch (Exception)
            {
                preview.Source = null;
                mini.Source = null;
            }
        }

        private void PreviewBackgroundSync()
        {
            // The left scene already owns BackgroundImage; keep the compare copy synced.
            this.CompareBackgroundImage.Source = this.BackgroundImage.Source;
        }

    }
}



