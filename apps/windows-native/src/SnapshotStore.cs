using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace UsageApp.Native
{
    internal sealed class SnapshotStore
    {
        private readonly string snapshotPath;

        public SnapshotStore()
        {
            string root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "UsageAppNative");
            snapshotPath = Path.Combine(root, "last-known-snapshot.json");
        }

        public UsageSnapshot Load()
        {
            try
            {
                if (!File.Exists(snapshotPath))
                {
                    return null;
                }
                return Deserialize(File.ReadAllText(snapshotPath, Encoding.UTF8));
            }
            catch
            {
                return null;
            }
        }

        public void Save(UsageSnapshot snapshot)
        {
            try
            {
                string directory = Path.GetDirectoryName(snapshotPath);
                Directory.CreateDirectory(directory);
                string temporary = snapshotPath + ".tmp";
                File.WriteAllText(temporary, Serialize(snapshot), Encoding.UTF8);
                if (File.Exists(snapshotPath))
                {
                    File.Replace(temporary, snapshotPath, null);
                }
                else
                {
                    File.Move(temporary, snapshotPath);
                }
            }
            catch
            {
                // Persistence is a convenience. A read-only live monitor must
                // continue working if the local cache cannot be written.
            }
        }

        internal static UsageSnapshot Merge(
            UsageSnapshot current,
            UsageSnapshot cached,
            bool includeCachedTokenUsage = true,
            bool includeCachedQuota = true)
        {
            if (current == null)
            {
                return cached;
            }
            if (cached == null)
            {
                return current;
            }

            if (includeCachedQuota
                && !current.BankedResets.AvailableCount.HasValue
                && cached.BankedResets.AvailableCount.HasValue)
            {
                current.BankedResets.AvailableCount =
                    cached.BankedResets.AvailableCount;
                current.BankedResets.CountObservedAtUtc =
                    cached.BankedResets.CountObservedAtUtc;
            }

            bool authoritativeZero = current.BankedResets.AvailableCount.HasValue
                && current.BankedResets.AvailableCount.Value == 0;
            if (includeCachedQuota
                && !authoritativeZero
                && !current.BankedResets.DetailsAvailable
                && cached.BankedResets.DetailsAvailable)
            {
                current.BankedResets.DetailsAvailable = true;
                current.BankedResets.DetailsObservedAtUtc =
                    cached.BankedResets.DetailsObservedAtUtc;
                foreach (BankedReset item in cached.BankedResets.Items)
                {
                    current.BankedResets.Items.Add(Clone(item));
                }
            }
            if (includeCachedTokenUsage
                && current.TokenUsage == null
                && cached.TokenUsage != null)
            {
                current.TokenUsage = Clone(cached.TokenUsage);
                current.TokenUsageObservedAtUtc =
                    cached.TokenUsageObservedAtUtc;
            }
            return current;
        }

        internal static string Serialize(UsageSnapshot snapshot)
        {
            Dictionary<string, object> root = new Dictionary<string, object>();
            root["schemaVersion"] = 1;
            root["observedAtUtc"] = snapshot.ObservedAtUtc.ToString(
                "o",
                CultureInfo.InvariantCulture);
            root["planType"] = snapshot.PlanType;
            root["tokenUsageObservedAtUtc"] =
                DateText(snapshot.TokenUsageObservedAtUtc);

            List<object> windows = new List<object>();
            foreach (UsageWindow window in snapshot.Windows)
            {
                Dictionary<string, object> value = new Dictionary<string, object>();
                value["limitId"] = window.LimitId;
                value["limitName"] = window.LimitName;
                value["kind"] = window.Kind;
                value["label"] = window.Label;
                value["usedPercent"] = window.UsedPercent;
                value["durationMinutes"] = window.DurationMinutes;
                value["resetsAtUtc"] = DateText(window.ResetsAtUtc);
                windows.Add(value);
            }
            root["windows"] = windows;

            Dictionary<string, object> banked = new Dictionary<string, object>();
            banked["availableCount"] = snapshot.BankedResets.AvailableCount;
            banked["detailsAvailable"] = snapshot.BankedResets.DetailsAvailable;
            banked["countObservedAtUtc"] =
                DateText(snapshot.BankedResets.CountObservedAtUtc);
            banked["detailsObservedAtUtc"] =
                DateText(snapshot.BankedResets.DetailsObservedAtUtc);

            List<object> items = new List<object>();
            foreach (BankedReset item in snapshot.BankedResets.Items)
            {
                Dictionary<string, object> value = new Dictionary<string, object>();
                value["id"] = item.Id;
                value["title"] = item.Title;
                value["description"] = item.Description;
                value["status"] = item.Status;
                value["grantedAtUtc"] = item.GrantedAtUtc.ToString(
                    "o",
                    CultureInfo.InvariantCulture);
                value["expiresAtUtc"] = DateText(item.ExpiresAtUtc);
                items.Add(value);
            }
            banked["items"] = items;
            root["bankedResets"] = banked;

            if (snapshot.TokenUsage != null)
            {
                Dictionary<string, object> tokenUsage =
                    new Dictionary<string, object>();
                tokenUsage["lifetimeTokens"] =
                    snapshot.TokenUsage.LifetimeTokens;
                tokenUsage["peakDailyTokens"] =
                    snapshot.TokenUsage.PeakDailyTokens;
                tokenUsage["longestRunningTurnSeconds"] =
                    snapshot.TokenUsage.LongestRunningTurnSeconds;
                tokenUsage["currentStreakDays"] =
                    snapshot.TokenUsage.CurrentStreakDays;
                tokenUsage["longestStreakDays"] =
                    snapshot.TokenUsage.LongestStreakDays;
                List<object> dailyBuckets = new List<object>();
                foreach (TokenUsageDailyBucket bucket in
                    snapshot.TokenUsage.DailyBuckets)
                {
                    Dictionary<string, object> value =
                        new Dictionary<string, object>();
                    value["startDate"] = bucket.StartDate;
                    value["tokens"] = bucket.Tokens;
                    dailyBuckets.Add(value);
                }
                tokenUsage["dailyBuckets"] = dailyBuckets;
                root["tokenUsage"] = tokenUsage;
            }
            else
            {
                root["tokenUsage"] = null;
            }

            JavaScriptSerializer json = new JavaScriptSerializer();
            return json.Serialize(root);
        }

        internal static UsageSnapshot Deserialize(string text)
        {
            JavaScriptSerializer json = new JavaScriptSerializer();
            IDictionary<string, object> root =
                json.DeserializeObject(text) as IDictionary<string, object>;
            if (root == null || Integer(Get(root, "schemaVersion")) != 1)
            {
                return null;
            }

            DateTime? observed = DateValue(Get(root, "observedAtUtc"));
            if (!observed.HasValue)
            {
                return null;
            }

            UsageSnapshot snapshot = new UsageSnapshot();
            snapshot.ObservedAtUtc = observed.Value;
            snapshot.PlanType = Get(root, "planType") as string;
            snapshot.TokenUsageObservedAtUtc =
                DateValue(Get(root, "tokenUsageObservedAtUtc"));

            IEnumerable windows = Get(root, "windows") as IEnumerable;
            if (windows != null)
            {
                foreach (object rawWindow in windows)
                {
                    IDictionary<string, object> value =
                        rawWindow as IDictionary<string, object>;
                    double? used = Number(Get(value, "usedPercent"));
                    if (value == null || !used.HasValue)
                    {
                        continue;
                    }
                    UsageWindow window = new UsageWindow();
                    window.LimitId = Get(value, "limitId") as string;
                    window.LimitName = Get(value, "limitName") as string;
                    window.Kind = Get(value, "kind") as string;
                    window.Label = Get(value, "label") as string;
                    window.UsedPercent = used.Value;
                    double? duration = Number(Get(value, "durationMinutes"));
                    window.DurationMinutes = duration.HasValue
                        ? (int?)Math.Truncate(duration.Value)
                        : null;
                    window.ResetsAtUtc = DateValue(Get(value, "resetsAtUtc"));
                    snapshot.Windows.Add(window);
                }
            }

            IDictionary<string, object> banked =
                Get(root, "bankedResets") as IDictionary<string, object>;
            if (banked != null)
            {
                double? count = Number(Get(banked, "availableCount"));
                snapshot.BankedResets.AvailableCount = count.HasValue
                    ? (int?)Math.Truncate(count.Value)
                    : null;
                snapshot.BankedResets.DetailsAvailable =
                    Boolean(Get(banked, "detailsAvailable"));
                snapshot.BankedResets.CountObservedAtUtc =
                    DateValue(Get(banked, "countObservedAtUtc"));
                snapshot.BankedResets.DetailsObservedAtUtc =
                    DateValue(Get(banked, "detailsObservedAtUtc"));

                IEnumerable items = Get(banked, "items") as IEnumerable;
                if (items != null)
                {
                    foreach (object rawItem in items)
                    {
                        IDictionary<string, object> value =
                            rawItem as IDictionary<string, object>;
                        DateTime? granted = DateValue(Get(value, "grantedAtUtc"));
                        string id = Get(value, "id") as string;
                        if (value == null || !granted.HasValue || string.IsNullOrEmpty(id))
                        {
                            continue;
                        }
                        BankedReset item = new BankedReset();
                        item.Id = id;
                        item.Title = Get(value, "title") as string;
                        item.Description = Get(value, "description") as string;
                        item.Status = Get(value, "status") as string;
                        item.GrantedAtUtc = granted.Value;
                        item.ExpiresAtUtc = DateValue(Get(value, "expiresAtUtc"));
                        snapshot.BankedResets.Items.Add(item);
                    }
                }
            }

            IDictionary<string, object> rawTokenUsage =
                Get(root, "tokenUsage") as IDictionary<string, object>;
            if (rawTokenUsage != null)
            {
                TokenUsageSummary tokenUsage = new TokenUsageSummary();
                tokenUsage.LifetimeTokens =
                    WholeNumber(Get(rawTokenUsage, "lifetimeTokens"));
                tokenUsage.PeakDailyTokens =
                    WholeNumber(Get(rawTokenUsage, "peakDailyTokens"));
                tokenUsage.LongestRunningTurnSeconds =
                    WholeNumber(Get(rawTokenUsage, "longestRunningTurnSeconds"));
                tokenUsage.CurrentStreakDays =
                    WholeNumber(Get(rawTokenUsage, "currentStreakDays"));
                tokenUsage.LongestStreakDays =
                    WholeNumber(Get(rawTokenUsage, "longestStreakDays"));
                IEnumerable dailyBuckets =
                    Get(rawTokenUsage, "dailyBuckets") as IEnumerable;
                if (dailyBuckets != null)
                {
                    Dictionary<string, long> byDate =
                        new Dictionary<string, long>(StringComparer.Ordinal);
                    foreach (object rawBucket in dailyBuckets)
                    {
                        IDictionary<string, object> value =
                            rawBucket as IDictionary<string, object>;
                        string startDate = Get(value, "startDate") as string;
                        long? tokens = WholeNumber(Get(value, "tokens"));
                        if (!IsDateKey(startDate)
                            || !tokens.HasValue
                            || tokens.Value < 0)
                        {
                            continue;
                        }
                        byDate[startDate] = tokens.Value;
                    }
                    foreach (KeyValuePair<string, long> entry in byDate)
                    {
                        tokenUsage.DailyBuckets.Add(
                            new TokenUsageDailyBucket
                            {
                                StartDate = entry.Key,
                                Tokens = entry.Value
                            });
                    }
                    tokenUsage.DailyBuckets.Sort(
                        delegate(
                            TokenUsageDailyBucket left,
                            TokenUsageDailyBucket right)
                        {
                            return string.CompareOrdinal(
                                left.StartDate,
                                right.StartDate);
                        });
                }
                snapshot.TokenUsage = tokenUsage;
            }
            return snapshot;
        }

        private static BankedReset Clone(BankedReset source)
        {
            BankedReset clone = new BankedReset();
            clone.Id = source.Id;
            clone.Title = source.Title;
            clone.Description = source.Description;
            clone.Status = source.Status;
            clone.GrantedAtUtc = source.GrantedAtUtc;
            clone.ExpiresAtUtc = source.ExpiresAtUtc;
            return clone;
        }

        private static TokenUsageSummary Clone(TokenUsageSummary source)
        {
            TokenUsageSummary clone = new TokenUsageSummary();
            clone.LifetimeTokens = source.LifetimeTokens;
            clone.PeakDailyTokens = source.PeakDailyTokens;
            clone.LongestRunningTurnSeconds =
                source.LongestRunningTurnSeconds;
            clone.CurrentStreakDays = source.CurrentStreakDays;
            clone.LongestStreakDays = source.LongestStreakDays;
            foreach (TokenUsageDailyBucket bucket in source.DailyBuckets)
            {
                clone.DailyBuckets.Add(new TokenUsageDailyBucket
                {
                    StartDate = bucket.StartDate,
                    Tokens = bucket.Tokens
                });
            }
            return clone;
        }

        private static bool IsDateKey(string value)
        {
            DateTime parsed;
            return !string.IsNullOrEmpty(value)
                && value.Length == 10
                && DateTime.TryParseExact(
                    value,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out parsed);
        }

        private static string DateText(DateTime? value)
        {
            return value.HasValue
                ? value.Value.ToUniversalTime().ToString(
                    "o",
                    CultureInfo.InvariantCulture)
                : null;
        }

        private static DateTime? DateValue(object value)
        {
            string text = value as string;
            DateTime parsed;
            if (string.IsNullOrEmpty(text)
                || !DateTime.TryParse(
                    text,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                    out parsed))
            {
                return null;
            }
            return parsed.ToUniversalTime();
        }

        private static object Get(IDictionary<string, object> value, string key)
        {
            if (value == null)
            {
                return null;
            }
            object result;
            return value.TryGetValue(key, out result) ? result : null;
        }

        private static double? Number(object value)
        {
            if (value == null || value is bool)
            {
                return null;
            }
            try
            {
                return Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return null;
            }
        }

        private static int Integer(object value)
        {
            try
            {
                return Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return int.MinValue;
            }
        }

        private static long? WholeNumber(object value)
        {
            if (value == null || value is bool)
            {
                return null;
            }
            try
            {
                decimal number = Convert.ToDecimal(
                    value,
                    CultureInfo.InvariantCulture);
                if (number < 0)
                {
                    return null;
                }
                return Convert.ToInt64(decimal.Truncate(number));
            }
            catch
            {
                return null;
            }
        }

        private static bool Boolean(object value)
        {
            return value is bool && (bool)value;
        }
    }
}
