using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace DeepSeekPet
{
    /// <summary>读取当前前台窗口的标题与所属进程名。</summary>
    internal static class ForegroundWindowInfo
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder text, int count);

        [DllImport("user32.dll")]
        private static extern int GetWindowThreadProcessId(IntPtr hWnd, out int processId);

        public static string CurrentTitle()
        {
            try
            {
                IntPtr handle = GetForegroundWindow();
                if (handle == IntPtr.Zero) return "";
                StringBuilder buffer = new StringBuilder(512);
                int length = GetWindowTextW(handle, buffer, buffer.Capacity);
                if (length <= 0) return "";
                return buffer.ToString();
            }
            catch { return ""; }
        }

        public static string CurrentProcessName()
        {
            try
            {
                IntPtr handle = GetForegroundWindow();
                if (handle == IntPtr.Zero) return "";
                int pid;
                GetWindowThreadProcessId(handle, out pid);
                if (pid <= 0) return "";
                using (Process process = Process.GetProcessById(pid))
                {
                    return process.ProcessName;
                }
            }
            catch { return ""; }
        }
    }

    /// <summary>
    /// 触发显示判定。
    /// 本机无法解密其它程序的 HTTPS 流量，因此"正在使用 DeepSeek"只能靠可观测的替代信号：
    /// 1) 前台窗口标题命中关键词；2) 指定进程在运行；3) 自己的 API Key 真实发生了消费（余额下降）。
    /// </summary>
    internal sealed class TriggerEvaluator
    {
        private readonly Config _config;
        private readonly List<string> _processNames = new List<string>();
        private string _processSignature = "";
        private bool _processActive;
        private DateTime _lastProcessCheckUtc = DateTime.MinValue;

        public List<string> Reasons = new List<string>();

        public TriggerEvaluator(Config config)
        {
            _config = config;
        }

        /// <summary>窗口标题是否命中关键词（逗号分隔，忽略大小写）。</summary>
        public static bool MatchTitle(string title, string keywords)
        {
            if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(keywords)) return false;
            string lower = title.ToLowerInvariant();
            string[] parts = keywords.Split(new char[] { ',', '，', ';', '；' });
            for (int i = 0; i < parts.Length; i++)
            {
                string keyword = parts[i].Trim().ToLowerInvariant();
                if (keyword.Length == 0) continue;
                if (lower.IndexOf(keyword, StringComparison.Ordinal) >= 0) return true;
            }
            return false;
        }

        /// <summary>把 "C:\a\Code.exe" / "Code.exe" 归一化为 "code"。</summary>
        public static string NormalizeProcessName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            string name = raw.Trim().Trim('"');
            int slash = name.LastIndexOfAny(new char[] { '\\', '/' });
            if (slash >= 0) name = name.Substring(slash + 1);
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 4);
            return name.ToLowerInvariant();
        }

        public static List<string> ParseProcessNames(string list)
        {
            List<string> result = new List<string>();
            if (string.IsNullOrEmpty(list)) return result;
            string[] parts = list.Split(new char[] { ',', '，', ';', '；', '\n', '\r' });
            for (int i = 0; i < parts.Length; i++)
            {
                string name = NormalizeProcessName(parts[i]);
                if (name.Length == 0) continue;
                if (!result.Contains(name)) result.Add(name);
            }
            return result;
        }

        /// <summary>
        /// 返回当前是否应显示挂件。spendActive 由调用方根据"最近一次检测到余额下降"给出。
        /// </summary>
        public bool Evaluate(DateTime nowUtc, string foregroundTitle, bool spendActive)
        {
            Reasons.Clear();

            if (MatchTitle(foregroundTitle, _config.TriggerTitleKeywords))
                Reasons.Add("窗口标题命中 deepseek 关键词");

            // 进程枚举比读窗口标题昂贵，节流到 2 秒一次
            if ((nowUtc - _lastProcessCheckUtc).TotalSeconds >= 2.0)
            {
                _processActive = AnyProcessRunning();
                _lastProcessCheckUtc = nowUtc;
            }
            if (_processActive)
                Reasons.Add("指定进程正在运行");

            if (spendActive && _config.TriggerOnSpend)
                Reasons.Add("检测到 API 余额下降");

            return Reasons.Count > 0;
        }

        private bool AnyProcessRunning()
        {
            string signature = _config.TriggerProcesses;
            if (signature != _processSignature)
            {
                _processSignature = signature;
                _processNames.Clear();
                _processNames.AddRange(ParseProcessNames(signature));
            }
            if (_processNames.Count == 0) return false;

            for (int i = 0; i < _processNames.Count; i++)
            {
                try
                {
                    Process[] found = Process.GetProcessesByName(_processNames[i]);
                    if (found != null && found.Length > 0)
                    {
                        for (int k = 0; k < found.Length; k++) found[k].Dispose();
                        return true;
                    }
                }
                catch { }
            }
            return false;
        }
    }
}
