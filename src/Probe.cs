using System;
using System.Threading;

namespace HypaceTray
{
    static class Probe
    {
        static string Hex(byte[] b) { return b == null ? "(no reply)" : BitConverter.ToString(b); }
        static void Log(string s) { Console.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + "  " + s); }

        static int Main(string[] args)
        {
            int seconds = args.Length > 0 ? int.Parse(args[0]) : 45;
            using (var dev = LofreeDevice.Open())
            {
                if (dev == null) { Log("No collection answered the handshake."); return 1; }
                Log("Opened: " + dev.Product + " type=" + dev.DeviceType + " wired=" + dev.IsWired);
                dev.ReportReceived += r => { if (r[0] != 3 && r[0] != 4) Log("rx " + Hex(r)); };
                string lastOnline = null;
                for (int s = 0; s < seconds; s++)
                {
                    var on = dev.Raw(3, null);
                    var hex = Hex(on);
                    if (hex != lastOnline) { Log("online : " + hex); lastOnline = hex; }
                    if (on != null && on[5] == 1)
                    {
                        Log("battery: " + Hex(dev.Raw(4, null)));
                        var b = dev.ReadBattery();
                        Log(string.Format("RESULT online={0} raw={1}% charging={2} voltage={3}mV level={4}%",
                            b.Online, b.RawLevel, b.Charging, b.VoltageMv, b.Level));
                        return 0;
                    }
                    Thread.Sleep(1000);
                }
                Log("Mouse stayed offline for " + seconds + "s.");
            }
            return 2;
        }
    }
}
