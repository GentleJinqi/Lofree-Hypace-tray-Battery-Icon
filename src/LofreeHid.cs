// Lofree Hypace HID protocol (reverse-engineered from the official WebHID driver at lofree.tech).
// Packets: report ID 0x08, 16 data bytes: [cmd, 0, 0, 0, len, data..., checksum].
// checksum = (0x55 - sum(bytes[0..14]) - reportId) & 0xFF
// Replies come back as input report 0x08 with the same cmd in byte 0, status in byte 1 (0 = ok).
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace HypaceTray
{
    public sealed class BatteryInfo
    {
        public bool Online;
        public int RawLevel;      // percentage reported by the mouse
        public bool Charging;
        public int VoltageMv;
        public int Level;         // percentage computed the same way as the official driver
    }

    public sealed class LofreeDevice : IDisposable
    {
        public const ushort Vid = 0x3554;
        public const byte ReportId = 0x08;
        const byte CmdEncryption = 1, CmdDeviceOnline = 3, CmdBattery = 4;

        // Voltage table used by the official driver; each step is 5%.
        static readonly int[] VoltTable = { 3050, 3420, 3480, 3540, 3600, 3660, 3720, 3760, 3800, 3840,
                                            3880, 3920, 3940, 3960, 3980, 4000, 4020, 4040, 4060, 4080, 4110 };

        // Separate handles for reading and writing: every open handle gets its own copy of
        // input reports, and this keeps the blocking reader thread off the write path.
        readonly FileStream stream;      // async handle, read only by the reader thread
        readonly FileStream writer;      // sync handle for output reports
        readonly Thread reader;
        readonly object gate = new object();
        readonly Queue<byte[]> inbox = new Queue<byte[]>();
        volatile bool closed;

        public readonly string Path;
        public readonly ushort Pid;
        public readonly string Product;
        public event Action StatusChanged;   // unsolicited "status changed" (cmd 10) from the dongle
        public event Action Disconnected;
        public event Action<byte[]> ReportReceived;  // every input report (debug)

        LofreeDevice(SafeFileHandle readHandle, SafeFileHandle writeHandle, string path, ushort pid, string product)
        {
            Path = path; Pid = pid; Product = product;
            stream = new FileStream(readHandle, FileAccess.ReadWrite, 1, true);
            writer = new FileStream(writeHandle, FileAccess.ReadWrite, 1, false);
            reader = new Thread(ReadLoop);
            reader.IsBackground = true;
            reader.Start();
        }

        // ---------- discovery ----------

        public static List<string> Describe()
        {
            var lines = new List<string>();
            foreach (var p in Native.EnumerateHidPaths())
            {
                using (var h = Native.OpenShared(p))
                {
                    if (h.IsInvalid) continue;
                    ushort vid, pid; if (!Native.GetVidPid(h, out vid, out pid) || vid != Vid) continue;
                    Native.HIDP_CAPS caps; Native.GetCaps(h, out caps);
                    lines.Add(string.Format("PID {0:X4} usage {1:X4}/{2:X4} in {3} out {4} feat {5}  {6}",
                        pid, caps.UsagePage, caps.Usage, caps.InputReportByteLength, caps.OutputReportByteLength,
                        caps.FeatureReportByteLength, p));
                }
            }
            return lines;
        }

        // Finds the vendor collection the official driver talks to (one 16-byte input + output report, ID 8).
        public static LofreeDevice Open()
        {
            var all = OpenAll();
            for (int i = 1; i < all.Count; i++) all[i].Dispose();
            return all.Count > 0 ? all[0] : null;
        }

        static bool IsCandidate(SafeFileHandle h, out ushort pid)
        {
            ushort vid; Native.HIDP_CAPS caps;
            return Native.GetVidPid(h, out vid, out pid) && vid == Vid
                   && Native.GetCaps(h, out caps) && caps.UsagePage >= 0xFF00
                   && caps.InputReportByteLength == 17 && caps.OutputReportByteLength == 17;
        }

        // Device paths of matching collections, without opening a session (cheap; no packets sent).
        public static List<string> CandidatePaths()
        {
            var result = new List<string>();
            foreach (var p in Native.EnumerateHidPaths())
                using (var h = Native.OpenShared(p))
                {
                    ushort pid;
                    if (!h.IsInvalid && IsCandidate(h, out pid)) result.Add(p);
                }
            return result;
        }

        // Opens every matching collection (e.g. the 2.4G receiver and the mouse itself when cabled).
        public static List<LofreeDevice> OpenAll()
        {
            var result = new List<LofreeDevice>();
            foreach (var p in Native.EnumerateHidPaths())
            {
                var h = Native.OpenShared(p);
                if (h.IsInvalid) { h.Dispose(); continue; }
                ushort pid;
                if (!IsCandidate(h, out pid)) { h.Dispose(); continue; }
                var ah = Native.OpenSharedAsync(p);
                if (ah.IsInvalid) { ah.Dispose(); h.Dispose(); continue; }
                var dev = new LofreeDevice(ah, h, p, pid, Native.GetProduct(h));
                if (dev.Handshake()) result.Add(dev);
                else dev.Dispose();
            }
            return result;
        }

        // ---------- protocol ----------

        static byte[] Build(byte cmd, byte[] data)
        {
            var b = new byte[17];
            b[0] = ReportId;
            b[1] = cmd;
            if (data != null) { b[5] = (byte)data.Length; Array.Copy(data, 0, b, 6, data.Length); }
            int sum = 0; for (int i = 1; i <= 15; i++) sum += b[i];
            b[16] = (byte)((0x55 - (sum & 0xFF) - ReportId) & 0xFF);
            return b;
        }

        // Sends a command and returns the 16-byte reply (without report ID) or null.
        byte[] Transact(byte cmd, byte[] data, int timeoutMs)
        {
            lock (gate) inbox.Clear();
            var pkt = Build(cmd, data);
            try { writer.Write(pkt, 0, pkt.Length); writer.Flush(); }
            catch (IOException) { return null; }
            catch (ObjectDisposedException) { return null; }
            var deadline = Environment.TickCount + timeoutMs;
            lock (gate)
            {
                while (true)
                {
                    while (inbox.Count > 0)
                    {
                        var r = inbox.Dequeue();
                        if (r[0] == cmd) return r;
                    }
                    int left = deadline - Environment.TickCount;
                    if (left <= 0 || closed) return null;
                    Monitor.Wait(gate, left);
                }
            }
        }

        public int DeviceType = -1;   // from handshake: 2/3 = wired, 0/1/4/5 = 2.4G receiver (1K/4K/2K/8K)

        bool Handshake()
        {
            var rnd = new Random();
            var d = new byte[8];
            for (int i = 0; i < 4; i++) d[i] = (byte)rnd.Next(256);
            var r = Transact(CmdEncryption, d, 600);
            if (r == null || r[1] != 0) return false;
            DeviceType = r[11];
            return true;
        }

        public bool IsWired { get { return DeviceType == 2 || DeviceType == 3; } }

        // Debug helper: send a raw command and return the reply bytes.
        public byte[] Raw(byte cmd, byte[] data) { return Transact(cmd, data, 800); }

        public BatteryInfo ReadBattery()
        {
            var info = new BatteryInfo();
            var on = Transact(CmdDeviceOnline, null, 600);
            if (on == null) throw new IOException("no reply from receiver");
            info.Online = on[1] == 0 && on[5] == 1;
            if (!info.Online) return info;
            var r = Transact(CmdBattery, null, 800);
            if (r == null || r[1] != 0) { info.Online = false; return info; }
            info.RawLevel = r[5];
            info.Charging = r[6] == 1;
            info.VoltageMv = (r[7] << 8) | r[8];
            info.Level = info.VoltageMv > 0 ? VoltageToLevel(info.VoltageMv, info.Charging) : info.RawLevel;
            return info;
        }

        public static int VoltageToLevel(int mv, bool charging)
        {
            if (mv > VoltTable[VoltTable.Length - 1]) return charging ? 99 : 100;
            int a = -1;
            for (int n = 0; n < VoltTable.Length; n++) if (mv < VoltTable[n]) { a = n; break; }
            double s = 0;
            if (a > 0)
            {
                double step = (VoltTable[a] - VoltTable[a - 1]) / 5.0;
                s = (mv - VoltTable[a - 1]) / step + 5 * (a - 1);
            }
            if (s == 0 || s == 15) s++;
            return (int)Math.Round(s, MidpointRounding.AwayFromZero);
        }

        void ReadLoop()
        {
            var buf = new byte[17];
            try
            {
                while (!closed)
                {
                    int n = stream.Read(buf, 0, buf.Length);
                    if (n <= 0) break;
                    if (buf[0] != ReportId) continue;
                    var r = new byte[16];
                    Array.Copy(buf, 1, r, 0, 16);
                    var rr = ReportReceived; if (rr != null) rr(r);
                    if (r[0] == 10 && r[1] == 0)
                    {
                        var h = StatusChanged; if (h != null) ThreadPool.QueueUserWorkItem(delegate { h(); });
                        continue;
                    }
                    lock (gate) { inbox.Enqueue(r); Monitor.PulseAll(gate); }
                }
            }
            catch (Exception) { }
            if (!closed)
            {
                closed = true;
                lock (gate) Monitor.PulseAll(gate);
                var d = Disconnected; if (d != null) d();
            }
        }

        public void Dispose()
        {
            closed = true;
            try { stream.Dispose(); } catch { }
            try { writer.Dispose(); } catch { }
            lock (gate) Monitor.PulseAll(gate);
        }
    }

    static class Native
    {
        const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;
        const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3, FILE_FLAG_OVERLAPPED = 0x40000000;
        const int DIGCF_PRESENT = 2, DIGCF_DEVICEINTERFACE = 0x10;

        [StructLayout(LayoutKind.Sequential)]
        struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }

        [StructLayout(LayoutKind.Sequential)]
        struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID; public ushort ProductID; public ushort VersionNumber; }

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDP_CAPS
        {
            public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices,
                NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices,
                NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
        }

        [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
        [DllImport("hid.dll")] static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES a);
        [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr p);
        [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr p);
        [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr p, out HIDP_CAPS c);
        [DllImport("hid.dll", CharSet = CharSet.Unicode)] static extern bool HidD_GetProductString(SafeFileHandle h, byte[] buf, int len);

        [DllImport("setupapi.dll", SetLastError = true)]
        static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr e, IntPtr hwnd, int flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid g, int idx, ref SP_DEVICE_INTERFACE_DATA d);
        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA d, IntPtr detail, int size, out int req, IntPtr info);
        [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);

        public static List<string> EnumerateHidPaths()
        {
            var list = new List<string>();
            Guid g; HidD_GetHidGuid(out g);
            IntPtr set = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (set == new IntPtr(-1)) return list;
            try
            {
                var d = new SP_DEVICE_INTERFACE_DATA();
                d.cbSize = Marshal.SizeOf(d);
                for (int i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref d); i++)
                {
                    int req;
                    SetupDiGetDeviceInterfaceDetail(set, ref d, IntPtr.Zero, 0, out req, IntPtr.Zero);
                    IntPtr buf = Marshal.AllocHGlobal(req);
                    try
                    {
                        Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                        if (SetupDiGetDeviceInterfaceDetail(set, ref d, buf, req, out req, IntPtr.Zero))
                        {
                            var path = Marshal.PtrToStringUni(new IntPtr(buf.ToInt64() + 4));
                            if (path.IndexOf("vid_3554", StringComparison.OrdinalIgnoreCase) >= 0) list.Add(path);
                        }
                    }
                    finally { Marshal.FreeHGlobal(buf); }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return list;
        }

        public static SafeFileHandle OpenShared(string path)
        {
            return CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        }

        public static SafeFileHandle OpenSharedAsync(string path)
        {
            return CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
        }

        public static bool GetVidPid(SafeFileHandle h, out ushort vid, out ushort pid)
        {
            var a = new HIDD_ATTRIBUTES(); a.Size = Marshal.SizeOf(a);
            bool ok = HidD_GetAttributes(h, ref a);
            vid = a.VendorID; pid = a.ProductID;
            return ok;
        }

        public static bool GetCaps(SafeFileHandle h, out HIDP_CAPS caps)
        {
            caps = new HIDP_CAPS();
            IntPtr p;
            if (!HidD_GetPreparsedData(h, out p)) return false;
            try { return HidP_GetCaps(p, out caps) == 0x00110000; }
            finally { HidD_FreePreparsedData(p); }
        }

        public static string GetProduct(SafeFileHandle h)
        {
            var b = new byte[256];
            if (!HidD_GetProductString(h, b, b.Length)) return "";
            return System.Text.Encoding.Unicode.GetString(b).TrimEnd('\0');
        }
    }
}
