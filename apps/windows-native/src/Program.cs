using System;
using System.Globalization;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace UsageApp.Native
{
    internal static class Program
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PrintWindow(
            IntPtr window,
            IntPtr targetDevice,
            uint flags);

        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length == 2
                && string.Equals(
                    args[0],
                    "--disconnect-claude-output",
                    StringComparison.OrdinalIgnoreCase))
            {
                RunClaudeDisconnect(args[1]);
                return;
            }
            if (args.Length == 2
                && string.Equals(args[0], "--probe-output", StringComparison.OrdinalIgnoreCase))
            {
                RunProbe(args[1]);
                return;
            }
            if (args.Length == 2
                && string.Equals(args[0], "--self-test-output", StringComparison.OrdinalIgnoreCase))
            {
                WriteDiagnosticReport(args[1], SelfTests.Run());
                return;
            }
            if (args.Length == 2
                && string.Equals(args[0], "--capture-output", StringComparison.OrdinalIgnoreCase))
            {
                RunCapture(args[1], false, false, false, false);
                return;
            }
            if (args.Length == 2
                && string.Equals(args[0], "--capture-demo-output", StringComparison.OrdinalIgnoreCase))
            {
                RunCapture(args[1], true, false, false, false);
                return;
            }
            if (args.Length == 2
                && string.Equals(args[0], "--capture-demo-bank-output", StringComparison.OrdinalIgnoreCase))
            {
                RunCapture(args[1], true, true, false, false);
                return;
            }
            if (args.Length == 2
                && string.Equals(args[0], "--capture-demo-settings-output", StringComparison.OrdinalIgnoreCase))
            {
                RunCapture(args[1], true, false, true, false);
                return;
            }
            if (args.Length == 2
                && string.Equals(
                    args[0],
                    "--capture-demo-settings-large-output",
                    StringComparison.OrdinalIgnoreCase))
            {
                RunCapture(args[1], true, false, true, true);
                return;
            }
            if (args.Length == 2
                && string.Equals(args[0], "--capture-demo-dashboard-output", StringComparison.OrdinalIgnoreCase))
            {
                RunDashboardCapture(args[1], true, false);
                return;
            }
            if (args.Length == 2
                && string.Equals(
                    args[0],
                    "--capture-demo-dashboard-window-output",
                    StringComparison.OrdinalIgnoreCase))
            {
                RunDashboardCapture(args[1], false, false);
                return;
            }
            if (args.Length == 2
                && string.Equals(
                    args[0],
                    "--capture-demo-dashboard-large-output",
                    StringComparison.OrdinalIgnoreCase))
            {
                RunDashboardCapture(args[1], false, true);
                return;
            }
            if (args.Length == 2
                && string.Equals(
                    args[0],
                    "--capture-demo-custom-range-output",
                    StringComparison.OrdinalIgnoreCase))
            {
                RunCustomRangeDialogCapture(args[1]);
                return;
            }
            if (args.Length == 2
                && string.Equals(args[0], "--layout-probe-output", StringComparison.OrdinalIgnoreCase))
            {
                RunLayoutProbe(args[1]);
                return;
            }
            if (args.Length == 2
                && string.Equals(args[0], "--picker-smoke-output", StringComparison.OrdinalIgnoreCase))
            {
                RunPickerSmoke(args[1]);
                return;
            }
            if (args.Length == 2
                && string.Equals(
                    args[0],
                    "--interaction-smoke-output",
                    StringComparison.OrdinalIgnoreCase))
            {
                RunInteractionSmoke(args[1]);
                return;
            }
            if (args.Length == 2
                && string.Equals(args[0], "--render-demo-output", StringComparison.OrdinalIgnoreCase))
            {
                RunOffscreenRender(args[1], false);
                return;
            }
            if (args.Length == 2
                && string.Equals(args[0], "--render-settings-output", StringComparison.OrdinalIgnoreCase))
            {
                RunOffscreenRender(args[1], true);
                return;
            }
            if (args.Length == 2
                && string.Equals(args[0], "--tray-preview-output", StringComparison.OrdinalIgnoreCase))
            {
                RunTrayPreview(args[1]);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool backgroundStart = args.Length == 1
                && string.Equals(
                    args[0],
                    "--background",
                    StringComparison.OrdinalIgnoreCase);
            bool created;
            using (EventWaitHandle showSignal = new EventWaitHandle(
                false,
                EventResetMode.AutoReset,
                @"Local\UsageApp.Native.Show"))
            using (Mutex instance = new Mutex(
                    true,
                    @"Local\UsageApp.Native.Windows",
                    out created))
            {
                if (!created)
                {
                    if (!backgroundStart)
                    {
                        showSignal.Set();
                    }
                    return;
                }

                bool showOnStart = args.Length == 1
                    && string.Equals(args[0], "--show", StringComparison.OrdinalIgnoreCase);
                using (TrayApplicationContext context = new TrayApplicationContext(
                    showOnStart,
                    showSignal))
                {
                    Application.Run(context);
                }
                GC.KeepAlive(instance);
            }
        }

        private static void RunClaudeDisconnect(string outputPath)
        {
            StringBuilder report = new StringBuilder();
            string savedToken = ClaudeStatusLineIntegration.LoadSavedToken();
            string token = ClaudeStatusLineReceiver.IsValidPathToken(savedToken)
                ? savedToken
                : ClaudeStatusLineReceiver.CreatePathToken();
            try
            {
                using (ClaudeStatusLineReceiver receiver =
                    new ClaudeStatusLineReceiver(
                        token,
                        ClaudeStatusLineIntegration.DefaultPort))
                {
                    ClaudeStatusLineIntegration integration =
                        new ClaudeStatusLineIntegration(receiver);
                    ClaudeIntegrationResult result = integration.Disconnect();
                    report.AppendLine(result.Conflict
                        ? "status=conflict"
                        : !result.Succeeded
                            ? "status=failed"
                            : result.Connected
                                ? "status=connected"
                                : "status=disconnected");
                    report.AppendLine("message=" + (result.Message ?? string.Empty));
                    Environment.ExitCode = result.Succeeded ? 0 : 2;
                }
            }
            catch (Exception error)
            {
                report.AppendLine("status=failed");
                report.AppendLine("message=" + error.Message);
                Environment.ExitCode = 1;
            }
            WriteDiagnosticReport(outputPath, report.ToString());
        }

        private static void RunProbe(string outputPath)
        {
            StringBuilder result = new StringBuilder();
            try
            {
                using (CodexAppServer client = new CodexAppServer())
                {
                    UsageSnapshot snapshot = client.ReadRateLimits();
                    result.AppendLine("status=live");
                    result.AppendLine(
                        "observedUtc="
                        + snapshot.ObservedAtUtc.ToString("o", CultureInfo.InvariantCulture));
                    result.AppendLine("windowCount=" + snapshot.Windows.Count);
                    result.AppendLine(
                        "bankedAvailable="
                        + (snapshot.BankedResets.AvailableCount.HasValue
                            ? snapshot.BankedResets.AvailableCount.Value.ToString(
                                CultureInfo.InvariantCulture)
                            : "unavailable"));
                    result.AppendLine(
                        "bankedDetailCount=" + snapshot.BankedResets.Items.Count);
                    result.AppendLine(
                        "bankedDetailsAvailable="
                        + snapshot.BankedResets.DetailsAvailable.ToString(
                            CultureInfo.InvariantCulture));
                    result.AppendLine(
                        "tokenHistoryAvailable="
                        + (snapshot.TokenUsage != null).ToString(
                            CultureInfo.InvariantCulture));
                    result.AppendLine(
                        "tokenDailyCount="
                        + (snapshot.TokenUsage == null
                            ? "0"
                            : snapshot.TokenUsage.DailyBuckets.Count.ToString(
                                CultureInfo.InvariantCulture)));
                    result.AppendLine(
                        "lifetimeTokens="
                        + (snapshot.TokenUsage == null
                            || !snapshot.TokenUsage.LifetimeTokens.HasValue
                                ? "unavailable"
                                : snapshot.TokenUsage.LifetimeTokens.Value.ToString(
                                    CultureInfo.InvariantCulture)));
                    UsageWindow preferred = snapshot.PreferredWindow;
                    result.AppendLine(
                        "trayRemaining="
                        + (preferred == null
                            ? "unavailable"
                            : preferred.RemainingPercent.ToString(CultureInfo.InvariantCulture)));
                    foreach (UsageWindow window in snapshot.Windows)
                    {
                        result.AppendLine(
                            string.Format(
                                CultureInfo.InvariantCulture,
                                "window={0}|remaining={1}|resetUtc={2}",
                                window.Label,
                                window.RemainingPercent,
                                window.ResetsAtUtc.HasValue
                                    ? window.ResetsAtUtc.Value.ToString(
                                        "o",
                                        CultureInfo.InvariantCulture)
                                    : "unavailable"));
                    }
                }
            }
            catch (Exception error)
            {
                result.AppendLine("status=error");
                result.AppendLine("message=" + error.Message.Replace("\r", " ").Replace("\n", " "));
            }

            WriteDiagnosticReport(outputPath, result.ToString());
        }

        private static void RunCapture(
            string outputPath,
            bool demo,
            bool scrollToBanked,
            bool settings,
            bool largeSettings)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            NativeSettings captureSettings = new NativeSettings();
            if (largeSettings)
            {
                captureSettings.FlyoutTextScale = 125;
            }
            if (settings)
            {
                captureSettings.CodexQuotaNotificationsEnabled = true;
                captureSettings.CodexQuotaNotificationThresholdsCsv = "42,17";
            }
            using (UsageFlyout flyout = new UsageFlyout(true, captureSettings))
            {
                try
                {
                    if (demo)
                    {
                        flyout.ShowSnapshot(DemoSnapshot());
                    }
                    else
                    {
                        using (CodexAppServer client = new CodexAppServer())
                        {
                            flyout.ShowSnapshot(client.ReadRateLimits());
                        }
                    }
                }
                catch (Exception error)
                {
                    MarkDiagnosticFailure();
                    flyout.ShowError(error.Message, null);
                }

                flyout.Location = new Point(40, 40);
                flyout.Show();
                flyout.Activate();
                flyout.BringToFront();
                Application.DoEvents();
                Thread.Sleep(250);
                Application.DoEvents();
                if (scrollToBanked)
                {
                    flyout.ScrollToBankedForCapture();
                    Application.DoEvents();
                }
                if (settings)
                {
                    flyout.ShowSettingsForCapture();
                    Application.DoEvents();
                }
                SaveWindowImage(flyout, outputPath);
                flyout.Dispose();
            }
        }

        private static void RunDashboardCapture(
            string outputPath,
            bool maximize,
            bool largeText)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            NativeSettings dashboardSettings = new NativeSettings();
            if (largeText)
            {
                dashboardSettings.FlyoutTextScale = 125;
            }
            using (NativeDashboardForm dashboard = new NativeDashboardForm(
                dashboardSettings))
            {
                dashboard.ShowSnapshot(DemoSnapshot(), false);
                if (maximize)
                {
                    dashboard.ShowDashboard();
                }
                else
                {
                    dashboard.Show();
                    dashboard.Activate();
                    dashboard.BringToFront();
                }
                Application.DoEvents();
                Thread.Sleep(250);
                Application.DoEvents();
                SaveWindowImage(dashboard, outputPath);
                dashboard.Hide();
            }
        }

        private static void RunCustomRangeDialogCapture(string outputPath)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool captured = false;
            using (NativeDashboardForm dashboard = new NativeDashboardForm(
                new NativeSettings()))
            using (System.Windows.Forms.Timer captureTimer =
                new System.Windows.Forms.Timer())
            {
                dashboard.ShowSnapshot(DemoSnapshot(), false);
                dashboard.Show();
                CreateHandles(dashboard);
                Application.DoEvents();
                int ticks = 0;
                captureTimer.Interval = 50;
                captureTimer.Tick += delegate
                {
                    ticks++;
                    Form dialog = FindOpenFormByAccessibleName(
                        "Custom activity date range");
                    if (dialog != null)
                    {
                        Application.DoEvents();
                        SaveWindowImage(dialog, outputPath);
                        captured = true;
                        dialog.DialogResult = DialogResult.Cancel;
                        dialog.Close();
                        captureTimer.Stop();
                    }
                    else if (ticks >= 100)
                    {
                        captureTimer.Stop();
                    }
                };
                captureTimer.Start();
                PerformButtonClick(FindButtonByText(dashboard, "Custom"));
                captureTimer.Stop();
                dashboard.Hide();
            }
            if (!captured)
            {
                MarkDiagnosticFailure();
            }
        }

        private static void SaveWindowImage(Form form, string outputPath)
        {
            using (Bitmap image = new Bitmap(
                Math.Max(1, form.Width),
                Math.Max(1, form.Height),
                PixelFormat.Format24bppRgb))
            using (Graphics graphics = Graphics.FromImage(image))
            {
                IntPtr device = graphics.GetHdc();
                bool captured;
                try
                {
                    captured = PrintWindow(form.Handle, device, 2);
                }
                finally
                {
                    graphics.ReleaseHdc(device);
                }
                if (!captured || !HasCapturedPixels(image))
                {
                    form.DrawToBitmap(
                        image,
                        new Rectangle(Point.Empty, form.Size));
                }
                image.Save(outputPath, ImageFormat.Png);
            }
        }

        private static bool HasCapturedPixels(Bitmap image)
        {
            int stepX = Math.Max(1, image.Width / 32);
            int stepY = Math.Max(1, image.Height / 24);
            for (int y = 0; y < image.Height; y += stepY)
            {
                for (int x = 0; x < image.Width; x += stepX)
                {
                    Color pixel = image.GetPixel(x, y);
                    if (pixel.R > 8 || pixel.G > 8 || pixel.B > 8)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static void RunPickerSmoke(string outputPath)
        {
            Application.SetUnhandledExceptionMode(
                UnhandledExceptionMode.CatchException);
            Exception callbackError = null;
            ThreadExceptionEventHandler threadException = delegate(
                object sender,
                ThreadExceptionEventArgs eventArgs)
            {
                callbackError = eventArgs.Exception;
            };
            Application.ThreadException += threadException;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            StringBuilder result = new StringBuilder();
            try
            {
                using (Form host = new Form())
                {
                    host.StartPosition = FormStartPosition.Manual;
                    host.Location = new Point(40, 40);
                    host.Size = new Size(320, 180);
                    ChoicePicker picker = new ChoicePicker();
                    picker.Location = new Point(30, 30);
                    picker.Size = new Size(220, 38);
                    picker.Options = new string[]
                    {
                        "First",
                        "Second",
                        "Third"
                    };
                    int changed = 0;
                    picker.SelectedIndexChanged += delegate
                    {
                        changed++;
                    };
                    host.Controls.Add(picker);
                    host.Show();
                    Application.DoEvents();

                    picker.ShowChoicesForTest();
                    Application.DoEvents();
                    bool opened = picker.IsDropDownOpen;
                    picker.ClickChoiceForTest(2);
                    Application.DoEvents();
                    bool selected = picker.SelectedIndex == 2
                        && changed == 1
                        && !picker.IsDropDownOpen;

                    picker.ShowChoicesForTest();
                    Application.DoEvents();
                    bool reopened = picker.IsDropDownOpen;
                    host.Dispose();
                    Application.DoEvents();

                    bool passed = opened
                        && selected
                        && reopened
                        && callbackError == null;
                    result.AppendLine(passed ? "status=passed" : "status=failed");
                    result.AppendLine("opened=" + opened);
                    result.AppendLine("selected=" + selected);
                    result.AppendLine("reopened=" + reopened);
                    if (callbackError != null)
                    {
                        result.AppendLine(
                            "callback="
                                + callbackError.ToString()
                                    .Replace("\r", " ")
                                    .Replace("\n", " "));
                    }
                }
            }
            catch (Exception error)
            {
                result.AppendLine("status=failed");
                result.AppendLine(
                    "message="
                        + error.Message.Replace("\r", " ").Replace("\n", " "));
            }
            finally
            {
                Application.ThreadException -= threadException;
            }
            WriteDiagnosticReport(outputPath, result.ToString());
        }

        private static void RunInteractionSmoke(string outputPath)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            StringBuilder result = new StringBuilder();
            string stage = "start";
            try
            {
                bool flyoutClaude;
                bool flyoutCodex;
                bool flyoutSettings;
                bool flyoutNotificationSettings;
                bool flyoutStartupSetting;
                bool flyoutRefreshStayedVisible;
                bool flyoutPinToggle;
                bool flyoutSingleSelectSetting;
                bool providerVisibilitySettings;
                bool codexOnlyProviderSettings = false;
                bool claudeOnlyProviderSettings = false;
                bool restoredProviderSettings = false;
                bool dashboardRequested = false;
                bool refreshRequested = false;
                NativeSettings nativeFlyoutSettings = new NativeSettings();
                using (UsageFlyout flyout = new UsageFlyout(
                    true,
                    nativeFlyoutSettings))
                {
                    flyout.DashboardRequested += delegate
                    {
                        dashboardRequested = true;
                    };
                    flyout.RefreshRequested += delegate
                    {
                        refreshRequested = true;
                    };
                    flyout.ShowSnapshot(DemoSnapshot());
                    CreateHandles(flyout);
                    flyout.Show();
                    Application.DoEvents();

                    stage = "flyout pin";
                    Control pin = FindControlByAccessibleName(
                        flyout,
                        "Keep taskbar popup open");
                    PerformButtonClick(pin);
                    Application.DoEvents();
                    flyoutPinToggle = string.Equals(
                        pin.Text,
                        "Pinned",
                        StringComparison.Ordinal)
                        && flyout.TopMost
                        && string.Equals(
                            pin.AccessibleName,
                            "Unpin taskbar popup",
                            StringComparison.Ordinal);
                    using (Form otherApplication = new Form())
                    {
                        otherApplication.Text = "Pin behavior test";
                        otherApplication.Show();
                        otherApplication.Activate();
                        Application.DoEvents();
                        Thread.Sleep(180);
                        Application.DoEvents();
                        flyoutPinToggle = flyoutPinToggle
                            && flyout.Visible
                            && flyout.TopMost;
                        otherApplication.Hide();
                    }
                    flyout.Activate();
                    Application.DoEvents();
                    PerformButtonClick(pin);
                    Application.DoEvents();
                    flyoutPinToggle = flyoutPinToggle && !flyout.TopMost;

                    stage = "flyout Claude";
                    Control claude = FindControlByAccessibleName(
                        flyout,
                        "Claude native beta information");
                    PerformButtonClick(claude);
                    Application.DoEvents();
                    flyoutClaude = ContainsControlText(
                        flyout,
                        "NOT CONNECTED");

                    stage = "flyout Codex";
                    Control codex = FindControlByAccessibleName(
                        flyout,
                        "Codex provider");
                    PerformButtonClick(codex);
                    Application.DoEvents();
                    flyoutCodex = ContainsControlText(
                        flyout,
                        "Usage windows");

                    stage = "flyout refresh";
                    PerformButtonClick(FindControlByAccessibleName(
                        flyout,
                        "Refresh Codex usage"));
                    Application.DoEvents();
                    flyoutRefreshStayedVisible = flyout.Visible;
                    if (!flyout.Visible)
                    {
                        // Keep exercising the remaining controls, but retain
                        // the failed visibility assertion in the final result.
                        flyout.Show();
                        Application.DoEvents();
                    }

                    stage = "flyout settings";
                    Control settings = FindControlByAccessibleName(
                        flyout,
                        "Settings");
                    PerformButtonClick(settings);
                    Application.DoEvents();
                    flyoutSettings = ContainsControlText(
                        flyout,
                        "Native preferences");

                    stage = "hide Claude provider";
                    CheckBox showClaude = FindControlByAccessibleName(
                        flyout,
                        "Show Claude provider") as CheckBox;
                    if (showClaude == null)
                    {
                        throw new InvalidOperationException(
                            "The Claude provider toggle was not found.");
                    }
                    showClaude.Checked = false;
                    Application.DoEvents();
                    CheckBox showCodex = FindControlByAccessibleName(
                        flyout,
                        "Show Codex provider") as CheckBox;
                    CheckBox claudeTray = FindControlByAccessibleName(
                        flyout,
                        "Show Claude taskbar icon") as CheckBox;
                    Control claudeProviderButton = FindControlByAccessibleName(
                        flyout,
                        "Claude native beta information");
                    codexOnlyProviderSettings = !nativeFlyoutSettings.ShowClaudeProvider
                        && nativeFlyoutSettings.ShowCodexProvider
                        && !nativeFlyoutSettings.ShowClaudeTrayIcon
                        && showCodex != null
                        && !showCodex.Enabled
                        && claudeTray != null
                        && !claudeTray.Enabled
                        && claudeProviderButton != null
                        && !claudeProviderButton.Visible
                        && flyout.ProviderButtonsFillAvailableSpaceForTest();

                    stage = "show Claude and use its taskbar icon";
                    showClaude = FindControlByAccessibleName(
                        flyout,
                        "Show Claude provider") as CheckBox;
                    showClaude.Checked = true;
                    Application.DoEvents();
                    claudeTray = FindControlByAccessibleName(
                        flyout,
                        "Show Claude taskbar icon") as CheckBox;
                    claudeTray.Checked = true;
                    Application.DoEvents();
                    showCodex = FindControlByAccessibleName(
                        flyout,
                        "Show Codex provider") as CheckBox;
                    showCodex.Checked = false;
                    Application.DoEvents();
                    Control codexProviderButton = FindControlByAccessibleName(
                        flyout,
                        "Codex provider");
                    claudeOnlyProviderSettings = !nativeFlyoutSettings.ShowCodexProvider
                        && nativeFlyoutSettings.ShowClaudeProvider
                        && !nativeFlyoutSettings.ShowCodexTrayIcon
                        && nativeFlyoutSettings.ShowClaudeTrayIcon
                        && codexProviderButton != null
                        && !codexProviderButton.Visible;

                    stage = "restore both providers";
                    showCodex = FindControlByAccessibleName(
                        flyout,
                        "Show Codex provider") as CheckBox;
                    showCodex.Checked = true;
                    Application.DoEvents();
                    CheckBox codexTray = FindControlByAccessibleName(
                        flyout,
                        "Show Codex taskbar icon") as CheckBox;
                    codexTray.Checked = true;
                    Application.DoEvents();
                    restoredProviderSettings = nativeFlyoutSettings.ShowCodexProvider
                        && nativeFlyoutSettings.ShowClaudeProvider
                        && nativeFlyoutSettings.ShowCodexTrayIcon
                        && nativeFlyoutSettings.ShowClaudeTrayIcon;
                    providerVisibilitySettings = codexOnlyProviderSettings
                        && claudeOnlyProviderSettings
                        && restoredProviderSettings;

                    stage = "choose app text size once";
                    ChoicePicker textSize = FindControlByAccessibleName(
                        flyout,
                        "App text size") as ChoicePicker;
                    if (textSize == null)
                    {
                        throw new InvalidOperationException(
                            "The App text size picker was not found.");
                    }
                    textSize.ShowChoicesForTest();
                    Application.DoEvents();
                    textSize.ClickChoiceForTest(1);
                    Application.DoEvents();
                    flyoutSingleSelectSetting =
                        nativeFlyoutSettings.FlyoutTextScale == 110;

                    stage = "enable quota notifications";
                    CheckBox alerts = FindControlByAccessibleName(
                        flyout,
                        "Codex usage alerts") as CheckBox;
                    if (alerts == null)
                    {
                        throw new InvalidOperationException(
                            "The Codex usage alerts toggle was not found.");
                    }
                    alerts.Checked = true;
                    Application.DoEvents();

                    stage = "choose custom notification preset";
                    ChoicePicker preset = FindControlByAccessibleName(
                        flyout,
                        "Notification preset") as ChoicePicker;
                    if (preset == null)
                    {
                        throw new InvalidOperationException(
                            "The notification preset picker was not found.");
                    }
                    preset.ShowChoicesForTest();
                    Application.DoEvents();
                    preset.ClickChoiceForTest(3);
                    Application.DoEvents();

                    stage = "enter custom notification thresholds";
                    TextBox thresholds = FindControlByAccessibleName(
                        flyout,
                        "Custom warning percentages") as TextBox;
                    if (thresholds == null)
                    {
                        throw new InvalidOperationException(
                            "The custom warning field was not found.");
                    }
                    thresholds.Text = "42, 17";
                    Application.DoEvents();
                    flyoutNotificationSettings =
                        nativeFlyoutSettings.CodexQuotaNotificationsEnabled
                        && string.Equals(
                            nativeFlyoutSettings.CodexQuotaNotificationThresholdsCsv,
                            "42,17",
                            StringComparison.Ordinal)
                        && ContainsControlText(
                            flyout,
                            "Warnings at 42% and 17% remaining.");

                    stage = "enable start with Windows setting";
                    CheckBox startup = FindControlByAccessibleName(
                        flyout,
                        "Start UsageApp with Windows") as CheckBox;
                    if (startup == null)
                    {
                        throw new InvalidOperationException(
                            "The Start with Windows toggle was not found.");
                    }
                    startup.Checked = true;
                    Application.DoEvents();
                    flyoutStartupSetting =
                        nativeFlyoutSettings.StartWithWindows;

                    stage = "flyout dashboard";
                    PerformButtonClick(FindControlByAccessibleName(
                        flyout,
                        "Open dashboard"));
                    flyout.Hide();
                }

                bool dashboardClaude;
                bool dashboardCodex;
                bool dashboardActivityRange;
                bool dashboardCustomRange;
                bool dashboardProviderVisibility;
                NativeSettings dashboardSettings = new NativeSettings();
                using (NativeDashboardForm dashboard = new NativeDashboardForm(
                    dashboardSettings))
                {
                    dashboard.ShowSnapshot(DemoSnapshot(), false);
                    CreateHandles(dashboard);
                    dashboard.Show();
                    Application.DoEvents();
                    stage = "dashboard Claude";
                    PerformButtonClick(FindControlByAccessibleName(
                        dashboard,
                        "Claude native beta information"));
                    Application.DoEvents();
                    dashboardClaude = ContainsControlText(
                        dashboard,
                        "NOT CONNECTED");
                    stage = "dashboard Codex";
                    PerformButtonClick(FindButtonByText(dashboard, "Codex"));
                    Application.DoEvents();
                    dashboardCodex = ContainsControlText(
                        dashboard,
                        "Quota and resets");
                    stage = "dashboard activity range";
                    PerformButtonClick(FindButtonByText(dashboard, "7 days"));
                    Application.DoEvents();
                    dashboardActivityRange = ContainsControlText(
                        dashboard,
                        "7 recorded days")
                        && ContainsControlText(dashboard, "Tokens by day");

                    stage = "dashboard custom activity range";
                    bool customDialogApplied = false;
                    int customDialogTicks = 0;
                    using (System.Windows.Forms.Timer customDialogTimer =
                        new System.Windows.Forms.Timer())
                    {
                        customDialogTimer.Interval = 50;
                        customDialogTimer.Tick += delegate
                        {
                            customDialogTicks++;
                            Form dialog = FindOpenFormByAccessibleName(
                                "Custom activity date range");
                            if (dialog == null)
                            {
                                return;
                            }
                            DateTimePicker from = FindControlByAccessibleName(
                                dialog,
                                "From date") as DateTimePicker;
                            DateTimePicker to = FindControlByAccessibleName(
                                dialog,
                                "To date") as DateTimePicker;
                            Button apply = FindControlByAccessibleName(
                                dialog,
                                "Apply custom date range") as Button;
                            if (from != null && to != null && apply != null)
                            {
                                DateTime chosenTo = to.MaxDate.Date;
                                DateTime chosenFrom = chosenTo.AddDays(-2);
                                if (chosenFrom < from.MinDate.Date)
                                {
                                    chosenFrom = from.MinDate.Date;
                                }
                                from.Value = chosenFrom;
                                to.Value = chosenTo;
                                apply.PerformClick();
                                customDialogApplied = true;
                                customDialogTimer.Stop();
                                return;
                            }
                            if (customDialogTicks >= 100)
                            {
                                dialog.DialogResult = DialogResult.Cancel;
                                dialog.Close();
                                customDialogTimer.Stop();
                            }
                        };
                        customDialogTimer.Start();
                        PerformButtonClick(FindButtonByText(dashboard, "Custom"));
                        customDialogTimer.Stop();
                    }
                    Application.DoEvents();
                    dashboardCustomRange = customDialogApplied
                        && ContainsControlText(dashboard, "Custom")
                        && ContainsControlText(dashboard, "Tokens by day");

                    stage = "dashboard provider visibility";
                    Control dashboardClaudeButton = FindControlByAccessibleName(
                        dashboard,
                        "Claude native beta information");
                    Control dashboardCodexButton = FindButtonByText(
                        dashboard,
                        "Codex");
                    dashboardSettings.ShowClaudeProvider = false;
                    dashboardSettings.ShowClaudeTrayIcon = false;
                    dashboard.ApplySettings();
                    Application.DoEvents();
                    bool dashboardCodexOnly = !dashboardClaudeButton.Visible
                        && !dashboardCodexButton.Visible
                        && dashboard.ProviderSwitcherMatchesVisibilityForTest()
                        && ContainsControlText(dashboard, "Quota and resets");
                    dashboardSettings.ShowCodexProvider = false;
                    dashboardSettings.ShowClaudeProvider = true;
                    dashboardSettings.ShowCodexTrayIcon = false;
                    dashboardSettings.ShowClaudeTrayIcon = true;
                    dashboard.ApplySettings();
                    Application.DoEvents();
                    bool dashboardClaudeOnly = !dashboardCodexButton.Visible
                        && !dashboardClaudeButton.Visible
                        && dashboard.ProviderSwitcherMatchesVisibilityForTest()
                        && ContainsControlText(dashboard, "NOT CONNECTED");
                    dashboardProviderVisibility = dashboardCodexOnly
                        && dashboardClaudeOnly;
                    dashboard.Hide();
                }

                bool passed = flyoutClaude
                    && flyoutCodex
                    && flyoutSettings
                    && flyoutNotificationSettings
                    && flyoutStartupSetting
                    && flyoutRefreshStayedVisible
                    && flyoutPinToggle
                    && flyoutSingleSelectSetting
                    && providerVisibilitySettings
                    && dashboardRequested
                    && refreshRequested
                    && dashboardClaude
                    && dashboardCodex
                    && dashboardActivityRange
                    && dashboardCustomRange
                    && dashboardProviderVisibility;
                result.AppendLine(passed ? "status=passed" : "status=failed");
                result.AppendLine("flyoutClaude=" + flyoutClaude);
                result.AppendLine("flyoutCodex=" + flyoutCodex);
                result.AppendLine("flyoutSettings=" + flyoutSettings);
                result.AppendLine(
                    "flyoutNotificationSettings="
                        + flyoutNotificationSettings);
                result.AppendLine(
                    "flyoutStartupSetting=" + flyoutStartupSetting);
                result.AppendLine(
                    "flyoutRefreshStayedVisible="
                        + flyoutRefreshStayedVisible);
                result.AppendLine("flyoutPinToggle=" + flyoutPinToggle);
                result.AppendLine(
                    "flyoutSingleSelectSetting="
                        + flyoutSingleSelectSetting);
                result.AppendLine(
                    "providerVisibilitySettings="
                        + providerVisibilitySettings);
                result.AppendLine(
                    "codexOnlyProviderSettings="
                        + codexOnlyProviderSettings);
                result.AppendLine(
                    "claudeOnlyProviderSettings="
                        + claudeOnlyProviderSettings);
                result.AppendLine(
                    "restoredProviderSettings="
                        + restoredProviderSettings);
                result.AppendLine("dashboardRequested=" + dashboardRequested);
                result.AppendLine("refreshRequested=" + refreshRequested);
                result.AppendLine("dashboardClaude=" + dashboardClaude);
                result.AppendLine("dashboardCodex=" + dashboardCodex);
                result.AppendLine(
                    "dashboardActivityRange=" + dashboardActivityRange);
                result.AppendLine(
                    "dashboardCustomRange=" + dashboardCustomRange);
                result.AppendLine(
                    "dashboardProviderVisibility="
                        + dashboardProviderVisibility);
            }
            catch (Exception error)
            {
                result.AppendLine("status=failed");
                result.AppendLine("stage=" + stage);
                result.AppendLine(
                    "message="
                        + error.Message.Replace("\r", " ").Replace("\n", " "));
            }
            WriteDiagnosticReport(outputPath, result.ToString());
        }

        private static void PerformButtonClick(Control control)
        {
            Button button = control as Button;
            if (button == null)
            {
                throw new InvalidOperationException(
                    "The expected native button was not found.");
            }
            button.PerformClick();
        }

        private static Control FindControlByAccessibleName(
            Control root,
            string accessibleName)
        {
            if (root != null
                && string.Equals(
                    root.AccessibleName,
                    accessibleName,
                    StringComparison.Ordinal))
            {
                return root;
            }
            if (root != null)
            {
                foreach (Control child in root.Controls)
                {
                    Control match = FindControlByAccessibleName(
                        child,
                        accessibleName);
                    if (match != null)
                    {
                        return match;
                    }
                }
            }
            return null;
        }

        private static Control FindButtonByText(Control root, string text)
        {
            Button button = root as Button;
            if (button != null
                && string.Equals(button.Text, text, StringComparison.Ordinal))
            {
                return button;
            }
            if (root != null)
            {
                foreach (Control child in root.Controls)
                {
                    Control match = FindButtonByText(child, text);
                    if (match != null)
                    {
                        return match;
                    }
                }
            }
            return null;
        }

        private static Form FindOpenFormByAccessibleName(string name)
        {
            foreach (Form form in Application.OpenForms)
            {
                if (string.Equals(
                    form.AccessibleName,
                    name,
                    StringComparison.Ordinal))
                {
                    return form;
                }
            }
            return null;
        }

        private static bool ContainsControlText(Control root, string fragment)
        {
            if (root != null
                && !string.IsNullOrEmpty(root.Text)
                && root.Text.IndexOf(
                    fragment,
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
            if (root != null)
            {
                foreach (Control child in root.Controls)
                {
                    if (ContainsControlText(child, fragment))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static void RunLayoutProbe(string outputPath)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            NativeSettings probeSettings = new NativeSettings();
            probeSettings.CodexQuotaNotificationsEnabled = true;
            probeSettings.CodexQuotaNotificationThresholdsCsv = "42,17";
            using (UsageFlyout flyout = new UsageFlyout(true, probeSettings))
            {
                flyout.ShowSnapshot(DemoSnapshot());
                flyout.CreateControl();
                flyout.PrepareNearTaskbarForLayoutProbe();
                flyout.PerformLayout();
                string usageReport = flyout.LayoutReport();
                flyout.ShowSettingsForCapture();
                flyout.PerformLayout();
                string settingsReport = flyout.LayoutReport();
                NativeSettings largeSettings = new NativeSettings();
                largeSettings.FlyoutTextScale = 125;
                largeSettings.CodexQuotaNotificationsEnabled = true;
                largeSettings.CodexQuotaNotificationThresholdsCsv = "42,17";
                using (UsageFlyout largeFlyout = new UsageFlyout(
                    true,
                    largeSettings))
                {
                    largeFlyout.ShowSnapshot(DemoSnapshot());
                    largeFlyout.CreateControl();
                    largeFlyout.PrepareNearTaskbarForLayoutProbe();
                    largeFlyout.ShowSettingsForCapture();
                    largeFlyout.PerformLayout();
                    string largeSettingsReport = largeFlyout.LayoutReport();
                    using (NativeDashboardForm dashboard = new NativeDashboardForm(
                        new NativeSettings()))
                    {
                        dashboard.ShowSnapshot(DemoSnapshot(), false);
                        dashboard.CreateControl();
                        CreateHandles(dashboard);
                        dashboard.PerformLayout();
                        string dashboardReport = dashboard.LayoutReport();
                        NativeSettings largeDashboardSettings =
                            new NativeSettings();
                        largeDashboardSettings.FlyoutTextScale = 125;
                        using (NativeDashboardForm largeDashboard =
                            new NativeDashboardForm(largeDashboardSettings))
                        {
                            largeDashboard.ShowSnapshot(DemoSnapshot(), false);
                            largeDashboard.CreateControl();
                            CreateHandles(largeDashboard);
                            largeDashboard.PerformLayout();
                            string largeDashboardReport =
                                largeDashboard.LayoutReport();
                            StringBuilder fontMatrix = new StringBuilder();
                            foreach (string fontName in
                                NativeSettings.InterfaceFontOptions)
                            {
                                foreach (int textScale in new int[] { 100, 125 })
                                {
                                    NativeSettings fontSettings =
                                        new NativeSettings();
                                    fontSettings.InterfaceFontName = fontName;
                                    fontSettings.FlyoutTextScale = textScale;
                                    using (NativeDashboardForm fontDashboard =
                                        new NativeDashboardForm(fontSettings))
                                    {
                                        fontDashboard.ShowSnapshot(
                                            DemoSnapshot(),
                                            false);
                                        fontDashboard.CreateControl();
                                        CreateHandles(fontDashboard);
                                        fontDashboard.PerformLayout();
                                        string fontReport =
                                            fontDashboard.LayoutReport();
                                        bool fontPassed = fontReport.StartsWith(
                                            "status=passed",
                                            StringComparison.Ordinal);
                                        fontMatrix.AppendLine(
                                            "font=" + fontName
                                                + ";scale=" + textScale
                                                + ";status="
                                                + (fontPassed
                                                    ? "passed"
                                                    : "failed"));
                                        if (!fontPassed)
                                        {
                                            fontMatrix.Append(fontReport);
                                        }
                                    }
                                }
                            }
                            WriteDiagnosticReport(
                                outputPath,
                                "[usage]"
                                    + Environment.NewLine
                                    + usageReport
                                    + "[settings]"
                                    + Environment.NewLine
                                    + settingsReport
                                    + "[settings-125]"
                                    + Environment.NewLine
                                    + largeSettingsReport
                                    + "[dashboard]"
                                    + Environment.NewLine
                                    + dashboardReport
                                    + "[dashboard-125]"
                                    + Environment.NewLine
                                    + largeDashboardReport
                                    + "[dashboard-font-matrix]"
                                    + Environment.NewLine
                                    + fontMatrix.ToString());
                        }
                    }
                }
            }
        }

        private static void RunOffscreenRender(string outputPath, bool settings)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (UsageFlyout flyout = new UsageFlyout(true, new NativeSettings()))
            {
                flyout.ShowSnapshot(DemoSnapshot());
                if (settings)
                {
                    flyout.ShowSettingsForCapture();
                }
                flyout.CreateControl();
                CreateHandles(flyout);
                flyout.Location = new Point(40, 40);
                flyout.Show();
                Application.DoEvents();
                flyout.PerformLayout();
                using (Bitmap image = new Bitmap(flyout.Width, flyout.Height))
                {
                    flyout.DrawToBitmap(
                        image,
                        new Rectangle(Point.Empty, flyout.Size));
                    image.Save(outputPath, ImageFormat.Png);
                }
            }
        }

        private static void CreateHandles(Control control)
        {
            control.CreateControl();
            foreach (Control child in control.Controls)
            {
                CreateHandles(child);
            }
        }

        private static void RunTrayPreview(string outputPath)
        {
            int?[] values = new int?[] { null, 1, 48, 100 };
            using (Bitmap preview = new Bitmap(560, 150))
            using (Graphics graphics = Graphics.FromImage(preview))
            using (Font caption = new Font("Segoe UI", 10.0f, FontStyle.Bold))
            {
                graphics.Clear(Color.FromArgb(241, 237, 243));
                graphics.InterpolationMode =
                    System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                for (int index = 0; index < values.Length; index++)
                {
                    int left = 20 + index * 135;
                    using (Bitmap icon = TrayIconRenderer.CreateBitmap(
                        values[index],
                        24,
                        "Consolas",
                        Color.FromArgb(48, 113, 141)))
                    {
                        graphics.DrawImageUnscaled(icon, left + 48, 12);
                        graphics.DrawImage(
                            icon,
                            new Rectangle(left + 10, 42, 96, 96),
                            new Rectangle(0, 0, icon.Width, icon.Height),
                            GraphicsUnit.Pixel);
                    }
                    TextRenderer.DrawText(
                        graphics,
                        values[index].HasValue ? values[index].Value.ToString() : "?",
                        caption,
                        new Rectangle(left, 10, 126, 26),
                        Color.FromArgb(25, 31, 39),
                        TextFormatFlags.Left);
                }
                preview.Save(outputPath, ImageFormat.Png);
            }
        }

        private static void WriteDiagnosticReport(
            string outputPath,
            string report)
        {
            File.WriteAllText(outputPath, report, Encoding.UTF8);
            if (DiagnosticReportFailedForTest(report))
            {
                MarkDiagnosticFailure();
            }
        }

        internal static bool DiagnosticReportFailedForTest(string report)
        {
            return string.IsNullOrEmpty(report)
                || report.IndexOf(
                    "status=failed",
                    StringComparison.OrdinalIgnoreCase) >= 0
                || report.IndexOf(
                    "status=error",
                    StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void MarkDiagnosticFailure()
        {
            if (Environment.ExitCode == 0)
            {
                Environment.ExitCode = 1;
            }
        }

        private static UsageSnapshot DemoSnapshot()
        {
            DateTime now = DateTime.UtcNow;
            UsageSnapshot snapshot = new UsageSnapshot();
            snapshot.ObservedAtUtc = now;
            snapshot.PlanType = "prolite";

            UsageWindow weekly = new UsageWindow();
            weekly.LimitId = "codex";
            weekly.Kind = "secondary";
            weekly.Label = "Weekly";
            weekly.UsedPercent = 51;
            weekly.DurationMinutes = 10080;
            weekly.ResetsAtUtc = now.AddDays(5).AddHours(19);
            snapshot.Windows.Add(weekly);

            UsageWindow spark = new UsageWindow();
            spark.LimitId = "spark";
            spark.LimitName = "GPT-5.3-Codex-Spark";
            spark.Kind = "secondary";
            spark.Label = "GPT-5.3-Codex-Spark · Weekly";
            spark.UsedPercent = 0;
            spark.DurationMinutes = 10080;
            spark.ResetsAtUtc = now.AddDays(6).AddHours(23);
            snapshot.Windows.Add(spark);

            snapshot.BankedResets.AvailableCount = 3;
            snapshot.BankedResets.DetailsAvailable = true;
            snapshot.BankedResets.CountObservedAtUtc = now;
            snapshot.BankedResets.DetailsObservedAtUtc = now;
            for (int index = 0; index < 3; index++)
            {
                BankedReset reset = new BankedReset();
                reset.Id = "demo-" + index.ToString(CultureInfo.InvariantCulture);
                reset.Title = "Full reset";
                reset.Description =
                    "Thanks for using Codex! You've been granted one free rate limit reset.";
                reset.Status = "available";
                reset.GrantedAtUtc = now.AddDays(-7);
                reset.ExpiresAtUtc = index == 0
                    ? now.AddDays(1).AddHours(11)
                    : now.AddDays(12 + index * 3).AddHours(index * 2);
                snapshot.BankedResets.Items.Add(reset);
            }

            TokenUsageSummary tokenUsage = new TokenUsageSummary();
            tokenUsage.LifetimeTokens = 2300000000L;
            tokenUsage.PeakDailyTokens = 196000000L;
            tokenUsage.LongestRunningTurnSeconds = 2760L;
            tokenUsage.CurrentStreakDays = 5L;
            tokenUsage.LongestStreakDays = 14L;
            DateTime today = DateTime.Today;
            for (int index = 34; index >= 0; index--)
            {
                long tokens = 18000000L
                    + ((34 - index) % 7) * 11700000L
                    + ((34 - index) % 5) * 6300000L;
                if (index == 8)
                {
                    tokens = 196000000L;
                }
                tokenUsage.DailyBuckets.Add(new TokenUsageDailyBucket
                {
                    StartDate = today.AddDays(-index).ToString(
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture),
                    Tokens = tokens
                });
            }
            snapshot.TokenUsage = tokenUsage;
            snapshot.TokenUsageObservedAtUtc = now;
            return snapshot;
        }
    }
}
