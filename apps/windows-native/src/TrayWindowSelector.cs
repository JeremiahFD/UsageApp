using System;
using System.Collections.Generic;

namespace UsageApp.Native
{
    internal static class TrayWindowSelector
    {
        internal static UsageWindow Select(
            IEnumerable<UsageWindow> windows,
            string mode)
        {
            UsageWindow selected = null;
            if (windows == null)
            {
                return null;
            }

            foreach (UsageWindow window in windows)
            {
                if (window == null)
                {
                    continue;
                }
                if (selected == null || Better(window, selected, mode))
                {
                    selected = window;
                }
            }
            return selected;
        }

        private static bool Better(
            UsageWindow candidate,
            UsageWindow current,
            string mode)
        {
            if (string.Equals(mode, "ShortestWindow", StringComparison.Ordinal))
            {
                return CompareDuration(candidate, current, true);
            }
            if (string.Equals(mode, "LongestWindow", StringComparison.Ordinal))
            {
                return CompareDuration(candidate, current, false);
            }
            return candidate.RemainingPercent < current.RemainingPercent;
        }

        private static bool CompareDuration(
            UsageWindow candidate,
            UsageWindow current,
            bool shortest)
        {
            bool candidateKnown = candidate.DurationMinutes.HasValue
                && candidate.DurationMinutes.Value > 0;
            bool currentKnown = current.DurationMinutes.HasValue
                && current.DurationMinutes.Value > 0;
            if (candidateKnown != currentKnown)
            {
                return candidateKnown;
            }
            if (!candidateKnown)
            {
                return candidate.RemainingPercent < current.RemainingPercent;
            }
            if (candidate.DurationMinutes.Value == current.DurationMinutes.Value)
            {
                return candidate.RemainingPercent < current.RemainingPercent;
            }
            return shortest
                ? candidate.DurationMinutes.Value < current.DurationMinutes.Value
                : candidate.DurationMinutes.Value > current.DurationMinutes.Value;
        }
    }
}
