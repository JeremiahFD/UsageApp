using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace UsageApp.Native
{
    /// <summary>
    /// Durable cache of normalized Claude quota only. No status-line payload,
    /// command text, prompt, session, or credential is written to disk.
    /// </summary>
    internal sealed class ClaudeSnapshotStore
    {
        private readonly string snapshotPath;

        internal ClaudeSnapshotStore()
        {
            string root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "UsageAppNative");
            snapshotPath = Path.Combine(root, "last-known-claude-quota.json");
        }

        internal ClaudeQuotaSnapshot Load(DateTime nowUtc)
        {
            try
            {
                if (!File.Exists(snapshotPath)) return null;
                return Deserialize(File.ReadAllText(snapshotPath, Encoding.UTF8), nowUtc);
            }
            catch
            {
                return null;
            }
        }

        internal void Save(ClaudeQuotaSnapshot snapshot)
        {
            if (snapshot == null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath));
                string temporary = snapshotPath + ".tmp";
                File.WriteAllText(temporary, Serialize(snapshot), Encoding.UTF8);
                if (File.Exists(snapshotPath)) File.Replace(temporary, snapshotPath, null);
                else File.Move(temporary, snapshotPath);
            }
            catch
            {
                // Last-known quota is a convenience. Collection continues if
                // the user profile cannot accept a cache update.
            }
        }

        internal static string Serialize(ClaudeQuotaSnapshot snapshot)
        {
            Dictionary<string, object> root = new Dictionary<string, object>();
            root["schemaVersion"] = 1;
            root["observedAtUtc"] = snapshot.ObservedAtUtc.ToUniversalTime()
                .ToString("o", CultureInfo.InvariantCulture);
            List<object> windows = new List<object>();
            foreach (UsageWindow window in snapshot.Windows)
            {
                Dictionary<string, object> value = new Dictionary<string, object>();
                value["limitId"] = window.LimitId;
                value["kind"] = window.Kind;
                value["label"] = window.Label;
                value["usedPercent"] = window.UsedPercent;
                value["durationMinutes"] = window.DurationMinutes;
                value["resetsAtUtc"] = window.ResetsAtUtc.HasValue
                    ? window.ResetsAtUtc.Value.ToUniversalTime().ToString(
                        "o", CultureInfo.InvariantCulture)
                    : null;
                windows.Add(value);
            }
            root["windows"] = windows;
            return new JavaScriptSerializer().Serialize(root);
        }

        internal static ClaudeQuotaSnapshot Deserialize(string text, DateTime nowUtc)
        {
            IDictionary<string, object> root = new JavaScriptSerializer()
                .DeserializeObject(text) as IDictionary<string, object>;
            if (root == null || Integer(Value(root, "schemaVersion")) != 1) return null;
            DateTime? observed = DateValue(Value(root, "observedAtUtc"));
            if (!observed.HasValue) return null;

            ClaudeQuotaSnapshot snapshot = new ClaudeQuotaSnapshot();
            snapshot.ObservedAtUtc = observed.Value;
            IEnumerable rawWindows = Value(root, "windows") as IEnumerable;
            if (rawWindows != null)
            {
                foreach (object raw in rawWindows)
                {
                    IDictionary<string, object> value = raw as IDictionary<string, object>;
                    double? used = Number(Value(value, "usedPercent"));
                    string id = Value(value, "limitId") as string;
                    string label = Value(value, "label") as string;
                    if (value == null || !used.HasValue || used.Value < 0 || used.Value > 100
                        || string.IsNullOrEmpty(id) || string.IsNullOrEmpty(label)) continue;
                    snapshot.Windows.Add(new UsageWindow
                    {
                        LimitId = id,
                        LimitName = "Claude",
                        Kind = Value(value, "kind") as string,
                        Label = label,
                        UsedPercent = used.Value,
                        DurationMinutes = IntegerNullable(Value(value, "durationMinutes")),
                        ResetsAtUtc = DateValue(Value(value, "resetsAtUtc"))
                    });
                }
            }
            if (snapshot.Windows.Count == 0) return null;

            bool resetPassed = false;
            foreach (UsageWindow window in snapshot.Windows)
            {
                if (window.ResetsAtUtc.HasValue && window.ResetsAtUtc.Value <= nowUtc.ToUniversalTime())
                {
                    resetPassed = true;
                    break;
                }
            }
            bool stale = nowUtc.ToUniversalTime() - snapshot.ObservedAtUtc
                > TimeSpan.FromMinutes(ClaudeStatusLine.DefaultStaleAfterMinutes);
            snapshot.Status = stale || resetPassed ? "stale" : "live";
            snapshot.Message = snapshot.Status == "stale"
                ? "Showing the last Claude Code status update."
                : null;
            return snapshot;
        }

        private static object Value(IDictionary<string, object> values, string name)
        {
            if (values == null) return null;
            object value;
            return values.TryGetValue(name, out value) ? value : null;
        }

        private static double? Number(object value)
        {
            if (value == null || value is bool) return null;
            try { return Convert.ToDouble(value, CultureInfo.InvariantCulture); }
            catch { return null; }
        }

        private static int Integer(object value)
        {
            try { return Convert.ToInt32(value, CultureInfo.InvariantCulture); }
            catch { return int.MinValue; }
        }

        private static int? IntegerNullable(object value)
        {
            double? number = Number(value);
            return number.HasValue && number.Value >= 0 && number.Value <= Int32.MaxValue
                ? (int?)Math.Truncate(number.Value)
                : null;
        }

        private static DateTime? DateValue(object value)
        {
            string text = value as string;
            DateTime parsed;
            return !string.IsNullOrEmpty(text)
                && DateTime.TryParse(text, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                    out parsed)
                ? (DateTime?)parsed.ToUniversalTime()
                : null;
        }
    }
}
