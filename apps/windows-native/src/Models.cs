using System;
using System.Collections.Generic;
using System.Globalization;

namespace UsageApp.Native
{
    internal sealed class UsageWindow
    {
        public string LimitId { get; set; }
        public string LimitName { get; set; }
        public string Kind { get; set; }
        public string Label { get; set; }
        public double UsedPercent { get; set; }
        public int? DurationMinutes { get; set; }
        public DateTime? ResetsAtUtc { get; set; }

        public int RemainingPercent
        {
            get
            {
                double remaining = Math.Max(0, Math.Min(100, 100 - UsedPercent));
                return (int)Math.Round(remaining, MidpointRounding.AwayFromZero);
            }
        }
    }

    internal sealed class UsageSnapshot
    {
        public UsageSnapshot()
        {
            Windows = new List<UsageWindow>();
            BankedResets = new BankedResetSummary();
        }

        public List<UsageWindow> Windows { get; private set; }
        public BankedResetSummary BankedResets { get; private set; }
        public TokenUsageSummary TokenUsage { get; set; }
        public DateTime? TokenUsageObservedAtUtc { get; set; }
        public string PlanType { get; set; }
        public DateTime ObservedAtUtc { get; set; }

        public UsageWindow PreferredWindow
        {
            get
            {
                UsageWindow match = null;
                foreach (UsageWindow window in Windows)
                {
                    if (match == null || window.RemainingPercent < match.RemainingPercent)
                    {
                        match = window;
                    }
                }
                return match;
            }
        }
    }

    internal sealed class TokenUsageDailyBucket
    {
        public string StartDate { get; set; }
        public long Tokens { get; set; }
    }

    internal sealed class TokenUsageSummary
    {
        public TokenUsageSummary()
        {
            DailyBuckets = new List<TokenUsageDailyBucket>();
        }

        public long? LifetimeTokens { get; set; }
        public long? PeakDailyTokens { get; set; }
        public long? LongestRunningTurnSeconds { get; set; }
        public long? CurrentStreakDays { get; set; }
        public long? LongestStreakDays { get; set; }
        public List<TokenUsageDailyBucket> DailyBuckets { get; private set; }
    }

    internal sealed class BankedReset
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Status { get; set; }
        public DateTime GrantedAtUtc { get; set; }
        public DateTime? ExpiresAtUtc { get; set; }
    }

    internal sealed class BankedResetSummary
    {
        public BankedResetSummary()
        {
            Items = new List<BankedReset>();
        }

        public int? AvailableCount { get; set; }
        public bool DetailsAvailable { get; set; }
        public DateTime? CountObservedAtUtc { get; set; }
        public DateTime? DetailsObservedAtUtc { get; set; }
        public List<BankedReset> Items { get; private set; }
    }

    internal static class UsageFormatting
    {
        public static string DurationLabel(int? durationMinutes)
        {
            if (!durationMinutes.HasValue || durationMinutes.Value <= 0)
            {
                return "Usage window";
            }

            int minutes = durationMinutes.Value;
            if (minutes == 10080)
            {
                return "Weekly";
            }
            if (minutes % 1440 == 0)
            {
                return string.Format(CultureInfo.CurrentCulture, "{0}-day", minutes / 1440);
            }
            if (minutes % 60 == 0)
            {
                return string.Format(CultureInfo.CurrentCulture, "{0}-hour", minutes / 60);
            }
            return string.Format(CultureInfo.CurrentCulture, "{0}-minute", minutes);
        }

        public static string ResetTime(DateTime? resetUtc)
        {
            if (!resetUtc.HasValue)
            {
                return "Reset time unavailable";
            }

            DateTime local = resetUtc.Value.ToLocalTime();
            TimeSpan remaining = resetUtc.Value - DateTime.UtcNow;
            string relative;
            if (remaining.TotalSeconds <= 0)
            {
                relative = "reset due";
            }
            else if (remaining.TotalDays >= 1)
            {
                relative = string.Format(
                    CultureInfo.CurrentCulture,
                    "in {0}d {1}h",
                    (int)remaining.TotalDays,
                    remaining.Hours);
            }
            else if (remaining.TotalHours >= 1)
            {
                relative = string.Format(
                    CultureInfo.CurrentCulture,
                    "in {0}h {1}m",
                    (int)remaining.TotalHours,
                    remaining.Minutes);
            }
            else
            {
                relative = string.Format(
                    CultureInfo.CurrentCulture,
                    "in {0}m",
                    Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes)));
            }

            return string.Format(
                CultureInfo.CurrentCulture,
                "{0} ({1})",
                local.ToString("ddd, MMM d, h:mm tt", CultureInfo.CurrentCulture),
                relative);
        }
    }
}
