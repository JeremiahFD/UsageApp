using System;
using System.Collections.Generic;
using System.Globalization;
using System.Web.Script.Serialization;

namespace UsageApp.Native
{
    internal sealed class ClaudeQuotaSnapshot
    {
        public ClaudeQuotaSnapshot()
        {
            Windows = new List<UsageWindow>();
        }

        public List<UsageWindow> Windows { get; private set; }
        public DateTime ObservedAtUtc { get; set; }
        public string Status { get; set; }
        public string Message { get; set; }

        public UsageWindow PreferredWindow
        {
            get
            {
                UsageWindow mostConstrained = null;
                foreach (UsageWindow window in Windows)
                {
                    if (window != null
                        && (mostConstrained == null
                            || window.RemainingPercent
                                < mostConstrained.RemainingPercent))
                    {
                        mostConstrained = window;
                    }
                }
                return mostConstrained;
            }
        }
    }

    /// <summary>
    /// Normalizes only the documented Claude Code status-line rate-limit fields.
    /// Session, transcript, cwd, prompts, and credentials are deliberately
    /// ignored and never retained by this type.
    /// </summary>
    internal static class ClaudeStatusLine
    {
        internal const int DefaultStaleAfterMinutes = 15;

        internal static ClaudeQuotaSnapshot Normalize(
            string rawJson,
            DateTime observedAtUtc,
            DateTime nowUtc)
        {
            object parsed = null;
            try
            {
                parsed = new JavaScriptSerializer().DeserializeObject(rawJson);
            }
            catch
            {
                // Treat malformed status input as no quota data. The caller can
                // retain a previously normalized snapshot separately.
            }
            return Normalize(parsed, observedAtUtc, nowUtc);
        }

        internal static ClaudeQuotaSnapshot Normalize(
            object rawValue,
            DateTime observedAtUtc,
            DateTime nowUtc)
        {
            ClaudeQuotaSnapshot snapshot = new ClaudeQuotaSnapshot();
            snapshot.ObservedAtUtc = ToUtc(observedAtUtc);
            Dictionary<string, object> root = AsObject(rawValue);
            Dictionary<string, object> limits = ObjectAt(root, "rate_limits");

            UsageWindow fiveHour = Window(
                ObjectAt(limits, "five_hour"),
                "claude:five-hour",
                "primary",
                "5-hour",
                300);
            if (fiveHour != null)
            {
                snapshot.Windows.Add(fiveHour);
            }
            UsageWindow weekly = Window(
                ObjectAt(limits, "seven_day"),
                "claude:seven-day",
                "secondary",
                "Weekly",
                10080);
            if (weekly != null)
            {
                snapshot.Windows.Add(weekly);
            }

            if (snapshot.Windows.Count == 0)
            {
                snapshot.Status = "unavailable";
                snapshot.Message =
                    "Claude Code did not provide subscription rate-limit data for this session.";
                return snapshot;
            }

            RefreshFreshness(snapshot, nowUtc);
            return snapshot;
        }

        internal static bool RefreshFreshness(
            ClaudeQuotaSnapshot snapshot,
            DateTime nowUtc)
        {
            if (snapshot == null || snapshot.Windows.Count == 0)
            {
                return false;
            }
            string previousStatus = snapshot.Status;
            string previousMessage = snapshot.Message;
            bool resetPassed = false;
            foreach (UsageWindow window in snapshot.Windows)
            {
                if (window.ResetsAtUtc.HasValue
                    && window.ResetsAtUtc.Value <= ToUtc(nowUtc))
                {
                    resetPassed = true;
                    break;
                }
            }
            bool stale = ToUtc(nowUtc) - snapshot.ObservedAtUtc
                > TimeSpan.FromMinutes(DefaultStaleAfterMinutes);
            if (resetPassed || stale)
            {
                snapshot.Status = "stale";
                snapshot.Message = resetPassed
                    ? "Claude's last reported reset time has passed. Run Claude Code to refresh it."
                    : "Showing the last Claude Code status update.";
            }
            else
            {
                snapshot.Status = "live";
                snapshot.Message = null;
            }
            return !string.Equals(
                    previousStatus,
                    snapshot.Status,
                    StringComparison.Ordinal)
                || !string.Equals(
                    previousMessage,
                    snapshot.Message,
                    StringComparison.Ordinal);
        }

        /// <summary>
        /// Applies one normalized status-line observation without allowing an
        /// empty or older callback to replace a newer usable last-known quota.
        /// Callers may persist the returned snapshot only when it contains at
        /// least one window. The method never retains raw status-line input.
        /// </summary>
        internal static ClaudeQuotaSnapshot SelectAcceptedSnapshot(
            ClaudeQuotaSnapshot current,
            ClaudeQuotaSnapshot incoming,
            DateTime nowUtc)
        {
            if (incoming == null)
            {
                RefreshFreshness(current, nowUtc);
                return current;
            }

            bool currentUsable = current != null
                && current.Windows.Count > 0;
            if (currentUsable && incoming.Windows.Count == 0)
            {
                current.Status = "stale";
                current.Message =
                    "Claude's latest status update did not include subscription rate limits. Showing the last known quota.";
                return current;
            }

            if (currentUsable
                && incoming.Windows.Count > 0
                && ToUtc(incoming.ObservedAtUtc)
                    < ToUtc(current.ObservedAtUtc))
            {
                RefreshFreshness(current, nowUtc);
                return current;
            }

            RefreshFreshness(incoming, nowUtc);
            return incoming;
        }

        private static UsageWindow Window(
            Dictionary<string, object> rawWindow,
            string id,
            string kind,
            string label,
            int durationMinutes)
        {
            if (rawWindow == null)
            {
                return null;
            }
            double usedPercent;
            if (!NumberAt(rawWindow, "used_percentage", out usedPercent)
                || usedPercent < 0
                || usedPercent > 100)
            {
                return null;
            }
            return new UsageWindow
            {
                LimitId = id,
                LimitName = "Claude",
                Kind = kind,
                Label = label,
                UsedPercent = usedPercent,
                DurationMinutes = durationMinutes,
                ResetsAtUtc = EpochSecondsAt(rawWindow, "resets_at")
            };
        }

        private static Dictionary<string, object> AsObject(object value)
        {
            return value as Dictionary<string, object>;
        }

        private static Dictionary<string, object> ObjectAt(
            Dictionary<string, object> parent,
            string name)
        {
            if (parent == null || string.IsNullOrEmpty(name))
            {
                return null;
            }
            object value;
            return parent.TryGetValue(name, out value)
                ? AsObject(value)
                : null;
        }

        private static bool NumberAt(
            Dictionary<string, object> parent,
            string name,
            out double value)
        {
            value = 0;
            if (parent == null || string.IsNullOrEmpty(name))
            {
                return false;
            }
            object raw;
            if (!parent.TryGetValue(name, out raw)
                || raw == null
                || raw is bool)
            {
                return false;
            }
            try
            {
                value = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
                return !double.IsNaN(value) && !double.IsInfinity(value);
            }
            catch
            {
                return false;
            }
        }

        private static DateTime? EpochSecondsAt(
            Dictionary<string, object> parent,
            string name)
        {
            double seconds;
            if (!NumberAt(parent, name, out seconds) || seconds <= 0)
            {
                return null;
            }
            try
            {
                return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                    .AddSeconds(seconds);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        private static DateTime ToUtc(DateTime value)
        {
            return value.Kind == DateTimeKind.Utc
                ? value
                : value.ToUniversalTime();
        }
    }
}
