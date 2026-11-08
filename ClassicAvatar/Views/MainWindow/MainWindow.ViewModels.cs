#nullable enable
// Small view models used by the XAML templates.
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
        #region 视图模型
        public sealed class SkinVm(int index, string name, string color)
        {
            public int Index { get; } = index;
            public string Name { get; } = name;
            public Brush Color { get; } = (Brush)new BrushConverter().ConvertFromString(color)!;
        }

        public sealed class SlotVm(EquipSlot slot, string name, Geometry icon)
        {
            public EquipSlot Slot { get; } = slot;
            public string Name { get; } = name;

            /// <summary>Theme-coloured part glyph (see Resources/Icons/PartIcons.xaml).</summary>
            public Geometry Icon { get; } = icon;
        }

        /// <summary>One slot line in the current-outfit summary.</summary>
        public sealed class OutfitLineVm(string label, string value)
        {
            public string Label { get; } = label;
            public string Value { get; } = value;
        }

        public sealed class EquipItemVm : INotifyPropertyChanged
        {
            private BitmapSource? _icon;

            public EquipItemVm(int id, string name)
            {
                this.Id = id;
                this.Name = CleanItemName(name);
            }

            public int Id { get; }
            public string Name { get; }
            public string IdText => $"#{this.Id:D5}";
            public string ToolTip => $"{this.Name}\n#{this.Id:D5}";

            public BitmapSource? Icon
            {
                get => this._icon;
                set
                {
                    if (ReferenceEquals(this._icon, value)) return;
                    this._icon = value;
                    this.OnPropertyChanged();
                }
            }

            public event PropertyChangedEventHandler? PropertyChanged;

            private void OnPropertyChanged([CallerMemberName] string? name = null) =>
                this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private sealed class BackgroundItem(string name, string path, ImageSource preview)
        {
            public string Name { get; } = name;
            public string Path { get; } = path;
            public ImageSource Preview { get; } = preview;
        }

        private sealed class LibraryItemVm(string name, string path, ImageSource? preview, string meta, string pathText)
        {
            public string Name { get; } = name;
            public string Path { get; } = path;
            public ImageSource? Preview { get; } = preview;
            public string Meta { get; } = meta;
            public string PathText { get; } = pathText;
        }
        #endregion
    }

    /// <summary>One open outfit document (tab).</summary>
    public sealed class OutfitDocument
    {
        public string Title { get; set; } = "Untitled";
        public string? Path { get; set; }
        public bool IsDirty { get; set; }
        public CharacterProfile Profile { get; set; } = new();
        public string Action { get; set; } = "stand1";

        /// <summary>Per-tab undo/redo history; switching tabs never mixes them.</summary>
        public Stack<DocumentSnapshot> Undo { get; } = new();
        public Stack<DocumentSnapshot> Redo { get; } = new();
    }

    /// <summary>One undoable editor state for a single outfit document.</summary>
    public readonly record struct DocumentSnapshot(CharacterProfile Profile);

    /// <summary>One row in the modal outfit list (icon / id / name).</summary>
    public sealed class OutfitRowVm(
        string slotName,
        string idText,
        string name,
        ImageSource? icon)
    {
        public string SlotName { get; } = slotName;
        public string IdText { get; } = idText;
        public string Name { get; } = name;
        public ImageSource? Icon { get; } = icon;
    }
}

