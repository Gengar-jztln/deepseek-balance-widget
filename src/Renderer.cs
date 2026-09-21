using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;

namespace DeepSeekPet
{
    internal sealed class WidgetModel
    {
        public string Title = "DeepSeek 余额";
        public string Currency = "CNY";
        public bool HasData;
        public decimal ToppedUp;
        public decimal Granted;
        public decimal Total;
        public decimal CumulativeSpend;
        public bool Calibrated;
        public bool HasSpendValue;
        public bool HasTrend;
        public decimal Spend24h;
        public bool HasDaysLeft;
        public double DaysLeft;
        public bool ShowSpendPulse;
        public decimal SpendPulse;
        public bool LowBalance;
        public bool IsError;
        public bool IsLoading;
        public bool IsStale;
        public string ErrorText = "";
        public string UpdatedText = "";
        public string RefreshText = "";
        public bool MiniMode;
        public float Spin;      // 0..1 旋转相位
        public float Hover;     // 0..1 悬停强度
    }

    internal sealed class Palette
    {
        public Color Card;
        public Color CardHover;
        public Color Border;
        public Color Shadow;
        public Color PrimaryText;
        public Color SecondaryText;
        public Color TertiaryText;
        public Color Accent;
        public Color AccentSoft;
        public Color Divider;
        public Color Ok;
        public Color Warn;
        public Color Danger;

        public static Palette Light()
        {
            Palette p = new Palette();
            p.Card = Color.FromArgb(255, 252, 253, 255);
            p.CardHover = Color.FromArgb(255, 255, 255, 255);
            p.Border = Color.FromArgb(58, 210, 216, 228);
            p.Shadow = Color.FromArgb(46, 24, 32, 56);
            p.PrimaryText = Color.FromArgb(255, 17, 24, 39);
            p.SecondaryText = Color.FromArgb(255, 95, 105, 122);
            p.TertiaryText = Color.FromArgb(255, 148, 158, 172);
            p.Accent = Color.FromArgb(255, 77, 107, 254);
            p.AccentSoft = Color.FromArgb(28, 77, 107, 254);
            p.Divider = Color.FromArgb(40, 140, 150, 168);
            p.Ok = Color.FromArgb(255, 16, 163, 127);
            p.Warn = Color.FromArgb(255, 233, 152, 26);
            p.Danger = Color.FromArgb(255, 226, 74, 74);
            return p;
        }

        public static Palette Dark()
        {
            Palette p = new Palette();
            p.Card = Color.FromArgb(255, 30, 35, 46);
            p.CardHover = Color.FromArgb(255, 36, 42, 55);
            p.Border = Color.FromArgb(64, 90, 100, 124);
            p.Shadow = Color.FromArgb(80, 0, 0, 0);
            p.PrimaryText = Color.FromArgb(255, 240, 243, 248);
            p.SecondaryText = Color.FromArgb(255, 156, 165, 182);
            p.TertiaryText = Color.FromArgb(255, 116, 126, 145);
            p.Accent = Color.FromArgb(255, 122, 146, 255);
            p.AccentSoft = Color.FromArgb(38, 122, 146, 255);
            p.Divider = Color.FromArgb(46, 140, 152, 180);
            p.Ok = Color.FromArgb(255, 60, 200, 160);
            p.Warn = Color.FromArgb(255, 245, 176, 65);
            p.Danger = Color.FromArgb(255, 248, 113, 113);
            return p;
        }
    }

    /// <summary>卡片渲染器：把状态模型画成一张圆角卡片（含阴影）或一枚迷你胶囊。</summary>
    internal static class Renderer
    {
        public const float Margin = 14f;          // 阴影留白
        public const float CardWidth = 316f;
        public const float MiniWidth = 158f;
        public const float MiniHeight = 46f;
        public const float Radius = 14f;

        private static string _fontFamily;

        private static string FontFamilyName()
        {
            if (_fontFamily != null) return _fontFamily;
            string[] candidates = new string[] { "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI", "SimSun" };
            for (int i = 0; i < candidates.Length; i++)
            {
                try
                {
                    using (FontFamily family = new FontFamily(candidates[i]))
                    {
                        if (family != null) { _fontFamily = candidates[i]; return _fontFamily; }
                    }
                }
                catch { }
            }
            _fontFamily = "SansSerif";
            return _fontFamily;
        }

