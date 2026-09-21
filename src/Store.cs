using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace DeepSeekPet
{
    /// <summary>用户可编辑的配置。</summary>
    internal sealed class Config
    {
        public string ApiKey = "";
        public string Currency = "CNY";
        public int RefreshSeconds = 600;              // 自动刷新间隔（秒），最小 60
        public decimal LowBalanceThreshold = 5m;      // 低余额提醒阈值（原币种）
        public bool NotifyLowBalance = true;          // 托盘气泡提醒
        public bool AutoStart = false;                // 开机自动启动
        public bool TopMost = true;                   // 总在最前
        public bool MiniMode = false;                 // 迷你胶囊模式
        public string Theme = "light";                // light | dark | auto
        public string ShowMode = "always";            // always | trigger（触发显示）
        public int TriggerStaySeconds = 90;           // 触发后停留秒数，0 = 不自动隐藏
        public int TriggerPollSeconds = 60;           // 触发模式下的余额轮询间隔
        public string TriggerTitleKeywords = "deepseek,深度求索";
        public string TriggerProcesses = "";          // 逗号分隔的进程名，如 Code.exe,pycharm64.exe
        public bool TriggerOnSpend = true;            // 检测到 API 消费后弹出
        public decimal SpendCalibration = 0m;         // 累计消费校准值（控制台显示的数字）
        public string SpendCalibrationDate = "";      // 校准日期 yyyy-MM-dd
        public int X = int.MinValue;                  // 窗口位置（逻辑像素）
        public int Y = int.MinValue;
    }

    /// <summary>一次余额采样点，用于估算消费速度。</summary>
    internal sealed class Sample
    {
        public long T;          // Unix 秒（UTC）
        public decimal Total;
    }

    /// <summary>运行状态（累积消费估算、历史采样等）。</summary>
    internal sealed class State
    {
        public bool Initialized;
        public decimal CumulativeSpend;
        public bool Calibrated;
        public decimal LastTotal = -1m;
        public decimal LastToppedUp = -1m;
        public decimal LastGranted = 0m;
        public string LastCurrency = "";
        public string LastSuccessUtc = "";
        public string LastError = "";
        public List<Sample> History = new List<Sample>();

        public const int MaxHistory = 3000;
    }

    internal static class Store
    {
        public static readonly string Directory;
        public static readonly string ConfigPath;
        public static readonly string StatePath;

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        static Store()
        {
            string exeDir = null;
            try { exeDir = Path.GetDirectoryName(Application.ExecutablePath); }
            catch { }
            if (string.IsNullOrEmpty(exeDir)) exeDir = Environment.CurrentDirectory;

            string target = exeDir;
            if (!IsWritable(target))
            {
                // 安装在只读目录（如 Program Files）时退回到用户配置目录
                string roaming = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeepSeekPet");
                try { System.IO.Directory.CreateDirectory(roaming); }
                catch { }
                target = roaming;
            }

            Directory = target;
            ConfigPath = Path.Combine(target, "settings.json");
            StatePath = Path.Combine(target, "state.json");
        }

        private static bool IsWritable(string dir)
        {
            try
            {
                string probe = Path.Combine(dir, ".write-probe-" + Guid.NewGuid().ToString("N") + ".tmp");
                File.WriteAllText(probe, "ok", Utf8NoBom);
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }

        // ---------------- 配置 ----------------

        public static Config LoadConfig()
        {
            Config cfg = new Config();
            Dictionary<string, object> root = TryReadObject(ConfigPath);
            if (root == null) return cfg;

            cfg.ApiKey = Json.GetString(root, "apiKey", cfg.ApiKey);
            cfg.Currency = Json.GetString(root, "currency", cfg.Currency);
            cfg.RefreshSeconds = Json.GetInt(root, "refreshSeconds", cfg.RefreshSeconds);
            cfg.LowBalanceThreshold = Json.GetDecimal(root, "lowBalanceThreshold", cfg.LowBalanceThreshold);
            cfg.NotifyLowBalance = Json.GetBool(root, "notifyLowBalance", cfg.NotifyLowBalance);
            cfg.AutoStart = Json.GetBool(root, "autoStart", cfg.AutoStart);
            cfg.TopMost = Json.GetBool(root, "topMost", cfg.TopMost);
            cfg.MiniMode = Json.GetBool(root, "miniMode", cfg.MiniMode);
            cfg.Theme = Json.GetString(root, "theme", cfg.Theme);
            cfg.ShowMode = Json.GetString(root, "showMode", cfg.ShowMode);
            cfg.TriggerStaySeconds = Json.GetInt(root, "triggerStaySeconds", cfg.TriggerStaySeconds);
            cfg.TriggerPollSeconds = Json.GetInt(root, "triggerPollSeconds", cfg.TriggerPollSeconds);
            cfg.TriggerTitleKeywords = Json.GetString(root, "triggerTitleKeywords", cfg.TriggerTitleKeywords);
            cfg.TriggerProcesses = Json.GetString(root, "triggerProcesses", cfg.TriggerProcesses);
            cfg.TriggerOnSpend = Json.GetBool(root, "triggerOnSpend", cfg.TriggerOnSpend);
            cfg.SpendCalibration = Json.GetDecimal(root, "spendCalibration", cfg.SpendCalibration);
            cfg.SpendCalibrationDate = Json.GetString(root, "spendCalibrationDate", cfg.SpendCalibrationDate);
            cfg.X = Json.GetInt(root, "x", cfg.X);
            cfg.Y = Json.GetInt(root, "y", cfg.Y);
            Normalize(cfg);
            return cfg;
        }

        public static void SaveConfig(Config cfg)
        {
            Normalize(cfg);
            Dictionary<string, object> root = new Dictionary<string, object>();
            root["apiKey"] = cfg.ApiKey;
            root["currency"] = cfg.Currency;
            root["refreshSeconds"] = cfg.RefreshSeconds;
            root["lowBalanceThreshold"] = cfg.LowBalanceThreshold;
            root["notifyLowBalance"] = cfg.NotifyLowBalance;
            root["autoStart"] = cfg.AutoStart;
            root["topMost"] = cfg.TopMost;
            root["miniMode"] = cfg.MiniMode;
            root["theme"] = cfg.Theme;
            root["showMode"] = cfg.ShowMode;
            root["triggerStaySeconds"] = cfg.TriggerStaySeconds;
            root["triggerPollSeconds"] = cfg.TriggerPollSeconds;
            root["triggerTitleKeywords"] = cfg.TriggerTitleKeywords;
            root["triggerProcesses"] = cfg.TriggerProcesses;
            root["triggerOnSpend"] = cfg.TriggerOnSpend;
            root["spendCalibration"] = cfg.SpendCalibration;
            root["spendCalibrationDate"] = cfg.SpendCalibrationDate;
            root["x"] = cfg.X;
            root["y"] = cfg.Y;
            WriteFile(ConfigPath, Json.Write(root));
        }

        private static void Normalize(Config cfg)
        {
            if (cfg.RefreshSeconds < 60) cfg.RefreshSeconds = 60;
            if (cfg.RefreshSeconds > 86400) cfg.RefreshSeconds = 86400;
            if (cfg.LowBalanceThreshold < 0m) cfg.LowBalanceThreshold = 0m;
            if (string.IsNullOrEmpty(cfg.Currency)) cfg.Currency = "CNY";
            if (cfg.Theme != "dark" && cfg.Theme != "light" && cfg.Theme != "auto") cfg.Theme = "light";
            if (cfg.ShowMode != "always" && cfg.ShowMode != "trigger") cfg.ShowMode = "always";
            if (cfg.TriggerStaySeconds < 0) cfg.TriggerStaySeconds = 0;
            if (cfg.TriggerStaySeconds > 86400) cfg.TriggerStaySeconds = 86400;
            if (cfg.TriggerPollSeconds < 30) cfg.TriggerPollSeconds = 30;
            if (cfg.TriggerPollSeconds > 3600) cfg.TriggerPollSeconds = 3600;
            if (cfg.TriggerTitleKeywords == null) cfg.TriggerTitleKeywords = "";
            if (cfg.TriggerProcesses == null) cfg.TriggerProcesses = "";
            if (cfg.ApiKey == null) cfg.ApiKey = "";
        }

        // ---------------- 状态 ----------------

        public static State LoadState()
        {
            State st = new State();
            Dictionary<string, object> root = TryReadObject(StatePath);
            if (root == null) return st;

            st.Initialized = Json.GetBool(root, "initialized", false);
            st.CumulativeSpend = Json.GetDecimal(root, "cumulativeSpend", 0m);
            st.Calibrated = Json.GetBool(root, "calibrated", false);
            st.LastTotal = Json.GetDecimal(root, "lastTotal", -1m);
            st.LastToppedUp = Json.GetDecimal(root, "lastToppedUp", -1m);
            st.LastGranted = Json.GetDecimal(root, "lastGranted", 0m);
            st.LastCurrency = Json.GetString(root, "lastCurrency", "");
            st.LastSuccessUtc = Json.GetString(root, "lastSuccessUtc", "");
            st.LastError = Json.GetString(root, "lastError", "");

            List<object> history = Json.GetArray(root, "history");
            if (history != null)
            {
                foreach (object item in history)
                {
                    Dictionary<string, object> entry = item as Dictionary<string, object>;
                    if (entry == null) continue;
                    Sample sample = new Sample();
                    sample.T = Json.GetLong(entry, "t", 0L);
                    sample.Total = Json.GetDecimal(entry, "v", 0m);
                    if (sample.T > 0) st.History.Add(sample);
                }
            }
            return st;
        }

        public static void SaveState(State st)
        {
            Dictionary<string, object> root = new Dictionary<string, object>();
            root["initialized"] = st.Initialized;
            root["cumulativeSpend"] = st.CumulativeSpend;
            root["calibrated"] = st.Calibrated;
            root["lastTotal"] = st.LastTotal;
            root["lastToppedUp"] = st.LastToppedUp;
            root["lastGranted"] = st.LastGranted;
            root["lastCurrency"] = st.LastCurrency;
            root["lastSuccessUtc"] = st.LastSuccessUtc;
            root["lastError"] = st.LastError;

            List<object> history = new List<object>();
            int start = Math.Max(0, st.History.Count - State.MaxHistory);
            for (int i = start; i < st.History.Count; i++)
            {
                Dictionary<string, object> entry = new Dictionary<string, object>();
                entry["t"] = st.History[i].T;
                entry["v"] = st.History[i].Total;
                history.Add(entry);
            }
            root["history"] = history;
            WriteFile(StatePath, Json.Write(root));
        }

        // ---------------- 工具 ----------------

        private static Dictionary<string, object> TryReadObject(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                string text = File.ReadAllText(path, Encoding.UTF8);
                if (string.IsNullOrEmpty(text.Trim())) return null;
                return Json.ParseObject(text);
            }
            catch
            {
                // 配置损坏时不阻塞启动：备份后使用默认值
                try { File.Copy(path, path + ".corrupt-" + DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture), true); }
                catch { }
                return null;
            }
        }

        private static void WriteFile(string path, string text)
        {
            try
            {
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, text, Utf8NoBom);
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch
            {
                try { File.WriteAllText(path, text, Utf8NoBom); }
                catch { }
            }
        }

        public static string ExePath()
        {
            try { return Application.ExecutablePath; }
            catch { return ""; }
        }
    }
}
