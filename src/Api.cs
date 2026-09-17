using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;

namespace DeepSeekPet
{
    internal sealed class BalanceInfo
    {
        public string Currency = "CNY";
        public decimal Total;
        public decimal Granted;
        public decimal ToppedUp;
    }

    internal sealed class BalanceSnapshot
    {
        public bool Ok;
        public bool IsAvailable;
        public BalanceInfo Info;
        public string Error = "";
        public DateTime FetchedAtUtc = DateTime.MinValue;
    }

    /// <summary>
    /// DeepSeek 开放平台余额接口客户端：GET https://api.deepseek.com/user/balance
    /// 该接口只返回「充值余额 / 赠送余额 / 总余额」，不含控制台里的「累计消费金额」。
    /// </summary>
    internal static class DeepSeekApi
    {
        public const string BalanceUrl = "https://api.deepseek.com/user/balance";
        public const string TopUpUrl = "https://platform.deepseek.com/top_up";
        public const string UsageUrl = "https://platform.deepseek.com/usage";

        public static BalanceSnapshot Fetch(string apiKey, string preferredCurrency, int timeoutMs)
        {
            BalanceSnapshot snapshot = new BalanceSnapshot();
            if (string.IsNullOrEmpty(apiKey) || apiKey.Trim().Length == 0)
            {
                snapshot.Error = "尚未填写 API Key";
                return snapshot;
            }

            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(BalanceUrl);
                request.Method = "GET";
                request.Timeout = timeoutMs;
                request.ReadWriteTimeout = timeoutMs;
                request.UserAgent = "DeepSeekPet/1.0";
                request.Accept = "application/json";
                request.Headers["Authorization"] = "Bearer " + apiKey.Trim();

                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream stream = response.GetResponseStream())
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                {
                    string body = reader.ReadToEnd();
                    return ParseBody(body, preferredCurrency, DateTime.UtcNow);
                }
            }
            catch (WebException ex)
            {
                snapshot.Error = DescribeWebError(ex);
                return snapshot;
            }
            catch (Exception ex)
            {
                snapshot.Error = "请求失败：" + ex.Message;
                return snapshot;
            }
        }

        public static BalanceSnapshot ParseBody(string body, string preferredCurrency, DateTime nowUtc)
        {
            BalanceSnapshot snapshot = new BalanceSnapshot();
            try
            {
                Dictionary<string, object> root = Json.ParseObject(body);
                snapshot.IsAvailable = Json.GetBool(root, "is_available", true);

                List<object> infos = Json.GetArray(root, "balance_infos");
                if (infos == null || infos.Count == 0)
                {
                    snapshot.Error = "接口未返回余额信息";
                    return snapshot;
                }

                BalanceInfo chosen = null;
                BalanceInfo first = null;
                for (int i = 0; i < infos.Count; i++)
                {
                    Dictionary<string, object> entry = infos[i] as Dictionary<string, object>;
                    if (entry == null) continue;
                    BalanceInfo info = new BalanceInfo();
                    info.Currency = Json.GetString(entry, "currency", "CNY");
                    info.Total = Json.GetDecimal(entry, "total_balance", 0m);
                    info.Granted = Json.GetDecimal(entry, "granted_balance", 0m);
                    info.ToppedUp = Json.GetDecimal(entry, "topped_up_balance", 0m);
                    if (first == null) first = info;
                    if (string.Equals(info.Currency, preferredCurrency, StringComparison.OrdinalIgnoreCase)) chosen = info;
                }

                if (chosen == null) chosen = first;
                if (chosen == null)
                {
                    snapshot.Error = "接口返回的币种无法识别";
                    return snapshot;
                }

                snapshot.Info = chosen;
                snapshot.Ok = true;
                snapshot.FetchedAtUtc = nowUtc;
                return snapshot;
            }
            catch (Exception ex)
            {
                snapshot.Error = "响应解析失败：" + ex.Message;
                return snapshot;
            }
        }

        private static string DescribeWebError(WebException ex)
        {
            if (ex.Status == WebExceptionStatus.Timeout) return "网络超时，请检查网络后重试";
            if (ex.Status == WebExceptionStatus.NameResolutionFailure) return "无法解析域名，请检查网络";
            if (ex.Status == WebExceptionStatus.ConnectFailure) return "无法连接到 api.deepseek.com";

            HttpWebResponse response = ex.Response as HttpWebResponse;
            if (response != null)
            {
                int code = (int)response.StatusCode;
                if (code == 401 || code == 403) return "API Key 无效或已失效（" + code.ToString(CultureInfo.InvariantCulture) + "）";
                if (code == 429) return "请求过于频繁，请稍后再试（429）";
                if (code >= 500) return "DeepSeek 服务端异常（" + code.ToString(CultureInfo.InvariantCulture) + "）";
                return "请求失败：HTTP " + code.ToString(CultureInfo.InvariantCulture);
            }
            return "网络异常：" + ex.Message;
        }
    }

    /// <summary>
    /// 累计消费估算。
    /// 官方余额接口不提供「累计消费金额」，这里通过两次采样之间的余额下降量累加得到估算值，
    /// 并支持用控制台显示的真实数字做一次校准。注意：赠送额度过期、离线期间充值等情况会带来偏差。
    /// </summary>
    internal static class SpendTracker
    {
        public static void Apply(State state, Config config, BalanceInfo info, DateTime nowUtc)
        {
            if (!state.Initialized)
            {
                state.Initialized = true;
                state.CumulativeSpend = config.SpendCalibration;
                state.Calibrated = !string.IsNullOrEmpty(config.SpendCalibrationDate);
            }

            if (state.LastTotal >= 0m)
            {
                decimal delta = state.LastTotal - info.Total;
                if (delta > 0m) state.CumulativeSpend += delta;   // 余额下降 = 发生消费
                // delta < 0：充值或赠送到账，不计入消费
            }

            state.LastTotal = info.Total;
            state.LastToppedUp = info.ToppedUp;
            state.LastGranted = info.Granted;
            state.LastCurrency = info.Currency;
            state.History.Add(new Sample { T = ToUnixSeconds(nowUtc), Total = info.Total });
            TrimHistory(state, nowUtc);
        }

        public static void Calibrate(State state, Config config, decimal cumulativeSpend, DateTime nowUtc)
        {
            state.CumulativeSpend = cumulativeSpend;
            state.Calibrated = true;
            state.Initialized = true;
            config.SpendCalibration = cumulativeSpend;
            config.SpendCalibrationDate = nowUtc.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        /// <summary>某时间窗内累计的消费估算（相邻采样点正增量之和）。</summary>
        public static decimal SpendInWindow(State state, TimeSpan window, DateTime nowUtc)
        {
            long nowSeconds = ToUnixSeconds(nowUtc);
            long from = nowSeconds - (long)window.TotalSeconds;
            decimal sum = 0m;
            decimal previous = -1m;
            for (int i = 0; i < state.History.Count; i++)
            {
                Sample sample = state.History[i];
                if (previous >= 0m && sample.T >= from)
                {
                    decimal delta = previous - sample.Total;
                    if (delta > 0m) sum += delta;
                }
                if (sample.T >= from) previous = sample.Total;
            }
            return sum;
        }

        public static bool HasWindowData(State state, TimeSpan window, DateTime nowUtc)
        {
            long nowSeconds = ToUnixSeconds(nowUtc);
            long from = nowSeconds - (long)window.TotalSeconds;
            int count = 0;
            for (int i = 0; i < state.History.Count; i++)
            {
                if (state.History[i].T >= from) count++;
            }
            return count >= 2;
        }

        /// <summary>根据最近窗口的消费速度估算余额还能用多少天。</summary>
        public static bool TryEstimateDaysLeft(State state, decimal balance, DateTime nowUtc, out double days)
        {
            days = 0;
            TimeSpan window = TimeSpan.FromHours(24);
            if (!HasWindowData(state, window, nowUtc)) return false;
            decimal spent = SpendInWindow(state, window, nowUtc);
            if (spent <= 0m) return false;
            double perDay = (double)spent;
            if (perDay <= 0) return false;
            days = (double)balance / perDay;
            return days >= 0 && days < 3650;
        }

        private static void TrimHistory(State state, DateTime nowUtc)
        {
            long cutoff = ToUnixSeconds(nowUtc) - 7L * 24L * 3600L;   // 保留 7 天
            int removeCount = 0;
            while (removeCount < state.History.Count && state.History[removeCount].T < cutoff) removeCount++;
            if (removeCount > 0) state.History.RemoveRange(0, removeCount);
            if (state.History.Count > State.MaxHistory)
                state.History.RemoveRange(0, state.History.Count - State.MaxHistory);
        }

        public static long ToUnixSeconds(DateTime utc)
        {
            DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return (long)(utc.ToUniversalTime() - epoch).TotalSeconds;
        }

        public static DateTime FromUnixSeconds(long seconds)
        {
            DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return epoch.AddSeconds(seconds).ToLocalTime();
        }
    }
}
