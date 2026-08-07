using System;
using System.Collections.Generic;
using System.Globalization;

namespace UsageApp.Native
{
    internal sealed class QuotaNotificationDecision
    {
        public string QuotaLabel { get; set; }
        public int RemainingPercent { get; set; }
        public int ThresholdPercent { get; set; }
    }

    internal sealed class QuotaNotificationEvaluator
    {
        private sealed class WindowObservation
        {
            public int RemainingPercent { get; set; }
            public long ResetIdentity { get; set; }
        }

        private readonly Dictionary<string, WindowObservation> observations =
            new Dictionary<string, WindowObservation>(StringComparer.OrdinalIgnoreCase);
        private bool hasObservedLiveSnapshot;

        public QuotaNotificationDecision Evaluate(
            UsageSnapshot snapshot,
            IEnumerable<int> thresholds)
        {
            if (snapshot == null
                || snapshot.Windows == null
                || snapshot.Windows.Count == 0)
            {
                return null;
            }

            int[] normalizedThresholds = NormalizeThresholds(thresholds);
            Dictionary<string, WindowObservation> next =
                new Dictionary<string, WindowObservation>(
                    StringComparer.OrdinalIgnoreCase);
            QuotaNotificationDecision decision = null;

            foreach (UsageWindow window in snapshot.Windows)
            {
                if (window == null)
                {
                    continue;
                }

                string key = WindowKey(window);
                int remaining = Math.Max(0, Math.Min(100, window.RemainingPercent));
                long resetIdentity = window.ResetsAtUtc.HasValue
                    ? window.ResetsAtUtc.Value.ToUniversalTime().Ticks
                    : long.MinValue;
                WindowObservation previous = null;
                bool comparable = hasObservedLiveSnapshot
                    && observations.TryGetValue(key, out previous)
                    && previous.ResetIdentity == resetIdentity;

                if (comparable)
                {
                    int? crossedThreshold = MostUrgentCrossing(
                        previous.RemainingPercent,
                        remaining,
                        normalizedThresholds);
                    if (crossedThreshold.HasValue)
                    {
                        QuotaNotificationDecision candidate =
                            new QuotaNotificationDecision
                            {
                                QuotaLabel = QuotaLabel(window),
                                RemainingPercent = remaining,
                                ThresholdPercent = crossedThreshold.Value
                            };
                        if (IsMoreUrgent(candidate, decision))
                        {
                            decision = candidate;
                        }
                    }
                }

                next[key] = new WindowObservation
                {
                    RemainingPercent = remaining,
                    ResetIdentity = resetIdentity
                };
            }

            observations.Clear();
            foreach (KeyValuePair<string, WindowObservation> item in next)
            {
                observations[item.Key] = item.Value;
            }
            if (next.Count > 0)
            {
                hasObservedLiveSnapshot = true;
            }
            return decision;
        }

        internal void Reset()
        {
            observations.Clear();
            hasObservedLiveSnapshot = false;
        }

        private static int[] NormalizeThresholds(IEnumerable<int> thresholds)
        {
            List<int> values = new List<int>();
            if (thresholds != null)
            {
                foreach (int threshold in thresholds)
                {
                    if (threshold < 1 || threshold > 99 || values.Contains(threshold))
                    {
                        continue;
                    }
                    values.Add(threshold);
                }
            }
            values.Sort(delegate(int left, int right) { return right.CompareTo(left); });
            return values.ToArray();
        }

        private static int? MostUrgentCrossing(
            int previousRemaining,
            int currentRemaining,
            int[] thresholds)
        {
            int? crossed = null;
            foreach (int threshold in thresholds)
            {
                if (previousRemaining > threshold
                    && currentRemaining <= threshold
                    && (!crossed.HasValue || threshold < crossed.Value))
                {
                    crossed = threshold;
                }
            }
            return crossed;
        }

        private static bool IsMoreUrgent(
            QuotaNotificationDecision candidate,
            QuotaNotificationDecision current)
        {
            if (current == null)
            {
                return true;
            }
            if (candidate.ThresholdPercent != current.ThresholdPercent)
            {
                return candidate.ThresholdPercent < current.ThresholdPercent;
            }
            if (candidate.RemainingPercent != current.RemainingPercent)
            {
                return candidate.RemainingPercent < current.RemainingPercent;
            }
            return string.Compare(
                candidate.QuotaLabel,
                current.QuotaLabel,
                StringComparison.CurrentCultureIgnoreCase) < 0;
        }

        private static string WindowKey(UsageWindow window)
        {
            return string.Join(
                "|",
                new string[]
                {
                    window.LimitId ?? string.Empty,
                    window.LimitName ?? string.Empty,
                    window.Kind ?? string.Empty,
                    window.DurationMinutes.HasValue
                        ? window.DurationMinutes.Value.ToString(CultureInfo.InvariantCulture)
                        : string.Empty
                });
        }

        private static string QuotaLabel(UsageWindow window)
        {
            if (!string.IsNullOrWhiteSpace(window.Label))
            {
                return window.Label.Trim();
            }
            if (!string.IsNullOrWhiteSpace(window.LimitName))
            {
                return window.LimitName.Trim();
            }
            return UsageFormatting.DurationLabel(window.DurationMinutes);
        }
    }
}
