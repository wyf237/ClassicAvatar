# Classic Avatar Studio · C2 Workbench Architecture

> Implemented from the C2 handoff and the follow-up review fixes.
> The window is a fixed 1280 × 800 workbench; only the preview widens visually
> between one and two columns when comparison mode is active.

## Window

- **Fixed 1280 × 800 content** inside a 1310 × 830 layered window:
  14 px transparent margin, 4 px rounded corners and a soft drop shadow.
- No resize grips, no maximize button and double-click maximize is suppressed.
- The header is a normal WPF Grid with `MouseLeftButtonDown` → `DragMove()`;
  buttons/menus/text boxes are excluded from the drag hit-test.
- 48 px header + flexible body + 24 px status bar. The old File/Edit/View/Help
  menu is intentionally removed; the header keeps brand → project → tools.
- Body columns: **320 px wardrobe / flexible preview / 300 px inspector**.
  A 20 px reveal rail replaces the wardrobe when it is collapsed.
- Version label is fixed at **V1.0** until release.

## Preview, tabs and comparison

- Preview rows: 34 px tab strip / 31 px stage head / flexible scene / 43 px transport.
- The tab strip is a **VControls `VTabControl`** with a C2 skin
  (`C2.VTabControl` / `C2.VTabItem`). Tabs show the component's active/inactive
  **indicator dot** instead of an icon; a dirty tab appends `*`.
- Each tab maps to an `OutfitDocument` (profile, title, path, dirty flag,
  action, and its own **undo/redo stacks**).
- `+` creates a new document tab; close removes it (with an unsaved prompt).
- **对比** splits the preview into two `VTabControl`s:
  - the active document stays in the left control;
  - every non-active tab moves to the right control;
  - the right control renders a static first frame of its selected document.
- Both sides are editable:
  - click a scene, or select its tab, to make that side current;
  - the active side gets an accent border and receives all equipment,
    appearance, random, undo/redo, save and export commands;
  - switching sides commits the outgoing document and loads the incoming one;
  - the inactive side keeps a static frame until it is activated.
- **结束对比** returns every tab to the main strip and keeps the document that
  was being edited.
- Main sprite is fixed at **300 DIP**; no zoom controls.
- A **native 1:1** reference card (no scaling, clipped to the card) sits at each
  scene's top-right; the old “5 items / coat / pants / shoes” caption is gone.
- Transport: play (30 px circle), localized action, live FPS, frame progress,
  undo, redo, **试试手气**, 保存, 另存, 导出. The old floor-shadow ellipse is gone.

## Rendering origin

- `AvatarAssembler.Build` is called with a **fixed 200 × 140 canvas**
  (`AvatarCanvas(200, 140, -100, -110)`), so equipment can never move the
  character origin or change the output crop.
- Character (0,0) is mapped to the feet anchor
  (`host.Height - 96`), then the sprite is scaled by `300 / 68`.
- `LayerOrder` maps the legacy `capBelowBody` layer to
  `capAccessoryBelowBody`, fixing full-body cap accessories such as 1007298.
- The floor shadow is pinned to the sprite feet, not the scene bottom.

## Visual system

- Surfaces: `Window`, `Surface`, `Surface2`, `Surface3`, `Stage`, `MiniSurface`.
- Radii: 3 px controls, 4 px cards, 5 px window.
- Borders: 1 px `VBrush.Border`; focus uses a 2 px `FocusRing`.
- Type: **Microsoft YaHei UI / Microsoft YaHei / Segoe UI**, 10–20 DIP
  (minimum 10 DIP for labels, 11.5+ for controls).
- Spacing: 4 px grid, 11/12 px panel padding.

## Colorways

- **Light** and **Dark** are the base themes and sit above a separator in the picker.
- Below the separator are **20 color-hunt-inspired themes**:
  Neo Digital, Serene Earth, Peach Fuzz, Arctic Sunrise, Retro Revival,
  Velvet Night, Citrus Pop, Soft Tech, Desert Mirage, Sakura Bloom,
  Industrial Rust, Aqua Horizon, Lush Forest, Futuristic Chrome,
  Blush & Sage, Heatwave, Nordic Calm, Cosmic Pastels, Rustic Autumn, Hyper Pop.
