using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace DeepSeekPet
{
    /// <summary>
    /// 设置窗口（常规 / 触发显示 两个标签页）。
    /// 所有尺寸按当前 DPI 缩放，保证在 125% / 150% / 200% 缩放下文字与控件比例一致。
    /// </summary>
    internal sealed class SettingsForm : Form
    {
        private readonly Config _config;
        private readonly State _state;
        private readonly float _scale = 1f;

        private readonly TextBox _apiKey = new TextBox();
        private readonly CheckBox _showKey = new CheckBox();
        private readonly NumericUpDown _refresh = new NumericUpDown();
        private readonly NumericUpDown _threshold = new NumericUpDown();
        private readonly NumericUpDown _calibration = new NumericUpDown();
        private readonly ComboBox _theme = new ComboBox();
        private readonly CheckBox _mini = new CheckBox();
        private readonly CheckBox _notify = new CheckBox();
        private readonly Label _status = new Label();
        private readonly Button _testButton = new Button();

        private readonly ComboBox _showMode = new ComboBox();
        private readonly NumericUpDown _triggerStay = new NumericUpDown();
        private readonly NumericUpDown _triggerPoll = new NumericUpDown();
        private readonly TextBox _triggerKeywords = new TextBox();
        private readonly TextBox _triggerProcesses = new TextBox();
        private readonly CheckBox _triggerOnSpend = new CheckBox();

        public event EventHandler SettingsSaved;

        private int Px(float value)
        {
            return (int)Math.Round(value * _scale, MidpointRounding.AwayFromZero);
        }

        public SettingsForm(Config config, State state)
            : this(config, state, 0)
        {
        }

        public SettingsForm(Config config, State state, int initialTab)
        {
            _config = config;
            _state = state;

            float dpi = 96f;
            try
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) dpi = g.DpiX;
            }
            catch { }
            _scale = dpi / 96f;
            if (_scale < 1f) _scale = 1f;

            Text = "DeepSeek 余额挂件 · 设置";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;     // 手动缩放，避免二次放大
            ClientSize = new Size(Px(560f), Px(472f));
            Font = new Font("Microsoft YaHei UI", 9f * _scale, FontStyle.Regular, GraphicsUnit.Point);
            TopMost = true;
            ShowInTaskbar = true;

            TabControl tabs = new TabControl();
            tabs.Location = new Point(Px(10f), Px(10f));
            tabs.Size = new Size(Px(540f), Px(404f));
            Controls.Add(tabs);

            TabPage general = new TabPage("常规");
            TabPage trigger = new TabPage("触发显示");
            general.UseVisualStyleBackColor = true;
            trigger.UseVisualStyleBackColor = true;
            tabs.TabPages.Add(general);
            tabs.TabPages.Add(trigger);

            BuildGeneralTab(general);
            BuildTriggerTab(trigger);
            if (initialTab > 0 && initialTab < tabs.TabPages.Count) tabs.SelectedIndex = initialTab;
            BuildButtonRow();
        }

        // ---------------- 常规 ----------------

        private void BuildGeneralTab(TabPage parent)
        {
            int labelX = Px(14f);
            int labelWidth = Px(160f);
            int fieldX = Px(176f);
            int fieldWidth = Px(320f);
            int y = Px(14f);

            AddLabel(parent, "API Key", labelX, y + Px(3f), labelWidth);
            _apiKey.Location = new Point(fieldX, y);
            _apiKey.Size = new Size(fieldWidth - Px(56f), Px(24f));
            _apiKey.UseSystemPasswordChar = true;
            _apiKey.Text = _config.ApiKey;
            parent.Controls.Add(_apiKey);

            _showKey.Text = "显示";
            _showKey.Location = new Point(fieldX + fieldWidth - Px(48f), y + Px(2f));
            _showKey.AutoSize = true;
            _showKey.CheckedChanged += delegate { _apiKey.UseSystemPasswordChar = !_showKey.Checked; };
            parent.Controls.Add(_showKey);
            y += Px(26f);

            AddHint(parent, "platform.deepseek.com → API keys 创建", labelX, y, fieldWidth + Px(60f));
            y += Px(32f);

            AddLabel(parent, "刷新间隔（秒）", labelX, y + Px(3f), labelWidth);
            _refresh.Location = new Point(fieldX, y);
            _refresh.Size = new Size(Px(110f), Px(24f));
            _refresh.Minimum = 60;
            _refresh.Maximum = 86400;
            _refresh.Increment = 60;
            _refresh.Value = Clamp(_config.RefreshSeconds, 60, 86400);
            parent.Controls.Add(_refresh);
            AddHint(parent, "建议 300 ~ 1800 秒", fieldX + Px(120f), y + Px(4f), Px(220f));
            y += Px(34f);

            AddLabel(parent, "低余额提醒阈值", labelX, y + Px(3f), labelWidth);
            _threshold.Location = new Point(fieldX, y);
            _threshold.Size = new Size(Px(110f), Px(24f));
            _threshold.DecimalPlaces = 2;
            _threshold.Minimum = 0m;
            _threshold.Maximum = 1000000m;
            _threshold.Increment = 1m;
            _threshold.Value = Clamp(_config.LowBalanceThreshold, 0m, 1000000m);
            parent.Controls.Add(_threshold);
            AddHint(parent, "低于该值时提醒（0 = 关闭）", fieldX + Px(120f), y + Px(4f), Px(260f));
            y += Px(34f);

            AddLabel(parent, "累计消费校准值", labelX, y + Px(3f), labelWidth);
            _calibration.Location = new Point(fieldX, y);
            _calibration.Size = new Size(Px(110f), Px(24f));
            _calibration.DecimalPlaces = 2;
            _calibration.Minimum = 0m;
            _calibration.Maximum = 10000000m;
            _calibration.Increment = 0.01m;
            _calibration.Value = Clamp(_config.SpendCalibration, 0m, 10000000m);
            parent.Controls.Add(_calibration);
            AddHint(parent, "填控制台「累计消费金额」", fieldX + Px(120f), y + Px(4f), Px(260f));
            y += Px(26f);

            string calibrationNote = string.IsNullOrEmpty(_config.SpendCalibrationDate)
                ? "当前为估算模式（尚未校准）"
                : "上次校准：" + _config.SpendCalibrationDate + "（¥" + Renderer.Money(_config.SpendCalibration) + "）";
            AddHint(parent, calibrationNote, labelX, y, fieldWidth + Px(60f));
            y += Px(32f);

            AddLabel(parent, "主题", labelX, y + Px(3f), labelWidth);
            _theme.DropDownStyle = ComboBoxStyle.DropDownList;
            _theme.Location = new Point(fieldX, y);
            _theme.Size = new Size(Px(110f), Px(24f));
            _theme.Items.Add("跟随系统");
            _theme.Items.Add("浅色");
            _theme.Items.Add("深色");
            _theme.SelectedIndex = _config.Theme == "dark" ? 2 : (_config.Theme == "light" ? 1 : 0);
            parent.Controls.Add(_theme);
            y += Px(34f);

            _mini.Text = "迷你模式下只显示总余额";
            _mini.Location = new Point(labelX, y);
            _mini.AutoSize = true;
            _mini.Checked = _config.MiniMode;
            parent.Controls.Add(_mini);
            y += Px(24f);

            _notify.Text = "余额偏低时弹出托盘提醒";
            _notify.Location = new Point(labelX, y);
            _notify.AutoSize = true;
            _notify.Checked = _config.NotifyLowBalance;
            parent.Controls.Add(_notify);
            y += Px(30f);

            _status.Location = new Point(labelX, y);
            _status.Size = new Size(fieldWidth + Px(150f), Px(40f));
            _status.ForeColor = Color.FromArgb(120, 126, 140);
            _status.Text = "提示：官方接口不返回「累计消费金额」，该项为本地估算值，可用上方校准值对齐。";
            parent.Controls.Add(_status);
            y += Px(52f);

            _testButton.Text = "测试连接";
            _testButton.Location = new Point(labelX, y);
            _testButton.Size = new Size(Px(100f), Px(30f));
            _testButton.Click += OnTestClick;
            parent.Controls.Add(_testButton);

            Button openFolder = new Button();
            openFolder.Text = "打开配置目录";
            openFolder.Location = new Point(labelX + Px(110f), y);
            openFolder.Size = new Size(Px(130f), Px(30f));
            openFolder.Click += delegate
            {
                try { System.Diagnostics.Process.Start(Store.Directory); }
                catch (Exception ex) { MessageBox.Show("无法打开目录：" + ex.Message, "提示"); }
            };
            parent.Controls.Add(openFolder);
        }

        // ---------------- 触发显示 ----------------

        private void BuildTriggerTab(TabPage parent)
        {
            int labelX = Px(14f);
            int labelWidth = Px(170f);
            int fieldX = Px(190f);
            int fieldWidth = Px(320f);
            int y = Px(14f);

            AddLabel(parent, "显示方式", labelX, y + Px(3f), labelWidth);
            _showMode.DropDownStyle = ComboBoxStyle.DropDownList;
            _showMode.Location = new Point(fieldX, y);
            _showMode.Size = new Size(Px(120f), Px(24f));
            _showMode.Items.Add("始终显示");
            _showMode.Items.Add("触发时显示");
            _showMode.SelectedIndex = _config.ShowMode == "trigger" ? 1 : 0;
            parent.Controls.Add(_showMode);
            y += Px(26f);

            AddHint(parent, "选「触发时显示」：平时只驻留托盘",
                labelX, y, fieldWidth + Px(60f));
            y += Px(32f);

            AddLabel(parent, "触发后停留（秒）", labelX, y + Px(3f), labelWidth);
            _triggerStay.Location = new Point(fieldX, y);
            _triggerStay.Size = new Size(Px(110f), Px(24f));
            _triggerStay.Minimum = 0;
            _triggerStay.Maximum = 86400;
            _triggerStay.Increment = 10;
            _triggerStay.Value = Clamp(_config.TriggerStaySeconds, 0, 86400);
            parent.Controls.Add(_triggerStay);
            AddHint(parent, "0 = 触发后不再自动隐藏", fieldX + Px(120f), y + Px(4f), Px(216f));
            y += Px(34f);

            AddLabel(parent, "检测间隔（秒）", labelX, y + Px(3f), labelWidth);
            _triggerPoll.Location = new Point(fieldX, y);
            _triggerPoll.Size = new Size(Px(110f), Px(24f));
            _triggerPoll.Minimum = 30;
            _triggerPoll.Maximum = 3600;
            _triggerPoll.Increment = 30;
            _triggerPoll.Value = Clamp(_config.TriggerPollSeconds, 30, 3600);
            parent.Controls.Add(_triggerPoll);
            AddHint(parent, "越小越快发现消费", fieldX + Px(120f), y + Px(4f), Px(216f));
            y += Px(34f);

            AddLabel(parent, "窗口标题关键词", labelX, y + Px(3f), labelWidth);
            _triggerKeywords.Location = new Point(fieldX, y);
            _triggerKeywords.Size = new Size(fieldWidth, Px(24f));
            _triggerKeywords.Text = _config.TriggerTitleKeywords;
            parent.Controls.Add(_triggerKeywords);
            AddHint(parent, "逗号分隔，例如 deepseek,深度求索", fieldX, y + Px(24f), fieldWidth);
            y += Px(54f);

            AddLabel(parent, "触发进程", labelX, y + Px(3f), labelWidth);
            _triggerProcesses.Location = new Point(fieldX, y);
            _triggerProcesses.Size = new Size(fieldWidth, Px(24f));
            _triggerProcesses.Text = _config.TriggerProcesses;
            parent.Controls.Add(_triggerProcesses);
            AddHint(parent, "留空则不启用；例如 Code.exe, python.exe", fieldX, y + Px(24f), fieldWidth);
            y += Px(54f);

            _triggerOnSpend.Text = "检测到自己的 API Key 发生消费后自动弹出";
            _triggerOnSpend.Location = new Point(labelX, y);
            _triggerOnSpend.AutoSize = true;
            _triggerOnSpend.Checked = _config.TriggerOnSpend;
            parent.Controls.Add(_triggerOnSpend);
            y += Px(28f);

            Label note = new Label();
            note.Location = new Point(labelX, y);
            note.Size = new Size(fieldWidth + Px(76f), Px(104f));
            note.ForeColor = Color.FromArgb(120, 126, 140);
            note.Text = "说明：本机无法解密其它程序的 HTTPS 流量，「正在使用 DeepSeek」只能靠上述三类信号近似判断；"
                + "其中「余额下降」是唯一能证明 Key 真被调用的硬信号，通常 1 ~ 5 分钟后才弹出。";
            parent.Controls.Add(note);
        }

        private void BuildButtonRow()
        {
            int y = ClientSize.Height - Px(48f);

            Button save = new Button();
            save.Text = "保存";
            save.Location = new Point(ClientSize.Width - Px(190f), y);
            save.Size = new Size(Px(80f), Px(30f));
            save.Click += OnSaveClick;
            Controls.Add(save);

            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.Location = new Point(ClientSize.Width - Px(100f), y);
            cancel.Size = new Size(Px(80f), Px(30f));
            cancel.DialogResult = DialogResult.Cancel;
            cancel.Click += delegate { Close(); };
            Controls.Add(cancel);

            AcceptButton = save;
            CancelButton = cancel;
        }

        private static decimal Clamp(decimal value, decimal min, decimal max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private void AddLabel(Control parent, string text, int x, int y, int width)
        {
            Label label = new Label();
            label.Text = text;
            label.Location = new Point(x, y);
            label.Size = new Size(width, Px(20f));
            label.TextAlign = ContentAlignment.MiddleLeft;
            parent.Controls.Add(label);
        }

        private void AddHint(Control parent, string text, int x, int y, int width)
        {
            Label label = new Label();
            label.Text = text;
            label.Location = new Point(x, y);
            label.Size = new Size(width, Px(20f));
            label.ForeColor = Color.FromArgb(140, 146, 158);
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.AutoEllipsis = true;
            parent.Controls.Add(label);
        }

        private void OnTestClick(object sender, EventArgs e)
        {
            string key = _apiKey.Text.Trim();
            if (key.Length == 0)
            {
                _status.ForeColor = Color.FromArgb(200, 80, 60);
                _status.Text = "请先填写 API Key。";
                return;
            }

            _testButton.Enabled = false;
            _status.ForeColor = Color.FromArgb(120, 126, 140);
            _status.Text = "正在测试连接…";

            string currency = _config.Currency;
            ThreadPool.QueueUserWorkItem(delegate(object ignored)
            {
                BalanceSnapshot result = DeepSeekApi.Fetch(key, currency, 15000);
                try
                {
                    if (!IsDisposed && IsHandleCreated)
                        BeginInvoke(new MethodInvoker(delegate { ShowTestResult(result); }));
                }
                catch { }
            });
        }

        private void ShowTestResult(BalanceSnapshot result)
        {
            _testButton.Enabled = true;
            if (result.Ok && result.Info != null)
            {
                _status.ForeColor = Color.FromArgb(16, 140, 100);
                _status.Text = "连接成功：" + result.Info.Currency
                    + "  充值余额 ¥" + Renderer.Money(result.Info.ToppedUp)
                    + " · 赠送 ¥" + Renderer.Money(result.Info.Granted)
                    + " · 总余额 ¥" + Renderer.Money(result.Info.Total);
            }
            else
            {
                _status.ForeColor = Color.FromArgb(200, 80, 60);
                _status.Text = "连接失败：" + result.Error;
            }
        }

        private void OnSaveClick(object sender, EventArgs e)
        {
            decimal calibration = _calibration.Value;
            bool recalibrate = Math.Abs(calibration - _config.SpendCalibration) > 0.0001m
                || string.IsNullOrEmpty(_config.SpendCalibrationDate);

            _config.ApiKey = _apiKey.Text.Trim();
            _config.RefreshSeconds = (int)_refresh.Value;
            _config.LowBalanceThreshold = _threshold.Value;
            _config.MiniMode = _mini.Checked;
            _config.NotifyLowBalance = _notify.Checked;
            _config.Theme = _theme.SelectedIndex == 2 ? "dark" : (_theme.SelectedIndex == 1 ? "light" : "auto");

            _config.ShowMode = _showMode.SelectedIndex == 1 ? "trigger" : "always";
            _config.TriggerStaySeconds = (int)_triggerStay.Value;
            _config.TriggerPollSeconds = (int)_triggerPoll.Value;
            _config.TriggerTitleKeywords = _triggerKeywords.Text.Trim();
            _config.TriggerProcesses = _triggerProcesses.Text.Trim();
            _config.TriggerOnSpend = _triggerOnSpend.Checked;

            if (recalibrate)
            {
                SpendTracker.Calibrate(_state, _config, calibration, DateTime.UtcNow);
                // 校准后以当前余额为基线，避免把历史下降量重复计入
                _state.LastTotal = -1m;
            }
            else
            {
                _config.SpendCalibration = calibration;
            }

            Store.SaveConfig(_config);
            Store.SaveState(_state);

            if (SettingsSaved != null) SettingsSaved(this, EventArgs.Empty);
            Close();
        }
    }
}
