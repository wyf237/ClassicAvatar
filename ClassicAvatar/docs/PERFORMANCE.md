# Performance notes

## Measured effect

Measurements are working set (WS) / private bytes from the real
`ClassicAvatar.exe` on the bundled v0.83 data set.

| State | Before | After |
|---|---:|---:|
| Launch, first slot visible | ~380 MB / ~358 MB | ~77 MB / ~180 MB |
| Seven slot switches, old behavior | ~794 MB / ~779 MB | — |
| Seven slot switches, idle release | — | **~82 MB / ~316 MB** |
| Settings → clear caches (after browsing) | — | ~353 MB private / ~56 MB WS |

The exact values depend on the WZ packages that the current outfit still
needs and on how quickly Windows releases WPF/WIC pages. All values are lower
than the old behavior, and the working set returns to tens of MB after a slot
change settles.

> Working set and private bytes are reported separately. Closing WZ packages
> and clearing canvases is what lowers committed memory; the working-set trim
> only makes the resident pages reflect that immediately.

## What is released

1. **Per-slot icon cache** — `IconLoader.ClearCache(slot)` removes the frozen
   `BitmapSource` cards from the previous equipment slot.
2. **WZ package** — `AvatarData.ReleaseSlotPackage(slot)` clears the slot's
   catalog/part/dye entries, disposes the per-slot `WzPackage`, and resets its
   lazy loader. Browsing the slot again reopens it on demand.
3. **Idle full cache release** — 1.5 s after the last icon finishes,
   `ClearAllCaches()` closes non-worn packages, unparses all loaded WZ canvases,
   reparses only the worn outfit, forces a compacting GC and trims the process
   working set. A second pass 2.5 s later catches late WPF/WIC pages.
4. **Library cache** — entries for deleted `.avatar` files are pruned on every
   library reload.
5. **Manual clear** — Settings → 清除图标缓存 closes every non-worn package and
   trims the working set.

## Threading

- Slot enumeration runs on `Task.Run`; names are batched (36 items) to the UI.
- Icons decode in `Parallel.ForEach` with `MaxDegreeOfParallelism` clamped to
  the CPU count (4–8). Icons are applied one by one with cancellation checks,
  so scrolling and tab switching stay responsive.
- Clipboard writes run on a background STA thread with a short retry loop; the
  UI thread never sleeps on a locked clipboard.
- Cancellation tokens invalidate stale slot loads when the user clicks another
  slot quickly. The idle cache release is also debounced/cancelled.
- Library previews are reused from `_libraryCache` keyed by file write time;
  deleted files are evicted.

## Notes

- The first load still parses the data package for the slot the user opens.
  This is unavoidable; subsequent switches close the previous package.
- Forcing a GC after a switch is intentional: it keeps the long-running app
  from accumulating WZ and WPF native memory. The release happens after the
  current icon pass finishes and the list is idle, not during the click.
- `SetProcessWorkingSetSize(-1, -1)` only trims resident pages; closing
  packages and clearing canvases is what returns committed memory.
