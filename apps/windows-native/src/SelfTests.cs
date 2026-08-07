using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace UsageApp.Native
{
    internal static class SelfTests
    {
        public static string Run()
        {
            List<string> failures = new List<string>();
            RunTest(failures, "multiple windows and limiting percentage", TestMultipleWindows);
            RunTest(failures, "clamping and duration labels", TestClampingAndLabels);
            RunTest(failures, "authoritative banked reset count", TestBankedResets);
            RunTest(failures, "plan type fallback", TestPlanTypeFallback);
            RunTest(failures, "mandatory RPC compatibility detection", TestCompatibilityErrors);
            RunTest(failures, "rate-limit notification recognition", TestRateLimitNotifications);
            RunTest(failures, "missing RPC result rejection", TestMissingRpcResult);
            RunTest(failures, "invalid mandatory result rejection", TestInvalidMandatoryResults);
            RunTest(failures, "optional account usage normalization", TestAccountUsageNormalization);
            RunTest(failures, "expired banked reset presentation", TestBankedResetStatus);
            RunTest(failures, "activity filter geometry", TestActivityFilterGeometry);
            RunTest(failures, "dashboard provider header clearance", TestProviderHeaderClearance);
            RunTest(failures, "taskbar-edge flyout positioning", TestTaskbarEdgePositioning);
            RunTest(failures, "tray icon edge values", TestTrayIcons);
            RunTest(failures, "tray number fills the shell", TestTrayIconCoverage);
            RunTest(failures, "tray DPI value and edge rendering",
                TrayIconRendererSelfTests.TestDpiValueAndEdgeMatrix);
            RunTest(failures, "tray configured color rendering",
                TrayIconRendererSelfTests.TestConfiguredColorModes);
            RunTest(failures, "tray installed font fallback",
                TrayIconRendererSelfTests.TestInstalledFontFallback);
            RunTest(failures, "tray quota window selection", TestTrayWindowSelection);
            RunTest(failures, "precision touchpad wheel accumulation", TestPrecisionWheelScrolling);
            RunTest(failures, "native settings normalization", TestSettings);
            RunTest(failures, "provider visibility invariants", TestProviderVisibilityRules);
            RunTest(failures, "native settings filesystem persistence", TestSettingsPersistence);
            RunTest(failures, "quota notification threshold normalization", TestQuotaNotificationThresholds);
            RunTest(failures, "quota notification crossing selection", TestQuotaNotificationCrossings);
            RunTest(failures, "quota notification reset and rise re-arm", TestQuotaNotificationRearm);
            RunTest(failures, "current-user startup registration safety", TestStartupRegistration);
            RunTest(failures, "Claude status-line quota normalization", TestClaudeStatusLine);
            RunTest(failures, "Claude freshness transitions", TestClaudeFreshnessTransitions);
            RunTest(failures, "Claude last-known update selection", TestClaudeSnapshotSelection);
            RunTest(failures, "Claude loopback receiver token safety", TestClaudeReceiverToken);
            RunTest(failures, "Claude normalized cache round trip", TestClaudeSnapshotRoundTrip);
            RunTest(failures, "Claude config and semantic JSON safety", TestClaudeIntegrationConfiguration);
            RunTest(failures, "Claude reversible integration transaction", TestClaudeIntegrationTransaction);
            RunTest(failures, "Claude receiver start failure reporting", TestClaudeReceiverStartFailure);
            RunTest(failures, "diagnostic failure recognition", TestDiagnosticFailureRecognition);
            RunTest(failures, "normalized snapshot cache round trip", TestSnapshotRoundTrip);
            RunTest(failures, "last-known banked reset merge", TestSnapshotMerge);

            if (failures.Count == 0)
            {
                return "status=passed" + Environment.NewLine + "tests=38" + Environment.NewLine;
            }
            return "status=failed"
                + Environment.NewLine
                + "tests=38"
                + Environment.NewLine
                + string.Join(Environment.NewLine, failures.ToArray())
                + Environment.NewLine;
        }

        private static void RunTest(
            List<string> failures,
            string name,
            Action test)
        {
            try
            {
                test();
            }
            catch (Exception error)
            {
                failures.Add("failure=" + name + ": " + error.Message);
            }
        }

        private static void TestMultipleWindows()
        {
            UsageSnapshot snapshot = Parse(
                "{\"rateLimitsByLimitId\":{"
                + "\"codex\":{\"limitId\":\"codex\",\"primary\":{\"usedPercent\":25,\"windowDurationMins\":300},"
                + "\"secondary\":{\"usedPercent\":40,\"windowDurationMins\":10080}},"
                + "\"spark\":{\"limitId\":\"spark\",\"limitName\":\"Spark\",\"secondary\":{\"usedPercent\":80,\"windowDurationMins\":10080}}"
                + "}}");
            Assert(snapshot.Windows.Count == 3, "expected every valid returned window");
            Assert(snapshot.PreferredWindow != null, "expected a tray window");
            Assert(snapshot.PreferredWindow.RemainingPercent == 20, "tray must use the most constrained window");
        }

        private static void TestClampingAndLabels()
        {
            UsageSnapshot snapshot = Parse(
                "{\"rateLimits\":{\"limitId\":\"codex\","
                + "\"primary\":{\"usedPercent\":-5,\"windowDurationMins\":300},"
                + "\"secondary\":{\"usedPercent\":140,\"windowDurationMins\":10080}}}");
            Assert(snapshot.Windows.Count == 2, "expected two valid windows");
            Assert(snapshot.Windows[0].RemainingPercent == 100, "negative usage must clamp");
            Assert(snapshot.Windows[1].RemainingPercent == 0, "usage over 100 must clamp");
            Assert(snapshot.Windows[0].Label == "5-hour", "300 minutes must be duration-derived");
            Assert(snapshot.Windows[1].Label == "Weekly", "10080 minutes must be weekly");
        }

        private static void TestBankedResets()
        {
            UsageSnapshot unavailableDetails = Parse(
                "{\"rateLimits\":{\"primary\":{\"usedPercent\":10}},"
                + "\"rateLimitResetCredits\":{\"availableCount\":3,\"credits\":null}}");
            Assert(
                unavailableDetails.BankedResets.AvailableCount == 3,
                "provider count must remain authoritative");
            Assert(
                !unavailableDetails.BankedResets.DetailsAvailable,
                "null details must mean unavailable");
            Assert(
                unavailableDetails.BankedResets.Items.Count == 0,
                "null details must not invent rows");

            UsageSnapshot withDetails = Parse(
                "{\"rateLimits\":{\"primary\":{\"usedPercent\":10}},"
                + "\"rateLimitResetCredits\":{\"availableCount\":2,\"credits\":["
                + "{\"id\":\"later\",\"grantedAt\":100,\"expiresAt\":300},"
                + "{\"id\":\"first\",\"grantedAt\":100,\"expiresAt\":200}]}}");
            Assert(withDetails.BankedResets.DetailsAvailable, "array details must be available");
            Assert(withDetails.BankedResets.Items.Count == 2, "expected both valid details");
            Assert(withDetails.BankedResets.Items[0].Id == "first", "expiries must sort ascending");
        }

        private static void TestPlanTypeFallback()
        {
            UsageSnapshot snapshot = Parse(
                "{\"rateLimitsByLimitId\":{"
                + "\"codex\":{\"limitId\":\"codex\",\"secondary\":{\"usedPercent\":20}},"
                + "\"spark\":{\"limitId\":\"spark\",\"planType\":\"pro\","
                + "\"secondary\":{\"usedPercent\":30}}}}");
            Assert(
                snapshot.PlanType == "pro",
                "plan type on a non-preferred returned limit was discarded");
        }

        private static void TestCompatibilityErrors()
        {
            Assert(
                CodexAppServer.IsCompatibilityRpcErrorForTest(-32601, "anything"),
                "JSON-RPC method-not-found code was not recognized");
            Assert(
                CodexAppServer.IsCompatibilityRpcErrorForTest(
                    null,
                    "account/rateLimits/read is not implemented"),
                "mandatory method compatibility message was not recognized");
            Assert(
                !CodexAppServer.IsCompatibilityRpcErrorForTest(
                    401,
                    "authentication required"),
                "authentication failure was misclassified as compatibility");
        }

        private static void TestRateLimitNotifications()
        {
            JavaScriptSerializer json = new JavaScriptSerializer();
            object update = json.DeserializeObject(
                "{\"method\":\"account/rateLimits/updated\",\"params\":{}}");
            object request = json.DeserializeObject(
                "{\"id\":4,\"method\":\"account/rateLimits/updated\",\"params\":{}}");
            object other = json.DeserializeObject(
                "{\"method\":\"account/updated\",\"params\":{}}");
            Assert(
                CodexAppServer.IsRateLimitsUpdatedNotificationForTest(update),
                "rate-limit update notification was not recognized");
            Assert(
                !CodexAppServer.IsRateLimitsUpdatedNotificationForTest(request),
                "server request was misclassified as a notification");
            Assert(
                !CodexAppServer.IsRateLimitsUpdatedNotificationForTest(other),
                "unrelated notification triggered a rate-limit refresh");
        }

        private static void TestMissingRpcResult()
        {
            JavaScriptSerializer json = new JavaScriptSerializer();
            object valid = json.DeserializeObject(
                "{\"id\":2,\"result\":{\"rateLimits\":{}}}");
            object extracted = CodexAppServer.ExtractResultForTest(
                valid,
                "account/rateLimits/read");
            Assert(extracted != null, "valid RPC result was rejected");

            object missing = json.DeserializeObject("{\"id\":2}");
            bool missingRejected = false;
            try
            {
                CodexAppServer.ExtractResultForTest(
                    missing,
                    "account/rateLimits/read");
            }
            catch (System.IO.InvalidDataException)
            {
                missingRejected = true;
            }
            Assert(
                missingRejected,
                "missing RPC result would overwrite the last-known quota");

            object nullResult = json.DeserializeObject(
                "{\"id\":2,\"result\":null}");
            bool nullRejected = false;
            try
            {
                CodexAppServer.ExtractResultForTest(
                    nullResult,
                    "account/rateLimits/read");
            }
            catch (System.IO.InvalidDataException)
            {
                nullRejected = true;
            }
            Assert(
                nullRejected,
                "null RPC result would overwrite the last-known quota");
        }

        private static void TestInvalidMandatoryResults()
        {
            object[] invalidResults = new object[]
            {
                "not a rate-limit object",
                new object[0]
            };
            foreach (object invalidResult in invalidResults)
            {
                bool rejected = false;
                try
                {
                    CodexAppServer.NormalizeForTest(invalidResult);
                }
                catch (InvalidDataException)
                {
                    rejected = true;
                }
                Assert(
                    rejected,
                    "a scalar or array mandatory rate-limit result was accepted");
            }
        }

        private static void TestAccountUsageNormalization()
        {
            JavaScriptSerializer json = new JavaScriptSerializer();
            object rawLimits = json.DeserializeObject(
                "{\"rateLimits\":{\"primary\":{\"usedPercent\":10}}}");
            object rawUsage = json.DeserializeObject(
                "{\"summary\":{"
                + "\"lifetimeTokens\":5000000000.9,"
                + "\"peakDailyTokens\":123.8,"
                + "\"longestRunningTurnSec\":45.7,"
                + "\"currentStreakDays\":-2,"
                + "\"longestStreakDays\":12.6},"
                + "\"dailyUsageBuckets\":["
                + "{\"startDate\":\"2026-07-31\",\"tokens\":4294967296.9},"
                + "{\"startDate\":\"2026-07-29\",\"tokens\":10.7},"
                + "{\"startDate\":\"2026-07-29\",\"tokens\":12.4},"
                + "{\"startDate\":\"2026-07-30\",\"tokens\":-1},"
                + "{\"startDate\":\"2026-02-30\",\"tokens\":99},"
                + "{\"startDate\":\"not-a-date\",\"tokens\":18}]}");

            UsageSnapshot snapshot = CodexAppServer.NormalizeForTest(
                rawLimits,
                rawUsage);
            Assert(snapshot.TokenUsage != null, "account usage summary was discarded");
            Assert(
                snapshot.TokenUsage.LifetimeTokens == 5000000000L,
                "lifetime tokens did not preserve a value above Int32");
            Assert(
                snapshot.TokenUsage.PeakDailyTokens == 123L,
                "positive fractional peak tokens were not truncated");
            Assert(
                snapshot.TokenUsage.LongestRunningTurnSeconds == 45L,
                "positive fractional turn duration was not truncated");
            Assert(
                !snapshot.TokenUsage.CurrentStreakDays.HasValue,
                "negative summary values were retained");
            Assert(
                snapshot.TokenUsage.LongestStreakDays == 12L,
                "positive fractional streak length was not truncated");
            Assert(
                snapshot.TokenUsage.DailyBuckets.Count == 2,
                "negative or invalid-date daily rows were retained");
            Assert(
                snapshot.TokenUsage.DailyBuckets[0].StartDate == "2026-07-29"
                    && snapshot.TokenUsage.DailyBuckets[0].Tokens == 12L,
                "daily usage buckets were not sorted, deduplicated, or truncated");
            Assert(
                snapshot.TokenUsage.DailyBuckets[1].StartDate == "2026-07-31"
                    && snapshot.TokenUsage.DailyBuckets[1].Tokens == 4294967296L,
                "daily usage did not preserve a value above Int32");
            Assert(
                snapshot.TokenUsageObservedAtUtc == snapshot.ObservedAtUtc,
                "account usage observation time did not match the snapshot");

            UsageSnapshot emptyUsage = CodexAppServer.NormalizeForTest(
                rawLimits,
                json.DeserializeObject("{\"summary\":{}}"));
            Assert(
                emptyUsage.TokenUsage == null,
                "an empty usage result was stamped as fresh history");
        }

        private static void TestBankedResetStatus()
        {
            DateTime nowUtc = new DateTime(
                2026,
                7,
                31,
                12,
                0,
                0,
                DateTimeKind.Utc);
            BankedReset expired = new BankedReset
            {
                Status = "available",
                ExpiresAtUtc = nowUtc.AddSeconds(-1)
            };
            BankedReset current = new BankedReset
            {
                Status = "available",
                ExpiresAtUtc = nowUtc.AddSeconds(1)
            };
            Assert(
                UsageFlyout.BankedRowStatusText(expired, nowUtc) == "EXPIRED",
                "past last-known reset was still presented as available");
            Assert(
                UsageFlyout.BankedRowStatusText(current, nowUtc) == "AVAILABLE",
                "current banked reset status changed");
            Assert(
                UsageFlyout.BankedRowStatusText(null, nowUtc) == "UNKNOWN",
                "missing banked reset did not use the safe status");
        }

        private static void TestActivityFilterGeometry()
        {
            const int filterWidth = 1260;
            const int optionsLeft = 116;
            const int optionCount = 6;
            const int optionGap = 3;
            const int rightInset = 23;
            int width = NativeDashboardForm.CalculateActivityFilterButtonWidth(
                filterWidth,
                optionsLeft,
                optionCount,
                optionGap,
                rightInset,
                83);
            int right = optionsLeft
                + width * optionCount
                + optionGap * (optionCount - 1);
            Assert(width > 0, "date-filter option width was not positive");
            Assert(
                right <= filterWidth - rightInset,
                "date-filter options overflowed their available row");
            Assert(
                NativeDashboardForm.CalculateActivityFilterButtonWidth(
                    100,
                    120,
                    optionCount,
                    optionGap,
                    rightInset,
                    83) >= 1,
                "narrow date-filter geometry collapsed to zero");

            DateTime today = new DateTime(2026, 7, 31);
            DateTime from = new DateTime(2026, 7, 10);
            DateTime to = new DateTime(2026, 7, 20);
            Assert(
                NativeDashboardForm.IsValidCustomActivityRange(from, to)
                    && !NativeDashboardForm.IsValidCustomActivityRange(to, from),
                "custom date-range ordering validation failed");
            Assert(
                NativeDashboardForm.ActivityDateIsSelected(
                    from,
                    -1,
                    from,
                    to,
                    today)
                    && NativeDashboardForm.ActivityDateIsSelected(
                        to,
                        -1,
                        from,
                        to,
                        today)
                    && !NativeDashboardForm.ActivityDateIsSelected(
                        from.AddDays(-1),
                        -1,
                        from,
                        to,
                        today)
                    && !NativeDashboardForm.ActivityDateIsSelected(
                        to.AddDays(1),
                        -1,
                        from,
                        to,
                        today),
                "custom date filtering was not inclusive or escaped its range");
            Assert(
                NativeDashboardForm.ActivityDateIsSelected(
                    today.AddDays(-6),
                    7,
                    null,
                    null,
                    today)
                    && !NativeDashboardForm.ActivityDateIsSelected(
                        today.AddDays(-7),
                        7,
                        null,
                        null,
                        today),
                "seven-day preset bounds changed while adding custom ranges");
        }

        private static void TestProviderHeaderClearance()
        {
            int left = NativeDashboardForm.CalculateProviderLeftLimit(
                214,
                238,
                270,
                16);
            Assert(left == 270, "minimum provider-header inset was not preserved");
            int longTitleLeft = NativeDashboardForm.CalculateProviderLeftLimit(
                314,
                286,
                270,
                16);
            Assert(
                longTitleLeft == 330,
                "provider switcher did not clear the product-title region");
        }

        private static void TestTaskbarEdgePositioning()
        {
            Rectangle screen = new Rectangle(0, 0, 1920, 1080);
            Size flyout = new Size(410, 650);
            int margin = 10;
            Rectangle bottomWork = new Rectangle(0, 0, 1920, 1040);
            Rectangle topWork = new Rectangle(0, 40, 1920, 1040);
            Rectangle leftWork = new Rectangle(40, 0, 1880, 1080);
            Rectangle rightWork = new Rectangle(0, 0, 1880, 1080);

            Rectangle bottom = UsageFlyout.CalculateFlyoutBounds(
                screen,
                bottomWork,
                flyout,
                margin);
            Rectangle top = UsageFlyout.CalculateFlyoutBounds(
                screen,
                topWork,
                flyout,
                margin);
            Rectangle left = UsageFlyout.CalculateFlyoutBounds(
                screen,
                leftWork,
                flyout,
                margin);
            Rectangle right = UsageFlyout.CalculateFlyoutBounds(
                screen,
                rightWork,
                flyout,
                margin);

            Assert(bottom.Bottom == bottomWork.Bottom - margin,
                "bottom taskbar did not anchor the flyout above its edge");
            Assert(top.Top == topWork.Top + margin,
                "top taskbar did not anchor the flyout below its edge");
            Assert(left.Left == leftWork.Left + margin,
                "left taskbar did not anchor the flyout beside its edge");
            Assert(right.Right == rightWork.Right - margin,
                "right taskbar did not anchor the flyout beside its edge");
            Assert(bottomWork.Contains(bottom)
                    && topWork.Contains(top)
                    && leftWork.Contains(left)
                    && rightWork.Contains(right),
                "a taskbar position escaped its monitor working area");
        }

        private static void TestTrayIcons()
        {
            int?[] values = new int?[] { null, 0, 1, 7, 10, 11, 88, 99, 100 };
            foreach (int? value in values)
            {
                using (Icon icon = TrayIconRenderer.Create(value))
                {
                    Assert(icon != null, "icon was not produced");
                    Assert(icon.Width > 0 && icon.Height > 0, "icon dimensions were invalid");
                    using (Bitmap shellBitmap = icon.ToBitmap())
                    {
                        bool visiblePixel = false;
                        for (int y = 0; y < shellBitmap.Height && !visiblePixel; y++)
                        {
                            for (int x = 0; x < shellBitmap.Width; x++)
                            {
                                if (shellBitmap.GetPixel(x, y).A >= 12)
                                {
                                    visiblePixel = true;
                                    break;
                                }
                            }
                        }
                        Assert(
                            visiblePixel,
                            "the Windows HICON round trip lost the tray glyph");
                    }
                }
            }
        }

        private static void TestTrayIconCoverage()
        {
            int?[] values = new int?[] { null, 10, 48, 88, 100 };
            foreach (int? value in values)
            {
                using (Bitmap bitmap = TrayIconRenderer.CreateBitmap(
                    value,
                    24,
                    "Consolas",
                    Color.FromArgb(48, 113, 141)))
                {
                    int left = bitmap.Width;
                    int top = bitmap.Height;
                    int right = -1;
                    int bottom = -1;
                    for (int y = 0; y < bitmap.Height; y++)
                    {
                        for (int x = 0; x < bitmap.Width; x++)
                        {
                            if (bitmap.GetPixel(x, y).A < 12)
                            {
                                continue;
                            }
                            left = Math.Min(left, x);
                            top = Math.Min(top, y);
                            right = Math.Max(right, x);
                            bottom = Math.Max(bottom, y);
                        }
                    }
                    Assert(right >= left && bottom >= top, "tray bitmap was transparent");
                    // Windows gives the notification area only a tiny square.
                    // Three natural-width digits must become shorter than two;
                    // the renderer must not squeeze them horizontally just to
                    // make them artificially tall.
                    int minimumHeight = value.HasValue && value.Value >= 100
                        ? 8
                        : value.HasValue && value.Value >= 10
                            ? 14
                            : 20;
                    Assert(
                        bottom - top + 1 >= minimumHeight,
                        "tray glyph was not tall enough for "
                            + (value.HasValue ? value.Value.ToString() : "?")
                            + ": "
                            + (bottom - top + 1));
                    if (value.HasValue && value.Value >= 10)
                    {
                        Assert(
                            right - left + 1 >= 20,
                            "multi-digit tray glyph was not wide enough for "
                                + value.Value
                                + ": "
                                + (right - left + 1));
                    }
                }
            }
        }

        private static void TestPrecisionWheelScrolling()
        {
            PrecisionWheelAccumulator wheel = new PrecisionWheelAccumulator();
            Assert(
                wheel.Consume(-120, 120, 64) == 64,
                "a conventional mouse-wheel detent changed distance");

            wheel.Reset();
            int precisionTotal = 0;
            for (int index = 0; index < 120; index++)
            {
                precisionTotal += wheel.Consume(-1, 120, 64);
            }
            Assert(
                precisionTotal == 64,
                "small precision-touchpad deltas were lost instead of accumulated");

            wheel.Reset();
            int cancelled = wheel.Consume(-1, 120, 64)
                + wheel.Consume(1, 120, 64);
            Assert(
                cancelled == 0,
                "a reversed sub-pixel gesture produced a phantom scroll");

            wheel.Reset();
            int upwardTotal = 0;
            for (int index = 0; index < 4; index++)
            {
                upwardTotal += wheel.Consume(30, 120, 64);
            }
            Assert(
                upwardTotal == -64,
                "precision scrolling did not preserve upward direction");
        }

        private static void TestSettings()
        {
            NativeSettings settings = new NativeSettings();
            Assert(
                settings.ShowCodexProvider && settings.ShowClaudeProvider,
                "provider visibility defaults changed");
            Assert(
                settings.ShowCodexTrayIcon && !settings.ShowClaudeTrayIcon,
                "taskbar icon defaults changed");
            Assert(settings.RefreshIntervalMinutes == 5, "refresh default changed");
            Assert(settings.TrayFontName == "Consolas", "tray font default changed");
            Assert(
                !settings.CodexQuotaNotificationsEnabled,
                "quota notifications were not off by default");
            Assert(
                settings.CodexQuotaNotificationThresholdsCsv == "25,10,5",
                "default quota notification thresholds changed");
            Assert(!settings.StartWithWindows, "startup was not off by default");
            settings.RefreshIntervalMinutes = 7;
            settings.FlyoutTextScale = 999;
            settings.InterfaceFontName = "missing";
            settings.TrayFontName = "missing";
            settings.TrayCodexColor = "nope";
            settings.TrayWindowMode = "missing";
            settings.Normalize();
            Assert(settings.RefreshIntervalMinutes == 5, "invalid refresh was retained");
            Assert(settings.FlyoutTextScale == 100, "invalid text scale was retained");
            Assert(settings.InterfaceFontName == "Segoe UI", "invalid interface font was retained");
            Assert(settings.TrayFontName == "Consolas", "invalid tray font was retained");
            Assert(settings.TrayCodexColor == "#30718D", "invalid tray color was retained");
            Assert(
                settings.TrayWindowMode == "MostConstrained",
                "invalid tray quota source was retained");

            settings.RefreshIntervalMinutes = 15;
            settings.TrayFontName = "Verdana";
            settings.TrayWindowMode = "LongestWindow";
            settings.CodexQuotaNotificationsEnabled = true;
            settings.CodexQuotaNotificationThresholdsCsv = "42,7";
            settings.StartWithWindows = true;
            string serialized = NativeSettingsStore.Serialize(settings);
            Assert(
                !serialized.Contains("CodexTrayColor"),
                "derived Color object leaked into settings JSON");
            NativeSettings restored = NativeSettingsStore.Deserialize(serialized);
            Assert(restored != null, "settings did not deserialize");
            Assert(restored.RefreshIntervalMinutes == 15, "refresh setting did not round trip");
            Assert(restored.TrayFontName == "Verdana", "tray font did not round trip");
            Assert(
                restored.TrayWindowMode == "LongestWindow",
                "tray quota source did not round trip");
            Assert(
                restored.CodexQuotaNotificationsEnabled,
                "notification enabled state did not round trip");
            Assert(
                restored.CodexQuotaNotificationThresholdsCsv == "42,7",
                "notification thresholds did not round trip");
            Assert(restored.StartWithWindows, "startup setting did not round trip");
        }

        private static void TestTrayWindowSelection()
        {
            List<UsageWindow> windows = new List<UsageWindow>();
            UsageWindow shortWindow = new UsageWindow
            {
                Label = "5-hour",
                DurationMinutes = 300,
                UsedPercent = 20
            };
            UsageWindow longWindow = new UsageWindow
            {
                Label = "Weekly",
                DurationMinutes = 10080,
                UsedPercent = 35
            };
            UsageWindow constrained = new UsageWindow
            {
                Label = "Special",
                DurationMinutes = 1440,
                UsedPercent = 90
            };
            windows.Add(shortWindow);
            windows.Add(longWindow);
            windows.Add(constrained);

            Assert(
                object.ReferenceEquals(
                    TrayWindowSelector.Select(windows, "MostConstrained"),
                    constrained),
                "lowest-remaining tray source was not selected");
            Assert(
                object.ReferenceEquals(
                    TrayWindowSelector.Select(windows, "ShortestWindow"),
                    shortWindow),
                "shortest tray window was not selected");
            Assert(
                object.ReferenceEquals(
                    TrayWindowSelector.Select(windows, "LongestWindow"),
                    longWindow),
                "longest tray window was not selected");
        }

        private static void TestProviderVisibilityRules()
        {
            NativeSettings settings = new NativeSettings();
            settings.ShowCodexProvider = false;
            settings.ShowClaudeProvider = false;
            settings.ShowCodexTrayIcon = false;
            settings.ShowClaudeTrayIcon = false;
            settings.Normalize();
            Assert(
                settings.ShowCodexProvider && !settings.ShowClaudeProvider,
                "normalization allowed both providers to be hidden");
            Assert(
                settings.ShowCodexTrayIcon && !settings.ShowClaudeTrayIcon,
                "normalization did not retain a usable taskbar icon");

            settings.ShowCodexProvider = false;
            settings.ShowClaudeProvider = true;
            settings.ShowCodexTrayIcon = true;
            settings.ShowClaudeTrayIcon = false;
            settings.Normalize();
            Assert(
                !settings.ShowCodexTrayIcon && settings.ShowClaudeTrayIcon,
                "a hidden provider retained its icon or left no icon visible");

            settings.ShowCodexProvider = true;
            settings.ShowClaudeProvider = false;
            settings.ShowCodexTrayIcon = true;
            settings.ShowClaudeTrayIcon = true;
            settings.Normalize();
            Assert(
                settings.ShowCodexTrayIcon && !settings.ShowClaudeTrayIcon,
                "Claude taskbar icon remained enabled while Claude was hidden");

            NativeSettings legacy = NativeSettingsStore.Deserialize(
                "{\"RefreshIntervalMinutes\":10}");
            legacy.Normalize();
            Assert(
                legacy.ShowCodexProvider
                    && legacy.ShowClaudeProvider
                    && legacy.ShowCodexTrayIcon
                    && !legacy.ShowClaudeTrayIcon,
                "an older settings file did not receive safe provider defaults");
        }

        private static void TestSettingsPersistence()
        {
            string testRoot = Path.Combine(
                Path.GetTempPath(),
                "UsageAppNativeSettingsTest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testRoot);
            try
            {
                string settingsPath = Path.Combine(testRoot, "settings.json");
                NativeSettingsStore store = new NativeSettingsStore(settingsPath);
                NativeSettings settings = new NativeSettings();
                settings.RefreshIntervalMinutes = 30;
                settings.FlyoutTextScale = 125;
                settings.InterfaceFontName = "Verdana";
                settings.TrayFontName = "Arial";
                settings.TrayCodexColor = "#38BDF8";
                settings.CodexQuotaNotificationsEnabled = true;
                settings.CodexQuotaNotificationThresholdsCsv = "42,7";
                settings.StartWithWindows = true;
                settings.ShowCodexProvider = false;
                settings.ShowClaudeProvider = true;
                settings.ShowCodexTrayIcon = false;
                settings.ShowClaudeTrayIcon = true;
                Assert(store.Save(settings), "settings file was not created");

                NativeSettings loaded = store.Load();
                Assert(loaded.RefreshIntervalMinutes == 30, "refresh setting was not loaded");
                Assert(loaded.FlyoutTextScale == 125, "text scale was not loaded");
                Assert(loaded.InterfaceFontName == "Verdana", "interface font was not loaded");
                Assert(loaded.TrayFontName == "Arial", "tray font was not loaded");
                Assert(loaded.TrayCodexColor == "#38BDF8", "tray color was not loaded");
                Assert(
                    loaded.CodexQuotaNotificationsEnabled,
                    "notification enabled state was not loaded");
                Assert(
                    loaded.CodexQuotaNotificationThresholdsCsv == "42,7",
                    "notification thresholds were not loaded");
                Assert(loaded.StartWithWindows, "startup setting was not loaded");
                Assert(
                    !loaded.ShowCodexProvider
                        && loaded.ShowClaudeProvider
                        && !loaded.ShowCodexTrayIcon
                        && loaded.ShowClaudeTrayIcon,
                    "provider visibility settings were not loaded");

                settings.RefreshIntervalMinutes = 60;
                settings.FlyoutTextScale = 110;
                settings.InterfaceFontName = "Tahoma";
                settings.TrayFontName = "Consolas";
                settings.TrayCodexColor = "#30718D";
                settings.CodexQuotaNotificationsEnabled = false;
                settings.CodexQuotaNotificationThresholdsCsv = "25,10,5";
                settings.StartWithWindows = false;
                settings.ShowCodexProvider = true;
                settings.ShowClaudeProvider = false;
                settings.ShowCodexTrayIcon = true;
                settings.ShowClaudeTrayIcon = false;
                Assert(store.Save(settings), "existing settings file was not replaced");
                NativeSettings replaced = store.Load();
                Assert(
                    replaced.RefreshIntervalMinutes == 60
                        && replaced.FlyoutTextScale == 110
                        && replaced.InterfaceFontName == "Tahoma"
                        && replaced.TrayFontName == "Consolas"
                        && replaced.TrayCodexColor == "#30718D"
                        && !replaced.CodexQuotaNotificationsEnabled
                        && replaced.CodexQuotaNotificationThresholdsCsv == "25,10,5"
                        && replaced.ShowCodexProvider
                        && !replaced.ShowClaudeProvider
                        && replaced.ShowCodexTrayIcon
                        && !replaced.ShowClaudeTrayIcon
                        && !replaced.StartWithWindows,
                    "replacement settings did not load from the new file");
                Assert(
                    !File.Exists(settingsPath + ".tmp"),
                    "successful replacement left a temporary file");

                File.WriteAllText(settingsPath, "{broken-json", Encoding.UTF8);
                NativeSettings fallback = store.Load();
                Assert(
                    fallback.RefreshIntervalMinutes == 5
                        && fallback.FlyoutTextScale == 100
                        && fallback.InterfaceFontName == "Segoe UI"
                        && fallback.TrayFontName == "Consolas"
                        && fallback.TrayCodexColor == "#30718D"
                        && !fallback.CodexQuotaNotificationsEnabled
                        && fallback.CodexQuotaNotificationThresholdsCsv == "25,10,5"
                        && fallback.ShowCodexProvider
                        && fallback.ShowClaudeProvider
                        && fallback.ShowCodexTrayIcon
                        && !fallback.ShowClaudeTrayIcon
                        && !fallback.StartWithWindows,
                    "corrupt settings did not fall back to defaults");

                string blockedSettingsPath = Path.Combine(
                    testRoot,
                    "settings-target-is-a-directory");
                Directory.CreateDirectory(blockedSettingsPath);
                NativeSettingsStore blockedStore = new NativeSettingsStore(
                    blockedSettingsPath);
                Assert(
                    !blockedStore.Save(new NativeSettings()),
                    "an unusable settings destination reported a successful save");
            }
            finally
            {
                try
                {
                    Directory.Delete(testRoot, true);
                }
                catch
                {
                    // A cleanup failure must not hide the persistence assertion.
                }
            }
        }

        private static void TestQuotaNotificationThresholds()
        {
            Assert(
                NativeSettings.NormalizeCodexQuotaNotificationThresholdsCsv(
                    "25,10,5") == "25,10,5",
                "the balanced notification preset changed");
            Assert(
                NativeSettings.NormalizeCodexQuotaNotificationThresholdsCsv(
                    " 7,42,7,99,0,100,not-a-number ") == "99,42,7",
                "arbitrary thresholds were not validated, deduplicated, and sorted");
            Assert(
                NativeSettings.NormalizeCodexQuotaNotificationThresholdsCsv(
                    "1,2,3,4,5,6") == "5,4,3,2,1",
                "more than five thresholds were retained");
            Assert(
                NativeSettings.NormalizeCodexQuotaNotificationThresholdsCsv(
                    "0,100,bad") == "25,10,5",
                "an invalid threshold set did not fall back safely");
            int[] parsed = NativeSettings.ParseCodexQuotaNotificationThresholds(
                "83,17,3");
            Assert(
                parsed.Length == 3
                    && parsed[0] == 83
                    && parsed[1] == 17
                    && parsed[2] == 3,
                "valid custom 1-99 thresholds were not exposed to the evaluator");
            string normalized;
            string error;
            Assert(
                NativeSettings.TryNormalizeCodexQuotaNotificationThresholdsCsv(
                    "7,42,3",
                    out normalized,
                    out error)
                    && normalized == "42,7,3"
                    && error == null,
                "strict custom-threshold validation rejected valid input");
            Assert(
                !NativeSettings.TryNormalizeCodexQuotaNotificationThresholdsCsv(
                    "10,10",
                    out normalized,
                    out error)
                    && !string.IsNullOrEmpty(error),
                "strict custom-threshold validation accepted a duplicate");
            Assert(
                !NativeSettings.TryNormalizeCodexQuotaNotificationThresholdsCsv(
                    "25,10,5,3,2,1",
                    out normalized,
                    out error)
                    && !string.IsNullOrEmpty(error),
                "strict custom-threshold validation accepted more than five values");
        }

        private static void TestQuotaNotificationCrossings()
        {
            QuotaNotificationEvaluator evaluator =
                new QuotaNotificationEvaluator();
            DateTime reset = new DateTime(
                2026,
                8,
                7,
                12,
                0,
                0,
                DateTimeKind.Utc);
            UsageSnapshot first = NotificationSnapshot(
                NotificationWindow("codex", "Weekly", 40, reset),
                NotificationWindow("spark", "Spark - Weekly", 40, reset));
            Assert(
                evaluator.Evaluate(first, new int[] { 25, 10, 5 }) == null,
                "startup observation generated a quota warning");

            UsageSnapshot crossed = NotificationSnapshot(
                NotificationWindow("codex", "Weekly", 24, reset),
                NotificationWindow("spark", "Spark - Weekly", 4, reset));
            QuotaNotificationDecision decision = evaluator.Evaluate(
                crossed,
                new int[] { 25, 10, 5 });
            Assert(decision != null, "a crossed threshold produced no warning");
            Assert(
                decision.ThresholdPercent == 5,
                "the most urgent newly crossed threshold was not selected");
            Assert(
                decision.QuotaLabel == "Spark - Weekly"
                    && decision.RemainingPercent == 4,
                "the warning did not identify its quota window");
            Assert(
                evaluator.Evaluate(crossed, new int[] { 25, 10, 5 }) == null,
                "the same snapshot produced a repeated warning");
        }

        private static void TestQuotaNotificationRearm()
        {
            QuotaNotificationEvaluator evaluator =
                new QuotaNotificationEvaluator();
            int[] thresholds = new int[] { 25, 10, 5 };
            DateTime firstReset = new DateTime(
                2026,
                8,
                7,
                12,
                0,
                0,
                DateTimeKind.Utc);
            DateTime secondReset = firstReset.AddDays(7);

            Assert(
                evaluator.Evaluate(new UsageSnapshot(), thresholds) == null,
                "empty data generated a quota warning");
            Assert(
                evaluator.Evaluate(
                    NotificationSnapshot(
                        NotificationWindow("codex", "Weekly", 30, firstReset)),
                    thresholds) == null,
                "first non-empty observation generated a quota warning");
            QuotaNotificationDecision ten = evaluator.Evaluate(
                NotificationSnapshot(
                    NotificationWindow("codex", "Weekly", 9, firstReset)),
                thresholds);
            Assert(
                ten != null && ten.ThresholdPercent == 10,
                "a fall through the 10 percent threshold was missed");
            Assert(
                evaluator.Evaluate(
                    NotificationSnapshot(
                        NotificationWindow("codex", "Weekly", 8, firstReset)),
                    thresholds) == null,
                "continued use below a threshold repeated its warning");
            Assert(
                evaluator.Evaluate(
                    NotificationSnapshot(
                        NotificationWindow("codex", "Weekly", 60, firstReset)),
                    thresholds) == null,
                "a quota rise generated a warning");
            QuotaNotificationDecision twentyFive = evaluator.Evaluate(
                NotificationSnapshot(
                    NotificationWindow("codex", "Weekly", 24, firstReset)),
                thresholds);
            Assert(
                twentyFive != null && twentyFive.ThresholdPercent == 25,
                "a quota rise did not re-arm the warning threshold");
            Assert(
                evaluator.Evaluate(
                    NotificationSnapshot(
                        NotificationWindow("codex", "Weekly", 4, secondReset)),
                    thresholds) == null,
                "a changed reset window reused the previous window baseline");
            Assert(
                evaluator.Evaluate(
                    NotificationSnapshot(
                        NotificationWindow("codex", "Weekly", 70, secondReset)),
                    thresholds) == null,
                "a reset-window quota rise generated a warning");
            QuotaNotificationDecision five = evaluator.Evaluate(
                NotificationSnapshot(
                    NotificationWindow("codex", "Weekly", 4, secondReset)),
                thresholds);
            Assert(
                five != null && five.ThresholdPercent == 5,
                "the changed reset window did not re-arm its thresholds");
        }

        private static UsageSnapshot NotificationSnapshot(
            params UsageWindow[] windows)
        {
            UsageSnapshot snapshot = new UsageSnapshot();
            snapshot.ObservedAtUtc = DateTime.UtcNow;
            foreach (UsageWindow window in windows)
            {
                snapshot.Windows.Add(window);
            }
            return snapshot;
        }

        private static UsageWindow NotificationWindow(
            string limitId,
            string label,
            int remainingPercent,
            DateTime resetUtc)
        {
            return new UsageWindow
            {
                LimitId = limitId,
                Kind = "secondary",
                Label = label,
                DurationMinutes = 10080,
                UsedPercent = 100 - remainingPercent,
                ResetsAtUtc = resetUtc
            };
        }

        private static void TestStartupRegistration()
        {
            string command;
            string error;
            Assert(
                StartupRegistration.TryBuildCommand(
                    @"C:\Program Files\UsageApp\UsageApp.Native.exe",
                    out command,
                    out error)
                    && command
                        == "\"C:\\Program Files\\UsageApp\\UsageApp.Native.exe\" --background"
                    && error == null,
                "the startup command was not quoted safely");
            Assert(
                !StartupRegistration.TryBuildCommand(
                    @"UsageApp.Native.exe",
                    out command,
                    out error),
                "a relative startup path was accepted");

            MemoryStartupRegistry memory = new MemoryStartupRegistry();
            StartupRegistration registration =
                new StartupRegistration(memory);
            bool enabled;
            Assert(
                registration.TryIsEnabled(
                    @"C:\Apps\UsageApp.Native.exe",
                    out enabled,
                    out error)
                    && !enabled,
                "a missing startup entry was reported enabled");
            Assert(
                registration.TrySetEnabled(
                    @"C:\Apps\UsageApp.Native.exe",
                    true,
                    out error)
                    && memory.Value
                        == "\"C:\\Apps\\UsageApp.Native.exe\" --background",
                "enabling startup did not write the expected command");
            Assert(
                registration.TryIsEnabled(
                    @"C:\Apps\UsageApp.Native.exe",
                    out enabled,
                    out error)
                    && enabled,
                "the registered command was not recognized");
            Assert(
                registration.TrySetEnabled(
                    @"C:\Apps\UsageApp.Native.exe",
                    false,
                    out error)
                    && memory.Value == null,
                "disabling startup did not remove UsageApp's command");

            memory.Value = @"C:\Other\unrelated.exe --background";
            Assert(
                !registration.TrySetEnabled(
                    @"C:\Apps\UsageApp.Native.exe",
                    false,
                    out error)
                    && memory.Value == @"C:\Other\unrelated.exe --background"
                    && !string.IsNullOrEmpty(error),
                "a foreign startup command was deleted or silently accepted");

            memory.Value =
                "\"C:\\Old\\UsageApp.Native.exe\" --background";
            Assert(
                registration.TryIsEnabled(
                    @"C:\Apps\UsageApp.Native.exe",
                    out enabled,
                    out error)
                    && enabled
                    && memory.Value
                        == "\"C:\\Apps\\UsageApp.Native.exe\" --background",
                "an older UsageApp Native startup path was not repaired during inspection");
        }

        private static void TestClaudeStatusLine()
        {
            DateTime observed = new DateTime(
                2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);
            ClaudeQuotaSnapshot snapshot = ClaudeStatusLine.Normalize(
                "{\"rate_limits\":{\"five_hour\":{\"used_percentage\":3},\"seven_day\":{\"used_percentage\":31,\"resets_at\":1786035540}}}",
                observed,
                observed.AddMinutes(1));
            Assert(snapshot.Status == "live", "fresh Claude quota was not live");
            Assert(snapshot.Windows.Count == 2, "Claude quota windows were not collected");
            Assert(
                snapshot.Windows[0].Label == "5-hour"
                    && snapshot.Windows[0].RemainingPercent == 97
                    && !snapshot.Windows[0].ResetsAtUtc.HasValue,
                "five-hour Claude quota did not preserve a missing reset time");
            Assert(
                snapshot.Windows[1].Label == "Weekly"
                    && snapshot.Windows[1].RemainingPercent == 69
                    && snapshot.Windows[1].ResetsAtUtc.HasValue,
                "weekly Claude quota did not normalize supplied values");
            Assert(
                snapshot.PreferredWindow != null
                    && snapshot.PreferredWindow.Label == "Weekly",
                "Claude preferred window was not the most constrained limit");

            ClaudeQuotaSnapshot stale = ClaudeStatusLine.Normalize(
                "{\"rate_limits\":{\"five_hour\":{\"used_percentage\":3}}}",
                observed,
                observed.AddMinutes(16));
            Assert(
                stale.Status == "stale"
                    && !string.IsNullOrEmpty(stale.Message),
                "an old Claude status-line update was not marked stale");

            ClaudeQuotaSnapshot invalid = ClaudeStatusLine.Normalize(
                "{\"rate_limits\":{\"five_hour\":{\"used_percentage\":101}}}",
                observed,
                observed);
            Assert(
                invalid.Status == "unavailable" && invalid.Windows.Count == 0,
                "invalid Claude quota input created a usable window");
        }

        private static void TestClaudeFreshnessTransitions()
        {
            DateTime observed = new DateTime(
                2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);
            ClaudeQuotaSnapshot snapshot = ClaudeStatusLine.Normalize(
                "{\"rate_limits\":{\"five_hour\":{\"used_percentage\":3}}}",
                observed,
                observed.AddMinutes(1));
            Assert(snapshot.Status == "live",
                "freshness transition did not start live");
            Assert(ClaudeStatusLine.RefreshFreshness(
                    snapshot,
                    observed.AddMinutes(16))
                    && snapshot.Status == "stale",
                "a live Claude snapshot did not transition to stale while running");
            Assert(!ClaudeStatusLine.RefreshFreshness(
                    snapshot,
                    observed.AddMinutes(17)),
                "an unchanged stale snapshot reported another transition");

            long resetEpoch = (long)(observed.AddMinutes(2)
                - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                .TotalSeconds;
            ClaudeQuotaSnapshot resetSnapshot = ClaudeStatusLine.Normalize(
                "{\"rate_limits\":{\"seven_day\":{\"used_percentage\":31,\"resets_at\":"
                    + resetEpoch.ToString(CultureInfo.InvariantCulture)
                    + "}}}",
                observed,
                observed);
            Assert(resetSnapshot.Status == "live",
                "future-reset Claude quota did not start live");
            Assert(ClaudeStatusLine.RefreshFreshness(
                    resetSnapshot,
                    observed.AddMinutes(3))
                    && resetSnapshot.Status == "stale"
                    && resetSnapshot.Message.IndexOf(
                        "reset time has passed",
                        StringComparison.OrdinalIgnoreCase) >= 0,
                "a passed Claude reset did not invalidate last-known quota");
        }

        private static void TestClaudeSnapshotSelection()
        {
            DateTime baseline = new DateTime(
                2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);
            ClaudeQuotaSnapshot current = ClaudeStatusLine.Normalize(
                "{\"rate_limits\":{\"five_hour\":{\"used_percentage\":20}}}",
                baseline.AddMinutes(2),
                baseline.AddMinutes(2));
            ClaudeQuotaSnapshot older = ClaudeStatusLine.Normalize(
                "{\"rate_limits\":{\"five_hour\":{\"used_percentage\":90}}}",
                baseline.AddMinutes(1),
                baseline.AddMinutes(2));
            ClaudeQuotaSnapshot selected = ClaudeStatusLine.SelectAcceptedSnapshot(
                current,
                older,
                baseline.AddMinutes(2));
            Assert(object.ReferenceEquals(selected, current)
                    && selected.PreferredWindow.RemainingPercent == 80,
                "an older concurrent Claude callback replaced newer quota");

            ClaudeQuotaSnapshot empty = ClaudeStatusLine.Normalize(
                "{\"rate_limits\":{}}",
                baseline.AddMinutes(3),
                baseline.AddMinutes(3));
            selected = ClaudeStatusLine.SelectAcceptedSnapshot(
                current,
                empty,
                baseline.AddMinutes(3));
            Assert(object.ReferenceEquals(selected, current)
                    && selected.Windows.Count == 1
                    && selected.Status == "stale"
                    && selected.Message.IndexOf(
                        "last known quota",
                        StringComparison.OrdinalIgnoreCase) >= 0,
                "an empty Claude callback discarded usable last-known quota");

            ClaudeQuotaSnapshot newer = ClaudeStatusLine.Normalize(
                "{\"rate_limits\":{\"five_hour\":{\"used_percentage\":40}}}",
                baseline.AddMinutes(4),
                baseline.AddMinutes(4));
            selected = ClaudeStatusLine.SelectAcceptedSnapshot(
                current,
                newer,
                baseline.AddMinutes(4));
            Assert(object.ReferenceEquals(selected, newer)
                    && selected.Status == "live"
                    && selected.PreferredWindow.RemainingPercent == 60,
                "a newer valid Claude callback was not accepted");
        }

        private static void TestClaudeReceiverToken()
        {
            string valid = "abcDEF012_-abcDEF012_-abcDEF012_-12";
            Assert(ClaudeStatusLineReceiver.IsValidPathToken(valid),
                "receiver accepts a long URL-safe token");
            Assert(!ClaudeStatusLineReceiver.IsValidPathToken("too-short"),
                "receiver rejects short tokens");
            Assert(!ClaudeStatusLineReceiver.IsValidPathToken(
                "abcDEF012_-abcDEF012_-abcDEF012_+12"),
                "receiver rejects unsafe path characters");
            using (ClaudeStatusLineReceiver receiver =
                new ClaudeStatusLineReceiver(valid))
            {
                Assert(receiver.StatusLineEndpoint == null,
                    "endpoint remains unavailable until local receiver starts");
            }
        }

        private static void TestClaudeSnapshotRoundTrip()
        {
            DateTime observed = new DateTime(
                2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);
            ClaudeQuotaSnapshot original = ClaudeStatusLine.Normalize(
                "{\"rate_limits\":{\"five_hour\":{\"used_percentage\":3},\"seven_day\":{\"used_percentage\":31,\"resets_at\":1786035540}}}",
                observed, observed);
            ClaudeQuotaSnapshot roundTrip = ClaudeSnapshotStore.Deserialize(
                ClaudeSnapshotStore.Serialize(original), observed.AddMinutes(16));
            Assert(roundTrip != null && roundTrip.Windows.Count == 2,
                "normalized Claude cache did not restore quota windows");
            Assert(roundTrip.Status == "stale"
                && roundTrip.Windows[0].RemainingPercent == 97
                && roundTrip.Windows[1].RemainingPercent == 69,
                "Claude cache did not re-evaluate freshness or preserve usage");
            Assert(ClaudeSnapshotStore.Deserialize("{\"schemaVersion\":1,\"windows\":[]}", observed) == null,
                "an invalid Claude cache should not create a snapshot");
        }

        private static void TestClaudeIntegrationConfiguration()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "UsageAppNative-ClaudeConfig-" + Guid.NewGuid().ToString("N"));
            try
            {
                string profile = Path.Combine(root, "profile");
                string custom = Path.Combine(root, "custom profile");
                Assert(
                    ClaudeStatusLineIntegration.ResolveSettingsPathForTest(
                        null,
                        profile)
                        == Path.GetFullPath(Path.Combine(
                            profile,
                            ".claude",
                            "settings.json")),
                    "default Claude settings path did not use the user profile");
                Assert(
                    ClaudeStatusLineIntegration.ResolveSettingsPathForTest(
                        "\"" + custom + "\"",
                        profile)
                        == Path.GetFullPath(Path.Combine(
                            custom,
                            "settings.json")),
                    "CLAUDE_CONFIG_DIR was not honored as a configuration directory");
                Assert(
                    ClaudeStatusLineIntegration.ResolveSettingsPathForTest(
                        "~\\.claude-work",
                        profile)
                        == Path.GetFullPath(Path.Combine(
                            profile,
                            ".claude-work",
                            "settings.json")),
                    "a home-relative CLAUDE_CONFIG_DIR did not resolve through the user profile");
                Assert(
                    ClaudeStatusLineIntegration.ResolveSettingsPathForTest(
                        new string('x', 40000),
                        profile) == null,
                    "an invalid CLAUDE_CONFIG_DIR silently selected another profile");

                JavaScriptSerializer json = new JavaScriptSerializer();
                object first = json.DeserializeObject(
                    "{\"type\":\"command\",\"refreshInterval\":5,\"nested\":{\"one\":1,\"two\":[1,2]}}");
                object reordered = json.DeserializeObject(
                    "{\"nested\":{\"two\":[1.0,2.0],\"one\":1.0},\"refreshInterval\":5.0,\"type\":\"command\"}");
                object changedArray = json.DeserializeObject(
                    "{\"nested\":{\"two\":[2,1],\"one\":1},\"refreshInterval\":5,\"type\":\"command\"}");
                Assert(
                    ClaudeStatusLineIntegration.SemanticJsonEqualsForTest(
                        first,
                        reordered),
                    "semantic Claude settings equality depended on JSON property order or numeric representation");
                Assert(
                    !ClaudeStatusLineIntegration.SemanticJsonEqualsForTest(
                        first,
                        changedArray),
                    "semantic Claude settings equality ignored array order");
            }
            finally
            {
                DeleteTestDirectory(root);
            }
        }

        private static void TestClaudeIntegrationTransaction()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "UsageAppNative-ClaudeTransaction-" + Guid.NewGuid().ToString("N"));
            ClaudeStatusLineReceiver receiver = null;
            ClaudeStatusLineReceiver rollbackReceiver = null;
            try
            {
                string stateDirectory = Path.Combine(root, "state");
                string configDirectory = Path.Combine(root, "config");
                string settingsPath = Path.Combine(configDirectory, "settings.json");
                Directory.CreateDirectory(configDirectory);
                File.WriteAllText(
                    settingsPath,
                    "{\"theme\":\"dark\",\"statusLine\":{\"refreshInterval\":7,\"command\":\"Write-Output prior\",\"type\":\"command\"}}",
                    new UTF8Encoding(false));

                receiver = new ClaudeStatusLineReceiver(
                    ClaudeStatusLineReceiver.CreatePathToken(),
                    0);
                ClaudeStatusLineIntegration integration =
                    new ClaudeStatusLineIntegration(
                        receiver,
                        stateDirectory,
                        settingsPath);
                ClaudeIntegrationResult connected = integration.Connect();
                Assert(connected.Succeeded
                        && connected.Connected
                        && !connected.Conflict
                        && receiver.IsListening,
                    "Claude integration did not report a successful connection transaction");

                JavaScriptSerializer json = new JavaScriptSerializer();
                Dictionary<string, object> installedSettings =
                    json.DeserializeObject(File.ReadAllText(
                        settingsPath,
                        Encoding.UTF8)) as Dictionary<string, object>;
                object installedValue;
                Dictionary<string, object> installedStatusLine =
                    installedSettings != null
                        && installedSettings.TryGetValue(
                            "statusLine",
                            out installedValue)
                        ? installedValue as Dictionary<string, object>
                        : null;
                Assert(installedStatusLine != null,
                    "Claude connection did not install an object statusLine");
                List<string> keys = new List<string>(installedStatusLine.Keys);
                Dictionary<string, object> reordered =
                    new Dictionary<string, object>();
                for (int index = keys.Count - 1; index >= 0; index--)
                {
                    reordered[keys[index]] = installedStatusLine[keys[index]];
                }
                installedSettings["statusLine"] = reordered;
                File.WriteAllText(
                    settingsPath,
                    json.Serialize(installedSettings),
                    new UTF8Encoding(false));

                ClaudeIntegrationResult disconnected = integration.Disconnect();
                Assert(disconnected.Succeeded
                        && !disconnected.Connected
                        && !disconnected.Conflict
                        && !receiver.IsListening,
                    "Claude integration did not restore a semantically identical reordered setting");
                Dictionary<string, object> restoredSettings =
                    json.DeserializeObject(File.ReadAllText(
                        settingsPath,
                        Encoding.UTF8)) as Dictionary<string, object>;
                object restoredStatusLine;
                object expectedStatusLine = json.DeserializeObject(
                    "{\"refreshInterval\":7,\"command\":\"Write-Output prior\",\"type\":\"command\"}");
                Assert(restoredSettings != null
                        && restoredSettings.TryGetValue(
                            "statusLine",
                            out restoredStatusLine)
                        && ClaudeStatusLineIntegration.SemanticJsonEqualsForTest(
                            restoredStatusLine,
                            expectedStatusLine)
                        && !File.Exists(Path.Combine(
                            stateDirectory,
                            "claude-statusline-state.json")),
                    "Claude disconnect did not restore prior settings and clean its journal");

                string rollbackState = Path.Combine(root, "rollback-state");
                string blockedParent = Path.Combine(root, "blocked-parent");
                File.WriteAllText(
                    blockedParent,
                    "not a directory",
                    new UTF8Encoding(false));
                rollbackReceiver = new ClaudeStatusLineReceiver(
                    ClaudeStatusLineReceiver.CreatePathToken(),
                    0);
                ClaudeStatusLineIntegration rollbackIntegration =
                    new ClaudeStatusLineIntegration(
                        rollbackReceiver,
                        rollbackState,
                        Path.Combine(blockedParent, "settings.json"));
                ClaudeIntegrationResult rolledBack =
                    rollbackIntegration.Connect();
                Assert(!rolledBack.Succeeded
                        && !rolledBack.Connected
                        && !rolledBack.Conflict
                        && !rollbackReceiver.IsListening
                        && !File.Exists(Path.Combine(
                            rollbackState,
                            "claude-statusline-state.json"))
                        && !File.Exists(Path.Combine(
                            rollbackState,
                            "claude-statusline",
                            "statusline-wrapper.ps1")),
                    "a failed Claude settings write left a live receiver or partial integration artifacts");

                ClaudeIntegrationResult cleanNoOp =
                    rollbackIntegration.Disconnect();
                Assert(cleanNoOp.Succeeded
                        && !cleanNoOp.Connected
                        && !cleanNoOp.Conflict,
                    "disconnecting a clean non-integration was reported as a failure");
            }
            finally
            {
                if (receiver != null) receiver.Dispose();
                if (rollbackReceiver != null) rollbackReceiver.Dispose();
                DeleteTestDirectory(root);
            }
        }

        private static void TestClaudeReceiverStartFailure()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "UsageAppNative-ClaudePort-" + Guid.NewGuid().ToString("N"));
            ClaudeStatusLineReceiver occupied = null;
            ClaudeStatusLineReceiver blocked = null;
            try
            {
                occupied = new ClaudeStatusLineReceiver(
                    ClaudeStatusLineReceiver.CreatePathToken(),
                    0);
                occupied.Start();
                blocked = new ClaudeStatusLineReceiver(
                    ClaudeStatusLineReceiver.CreatePathToken(),
                    occupied.Port);
                ClaudeStatusLineIntegration integration =
                    new ClaudeStatusLineIntegration(
                        blocked,
                        Path.Combine(root, "state"),
                        Path.Combine(root, "config", "settings.json"));
                ClaudeIntegrationResult result = integration.Connect();
                Assert(!result.Succeeded
                        && !result.Connected
                        && !result.Conflict
                        && !blocked.IsListening
                        && !string.IsNullOrEmpty(result.Message)
                        && result.Message.IndexOf(
                            "receiver",
                            StringComparison.OrdinalIgnoreCase) >= 0,
                    "a Claude receiver port collision escaped or returned an ambiguous success");
            }
            finally
            {
                if (blocked != null) blocked.Dispose();
                if (occupied != null) occupied.Dispose();
                DeleteTestDirectory(root);
            }
        }

        private static void DeleteTestDirectory(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path)
                    && Directory.Exists(path)
                    && Path.GetFullPath(path).StartsWith(
                        Path.GetFullPath(Path.GetTempPath()),
                        StringComparison.OrdinalIgnoreCase))
                {
                    Directory.Delete(path, true);
                }
            }
            catch
            {
                // Test cleanup must not hide the assertion that produced it.
            }
        }

        private sealed class MemoryStartupRegistry : IStartupRegistry
        {
            public string Value { get; set; }

            public string ReadValue()
            {
                return Value;
            }

            public void WriteValue(string command)
            {
                Value = command;
            }

            public void DeleteValue()
            {
                Value = null;
            }
        }

        private static void TestDiagnosticFailureRecognition()
        {
            Assert(
                !Program.DiagnosticReportFailedForTest(
                    "status=passed" + Environment.NewLine),
                "a passing diagnostic report was marked failed");
            Assert(
                Program.DiagnosticReportFailedForTest(
                    "status=failed" + Environment.NewLine),
                "a failed diagnostic report would still exit successfully");
            Assert(
                Program.DiagnosticReportFailedForTest(
                    "status=error" + Environment.NewLine),
                "a read-error diagnostic report would still exit successfully");
            Assert(
                Program.DiagnosticReportFailedForTest(string.Empty),
                "an empty diagnostic report would still exit successfully");
        }

        private static void TestSnapshotRoundTrip()
        {
            UsageSnapshot source = Parse(
                "{\"rateLimits\":{\"limitId\":\"codex\",\"planType\":\"pro\","
                + "\"secondary\":{\"usedPercent\":51,\"windowDurationMins\":10080,\"resetsAt\":2000000000}},"
                + "\"rateLimitResetCredits\":{\"availableCount\":3,\"credits\":["
                + "{\"id\":\"one\",\"title\":\"Full reset\",\"description\":\"Reset description\","
                + "\"status\":\"available\",\"grantedAt\":100,\"expiresAt\":2000000100}]}}");
            source.TokenUsage = new TokenUsageSummary();
            source.TokenUsage.LifetimeTokens = 5000000000L;
            source.TokenUsage.PeakDailyTokens = 250000000L;
            source.TokenUsage.CurrentStreakDays = 4L;
            source.TokenUsage.DailyBuckets.Add(new TokenUsageDailyBucket
            {
                StartDate = "2026-07-30",
                Tokens = 4294967296L
            });
            source.TokenUsageObservedAtUtc = source.ObservedAtUtc;
            string serialized = SnapshotStore.Serialize(source);
            UsageSnapshot restored = SnapshotStore.Deserialize(serialized);
            Assert(restored != null, "cache did not deserialize");
            Assert(restored.Windows.Count == 1, "quota window was not preserved");
            Assert(restored.PreferredWindow.RemainingPercent == 49, "quota value changed");
            Assert(restored.BankedResets.AvailableCount == 3, "banked count changed");
            Assert(restored.BankedResets.Items.Count == 1, "banked detail changed");
            Assert(
                restored.BankedResets.Items[0].Description == "Reset description",
                "banked description changed");
            Assert(restored.TokenUsage != null, "token history was not preserved");
            Assert(
                restored.TokenUsage.LifetimeTokens == 5000000000L,
                "large lifetime token total changed");
            Assert(
                restored.TokenUsage.DailyBuckets.Count == 1
                    && restored.TokenUsage.DailyBuckets[0].Tokens == 4294967296L,
                "daily token history changed");
            Assert(
                restored.TokenUsageObservedAtUtc == source.TokenUsageObservedAtUtc,
                "token history freshness changed");
        }

        private static void TestSnapshotMerge()
        {
            UsageSnapshot cached = Parse(
                "{\"rateLimits\":{\"secondary\":{\"usedPercent\":60}},"
                + "\"rateLimitResetCredits\":{\"availableCount\":3,\"credits\":["
                + "{\"id\":\"one\",\"grantedAt\":100,\"expiresAt\":2000000100}]}}");
            UsageSnapshot current = Parse(
                "{\"rateLimits\":{\"secondary\":{\"usedPercent\":50}}}");
            UsageSnapshot merged = SnapshotStore.Merge(current, cached);
            Assert(merged.PreferredWindow.RemainingPercent == 50, "current quota was replaced");
            Assert(merged.BankedResets.AvailableCount == 3, "cached count was not retained");
            Assert(merged.BankedResets.Items.Count == 1, "cached detail was not retained");
            Assert(merged.BankedResets.DetailsAvailable, "cached details were not marked displayable");

            UsageSnapshot authoritativeZero = Parse(
                "{\"rateLimits\":{\"secondary\":{\"usedPercent\":50}},"
                + "\"rateLimitResetCredits\":{\"availableCount\":0,\"credits\":null}}");
            UsageSnapshot zeroMerged = SnapshotStore.Merge(authoritativeZero, cached);
            Assert(
                zeroMerged.BankedResets.AvailableCount == 0,
                "authoritative zero banked count was replaced");
            Assert(
                !zeroMerged.BankedResets.DetailsAvailable,
                "old expiry rows were presented beside an authoritative zero");
            Assert(
                zeroMerged.BankedResets.Items.Count == 0,
                "old expiry items survived an authoritative zero");

            cached.TokenUsage = new TokenUsageSummary();
            cached.TokenUsage.LifetimeTokens = 1000L;
            cached.TokenUsageObservedAtUtc = cached.ObservedAtUtc;
            UsageSnapshot isolatedCurrent = Parse(
                "{\"rateLimits\":{\"secondary\":{\"usedPercent\":45}}}");
            UsageSnapshot isolated = SnapshotStore.Merge(
                isolatedCurrent,
                cached,
                false);
            Assert(
                isolated.TokenUsage == null,
                "persisted token history crossed a new-session trust boundary");
        }

        private static UsageSnapshot Parse(string text)
        {
            JavaScriptSerializer json = new JavaScriptSerializer();
            return CodexAppServer.NormalizeForTest(json.DeserializeObject(text));
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
