using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace HypaceTray
{
    // Where the merged block goes, in taskbar coordinates (the block is a child window of the taskbar),
    // vertically centred.
    public static class BadgeLayout
    {
        // Preferred: centred on our own (transparent) tray icon's slot, right of ^, so the tray keeps room for it.
        public static bool TryPlaceOnSlot(Rectangle taskbar, Rectangle slot, Size badge, out Point pos)
        {
            pos = Point.Empty;
            if (taskbar.Width <= taskbar.Height || slot.Width <= 0 || slot.Height <= 0 || !taskbar.Contains(slot)) return false;
            pos = new Point(slot.Left - taskbar.Left + (slot.Width - badge.Width) / 2, (taskbar.Height - badge.Height) / 2);
            return true;
        }

        // Otherwise: flush against the left edge of the tray area, in the empty taskbar there.
        public static bool TryPlace(Rectangle taskbar, Rectangle trayArea, Size badge, out Point pos)
        {
            pos = Point.Empty;
            if (taskbar.Width <= taskbar.Height || trayArea.Width <= 0 || trayArea.Height <= 0) return false;
            int x = trayArea.Left - taskbar.Left - badge.Width;
            if (x < 0) return false;
            pos = new Point(x, (taskbar.Height - badge.Height) / 2);
            return true;
        }
    }

    // The screen slot of one of our tray icons, when Windows shows it on the taskbar (not in the ^ overflow).
    static class TraySlot
    {
        static readonly FieldInfo WindowField = typeof(NotifyIcon).GetField("window", BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly FieldInfo IdField = typeof(NotifyIcon).GetField("id", BindingFlags.NonPublic | BindingFlags.Instance);
        static string cachedKey;   // the icon's entry under NotifyIconSettings, found once

        public static bool TryGet(NotifyIcon icon, out Rectangle rect)
        {
            rect = Rectangle.Empty;
            if (WindowField == null || IdField == null || !icon.Visible) return false;
            var window = WindowField.GetValue(icon) as NativeWindow;
            if (window == null || window.Handle == IntPtr.Zero) return false;
            int id = (int)IdField.GetValue(icon);
            // for an icon in the overflow Windows reports the ^ button's rectangle, so ask whether it is shown first
            if (!IsShownOnTaskbar(id)) return false;
            var nii = new NOTIFYICONIDENTIFIER { cbSize = Marshal.SizeOf(typeof(NOTIFYICONIDENTIFIER)), hWnd = window.Handle, uID = (uint)id };
            RECT r;
            if (Shell_NotifyIconGetRect(ref nii, out r) != 0) return false;
            rect = Rectangle.FromLTRB(r.L, r.T, r.R, r.B);
            return rect.Width > 0 && rect.Height > 0;
        }

        // Windows 11 keeps "show on taskbar" per icon (exe path + ID) under HKCU\Control Panel\NotifyIconSettings.
        // Read only; the user changes it by dragging the icon in or out of ^.
        static bool IsShownOnTaskbar(int id)
        {
            try
            {
                using (var root = Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings"))
                {
                    if (root == null) return false;
                    if (cachedKey != null)
                    {
                        bool shown;
                        if (Read(root, cachedKey, id, out shown)) return shown;
                        cachedKey = null;
                    }
                    foreach (var name in root.GetSubKeyNames())
                    {
                        bool shown;
                        if (Read(root, name, id, out shown)) { cachedKey = name; return shown; }
                    }
                }
            }
            catch (Exception) { }
            return false;
        }

        static bool Read(RegistryKey root, string name, int id, out bool shown)
        {
            shown = false;
            using (var k = root.OpenSubKey(name))
            {
                if (k == null) return false;
                var path = k.GetValue("ExecutablePath") as string;
                var uid = k.GetValue("UID");
                if (path == null || !(uid is int) || (int)uid != id
                    || !string.Equals(path, Application.ExecutablePath, StringComparison.OrdinalIgnoreCase)) return false;
                var promoted = k.GetValue("IsPromoted");
                shown = promoted is int && (int)promoted == 1;
                return true;
            }
        }

        [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
        [StructLayout(LayoutKind.Sequential)]
        struct NOTIFYICONIDENTIFIER { public int cbSize; public IntPtr hWnd; public uint uID; public Guid guidItem; }
        [DllImport("shell32.dll")] static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER id, out RECT rect);
    }

    // The merged number + mouse block, shown as a small per-pixel-alpha child window of the taskbar (how taskbar
    // meters such as TrafficMonitor appear on Windows 11), over our transparent tray icon's slot or just left of
    // the tray area. This is not a documented extension point, so Track() re-finds the taskbar on every call and
    // returns false whenever it cannot attach; the app then shows tray icons instead.
    sealed class TaskbarBadge : NativeWindow, IDisposable
    {
        public event EventHandler LeftClick, RightClick;

        Bitmap image;
        string tipText = "";
        IntPtr tip = IntPtr.Zero, parent = IntPtr.Zero;
        Point pos;

        // Takes ownership of the bitmap.
        public void SetImage(Bitmap bmp)
        {
            if (image != null) image.Dispose();
            image = bmp;
            if (Handle != IntPtr.Zero && !Push()) Detach();
        }

        public void SetTip(string text)
        {
            tipText = text ?? "";
            if (tip != IntPtr.Zero) { var ti = Tool(); SendMessage(tip, TTM_UPDATETIPTEXTW, IntPtr.Zero, ref ti); }
        }

        // Attaches to the taskbar if needed and keeps the block in place: centred on the given tray slot (screen
        // coordinates), or with no slot just left of the tray area. False when there is nowhere to put it
        // (no taskbar, vertical taskbar, no room, or Windows refused the window).
        public bool Track(Rectangle? slot)
        {
            if (image == null) return false;
            IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
            Point p;
            if (!Place(taskbar, slot, out p))
            {
                Detach();
                return false;
            }

            // explorer restarted (new taskbar) or our window went away with the old one
            if (Handle != IntPtr.Zero && (!IsWindow(Handle) || parent != taskbar)) Detach();

            if (Handle == IntPtr.Zero)
            {
                if (tip != IntPtr.Zero) { DestroyWindow(tip); tip = IntPtr.Zero; }
                var cp = new CreateParams
                {
                    Caption = "HypaceTray",
                    Parent = taskbar,
                    X = p.X, Y = p.Y, Width = image.Width, Height = image.Height,
                    Style = WS_CHILD | WS_CLIPSIBLINGS,
                    ExStyle = WS_EX_LAYERED | WS_EX_NOPARENTNOTIFY,
                };
                try { CreateHandle(cp); }
                catch (Exception) { return false; }
                parent = taskbar;
                if (!Push()) { Detach(); return false; }
                AddTip();
                pos = p;
                SetWindowPos(Handle, HWND_TOP, p.X, p.Y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
                return true;
            }

            // follow the tray area as icons come and go; stay above explorer's own children
            if (p != pos || GetWindow(Handle, GW_HWNDPREV) != IntPtr.Zero)
            {
                pos = p;
                SetWindowPos(Handle, HWND_TOP, p.X, p.Y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            }
            return true;
        }

        bool Place(IntPtr taskbar, Rectangle? slot, out Point p)
        {
            p = Point.Empty;
            RECT tb;
            if (taskbar == IntPtr.Zero || !GetWindowRect(taskbar, out tb)) return false;
            if (slot.HasValue) return BadgeLayout.TryPlaceOnSlot(tb.ToRectangle(), slot.Value, image.Size, out p);
            IntPtr trayArea = FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
            RECT ta;
            if (trayArea == IntPtr.Zero || !GetWindowRect(trayArea, out ta)) return false;
            return BadgeLayout.TryPlace(tb.ToRectangle(), ta.ToRectangle(), image.Size, out p);
        }

        public void Dispose()
        {
            Detach();
            if (image != null) { image.Dispose(); image = null; }
        }

        void Detach()
        {
            if (tip != IntPtr.Zero) { DestroyWindow(tip); tip = IntPtr.Zero; }
            if (Handle != IntPtr.Zero)
            {
                if (IsWindow(Handle)) DestroyHandle();
                else ReleaseHandle();
            }
            parent = IntPtr.Zero;
        }

        // Hands the bitmap (with its alpha) to Windows; also sizes the window to it.
        bool Push()
        {
            IntPtr screen = GetDC(IntPtr.Zero), mem = CreateCompatibleDC(screen), hbmp = IntPtr.Zero, old = IntPtr.Zero;
            try
            {
                hbmp = image.GetHbitmap(Color.FromArgb(0));
                old = SelectObject(mem, hbmp);
                var size = new SIZE { cx = image.Width, cy = image.Height };
                var src = new POINT();
                var blend = new BLENDFUNCTION { BlendOp = AC_SRC_OVER, SourceConstantAlpha = 255, AlphaFormat = AC_SRC_ALPHA };
                return UpdateLayeredWindow(Handle, screen, IntPtr.Zero, ref size, mem, ref src, 0, ref blend, ULW_ALPHA);
            }
            catch (Exception) { return false; }
            finally
            {
                if (old != IntPtr.Zero) SelectObject(mem, old);
                if (hbmp != IntPtr.Zero) DeleteObject(hbmp);
                DeleteDC(mem);
                ReleaseDC(IntPtr.Zero, screen);
            }
        }

        void AddTip()
        {
            var icc = new INITCOMMONCONTROLSEX { dwSize = Marshal.SizeOf(typeof(INITCOMMONCONTROLSEX)), dwICC = ICC_WIN95_CLASSES };
            InitCommonControlsEx(ref icc);
            tip = CreateWindowEx(WS_EX_TOPMOST, "tooltips_class32", null, TTS_ALWAYSTIP | TTS_NOPREFIX,
                CW_USEDEFAULT, CW_USEDEFAULT, CW_USEDEFAULT, CW_USEDEFAULT, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (tip == IntPtr.Zero) return;
            SendMessage(tip, TTM_SETMAXTIPWIDTH, IntPtr.Zero, (IntPtr)400);   // allows "\n" line breaks
            var ti = Tool();
            SendMessage(tip, TTM_ADDTOOLW, IntPtr.Zero, ref ti);
        }

        TOOLINFO Tool()
        {
            return new TOOLINFO
            {
                cbSize = Marshal.SizeOf(typeof(TOOLINFO)),
                uFlags = TTF_IDISHWND | TTF_SUBCLASS,
                hwnd = Handle,
                uId = Handle,
                lpszText = tipText,
            };
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case WM_NCHITTEST: m.Result = (IntPtr)HTCLIENT; return;
                case WM_MOUSEACTIVATE: m.Result = (IntPtr)MA_NOACTIVATE; return;
                // handled here and not passed on, so the taskbar never sees the clicks or shows its own menu
                case WM_LBUTTONDOWN: case WM_RBUTTONDOWN: case WM_CONTEXTMENU: return;
                case WM_LBUTTONUP: if (LeftClick != null) LeftClick(this, EventArgs.Empty); return;
                case WM_RBUTTONUP: if (RightClick != null) RightClick(this, EventArgs.Empty); return;
            }
            base.WndProc(ref m);
        }

        // ---------------- Win32 ----------------

        const int WS_CHILD = 0x40000000, WS_CLIPSIBLINGS = 0x04000000;
        const int WS_EX_LAYERED = 0x00080000, WS_EX_NOPARENTNOTIFY = 0x00000004, WS_EX_TOPMOST = 0x00000008;
        const int WM_NCHITTEST = 0x84, WM_MOUSEACTIVATE = 0x21, WM_CONTEXTMENU = 0x7B;
        const int WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202, WM_RBUTTONDOWN = 0x204, WM_RBUTTONUP = 0x205;
        const int HTCLIENT = 1, MA_NOACTIVATE = 3, GW_HWNDPREV = 3;
        const uint SWP_NOSIZE = 0x1, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;
        static readonly IntPtr HWND_TOP = IntPtr.Zero;
        const byte AC_SRC_OVER = 0, AC_SRC_ALPHA = 1;
        const int ULW_ALPHA = 2;
        const int TTS_ALWAYSTIP = 0x1, TTS_NOPREFIX = 0x2, CW_USEDEFAULT = unchecked((int)0x80000000);
        const int TTF_IDISHWND = 0x1, TTF_SUBCLASS = 0x10;
        const int TTM_SETMAXTIPWIDTH = 0x418, TTM_ADDTOOLW = 0x432, TTM_UPDATETIPTEXTW = 0x439;
        const int ICC_WIN95_CLASSES = 0xFF;

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int L, T, R, B; public Rectangle ToRectangle() { return Rectangle.FromLTRB(L, T, R, B); } }
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int x, y; }
        [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
        [StructLayout(LayoutKind.Sequential)] struct INITCOMMONCONTROLSEX { public int dwSize, dwICC; }
        // TTTOOLINFOW_V2 layout (no lpReserved): accepted by both comctl32 v5 and v6
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct TOOLINFO
        {
            public int cbSize, uFlags;
            public IntPtr hwnd, uId;
            public RECT rect;
            public IntPtr hinst;
            public string lpszText;
            public IntPtr lParam;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string name);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr CreateWindowEx(int exStyle, string cls, string name, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, ref TOOLINFO l);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("user32.dll")]
        static extern bool UpdateLayeredWindow(IntPtr h, IntPtr dst, IntPtr pptDst, ref SIZE size, IntPtr src, ref POINT pptSrc, int key, ref BLENDFUNCTION blend, int flags);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        [DllImport("comctl32.dll")] static extern bool InitCommonControlsEx(ref INITCOMMONCONTROLSEX icc);
    }
}
