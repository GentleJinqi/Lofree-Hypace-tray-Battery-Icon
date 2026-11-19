using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace HypaceTray
{
    enum Language { English, Chinese }

    // Every text the user sees (menu, hover tip, notifications), in English and Chinese.
    // English until the user picks Chinese in the menu.
    sealed class UiText
    {
        public Language Lang { get; private set; }
        public string Connecting { get; private set; }
        public string Refresh { get; private set; }
        public string AutoStart { get; private set; }
        public string WebDriver { get; private set; }
        public string LanguageMenu { get; private set; }
        public string Exit { get; private set; }
        public string LowTitle { get; private set; }
        public string CriticalTitle { get; private set; }
        public string MouseStyle { get; private set; }
        public string Drawn { get; private set; }
        public string Realistic { get; private set; }
        public string GreyWhite { get; private set; }
        public string SeeThrough { get; private set; }
        public string LevelColour { get; private set; }
        public string ChargingBolt { get; private set; }
        public string Downloading { get; private set; }      // appended to a colourway while its photo downloads
        public string DownloadFailed { get; private set; }

        // format strings: {0} = level, {1} = when it was last read
        string lowText, criticalText;
        string notOnline, notOnlineTip, noReceiver, noReceiverTip;
        string statusOnline, statusCharging, tipOnline, tipCharging, tipUpdated;
        string statusAsleep, tipAsleep, statusReceiverGone, tipReceiverGone;

        UiText() { }

        static readonly UiText english = new UiText
        {
            Lang = Language.English,
            Connecting = "Connecting…",
            Refresh = "Refresh now",
            AutoStart = "Start with Windows",
            WebDriver = "Open the official web driver",
            LanguageMenu = "Language",
            Exit = "Exit",
            LowTitle = "Hypace battery low",
            CriticalTitle = "Hypace battery critically low",
            MouseStyle = "Mouse style",
            Drawn = "Drawn",
            Realistic = "Realistic",
            GreyWhite = "Drained part turns grey-white",
            SeeThrough = "Drained part turns see-through",
            LevelColour = "Charged part in level colours",
            ChargingBolt = "Show charging bolt",
            Downloading = " (downloading…)",
            DownloadFailed = " (download failed, will retry)",
            lowText = "{0}% left.",
            criticalText = "{0}% left, please charge it soon.",
            notOnline = "Mouse not online (move it to wake it up)",
            notOnlineTip = "Hypace: mouse not online (move it to wake it up)",
            noReceiver = "Hypace receiver not found",
            noReceiverTip = "Hypace: receiver not found",
            statusOnline = "Battery {0}%",
            statusCharging = " (charging)",
            tipOnline = "Hypace {0}%",
            tipCharging = " charging",
            tipUpdated = "\nUpdated {1}",
            statusAsleep = "Mouse asleep or offline, last {0}% ({1})",
            tipAsleep = "Hypace asleep · last {0}%\n{1}",
            statusReceiverGone = "Receiver not found, last {0}%",
            tipReceiverGone = "Hypace: receiver not found\nlast {0}%",
        };

        static readonly UiText chinese = new UiText
        {
            Lang = Language.Chinese,
            Connecting = "正在连接…",
            Refresh = "立即刷新",
            AutoStart = "开机自启动",
            WebDriver = "打开官方网页驱动",
            LanguageMenu = "语言",
            Exit = "退出",
            LowTitle = "Hypace 电量低",
            CriticalTitle = "Hypace 电量严重不足",
            MouseStyle = "鼠标样式",
            Drawn = "线条",
            Realistic = "仿真",
            GreyWhite = "没电部分变灰白",
            SeeThrough = "没电部分变透明",
            LevelColour = "电量部分用电量颜色",
            ChargingBolt = "充电时显示闪电",
            Downloading = "（下载中…）",
            DownloadFailed = "（下载失败，稍后重试）",
            lowText = "剩余 {0}%。",
            criticalText = "剩余 {0}%，请尽快充电。",
            notOnline = "鼠标未在线（晃动一下鼠标唤醒）",
            notOnlineTip = "Hypace：鼠标未在线（晃动一下鼠标唤醒）",
            noReceiver = "未找到 Hypace 接收器",
            noReceiverTip = "Hypace：未找到 Hypace 接收器",
            statusOnline = "电量 {0}%",
            statusCharging = "（充电中）",
            tipOnline = "Hypace {0}%",
            tipCharging = " 充电中",
            tipUpdated = "\n更新于 {1}",
            statusAsleep = "鼠标休眠/离线，上次 {0}%（{1}）",
            tipAsleep = "Hypace 休眠中 · 上次 {0}%\n{1}",
            statusReceiverGone = "未找到接收器，上次 {0}%",
            tipReceiverGone = "Hypace：未找到接收器\n上次 {0}%",
        };

        public static UiText For(Language lang) { return lang == Language.Chinese ? chinese : english; }

        // The saved choice: "zh" is Chinese; anything else, or nothing saved yet, is English.
        public static Language ParseLanguage(string saved)
        {
            return (saved ?? "").Trim().ToLowerInvariant() == "zh" ? Language.Chinese : Language.English;
        }

        public static string FormatLanguage(Language lang) { return lang == Language.Chinese ? "zh" : "en"; }

        public string LowText(int level) { return F(lowText, level, ""); }
        public string CriticalText(int level) { return F(criticalText, level, ""); }

        // The status line at the top of the menu and the hover tip. level < 0: never read.
        public void Describe(int level, bool online, bool receiverFound, bool charging, int voltageMv,
                             DateTime lastSeen, DateTime now, out string status, out string tip)
        {
            if (level < 0)
            {
                status = receiverFound ? notOnline : noReceiver;
                tip = receiverFound ? notOnlineTip : noReceiverTip;
                return;
            }
            if (!receiverFound)
            {
                status = F(statusReceiverGone, level, "");
                tip = F(tipReceiverGone, level, "");
                return;
            }
            string when = lastSeen == DateTime.MinValue ? ""
                        : lastSeen.ToString(lastSeen.Date == now.Date ? "HH:mm" : "M/d HH:mm", CultureInfo.InvariantCulture);
            if (online)
            {
                status = F(statusOnline, level, when) + (charging ? statusCharging : "")
                       + (voltageMv > 0 ? "  " + (voltageMv / 1000.0).ToString("0.00", CultureInfo.InvariantCulture) + "V" : "");
                tip = F(tipOnline, level, when) + (charging ? tipCharging : "") + F(tipUpdated, level, when);
            }
            else
            {
                status = F(statusAsleep, level, when);
                tip = F(tipAsleep, level, when);
            }
        }

        static string F(string format, int level, string when) { return string.Format(CultureInfo.InvariantCulture, format, level, when); }
    }

    // settings.txt: one key=value per line. Keys this version does not use (like the old iconSize) are kept.
    static class SettingsText
    {
        public static Dictionary<string, string> Parse(string text)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(text)) return d;
            foreach (var line in text.Split('\n'))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim();
                if (key.Length > 0) d[key] = line.Substring(eq + 1).Trim();
            }
            return d;
        }

        public static string Format(Dictionary<string, string> values)
        {
            var sb = new StringBuilder();
            foreach (var kv in values) sb.Append(kv.Key).Append('=').Append(kv.Value).Append("\r\n");
            return sb.ToString();
        }
    }
}
