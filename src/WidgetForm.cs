using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DeepSeekPet
{
    /// <summary>
    /// 桌面余额挂件主窗口：透明分层窗口，可拖动、可切换迷你模式、可隐藏到托盘。
    /// </summary>
    internal sealed class WidgetForm : Form
    {
        private readonly Config _config;
        private readonly State _state;
        private readonly bool _demo;
        private readonly TriggerEvaluator _triggers;
        private readonly LayeredSurface _surface = new LayeredSurface();
        private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer();
        private readonly NotifyIcon _tray = new NotifyIcon();
        private readonly ContextMenuStrip _menu = new ContextMenuStrip();
        private readonly object _sync = new object();

        private Bitmap _canvas;
        private BalanceSnapshot _snapshot = new BalanceSnapshot();
        private DateTime _lastAttemptUtc = DateTime.MinValue;
        private DateTime _lastSuccessUtc = DateTime.MinValue;
        private readonly DateTime _startedUtc = DateTime.UtcNow;

        private float _phase;
        private float _hover;
        private float _hoverTarget;
        private bool _dirty = true;
        private int _x;
        private int _y;
        private int _lastWidth;
        private int _lastHeight;
        private bool _placed;
        private float _scale = 1f;
        private bool _refreshing;
        private bool _notifiedLow;
        private DateTime _lastNotifyUtc = DateTime.MinValue;
        private DateTime _triggerUntilUtc = DateTime.MinValue;
        private DateTime _lastTriggerCheckUtc = DateTime.MinValue;
        private bool _manualVisible = true;
        private bool _settingsOpen;
        private DateTime _spendPulseUtc = DateTime.MinValue;
        private decimal _spendPulseAmount;
        private bool _dragging;
        private bool _dragMoved;
        private Point _dragCursorOrigin;
        private int _dragWindowX;
        private int _dragWindowY;

        private const int HotkeyId = 0xB105;
        private const int WM_HOTKEY = 0x0312;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint VK_Q = 0x51;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        public WidgetForm(Config config, State state, bool demo)
        {
            _config = config;
            _state = state;
            _demo = demo;
            _triggers = new TriggerEvaluator(config);

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = _config.TopMost;
            StartPosition = FormStartPosition.Manual;
            Text = "DeepSeek 余额挂件";
            MinimumSize = new Size(1, 1);
            SetStyle(ControlStyles.Opaque | ControlStyles.UserPaint, true);

            _timer.Interval = 40;
            _timer.Tick += OnTick;

            BuildMenus();
            BuildTray();
        }

        public DateTime LastSuccessUtc { get { return _lastSuccessUtc; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00080000;   // WS_EX_LAYERED
                cp.ExStyle |= 0x00000080;   // WS_EX_TOOLWINDOW：不在 Alt+Tab 中出现
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            UpdateScale();

            if (IsHandleCreated) RegisterHotKey(Handle, HotkeyId, MOD_CONTROL | MOD_ALT, VK_Q);

            WidgetModel model = BuildModel();
            SizeF size = Renderer.Measure(model);
            int width = (int)Math.Ceiling(size.Width * _scale);
            int height = (int)Math.Ceiling(size.Height * _scale);

            _x = _config.X == int.MinValue ? int.MinValue : _config.X;
            _y = _config.Y == int.MinValue ? int.MinValue : _config.Y;
            PlaceInitial(width, height);

            SetBounds(_x, _y, width, height);
            _lastWidth = width;
            _lastHeight = height;
            _placed = true;
            if (_demo)
            {
                _snapshot = DemoSnapshot();
                _lastSuccessUtc = DateTime.UtcNow;
            }
            Render(true);
            _timer.Start();
            StartRefresh();

            if (_config.ShowMode == "trigger")
            {
                // 触发模式：启动后只驻留托盘，满足条件时才显示卡片
                _manualVisible = false;
                BeginInvoke(new MethodInvoker(delegate { Hide(); }));
            }
        }

        private BalanceSnapshot DemoSnapshot()
        {
            BalanceSnapshot snapshot = new BalanceSnapshot();
            snapshot.Ok = true;
            snapshot.IsAvailable = true;
            snapshot.FetchedAtUtc = DateTime.UtcNow;
            snapshot.Info = new BalanceInfo();
            snapshot.Info.Currency = "CNY";
            snapshot.Info.ToppedUp = 10.64m;
            snapshot.Info.Granted = 0m;
            snapshot.Info.Total = 10.64m;
            return snapshot;
        }

        private void UpdateScale()
        {
            float dpi = 96f;
            try
            {
                uint value = GetDpiForWindow(Handle);
                if (value >= 72 && value <= 480) dpi = value;
            }
            catch { }
            _scale = dpi / 96f;
        }

        private void PlaceInitial(int width, int height)
        {
            Rectangle workArea = Screen.FromPoint(new Point(_x == int.MinValue ? 0 : _x, _y == int.MinValue ? 0 : _y)).WorkingArea;
            if (_x == int.MinValue || _y == int.MinValue || !IsOnScreen(_x, _y, width, height))
            {
                _x = workArea.Right - width - 24;
                _y = workArea.Bottom - height - 24;
            }
            ClampToScreen(width, height);
        }

        private bool IsOnScreen(int x, int y, int width, int height)
        {
            Rectangle bounds = new Rectangle(x, y, width, height);
            foreach (Screen screen in Screen.AllScreens)
            {
                Rectangle inter = Rectangle.Intersect(screen.WorkingArea, bounds);
                if (inter.Width > 40 && inter.Height > 40) return true;
            }
            return false;
        }

        private void ClampToScreen(int width, int height)
        {
            Rectangle workArea = Screen.FromPoint(new Point(_x + width / 2, _y + height / 2)).WorkingArea;
            // 挂件整体保持在工作区内，避免出现边缘被裁掉的情况
            if (_x + width > workArea.Right) _x = workArea.Right - width;
            if (_y + height > workArea.Bottom) _y = workArea.Bottom - height;
            if (_x < workArea.Left) _x = workArea.Left;
            if (_y < workArea.Top) _y = workArea.Top;
        }

        // ---------------- 定时与渲染 ----------------

        private void OnTick(object sender, EventArgs e)
        {
            _phase += 0.012f;
            if (_phase > 1000f) _phase = 0f;

            float previousHover = _hover;
            _hover += (_hoverTarget - _hover) * 0.22f;
            if (Math.Abs(_hover - previousHover) > 0.002f) _dirty = true;

            bool animating = _refreshing || Math.Abs(_hover - _hoverTarget) > 0.01f;
            if (_config.LowBalanceThreshold > 0m && _snapshot.Ok && _snapshot.Info != null)
                animating = animating || _snapshot.Info.ToppedUp <= _config.LowBalanceThreshold;

            DateTime now = DateTime.UtcNow;
            if ((now - _lastAttemptUtc).TotalSeconds >= EffectivePollSeconds()) StartRefresh();

            if (_config.ShowMode == "trigger")
            {
                if ((now - _lastTriggerCheckUtc).TotalMilliseconds >= 1500)
                {
                    EvaluateTriggers(now);
                    _lastTriggerCheckUtc = now;
                }
                ApplyTriggerVisibility(now);
            }

            if (_dirty || animating) Render(false);
            else if (DateTime.Now.Millisecond < 45) Render(false);   // 兜底：至少每秒刷新一次
        }

        /// <summary>触发模式下用更短的轮询间隔，以便及时发现自己这个 Key 的消费。</summary>
        private int EffectivePollSeconds()
        {
            return _config.ShowMode == "trigger" ? _config.TriggerPollSeconds : _config.RefreshSeconds;
        }

        private bool SpendPulseActive(DateTime now)
        {
            if (!_config.TriggerOnSpend || _spendPulseUtc == DateTime.MinValue) return false;
            return (now - _spendPulseUtc).TotalSeconds <= Math.Max(_config.TriggerStaySeconds, 60);
        }

        private void EvaluateTriggers(DateTime now)
        {
            string title = ForegroundWindowInfo.CurrentTitle();
            bool active = _triggers.Evaluate(now, title, SpendPulseActive(now));
            if (!active) return;

            if (_config.TriggerStaySeconds <= 0) _manualVisible = true;      // 0 = 触发后不再自动隐藏
            else _triggerUntilUtc = now.AddSeconds(_config.TriggerStaySeconds);
            _dirty = true;
        }

        private void ApplyTriggerVisibility(DateTime now)
        {
            if (_manualVisible) return;
            if (_menu.Visible || _settingsOpen || _dragging) return;
            if (Visible && _hover > 0.05f)
            {
                // 鼠标停留在卡片上时顺延，避免看着看着就消失
                _triggerUntilUtc = now.AddSeconds(Math.Max(_config.TriggerStaySeconds, 5));
                return;
            }

            if (now <= _triggerUntilUtc)
            {
                if (!Visible)
                {
                    Show();
                    _dirty = true;
                }
            }
            else if (Visible)
            {
                Hide();
            }
        }

        private void Render(bool force)
        {
            if (!IsHandleCreated) return;
            WidgetModel model = BuildModel();
            Palette palette = ResolvePalette();

            SizeF logical = Renderer.Measure(model);
            int width = (int)Math.Ceiling(logical.Width * _scale);
            int height = (int)Math.Ceiling(logical.Height * _scale);
            if (width < 1) width = 1;
            if (height < 1) height = 1;

            if (_canvas == null || _canvas.Width != width || _canvas.Height != height)
            {
                if (_canvas != null) _canvas.Dispose();
                _canvas = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                _dragging = false;
                if (_placed)
                {
                    // 卡片变高或变宽时保持右下角不动，避免越出屏幕
                    _x -= width - _lastWidth;
                    _y -= height - _lastHeight;
                    ClampToScreen(width, height);
                }
            }
            _lastWidth = width;
            _lastHeight = height;

            using (Graphics g = Graphics.FromImage(_canvas))
            {
                g.Clear(Color.Transparent);
                g.ScaleTransform(_scale, _scale);
                Renderer.Draw(g, model, palette);
            }

            try
            {
                _surface.Present(Handle, _canvas, _x, _y);
                if (Width != width || Height != height) SetBounds(_x, _y, width, height);
            }
            catch { }

            if (force) _dirty = true;
            _dirty = false;
        }

        private Palette ResolvePalette()
        {
            if (_config.Theme == "dark") return Palette.Dark();
            if (_config.Theme == "light") return Palette.Light();
            return IsSystemLightTheme() ? Palette.Light() : Palette.Dark();
        }

        private static bool IsSystemLightTheme()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key != null)
                    {
                        object value = key.GetValue("AppsUseLightTheme");
                        if (value is int) return (int)value != 0;
                    }
                }
            }
            catch { }
            return true;
        }

        private WidgetModel BuildModel()
        {
            WidgetModel model = new WidgetModel();
            model.MiniMode = _config.MiniMode;
            model.Spin = _phase % 1f;
            model.Hover = _hover;
            model.IsLoading = _refreshing;
            model.Currency = _config.Currency;

            DateTime now = DateTime.UtcNow;
            bool hasFreshData = _snapshot.Ok && _snapshot.Info != null;

            if (hasFreshData)
            {
                model.HasData = true;
                model.Currency = string.IsNullOrEmpty(_snapshot.Info.Currency) ? _config.Currency : _snapshot.Info.Currency;
                model.ToppedUp = _snapshot.Info.ToppedUp;
                model.Granted = _snapshot.Info.Granted;
                model.Total = _snapshot.Info.Total;
            }
            else if (_state.LastToppedUp >= 0m)
            {
                // 网络异常或刚启动时，先用上一次成功获取的余额占位，避免显示空白
                model.HasData = true;
                model.Currency = string.IsNullOrEmpty(_state.LastCurrency) ? _config.Currency : _state.LastCurrency;
                model.ToppedUp = _state.LastToppedUp;
                model.Granted = _state.LastGranted;
                model.Total = _state.LastTotal >= 0m ? _state.LastTotal : _state.LastToppedUp;
            }

            model.CumulativeSpend = _state.CumulativeSpend;
            model.Calibrated = _state.Calibrated;
            model.HasSpendValue = _state.Calibrated || _state.CumulativeSpend > 0m;

            model.HasTrend = model.HasData && SpendTracker.HasWindowData(_state, TimeSpan.FromHours(24), now);
            if (model.HasTrend) model.Spend24h = SpendTracker.SpendInWindow(_state, TimeSpan.FromHours(24), now);
            double days = 0;
            model.HasDaysLeft = model.HasData
                && SpendTracker.TryEstimateDaysLeft(_state, model.Total, now, out days);
            if (model.HasDaysLeft) model.DaysLeft = days;

            model.LowBalance = model.HasData && _config.LowBalanceThreshold > 0m
                && model.ToppedUp <= _config.LowBalanceThreshold;

            model.ShowSpendPulse = SpendPulseActive(now);
            model.SpendPulse = _spendPulseAmount;

            string error = "";
            lock (_sync) { error = _state.LastError; }
            model.IsError = error.Length > 0 && !_refreshing;
            model.ErrorText = DescribeError(error);

            if (_lastSuccessUtc != DateTime.MinValue)
            {
                DateTime local = _lastSuccessUtc.ToLocalTime();
                model.UpdatedText = "更新于 " + local.ToString("HH:mm", CultureInfo.InvariantCulture);
                double ageMinutes = (now - _lastSuccessUtc).TotalMinutes;
                model.IsStale = ageMinutes > Math.Max(EffectivePollSeconds() / 60.0 * 3.0, 30.0);
            }
            else
            {
                model.UpdatedText = "尚未获取数据";
                model.IsStale = true;
            }

            int pollSeconds = EffectivePollSeconds();
            string pollVerb = _config.ShowMode == "trigger" ? "检测" : "刷新";
            if (pollSeconds % 60 == 0)
            {
                int minutes = pollSeconds / 60;
                model.RefreshText = minutes >= 60
                    ? "每 " + (minutes / 60).ToString(CultureInfo.InvariantCulture) + " 小时" + pollVerb
                    : "每 " + minutes.ToString(CultureInfo.InvariantCulture) + " 分钟" + pollVerb;
            }
            else
            {
                model.RefreshText = "每 " + pollSeconds.ToString(CultureInfo.InvariantCulture) + " 秒" + pollVerb;
            }

            return model;
        }

        private static string DescribeError(string error)
        {
            if (string.IsNullOrEmpty(error)) return "";
            if (error.IndexOf("尚未填写", StringComparison.Ordinal) >= 0)
                return "未配置 API Key · 右键「设置…」填写";
            return error;
        }

        // ---------------- 数据刷新 ----------------

        private void StartRefresh()
        {
            if (_demo)
            {
                lock (_sync)
                {
                    if (_refreshing) return;
                    _refreshing = true;
                }
                _dirty = true;
                ThreadPool.QueueUserWorkItem(delegate(object ignored)
                {
                    Thread.Sleep(700);
                    try
                    {
                        if (IsHandleCreated) BeginInvoke(new MethodInvoker(delegate
                        {
                            _refreshing = false;
                            _lastSuccessUtc = DateTime.UtcNow;
                            _lastAttemptUtc = DateTime.UtcNow;
                            _dirty = true;
                        }));
                    }
                    catch { }
                });
                return;
            }

            lock (_sync)
            {
                if (_refreshing) return;
                if (_lastAttemptUtc != DateTime.MinValue && (DateTime.UtcNow - _lastAttemptUtc).TotalSeconds < 5) return;
                _refreshing = true;
                _lastAttemptUtc = DateTime.UtcNow;
            }
            _dirty = true;

            string apiKey = _config.ApiKey;
            string currency = _config.Currency;
            ThreadPool.QueueUserWorkItem(delegate(object ignored)
            {
                BalanceSnapshot result = DeepSeekApi.Fetch(apiKey, currency, 15000);
                try
                {
                    if (IsHandleCreated) BeginInvoke(new MethodInvoker(delegate { ApplyResult(result); }));
                }
                catch { }
            });
        }

        private void ApplyResult(BalanceSnapshot result)
        {
            lock (_sync)
            {
                _refreshing = false;
            }

            DateTime now = DateTime.UtcNow;
            if (result.Ok && result.Info != null)
            {
                decimal previousTotal = _state.LastTotal;
                SpendTracker.Apply(_state, _config, result.Info, now);
                if (previousTotal >= 0m && result.Info.Total < previousTotal)
                {
                    // 余额下降 = 这个 API Key 真的被用了，作为“正在使用 DeepSeek”的硬信号
                    _spendPulseUtc = now;
                    _spendPulseAmount = previousTotal - result.Info.Total;
                }
                _state.LastSuccessUtc = now.ToString("o", CultureInfo.InvariantCulture);
                _state.LastError = "";
                _lastSuccessUtc = now;
                _snapshot = result;
                Store.SaveState(_state);
                MaybeNotifyLowBalance(result);
            }
            else
            {
                _state.LastError = result.Error;
                Store.SaveState(_state);
            }

            _dirty = true;
            UpdateTrayText();
            Render(false);
        }

        private void MaybeNotifyLowBalance(BalanceSnapshot result)
        {
            if (!_config.NotifyLowBalance) return;
            if (_config.LowBalanceThreshold <= 0m) return;
            bool low = result.Info.ToppedUp <= _config.LowBalanceThreshold;
            if (low && !_notifiedLow && (DateTime.UtcNow - _lastNotifyUtc).TotalMinutes > 30)
            {
                _notifiedLow = true;
                _lastNotifyUtc = DateTime.UtcNow;
                try
                {
                    _tray.BalloonTipTitle = "DeepSeek 余额不足";
                    _tray.BalloonTipText = "充值余额 ¥" + Renderer.Money(result.Info.ToppedUp) + result.Info.Currency
                        + "，低于阈值 ¥" + Renderer.Money(_config.LowBalanceThreshold) + "，建议尽快充值。";
                    _tray.BalloonTipIcon = ToolTipIcon.Warning;
                    _tray.ShowBalloonTip(8000);
                }
                catch { }
            }
            else if (!low)
            {
                _notifiedLow = false;
            }
        }

        // ---------------- 交互 ----------------

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hoverTarget = 1f;
            _dirty = true;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hoverTarget = 0f;
            _dirty = true;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            _dragging = true;
            _dragMoved = false;
            _dragCursorOrigin = Cursor.Position;
            _dragWindowX = _x;
            _dragWindowY = _y;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging) return;
            Point current = Cursor.Position;
            int dx = current.X - _dragCursorOrigin.X;
            int dy = current.Y - _dragCursorOrigin.Y;
            if (!_dragMoved && (dx * dx + dy * dy) > 9) _dragMoved = true;
            if (!_dragMoved) return;

            _x = _dragWindowX + dx;
            _y = _dragWindowY + dy;
            _dirty = true;
            Render(false);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left || !_dragging) return;
            _dragging = false;
            if (_dragMoved)
            {
                int width = _canvas != null ? _canvas.Width : Width;
                int height = _canvas != null ? _canvas.Height : Height;
                ClampToScreen(width, height);
                UpdateScale();
                _config.X = _x;
                _config.Y = _y;
                Store.SaveConfig(_config);
                Render(true);
            }
            else
            {
                StartRefresh();
            }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button != MouseButtons.Left) return;
            ToggleMiniMode();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
            {
                ToggleVisible();
                return;
            }
            base.WndProc(ref m);
        }

        public void ToggleVisible()
        {
            if (Visible)
            {
                Hide();
                _manualVisible = false;
            }
            else
            {
                Show();
                _manualVisible = true;   // 手动打开就一直显示，直到再次手动隐藏
                Render(true);
            }
        }

        private void ToggleMiniMode()
        {
            _config.MiniMode = !_config.MiniMode;
            Store.SaveConfig(_config);
            Render(true);
            UpdateMenuChecks();
        }

        // ---------------- 菜单与托盘 ----------------

        private void BuildMenus()
        {
            _menu.ShowImageMargin = false;
            AddMenuItem("立即刷新", delegate { StartRefresh(); });
            AddMenuItem("迷你模式", delegate { ToggleMiniMode(); }, true, "mini");
            AddMenuItem("总在最前", delegate { ToggleTopMost(); }, true, "topmost");
            AddMenuItem("隐藏挂件（Ctrl+Alt+Q 呼出）", delegate { _manualVisible = false; Hide(); });
            _menu.Items.Add(new ToolStripSeparator());
            AddMenuItem("打开充值页面", delegate { OpenUrl(DeepSeekApi.TopUpUrl); });
            AddMenuItem("打开用量页面", delegate { OpenUrl(DeepSeekApi.UsageUrl); });
            AddMenuItem("设置…", delegate { OpenSettings(); });
            AddMenuItem("开机自动启动", delegate { ToggleAutoStart(); }, true, "autostart");
            _menu.Items.Add(new ToolStripSeparator());
            AddMenuItem("关于", delegate { ShowAbout(); });
            AddMenuItem("退出", delegate { CloseApp(); });
            ContextMenuStrip = _menu;
            UpdateMenuChecks();
        }

        private void AddMenuItem(string text, EventHandler handler, bool checkable = false, string tag = null)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.ShowShortcutKeys = false;
            if (checkable)
            {
                item.CheckOnClick = false;
                item.Tag = tag;
                if (tag == "mini") item.Checked = _config.MiniMode;
                else if (tag == "topmost") item.Checked = _config.TopMost;
                else item.Checked = IsAutoStartEnabled();
            }
            item.Click += handler;
            _menu.Items.Add(item);
        }

        private void UpdateMenuChecks()
        {
            foreach (ToolStripItem item in _menu.Items)
            {
                ToolStripMenuItem menuItem = item as ToolStripMenuItem;
                if (menuItem == null || menuItem.Tag == null) continue;
                string tag = Convert.ToString(menuItem.Tag, CultureInfo.InvariantCulture);
                if (tag == "mini") menuItem.Checked = _config.MiniMode;
                else if (tag == "topmost") menuItem.Checked = _config.TopMost;
                else if (tag == "autostart") menuItem.Checked = IsAutoStartEnabled();
            }
        }

        private void ToggleTopMost()
        {
            _config.TopMost = !_config.TopMost;
            TopMost = _config.TopMost;
            Store.SaveConfig(_config);
            UpdateMenuChecks();
            Render(true);
        }

        private void BuildTray()
        {
            _tray.Icon = BuildTrayIcon();
            _tray.Text = "DeepSeek 余额挂件";
            _tray.Visible = true;
            _tray.ContextMenuStrip = _menu;
            _tray.MouseClick += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) ToggleVisible();
            };
        }

        private Icon BuildTrayIcon()
        {
            try
            {
                using (Bitmap bitmap = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(bitmap))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.Clear(Color.Transparent);
                        Renderer.DrawWhaleIcon(g, new RectangleF(2f, 2f, 28f, 28f), Palette.Light().Accent);
                    }
                    IntPtr handle = bitmap.GetHicon();
                    try
                    {
                        using (Icon raw = Icon.FromHandle(handle))
                        {
                            return (Icon)raw.Clone();
                        }
                    }
                    finally
                    {
                        DestroyIcon(handle);
                    }
                }
            }
            catch
            {
                return SystemIcons.Application;
            }
        }

        private void UpdateTrayText()
        {
            string text = "DeepSeek 余额挂件";
            if (_snapshot.Ok && _snapshot.Info != null)
                text = "充值余额 ¥" + Renderer.Money(_snapshot.Info.ToppedUp) + " / 累计消费 ≈¥" + Renderer.Money(_state.CumulativeSpend);
            if (text.Length > 63) text = text.Substring(0, 60) + "…";
            try { _tray.Text = text; }
            catch { }
        }

        private void OpenSettings()
        {
            SettingsForm form = new SettingsForm(_config, _state);
            _settingsOpen = true;
            form.SettingsSaved += delegate
            {
                _dirty = true;
                UpdateMenuChecks();
                if (_config.ShowMode == "trigger" && !_manualVisible)
                {
                    // 切到触发模式后，若当前没有触发条件就立刻收起
                    _triggerUntilUtc = DateTime.MinValue;
                    _lastTriggerCheckUtc = DateTime.MinValue;
                }
                if (_config.ApiKey.Trim().Length > 0)
                {
                    _lastAttemptUtc = DateTime.MinValue;
                    StartRefresh();
                }
                Render(true);
            };
            form.FormClosed += delegate
            {
                _settingsOpen = false;
                _dirty = true;
            };
            form.Show();
            form.Activate();
        }

        private void ShowAbout()
        {
            string text =
                "DeepSeek 余额挂件 v1.1" + Environment.NewLine + Environment.NewLine +
                "数据来源：" + DeepSeekApi.BalanceUrl + Environment.NewLine + Environment.NewLine +
                "· 充值余额 / 赠送余额 / 总余额：来自官方接口，精确值。" + Environment.NewLine +
                "· 累计消费：官方接口未提供，本程序通过余额下降量累加估算，" + Environment.NewLine +
                "  可用控制台显示的累计消费金额在「设置」中校准。" + Environment.NewLine + Environment.NewLine +
                "· 触发显示：可设置为“使用 DeepSeek 时才显示”，触发条件为" + Environment.NewLine +
                "  窗口标题关键词 / 指定进程运行 / 自己的 Key 发生消费。" + Environment.NewLine + Environment.NewLine +
                "配置文件：" + Store.ConfigPath;
            MessageBox.Show(text, "关于", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OpenUrl(string url)
        {
            try { System.Diagnostics.Process.Start(url); }
            catch (Exception ex) { MessageBox.Show("无法打开链接：" + ex.Message, "提示"); }
        }

        private static string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private static string RunValueName = "DeepSeekPet";

        private static bool IsAutoStartEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath))
                {
                    return key != null && key.GetValue(RunValueName) != null;
                }
            }
            catch { return false; }
        }

        private void ToggleAutoStart()
        {
            bool enabled = IsAutoStartEnabled();
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    if (key == null) return;
                    if (enabled) key.DeleteValue(RunValueName, false);
                    else key.SetValue(RunValueName, "\"" + Store.ExePath() + "\"");
                }
                _config.AutoStart = !enabled;
                Store.SaveConfig(_config);
            }
            catch (Exception ex)
            {
                MessageBox.Show("设置开机启动失败：" + ex.Message, "提示");
            }
            UpdateMenuChecks();
        }

        public void CloseApp()
        {
            try
            {
                _timer.Stop();
                _config.X = _x;
                _config.Y = _y;
                Store.SaveConfig(_config);
                Store.SaveState(_state);
            }
            catch { }
            try { _tray.Visible = false; _tray.Dispose(); } catch { }
            try { UnregisterHotKey(Handle, HotkeyId); } catch { }
            Close();
            Application.Exit();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                // 直接关闭时只隐藏，避免误操作导致退出
                e.Cancel = true;
                Hide();
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Dispose();
                _surface.Dispose();
                if (_canvas != null) { _canvas.Dispose(); _canvas = null; }
                _menu.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