        public static Font Font(float pixelSize, FontStyle style)
        {
            return new Font(FontFamilyName(), pixelSize, style, GraphicsUnit.Pixel);
        }

        // ---------------- 尺寸计算 ----------------

        /// <summary>返回画布逻辑尺寸（含阴影留白）。</summary>
        public static SizeF Measure(WidgetModel model)
        {
            if (model.MiniMode) return new SizeF(MiniWidth + Margin * 2f, MiniHeight + Margin * 2f);
            return new SizeF(CardWidth + Margin * 2f, CardHeight(model) + Margin * 2f);
        }

        private static float CardHeight(WidgetModel model)
        {
            float y = 13f;                 // 上留白
            y += 20f;                      // 标题行
            y += 12f;                      // 间距
            y += 15f;                      // “充值余额”标签
            y += 40f;                      // 大号金额
            y += 10f;                      // 间距
            y += 1f + 16f;                 // 分隔线 + 间距
            y += 14f + 20f;                // 两列统计
            if (model.HasTrend) y += 8f + 15f;
            if (model.Granted > 0m) y += 2f + 15f;
            if (model.ShowSpendPulse) y += 8f + 15f;
            y += 10f + 15f;                // 底部说明
            y += 13f;                      // 下留白
            return y;
        }

        // ---------------- 绘制入口 ----------------

        public static void Draw(Graphics g, WidgetModel model, Palette palette)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            if (model.MiniMode) DrawMini(g, model, palette);
            else DrawCard(g, model, palette);
        }

        private static void DrawCard(Graphics g, WidgetModel model, Palette palette)
        {
            float width = CardWidth;
            float height = CardHeight(model);
            float left = Margin;
            float top = Margin;
            RectangleF card = new RectangleF(left, top, width, height);

            DrawShadow(g, card, palette, Radius);

            using (GraphicsPath path = RoundedPath(card, Radius))
            using (SolidBrush brush = new SolidBrush(model.Hover > 0.5f ? palette.CardHover : palette.Card))
            using (Pen pen = new Pen(palette.Border, 1f))
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
            }

            float padX = 18f;
            float y = top + 13f;

            // ---- 标题行 ----
            RectangleF iconRect = new RectangleF(left + padX, y + 1f, 18f, 18f);
            DrawWhale(g, iconRect, palette.Accent);

            using (Font titleFont = Font(12.5f, FontStyle.Bold))
            using (SolidBrush textBrush = new SolidBrush(palette.PrimaryText))
            {
                g.DrawString(model.Title, titleFont, textBrush, left + padX + 24f, y + 1f);
            }

            DrawStatus(g, model, palette, new RectangleF(left + width - padX - 140f, y, 140f, 20f));
            y += 20f + 12f;

            // ---- 大号：充值余额 ----
            using (Font labelFont = Font(10.5f, FontStyle.Regular))
            using (SolidBrush labelBrush = new SolidBrush(palette.SecondaryText))
            {
                g.DrawString("充值余额", labelFont, labelBrush, left + padX, y);
            }
            y += 15f;

            Color valueColor = palette.PrimaryText;
            if (model.LowBalance) valueColor = palette.Warn;
            if (model.IsError) valueColor = palette.TertiaryText;

            string primary = model.HasData
                ? "¥" + Money(model.ToppedUp)
                : "—";
            float valueWidth;
            using (Font valueFont = Font(27f, FontStyle.Bold))
            using (SolidBrush valueBrush = new SolidBrush(valueColor))
            {
                SizeF size = g.MeasureString(primary, valueFont);
                g.DrawString(primary, valueFont, valueBrush, left + padX - 1f, y);
                valueWidth = size.Width;
            }
            if (model.HasData)
            {
                using (Font unitFont = Font(11f, FontStyle.Regular))
                using (SolidBrush unitBrush = new SolidBrush(palette.TertiaryText))
                {
                    g.DrawString(model.Currency, unitFont, unitBrush, left + padX + valueWidth, y + 20f);
                }
            }
            y += 40f + 10f;

