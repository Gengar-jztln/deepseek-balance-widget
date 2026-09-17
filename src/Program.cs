using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace DeepSeekPet
{
    internal static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr context);

        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int processId);

        private const int AttachParentProcess = -1;

        [STAThread]
        private static int Main(string[] args)
        {
            bool cliMode = HasFlag(args, "--selftest") || HasFlag(args, "--render") || HasFlag(args, "--help") || HasFlag(args, "-h");
            if (cliMode) AttachConsole(AttachParentProcess);

            EnableDpiAwareness();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (HasFlag(args, "--help") || HasFlag(args, "-h"))
            {
                PrintHelp();
                return 0;
            }

            if (HasFlag(args, "--selftest"))
            {
                string report = Path.Combine(Store.Directory, "selftest-report.txt");
                string text = SelfTest.Run();
                try { File.WriteAllText(report, text, new UTF8Encoding(false)); } catch { }
                Console.WriteLine(text);
                Console.WriteLine();
                Console.WriteLine("报告文件：" + report);
                return text.IndexOf("FAIL", StringComparison.Ordinal) >= 0 ? 1 : 0;
            }

            if (HasFlag(args, "--diag"))
            {
                string report = WriteDiagnostics();
                Console.WriteLine(report);
                return 0;
            }

            bool demo = HasFlag(args, "--demo");
            string renderTarget = GetOption(args, "--render");
            string captureTarget = GetOption(args, "--capture");
            string keyOption = GetOption(args, "--key");

            Config config = Store.LoadConfig();
            State state = Store.LoadState();

            if (keyOption != null)
            {
                config.ApiKey = keyOption.Trim();
                Store.SaveConfig(config);
                Console.WriteLine("API Key 已写入 " + Store.ConfigPath);
                if (!HasFlag(args, "--nowindow")) return 0;
            }

            if (renderTarget != null)
            {
                RenderPreview(renderTarget, args, config, state);
                Console.WriteLine("预览图已生成：" + renderTarget);
                return 0;
            }

            if (captureTarget != null)
            {
                CaptureScreen(captureTarget);
                Console.WriteLine("屏幕截图已保存：" + captureTarget);
                return 0;
            }

            if (HasFlag(args, "--settings"))
            {
                Application.Run(new SettingsForm(config, state));
                return 0;
            }

            bool createdNew;
            using (Mutex mutex = new Mutex(true, "DeepSeekPet.SingleInstance.v1", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show("DeepSeek 余额挂件已经在运行，请查看任务栏右下角的托盘图标。",
                        "DeepSeek 余额挂件", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }

                if (demo)
                {
                    state.Calibrated = true;
                    state.CumulativeSpend = 14.35m;
                    state.LastTotal = 10.64m;
                }

                using (WidgetForm form = new WidgetForm(config, state, demo))
                {
                    Application.Run(form);
                }
            }
            return 0;
        }

        private static void EnableDpiAwareness()
        {
            try
            {
                // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4（Windows 10 1703+）
                if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return;
            }
            catch { }
            try { SetProcessDPIAware(); }
            catch { }
        }

        private static bool HasFlag(string[] args, string flag)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static string GetOption(string[] args, string name)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < args.Length) return args[i + 1];
                    return "";
                }
            }
            return null;
        }

        private static void PrintHelp()
        {
            Console.WriteLine("DeepSeek 余额挂件");
            Console.WriteLine();
            Console.WriteLine("  DeepSeekPet.exe                启动桌面挂件");
            Console.WriteLine("  DeepSeekPet.exe --key sk-xxx   写入 API Key 后退出");
            Console.WriteLine("  DeepSeekPet.exe --demo         以演示数据启动（不需要 API Key）");
            Console.WriteLine("  DeepSeekPet.exe --settings     直接打开设置窗口");
            Console.WriteLine("  DeepSeekPet.exe --selftest     运行自检并输出报告");
            Console.WriteLine("  DeepSeekPet.exe --diag         输出屏幕 / DPI / 尺寸诊断信息");
            Console.WriteLine("  DeepSeekPet.exe --render 文件  渲染预览图（--state ok|low|unc|error|empty --mini --dark）");
            Console.WriteLine("  DeepSeekPet.exe --capture 文件 抓取主屏截图（排障用）");
        }

        private static void CaptureScreen(string target)
        {
            Rectangle bounds = Screen.PrimaryScreen.Bounds;
            using (Bitmap bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, new Size(bounds.Width, bounds.Height));
                }
                bitmap.Save(target, ImageFormat.Png);
            }
        }

        private static string WriteDiagnostics()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("DeepSeek 余额挂件 环境诊断");
            sb.AppendLine("系统：" + Environment.OSVersion.VersionString);
            sb.AppendLine("进程位数：" + (IntPtr.Size == 8 ? "64 位" : "32 位"));
            sb.AppendLine("配置目录：" + Store.Directory);
            sb.AppendLine("可执行文件：" + Store.ExePath());

            float dpi = 96f;
            try
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) dpi = g.DpiX;
            }
            catch { }
            sb.AppendLine("主屏 DPI：" + dpi.ToString("0.##", CultureInfo.InvariantCulture) + "（缩放 " + (dpi / 96f).ToString("0.##", CultureInfo.InvariantCulture) + "x）");

            foreach (Screen screen in Screen.AllScreens)
            {
                sb.AppendLine("显示器 " + screen.DeviceName + " 边界 " + screen.Bounds + " 工作区 " + screen.WorkingArea
                    + (screen.Primary ? "（主屏）" : ""));
            }

            WidgetModel model = new WidgetModel();
            model.HasData = true;
            model.HasSpendValue = true;
            model.HasTrend = true;
            SizeF full = Renderer.Measure(model);
            model.MiniMode = true;
            SizeF mini = Renderer.Measure(model);
            float scale = dpi / 96f;
            sb.AppendLine("完整卡片逻辑尺寸：" + full.Width.ToString("0", CultureInfo.InvariantCulture) + "x" + full.Height.ToString("0", CultureInfo.InvariantCulture)
                + "，物理尺寸：" + (full.Width * scale).ToString("0", CultureInfo.InvariantCulture) + "x" + (full.Height * scale).ToString("0", CultureInfo.InvariantCulture));
            sb.AppendLine("迷你胶囊逻辑尺寸：" + mini.Width.ToString("0", CultureInfo.InvariantCulture) + "x" + mini.Height.ToString("0", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        private static void RenderPreview(string target, string[] args, Config config, State state)
        {
            string stateName = GetOption(args, "--state");
            if (stateName == null) stateName = "ok";
            bool mini = HasFlag(args, "--mini");
            bool dark = HasFlag(args, "--dark");
            float scale = 2f;
            string scaleOption = GetOption(args, "--scale");
            float parsed;
            if (scaleOption != null && float.TryParse(scaleOption, NumberStyles.Any, CultureInfo.InvariantCulture, out parsed) && parsed > 0.1f)
                scale = parsed;

            WidgetModel model = PreviewModel(stateName, mini);
            Palette palette = dark ? Palette.Dark() : Palette.Light();

            SizeF logical = Renderer.Measure(model);
            int width = (int)Math.Ceiling(logical.Width * scale);
            int height = (int)Math.Ceiling(logical.Height * scale);

            string dir = Path.GetDirectoryName(Path.GetFullPath(target));
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            using (Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    g.Clear(Color.Transparent);
                    g.ScaleTransform(scale, scale);
                    Renderer.Draw(g, model, palette);
                }
                bitmap.Save(target, ImageFormat.Png);
            }
        }

        private static WidgetModel PreviewModel(string stateName, bool mini)
        {
            WidgetModel model = new WidgetModel();
            model.MiniMode = mini;
            model.Hover = 0f;
            model.Spin = 0.25f;
            model.Currency = "CNY";
            model.Title = "DeepSeek 余额";

            switch (stateName)
            {
                case "low":
                    model.HasData = true;
                    model.ToppedUp = 2.18m;
                    model.Granted = 0m;
                    model.Total = 2.18m;
                    model.CumulativeSpend = 22.81m;
                    model.Calibrated = true;
                    model.HasSpendValue = true;
                    model.HasTrend = true;
                    model.Spend24h = 0.86m;
                    model.HasDaysLeft = true;
                    model.DaysLeft = 2.5;
                    model.LowBalance = true;
                    model.UpdatedText = "更新于 14:26";
                    model.RefreshText = "每 10 分钟刷新";
                    return model;

                case "unc":
                    model.HasData = true;
                    model.ToppedUp = 10.64m;
                    model.Total = 10.64m;
                    model.CumulativeSpend = 1.32m;
                    model.Calibrated = false;
                    model.HasSpendValue = true;
                    model.UpdatedText = "更新于 14:26";
                    model.RefreshText = "每 10 分钟刷新";
                    return model;

                case "error":
                    model.HasData = false;
                    model.IsError = true;
                    model.ErrorText = "API Key 无效或已失效（401）";
                    model.CumulativeSpend = 14.35m;
                    model.Calibrated = true;
                    model.HasSpendValue = true;
                    model.IsStale = true;
                    model.UpdatedText = "尚未获取数据";
                    model.RefreshText = "每 10 分钟刷新";
                    return model;

                case "empty":
                    model.HasData = false;
                    model.UpdatedText = "尚未获取数据";
                    model.RefreshText = "每 10 分钟刷新";
                    return model;

                case "loading":
                    model.HasData = true;
                    model.ToppedUp = 10.64m;
                    model.Total = 10.64m;
                    model.CumulativeSpend = 14.35m;
                    model.Calibrated = true;
                    model.HasSpendValue = true;
                    model.IsLoading = true;
                    model.UpdatedText = "更新于 14:26";
                    model.RefreshText = "每 10 分钟刷新";
                    return model;

                default: // ok
                    model.HasData = true;
                    model.ToppedUp = 10.64m;
                    model.Granted = 0m;
                    model.Total = 10.64m;
                    model.CumulativeSpend = 14.35m;
                    model.Calibrated = true;
                    model.HasSpendValue = true;
                    model.HasTrend = true;
                    model.Spend24h = 1.20m;
                    model.HasDaysLeft = true;
                    model.DaysLeft = 8.9;
                    model.UpdatedText = "更新于 14:26";
                    model.RefreshText = "每 10 分钟刷新";
                    return model;
            }
        }
    }

    internal static class SelfTest
    {
        public static string Run()
        {
            StringBuilder sb = new StringBuilder();
            int failures = 0;

            sb.AppendLine("DeepSeek 余额挂件 自检报告");
            sb.AppendLine("时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            sb.AppendLine("可执行文件：" + Store.ExePath());
            sb.AppendLine("配置目录：" + Store.Directory);
            sb.AppendLine();

            // 1. JSON 解析
            const string apiSample = "{\"is_available\":true,\"balance_infos\":[{\"currency\":\"CNY\",\"total_balance\":\"110.00\",\"granted_balance\":\"10.00\",\"topped_up_balance\":\"100.00\"}]}";
            try
            {
                BalanceSnapshot snapshot = DeepSeekApi.ParseBody(apiSample, "CNY", DateTime.UtcNow);
                Check(sb, ref failures, "解析官方余额示例", snapshot.Ok
                    && snapshot.Info.ToppedUp == 100.00m
                    && snapshot.Info.Granted == 10.00m
                    && snapshot.Info.Total == 110.00m);

                string roundTrip = Json.Write(Json.Parse(apiSample));
                Check(sb, ref failures, "JSON 读写往返", Json.Parse(roundTrip) is System.Collections.Generic.Dictionary<string, object>);
            }
            catch (Exception ex)
            {
                Check(sb, ref failures, "解析官方余额示例", false, ex.Message);
            }

            // 2. 异常响应处理
            try
            {
                BalanceSnapshot bad = DeepSeekApi.ParseBody("{\"error\":{\"message\":\"Authentication Fails\"}}", "CNY", DateTime.UtcNow);
                Check(sb, ref failures, "异常响应不崩溃且给出错误", !bad.Ok && bad.Error.Length > 0);
            }
            catch (Exception ex)
            {
                Check(sb, ref failures, "异常响应不崩溃且给出错误", false, ex.Message);
            }

            // 3. 累计消费估算
            Config config = new Config();
            config.SpendCalibration = 14.35m;
            State state = new State();
            DateTime baseTime = new DateTime(2026, 9, 16, 0, 0, 0, DateTimeKind.Utc);

            BalanceInfo info = new BalanceInfo();
            info.Total = 10.64m;
            SpendTracker.Apply(state, config, info, baseTime);
            Check(sb, ref failures, "首次采样采用校准值", state.CumulativeSpend == 14.35m);

            info = new BalanceInfo(); info.Total = 10.24m;
            SpendTracker.Apply(state, config, info, baseTime.AddMinutes(10));
            Check(sb, ref failures, "余额下降计入消费", state.CumulativeSpend == 14.75m);

            info = new BalanceInfo(); info.Total = 30.24m;
            SpendTracker.Apply(state, config, info, baseTime.AddMinutes(20));
            Check(sb, ref failures, "充值不计入消费", state.CumulativeSpend == 14.75m);

            info = new BalanceInfo(); info.Total = 29.94m;
            SpendTracker.Apply(state, config, info, baseTime.AddMinutes(30));
            Check(sb, ref failures, "充值后继续累加", state.CumulativeSpend == 15.05m);

            decimal window = SpendTracker.SpendInWindow(state, TimeSpan.FromHours(24), baseTime.AddMinutes(30));
            Check(sb, ref failures, "窗口消费统计", window == 0.70m, "实际 " + Renderer.Money(window));

            // 4. GDI+ 像素格式（透明通道语义）
            try
            {
                using (Bitmap probe = new Bitmap(2, 2, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(probe)) g.Clear(Color.FromArgb(128, 255, 64, 32));
                    BitmapData data = probe.LockBits(new Rectangle(0, 0, 2, 2), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                    byte[] buffer = new byte[4];
                    Marshal.Copy(data.Scan0, buffer, 0, 4);
                    probe.UnlockBits(data);
                    bool straight = buffer[2] == 255 && buffer[3] == 128;
                    sb.AppendLine("[像素] BGRA = " + buffer[0] + "," + buffer[1] + "," + buffer[2] + "," + buffer[3]
                        + (straight ? "（直通 Alpha，需要预乘）" : "（已预乘或格式不同，请核对）"));
                    Check(sb, ref failures, "位图使用直通 Alpha（预乘逻辑正确）", straight);
                }
            }
            catch (Exception ex)
            {
                Check(sb, ref failures, "位图 Alpha 检查", false, ex.Message);
            }

            // 5. 布局尺寸
            WidgetModel model = new WidgetModel();
            model.HasData = true;
            SizeF full = Renderer.Measure(model);
            model.MiniMode = true;
            SizeF mini = Renderer.Measure(model);
            Check(sb, ref failures, "卡片/胶囊尺寸有效",
                full.Width > 200f && full.Height > 150f && mini.Width > 100f && mini.Height > 50f);

            sb.AppendLine();
            sb.AppendLine(failures == 0 ? "结果：PASS（全部通过）" : "结果：FAIL（" + failures + " 项未通过）");
            return sb.ToString();
        }

        private static void Check(StringBuilder sb, ref int failures, string name, bool ok, string detail)
        {
            if (!ok) failures++;
            sb.AppendLine("[" + (ok ? "PASS" : "FAIL") + "] " + name + (string.IsNullOrEmpty(detail) ? "" : " —— " + detail));
        }

        private static void Check(StringBuilder sb, ref int failures, string name, bool ok)
        {
            Check(sb, ref failures, name, ok, "");
        }
    }
}
