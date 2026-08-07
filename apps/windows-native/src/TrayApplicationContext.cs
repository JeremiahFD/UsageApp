using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace UsageApp.Native
{
    internal sealed class TrayApplicationContext : ApplicationContext
    {
        private readonly NotifyIcon codexTray;
        private readonly NotifyIcon claudeTray;
        private readonly UsageFlyout flyout;
        private readonly NativeDashboardForm dashboard;
        private readonly Control dispatcher;
        private readonly CodexAppServer client;
        private readonly SnapshotStore snapshotStore;
        private readonly NativeSettingsStore settingsStore;
        private readonly NativeSettings settings;
        private readonly StartupRegistration startupRegistration;
        private readonly QuotaNotificationEvaluator quotaNotificationEvaluator;
        private readonly ClaudeStatusLineReceiver claudeReceiver;
        private readonly ClaudeStatusLineIntegration claudeIntegration;
        private readonly ClaudeSnapshotStore claudeSnapshotStore;
        private readonly System.Windows.Forms.Timer refreshTimer;
        private readonly System.Windows.Forms.Timer notificationRefreshTimer;
        private readonly System.Windows.Forms.Timer claudeFreshnessTimer;
        private readonly RegisteredWaitHandle showSignalRegistration;
        private Icon codexDynamicIcon;
        private Icon claudeDynamicIcon;
        private int refreshing;
        private int notificationRefreshPending;
        private int exiting;
        private int uiDisposed;
        private bool liveSnapshotSeen;
        private bool appliedStartWithWindows;
        private bool claudeConnected;
        private UsageSnapshot lastSnapshot;
        private ClaudeQuotaSnapshot lastClaudeSnapshot;

        public TrayApplicationContext(
            bool showOnStart,
            EventWaitHandle showSignal)
        {
            if (showSignal == null)
            {
                throw new ArgumentNullException("showSignal");
            }
            dispatcher = new Control();
            dispatcher.CreateControl();
            settingsStore = new NativeSettingsStore();
            settings = settingsStore.Load();
            startupRegistration = new StartupRegistration();
            string startupReadError;
            bool startupEnabled;
            if (startupRegistration.TryIsEnabled(
                Application.ExecutablePath,
                out startupEnabled,
                out startupReadError))
            {
                settings.StartWithWindows = startupEnabled;
                appliedStartWithWindows = startupEnabled;
            }
            else
            {
                appliedStartWithWindows = settings.StartWithWindows;
            }
            quotaNotificationEvaluator = new QuotaNotificationEvaluator();
            string savedClaudeToken = ClaudeStatusLineIntegration.LoadSavedToken();
            claudeReceiver = ClaudeStatusLineReceiver.IsValidPathToken(savedClaudeToken)
                ? new ClaudeStatusLineReceiver(
                    savedClaudeToken, ClaudeStatusLineIntegration.DefaultPort)
                : new ClaudeStatusLineReceiver(
                    ClaudeStatusLineReceiver.CreatePathToken(),
                    ClaudeStatusLineIntegration.DefaultPort);
            claudeIntegration = new ClaudeStatusLineIntegration(claudeReceiver);
            claudeSnapshotStore = new ClaudeSnapshotStore();
            claudeReceiver.SnapshotReceived += OnClaudeSnapshotReceived;
            claudeReceiver.ReceiverFaulted += OnClaudeReceiverFaulted;
            client = new CodexAppServer();
            client.RateLimitsUpdated += OnRateLimitsUpdated;
            snapshotStore = new SnapshotStore();
            flyout = new UsageFlyout(false, settings);
            flyout.SetStartupRegistrationError(startupReadError);
            dashboard = new NativeDashboardForm(settings);
            showSignalRegistration = ThreadPool.RegisterWaitForSingleObject(
                showSignal,
                OnShowSignal,
                null,
                Timeout.Infinite,
                false);
            flyout.RefreshRequested += delegate { Refresh(); };
            flyout.DashboardRequested += delegate
            {
                dashboard.ShowProviderDashboard(!flyout.ShowingClaudeProvider);
            };
            flyout.SettingsChanged += delegate { ApplySettings(); };
            flyout.ClaudeConnectRequested += delegate { ConnectClaude(); };
            flyout.ClaudeDisconnectRequested += delegate { DisconnectClaude(); };
            flyout.QuitRequested += delegate { ExitApplication(); };
            dashboard.RefreshRequested += delegate { Refresh(); };
            lastSnapshot = snapshotStore.Load();
            if (lastSnapshot != null)
            {
                flyout.ShowCachedSnapshot(lastSnapshot);
                dashboard.ShowSnapshot(lastSnapshot, true);
            }
            lastClaudeSnapshot = claudeSnapshotStore.Load(DateTime.UtcNow);
            if (lastClaudeSnapshot != null)
            {
                flyout.ShowClaudeSnapshot(lastClaudeSnapshot);
            }
            ClaudeIntegrationResult resumedClaude = claudeIntegration.Resume();
            claudeConnected = resumedClaude.Connected;
            flyout.SetClaudeIntegrationStatus(
                resumedClaude.Connected, resumedClaude.Message);

            UsageWindow cachedPreferred = lastSnapshot == null
                ? null
                : TrayWindowSelector.Select(
                    lastSnapshot.Windows,
                    settings.TrayWindowMode);
            codexTray = new NotifyIcon();
            codexTray.Text = lastSnapshot == null
                ? "UsageApp Native - connecting to Codex"
                : cachedPreferred == null
                    ? "Codex last known data has no quota window"
                    : TruncateTooltip(
                        string.Format(
                            "Codex: last known {0}% left at {1:g}",
                            cachedPreferred.RemainingPercent,
                            lastSnapshot.ObservedAtUtc.ToLocalTime()));
            codexTray.ContextMenuStrip = CreateTrayMenu(true);
            codexDynamicIcon = TrayIconRenderer.Create(
                cachedPreferred == null
                    ? (int?)null
                    : cachedPreferred.RemainingPercent,
                settings.TrayFontName,
                settings.CodexTrayColor,
                settings.TrayEdgeMode);
            codexTray.Icon = codexDynamicIcon;
            codexTray.Visible = settings.ShowCodexTrayIcon;
            codexTray.MouseClick += delegate(object sender, MouseEventArgs eventArgs)
            {
                if (eventArgs.Button == MouseButtons.Left)
                {
                    flyout.ShowProviderNearTaskbar(true);
                }
            };

            UsageWindow cachedClaudePreferred = lastClaudeSnapshot == null
                ? null
                : TrayWindowSelector.Select(
                    lastClaudeSnapshot.Windows,
                    settings.TrayWindowMode);
            claudeTray = new NotifyIcon();
            claudeTray.Text = lastClaudeSnapshot == null
                ? "UsageApp Native - waiting for Claude"
                : cachedClaudePreferred == null
                    ? "Claude last known data has no quota window"
                    : TruncateTooltip(
                        string.Format(
                            "Claude: last known {0}% left at {1:g}",
                            cachedClaudePreferred.RemainingPercent,
                            lastClaudeSnapshot.ObservedAtUtc.ToLocalTime()));
            claudeTray.ContextMenuStrip = CreateTrayMenu(false);
            claudeDynamicIcon = TrayIconRenderer.Create(
                cachedClaudePreferred == null
                    ? (int?)null
                    : cachedClaudePreferred.RemainingPercent,
                settings.TrayFontName,
                settings.ClaudeTrayColor,
                settings.TrayEdgeMode);
            claudeTray.Icon = claudeDynamicIcon;
            claudeTray.Visible = settings.ShowClaudeTrayIcon;
            claudeTray.MouseClick += delegate(object sender, MouseEventArgs eventArgs)
            {
                if (eventArgs.Button == MouseButtons.Left)
                {
                    flyout.ShowProviderNearTaskbar(false);
                }
            };

            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

            refreshTimer = new System.Windows.Forms.Timer();
            refreshTimer.Interval = RefreshIntervalMilliseconds();
            refreshTimer.Tick += delegate { Refresh(); };
            refreshTimer.Start();

            notificationRefreshTimer = new System.Windows.Forms.Timer();
            notificationRefreshTimer.Interval = 750;
            notificationRefreshTimer.Tick += delegate
            {
                notificationRefreshTimer.Stop();
                if (Volatile.Read(ref exiting) != 0)
                {
                    Interlocked.Exchange(ref notificationRefreshPending, 0);
                    return;
                }
                if (Volatile.Read(ref refreshing) != 0)
                {
                    notificationRefreshTimer.Start();
                    return;
                }
                Interlocked.Exchange(ref notificationRefreshPending, 0);
                Refresh();
            };

            claudeFreshnessTimer = new System.Windows.Forms.Timer();
            claudeFreshnessTimer.Interval = 60 * 1000;
            claudeFreshnessTimer.Tick += delegate { RefreshClaudeFreshness(); };
            claudeFreshnessTimer.Start();

            Refresh();
            if (showOnStart)
            {
                flyout.ShowNearTaskbar();
            }
        }

        private void OnShowSignal(object state, bool timedOut)
        {
            if (timedOut || Volatile.Read(ref exiting) != 0)
            {
                return;
            }
            try
            {
                dispatcher.BeginInvoke((MethodInvoker)delegate
                {
                    if (Volatile.Read(ref exiting) == 0)
                    {
                        flyout.ShowProviderNearTaskbar(!flyout.ShowingClaudeProvider);
                    }
                });
            }
            catch
            {
                // The primary instance may already be shutting down.
            }
        }

        private void OnDisplaySettingsChanged(object sender, EventArgs eventArgs)
        {
            RefreshTrayPresentation();
        }

        private void OnUserPreferenceChanged(
            object sender,
            UserPreferenceChangedEventArgs eventArgs)
        {
            RefreshTrayPresentation();
        }

        private void RefreshTrayPresentation()
        {
            if (Volatile.Read(ref exiting) != 0)
            {
                return;
            }
            try
            {
                dispatcher.BeginInvoke((MethodInvoker)delegate
                {
                    if (Volatile.Read(ref exiting) != 0)
                    {
                        return;
                    }
                    UsageWindow codex = lastSnapshot == null
                        ? null
                        : TrayWindowSelector.Select(
                            lastSnapshot.Windows,
                            settings.TrayWindowMode);
                    ReplaceCodexIcon(codex == null
                        ? (int?)null
                        : codex.RemainingPercent);
                    UpdateClaudeTray(lastClaudeSnapshot);
                });
            }
            catch
            {
                // Theme/DPI notifications can race with process shutdown.
            }
        }

        private ContextMenuStrip CreateTrayMenu(bool codex)
        {
            string providerName = codex ? "Codex" : "Claude";
            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem open = new ToolStripMenuItem(
                "Open " + providerName + " usage");
            open.Click += delegate { flyout.ShowProviderNearTaskbar(codex); };
            menu.Items.Add(open);
            ToolStripMenuItem openDashboard = new ToolStripMenuItem(
                "Open " + providerName + " dashboard");
            openDashboard.Click += delegate
            {
                dashboard.ShowProviderDashboard(codex);
            };
            menu.Items.Add(openDashboard);
            ToolStripMenuItem refresh = new ToolStripMenuItem("Refresh now");
            refresh.Click += delegate { Refresh(); };
            menu.Items.Add(refresh);
            menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem exit = new ToolStripMenuItem("Exit UsageApp Native");
            exit.Click += delegate { ExitApplication(); };
            menu.Items.Add(exit);
            return menu;
        }

        private void OnRateLimitsUpdated(object sender, EventArgs eventArgs)
        {
            if (Volatile.Read(ref exiting) != 0
                || Interlocked.Exchange(ref notificationRefreshPending, 1) != 0)
            {
                return;
            }

            try
            {
                dispatcher.BeginInvoke((MethodInvoker)delegate
                {
                    if (Volatile.Read(ref exiting) != 0)
                    {
                        Interlocked.Exchange(ref notificationRefreshPending, 0);
                        return;
                    }
                    notificationRefreshTimer.Stop();
                    notificationRefreshTimer.Start();
                });
            }
            catch
            {
                Interlocked.Exchange(ref notificationRefreshPending, 0);
            }
        }

        private void OnClaudeSnapshotReceived(object sender, ClaudeQuotaSnapshot snapshot)
        {
            if (snapshot == null || Volatile.Read(ref exiting) != 0) return;
            try
            {
                dispatcher.BeginInvoke((MethodInvoker)delegate
                {
                    if (Volatile.Read(ref exiting) != 0) return;
                    ClaudeQuotaSnapshot accepted =
                        ClaudeStatusLine.SelectAcceptedSnapshot(
                            lastClaudeSnapshot,
                            snapshot,
                            DateTime.UtcNow);
                    if (accepted == null)
                    {
                        return;
                    }
                    bool acceptedIncoming = object.ReferenceEquals(
                        accepted,
                        snapshot);
                    lastClaudeSnapshot = accepted;
                    if (accepted.Windows.Count > 0)
                    {
                        claudeSnapshotStore.Save(accepted);
                    }
                    flyout.ShowClaudeSnapshot(accepted);
                    UpdateClaudeTray(accepted);
                    if (acceptedIncoming && accepted.Windows.Count > 0)
                    {
                        claudeConnected = true;
                    }
                    flyout.SetClaudeIntegrationStatus(claudeConnected,
                        accepted.Status == "live"
                            ? "Claude quota updated from the current Claude Code session."
                            : accepted.Message);
                });
            }
            catch { }
        }

        private void OnClaudeReceiverFaulted(object sender, string message)
        {
            if (Volatile.Read(ref exiting) != 0) return;
            try
            {
                dispatcher.BeginInvoke((MethodInvoker)delegate
                {
                    if (Volatile.Read(ref exiting) != 0) return;
                    flyout.SetClaudeIntegrationStatus(
                        claudeConnected,
                        string.IsNullOrWhiteSpace(message)
                            ? "Claude's local receiver stopped. Reconnect Claude monitoring to retry."
                            : "Claude's local receiver stopped: " + message);
                });
            }
            catch
            {
                // Receiver failures can race with process shutdown.
            }
        }

        private void ConnectClaude()
        {
            DialogResult confirmation = flyout.ShowConfirmation(
                "UsageApp will add a loopback-only Claude Code status-line bridge and preserve your current status-line setting. Start a new Claude Code session afterward. Continue?",
                "Connect Claude monitoring",
                MessageBoxIcon.Information);
            if (confirmation != DialogResult.Yes) return;
            ClaudeIntegrationResult result = claudeIntegration.Connect();
            claudeConnected = result.Connected;
            flyout.SetClaudeIntegrationStatus(result.Connected, result.Message);
        }

        private void DisconnectClaude()
        {
            DialogResult confirmation = flyout.ShowConfirmation(
                "Disconnect Claude monitoring and restore the status-line setting UsageApp saved when it connected?",
                "Disconnect Claude monitoring",
                MessageBoxIcon.Warning);
            if (confirmation != DialogResult.Yes) return;
            ClaudeIntegrationResult result = claudeIntegration.Disconnect();
            claudeConnected = result.Connected;
            if (!result.Connected && lastClaudeSnapshot != null)
            {
                lastClaudeSnapshot.Status = "stale";
                lastClaudeSnapshot.Message =
                    "Claude monitoring is disconnected. Showing the last known quota.";
                flyout.ShowClaudeSnapshot(lastClaudeSnapshot);
                UpdateClaudeTray(lastClaudeSnapshot);
            }
            else if (!result.Connected)
            {
                UpdateClaudeTray(null);
            }
            flyout.SetClaudeIntegrationStatus(result.Connected, result.Message);
        }

        private void RefreshClaudeFreshness()
        {
            if (lastClaudeSnapshot == null
                || Volatile.Read(ref exiting) != 0
                || !ClaudeStatusLine.RefreshFreshness(
                    lastClaudeSnapshot,
                    DateTime.UtcNow))
            {
                return;
            }
            flyout.ShowClaudeSnapshot(lastClaudeSnapshot);
            UpdateClaudeTray(lastClaudeSnapshot);
            flyout.SetClaudeIntegrationStatus(
                claudeConnected,
                lastClaudeSnapshot.Message);
        }

        private void Refresh()
        {
            if (Volatile.Read(ref exiting) != 0)
            {
                return;
            }
            if (Interlocked.CompareExchange(ref refreshing, 1, 0) != 0)
            {
                return;
            }
            flyout.ShowLoading();
            dashboard.ShowLoading();

            ThreadPool.QueueUserWorkItem(delegate
            {
                UsageSnapshot snapshot = null;
                Exception failure = null;
                try
                {
                    snapshot = client.ReadRateLimits();
                }
                catch (Exception error)
                {
                    failure = error;
                }

                try
                {
                    dispatcher.BeginInvoke((MethodInvoker)delegate
                    {
                        try
                        {
                            if (Volatile.Read(ref exiting) != 0)
                            {
                                return;
                            }
                            if (failure == null)
                            {
                                ApplySnapshot(snapshot);
                            }
                            else
                            {
                                ApplyFailure(failure);
                            }
                        }
                        finally
                        {
                            Interlocked.Exchange(ref refreshing, 0);
                        }
                    });
                }
                catch
                {
                    Interlocked.Exchange(ref refreshing, 0);
                }
            });
        }

        private void ApplySnapshot(UsageSnapshot snapshot)
        {
            bool samePlan = lastSnapshot != null
                && string.Equals(
                    snapshot.PlanType,
                    lastSnapshot.PlanType,
                    StringComparison.OrdinalIgnoreCase);
            if (lastSnapshot != null && !samePlan)
            {
                quotaNotificationEvaluator.Reset();
            }
            QuotaNotificationDecision notification =
                quotaNotificationEvaluator.Evaluate(
                    snapshot,
                    settings.CodexQuotaNotificationThresholds);
            snapshot = SnapshotStore.Merge(
                snapshot,
                lastSnapshot,
                liveSnapshotSeen && samePlan,
                samePlan);
            liveSnapshotSeen = true;
            lastSnapshot = snapshot;
            snapshotStore.Save(snapshot);
            flyout.ShowSnapshot(snapshot);
            dashboard.ShowSnapshot(snapshot, false);
            UsageWindow preferred = TrayWindowSelector.Select(
                snapshot.Windows,
                settings.TrayWindowMode);
            ReplaceCodexIcon(preferred == null ? (int?)null : preferred.RemainingPercent);
            codexTray.Text = preferred == null
                ? "Codex usage - no quota window returned"
                : TruncateTooltip(
                    string.Format(
                        "Codex: {0}% left; updated {1:g}",
                        preferred.RemainingPercent,
                        snapshot.ObservedAtUtc.ToLocalTime()));
            if (settings.CodexQuotaNotificationsEnabled
                && notification != null)
            {
                ShowQuotaNotification(notification);
            }
        }

        private void ShowQuotaNotification(
            QuotaNotificationDecision notification)
        {
            NotifyIcon notificationTray = codexTray.Visible
                ? codexTray
                : claudeTray;
            notificationTray.BalloonTipIcon = ToolTipIcon.Warning;
            notificationTray.BalloonTipTitle = "Codex quota warning";
            notificationTray.BalloonTipText = string.Format(
                "{0} has {1}% remaining (warning at {2}%).",
                notification.QuotaLabel,
                notification.RemainingPercent,
                notification.ThresholdPercent);
            notificationTray.ShowBalloonTip(8000);
        }

        private void ApplyFailure(Exception error)
        {
            flyout.ShowError(
                FriendlyError(error),
                lastSnapshot == null ? (DateTime?)null : lastSnapshot.ObservedAtUtc);
            dashboard.ShowError(FriendlyError(error));
            UsageWindow preferred = lastSnapshot == null
                ? null
                : TrayWindowSelector.Select(
                    lastSnapshot.Windows,
                    settings.TrayWindowMode);
            if (lastSnapshot != null)
            {
                ReplaceCodexIcon(preferred == null
                    ? (int?)null
                    : preferred.RemainingPercent);
            }
            codexTray.Text = lastSnapshot == null
                ? "Codex usage unavailable - click to retry"
                : preferred == null
                    ? "Codex last known data has no quota window"
                : TruncateTooltip(
                    string.Format(
                        "Codex: last known {0}% left at {1:g}",
                        preferred.RemainingPercent,
                        lastSnapshot.ObservedAtUtc.ToLocalTime()));
        }

        private static string FriendlyError(Exception error)
        {
            string message = error == null ? string.Empty : error.Message;
            string lower = message.ToLowerInvariant();
            if (lower.Contains("not logged in")
                || lower.Contains("authentication")
                || lower.Contains("unauthorized")
                || lower.Contains("sign in"))
            {
                return "Sign in to Codex, then choose Refresh.";
            }
            return string.IsNullOrEmpty(message)
                ? "UsageApp could not reach the local Codex app-server."
                : "Codex connection failed: " + message;
        }

        private static string TruncateTooltip(string value)
        {
            return value.Length <= 63 ? value : value.Substring(0, 63);
        }

        private void ReplaceCodexIcon(int? percentage)
        {
            Icon next = TrayIconRenderer.Create(
                percentage,
                settings.TrayFontName,
                settings.CodexTrayColor,
                settings.TrayEdgeMode);
            codexTray.Icon = next;
            Icon previous = codexDynamicIcon;
            codexDynamicIcon = next;
            if (previous != null)
            {
                previous.Dispose();
            }
        }

        private void ReplaceClaudeIcon(int? percentage)
        {
            Icon next = TrayIconRenderer.Create(
                percentage,
                settings.TrayFontName,
                settings.ClaudeTrayColor,
                settings.TrayEdgeMode);
            claudeTray.Icon = next;
            Icon previous = claudeDynamicIcon;
            claudeDynamicIcon = next;
            if (previous != null)
            {
                previous.Dispose();
            }
        }

        private void UpdateClaudeTray(ClaudeQuotaSnapshot snapshot)
        {
            UsageWindow preferred = snapshot == null
                ? null
                : TrayWindowSelector.Select(
                    snapshot.Windows,
                    settings.TrayWindowMode);
            ReplaceClaudeIcon(preferred == null
                ? (int?)null
                : preferred.RemainingPercent);
            claudeTray.Text = snapshot == null
                ? "UsageApp Native - waiting for Claude"
                : preferred == null
                    ? "Claude usage - no quota window returned"
                    : TruncateTooltip(
                        string.Format(
                            snapshot.Status == "live"
                                ? "Claude: {0}% left; updated {1:g}"
                                : "Claude: last known {0}% left at {1:g}",
                            preferred.RemainingPercent,
                            snapshot.ObservedAtUtc.ToLocalTime()));
        }

        private int RefreshIntervalMilliseconds()
        {
            return Math.Max(
                1,
                Math.Min(60, settings.RefreshIntervalMinutes))
                * 60
                * 1000;
        }

        private void ApplySettings()
        {
            settings.Normalize();
            string startupError = null;
            if (settings.StartWithWindows != appliedStartWithWindows)
            {
                bool requested = settings.StartWithWindows;
                if (startupRegistration.TrySetEnabled(
                    Application.ExecutablePath,
                    requested,
                    out startupError))
                {
                    appliedStartWithWindows = requested;
                }
                else
                {
                    settings.StartWithWindows = appliedStartWithWindows;
                }
            }
            bool saved = settingsStore.Save(settings);
            flyout.SetSettingsPersistenceFailed(!saved);
            flyout.SetStartupRegistrationError(startupError);
            refreshTimer.Interval = RefreshIntervalMilliseconds();
            dashboard.ApplySettings();
            UsageWindow preferred = lastSnapshot == null
                ? null
                : TrayWindowSelector.Select(
                    lastSnapshot.Windows,
                    settings.TrayWindowMode);
            ReplaceCodexIcon(preferred == null ? (int?)null : preferred.RemainingPercent);
            UpdateClaudeTray(lastClaudeSnapshot);
            codexTray.Visible = settings.ShowCodexTrayIcon;
            claudeTray.Visible = settings.ShowClaudeTrayIcon;
        }

        private void ExitApplication()
        {
            if (Interlocked.Exchange(ref exiting, 1) != 0)
            {
                return;
            }
            refreshTimer.Stop();
            notificationRefreshTimer.Stop();
            claudeFreshnessTimer.Stop();
            showSignalRegistration.Unregister(null);
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            client.RateLimitsUpdated -= OnRateLimitsUpdated;
            claudeReceiver.SnapshotReceived -= OnClaudeSnapshotReceived;
            claudeReceiver.ReceiverFaulted -= OnClaudeReceiverFaulted;
            claudeReceiver.Stop();
            codexTray.Visible = false;
            claudeTray.Visible = false;
            flyout.Hide();
            dashboard.Hide();
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    client.Dispose();
                }
                catch
                {
                    // Cleanup continues even if the child process already exited.
                }
                try
                {
                    dispatcher.BeginInvoke((MethodInvoker)FinalizeExit);
                }
                catch
                {
                    // Windows is already tearing down the UI thread.
                }
            });
        }

        private void FinalizeExit()
        {
            if (Interlocked.Exchange(ref uiDisposed, 1) != 0)
            {
                return;
            }
            refreshTimer.Dispose();
            notificationRefreshTimer.Dispose();
            claudeFreshnessTimer.Dispose();
            flyout.Dispose();
            dashboard.Dispose();
            codexTray.Dispose();
            claudeTray.Dispose();
            if (codexDynamicIcon != null)
            {
                codexDynamicIcon.Dispose();
                codexDynamicIcon = null;
            }
            if (claudeDynamicIcon != null)
            {
                claudeDynamicIcon.Dispose();
                claudeDynamicIcon = null;
            }
            ExitThread();
            dispatcher.Dispose();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (Interlocked.Exchange(ref uiDisposed, 1) == 0)
                {
                    refreshTimer.Dispose();
                    notificationRefreshTimer.Dispose();
                    claudeFreshnessTimer.Dispose();
                    showSignalRegistration.Unregister(null);
                    SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
                    SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
                    client.RateLimitsUpdated -= OnRateLimitsUpdated;
                    claudeReceiver.SnapshotReceived -= OnClaudeSnapshotReceived;
                    claudeReceiver.ReceiverFaulted -= OnClaudeReceiverFaulted;
                    claudeReceiver.Dispose();
                    client.Dispose();
                    flyout.Dispose();
                    dashboard.Dispose();
                    dispatcher.Dispose();
                    codexTray.Dispose();
                    claudeTray.Dispose();
                    if (codexDynamicIcon != null)
                    {
                        codexDynamicIcon.Dispose();
                        codexDynamicIcon = null;
                    }
                    if (claudeDynamicIcon != null)
                    {
                        claudeDynamicIcon.Dispose();
                        claudeDynamicIcon = null;
                    }
                }
            }
            base.Dispose(disposing);
        }
    }

    internal static class TrayIconRenderer
    {
        private const string DefaultFont = "Consolas";
        private static readonly Color DefaultCodexColor = Color.FromArgb(48, 113, 141);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr handle);

        public static Icon Create(int? percentage)
        {
            return Create(percentage, DefaultFont, DefaultCodexColor);
        }

        public static Icon Create(
            int? percentage,
            string fontName,
            Color providerColor)
        {
            return Create(percentage, fontName, providerColor, "None");
        }

        public static Icon Create(
            int? percentage,
            string fontName,
            Color providerColor,
            string edgeMode)
        {
            int shellPixels = Math.Max(
                16,
                Math.Min(64, (int)Math.Round(16 * NativeDrawing.SystemScale)));
            return Create(
                percentage,
                fontName,
                providerColor,
                edgeMode,
                shellPixels);
        }

        public static Icon Create(
            int? percentage,
            string fontName,
            Color providerColor,
            int outputSize)
        {
            return Create(
                percentage,
                fontName,
                providerColor,
                "None",
                outputSize);
        }

        public static Icon Create(
            int? percentage,
            string fontName,
            Color providerColor,
            string edgeMode,
            int outputSize)
        {
            using (Bitmap bitmap = CreateBitmap(
                percentage,
                outputSize,
                string.IsNullOrEmpty(fontName) ? DefaultFont : fontName,
                providerColor.IsEmpty ? DefaultCodexColor : providerColor,
                edgeMode))
            {
                IntPtr handle = bitmap.GetHicon();
                try
                {
                    using (Icon temporary = Icon.FromHandle(handle))
                    {
                        return (Icon)temporary.Clone();
                    }
                }
                finally
                {
                    DestroyIcon(handle);
                }
            }
        }

        internal static Bitmap CreateBitmap(
            int? percentage,
            int outputSize,
            string fontName,
            Color providerColor)
        {
            return CreateBitmap(
                percentage,
                outputSize,
                fontName,
                providerColor,
                "None");
        }

        internal static Bitmap CreateBitmap(
            int? percentage,
            int outputSize,
            string fontName,
            Color providerColor,
            string edgeMode)
        {
            outputSize = Math.Max(16, Math.Min(64, outputSize));
            const int oversample = 4;
            int masterSize = outputSize * oversample;
            string text = percentage.HasValue
                ? Math.Max(0, Math.Min(100, percentage.Value)).ToString()
                : "?";
            Color textColor = SystemInformation.HighContrast
                ? SystemColors.HighlightText
                : providerColor;
            Color edgeColor = ResolveEdgeColor(textColor, edgeMode);

            using (Bitmap master = new Bitmap(
                masterSize,
                masterSize,
                PixelFormat.Format32bppPArgb))
            using (Graphics graphics = Graphics.FromImage(master))
            using (GraphicsPath path = CreateFittedTextPath(
                text,
                fontName,
                masterSize,
                masterSize * (62.0f / 64.0f),
                masterSize * (60.0f / 64.0f)))
            using (SolidBrush brush = new SolidBrush(textColor))
            {
                graphics.Clear(Color.Transparent);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.CompositingMode = CompositingMode.SourceCopy;
                if (edgeColor.A > 0)
                {
                    float edgeWidth = Math.Max(2.0f, masterSize / 24.0f);
                    using (Pen edge = new Pen(edgeColor, edgeWidth))
                    {
                        edge.LineJoin = LineJoin.Round;
                        graphics.DrawPath(edge, path);
                    }
                }
                graphics.FillPath(brush, path);

                Bitmap output = new Bitmap(
                    outputSize,
                    outputSize,
                    PixelFormat.Format32bppPArgb);
                using (Graphics resized = Graphics.FromImage(output))
                {
                    resized.Clear(Color.Transparent);
                    resized.CompositingMode = CompositingMode.SourceCopy;
                    resized.CompositingQuality = CompositingQuality.HighQuality;
                    resized.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    resized.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    resized.SmoothingMode = SmoothingMode.HighQuality;
                    resized.DrawImage(
                        master,
                        new Rectangle(0, 0, outputSize, outputSize),
                        new Rectangle(0, 0, masterSize, masterSize),
                        GraphicsUnit.Pixel);
                }
                return output;
            }
        }

        private static GraphicsPath CreateFittedTextPath(
            string text,
            string fontName,
            int canvasSize,
            float targetWidth,
            float targetHeight)
        {
            FontFamily family;
            try
            {
                family = new FontFamily(fontName);
            }
            catch
            {
                family = new FontFamily(DefaultFont);
            }

            using (family)
            using (StringFormat format = (StringFormat)StringFormat.GenericTypographic.Clone())
            {
                format.FormatFlags |= StringFormatFlags.NoClip;
                FontStyle style = family.IsStyleAvailable(FontStyle.Bold)
                    ? FontStyle.Bold
                    : family.IsStyleAvailable(FontStyle.Regular)
                        ? FontStyle.Regular
                        : FontStyle.Bold;
                float low = 1.0f;
                float high = canvasSize * 2.0f;
                for (int iteration = 0; iteration < 24; iteration++)
                {
                    float candidate = (low + high) / 2.0f;
                    using (GraphicsPath probe = new GraphicsPath())
                    {
                        probe.AddString(
                            text,
                            family,
                            (int)style,
                            candidate,
                            Point.Empty,
                            format);
                        RectangleF bounds = probe.GetBounds();
                        if (bounds.Height <= targetHeight
                            && bounds.Width <= targetWidth)
                        {
                            low = candidate;
                        }
                        else
                        {
                            high = candidate;
                        }
                    }
                }

                GraphicsPath result = new GraphicsPath();
                result.AddString(
                    text,
                    family,
                    (int)style,
                    low,
                    Point.Empty,
                    format);
                RectangleF resultBounds = result.GetBounds();
                using (Matrix center = new Matrix())
                {
                    center.Translate(
                        (canvasSize - resultBounds.Width) / 2.0f - resultBounds.Left,
                        (canvasSize - resultBounds.Height) / 2.0f - resultBounds.Top);
                    result.Transform(center);
                }
                return result;
            }
        }

        private static Color ResolveEdgeColor(Color textColor, string edgeMode)
        {
            if (SystemInformation.HighContrast)
            {
                return SystemColors.Highlight;
            }
            if (string.Equals(edgeMode, "None", StringComparison.Ordinal))
            {
                return Color.Transparent;
            }
            if (string.Equals(edgeMode, "Dark", StringComparison.Ordinal))
            {
                return Color.FromArgb(225, 0, 0, 0);
            }
            if (string.Equals(edgeMode, "Light", StringComparison.Ordinal))
            {
                return Color.FromArgb(235, 255, 255, 255);
            }

            double luminance = (0.2126 * textColor.R
                + 0.7152 * textColor.G
                + 0.0722 * textColor.B) / 255.0;
            return luminance >= 0.55
                ? Color.FromArgb(220, 0, 0, 0)
                : Color.FromArgb(235, 255, 255, 255);
        }
    }
}