            // ---- 分隔线 ----
            using (Pen divider = new Pen(palette.Divider, 1f))
            {
                g.DrawLine(divider, left + padX, y, left + width - padX, y);
            }
            y += 1f + 16f;

            // ---- 两列统计 ----
            float colWidth = (width - padX * 2f - 16f) / 2f;
            DrawStat(g, palette, left + padX, y, colWidth,
                "总余额",
                model.HasData ? "¥" + Money(model.Total) : "—",
                model.HasData ? palette.PrimaryText : palette.TertiaryText);

            string spendValue = model.HasSpendValue
                ? "≈¥" + Money(model.CumulativeSpend) + (model.Calibrated ? "" : "*")
                : "—";
            DrawStat(g, palette, left + padX + colWidth + 16f, y, colWidth,
                "累计消费" + (model.Calibrated ? "" : "（未校准）"),
                spendValue,
                model.HasSpendValue ? palette.PrimaryText : palette.TertiaryText);
            y += 14f + 20f;

            // ---- 趋势行 ----
            if (model.HasTrend)
            {
                y += 8f;
                string trend = "近24h 消费 ¥" + Money(model.Spend24h);
                if (model.HasDaysLeft)
                    trend += " · 预计可用 " + Days(model.DaysLeft) + " 天";
                using (Font trendFont = Font(10.5f, FontStyle.Regular))
                using (SolidBrush trendBrush = new SolidBrush(palette.SecondaryText))
                {
                    g.DrawString(trend, trendFont, trendBrush, left + padX, y);
                }
                y += 15f;
            }

            if (model.Granted > 0m)
            {
                y += 2f;
                using (Font grantFont = Font(10.5f, FontStyle.Regular))
                using (SolidBrush grantBrush = new SolidBrush(palette.SecondaryText))
                {
                    g.DrawString("其中赠送余额 ¥" + Money(model.Granted), grantFont, grantBrush, left + padX, y);
                }
                y += 15f;
            }

            if (model.ShowSpendPulse)
            {
                y += 8f;
                using (Font pulseFont = Font(10.5f, FontStyle.Bold))
                using (SolidBrush pulseBrush = new SolidBrush(palette.Accent))
                {
                    g.DrawString("本次检测到消费 ¥" + Money(model.SpendPulse), pulseFont, pulseBrush, left + padX, y);
                }
                y += 15f;
            }