- Default is **Light**. The picker remembers the last choice in
  `%LOCALAPPDATA%\ClassicAvatar\ui-settings.txt`.
- Every palette has the same 69 `VBrush.*` keys, so switching only swaps one
  `ResourceDictionary` and never rebuilds the page.

```
Themes/Palettes/Light.xaml, Dark.xaml, NeoDigitalGlow.xaml, ...
Themes/C2.Tokens.xaml        metrics, radii, shadows, media icons
Themes/C2.Styles.xaml        typography, buttons, tabs, cards, progress
Services/Appearance/C2Appearance.cs     palette catalog + swap + persistence
```

## Icons

- `Resources/Icons/C2UiIcons.xaml` holds the toolbar/browser icons converted
  from the supplied `svg.txt`: close, minimize, help, settings, theme palette
  and delete. Header buttons, the browser close button and the equipped-item
  delete button bind to these geometries.
- `Resources/Icons/PartIcons.xaml` contains fourteen `Geometry` resources
  (`PIcon.Hair` … `PIcon.EarAccessory`) converted from the supplied
  1024 × 1024 SVG set (`Resources/PartIcons/*.svg`).
- Views never hard-code a fill: `Path.Fill` binds to the owning
  `ListBoxItem.Foreground`, so icons follow the theme and active state.
- `Services/Assets/IconLoader.cs` resolves `info/iconRaw` links, the paired
  `_Canvas` package, and finally renders the first weapon part canvas for
  cash/effect weapons whose icon is a 1×1 placeholder (e.g. 1702302).

## Browser behaviour

- Part selector: 14 slots, 7 × 2, 40 px tiles.
- Search resets when the part changes; hair/face hide the normal/cash filter.
- Cash filter: **全部 / 普通 / 现金**; 全部 is the default and shows all items.
- The equipment grid uses **VirtualizingWrapPanel** with recycling, so opening
  the 477–768 item slots only realizes the visible cards.
- Names/IDs are appended from a worker in 36-item UI batches; icons decode in
  parallel (up to four workers) and each VM gets its icon as soon as decoded.
  The loading overlay disappears after the first batch.
- Item right-click opens the context menu manually and never selects/applies
  the item. The menu is Copy ID / Copy Name / Favorite, or Copy ID / Copy Name /
  Remove favorite in the favorites filter. Clipboard writes run on a background
  STA thread so a locked clipboard never freezes the UI.
- Item grid: 4 fixed-width 64 px cards per row, click to try, double-click to
  pin. Icons are **32 px** with the whole item visible; names are shown without
  the trailing ` (itemId)` and every card exposes the full name in a tooltip.
- The list is filtered by the current character gender: male = male + unisex,
  female = female + unisex. Changing gender (or switching to a document with a
  different gender) reloads the list.
- Favorites can be removed directly from the favorites filter: the context menu
  shows **取消收藏** there and removes the entry immediately.
- Scrollbars use a C2 implicit style: 8 px hover hit area, no arrow buttons,
  and a 2 px thumb that grows to 8 px while the pointer is over the bar.
- The outfit library is a **two-column card wall**: each 142 px card shows a
  full-character preview rendered from the saved profile, plus name/time.
- Every appearance action (item, skin, hair, face, random, reset, undo/redo)
  applies to the **currently active tab / comparison side**.
- Random (“试试手气”) rolls a complete outfit across all 14 slots plus skin,
  hair dye is intentionally not used, and gender may be chosen if unisex.
  The roll runs off the UI thread and is discarded if the user switches
  documents before it finishes.

## Outfit dialog

