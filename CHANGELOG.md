# Changelog

## V1.0.1

### Added

- Delete button on saved outfit cards, with a VDialog confirmation and cleanup of any open tab that pointed to the deleted file.
- Empty-state hints for the equipment browser: favorites, recent items, search results, and empty categories.
- Larger outfit list dialog with a current-outfit preview and a full character portrait; "Copy image" now captures the list and both previews.
- Multi-tab unsaved-close prompt listing every dirty tab; save-all while closing.
- VTextBox placeholder support for the equipment and library search boxes.

### Changed

- Switching gender now converts the outfit correctly: female starter coat/pants, gender-restricted items removed, unisex pieces kept, and the browser reloaded for the new gender.
- ItemDescriptor now infers gender for coats, longcoats, and pants in addition to hair and face.
- Version updated to 1.0.1.

### Fixed

- Repository project references and solution paths now point at the bundled `Dependencies/` sources instead of an external workspace.
- Added the missing VTextBox source/resource files to the repository dependencies.
- Two-handed spear/polearm weapons hide the shield even when their `vslot` is only `Wp` (for example 1432002–1432006).
- VDialog scrim is clamped to the rounded window content instead of extending into the transparent shadow margin.

## V1.0

- Initial public release: local offline avatar workbench, 14 equipment slots, 22 themes, three UI languages, VDialog dialogs, PNG/GIF/TXT/`.avatar` export, full and lite release packages.
