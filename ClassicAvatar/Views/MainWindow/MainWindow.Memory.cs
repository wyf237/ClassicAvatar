#nullable enable
// Working-set trimming helpers used after large equipment/library reloads.
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avatar.Core.Assets;
using Avatar.Core.Models;
using ClassicAvatar.Services;

namespace ClassicAvatar
{
    public partial class MainWindow
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessWorkingSetSize(
            IntPtr process,
            IntPtr minimumWorkingSetSize,
            IntPtr maximumWorkingSetSize);

        private bool IsSlotPackageInUse(EquipSlot slot)
        {
            string packagePath = ItemPathResolver.GetPackagePath(slot);
            foreach (EquipSlot candidate in PanelSlots)
            {
                if (this._slotSelections.TryGetValue(candidate, out int? id)
                    && id is int
                    && string.Equals(
                        ItemPathResolver.GetPackagePath(candidate),
                        packagePath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private void ClearAllCaches()
        {
            IconLoader.ClearCache();
            try
            {
                // Close every package that is not backing a currently worn
                // item. This releases the WZ file handles and the parsed image
                // tree, not just the icon bitmaps.
                foreach (EquipSlot slot in PanelSlots)
                {
                    if (!this.IsSlotPackageInUse(slot))
                    {
                        this._data?.ReleaseSlotPackage(slot);
                    }
                }

                this._data?.ClearImageCaches();
            }
            catch (Exception)
            {
            }

            // Reparse only the handful of currently worn parts, not every item
            // that was visited in the browser.
            if (this._data != null && this._build != null)
            {
                try
                {
                    this.Rebuild();
                }
                catch (Exception)
                {
                }
            }

            this.TrimMemory();
        }

        /// <summary>
        /// Drops dead equipment bitmaps/WZ canvases and returns their pages to
        /// the OS. Call only after a list swap or an explicit cache clear.
        /// </summary>
        private void TrimMemory()
        {
            _ = Task.Run(() =>
            {
                try
                {
                    GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                    GC.WaitForPendingFinalizers();
                    GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);

                    using Process process = Process.GetCurrentProcess();
                    SetProcessWorkingSetSize(process.Handle, new IntPtr(-1), new IntPtr(-1));
                }
                catch (Exception)
                {
                }
            });
        }
    }
}