            // ---- 底部说明 / 错误 ----
            y += 10f;
            string footer;
            Color footerColor;
            if (model.IsError)
            {
                footer = model.ErrorText;
                footerColor = palette.Danger;
            }
            else if (model.IsLoading && !model.HasData)
            {
                footer = "正在获取余额…";
                footerColor = palette.SecondaryText;
            }
            else
            {
                footer = model.UpdatedText;
                if (!string.IsNullOrEmpty(model.RefreshText))
                    footer += " · " + model.RefreshText;
                footerColor = model.IsStale ? palette.Warn : palette.TertiaryText;
            }
            using (Font footerFont = Font(10.5f, FontStyle.Regular))
            using (SolidBrush footerBrush = new SolidBrush(footerColor))
            using (StringFormat format = new StringFormat())
            {
                format.Trimming = StringTrimming.EllipsisCharacter;
                format.FormatFlags = StringFormatFlags.NoWrap;
                g.DrawString(footer, footerFont, footerBrush,
                    new RectangleF(left + padX, y, width - padX * 2f, 15f), format);
            }
        }

        private static void DrawMini(Graphics g, WidgetModel model, Palette palette)
        {
            RectangleF card = new RectangleF(Margin, Margin, MiniWidth, MiniHeight);
            DrawShadow(g, card, palette, MiniHeight / 2f);

            using (GraphicsPath path = RoundedPath(card, MiniHeight / 2f))
            using (SolidBrush brush = new SolidBrush(model.Hover > 0.5f ? palette.CardHover : palette.Card))
            using (Pen pen = new Pen(palette.Border, 1f))
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
            }

            RectangleF iconRect = new RectangleF(card.X + 13f, card.Y + 12f, 22f, 22f);
            DrawWhale(g, iconRect, palette.Accent);

            Color valueColor = palette.PrimaryText;
            if (model.LowBalance) valueColor = palette.Warn;
            if (model.IsError) valueColor = palette.TertiaryText;

            string text = model.HasData ? "¥" + Money(model.Total) : "—";
            using (Font valueFont = Font(16f, FontStyle.Bold))
            using (SolidBrush valueBrush = new SolidBrush(valueColor))
            using (StringFormat format = new StringFormat())
            {
                format.LineAlignment = StringAlignment.Center;
                format.Trimming = StringTrimming.EllipsisCharacter;
                format.FormatFlags = StringFormatFlags.NoWrap;
                g.DrawString(text, valueFont, valueBrush, new RectangleF(card.X + 41f, card.Y, 96f, card.Height), format);
            }

            DrawStatusDot(g, palette, StatusColor(model, palette), card.Right - 16f, card.Y + card.Height / 2f, 4.5f);
        }

        private static void DrawStat(Graphics g, Palette palette, float x, float y, float width, string label, string value, Color valueColor)
        {
            using (Font labelFont = Font(10.5f, FontStyle.Regular))
            using (SolidBrush labelBrush = new SolidBrush(palette.SecondaryText))
            using (StringFormat format = new StringFormat())
            {
                format.Trimming = StringTrimming.EllipsisCharacter;
                format.FormatFlags = StringFormatFlags.NoWrap;
                g.DrawString(label, labelFont, labelBrush, new RectangleF(x, y, width, 14f), format);
            }
            using (Font valueFont = Font(14f, FontStyle.Bold))
            using (SolidBrush valueBrush = new SolidBrush(valueColor))
            using (StringFormat format = new StringFormat())
            {
                format.Trimming = StringTrimming.EllipsisCharacter;
                format.FormatFlags = StringFormatFlags.NoWrap;
                g.DrawString(value, valueFont, valueBrush, new RectangleF(x, y + 14f, width, 20f), format);
            }
        }

        private static Color StatusColor(WidgetModel model, Palette palette)
        {
            if (model.IsError) return palette.Danger;
            if (model.LowBalance) return palette.Warn;
            if (model.IsStale) return palette.Warn;
            return palette.Ok;
        }

        private static void DrawStatus(Graphics g, WidgetModel model, Palette palette, RectangleF area)
        {
            string text;
            if (model.IsLoading) text = model.HasData ? "刷新中" : "获取中";
            else if (model.IsError) text = "异常";
            else if (model.LowBalance) text = "余额偏低";
            else if (model.IsStale) text = "数据过期";
            else if (model.HasData) text = "正常";
            else text = "待配置";

            using (Font statusFont = Font(10.5f, FontStyle.Regular))
            using (SolidBrush statusBrush = new SolidBrush(palette.SecondaryText))
            using (StringFormat format = new StringFormat())
            {
                format.Alignment = StringAlignment.Far;
                format.LineAlignment = StringAlignment.Center;
                g.DrawString(text, statusFont, statusBrush, new RectangleF(area.X, area.Y, area.Width - 16f, area.Height), format);
            }

            float cx = area.Right - 6f;
            float cy = area.Y + area.Height / 2f;
            if (model.IsLoading)
            {
                DrawSpinner(g, palette, cx, cy, 6f, model.Spin);
            }
            else
            {
                DrawStatusDot(g, palette, StatusColor(model, palette), cx, cy, 4.5f);
            }
        }

        private static void DrawStatusDot(Graphics g, Palette palette, Color color, float cx, float cy, float radius)
        {
            using (SolidBrush glow = new SolidBrush(Color.FromArgb(48, color)))
            {
                g.FillEllipse(glow, cx - radius * 1.9f, cy - radius * 1.9f, radius * 3.8f, radius * 3.8f);
            }
            using (SolidBrush brush = new SolidBrush(color))
            {
                g.FillEllipse(brush, cx - radius, cy - radius, radius * 2f, radius * 2f);
            }
        }

        private static void DrawSpinner(Graphics g, Palette palette, float cx, float cy, float radius, float phase)
        {
            using (Pen track = new Pen(Color.FromArgb(48, palette.Accent), 2f))
            {
                track.StartCap = LineCap.Round;
                track.EndCap = LineCap.Round;
                g.DrawEllipse(track, cx - radius, cy - radius, radius * 2f, radius * 2f);
            }
            using (Pen arc = new Pen(palette.Accent, 2f))
            {
                arc.StartCap = LineCap.Round;
                arc.EndCap = LineCap.Round;
                g.DrawArc(arc, cx - radius, cy - radius, radius * 2f, radius * 2f, phase * 360f, 110f);
            }
        }

        private static void DrawShadow(Graphics g, RectangleF card, Palette palette, float radius)
        {
            for (int i = 6; i >= 1; i--)
            {
                float spread = i * 1.6f;
                int alpha = (int)(palette.Shadow.A * (0.16f / i));
                if (alpha < 1) continue;
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(alpha, palette.Shadow)))
                using (GraphicsPath path = RoundedPath(new RectangleF(
                    card.X - spread * 0.5f, card.Y - spread * 0.25f + i * 0.9f,
                    card.Width + spread, card.Height + spread * 1.2f), radius + spread * 0.4f))
                {
                    g.FillPath(brush, path);
                }
            }
        }

        /// <summary>DeepSeek 鲸鱼风格的标识图形（纯矢量绘制，不含任何商标文字）。</summary>
        public static void DrawWhaleIcon(Graphics g, RectangleF box, Color color)
        {
            DrawWhale(g, box, color);
        }

        private static void DrawWhale(Graphics g, RectangleF box, Color color)
        {
            float w = box.Width;
            float h = box.Height;
            float bx = box.X;
            float by = box.Y;

            using (SolidBrush brush = new SolidBrush(color))
            using (GraphicsPath path = new GraphicsPath())
            {
                // 身体
                path.AddBezier(
                    bx + 0.06f * w, by + 0.62f * h,
                    bx + 0.10f * w, by + 0.30f * h,
                    bx + 0.38f * w, by + 0.18f * h,
                    bx + 0.58f * w, by + 0.30f * h);
                path.AddBezier(
                    bx + 0.58f * w, by + 0.30f * h,
                    bx + 0.74f * w, by + 0.38f * h,
                    bx + 0.80f * w, by + 0.52f * h,
                    bx + 0.82f * w, by + 0.62f * h);
                // 尾鳍
                path.AddBezier(
                    bx + 0.82f * w, by + 0.62f * h,
                    bx + 0.90f * w, by + 0.36f * h,
                    bx + 0.97f * w, by + 0.30f * h,
                    bx + 0.99f * w, by + 0.36f * h);
                path.AddBezier(
                    bx + 0.99f * w, by + 0.36f * h,
                    bx + 0.94f * w, by + 0.62f * h,
                    bx + 0.90f * w, by + 0.74f * h,
                    bx + 0.86f * w, by + 0.80f * h);
                path.AddBezier(
                    bx + 0.86f * w, by + 0.80f * h,
                    bx + 0.62f * w, by + 0.94f * h,
                    bx + 0.28f * w, by + 0.90f * h,
                    bx + 0.06f * w, by + 0.62f * h);
                path.CloseFigure();
                g.FillPath(brush, path);
            }

            // 眼睛：白色小点，保证在实色鲸身上可见
            using (SolidBrush eye = new SolidBrush(Color.FromArgb(235, 255, 255, 255)))
            {
                g.FillEllipse(eye, bx + 0.22f * w, by + 0.46f * h, 0.16f * w, 0.16f * w);
            }
        }

        private static GraphicsPath RoundedPath(RectangleF rect, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float d = Math.Min(radius * 2f, Math.Min(rect.Width, rect.Height));
            if (d <= 0.5f)
            {
                path.AddRectangle(rect);
                return path;
            }
            path.AddArc(rect.X, rect.Y, d, d, 180f, 90f);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270f, 90f);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0f, 90f);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90f, 90f);
            path.CloseFigure();
            return path;
        }

        public static string Money(decimal value)
        {
            return value.ToString("0.00", CultureInfo.InvariantCulture);
        }

        private static string Days(double days)
        {
            if (days >= 100) return ">100";
            return days.ToString(days < 10 ? "0.0" : "0", CultureInfo.InvariantCulture);
        }
    }
}
