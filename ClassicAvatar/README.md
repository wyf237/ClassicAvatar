# ClassicAvatar Workbench

WPF avatar editor for the bundled classic MapleStory data set.

## Layout

```
ClassicAvatar/
  App.xaml(.cs)                 application entry + merged dictionaries
  Views/MainWindow/             main window and partials
  Views/Dialogs/                VDialog dialogs
  Services/Appearance/          palette/language/settings persistence
  Services/Assets/              WZ icon decoder + cache
  Themes/                       tokens, C2 styles, 22 palettes
  Resources/                    SVG icons, part glyphs, backgrounds, strings
  docs/                         architecture, performance, error handling, manuals
  docs/manual/                  zh-CN / zh-TW / en-US PDF user guides
```

## Documentation

- [Architecture](docs/ARCHITECTURE.md)
- [Performance](docs/PERFORMANCE.md)
- [Error handling](docs/ERROR_HANDLING.md)

## First launch

No data path is bundled. On first launch the app asks the user to choose the
MapleStory `Data` folder (the one containing `Base`, `Character`, and `Item`).
The choice is remembered in `%LOCALAPPDATA%\ClassicAvatar\ui-settings.txt`;
`first-run.txt` marks the initial selection.

## Build

```powershell
dotnet build ClassicAvatar/ClassicAvatar.csproj
```

Data directory, save directory and language are stored in
`%LOCALAPPDATA%\ClassicAvatar\ui-settings.txt`.
