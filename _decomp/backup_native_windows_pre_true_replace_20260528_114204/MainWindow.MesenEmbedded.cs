// Hosts an embedded Mesen2 instance (via MesenCore.dll) inside the editor's
// MainWindow.  Replaces the Process.Start launch path when
// Option_EmbeddedMesen is true.

using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using FamidashEditor.MesenEmbedded;

namespace FamidashEditor
{
    public partial class MainWindow
    {
        private MesenHwndHost? _mesenEmbeddedHwndHost;
        private bool _mesenEmbeddedShown;

        // Default width of the embedded Mesen column (NES 256 * 2 + ~20 for chrome).
        private const double MesenEmbeddedDefaultWidth = 532;

        /// <summary>
        /// Show the Mesen panel (creates the child HWND if needed) and bring up
        /// the emulator core, then load the ROM and overlay Lua.
        /// Returns true on success.
        /// </summary>
        internal bool OpenRomInEmbeddedMesen(string romPath, string luaPath)
        {
            try
            {
                EnsureEmbeddedPanelVisible();

                if (_mesenEmbeddedHwndHost == null || _mesenEmbeddedHwndHost.Hwnd == IntPtr.Zero)
                {
                    MessageBox.Show(this, "Embedded Mesen panel failed to create its host HWND.",
                        "Embedded Mesen", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }

                // One-time bring-up: bind to the main editor HWND (input/audio target)
                // and the child HWND (D3D11 viewer).  homeFolder is the bundled mesen
                // dir so Mesen looks for its config alongside the DLL.
                bool firstBringUp = !MesenEmbeddedController.Initialized;
                if (firstBringUp)
                {
                    IntPtr topLevelHwnd = new WindowInteropHelper(this).Handle;
                    string homeFolder = MesenInterop.BundledMesenDir;

                    // noInput=true so Mesen ignores the editor keyboard; replay
                    // inputs are injected by the overlay Lua via emu.setInput.
                    MesenEmbeddedController.Initialize(
                        topLevelHwnd: topLevelHwnd,
                        viewerHwnd:   _mesenEmbeddedHwndHost.Hwnd,
                        homeFolder:   homeFolder,
                        noInput:      true);
                }

                // Load the ROM.
                if (!MesenEmbeddedController.LoadRom(romPath))
                {
                    MessageBox.Show(this, "Embedded Mesen: LoadRom failed for " + romPath,
                        "Embedded Mesen", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }

                // Push the current panel size to the renderer.  Must come AFTER
                // LoadRom on first bring-up: SetRendererSize touches the swap-
                // chain back buffer which Mesen only allocates once a console
                // is loaded.  Calling it before LoadRom AVs in MesenCore.dll.
                UpdateEmbeddedMesenRendererSize();

                // Load the overlay Lua (replay inputs + path overlay).
                if (!string.IsNullOrEmpty(luaPath) && File.Exists(luaPath))
                {
                    MesenEmbeddedController.LoadOverlayLua(luaPath);
                }

                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Embedded Mesen launch failed:\n" + ex.Message,
                    "Embedded Mesen", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        internal void CloseEmbeddedMesen()
        {
            try { MesenEmbeddedController.UnloadOverlayLua(); } catch { }
            try { MesenEmbeddedController.Stop(); } catch { }
            HideEmbeddedPanel();
        }

        private void EnsureEmbeddedPanelVisible()
        {
            if (MesenEmbeddedHost == null) return;

            if (_mesenEmbeddedHwndHost == null)
            {
                _mesenEmbeddedHwndHost = new MesenHwndHost();
                MesenEmbeddedHost.Child = _mesenEmbeddedHwndHost;
                MesenEmbeddedHost.SizeChanged += MesenEmbeddedHost_SizeChanged;
            }

            if (!_mesenEmbeddedShown)
            {
                if (MesenSplitterCol != null)
                    MesenSplitterCol.Width = new GridLength(5);
                if (MesenPanelCol != null)
                    MesenPanelCol.Width = new GridLength(MesenEmbeddedDefaultWidth);
                if (MesenSplitter != null)
                    MesenSplitter.Visibility = Visibility.Visible;
                MesenEmbeddedHost.Visibility = Visibility.Visible;
                _mesenEmbeddedShown = true;
            }
        }

        private void HideEmbeddedPanel()
        {
            if (MesenEmbeddedHost == null) return;
            if (MesenPanelCol != null)    MesenPanelCol.Width    = new GridLength(0);
            if (MesenSplitterCol != null) MesenSplitterCol.Width = new GridLength(0);
            if (MesenSplitter != null)    MesenSplitter.Visibility = Visibility.Collapsed;
            MesenEmbeddedHost.Visibility = Visibility.Collapsed;
            _mesenEmbeddedShown = false;
        }

        private void MesenEmbeddedHost_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateEmbeddedMesenRendererSize();
        }

        private void UpdateEmbeddedMesenRendererSize()
        {
            if (!MesenEmbeddedController.Initialized) return;
            if (MesenEmbeddedHost == null) return;
            double w = MesenEmbeddedHost.ActualWidth;
            double h = MesenEmbeddedHost.ActualHeight;
            if (w < 1 || h < 1) return;
            // Account for DPI so Mesen's swap chain matches actual pixels.
            var src = PresentationSource.FromVisual(this);
            double dpiX = 1.0, dpiY = 1.0;
            if (src?.CompositionTarget != null)
            {
                dpiX = src.CompositionTarget.TransformToDevice.M11;
                dpiY = src.CompositionTarget.TransformToDevice.M22;
            }
            MesenEmbeddedController.SetRendererSize(
                (int)Math.Round(w * dpiX),
                (int)Math.Round(h * dpiY));
        }
    }
}