- “查看完整清单” opens a **VControls `VDialog`** over the workbench, 340 px
  wide, with **all 14 positions** (hair → ear accessory):
  - title = active outfit name;
  - each row shows **slot type → icon → id → name**;
  - unequipped slots keep the row with a blank icon and `—` for id/name;
  - **复制文本** copies all rows;
  - **复制图片** renders the bordered, padded list (about 306 × 504) to the
    clipboard.
- Hair dye selection was removed from the inspector; profiles are saved with
  `HairDye = null` because arbitrary colours do not follow the game rules.
- The right inspector’s current-outfit card lists five key slots inline
  (missing values `—`); its “查看完整清单” button opens the 14-row dialog.

## Save, folders and settings

- **保存** writes the active document straight to
  `<exe>\Outfits\<title>.avatar` (the folder is created on demand, and a
  name collision overwrites the same outfit).
- **另存** opens the classic SaveFileDialog, starting in the same default
  folder.
- **导出** keeps PNG / GIF / text / `.avatar` export targets.
- All in-app prompts use **VControls.VDialog**: settings, the outfit list, simple
  alerts/errors, tab-close confirmation and the three-way unsaved-changes prompt
  (保存 / 不保存 / 取消). Native file/folder pickers remain OS dialogs.
- The header gear opens a **VControls `VDialog`** `SettingsDialog` (600 px):
  - game data folder (WZ Data), changeable at any time via folder picker;
  - default `.avatar` save folder, with “open folder”;
  - language (zh-CN / zh-TW / en-US);
  - icon-cache clear.
- `Services/Appearance/C2Appearance.cs` persists palette, language, data folder and save
  folder in `%LOCALAPPDATA%\ClassicAvatar\ui-settings.txt`; the data folder is
  applied immediately and the library reloads from the save folder.

## Localization

- zh-CN / zh-TW / en-US for all workbench strings.
- Action display names:
  - `stand1` → 站立1, `walk1` → 行走1, `stand2` → 站立2, `walk2` → 行走2;
  - other actions map to Chinese names (挥砍1/重挥1/刺击F…);
  - unknown actions fall back to `动作 (key)`, and English keeps raw keys.
- Slot names (Hair/Coat/Weapon…) are localized in the selector, summary,
  dialog and exported text.

## Code layout

```
Views/MainWindow/
  MainWindow.xaml(.cs)      fields, constructor, static UI, data entry, undo
  MainWindow.Dialogs.cs     VDialog alerts/confirmation helpers
  MainWindow.Memory.cs      cache release + working-set trimming
  MainWindow.Window.cs      header drag, browser tabs, colorways, language, settings
  MainWindow.Documents.cs   document tabs, side-aware editing, comparison
  MainWindow.Avatar.cs      build/render, fixed canvas, 1:1 card, inspector, backgrounds
  MainWindow.Equipment.cs   part selector, loading, filters, context actions
  MainWindow.Library.cs     outfit library, .avatar open/drag-drop
  MainWindow.File.cs        file menu, quick save/save-as/export commands
  MainWindow.ViewModels.cs  template view models, OutfitDocument + per-tab history
Views/Dialogs/
  OutfitListDialog.xaml(.cs)      compact five-row outfit list with clipboard export
  UnsavedChangesDialog.xaml(.cs)  three-way VDialog save/discard/cancel prompt
  SettingsDialog.xaml(.cs)        data folder, save folder, language, cache
Services/Assets/IconLoader.cs     cached WZ icon decoder + cash-weapon fallback
Services/Appearance/C2Appearance.cs  palette/language/data/save-folder persistence
docs/ARCHITECTURE.md               this document
docs/PERFORMANCE.md                memory + threading notes
docs/ERROR_HANDLING.md             dialog/exception policy
```

## Adding a colorway

1. Copy an existing palette and keep every `VBrush.*` key.
2. Change the `VControls.ThemeName` marker.
3. Add a `C2Palette` enum value, a `C2PaletteInfo` catalog entry, and the file.
4. Call `C2Appearance.ApplyPalette`; do not touch the view.
