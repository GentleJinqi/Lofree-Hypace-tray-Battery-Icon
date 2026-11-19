using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace HypaceTray
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            bool created;
            using (var mutex = new Mutex(true, "HypaceTray.SingleInstance", out created))
            {
                if (!created) return;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayContext());
            }
        }
    }

    sealed class TrayContext : ApplicationContext
    {
        const string AppName = "HypaceTray";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string WebDriverUrl = "https://www.lofree.tech/";
        const int PollOnlineMs = 60000, PollOfflineMs = 20000, PollNoReceiverMs = 5000;

        // Main display: one block (number + mouse) on the taskbar. A tray icon is always a square slot, so the block
        // is our own child window of the taskbar rather than a tray icon. It sits over the number tray icon's slot,
        // right of ^: that icon stays in the tray, drawn fully transparent, so the tray keeps room for the block.
        // Windows remembers icons by ID (1, 2 in creation order); the number icon is created first to keep ID 1, the
        // slot the user already made visible. If that icon is in the ^ overflow, the block sits just left of the tray
        // area instead, and the number icon shows the number (dragging it out of ^ moves the block into its slot);
        // a notification tells the user so once (TrayHint), as a new exe location always starts under ^.
        // If the block cannot attach to the taskbar at all, both tray icons show their pictures.
        // A low-battery alert needs a visible tray icon: the mouse icon appears for the alert and hides again.
        enum Placement { None, InSlot, BesideTray, TrayIcons }
        const int AlertIconMs = 10000;
        readonly NotifyIcon numberTray, mouseTray;
        readonly TaskbarBadge badge = new TaskbarBadge();
        readonly NativeWindow menuOwner = new NativeWindow();
        readonly System.Windows.Forms.Timer trackTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        readonly System.Windows.Forms.Timer alertTimer = new System.Windows.Forms.Timer { Interval = AlertIconMs };
        readonly ContextMenuStrip menu;
        readonly Icon blankIcon;
        readonly IntPtr blankHandle;
        Icon numberIcon;
        Placement placement = Placement.None;
        readonly TrayHint trayHint;
        readonly ToolStripMenuItem statusItem, refreshItem, autoStartItem, webItem, numberItem, languageItem, englishItem, chineseItem, exitItem;
        readonly Control ui;
        readonly Thread worker;
        readonly AutoResetEvent poke = new AutoResetEvent(false);
        readonly string stateFile, settingsFile;
        readonly Dictionary<string, string> settings;
        UiText text;

        // mouse style: drawn, or a product photo (built into the program, one per colourway)
        readonly ToolStripMenuItem styleItem, drawnItem, realisticItem, greyWhiteItem, seeThroughItem, levelColourItem, boltItem;
        readonly Dictionary<Colourway, ToolStripMenuItem> colourwayItems = new Dictionary<Colourway, ToolStripMenuItem>();
        readonly MouseSettings mouseChoice;
        readonly Dictionary<Colourway, MousePhoto> photos = new Dictionary<Colourway, MousePhoto>();
        volatile bool exiting;
        IntPtr numberHandle = IntPtr.Zero, mouseHandle = IntPtr.Zero;

        // UI-thread state
        int shownLevel = -1;
        bool charging, online, receiverFound, everOnline;
        int voltage;
        DateTime lastSeen = DateTime.MinValue;
        bool warned20, warned10;

        public TrayContext()
        {
            ui = new Control();
            var forceHandle = ui.Handle;

            string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);
            stateFile = Path.Combine(dataDir, "last.txt");
            settingsFile = Path.Combine(dataDir, "settings.txt");
            LoadState();
            settings = LoadSettings();
            string savedLanguage;
            settings.TryGetValue("language", out savedLanguage);
            text = UiText.For(UiText.ParseLanguage(savedLanguage));
            string hintShownFor;
            settings.TryGetValue("trayHintShown", out hintShownFor);
            trayHint = new TrayHint(hintShownFor, Application.ExecutablePath);

            statusItem = new ToolStripMenuItem(text.Connecting) { Enabled = false };
            refreshItem = new ToolStripMenuItem("", null, delegate { poke.Set(); });
            autoStartItem = new ToolStripMenuItem("", null, delegate { ToggleAutoStart(); });
            autoStartItem.Checked = IsAutoStart();
            webItem = new ToolStripMenuItem("", null, delegate { try { Process.Start(WebDriverUrl); } catch { } });
            // each language is named in itself, so it can be found whatever the menu is showing
            englishItem = new ToolStripMenuItem("English", null, delegate { SetLanguage(Language.English); });
            chineseItem = new ToolStripMenuItem("中文", null, delegate { SetLanguage(Language.Chinese); });
            languageItem = new ToolStripMenuItem("");
            languageItem.DropDownItems.AddRange(new ToolStripItem[] { englishItem, chineseItem });
            exitItem = new ToolStripMenuItem("", null, delegate { ExitThread(); });

            mouseChoice = MouseSettings.Parse(settings);
            drawnItem = new ToolStripMenuItem("", null, delegate { SetMouse(false, mouseChoice.Colourway); });
            realisticItem = new ToolStripMenuItem("", null, delegate { SetMouse(true, mouseChoice.Colourway); });
            foreach (Colourway c in Enum.GetValues(typeof(Colourway)))
            {
                Colourway way = c;
                colourwayItems[c] = new ToolStripMenuItem("", null, delegate { SetMouse(true, way); });
                realisticItem.DropDownItems.Add(colourwayItems[c]);
            }
            // the level display only matters for the realistic mouse, so picking one also switches to it
            greyWhiteItem = new ToolStripMenuItem("", null, delegate { SetDisplay(LevelDisplay.GreyWhite); });
            seeThroughItem = new ToolStripMenuItem("", null, delegate { SetDisplay(LevelDisplay.SeeThrough); });
            levelColourItem = new ToolStripMenuItem("", null, delegate { SetDisplay(LevelDisplay.LevelColour); });
            realisticItem.DropDownItems.AddRange(new ToolStripItem[] { new ToolStripSeparator(), greyWhiteItem, seeThroughItem, levelColourItem });
            boltItem = new ToolStripMenuItem("", null, delegate { SetBolt(!mouseChoice.ChargingBolt); });
            numberItem = new ToolStripMenuItem("", null, delegate { mouseChoice.ShowNumber = !mouseChoice.ShowNumber; MouseChanged(); });
            styleItem = new ToolStripMenuItem("");
            styleItem.DropDownItems.AddRange(new ToolStripItem[] { drawnItem, realisticItem, new ToolStripSeparator(), boltItem });
            ApplyMenuText();

            menu = new ContextMenuStrip();
            menu.Items.AddRange(new ToolStripItem[] { statusItem, new ToolStripSeparator(), refreshItem, autoStartItem, webItem, numberItem, styleItem, languageItem, new ToolStripSeparator(), exitItem });

            int iconSize = SystemInformation.SmallIconSize.Width;
            using (var empty = new Bitmap(iconSize, iconSize, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                blankHandle = empty.GetHicon();
            blankIcon = Icon.FromHandle(blankHandle);

            // the number icon is always in the tray (it holds the block's slot); it appears once Render gives it a picture
            numberTray = new NotifyIcon { ContextMenuStrip = menu, Visible = true };
            mouseTray = new NotifyIcon { ContextMenuStrip = menu, Visible = false };
            MouseEventHandler click = (s, e) => { if (e.Button == MouseButtons.Left) poke.Set(); };
            numberTray.MouseClick += click;
            mouseTray.MouseClick += click;

            menuOwner.CreateHandle(new CreateParams());   // hidden top-level window that owns the badge's menu
            badge.LeftClick += delegate { poke.Set(); };
            badge.RightClick += delegate { ShowBadgeMenu(); };
            trackTimer.Tick += delegate { UpdatePlacement(); CheckTrayHint(); };
            trackTimer.Start();
            alertTimer.Tick += delegate { alertTimer.Stop(); ShowMouseIcon(); };

            SystemEvents.UserPreferenceChanged += OnThemeChanged;
            Render();

            worker = new Thread(WorkerLoop) { IsBackground = true, Name = "hid-worker" };
            worker.Start();
        }

        // ---------------- worker thread ----------------

        void WorkerLoop()
        {
            var devs = new List<LofreeDevice>();
            while (!exiting)
            {
                BatteryInfo best = null;
                foreach (var d in devs.ToArray())
                {
                    try { var b = d.ReadBattery(); if (b.Online && best == null) best = b; }
                    catch (Exception) { d.Dispose(); devs.Remove(d); }
                }
                if (best == null && !SamePaths(devs))
                {
                    // Mouse not reachable and the set of receivers/cables changed: reopen everything.
                    foreach (var d in devs) d.Dispose();
                    devs.Clear();
                    try { devs = LofreeDevice.OpenAll(); } catch (Exception) { devs = new List<LofreeDevice>(); }
                    foreach (var d in devs)
                    {
                        d.StatusChanged += () => poke.Set();
                        d.Disconnected += () => poke.Set();
                        if (best == null)
                        {
                            try { var b = d.ReadBattery(); if (b.Online) best = b; } catch (Exception) { }
                        }
                    }
                }
                bool found = devs.Count > 0;
                var result = best;
                Post(() => Apply(result, found));
                poke.WaitOne(best != null ? PollOnlineMs : found ? PollOfflineMs : PollNoReceiverMs);
            }
            foreach (var d in devs) d.Dispose();
        }

        static bool SamePaths(List<LofreeDevice> devs)
        {
            if (devs.Count == 0) return false;
            List<string> now;
            try { now = LofreeDevice.CandidatePaths(); } catch (Exception) { return false; }
            if (now.Count != devs.Count) return false;
            foreach (var d in devs)
                if (!now.Exists(p => string.Equals(p, d.Path, StringComparison.OrdinalIgnoreCase))) return false;
            return true;
        }

        void Post(Action a)
        {
            if (exiting) return;
            try { ui.BeginInvoke(a); } catch (Exception) { }
        }

        // ---------------- UI thread ----------------

        void Apply(BatteryInfo b, bool found)
        {
            receiverFound = found;
            if (b != null && b.Online)
            {
                int lvl = Math.Max(0, Math.Min(100, b.Level));
                // Smooth voltage noise: while discharging the number only goes down, while charging only up.
                if (shownLevel < 0 || b.Charging != charging || !everOnline) shownLevel = lvl;
                else if (b.Charging) shownLevel = Math.Max(shownLevel, lvl);
                else shownLevel = lvl > shownLevel + 10 ? lvl : Math.Min(shownLevel, lvl);

                charging = b.Charging;
                voltage = b.VoltageMv;
                online = true;
                everOnline = true;
                lastSeen = DateTime.Now;
                SaveState();
                CheckLowBattery();
            }
            else
            {
                online = false;
            }
            Render();
        }

        void CheckLowBattery()
        {
            if (charging || shownLevel > 25) { warned20 = warned10 = false; return; }
            if (shownLevel <= 10 && !warned10)
            {
                warned10 = warned20 = true;
                Alert(text.CriticalTitle, text.CriticalText(shownLevel), ToolTipIcon.Warning);
            }
            else if (shownLevel <= 20 && !warned20)
            {
                warned20 = true;
                Alert(text.LowTitle, text.LowText(shownLevel), ToolTipIcon.Info);
            }
        }

        // Windows only shows notifications from a tray icon: show the mouse icon for the alert, then hide it again
        // if the block is on the taskbar.
        void Alert(string title, string text, ToolTipIcon icon)
        {
            mouseTray.Visible = true;
            mouseTray.ShowBalloonTip(5000, title, text, icon);
            alertTimer.Stop();
            alertTimer.Start();
        }

        // The first-run reminder, from the number icon: it is the icon to drag, and it is visible under ^.
        void CheckTrayHint()
        {
            if (!trayHint.Tick(placement == Placement.BesideTray)) return;
            settings["trayHintShown"] = Application.ExecutablePath;
            SaveSettings();
            numberTray.ShowBalloonTip(10000, text.TrayHintTitle, text.TrayHintText, ToolTipIcon.Info);
        }

        void Render()
        {
            string tip, status;
            IconState state = TrayIconState.For(shownLevel, online, receiverFound, charging);
            text.Describe(shownLevel, online, receiverFound, charging, voltage, lastSeen, DateTime.Now, out status, out tip);

            statusItem.Text = status;
            string trayTip = tip.Length > 63 ? tip.Substring(0, 63) : tip;
            numberTray.Text = trayTip;
            mouseTray.Text = trayTip;
            int level = Math.Max(shownLevel, 0), size = SystemInformation.SmallIconSize.Width;
            bool light = TaskbarIsLight();
            MouseLook look = CurrentLook();
            bool showNumber = mouseChoice.ShowNumber;
            Func<Bitmap> paintMouse = () => look.PaintMouse(level, state, light, size, MouseIconPainter.LineWidth * size / 20f);
            // without the number, the number icon (which also holds the block's slot) shows the mouse instead
            PaintNumberIcon(showNumber ? () => NumberIconPainter.Paint(level, state, light, size) : paintMouse);
            SetIcon(mouseTray, ref mouseHandle, paintMouse);
            badge.SetTip(tip);
            try { badge.SetImage(BadgePainter.Paint(level, state, light, size, look, showNumber)); }
            catch (Exception) { }   // e.g. GDI resources exhausted: keep the previous block; the next poll redraws
            UpdatePlacement();
        }

        // Block over the number icon's slot when Windows shows that icon on the taskbar, else just left of the tray
        // area; plain tray icons when the block cannot attach.
        void UpdatePlacement()
        {
            Rectangle slot;
            bool haveSlot = TraySlot.TryGet(numberTray, out slot);
            Placement now = !badge.Track(haveSlot ? slot : (Rectangle?)null) ? Placement.TrayIcons
                          : haveSlot ? Placement.InSlot : Placement.BesideTray;
            if (now == placement) return;
            placement = now;
            ShowNumberIcon();
            ShowMouseIcon();
        }

        // The mouse icon is in the tray beside the number icon when the block cannot attach (unless the number is
        // hidden: then the number icon shows the mouse), and during a notification.
        void ShowMouseIcon()
        {
            mouseTray.Visible = (placement == Placement.TrayIcons && mouseChoice.ShowNumber) || alertTimer.Enabled;
        }

        void PaintNumberIcon(Func<Bitmap> paint)
        {
            IntPtr h = IntPtr.Zero;
            try
            {
                using (var bmp = paint())
                    h = bmp.GetHicon();
            }
            catch (Exception)
            {
                // e.g. GDI resources exhausted: keep the previous icon; the next poll redraws.
                if (h != IntPtr.Zero) DestroyIcon(h);
                return;
            }
            IntPtr old = numberHandle;
            numberHandle = h;
            numberIcon = Icon.FromHandle(h);
            ShowNumberIcon();
            if (old != IntPtr.Zero) DestroyIcon(old);
        }

        // Under the block the number icon is transparent, so nothing shows through the block's see-through parts.
        void ShowNumberIcon()
        {
            var want = placement == Placement.InSlot ? blankIcon : numberIcon;
            if (want != null && numberTray.Icon != want) numberTray.Icon = want;
        }

        void ShowBadgeMenu()
        {
            // as NotifyIcon does: make our window foreground first, so the menu closes when the user clicks elsewhere
            SetForegroundWindow(menuOwner.Handle);
            menu.Show(Cursor.Position, ToolStripDropDownDirection.AboveLeft);
        }

        static void SetIcon(NotifyIcon tray, ref IntPtr handle, Func<Bitmap> paint)
        {
            IntPtr h = IntPtr.Zero;
            try
            {
                using (var bmp = paint())
                    h = bmp.GetHicon();
                tray.Icon = Icon.FromHandle(h);
            }
            catch (Exception)
            {
                // e.g. GDI resources exhausted: keep the previous icon; the next poll redraws.
                if (h != IntPtr.Zero) DestroyIcon(h);
                return;
            }
            if (handle != IntPtr.Zero) DestroyIcon(handle);
            handle = h;
        }

        static bool TaskbarIsLight()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    var v = k == null ? null : k.GetValue("SystemUsesLightTheme");
                    return v is int && (int)v == 1;
                }
            }
            catch (Exception) { return false; }
        }

        void OnThemeChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category == UserPreferenceCategory.General || e.Category == UserPreferenceCategory.Color)
                Post(Render);
        }

        // ---------------- language ----------------

        void ApplyMenuText()
        {
            refreshItem.Text = text.Refresh;
            autoStartItem.Text = text.AutoStart;
            webItem.Text = text.WebDriver;
            languageItem.Text = text.LanguageMenu;
            exitItem.Text = text.Exit;
            englishItem.Checked = text.Lang == Language.English;
            chineseItem.Checked = text.Lang == Language.Chinese;

            styleItem.Text = text.MouseStyle;
            drawnItem.Text = text.Drawn;
            realisticItem.Text = text.Realistic;
            greyWhiteItem.Text = text.GreyWhite;
            seeThroughItem.Text = text.SeeThrough;
            levelColourItem.Text = text.LevelColour;
            boltItem.Text = text.ChargingBolt;
            drawnItem.Checked = !mouseChoice.Realistic;
            realisticItem.Checked = mouseChoice.Realistic;
            foreach (var kv in colourwayItems)
            {
                kv.Value.Text = ColourwayName(kv.Key);
                kv.Value.Checked = kv.Key == mouseChoice.Colourway;
            }
            greyWhiteItem.Checked = mouseChoice.Display == LevelDisplay.GreyWhite;
            seeThroughItem.Checked = mouseChoice.Display == LevelDisplay.SeeThrough;
            levelColourItem.Checked = mouseChoice.Display == LevelDisplay.LevelColour;
            boltItem.Checked = mouseChoice.ChargingBolt;
            numberItem.Text = text.ShowNumber;
            numberItem.Checked = mouseChoice.ShowNumber;
        }

        // ---------------- mouse style ----------------

        // Lofree's own names, in both languages
        static string ColourwayName(Colourway c)
        {
            return c == Colourway.CrayonGrey ? "Crayon Grey" : c == Colourway.JetBlack ? "Jet Black" : "Ghost";
        }

        void SetMouse(bool realistic, Colourway c) { mouseChoice.Realistic = realistic; mouseChoice.Colourway = c; MouseChanged(); }
        void SetDisplay(LevelDisplay d) { mouseChoice.Display = d; mouseChoice.Realistic = true; MouseChanged(); }
        void SetBolt(bool on) { mouseChoice.ChargingBolt = on; MouseChanged(); }

        void MouseChanged()
        {
            mouseChoice.WriteTo(settings);
            SaveSettings();
            ApplyMenuText();
            Render();
            ShowMouseIcon();
        }

        // The realistic mouse (the drawn one if its photo is somehow missing from the program).
        MouseLook CurrentLook()
        {
            MousePhoto photo = mouseChoice.Realistic ? PhotoFor(mouseChoice.Colourway) : null;
            return new MouseLook(photo, mouseChoice.Display, mouseChoice.ChargingBolt);
        }

        // The colourway's built-in photo, read once.
        MousePhoto PhotoFor(Colourway c)
        {
            MousePhoto photo;
            if (!photos.TryGetValue(c, out photo)) photos[c] = photo = Photos.Load(c);
            return photo;
        }

        void SetLanguage(Language lang)
        {
            if (lang == text.Lang) return;
            text = UiText.For(lang);
            settings["language"] = UiText.FormatLanguage(lang);
            SaveSettings();
            ApplyMenuText();
            Render();
        }

        // ---------------- persistence / autostart ----------------

        Dictionary<string, string> LoadSettings()
        {
            try { return SettingsText.Parse(File.ReadAllText(settingsFile)); }
            catch (Exception) { return SettingsText.Parse(null); }
        }

        void SaveSettings()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(settingsFile));
                File.WriteAllText(settingsFile, SettingsText.Format(settings));
            }
            catch (Exception) { }
        }

        void LoadState()
        {
            try
            {
                var parts = File.ReadAllText(stateFile).Trim().Split('|');
                shownLevel = int.Parse(parts[0]);
                lastSeen = DateTime.Parse(parts[1], null, System.Globalization.DateTimeStyles.RoundtripKind);
                charging = parts.Length > 2 && parts[2] == "1";
            }
            catch (Exception) { shownLevel = -1; }
        }

        void SaveState()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(stateFile));
                File.WriteAllText(stateFile, shownLevel + "|" + lastSeen.ToString("o") + "|" + (charging ? "1" : "0"));
            }
            catch (Exception) { }
        }

        static bool IsAutoStart()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                return k != null && k.GetValue(AppName) != null;
        }

        void ToggleAutoStart()
        {
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (IsAutoStart()) k.DeleteValue(AppName, false);
                else k.SetValue(AppName, "\"" + Application.ExecutablePath + "\"");
            }
            autoStartItem.Checked = IsAutoStart();
        }

        protected override void ExitThreadCore()
        {
            exiting = true;
            poke.Set();
            SystemEvents.UserPreferenceChanged -= OnThemeChanged;
            trackTimer.Stop();
            trackTimer.Dispose();
            alertTimer.Stop();
            alertTimer.Dispose();
            badge.Dispose();
            menuOwner.DestroyHandle();
            numberTray.Visible = false;
            mouseTray.Visible = false;
            numberTray.Dispose();
            mouseTray.Dispose();
            if (numberHandle != IntPtr.Zero) { DestroyIcon(numberHandle); numberHandle = IntPtr.Zero; }
            if (mouseHandle != IntPtr.Zero) { DestroyIcon(mouseHandle); mouseHandle = IntPtr.Zero; }
            DestroyIcon(blankHandle);
            base.ExitThreadCore();
        }

        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    }
}
