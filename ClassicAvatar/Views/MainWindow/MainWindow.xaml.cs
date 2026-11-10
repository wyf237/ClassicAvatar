#nullable enable
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
    /// <summary>
    /// Studio shell: owns the layout, document state and the services shared by
    /// the partial files (Avatar, Equipment, Library, File, Window).
    /// </summary>
    public partial class MainWindow : Window
    {
        #region 变量定义部分
        /// <summary>参与换装的面板槽位（Body/Head 由肤色决定，Ring 无外观，均不在此列）。</summary>
        private static readonly EquipSlot[] PanelSlots =
        {
            EquipSlot.Hair, EquipSlot.Face, EquipSlot.Cap, EquipSlot.Coat, EquipSlot.Longcoat,
            EquipSlot.Pants, EquipSlot.Shoes, EquipSlot.Glove, EquipSlot.Shield, EquipSlot.Cape,
            EquipSlot.Weapon, EquipSlot.FaceAccessory, EquipSlot.EyeAccessory, EquipSlot.EarAccessory,
        };

        /// <summary>槽位 → 部件矢量图标资源键（C2：图标随主题变色，不再用 PNG）。</summary>
        private static readonly Dictionary<EquipSlot, string> SlotIconKeys = new()
        {
            [EquipSlot.Hair] = "PIcon.Hair",
            [EquipSlot.Face] = "PIcon.Face",
            [EquipSlot.Cap] = "PIcon.Cap",
            [EquipSlot.Coat] = "PIcon.Coat",
            [EquipSlot.Longcoat] = "PIcon.Longcoat",
            [EquipSlot.Pants] = "PIcon.Pants",
            [EquipSlot.Shoes] = "PIcon.Shoes",
            [EquipSlot.Glove] = "PIcon.Glove",
            [EquipSlot.Shield] = "PIcon.Shield",
            [EquipSlot.Cape] = "PIcon.Cape",
            [EquipSlot.Weapon] = "PIcon.Weapon",
            [EquipSlot.FaceAccessory] = "PIcon.FaceAccessory",
            [EquipSlot.EyeAccessory] = "PIcon.EyeAccessory",
            [EquipSlot.EarAccessory] = "PIcon.EarAccessory",
        };

        /// <summary>肤色预设名称与显示色（与 00002000..00002006 对应）。</summary>
        private static readonly (string Name, string Color)[] SkinPresets =
        {
            ("嫩白 01", "#FFDDAA"),
            ("暖白 02", "#BB9966"),
            ("自然 03", "#886633"),
            ("小麦 04", "#FFFFEE"),
            ("蜂蜜 05", "#CCCCDD"),
            ("古铜 06", "#DDDDDD"),
            ("深棕 07", "#FFDDDD"),
        };

        /// <summary>数据入口（打开目录后非空）。</summary>
        private AvatarData? _data;
        /// <summary>是否正在批量刷新 UI（避免级联触发重建）。</summary>
        private bool _suspendEvents;
        /// <summary>当前组装结果。</summary>
        private AvatarBuild? _build;
        /// <summary>读取档案后待恢复的动作名。</summary>
        private string? _pendingAction;
        /// <summary>当前动作名（内部键，供渲染使用）。</summary>
        private string CurrentAction =>
            this.ActionBox?.SelectedItem is string display
            && this._actionDisplayToKey.TryGetValue(display, out string? key)
                ? key
                : "stand1";

        /// <summary>动作下拉框显示文本 → 实际动作键。</summary>
        private readonly Dictionary<string, string> _actionDisplayToKey = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>槽位 → 当前选中 ID（null 表示不穿）。</summary>
        private readonly Dictionary<EquipSlot, int?> _slotSelections = [];
        /// <summary>帧播放计时器。</summary>
        private readonly DispatcherTimer _timer = new();
        /// <summary>界面是否已完成初始化。</summary>
        private bool _uiReady;
        private int _actionCount;
        private int _actionIndex;
        private int _currentDelayMs = 60;
        /// <summary>C2 fixed preview scale (nominal 3× of a 100 DIP baseline).</summary>
        private const int PreviewZoom = 3;

        /// <summary>Character height in DIP, independent of the current equipment.</summary>
        private const double SpriteHeight = 300;

        /// <summary>Nominal body artboard height used to keep the character scale stable.</summary>
        private const double BaseBodyHeight = 68;

        /// <summary>Feet distance from the scene bottom (DIP).</summary>
        private const double SpriteBottomGap = 96;

        /// <summary>
        /// Fixed character-space canvas: every frame is rendered into the same
        /// 200×140 box, so equipping an item can never move the character origin.
        /// </summary>
        private static readonly AvatarOptions PreviewOptions = new()
        {
            FixedCanvas = new AvatarCanvas(200, 140, -100, -110),
            Padding = 0,
            CollectDebugAnchors = false,
        };

        private double SpriteScale => SpriteHeight / BaseBodyHeight;

        /// <summary>当前肤色下标 0..6。</summary>
        private int _skinIndex = 0;
        /// <summary>装备面板当前槽位。</summary>
        private EquipSlot _equipSlot = EquipSlot.Coat;
        /// <summary>装备列表主数据（图标后台补全）。</summary>
        private readonly ObservableCollection<EquipItemVm> _equipItems = [];
        private CancellationTokenSource? _equipLoadCts;
        private CancellationTokenSource? _heavyReleaseCts;
        private Gender? _equipListGender;
        private EquipSlot _equipListSlot;
        /// <summary>收藏的部件 ID。</summary>
        private readonly HashSet<int> _favoriteIds = [];
        /// <summary>最近试穿过的部件 ID（新在前）。</summary>
        private readonly List<int> _recentIds = [];
        /// <summary>撤销 / 重做栈。</summary>
        /// <summary>当前文档路径（.avatar）。</summary>
        private string? _documentPath;
        /// <summary>是否有未保存修改。</summary>
        private bool _dirty;
        /// <summary>背景图片目录。</summary>
        private readonly string _backgroundFolder =
            Path.Combine(AppContext.BaseDirectory, "Backgrounds");
        /// <summary>当前背景路径；null 表示无背景。</summary>
        private string? _backgroundPath;
        /// <summary>搭配库目录 = 默认保存目录（可在设置中修改）。</summary>
        private string _libraryFolder => C2Appearance.SaveFolder;
        /// <summary>搭配库全部条目。</summary>
        private readonly List<LibraryItemVm> _libraryItems = [];
        /// <summary>最近一次使用的数据目录记录文件。</summary>
        private readonly string _lastDataFile =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ClassicAvatar", "last-data.txt");

        private readonly string _firstRunFile =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ClassicAvatar", "first-run.txt");

        private bool _firstRunDialogOpen;
        #endregion

        public MainWindow()
        {
            InitializeComponent();

            // Reflect the persisted colorway / language before handlers go live.
            this.BuildPaletteChoices();
            this.SyncAppearanceRadios();

            this._timer.Tick += this.OnTimerTick;
            this._uiReady = true;

            this.BuildStaticUi();
            this.UpdateStatus();

            this.AllowDrop = true;
            this.Drop += OnAvatarDrop;
            this.DragOver += OnAvatarDragOver;
            this.PreviewKeyDown += OnWindowKeyDown;
            this.Loaded += this.OnWindowLoaded;
            this.Closing += this.OnWindowClosing;
            this.StateChanged += this.OnWindowStateGuard;
        }

        /// <summary>Fixed-size workbench: never allow maximize.</summary>
        private void OnWindowStateGuard(object? sender, EventArgs e)
        {
            if (this.WindowState == WindowState.Maximized)
            {
                this.WindowState = WindowState.Normal;
            }
        }

        /// <summary>Marks the palette and language radios without re-applying them.</summary>
        private void SyncAppearanceRadios()
        {
            string palette = Services.C2Appearance.Palette.ToString();
            foreach (FrameworkElement child in this.BasePaletteGrid.Children)
            {
                if (child is RadioButton button)
                {
                    button.IsChecked = string.Equals(button.Tag as string, palette, StringComparison.Ordinal);
                }
            }
            foreach (FrameworkElement child in this.PaletteGrid.Children)
            {
                if (child is RadioButton button)
                {
                    button.IsChecked = string.Equals(button.Tag as string, palette, StringComparison.Ordinal);
                }
            }

            switch (Services.C2Appearance.Language)
            {
                case VLanguage.ZhTW: this.LangZhTW.IsChecked = true; break;
                case VLanguage.EnUS: this.LangEnUS.IsChecked = true; break;
                default: this.LangZhCN.IsChecked = true; break;
            }
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            // WindowStartupLocation=CenterScreen can pick a secondary monitor
            // (or a monitor left of the primary). Always open on the primary
            // work area so the window is never born off-screen.
            Rect work = SystemParameters.WorkArea;
            this.Left = work.Left + Math.Max(0, (work.Width - this.ActualWidth) / 2);
            this.Top = work.Top + Math.Max(0, (work.Height - this.ActualHeight) / 2);

            this.UpdatePreviewPlacement();
            this.UpdateStatus();

            // Let the shell render at least one frame before a modal VDialog is
            // pushed; otherwise the transparent window can stay blank.
            this.Dispatcher.BeginInvoke(
                new Action(this.TryAutoOpenData),
                DispatcherPriority.ApplicationIdle);
        }

        private void OnWindowClosing(object? sender, CancelEventArgs e)
        {
            this.CommitActiveDocument();

            // Every tab is validated separately: a clean document never triggers
            // the prompt, dirty ones are listed by tab so the user knows exactly
            // which outfits still need to be saved.
            List<OutfitDocument> dirty = [];
            foreach (OutfitDocument doc in this._documents)
            {
                if (doc.IsDirty) dirty.Add(doc);
            }
            if (dirty.Count == 0) return;

            try
            {
                string message;
                if (dirty.Count == 1)
                {
                    message = string.Format(
                        this.C2("C2Loc.Unsaved.Single", "“{0}”还没有保存，是否保存后关闭？"),
                        dirty[0].Title);
                }
                else
                {
                    List<string> lines =
                    [
                        string.Format(
                            this.C2("C2Loc.Unsaved.MultipleHeader", "以下 {0} 个搭配还没有保存："),
                            dirty.Count),
                    ];
                    foreach (OutfitDocument doc in dirty)
                    {
                        lines.Add(string.Format(
                            this.C2("C2Loc.Unsaved.Line", "{0} · {1}"),
                            this.TabLabel(doc),
                            doc.Title));
                    }
                    lines.Add(this.C2("C2Loc.Unsaved.Question", "是否保存后关闭？"));
                    message = string.Join("\n", lines);
                }

                UnsavedChangesDialog dialog = new(
                    this.Loc("VLoc.Dialog.UnsavedTitle", "未保存的搭配"),
                    message);
                dialog.ShowDialog(this);

                if (dialog.Choice == UnsavedChoice.Cancel)
                {
                    e.Cancel = true;
                    return;
                }

                if (dialog.Choice == UnsavedChoice.Save)
                {
                    foreach (OutfitDocument doc in dirty)
                    {
                        if (!this.SaveDocumentForClose(doc))
                        {
                            e.Cancel = true;
                            return;
                        }
                    }
                    this.SetDirty(false);
                }
            }
            catch (Exception)
            {
                // Never close with unsaved work if the prompt itself failed.
                e.Cancel = true;
            }
        }

        /// <summary>Tab caption used by the close prompt ("标签 1").</summary>
        private string TabLabel(OutfitDocument doc)
        {
            int index = this.IndexOfTab(this.MainTabs, doc);
            if (index >= 0)
            {
                return string.Format(this.C2("C2Loc.Unsaved.Tab", "标签 {0}"), index + 1);
            }

            index = this.IndexOfTab(this.CompareTabs, doc);
            return string.Format(this.C2("C2Loc.Unsaved.CompareTab", "对比标签 {0}"), index + 1);
        }

        private int IndexOfTab(VTabControl control, OutfitDocument doc)
        {
            for (int i = 0; i < control.Items.Count; i++)
            {
                if (control.Items[i] is VTabItem tab && ReferenceEquals(tab.Tag, doc))
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>Saves one dirty document while closing; returns false when it must be aborted.</summary>
        private bool SaveDocumentForClose(OutfitDocument doc)
        {
            if (!doc.IsDirty) return true;

            // Without a data folder nothing can be rebuilt, so saving would only
            // trap the user in the close prompt.
            if (this._data == null) return true;

            // Documents that were saved before keep their path, so closing can
            // write them without another dialog.
            if (!string.IsNullOrWhiteSpace(doc.Path))
            {
                return this.TrySaveDocumentToPath(doc, doc.Path!);
            }

            this.ActivateDocument(doc);
            this.MenuSave_Click(this, new RoutedEventArgs());
            return !doc.IsDirty;
        }

        private bool TrySaveDocumentToPath(OutfitDocument doc, string path)
        {
            if (this._data == null) return false;
            try
            {
                AvatarBuild build = AvatarAssembler.Build(this._data, doc.Profile, PreviewOptions);
                AvatarPack.Save(build, path);
                doc.Path = path;
                doc.IsDirty = false;
                if (ReferenceEquals(doc, this._activeDocument))
                {
                    this._documentPath = path;
                    this.SetDirty(false);
                }
                else
                {
                    this.UpdateTabHeader(doc);
                }
                this.LoadLibrary();
                return true;
            }
            catch (Exception ex)
            {
                this.ShowAlert(ex.Message, "保存失败");
                return false;
            }
        }

        private void ActivateDocument(OutfitDocument doc)
        {
            if (ReferenceEquals(doc, this._activeDocument)) return;
            if (this.IndexOfTab(this.MainTabs, doc) >= 0)
            {
                this.ActivateSide(EditSide.Left, doc);
            }
            else if (this.IndexOfTab(this.CompareTabs, doc) >= 0)
            {
                this.ActivateSide(EditSide.Right, doc);
            }
        }

        #region 初始化静态 UI
        /// <summary>填充肤色、槽位、发色等固定数据。</summary>
        private void BuildStaticUi()
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
            this.UpdatePartHeader();
            this.UpdateStatus();
            this.UpdateUndoState();
            this.LoadBackgrounds();
            this.LoadLibrary();
            this.InitializeDocuments();
        }

        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            if (ctrl)
            {
                switch (e.Key)
                {
                    case Key.S: MenuSave_Click(this, new RoutedEventArgs()); e.Handled = true; return;
                    case Key.O: MenuOpen_Click(this, new RoutedEventArgs()); e.Handled = true; return;
                    case Key.E: MenuExportPng_Click(this, new RoutedEventArgs()); e.Handled = true; return;
                    case Key.Z: MenuUndo_Click(this, new RoutedEventArgs()); e.Handled = true; return;
                    case Key.Y: MenuRedo_Click(this, new RoutedEventArgs()); e.Handled = true; return;
                }
            }

            if (e.Key == Key.F2)
            {
                this.RenameCurrent();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                this.HelpOverlay.Visibility = Visibility.Collapsed;
                this.BgPopup.IsOpen = false;
                this.SkinPopup.IsOpen = false;
                e.Handled = true;
            }
        }
        #endregion







        #region 编辑菜单
        private void MenuUndo_Click(object sender, RoutedEventArgs e)
        {
            if (this._activeDocument is not { } doc || doc.Undo.Count == 0)
            {
                this.UpdateUndoState();
                return;
            }
            doc.Redo.Push(this.CaptureSnapshot());
            this.ApplySnapshot(doc.Undo.Pop());
            this.MarkDirty();
            this.UpdateUndoState();
        }

        private void MenuRedo_Click(object sender, RoutedEventArgs e)
        {
            if (this._activeDocument is not { } doc || doc.Redo.Count == 0)
            {
                this.UpdateUndoState();
                return;
            }
            doc.Undo.Push(this.CaptureSnapshot());
            this.ApplySnapshot(doc.Redo.Pop());
            this.MarkDirty();
            this.UpdateUndoState();
        }

        private void MenuResetAppearance_Click(object sender, RoutedEventArgs e)
        {
            this.PushUndo();
            this.InitAvatar();
        }

        private void MenuClearCache_Click(object sender, RoutedEventArgs e)
        {
            IconLoader.ClearCache();
            if (this._data != null)
            {
                this.UpdateInspector();
                this.LoadEquipList();
            }
        }
        #endregion












        #region 撤销 / 重做
        private DocumentSnapshot CaptureSnapshot() => new(this.CollectProfile());

        private void PushUndo()
        {
            if (this._build == null || this._activeDocument == null) return;
            this._activeDocument.Undo.Push(this.CaptureSnapshot());
            this._activeDocument.Redo.Clear();
            this.UpdateUndoState();
        }

        /// <summary>Undo state follows the active tab/side document.</summary>
        private void UpdateUndoState()
        {
            int undo = this._activeDocument?.Undo.Count ?? 0;
            int redo = this._activeDocument?.Redo.Count ?? 0;
            if (this.BtnUndo != null) this.BtnUndo.IsEnabled = undo > 0;
            if (this.BtnRedo != null) this.BtnRedo.IsEnabled = redo > 0;
        }

        private void ApplySnapshot(DocumentSnapshot snapshot)
        {
            this.ApplyProfile(snapshot.Profile);
            this.ApplyEquipFilter();
        }
        #endregion

        /// <summary>Removes the game's trailing " (itemId)" suffix from a name.</summary>
        internal static string CleanItemName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;

            int suffix = name.LastIndexOf(" (", StringComparison.Ordinal);
            if (suffix > 0 && name.EndsWith(')')
                && int.TryParse(name[(suffix + 2)..^1], out _))
            {
                return name[..suffix];
            }
            return name;
        }

        private string ItemName(int id) => CleanItemName(this._data?.GetItemName(id) ?? $"#{id:D5}");

        #region 文档状态
        private void MarkDirty()
        {
            if (this._loadingDocument) return;
            if (this._documentPath == null && this._build == null) return;
            this.SetDirty(true);
        }

        private void SetDirty(bool dirty)
        {
            this._dirty = dirty;
            this.DirtyDot.Visibility = dirty ? Visibility.Visible : Visibility.Collapsed;
            if (this.SaveState != null)
            {
                this.SaveState.Text = this.Loc(
                    dirty ? "VLoc.Status.Unsaved" : "VLoc.Status.Saved",
                    dirty ? "未保存" : "已保存");
            }
            if (this._documentPath != null)
            {
                this.DocName.Text = Path.GetFileNameWithoutExtension(this._documentPath);
            }

            if (this._activeDocument != null && !this._loadingDocument)
            {
                this._activeDocument.IsDirty = dirty;
                this._activeDocument.Title = this.DocName.Text;
                this._activeDocument.Path = this._documentPath;
                this.UpdateTabHeader(this._activeDocument);
            }
        }
        #endregion

        #region 数据入口
        private void MenuItem_Open_Click(object sender, RoutedEventArgs e) => this.OpenDataFolder();

        /// <summary>启动时读取用户上次选择的目录；首次启动必须由用户选择。</summary>
        private void TryAutoOpenData()
        {
            try
            {
                if (!File.Exists(this._firstRunFile))
                {
                    this.ShowFirstRunDialog();
                    return;
                }

                string? dir = C2Appearance.DataFolder;
                if (string.IsNullOrWhiteSpace(dir) && File.Exists(this._lastDataFile))
                {
                    dir = File.ReadAllText(this._lastDataFile).Trim();
                }

                if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                {
                    this.OpenDataDirectory(dir, showError: false);
                }
                else
                {
                    this.ShowFirstRunDialog();
                }
            }
            catch (Exception)
            {
                // 记录文件损坏或目录不可用时不打断启动。
            }
        }

        private void MarkFirstRunComplete()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(this._firstRunFile)!);
                File.WriteAllText(this._firstRunFile, "1");
            }
            catch (Exception)
            {
                // A read-only profile still works for the current session.
            }
        }

        private void ShowFirstRunDialog()
        {
            if (this._firstRunDialogOpen) return;
            this._firstRunDialogOpen = true;
            try
            {
                FirstRunDialog dialog = new();
                if (dialog.ShowDialog(this) && !string.IsNullOrWhiteSpace(dialog.SelectedFolder))
                {
                    this.OpenDataDirectory(dialog.SelectedFolder, showError: true);
                }
            }
            catch (Exception)
            {
                // 首次选择失败时仍允许进入主界面，之后可通过设置重试。
            }
            finally
            {
                this._firstRunDialogOpen = false;
            }
        }

        /// <summary>选择冒险岛 Data 目录。</summary>
        private void OpenDataFolder()
        {
            Microsoft.WindowsAPICodePack.Dialogs.CommonOpenFileDialog dialog = new()
            {
                IsFolderPicker = true,
                Title = "选择包含 Base / Character / Item 的游戏 Data 目录",
            };
            if (dialog.ShowDialog() != Microsoft.WindowsAPICodePack.Dialogs.CommonFileDialogResult.Ok) return;

            this.OpenDataDirectory(dialog.FileName, showError: true);
        }

        /// <summary>打开数据目录并刷新界面。</summary>
        private void OpenDataDirectory(string dir, bool showError)
        {
            if (!Directory.Exists(dir))
            {
                if (showError) this.ShowAlert($"目录不存在：{dir}", "提示");
                return;
            }

            this.CommitActiveDocument();
            OutfitDocument? previous = this._activeDocument;
            EditSide previousSide = this._editSide;

            this._data?.Dispose();
            this._data = null;
            try
            {
                this.Cursor = Cursors.Wait;
                this._data = AvatarData.Open(dir);
            }
            catch (Exception exception)
            {
                this._data?.Dispose();
                this._data = null;
                if (showError) this.ShowAlert(exception.Message, "打开失败");
                return;
            }
            finally
            {
                this.Cursor = null;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(this._lastDataFile)!);
                File.WriteAllText(this._lastDataFile, dir);
            }
            catch (Exception)
            {
                // 记录失败不影响本次使用。
            }

            C2Appearance.SetDataFolder(dir);
            this.MarkFirstRunComplete();
            this._libraryCache.Clear();
            this.LoadLibrary();

            this.NoDataPanel.Visibility = Visibility.Collapsed;
            this.InitAvatar();

            // Re-apply the document that was open before the data switch so the
            // user does not lose the current outfit just by changing WZ folders.
            if (previous != null)
            {
                // The first document is created before auto-open finishes, so it
                // has only hair/face. Once data is available, fill the normal
                // starter outfit instead of leaving the character undressed.
                if (previous.Profile.CoatId == null
                    && previous.Profile.LongcoatId == null
                    && previous.Profile.PantsId == null
                    && previous.Profile.ShoesId == null)
                {
                    previous.Profile = this.CreateDefaultDocument().Profile;
                }

                this._loadingDocument = true;
                this._activeDocument = null;
                this.ActivateSide(previousSide, previous);
                this._loadingDocument = false;
            }

            this.UpdateInspector();
            this.LoadEquipList();
            this.UpdateStatus();
            this.SetDirty(false);
        }
        #endregion

    }
}







