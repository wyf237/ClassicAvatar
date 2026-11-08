# Error handling

## UI policy

All in-app prompts use **VControls.VDialog**:

- `ShowAlert(message, title)` — single confirm button for informational and
  error messages.
- `ShowConfirm(message, title, confirmText, cancelText)` — two-button
  confirmation.
- `UnsavedChangesDialog` — three-way save / discard / cancel prompt.
- `SettingsDialog`, `OutfitListDialog` — VDialog based.

Native `SaveFileDialog` and `CommonOpenFileDialog` are still OS dialogs for
file/folder selection.

## Boundaries

- Slot enumeration and icon decode: per-item try/catch; a broken image becomes
  an empty card, not a crash. `IconLoader.LoadIcon` has a final catch as well.
- Slot switching: previous package release is wrapped; a failed release only
  leaves memory allocated, it cannot break the new list.
- Cash filter metadata lookup: `IsCashItem` catches malformed descriptors.
- Clipboard: writes happen on a background STA thread; `ExternalException` is
  retried, then silently dropped.
- Library load: directory creation, `.avatar` parse, preview render and cache
  pruning are each guarded; an unreadable file is skipped.
- Window close with unsaved work: the VDialog is wrapped; if the prompt itself
  fails, the window stays open (`Cancel`) rather than losing data.
- Open data: directory validation, previous-document restore and WZ open are
  guarded; failures show a VDialog alert and keep the previous data.
- Export/save: IO, encoder and bitmap errors are reported through `ShowAlert`.
- Settings cache clear: wrapped; failure cannot close the dialog.
- VDialog helpers catch their own failures so an alert problem does not take
  down the workbench.

## Manual checks

- Close a dirty tab → VDialog confirmation.
- Close the window with unsaved work → three-button VDialog.
- Invalid data directory → VDialog alert, old data preserved.
- Rapid slot switching → no stale icons, no crash.
- Right-click item → menu never selects/applies the item.
- Locked clipboard → UI remains responsive.
