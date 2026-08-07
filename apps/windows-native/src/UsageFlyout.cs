using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace UsageApp.Native
{
    internal sealed class UsageFlyout : Form
    {
        private const int CornerRadius = 15;
        private const int WmNcLButtonDown = 0xA1;
        private const int WmDpiChanged = 0x02E0;
        private const int HtCaption = 0x2;
        private const uint MonitorDefaultToNearest = 0x00000002;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(
            IntPtr window,
            int message,
            IntPtr parameter,
            IntPtr value);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateRoundRectRgn(
            int left,
            int top,
            int right,
            int bottom,
            int width,
            int height);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr value);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(
            IntPtr window,
            string subApplicationName,
            string subIdList);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(
            IntPtr window,
            int attribute,
            ref int value,
            int valueSize);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(
            NativePoint point,
            uint flags);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(
            IntPtr monitor,
            MonitorDpiType dpiType,
            out uint dpiX,
            out uint dpiY);

        private readonly bool developmentWindow;
        private readonly FlowLayoutPanel content;
        private readonly Panel contentViewport;
        private readonly DarkScrollBar contentScroll;
        private readonly RoundedButton refreshButton;
        private readonly Panel providerBand;
        private readonly RoundedPanel providerShell;
        private readonly RoundedButton codexButton;
        private readonly RoundedButton claudeButton;
        private readonly RoundedButton usageButton;
        private readonly RoundedButton settingsButton;
        private readonly Timer autoHideTimer;
        private readonly PrecisionWheelAccumulator wheelScroll =
            new PrecisionWheelAccumulator();
        private readonly Dictionary<Control, DpiLayoutSnapshot> dpiLayouts =
            new Dictionary<Control, DpiLayoutSnapshot>();
        private readonly TableLayoutPanel root;
        private readonly NativeSettings settings;
        private float dpiScale;
        private int currentDpi;
        private bool handlingDpiChange;
        private Label productTitle;
        private Label productSubtitle;
        private RoundedButton dashboardButton;
        private RoundedButton pinButton;
        private Label liveStateLabel;
        private UsageSnapshot lastSnapshot;
        private ClaudeQuotaSnapshot lastClaudeSnapshot;
        private string claudeStatusMessage = "Claude monitoring is not connected.";
        private bool claudeConnected;
        private bool bankedExpanded = true;
        private bool refreshing;
        private bool showingSettings;
        private bool showingClaudeInfo;
        private bool notificationCustomEditorSelected;
        private bool settingsRebuildPending;
        private bool pinned;
        private int modalDialogDepth;
        private string currentMessage;
        private string settingsWarning;
        private string startupWarning;
        private Color currentMessageColor = NativePalette.Success;

        public event EventHandler RefreshRequested;
        public event EventHandler DashboardRequested;
        public event EventHandler SettingsChanged;
        public event EventHandler ClaudeConnectRequested;
        public event EventHandler ClaudeDisconnectRequested;
        public event EventHandler QuitRequested;

        public UsageFlyout(bool showInTaskbar, NativeSettings nativeSettings)
        {
            currentDpi = NativeDrawing.SystemDpi;
            dpiScale = currentDpi / 96.0f;
            settings = nativeSettings ?? new NativeSettings();
            settings.Normalize();
            developmentWindow = showInTaskbar;
            Text = "UsageApp Native";
            ShowInTaskbar = showInTaskbar;
            StartPosition = FormStartPosition.Manual;
            FormBorderStyle = FormBorderStyle.None;
            ClientSize = new Size(410, 650);
            MinimumSize = new Size(410, 650);
            MaximumSize = new Size(410, 650);
            AutoScaleMode = AutoScaleMode.None;
            Font = new Font("Segoe UI", 10.5f, FontStyle.Regular, GraphicsUnit.Point);
            BackColor = NativePalette.Shell;
            ForeColor = NativePalette.Primary;
            DoubleBuffered = true;
            Padding = new Padding(ScalePixel(1));
            KeyPreview = true;

            autoHideTimer = new Timer();
            autoHideTimer.Interval = 120;
            autoHideTimer.Tick += delegate
            {
                autoHideTimer.Stop();
                if (Visible
                    && !pinned
                    && modalDialogDepth == 0
                    && !ContainsFocus
                    && !ContainsOpenChoicePicker(content))
                {
                    Hide();
                }
            };

            root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Margin = Padding.Empty;
            root.Padding = Padding.Empty;
            root.ColumnCount = 1;
            root.RowCount = 4;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            Controls.Add(root);

            Panel header = CreateHeader();
            root.Controls.Add(header, 0, 0);

            providerBand = new Panel();
            providerBand.Dock = DockStyle.Fill;
            providerBand.Margin = Padding.Empty;
            providerBand.BackColor = NativePalette.Shell;
            providerBand.Padding = new Padding(12, 7, 12, 5);
            root.Controls.Add(providerBand, 0, 1);

            providerShell = new RoundedPanel();
            providerShell.Dock = DockStyle.Fill;
            providerShell.CornerRadius = 13;
            providerShell.FillColor = NativePalette.ShellRaised;
            providerShell.BorderColor = NativePalette.Border;
            providerBand.Controls.Add(providerShell);

            codexButton = CreateProviderButton(
                "Codex",
                "C",
                NativePalette.Accent,
                true,
                3);
            codexButton.Click += delegate
            {
                SelectProvider(true);
                showingClaudeInfo = false;
                productSubtitle.Text = "Codex monitor";
                ShowUsageView();
            };
            providerShell.Controls.Add(codexButton);

            claudeButton = CreateProviderButton(
                "Claude",
                "A",
                NativePalette.Claude,
                false,
                195);
            claudeButton.Click += delegate
            {
                SelectProvider(false);
                showingClaudeInfo = true;
                productSubtitle.Text = "Claude info · native beta";
                ShowUsageView();
            };
            claudeButton.AccessibleName = "Claude native beta information";
            claudeButton.AccessibleDescription =
                "Shows Claude quota connection status and last known usage.";
            providerShell.Controls.Add(claudeButton);
            providerShell.Resize += delegate { LayoutProviderButtons(); };

            Panel contentHost = new Panel();
            contentHost.Dock = DockStyle.Fill;
            contentHost.Margin = Padding.Empty;
            contentHost.BackColor = NativePalette.Shell;
            root.Controls.Add(contentHost, 0, 2);

            contentScroll = new DarkScrollBar();
            contentScroll.Dock = DockStyle.Right;
            contentScroll.Width = 9;
            contentScroll.ValueChanged += delegate
            {
                content.Top = -contentScroll.Value;
            };
            contentHost.Controls.Add(contentScroll);

            contentViewport = new Panel();
            contentViewport.Dock = DockStyle.Fill;
            contentViewport.Margin = Padding.Empty;
            contentViewport.BackColor = NativePalette.Shell;
            contentViewport.Resize += delegate { LayoutContentSurface(); };
            contentHost.Controls.Add(contentViewport);

            content = new BufferedFlowLayoutPanel();
            content.Location = Point.Empty;
            content.Margin = Padding.Empty;
            content.Padding = new Padding(14, 10, 14, 18);
            content.BackColor = NativePalette.Shell;
            content.AutoScroll = false;
            content.AutoSize = false;
            content.FlowDirection = FlowDirection.TopDown;
            content.WrapContents = false;
            content.TabStop = true;
            content.MouseWheel += ScrollContent;
            contentViewport.MouseWheel += ScrollContent;
            contentViewport.Controls.Add(content);

            Panel footer = new Panel();
            footer.Dock = DockStyle.Fill;
            footer.Margin = Padding.Empty;
            footer.BackColor = NativePalette.ShellRaised;
            footer.Paint += delegate(object sender, PaintEventArgs eventArgs)
            {
                using (Pen line = new Pen(NativePalette.Border))
                {
                    eventArgs.Graphics.DrawLine(line, 0, 0, footer.Width, 0);
                }
            };
            root.Controls.Add(footer, 0, 3);

            usageButton = new RoundedButton();
            usageButton.Text = "◔   Usage";
            usageButton.Location = new Point(12, 8);
            usageButton.Size = new Size(189, 46);
            usageButton.Font = new Font("Segoe UI", 11.0f, FontStyle.Bold);
            usageButton.ForeColor = NativePalette.Accent;
            usageButton.Selected = true;
            usageButton.FillColor = NativePalette.CardSelected;
            usageButton.SelectedColor = NativePalette.CardSelected;
            usageButton.BorderColor = NativePalette.BorderStrong;
            usageButton.AccessibleName = "Usage";
            usageButton.Click += delegate
            {
                ShowUsageView();
            };
            footer.Controls.Add(usageButton);

            settingsButton = new RoundedButton();
            settingsButton.Text = "⚙   Settings";
            settingsButton.Location = new Point(209, 8);
            settingsButton.Size = new Size(189, 46);
            settingsButton.Font = new Font("Segoe UI", 11.0f, FontStyle.Bold);
            settingsButton.ForeColor = NativePalette.Secondary;
            settingsButton.FillColor = NativePalette.ShellRaised;
            settingsButton.BorderColor = Color.Transparent;
            settingsButton.AccessibleName = "Settings";
            settingsButton.Click += delegate
            {
                ShowSettingsView();
            };
            footer.Controls.Add(settingsButton);

            refreshButton = new RoundedButton();
            refreshButton.Text = "Refresh";
            refreshButton.Size = new Size(76, 34);
            refreshButton.Font = new Font("Segoe UI", 9.0f, FontStyle.Bold);
            refreshButton.ForeColor = NativePalette.Primary;
            refreshButton.FillColor = NativePalette.ShellRaised;
            refreshButton.BorderColor = NativePalette.Border;
            refreshButton.AccessibleName = "Refresh Codex usage";
            refreshButton.Click += delegate
            {
                EventHandler handler = RefreshRequested;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            };

            FormClosing += delegate(object sender, FormClosingEventArgs eventArgs)
            {
                if (eventArgs.CloseReason == CloseReason.UserClosing)
                {
                    eventArgs.Cancel = true;
                    Hide();
                }
            };
            Deactivate += delegate
            {
                if (Visible && !pinned && modalDialogDepth == 0)
                {
                    // A ChoicePicker opens a native menu, which briefly makes
                    // this borderless flyout inactive. Delay the outside-click
                    // decision so that first click can open and commit the
                    // picker instead of dismissing the entire flyout.
                    autoHideTimer.Stop();
                    autoHideTimer.Start();
                }
            };
            Activated += delegate { autoHideTimer.Stop(); };
            Resize += delegate
            {
                if (handlingDpiChange)
                {
                    return;
                }
                ApplyRoundedRegion();
                LayoutContentSurface();
            };
            Paint += PaintShellBorder;

            ApplyChromeFonts();
            ScaleControlTree(root);
            ScaleControlTree(refreshButton);
            ApplyProviderVisibility();
            MinimumSize = Size.Empty;
            MaximumSize = Size.Empty;
            MaximumSize = ScaleSize(new Size(410, 650));
            MinimumSize = ScaleSize(new Size(410, 360));
            ClientSize = MaximumSize;
            content.ControlAdded += delegate(object sender, ControlEventArgs eventArgs)
            {
                ScaleControlTree(eventArgs.Control);
                LayoutContentSurface();
            };
            BuildEmptyState("Connecting securely to the local Codex app-server...");
            PerformLayout();
            ApplyRoundedRegion();
        }

        protected override void OnHandleCreated(EventArgs eventArgs)
        {
            base.OnHandleCreated(eventArgs);
            int enabled = 1;
            DwmSetWindowAttribute(Handle, 20, ref enabled, sizeof(int));
            SetWindowTheme(Handle, "DarkMode_Explorer", null);
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg != WmDpiChanged)
            {
                base.WndProc(ref message);
                return;
            }

            int nextDpi = unchecked((int)(message.WParam.ToInt64() & 0xffff));
            Rectangle suggestedBounds;
            bool hasSuggestedBounds = TryReadSuggestedBounds(
                message.LParam,
                out suggestedBounds);
            handlingDpiChange = true;
            try
            {
                // Let WinForms update DeviceDpi for this form and its children first.
                // Our geometry is then reapplied from immutable 96-DPI measurements.
                base.WndProc(ref message);
                ApplyDpiChange(nextDpi, suggestedBounds, hasSuggestedBounds);
            }
            finally
            {
                handlingDpiChange = false;
            }
        }

        private void ApplyDpiChange(
            int nextDpi,
            Rectangle suggestedBounds,
            bool hasSuggestedBounds)
        {
            if (nextDpi <= 0)
            {
                return;
            }
            if (nextDpi == currentDpi)
            {
                if (hasSuggestedBounds)
                {
                    SetBounds(
                        suggestedBounds.Left,
                        suggestedBounds.Top,
                        MaximumSize.Width,
                        MaximumSize.Height,
                        BoundsSpecified.All);
                }
                return;
            }

            int previousDpi = Math.Max(1, currentDpi);
            int previousScroll = contentScroll == null ? 0 : contentScroll.Value;
            double logicalScroll = previousScroll * 96.0 / previousDpi;
            currentDpi = nextDpi;
            dpiScale = currentDpi / 96.0f;

            SuspendLayout();
            try
            {
                Padding = new Padding(ScalePixel(1));
                RescaleControlTree(root);
                RescaleControlTree(refreshButton);
                ApplyChromeFonts();
                ApplyProviderVisibility();
                MinimumSize = Size.Empty;
                MaximumSize = Size.Empty;
                MaximumSize = ScaleSize(new Size(410, 650));
                MinimumSize = ScaleSize(new Size(410, 360));
                if (hasSuggestedBounds)
                {
                    int height = Math.Max(
                        MinimumSize.Height,
                        Math.Min(MaximumSize.Height, suggestedBounds.Height));
                    SetBounds(
                        suggestedBounds.Left,
                        suggestedBounds.Top,
                        MaximumSize.Width,
                        height,
                        BoundsSpecified.All);
                }
                else
                {
                    ClientSize = MaximumSize;
                }

                RebuildCurrentView();
                PerformLayout();
                LayoutContentSurface();
                contentScroll.Value = (int)Math.Round(logicalScroll * dpiScale);
                ApplyRoundedRegion();
                Invalidate(true);
            }
            finally
            {
                ResumeLayout(true);
            }
        }

        private void RebuildCurrentView()
        {
            if (showingSettings)
            {
                BuildSettingsView();
            }
            else if (showingClaudeInfo)
            {
                BuildClaudeInfoView();
            }
            else if (lastSnapshot != null)
            {
                BuildContent(lastSnapshot);
            }
            else
            {
                BuildEmptyState(
                    string.IsNullOrEmpty(currentMessage)
                        ? "Connecting securely to the local Codex app-server..."
                        : currentMessage);
            }
        }

        private static bool TryReadSuggestedBounds(
            IntPtr parameter,
            out Rectangle bounds)
        {
            bounds = Rectangle.Empty;
            if (parameter == IntPtr.Zero)
            {
                return false;
            }
            NativeRect rectangle = (NativeRect)Marshal.PtrToStructure(
                parameter,
                typeof(NativeRect));
            if (rectangle.Right <= rectangle.Left
                || rectangle.Bottom <= rectangle.Top)
            {
                return false;
            }
            bounds = Rectangle.FromLTRB(
                rectangle.Left,
                rectangle.Top,
                rectangle.Right,
                rectangle.Bottom);
            return true;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ClassStyle |= 0x00020000;
                return parameters;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                autoHideTimer.Stop();
                autoHideTimer.Dispose();
            }
            base.Dispose(disposing);
        }

        private Panel CreateHeader()
        {
            Panel header = new Panel();
            header.Dock = DockStyle.Fill;
            header.Margin = Padding.Empty;
            header.BackColor = NativePalette.ShellRaised;
            header.Paint += delegate(object sender, PaintEventArgs eventArgs)
            {
                using (Pen line = new Pen(NativePalette.Border))
                {
                    eventArgs.Graphics.DrawLine(line, 0, header.Height - 1, header.Width, header.Height - 1);
                }
            };
            header.MouseDown += DragHeader;

            BrandLogo logo = new BrandLogo();
            logo.Location = new Point(12, 11);
            logo.Size = new Size(34, 34);
            logo.MouseDown += DragHeader;
            header.Controls.Add(logo);

            productTitle = new Label();
            productTitle.Text = "UsageApp";
            productTitle.AutoSize = false;
            productTitle.Location = new Point(56, 4);
            productTitle.Size = new Size(170, 26);
            productTitle.TextAlign = ContentAlignment.MiddleLeft;
            productTitle.Font = new Font("Segoe UI", 14.0f, FontStyle.Bold);
            productTitle.ForeColor = NativePalette.Primary;
            productTitle.BackColor = Color.Transparent;
            productTitle.MouseDown += DragHeader;
            header.Controls.Add(productTitle);

            productSubtitle = new Label();
            productSubtitle.Text = "Codex monitor";
            productSubtitle.AutoSize = false;
            productSubtitle.Location = new Point(56, 29);
            productSubtitle.Size = new Size(170, 25);
            productSubtitle.TextAlign = ContentAlignment.MiddleLeft;
            productSubtitle.Font = new Font("Segoe UI", 10.5f, FontStyle.Regular);
            productSubtitle.ForeColor = Color.FromArgb(145, 182, 232);
            productSubtitle.BackColor = Color.Transparent;
            productSubtitle.MouseDown += DragHeader;
            header.Controls.Add(productSubtitle);

            dashboardButton = new RoundedButton();
            dashboardButton.Text = "Dashboard";
            dashboardButton.Location = new Point(236, 10);
            dashboardButton.Size = new Size(94, 38);
            dashboardButton.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            dashboardButton.ForeColor = Color.FromArgb(171, 204, 246);
            dashboardButton.FillColor = NativePalette.ShellRaised;
            dashboardButton.BorderColor = NativePalette.Border;
            dashboardButton.AccessibleName = "Open dashboard";
            dashboardButton.Click += delegate
            {
                EventHandler handler = DashboardRequested;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            };
            header.Controls.Add(dashboardButton);

            pinButton = new RoundedButton();
            pinButton.Text = "Pin";
            pinButton.Location = new Point(334, 10);
            pinButton.Size = new Size(64, 38);
            pinButton.Font = new Font("Segoe UI", 9.0f, FontStyle.Bold);
            pinButton.ForeColor = Color.FromArgb(171, 204, 246);
            pinButton.FillColor = NativePalette.ShellRaised;
            pinButton.SelectedColor = NativePalette.CardSelected;
            pinButton.BorderColor = NativePalette.Border;
            pinButton.AccessibleName = "Keep taskbar popup open";
            pinButton.AccessibleDescription =
                "When pinned, the taskbar popup stays open above other applications.";
            pinButton.Click += delegate
            {
                pinned = !pinned;
                TopMost = pinned;
                pinButton.Selected = pinned;
                pinButton.Text = pinned ? "Pinned" : "Pin";
                pinButton.AccessibleName = pinned
                    ? "Unpin taskbar popup"
                    : "Keep taskbar popup open";
                autoHideTimer.Stop();
                pinButton.Invalidate();
            };
            header.Controls.Add(pinButton);
            return header;
        }

        private RoundedButton CreateProviderButton(
            string provider,
            string markText,
            Color markColor,
            bool selected,
            int left)
        {
            RoundedButton button = new RoundedButton();
            button.Text = string.Empty;
            button.Location = new Point(left, 3);
            button.Size = new Size(188, 30);
            button.Font = new Font("Segoe UI", 10.0f, FontStyle.Bold);
            button.ForeColor = selected ? NativePalette.Primary : NativePalette.Secondary;
            button.FillColor = NativePalette.ShellRaised;
            button.SelectedColor = NativePalette.CardSelected;
            button.BorderColor = selected ? NativePalette.BorderStrong : Color.Transparent;
            button.Selected = selected;
            button.AccessibleName = provider + " provider";

            ProviderMark mark = new ProviderMark();
            mark.Mark = markText;
            mark.ProviderColor = markColor;
            mark.Location = new Point(58, 5);
            mark.Size = new Size(20, 20);
            mark.Click += delegate { button.PerformClick(); };
            button.Controls.Add(mark);

            Label providerName = new Label();
            providerName.Text = provider;
            providerName.Location = new Point(82, 0);
            providerName.Size = new Size(92, 30);
            providerName.BackColor = Color.Transparent;
            providerName.TextAlign = ContentAlignment.MiddleLeft;
            providerName.UseMnemonic = false;
            providerName.Cursor = Cursors.Hand;
            providerName.Click += delegate { button.PerformClick(); };
            button.Controls.Add(providerName);
            return button;
        }

        public void ShowLoading()
        {
            refreshing = true;
            refreshButton.Enabled = false;
            currentMessage = "Refreshing live Codex quota...";
            currentMessageColor = NativePalette.Accent;
            if (showingSettings || showingClaudeInfo)
            {
                return;
            }
            if (lastSnapshot == null)
            {
                BuildEmptyState(currentMessage);
            }
            else
            {
                BuildContent(lastSnapshot);
            }
        }

        public void ShowSnapshot(UsageSnapshot snapshot)
        {
            refreshing = false;
            refreshButton.Enabled = true;
            lastSnapshot = snapshot;
            currentMessage = snapshot.Windows.Count == 0
                ? "Codex returned no usage windows."
                : "Live";
            currentMessageColor = snapshot.Windows.Count == 0
                ? NativePalette.Warning
                : NativePalette.Success;
            if (!showingSettings && !showingClaudeInfo)
            {
                BuildContent(snapshot);
            }
        }

        public void ShowCachedSnapshot(UsageSnapshot snapshot)
        {
            refreshing = false;
            lastSnapshot = snapshot;
            currentMessage = "Last known";
            currentMessageColor = NativePalette.Warning;
            if (!showingSettings && !showingClaudeInfo)
            {
                BuildContent(snapshot);
            }
        }

        public void ShowError(string message, DateTime? lastSuccessUtc)
        {
            refreshing = false;
            refreshButton.Enabled = true;
            currentMessage = lastSuccessUtc.HasValue
                ? "Stale · " + SafeMessage(message)
                : SafeMessage(message);
            currentMessageColor = NativePalette.Error;
            if (showingSettings || showingClaudeInfo)
            {
                return;
            }
            if (lastSnapshot != null)
            {
                BuildContent(lastSnapshot);
            }
            else
            {
                BuildEmptyState(currentMessage);
            }
        }

        private void ShowUsageView()
        {
            showingSettings = false;
            usageButton.Selected = true;
            usageButton.ForeColor = NativePalette.Accent;
            settingsButton.Selected = false;
            settingsButton.ForeColor = NativePalette.Secondary;
            usageButton.Invalidate();
            settingsButton.Invalidate();
            contentScroll.Value = 0;
            if (showingClaudeInfo)
            {
                BuildClaudeInfoView();
            }
            else if (lastSnapshot != null)
            {
                BuildContent(lastSnapshot);
            }
            else
            {
                BuildEmptyState(
                    string.IsNullOrEmpty(currentMessage)
                        ? "Connecting securely to the local Codex app-server..."
                        : currentMessage);
            }
        }

        private void ShowSettingsView()
        {
            showingSettings = true;
            usageButton.Selected = false;
            usageButton.ForeColor = NativePalette.Secondary;
            settingsButton.Selected = true;
            settingsButton.ForeColor = NativePalette.Accent;
            usageButton.Invalidate();
            settingsButton.Invalidate();
            contentScroll.Value = 0;
            BuildSettingsView();
        }

        private void BuildClaudeInfoView()
        {
            content.SuspendLayout();
            NativeDrawing.SetRedraw(content, false);
            ClearContentControls();
            liveStateLabel = null;

            Panel heading = new Panel();
            heading.Size = new Size(CardWidth, 72);
            heading.Margin = new Padding(0, 0, 0, 6);
            heading.BackColor = NativePalette.Shell;
            heading.Controls.Add(MakeLabel(
                "ANTHROPIC CLAUDE",
                7.5f,
                FontStyle.Bold,
                NativePalette.Claude,
                new Point(2, 0),
                new Size(CardWidth - 4, 20)));
            heading.Controls.Add(MakeLabel(
                "Claude native beta",
                17.0f,
                FontStyle.Bold,
                NativePalette.Primary,
                new Point(0, 23),
                new Size(CardWidth, 38)));
            content.Controls.Add(heading);

            RoundedPanel status = new RoundedPanel();
            status.Size = new Size(CardWidth, 262);
            status.Margin = new Padding(0, 0, 0, 12);
            status.FillColor = Color.FromArgb(27, 22, 21);
            status.BorderColor = NativePalette.Claude;

            Label state = MakeLabel(
                claudeConnected
                    ? (lastClaudeSnapshot != null && lastClaudeSnapshot.Status == "live"
                        ? "CONNECTED · LIVE"
                        : "CONNECTED · LAST KNOWN")
                    : "NATIVE BETA · NOT CONNECTED",
                8.0f,
                FontStyle.Bold,
                NativePalette.Claude,
                new Point(18, 17),
                new Size(CardWidth - 36, 24));
            status.Controls.Add(state);
            status.Controls.Add(MakeLabel(
                lastClaudeSnapshot != null && lastClaudeSnapshot.PreferredWindow != null
                    ? "Claude: " + lastClaudeSnapshot.PreferredWindow.RemainingPercent
                        + "% remaining"
                    : "Connect Claude Code to show usage",
                14.0f,
                FontStyle.Bold,
                NativePalette.Primary,
                new Point(18, 49),
                new Size(CardWidth - 36, 54)));
            status.Controls.Add(MakeLabel(
                claudeStatusMessage,
                9.5f,
                FontStyle.Regular,
                NativePalette.Secondary,
                new Point(18, 109),
                new Size(CardWidth - 36, 66)));
            status.Controls.Add(MakeLabel(
                lastClaudeSnapshot != null && lastClaudeSnapshot.PreferredWindow != null
                    ? ClaudeQuotaDetail(lastClaudeSnapshot)
                    : "Claude Code must start a new session after connection. Codex monitoring continues separately.",
                9.0f,
                FontStyle.Regular,
                NativePalette.Muted,
                new Point(18, 188),
                new Size(CardWidth - 36, 58)));
            content.Controls.Add(status);

            RoundedButton connection = new RoundedButton();
            connection.Text = claudeConnected ? "Disconnect Claude" : "Connect Claude";
            connection.Size = new Size(CardWidth, 42);
            connection.Margin = new Padding(0, 0, 0, 10);
            connection.Font = CreateInterfaceFont(10.0f, FontStyle.Bold);
            connection.ForeColor = NativePalette.Primary;
            connection.FillColor = claudeConnected
                ? Color.FromArgb(70, 39, 29)
                : Color.FromArgb(62, 43, 34);
            connection.BorderColor = NativePalette.Claude;
            connection.Click += delegate
            {
                EventHandler handler = claudeConnected
                    ? ClaudeDisconnectRequested : ClaudeConnectRequested;
                if (handler != null) handler(this, EventArgs.Empty);
            };
            content.Controls.Add(connection);

            Label scope = MakeLabel(
                "Claude support will remain clearly separate from Codex and will not infer a quota percentage from token activity.",
                8.5f,
                FontStyle.Regular,
                NativePalette.Muted,
                Point.Empty,
                new Size(CardWidth, 58));
            scope.Margin = new Padding(2, 2, 0, 0);
            content.Controls.Add(scope);

            content.ResumeLayout();
            LayoutContentSurface();
            NativeDrawing.SetRedraw(content, true);
        }

        public void SetClaudeIntegrationStatus(bool connected, string message)
        {
            claudeConnected = connected;
            claudeStatusMessage = string.IsNullOrEmpty(message)
                ? (connected ? "Claude is connected." : "Claude monitoring is not connected.")
                : message;
            if (showingClaudeInfo) BuildClaudeInfoView();
        }

        public void ShowClaudeSnapshot(ClaudeQuotaSnapshot snapshot)
        {
            lastClaudeSnapshot = snapshot;
            if (snapshot != null && !string.IsNullOrEmpty(snapshot.Message))
            {
                claudeStatusMessage = snapshot.Message;
            }
            if (showingClaudeInfo) BuildClaudeInfoView();
        }

        private static string ClaudeQuotaDetail(ClaudeQuotaSnapshot snapshot)
        {
            UsageWindow window = snapshot.PreferredWindow;
            if (window == null) return "Claude did not provide a quota window.";
            return window.Label + " · " + UsageFormatting.ResetTime(window.ResetsAtUtc)
                + " · last known " + snapshot.ObservedAtUtc.ToLocalTime().ToString(
                    "ddd, MMM d, h:mm tt", CultureInfo.CurrentCulture);
        }

        private void BuildSettingsView()
        {
            int previousScroll = contentScroll.Value;
            content.SuspendLayout();
            NativeDrawing.SetRedraw(content, false);
            ClearContentControls();

            Panel heading = new Panel();
            heading.Size = new Size(CardWidth, 66);
            heading.Margin = new Padding(0, 0, 0, 4);
            heading.BackColor = NativePalette.Shell;
            heading.Controls.Add(MakeLabel(
                "SETTINGS",
                7.5f,
                FontStyle.Bold,
                NativePalette.Accent,
                new Point(2, 0),
                new Size(180, 18)));
            heading.Controls.Add(MakeLabel(
                "Native preferences",
                16.0f,
                FontStyle.Bold,
                NativePalette.Primary,
                new Point(0, 20),
                new Size(CardWidth, 34)));
            content.Controls.Add(heading);
            if (!string.IsNullOrEmpty(settingsWarning))
            {
                content.Controls.Add(CreateMessageBanner(
                    settingsWarning,
                    NativePalette.Warning));
            }
            if (!string.IsNullOrEmpty(startupWarning))
            {
                content.Controls.Add(CreateMessageBanner(
                    startupWarning,
                    NativePalette.Warning));
            }

            Label providerSection = MakeLabel(
                "PROVIDERS",
                7.5f,
                FontStyle.Bold,
                NativePalette.Accent,
                Point.Empty,
                new Size(CardWidth, 22));
            providerSection.Margin = new Padding(2, 2, 0, 2);
            content.Controls.Add(providerSection);

            List<Control> providerRows = new List<Control>();
            providerRows.Add(CreateToggleSetting(
                "Show Codex",
                "Show Codex in the popup and dashboard. At least one provider stays on.",
                settings.ShowCodexProvider,
                settings.ShowClaudeProvider,
                "Show Codex provider",
                delegate(bool enabled)
                {
                    settings.ShowCodexProvider = enabled;
                    settings.Normalize();
                    NotifySettingsChanged(true);
                }));
            providerRows.Add(CreateToggleSetting(
                "Show Claude",
                "Show Claude in the popup and dashboard. At least one provider stays on.",
                settings.ShowClaudeProvider,
                settings.ShowCodexProvider,
                "Show Claude provider",
                delegate(bool enabled)
                {
                    settings.ShowClaudeProvider = enabled;
                    settings.Normalize();
                    NotifySettingsChanged(true);
                }));
            providerRows.Add(CreateToggleSetting(
                "Codex taskbar icon",
                settings.ShowCodexProvider
                    ? "Show a blue Codex usage icon in the notification area."
                    : "Turn on Codex above to use its taskbar icon.",
                settings.ShowCodexTrayIcon,
                settings.ShowCodexProvider
                    && (!settings.ShowCodexTrayIcon
                        || settings.ShowClaudeTrayIcon),
                "Show Codex taskbar icon",
                delegate(bool enabled)
                {
                    settings.ShowCodexTrayIcon = enabled;
                    settings.Normalize();
                    NotifySettingsChanged(true);
                }));
            providerRows.Add(CreateToggleSetting(
                "Claude taskbar icon",
                settings.ShowClaudeProvider
                    ? "Show an orange Claude usage icon in the notification area."
                    : "Turn on Claude above to use its taskbar icon.",
                settings.ShowClaudeTrayIcon,
                settings.ShowClaudeProvider
                    && (!settings.ShowClaudeTrayIcon
                        || settings.ShowCodexTrayIcon),
                "Show Claude taskbar icon",
                delegate(bool enabled)
                {
                    settings.ShowClaudeTrayIcon = enabled;
                    settings.Normalize();
                    NotifySettingsChanged(true);
                }));
            content.Controls.Add(CreateSettingsGroup(providerRows));

            Label alertSection = MakeLabel(
                "CODEX ALERTS",
                7.5f,
                FontStyle.Bold,
                NativePalette.Accent,
                Point.Empty,
                new Size(CardWidth, 22));
            alertSection.Margin = new Padding(2, 2, 0, 2);
            content.Controls.Add(alertSection);

            List<Control> alertRows = new List<Control>();
            alertRows.Add(CreateToggleSetting(
                "Usage warnings",
                "Show a Windows notification when remaining Codex usage crosses a warning level.",
                settings.CodexQuotaNotificationsEnabled,
                "Codex usage alerts",
                delegate(bool enabled)
                {
                    settings.CodexQuotaNotificationsEnabled = enabled;
                    NotifySettingsChanged(true);
                }));

            if (settings.CodexQuotaNotificationsEnabled)
            {
                string[] presetLabels = new string[]
                {
                    "Balanced - 25%, 10%, 5%",
                    "Critical - 10%, 5%",
                    "Early - 50%, 25%, 10%, 5%",
                    "Custom"
                };
                int presetIndex = NotificationPresetIndex(
                    settings.CodexQuotaNotificationThresholdsCsv);
                if (notificationCustomEditorSelected)
                {
                    presetIndex = presetLabels.Length - 1;
                }
                alertRows.Add(CreateFullWidthChoiceSetting(
                    "Warning preset",
                    "Choose when UsageApp should warn you.",
                    presetLabels,
                    presetIndex,
                    delegate(int index)
                    {
                        string[] presets =
                            NativeSettings.CodexQuotaNotificationPresetOptions;
                        if (index >= presets.Length)
                        {
                            notificationCustomEditorSelected = true;
                            ScheduleSettingsRebuild();
                            return;
                        }
                        notificationCustomEditorSelected = false;
                        settings.CodexQuotaNotificationThresholdsCsv =
                            presets[Math.Max(0, index)];
                        NotifySettingsChanged(true);
                    }));

                if (notificationCustomEditorSelected || presetIndex == 3)
                {
                    notificationCustomEditorSelected = true;
                    alertRows.Add(CreateCustomThresholdSetting());
                }
            }
            content.Controls.Add(CreateSettingsGroup(alertRows));

            Label appearanceSection = MakeLabel(
                "APPEARANCE AND REFRESH",
                7.5f,
                FontStyle.Bold,
                NativePalette.Accent,
                Point.Empty,
                new Size(CardWidth, 22));
            appearanceSection.Margin = new Padding(2, 2, 0, 2);
            content.Controls.Add(appearanceSection);

            List<Control> settingRows = new List<Control>();
            settingRows.Add(CreateChoiceSetting(
                "App text size",
                "Increase text in the taskbar-side window and dashboard.",
                TextScaleLabels(),
                TextScaleIndex(settings.FlyoutTextScale),
                delegate(int index)
                {
                    int[] options = NativeSettings.TextScaleOptions;
                    settings.FlyoutTextScale = options[
                        Math.Max(0, Math.Min(options.Length - 1, index))];
                    NotifySettingsChanged(true);
                }));

            settingRows.Add(CreateChoiceSetting(
                "Interface font",
                "Used for quota, reset, and settings text.",
                NativeSettings.InterfaceFontOptions,
                StringIndex(
                    NativeSettings.InterfaceFontOptions,
                    settings.InterfaceFontName),
                delegate(int index)
                {
                    string[] options = NativeSettings.InterfaceFontOptions;
                    settings.InterfaceFontName = options[
                        Math.Max(0, Math.Min(options.Length - 1, index))];
                    NotifySettingsChanged(true);
                }));

            settingRows.Add(CreateChoiceSetting(
                "Taskbar number font",
                "Consolas matches the large text-only icon you preferred.",
                NativeSettings.TrayFontOptions,
                StringIndex(NativeSettings.TrayFontOptions, settings.TrayFontName),
                delegate(int index)
                {
                    string[] options = NativeSettings.TrayFontOptions;
                    settings.TrayFontName = options[
                        Math.Max(0, Math.Min(options.Length - 1, index))];
                    NotifySettingsChanged(false);
                }));

            string[] colorNames = new string[]
            {
                "Automatic contrast",
                "Original blue + orange",
                "Bright cyan + amber",
                "Dark blue + burnt orange"
            };
            settingRows.Add(CreateChoiceSetting(
                "Taskbar color pair",
                "Keeps Codex blue and Claude orange while adapting readability.",
                colorNames,
                StringIndex(
                    NativeSettings.TrayColorPresetOptions,
                    settings.TrayColorPreset),
                delegate(int index)
                {
                    string[] options = NativeSettings.TrayColorPresetOptions;
                    settings.TrayColorPreset = options[
                        Math.Max(0, Math.Min(options.Length - 1, index))];
                    NotifySettingsChanged(false);
                }));

            string[] edgeNames = new string[]
            {
                "Automatic contrast edge",
                "No edge",
                "Dark edge",
                "Light edge"
            };
            settingRows.Add(CreateChoiceSetting(
                "Taskbar number edge",
                "A thin edge helps tiny numbers survive light and dark taskbars.",
                edgeNames,
                StringIndex(
                    NativeSettings.TrayEdgeModeOptions,
                    settings.TrayEdgeMode),
                delegate(int index)
                {
                    string[] options = NativeSettings.TrayEdgeModeOptions;
                    settings.TrayEdgeMode = options[
                        Math.Max(0, Math.Min(options.Length - 1, index))];
                    NotifySettingsChanged(false);
                }));

            string[] trayWindowNames = new string[]
            {
                "Lowest remaining (recommended)",
                "Shortest usage window",
                "Longest usage window"
            };
            settingRows.Add(CreateChoiceSetting(
                "Taskbar number source",
                "Choose which live limit each provider's number represents.",
                trayWindowNames,
                StringIndex(
                    NativeSettings.TrayWindowModeOptions,
                    settings.TrayWindowMode),
                delegate(int index)
                {
                    string[] options = NativeSettings.TrayWindowModeOptions;
                    settings.TrayWindowMode = options[
                        Math.Max(0, Math.Min(options.Length - 1, index))];
                    NotifySettingsChanged(false);
                }));

            string[] refreshLabels = RefreshIntervalLabels();
            settingRows.Add(CreateChoiceSetting(
                "Refresh interval",
                "How often UsageApp asks the local Codex app-server.",
                refreshLabels,
                RefreshIntervalIndex(settings.RefreshIntervalMinutes),
                delegate(int index)
                {
                    int[] options = NativeSettings.RefreshOptions;
                    settings.RefreshIntervalMinutes = options[
                        Math.Max(0, Math.Min(options.Length - 1, index))];
                    NotifySettingsChanged(false);
                }));
            content.Controls.Add(CreateSettingsGroup(settingRows));

            Label windowsSection = MakeLabel(
                "WINDOWS",
                7.5f,
                FontStyle.Bold,
                NativePalette.Accent,
                Point.Empty,
                new Size(CardWidth, 22));
            windowsSection.Margin = new Padding(2, 2, 0, 2);
            content.Controls.Add(windowsSection);
            List<Control> windowsRows = new List<Control>();
            windowsRows.Add(CreateToggleSetting(
                "Start with Windows",
                "Launch quietly in the notification area after you sign in.",
                settings.StartWithWindows,
                "Start UsageApp with Windows",
                delegate(bool enabled)
                {
                    settings.StartWithWindows = enabled;
                    NotifySettingsChanged(true);
                }));
            content.Controls.Add(CreateSettingsGroup(windowsRows));

            RoundedPanel note = new RoundedPanel();
            note.Size = new Size(CardWidth, 104);
            note.Margin = new Padding(0, 2, 0, 10);
            note.FillColor = Color.FromArgb(13, 24, 37);
            note.BorderColor = NativePalette.Border;
            note.Controls.Add(MakeLabel(
                "Native beta",
                11.0f,
                FontStyle.Bold,
                NativePalette.Primary,
                new Point(14, 12),
                new Size(CardWidth - 28, 26)));
            note.Controls.Add(MakeLabel(
                "Codex quota and optional daily activity are live. Claude status-line quota is experimental and still needs subscribed-account testing.",
                8.7f,
                FontStyle.Regular,
                NativePalette.Secondary,
                new Point(14, 41),
                new Size(CardWidth - 28, 52)));
            content.Controls.Add(note);

            RoundedButton quit = new RoundedButton();
            quit.Text = "Quit UsageApp Native";
            quit.Size = new Size(CardWidth, 42);
            quit.Margin = new Padding(0, 0, 0, 18);
            quit.Font = CreateInterfaceFont(9.5f, FontStyle.Bold);
            quit.ForeColor = Color.FromArgb(254, 202, 202);
            quit.FillColor = Color.FromArgb(39, 21, 26);
            quit.HoverColor = Color.FromArgb(58, 28, 34);
            quit.BorderColor = Color.FromArgb(113, 58, 66);
            quit.Click += delegate
            {
                EventHandler handler = QuitRequested;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            };
            content.Controls.Add(quit);

            content.ResumeLayout();
            LayoutContentSurface();
            contentScroll.Value = previousScroll;
            NativeDrawing.SetRedraw(content, true);
        }

        private Control CreateToggleSetting(
            string title,
            string description,
            bool isChecked,
            string accessibleName,
            Action<bool> changed)
        {
            return CreateToggleSetting(
                title,
                description,
                isChecked,
                true,
                accessibleName,
                changed);
        }

        private Control CreateToggleSetting(
            string title,
            string description,
            bool isChecked,
            bool isEnabled,
            string accessibleName,
            Action<bool> changed)
        {
            float textScale = settings.FlyoutTextScale / 100.0f;
            int rowHeight = Math.Max(76, (int)Math.Ceiling(76 * textScale));
            int titleHeight = Math.Max(24, (int)Math.Ceiling(24 * textScale));
            Panel row = CreateSettingRow(rowHeight);

            row.Controls.Add(MakeLabel(
                title,
                10.5f,
                FontStyle.Bold,
                isEnabled ? NativePalette.Primary : NativePalette.Muted,
                new Point(14, 9),
                new Size(CardWidth - 112, titleHeight)));
            row.Controls.Add(MakeLabel(
                description,
                8.2f,
                FontStyle.Regular,
                isEnabled ? NativePalette.Muted : NativePalette.Secondary,
                new Point(14, 9 + titleHeight),
                new Size(
                    CardWidth - 112,
                    Math.Max(32, rowHeight - titleHeight - 14))));

            CheckBox toggle = new CheckBox();
            toggle.Text = isChecked ? "On" : "Off";
            toggle.Checked = isChecked;
            toggle.Enabled = isEnabled;
            toggle.AutoSize = false;
            toggle.Location = new Point(CardWidth - 90, (rowHeight - 34) / 2);
            toggle.Size = new Size(76, 34);
            toggle.Font = CreateInterfaceFont(9.0f, FontStyle.Bold);
            toggle.ForeColor = isChecked
                ? NativePalette.Accent
                : NativePalette.Secondary;
            toggle.BackColor = NativePalette.Card;
            toggle.FlatStyle = FlatStyle.Flat;
            toggle.CheckAlign = ContentAlignment.MiddleLeft;
            toggle.TextAlign = ContentAlignment.MiddleCenter;
            toggle.Cursor = isEnabled ? Cursors.Hand : Cursors.Default;
            toggle.AccessibleName = accessibleName;
            toggle.AccessibleDescription = description;
            toggle.TabIndex = 0;
            toggle.CheckedChanged += delegate
            {
                toggle.Text = toggle.Checked ? "On" : "Off";
                toggle.ForeColor = toggle.Checked
                    ? NativePalette.Accent
                    : NativePalette.Secondary;
                changed(toggle.Checked);
            };
            row.Controls.Add(toggle);
            return row;
        }

        private Control CreateFullWidthChoiceSetting(
            string title,
            string description,
            string[] options,
            int selectedIndex,
            Action<int> changed)
        {
            float textScale = settings.FlyoutTextScale / 100.0f;
            int rowHeight = Math.Max(108, (int)Math.Ceiling(108 * textScale));
            int titleHeight = Math.Max(24, (int)Math.Ceiling(24 * textScale));
            Panel row = CreateSettingRow(rowHeight);
            row.Controls.Add(MakeLabel(
                title,
                10.5f,
                FontStyle.Bold,
                NativePalette.Primary,
                new Point(14, 9),
                new Size(CardWidth - 28, titleHeight)));
            row.Controls.Add(MakeLabel(
                description,
                8.2f,
                FontStyle.Regular,
                NativePalette.Muted,
                new Point(14, 9 + titleHeight),
                new Size(CardWidth - 28, 28)));

            ChoicePicker choice = new ChoicePicker();
            choice.ForeColor = NativePalette.Primary;
            choice.Font = CreateInterfaceFont(8.5f, FontStyle.Regular);
            choice.Location = new Point(14, rowHeight - 46);
            choice.Size = new Size(CardWidth - 28, 36);
            choice.AccessibleName = "Notification preset";
            choice.AccessibleDescription =
                "Choose preset warning percentages or Custom.";
            choice.TabIndex = 1;
            choice.Options = options;
            choice.SelectedIndex = Math.Max(
                0,
                Math.Min(options.Length - 1, selectedIndex));
            choice.SelectedIndexChanged += delegate
            {
                changed(choice.SelectedIndex);
            };
            row.Controls.Add(choice);
            return row;
        }

        private Control CreateCustomThresholdSetting()
        {
            float textScale = settings.FlyoutTextScale / 100.0f;
            int rowHeight = Math.Max(142, (int)Math.Ceiling(142 * textScale));
            int titleHeight = Math.Max(24, (int)Math.Ceiling(24 * textScale));
            int descriptionTop = 9 + titleHeight;
            int inputTop = descriptionTop + Math.Max(
                34,
                (int)Math.Ceiling(34 * textScale));
            Panel row = CreateSettingRow(rowHeight);

            row.Controls.Add(MakeLabel(
                "Warning percentages",
                10.5f,
                FontStyle.Bold,
                NativePalette.Primary,
                new Point(14, 9),
                new Size(CardWidth - 28, titleHeight)));
            row.Controls.Add(MakeLabel(
                "Enter 1 to 5 whole percentages, separated by commas.",
                8.2f,
                FontStyle.Regular,
                NativePalette.Muted,
                new Point(14, descriptionTop),
                new Size(CardWidth - 28, inputTop - descriptionTop)));

            TextBox input = new TextBox();
            input.Text = settings.CodexQuotaNotificationThresholdsCsv;
            input.Location = new Point(14, inputTop);
            input.Width = CardWidth - 28;
            input.Font = CreateInterfaceFont(10.0f, FontStyle.Regular);
            input.ForeColor = NativePalette.Primary;
            input.BackColor = Color.FromArgb(9, 20, 33);
            input.BorderStyle = BorderStyle.FixedSingle;
            input.MaxLength = 30;
            input.AccessibleName = "Custom warning percentages";
            input.AccessibleDescription =
                "Enter one to five whole percentages from 1 through 99, separated by commas.";
            input.TabIndex = 2;
            row.Controls.Add(input);

            Label feedback = MakeLabel(
                NotificationThresholdPreview(
                    settings.CodexQuotaNotificationThresholdsCsv),
                8.0f,
                FontStyle.Regular,
                NativePalette.Secondary,
                new Point(14, input.Bottom + 5),
                new Size(CardWidth - 28, Math.Max(20, rowHeight - input.Bottom - 8)));
            feedback.AccessibleName = "Custom warning validation";
            row.Controls.Add(feedback);

            input.TextChanged += delegate
            {
                string normalized;
                string error;
                if (!NativeSettings.TryNormalizeCodexQuotaNotificationThresholdsCsv(
                        input.Text,
                        out normalized,
                        out error))
                {
                    feedback.Text = error;
                    feedback.ForeColor = NativePalette.Warning;
                    return;
                }
                feedback.Text = NotificationThresholdPreview(normalized);
                feedback.ForeColor = NativePalette.Secondary;
                if (!string.Equals(
                        settings.CodexQuotaNotificationThresholdsCsv,
                        normalized,
                        StringComparison.Ordinal))
                {
                    settings.CodexQuotaNotificationThresholdsCsv = normalized;
                    NotifySettingsChanged(false);
                }
            };
            input.Leave += delegate
            {
                string normalized;
                string error;
                if (NativeSettings.TryNormalizeCodexQuotaNotificationThresholdsCsv(
                        input.Text,
                        out normalized,
                        out error)
                    && !string.Equals(input.Text, normalized, StringComparison.Ordinal))
                {
                    input.Text = normalized;
                }
            };
            return row;
        }

        private Panel CreateSettingRow(int rowHeight)
        {
            Panel row = new Panel();
            row.Size = new Size(CardWidth, rowHeight);
            row.Margin = Padding.Empty;
            row.BackColor = Color.Transparent;
            row.Paint += delegate(object sender, PaintEventArgs eventArgs)
            {
                if (row.Parent != null
                    && row.Bottom >= row.Parent.ClientSize.Height)
                {
                    return;
                }
                using (Pen separator = new Pen(NativePalette.Border))
                {
                    int inset = NativeDrawing.Dpi(row, 14);
                    eventArgs.Graphics.DrawLine(
                        separator,
                        inset,
                        row.Height - 1,
                        row.Width - inset,
                        row.Height - 1);
                }
            };
            return row;
        }

        private static int NotificationPresetIndex(string csv)
        {
            string normalized =
                NativeSettings.NormalizeCodexQuotaNotificationThresholdsCsv(csv);
            string[] presets = NativeSettings.CodexQuotaNotificationPresetOptions;
            for (int index = 0; index < presets.Length; index++)
            {
                if (string.Equals(
                    normalized,
                    presets[index],
                    StringComparison.Ordinal))
                {
                    return index;
                }
            }
            return presets.Length;
        }

        private static string NotificationThresholdPreview(string csv)
        {
            int[] thresholds =
                NativeSettings.ParseCodexQuotaNotificationThresholds(csv);
            if (thresholds.Length == 0)
            {
                return "No valid warning percentages yet.";
            }
            StringBuilder text = new StringBuilder("Warnings at ");
            for (int index = 0; index < thresholds.Length; index++)
            {
                if (index > 0)
                {
                    text.Append(index == thresholds.Length - 1 ? " and " : ", ");
                }
                text.Append(thresholds[index]);
                text.Append('%');
            }
            text.Append(" remaining.");
            return text.ToString();
        }

        private Control CreateChoiceSetting(
            string title,
            string description,
            string[] options,
            int selectedIndex,
            Action<int> changed)
        {
            float textScale = settings.FlyoutTextScale / 100.0f;
            int rowHeight = Math.Max(82, (int)Math.Ceiling(82 * textScale));
            int titleHeight = Math.Max(
                26,
                (int)Math.Ceiling(26 * textScale));
            int descriptionTop = 10 + titleHeight + 1;
            Panel row = CreateSettingRow(rowHeight);

            row.Controls.Add(MakeLabel(
                title,
                10.5f,
                FontStyle.Bold,
                NativePalette.Primary,
                new Point(14, 10),
                new Size(210, titleHeight)));
            row.Controls.Add(MakeLabel(
                description,
                8.2f,
                FontStyle.Regular,
                NativePalette.Muted,
                new Point(14, descriptionTop),
                new Size(
                    205,
                    Math.Max(37, rowHeight - descriptionTop - 8))));

            ChoicePicker choice = new ChoicePicker();
            choice.ForeColor = NativePalette.Primary;
            choice.Font = CreateInterfaceFont(8.5f, FontStyle.Regular);
            choice.Location = new Point(226, Math.Max(12, (rowHeight - 36) / 2));
            choice.Size = new Size(Math.Max(120, CardWidth - 240), 36);
            choice.AccessibleName = title;
            choice.Options = options;
            choice.SelectedIndex = Math.Max(
                0,
                Math.Min(options.Length - 1, selectedIndex));
            choice.SelectedIndexChanged += delegate
            {
                changed(choice.SelectedIndex);
            };
            row.Controls.Add(choice);
            return row;
        }

        private Control CreateSettingsGroup(IList<Control> rows)
        {
            RoundedPanel group = new RoundedPanel();
            group.Width = CardWidth;
            group.Margin = new Padding(0, 0, 0, 10);
            group.FillColor = NativePalette.Card;
            group.BorderColor = NativePalette.Border;
            int top = 0;
            foreach (Control row in rows)
            {
                row.Location = new Point(0, top);
                row.Width = CardWidth;
                group.Controls.Add(row);
                top += row.Height;
            }
            group.Height = top;
            return group;
        }

        private void NotifySettingsChanged(bool rebuild)
        {
            settings.Normalize();
            ApplyProviderVisibility();
            ApplyChromeFonts();
            EventHandler handler = SettingsChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
            if (rebuild && showingSettings)
            {
                ScheduleSettingsRebuild();
            }
        }

        private static bool ContainsOpenChoicePicker(Control root)
        {
            if (root == null)
            {
                return false;
            }
            ChoicePicker picker = root as ChoicePicker;
            if (picker != null && picker.IsDropDownOpen)
            {
                return true;
            }
            foreach (Control child in root.Controls)
            {
                if (ContainsOpenChoicePicker(child))
                {
                    return true;
                }
            }
            return false;
        }

        public void SetSettingsPersistenceFailed(bool failed)
        {
            string next = failed
                ? "Changes are active for this session, but Windows could not save them."
                : null;
            if (string.Equals(settingsWarning, next, StringComparison.Ordinal))
            {
                return;
            }
            settingsWarning = next;
            if (showingSettings)
            {
                ScheduleSettingsRebuild();
            }
        }

        public void SetStartupRegistrationError(string error)
        {
            string next = string.IsNullOrWhiteSpace(error)
                ? null
                : error.Trim();
            if (string.Equals(startupWarning, next, StringComparison.Ordinal))
            {
                return;
            }
            startupWarning = next;
            if (showingSettings)
            {
                ScheduleSettingsRebuild();
            }
        }

        private void ScheduleSettingsRebuild()
        {
            if (settingsRebuildPending)
            {
                return;
            }
            if (!IsHandleCreated)
            {
                BuildSettingsView();
                return;
            }
            settingsRebuildPending = true;
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    settingsRebuildPending = false;
                    if (showingSettings && !IsDisposed)
                    {
                        BuildSettingsView();
                    }
                });
            }
            catch (ObjectDisposedException)
            {
                settingsRebuildPending = false;
            }
            catch (InvalidOperationException)
            {
                settingsRebuildPending = false;
            }
        }

        private static int StringIndex(string[] values, string target)
        {
            for (int index = 0; index < values.Length; index++)
            {
                if (string.Equals(
                    values[index],
                    target,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }
            }
            return 0;
        }

        private static string[] TextScaleLabels()
        {
            int[] options = NativeSettings.TextScaleOptions;
            string[] labels = new string[options.Length];
            for (int index = 0; index < options.Length; index++)
            {
                labels[index] = options[index] + "%";
            }
            return labels;
        }

        private static int TextScaleIndex(int value)
        {
            int[] options = NativeSettings.TextScaleOptions;
            for (int index = 0; index < options.Length; index++)
            {
                if (options[index] == value)
                {
                    return index;
                }
            }
            return 0;
        }

        private static string[] RefreshIntervalLabels()
        {
            int[] options = NativeSettings.RefreshOptions;
            string[] labels = new string[options.Length];
            for (int index = 0; index < options.Length; index++)
            {
                labels[index] = options[index] == 1
                    ? "1 minute"
                    : options[index] + " minutes";
            }
            return labels;
        }

        private static int RefreshIntervalIndex(int value)
        {
            int[] options = NativeSettings.RefreshOptions;
            for (int index = 0; index < options.Length; index++)
            {
                if (options[index] == value)
                {
                    return index;
                }
            }
            return 2;
        }

        private static string SafeMessage(string message)
        {
            return string.IsNullOrEmpty(message)
                ? "Codex usage is temporarily unavailable."
                : message;
        }

        private void BuildEmptyState(string message)
        {
            content.SuspendLayout();
            ClearContentControls();

            RoundedPanel panel = new RoundedPanel();
            panel.Size = new Size(CardWidth, 170);
            panel.Margin = Padding.Empty;
            panel.FillColor = NativePalette.Card;
            panel.BorderColor = NativePalette.Border;

            Label title = MakeLabel(
                "Codex usage",
                18.0f,
                FontStyle.Bold,
                NativePalette.Primary,
                new Point(20, 22),
                new Size(320, 34));
            panel.Controls.Add(title);

            Label description = MakeLabel(
                message,
                11.0f,
                FontStyle.Regular,
                NativePalette.Secondary,
                new Point(20, 69),
                new Size(320, 64));
            panel.Controls.Add(description);
            content.Controls.Add(panel);
            content.ResumeLayout();
            LayoutContentSurface();
        }

        private void BuildContent(UsageSnapshot snapshot)
        {
            int previousScroll = contentScroll.Value;
            content.SuspendLayout();
            NativeDrawing.SetRedraw(content, false);
            ClearContentControls();
            liveStateLabel = null;

            if (!string.IsNullOrEmpty(currentMessage)
                && currentMessage != "Live"
                && !refreshing)
            {
                content.Controls.Add(CreateMessageBanner(currentMessage, currentMessageColor));
            }

            content.Controls.Add(CreateHeroCard(snapshot));
            content.Controls.Add(CreateSectionHeader("RATE LIMITS", "Usage windows", true));

            foreach (UsageWindow window in DisplayWindows(snapshot))
            {
                content.Controls.Add(CreateUsageWindowCard(window));
            }

            content.Controls.Add(CreateBankedSectionHeader(snapshot.BankedResets));
            Control freshness = CreateBankedFreshness(snapshot);
            if (freshness != null)
            {
                content.Controls.Add(freshness);
            }
            content.Controls.Add(CreateBankedToggle());
            if (bankedExpanded)
            {
                foreach (BankedReset reset in snapshot.BankedResets.Items)
                {
                    content.Controls.Add(CreateBankedCard(reset));
                }
            }

            Label updated = MakeLabel(
                "Last known: "
                    + snapshot.ObservedAtUtc.ToLocalTime().ToString(
                        "ddd, MMM d, h:mm tt",
                        CultureInfo.CurrentCulture),
                8.5f,
                FontStyle.Regular,
                NativePalette.Muted,
                Point.Empty,
                new Size(CardWidth, 40));
            updated.Margin = new Padding(2, 8, 0, 0);
            updated.TextAlign = ContentAlignment.MiddleLeft;
            content.Controls.Add(updated);

            content.ResumeLayout();
            LayoutContentSurface();
            contentScroll.Value = previousScroll;
            NativeDrawing.SetRedraw(content, true);
        }

        private Control CreateMessageBanner(string message, Color accent)
        {
            RoundedPanel banner = new RoundedPanel();
            banner.Size = new Size(CardWidth, 58);
            banner.Margin = new Padding(0, 0, 0, 10);
            banner.FillColor = Color.FromArgb(14, 24, 37);
            banner.BorderColor = accent;
            banner.BorderThickness = 1.0f;

            Label label = MakeLabel(
                message,
                9.0f,
                FontStyle.Regular,
                NativePalette.Secondary,
                new Point(14, 10),
                new Size(CardWidth - 28, 38));
            label.TextAlign = ContentAlignment.MiddleLeft;
            banner.Controls.Add(label);
            return banner;
        }

        private Control CreateHeroCard(UsageSnapshot snapshot)
        {
            RoundedPanel card = new RoundedPanel();
            card.Size = new Size(CardWidth, 160);
            card.Margin = new Padding(0, 0, 0, 14);
            card.FillColor = NativePalette.Card;
            card.BorderColor = NativePalette.Border;

            UsageWindow preferred = snapshot.PreferredWindow;
            int? remaining = preferred == null
                ? (int?)null
                : preferred.RemainingPercent;
            RingGauge gauge = new RingGauge();
            gauge.Location = new Point(8, 15);
            gauge.Font = CreateInterfaceFont(10.0f, FontStyle.Regular);
            gauge.Percentage = remaining;
            gauge.AccessibleName = remaining.HasValue
                ? remaining.Value + " percent remaining"
                : "Codex usage percentage unavailable";
            card.Controls.Add(gauge);

            Label provider = MakeLabel(
                "Codex",
                15.0f,
                FontStyle.Bold,
                NativePalette.Primary,
                new Point(132, 22),
                new Size(114, 29));
            card.Controls.Add(provider);

            liveStateLabel = MakeLabel(
                refreshing ? "● Refreshing" : "● " + currentMessage,
                8.5f,
                FontStyle.Bold,
                refreshing ? NativePalette.Accent : currentMessageColor,
                new Point(247, 24),
                new Size(100, 24));
            liveStateLabel.TextAlign = ContentAlignment.MiddleRight;
            card.Controls.Add(liveStateLabel);

            Label plan = MakeLabel(
                string.IsNullOrEmpty(snapshot.PlanType)
                    ? "Codex plan"
                    : snapshot.PlanType + " plan",
                10.5f,
                FontStyle.Regular,
                NativePalette.Secondary,
                new Point(132, 54),
                new Size(205, 24));
            card.Controls.Add(plan);

            DateTime? nextReset = EarliestReset(snapshot);
            Label resetTitle = MakeLabel(
                nextReset.HasValue ? "Next reset" : "Reset time unavailable",
                9.5f,
                FontStyle.Bold,
                NativePalette.Primary,
                new Point(132, 88),
                new Size(205, 22));
            card.Controls.Add(resetTitle);

            Label resetValue = MakeLabel(
                nextReset.HasValue
                    ? nextReset.Value.ToLocalTime().ToString(
                        "ddd, MMM d, h:mm tt",
                        CultureInfo.CurrentCulture)
                    : "Codex did not return a reset timestamp.",
                8.3f,
                FontStyle.Regular,
                NativePalette.Muted,
                new Point(132, 111),
                new Size(214, 42));
            card.Controls.Add(resetValue);
            return card;
        }

        private Control CreateSectionHeader(string eyebrow, string title, bool includeRefresh)
        {
            Panel header = new Panel();
            header.Size = new Size(CardWidth, 55);
            header.Margin = Padding.Empty;
            header.BackColor = NativePalette.Shell;

            Label eyebrowLabel = MakeLabel(
                eyebrow,
                7.5f,
                FontStyle.Bold,
                NativePalette.Accent,
                new Point(2, 0),
                new Size(180, 18));
            header.Controls.Add(eyebrowLabel);

            Label titleLabel = MakeLabel(
                title,
                15.0f,
                FontStyle.Bold,
                NativePalette.Primary,
                new Point(0, 17),
                new Size(220, 35));
            header.Controls.Add(titleLabel);

            if (includeRefresh)
            {
                refreshButton.Location = new Point(
                    ScalePixel(CardWidth) - refreshButton.Width,
                    ScalePixel(10));
                header.Controls.Add(refreshButton);
            }
            return header;
        }

        private Control CreateBankedFreshness(UsageSnapshot snapshot)
        {
            DateTime? countObserved = snapshot.BankedResets.CountObservedAtUtc;
            DateTime? detailsObserved = snapshot.BankedResets.DetailsObservedAtUtc;
            if (!countObserved.HasValue && !detailsObserved.HasValue)
            {
                return null;
            }

            bool stale = (countObserved.HasValue
                    && Math.Abs(
                        (snapshot.ObservedAtUtc - countObserved.Value).TotalSeconds) >= 2)
                || (detailsObserved.HasValue
                    && Math.Abs(
                        (snapshot.ObservedAtUtc - detailsObserved.Value).TotalSeconds) >= 2);
            string text = "Count last known: "
                + ObservationText(countObserved);
            if (snapshot.BankedResets.DetailsAvailable)
            {
                text += Environment.NewLine
                    + snapshot.BankedResets.Items.Count
                    + " expiry rows last known: "
                    + ObservationText(detailsObserved);
            }
            Label label = MakeLabel(
                text,
                8.2f,
                FontStyle.Regular,
                stale ? NativePalette.Warning : NativePalette.Muted,
                Point.Empty,
                new Size(CardWidth, snapshot.BankedResets.DetailsAvailable ? 48 : 28));
            label.Margin = new Padding(2, 0, 0, 5);
            label.TextAlign = ContentAlignment.MiddleLeft;
            return label;
        }

        private Control CreateUsageWindowCard(UsageWindow window)
        {
            int titleExtraHeight = settings.FlyoutTextScale >= 125
                ? 18
                : settings.FlyoutTextScale >= 110
                    ? 10
                    : 0;
            RoundedPanel card = new RoundedPanel();
            card.Size = new Size(CardWidth, 111 + titleExtraHeight);
            card.Margin = new Padding(0, 0, 0, 10);
            card.FillColor = NativePalette.Card;
            card.BorderColor = NativePalette.Border;

            Label name = MakeLabel(
                window.Label,
                10.3f,
                FontStyle.Bold,
                NativePalette.Primary,
                new Point(14, 13),
                new Size(235, 27 + titleExtraHeight));
            card.Controls.Add(name);

            Label percent = MakeLabel(
                window.RemainingPercent.ToString(CultureInfo.CurrentCulture) + "%",
                14.0f,
                FontStyle.Bold,
                NativePalette.Primary,
                new Point(238, 9),
                new Size(108, 34));
            percent.TextAlign = ContentAlignment.TopRight;
            card.Controls.Add(percent);

            UsageProgress progress = new UsageProgress();
            progress.Location = new Point(14, 49 + titleExtraHeight);
            progress.Size = new Size(332, 8);
            progress.Percentage = window.RemainingPercent;
            progress.AccessibleName = window.Label + " " + window.RemainingPercent + " percent remaining";
            card.Controls.Add(progress);

            Label reset = MakeLabel(
                UsageFormatting.ResetTime(window.ResetsAtUtc),
                8.7f,
                FontStyle.Regular,
                NativePalette.Secondary,
                new Point(14, 68 + titleExtraHeight),
                new Size(332, 32));
            card.Controls.Add(reset);
            return card;
        }

        private Control CreateBankedSectionHeader(BankedResetSummary summary)
        {
            Panel header = new Panel();
            header.Size = new Size(CardWidth, 58);
            header.Margin = new Padding(0, 8, 0, 0);
            header.BackColor = NativePalette.Shell;
            header.Tag = "banked-section";

            Label eyebrow = MakeLabel(
                "EXTRA CAPACITY",
                7.5f,
                FontStyle.Bold,
                NativePalette.Accent,
                new Point(2, 0),
                new Size(180, 18));
            header.Controls.Add(eyebrow);

            Label title = MakeLabel(
                "Banked resets",
                15.0f,
                FontStyle.Bold,
                NativePalette.Primary,
                new Point(0, 20),
                new Size(230, 31));
            header.Controls.Add(title);

            Label count = MakeLabel(
                summary.AvailableCount.HasValue
                    ? summary.AvailableCount.Value.ToString(CultureInfo.CurrentCulture)
                    : "—",
                16.0f,
                FontStyle.Bold,
                NativePalette.Primary,
                new Point(274, 9),
                new Size(70, 32));
            count.TextAlign = ContentAlignment.MiddleRight;
            header.Controls.Add(count);

            Label available = MakeLabel(
                "AVAILABLE",
                7.0f,
                FontStyle.Bold,
                NativePalette.Muted,
                new Point(264, 39),
                new Size(80, 17));
            available.TextAlign = ContentAlignment.MiddleRight;
            header.Controls.Add(available);
            return header;
        }

        private Control CreateBankedToggle()
        {
            RoundedButton toggle = new RoundedButton();
            if (lastSnapshot == null || !lastSnapshot.BankedResets.DetailsAvailable)
            {
                toggle.Text = "Expiry dates unavailable in this update";
                toggle.Enabled = false;
            }
            else if (lastSnapshot.BankedResets.Items.Count == 0)
            {
                toggle.Text = "No banked reset expiry rows returned";
                toggle.Enabled = false;
            }
            else
            {
                toggle.Text = (bankedExpanded ? "⌃" : "⌄")
                    + "    "
                    + (bankedExpanded ? "Hide expiry dates" : "Show expiry dates");
            }
            toggle.Size = new Size(CardWidth, 42);
            toggle.Margin = new Padding(0, 0, 0, 10);
            toggle.Font = CreateInterfaceFont(9.5f, FontStyle.Bold);
            toggle.ForeColor = NativePalette.Secondary;
            toggle.FillColor = NativePalette.Shell;
            toggle.HoverColor = NativePalette.ShellRaised;
            toggle.BorderColor = NativePalette.Border;
            toggle.TextAlign = ContentAlignment.MiddleLeft;
            toggle.AccessibleName = toggle.Enabled
                ? bankedExpanded
                    ? "Hide banked reset expiry dates"
                    : "Show banked reset expiry dates"
                : toggle.Text;
            toggle.Click += delegate
            {
                if (!toggle.Enabled)
                {
                    return;
                }
                bankedExpanded = !bankedExpanded;
                if (lastSnapshot != null)
                {
                    BuildContent(lastSnapshot);
                }
            };
            return toggle;
        }

        private Control CreateBankedCard(BankedReset reset)
        {
            DateTime nowUtc = DateTime.UtcNow;
            bool expired = reset.ExpiresAtUtc.HasValue
                && reset.ExpiresAtUtc.Value <= nowUtc;
            RoundedPanel card = new RoundedPanel();
            card.Size = new Size(CardWidth, 152);
            card.Margin = new Padding(0, 0, 0, 10);
            card.FillColor = NativePalette.Card;
            card.BorderColor = NativePalette.Border;

            Label title = MakeLabel(
                string.IsNullOrEmpty(reset.Title) ? "Full reset" : reset.Title,
                11.5f,
                FontStyle.Bold,
                NativePalette.Primary,
                new Point(14, 13),
                new Size(220, 27));
            card.Controls.Add(title);

            RoundedPanel pill = new RoundedPanel();
            pill.Size = new Size(92, 26);
            pill.Location = new Point(252, 12);
            pill.CornerRadius = 13;
            pill.FillColor = expired
                ? Color.FromArgb(55, 30, 38)
                : Color.FromArgb(31, 45, 64);
            pill.BorderColor = Color.Transparent;
            pill.BorderThickness = 0;
            Label pillText = MakeLabel(
                BankedRowStatusText(reset, nowUtc),
                7.3f,
                FontStyle.Bold,
                expired ? NativePalette.Error : NativePalette.Secondary,
                new Point(3, 3),
                new Size(86, 20));
            pillText.TextAlign = ContentAlignment.MiddleCenter;
            pill.Controls.Add(pillText);
            card.Controls.Add(pill);

            Label description = MakeLabel(
                expired
                    ? string.IsNullOrEmpty(reset.Description)
                        ? "This last-known banked reset has expired."
                        : "Last known · " + WrapBankDescription(reset.Description)
                    : string.IsNullOrEmpty(reset.Description)
                    ? "A full Codex rate-limit reset is available."
                    : WrapBankDescription(reset.Description),
                9.0f,
                FontStyle.Regular,
                NativePalette.Secondary,
                new Point(14, 45),
                new Size(330, 42));
            card.Controls.Add(description);

            Label expiry = MakeLabel(
                reset.ExpiresAtUtc.HasValue
                    ? ExpiryHeadline(reset.ExpiresAtUtc.Value)
                    : "Expiry details unavailable",
                10.2f,
                FontStyle.Bold,
                NativePalette.Primary,
                new Point(14, 91),
                new Size(330, 28));
            card.Controls.Add(expiry);

            if (reset.ExpiresAtUtc.HasValue)
            {
                Label exact = MakeLabel(
                    ExpiryDetail(reset.ExpiresAtUtc.Value),
                    8.2f,
                    FontStyle.Regular,
                    NativePalette.Muted,
                    new Point(14, 119),
                    new Size(330, 24));
                card.Controls.Add(exact);
            }
            return card;
        }

        internal static string BankedRowStatusText(
            BankedReset reset,
            DateTime nowUtc)
        {
            if (reset == null)
            {
                return "UNKNOWN";
            }
            if (reset.ExpiresAtUtc.HasValue
                && reset.ExpiresAtUtc.Value <= nowUtc)
            {
                return "EXPIRED";
            }
            return string.IsNullOrEmpty(reset.Status)
                ? "AVAILABLE"
                : reset.Status.ToUpperInvariant();
        }

        private static string WrapBankDescription(string description)
        {
            const string breakBefore = " rate limit reset.";
            int index = description.IndexOf(breakBefore, StringComparison.OrdinalIgnoreCase);
            if (index > 0)
            {
                return description.Substring(0, index) + Environment.NewLine
                    + description.Substring(index + 1);
            }
            return description;
        }

        private static string ExpiryHeadline(DateTime expiresAtUtc)
        {
            DateTime local = expiresAtUtc.ToLocalTime();
            DateTime today = DateTime.Now.Date;
            bool expired = expiresAtUtc <= DateTime.UtcNow;
            if (expired)
            {
                return local.Date == today
                    ? "Expired today at "
                        + local.ToString("h:mm tt", CultureInfo.CurrentCulture)
                    : "Expired "
                        + local.ToString(
                            "ddd, MMM d, h:mm tt",
                            CultureInfo.CurrentCulture);
            }
            if (local.Date == today)
            {
                return "Expires today at " + local.ToString("h:mm tt", CultureInfo.CurrentCulture);
            }
            if (local.Date == today.AddDays(1))
            {
                return "Expires tomorrow at " + local.ToString("h:mm tt", CultureInfo.CurrentCulture);
            }
            return "Expires " + local.ToString("ddd, MMM d, h:mm tt", CultureInfo.CurrentCulture);
        }

        private static string ExpiryDetail(DateTime expiresAtUtc)
        {
            DateTime local = expiresAtUtc.ToLocalTime();
            TimeSpan remaining = expiresAtUtc - DateTime.UtcNow;
            string relative;
            if (remaining.TotalSeconds <= 0)
            {
                relative = "expired";
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
            return local.ToString(
                "ddd, MMM d, h:mm:ss tt",
                CultureInfo.CurrentCulture)
                + " · "
                + relative;
        }

        private static string ObservationText(DateTime? observedAtUtc)
        {
            return observedAtUtc.HasValue
                ? observedAtUtc.Value.ToLocalTime().ToString(
                    "ddd, MMM d, yyyy h:mm:ss tt",
                    CultureInfo.CurrentCulture)
                : "not available";
        }

        private Font CreateInterfaceFont(float size, FontStyle style)
        {
            float adjusted = size * (settings.FlyoutTextScale / 100.0f);
            return NativeDrawing.CreateSafeFont(
                settings.InterfaceFontName,
                adjusted,
                style);
        }

        private void ApplyChromeFonts()
        {
            ReplaceInterfaceFont(this, 10.5f, FontStyle.Regular);
            ReplaceInterfaceFont(productTitle, 14.0f, FontStyle.Bold);
            ReplaceInterfaceFont(productSubtitle, 10.5f, FontStyle.Regular);
            ReplaceInterfaceFont(dashboardButton, 9.5f, FontStyle.Bold);
            ReplaceInterfaceFont(codexButton, 10.0f, FontStyle.Bold);
            ReplaceInterfaceFont(claudeButton, 10.0f, FontStyle.Bold);
            ReplaceInterfaceFont(usageButton, 11.0f, FontStyle.Bold);
            ReplaceInterfaceFont(settingsButton, 11.0f, FontStyle.Bold);
            ReplaceInterfaceFont(refreshButton, 9.0f, FontStyle.Bold);
        }

        private void ReplaceInterfaceFont(
            Control control,
            float size,
            FontStyle style)
        {
            if (control == null)
            {
                return;
            }
            Font previous = control.Font;
            control.Font = CreateInterfaceFont(size, style);
            if (previous != null)
            {
                previous.Dispose();
            }
        }

        private Label MakeLabel(
            string text,
            float size,
            FontStyle style,
            Color color,
            Point location,
            Size bounds)
        {
            Label label = new Label();
            label.Text = text;
            label.Location = location;
            label.Size = bounds;
            label.Font = CreateInterfaceFont(size, style);
            label.ForeColor = color;
            label.BackColor = Color.Transparent;
            label.UseMnemonic = false;
            return label;
        }

        private static DateTime? EarliestReset(UsageSnapshot snapshot)
        {
            DateTime? earliest = null;
            foreach (UsageWindow window in snapshot.Windows)
            {
                if (!window.ResetsAtUtc.HasValue)
                {
                    continue;
                }
                if (!earliest.HasValue || window.ResetsAtUtc.Value < earliest.Value)
                {
                    earliest = window.ResetsAtUtc.Value;
                }
            }
            return earliest;
        }

        private static List<UsageWindow> DisplayWindows(UsageSnapshot snapshot)
        {
            List<UsageWindow> windows = new List<UsageWindow>(snapshot.Windows);
            windows.Sort(delegate(UsageWindow left, UsageWindow right)
            {
                bool leftCodex = string.Equals(
                    left.LimitId,
                    "codex",
                    StringComparison.OrdinalIgnoreCase);
                bool rightCodex = string.Equals(
                    right.LimitId,
                    "codex",
                    StringComparison.OrdinalIgnoreCase);
                if (leftCodex != rightCodex)
                {
                    return leftCodex ? -1 : 1;
                }
                int leftDuration = left.DurationMinutes.HasValue
                    ? left.DurationMinutes.Value
                    : int.MaxValue;
                int rightDuration = right.DurationMinutes.HasValue
                    ? right.DurationMinutes.Value
                    : int.MaxValue;
                int durationOrder = leftDuration.CompareTo(rightDuration);
                return durationOrder != 0
                    ? durationOrder
                    : string.Compare(left.Label, right.Label, StringComparison.CurrentCulture);
            });
            return windows;
        }

        private int CardWidth
        {
            get
            {
                if (contentViewport == null || contentViewport.ClientSize.Width <= 0)
                {
                    return 373;
                }
                int usablePixels = Math.Max(
                    ScalePixel(320),
                    contentViewport.ClientSize.Width
                        - (contentScroll == null ? 0 : contentScroll.Width)
                        - ScalePixel(28));
                return Math.Max(
                    320,
                    (int)Math.Floor(usablePixels / Math.Max(1.0f, dpiScale)));
            }
        }

        private int ScalePixel(int logicalPixels)
        {
            return (int)Math.Round(logicalPixels * dpiScale);
        }

        private Size ScaleSize(Size logicalSize)
        {
            return new Size(
                ScalePixel(logicalSize.Width),
                ScalePixel(logicalSize.Height));
        }

        private Point ScalePoint(Point logicalPoint)
        {
            return new Point(
                ScalePixel(logicalPoint.X),
                ScalePixel(logicalPoint.Y));
        }

        private Padding ScalePadding(Padding logicalPadding)
        {
            return new Padding(
                ScalePixel(logicalPadding.Left),
                ScalePixel(logicalPadding.Top),
                ScalePixel(logicalPadding.Right),
                ScalePixel(logicalPadding.Bottom));
        }

        private void ScaleControlTree(Control control)
        {
            if (control == null || dpiLayouts.ContainsKey(control))
            {
                return;
            }

            DpiLayoutSnapshot layout = DpiLayoutSnapshot.Capture(control);
            dpiLayouts.Add(control, layout);
            control.SuspendLayout();
            ApplyDpiLayout(control, layout);

            foreach (Control child in control.Controls)
            {
                ScaleControlTree(child);
            }
            control.ResumeLayout(false);
        }

        private void RescaleControlTree(Control control)
        {
            if (control == null)
            {
                return;
            }

            DpiLayoutSnapshot layout;
            if (!dpiLayouts.TryGetValue(control, out layout))
            {
                foreach (Control child in control.Controls)
                {
                    RescaleControlTree(child);
                }
                return;
            }
            control.SuspendLayout();
            ApplyDpiLayout(control, layout);
            foreach (Control child in control.Controls)
            {
                RescaleControlTree(child);
            }
            control.ResumeLayout(false);
        }

        private void ApplyDpiLayout(Control control, DpiLayoutSnapshot layout)
        {
            control.Bounds = new Rectangle(
                ScalePoint(layout.Bounds.Location),
                ScaleSize(layout.Bounds.Size));
            control.Margin = ScalePadding(layout.Margin);
            control.Padding = ScalePadding(layout.Padding);
            control.MinimumSize = layout.MinimumSize.IsEmpty
                ? Size.Empty
                : ScaleSize(layout.MinimumSize);
            control.MaximumSize = layout.MaximumSize.IsEmpty
                ? Size.Empty
                : ScaleSize(layout.MaximumSize);

            TableLayoutPanel table = control as TableLayoutPanel;
            if (table == null)
            {
                return;
            }
            int rowCount = Math.Min(table.RowStyles.Count, layout.RowHeights.Length);
            for (int index = 0; index < rowCount; index++)
            {
                if (table.RowStyles[index].SizeType == SizeType.Absolute)
                {
                    table.RowStyles[index].Height =
                        ScalePixel((int)Math.Round(layout.RowHeights[index]));
                }
            }
            int columnCount = Math.Min(
                table.ColumnStyles.Count,
                layout.ColumnWidths.Length);
            for (int index = 0; index < columnCount; index++)
            {
                if (table.ColumnStyles[index].SizeType == SizeType.Absolute)
                {
                    table.ColumnStyles[index].Width =
                        ScalePixel((int)Math.Round(layout.ColumnWidths[index]));
                }
            }
        }

        private void ClearContentControls()
        {
            while (content.Controls.Count > 0)
            {
                Control root = content.Controls[0];
                content.Controls.RemoveAt(0);
                if (refreshButton != null
                    && refreshButton.Parent != null
                    && root.Contains(refreshButton))
                {
                    refreshButton.Parent.Controls.Remove(refreshButton);
                }
                ForgetScaledTree(root);
                root.Dispose();
            }
        }

        private void ForgetScaledTree(Control control)
        {
            if (control == null || object.ReferenceEquals(control, refreshButton))
            {
                return;
            }
            foreach (Control child in control.Controls)
            {
                ForgetScaledTree(child);
            }
            dpiLayouts.Remove(control);
        }

        private void LayoutContentSurface()
        {
            if (content == null
                || contentScroll == null
                || contentViewport == null
                || contentViewport.ClientSize.Width <= 0
                || contentViewport.ClientSize.Height <= 0)
            {
                return;
            }
            content.Width = contentViewport.ClientSize.Width;
            content.PerformLayout();
            int height = content.Padding.Top + content.Padding.Bottom;
            foreach (Control control in content.Controls)
            {
                height = Math.Max(height, control.Bottom + control.Margin.Bottom + content.Padding.Bottom);
            }
            content.Height = Math.Max(contentViewport.ClientSize.Height, height);
            contentScroll.SetMetrics(contentViewport.ClientSize.Height, content.Height);
            content.Top = -contentScroll.Value;
        }

        private void ScrollContent(object sender, MouseEventArgs eventArgs)
        {
            int pixels = wheelScroll.Consume(
                eventArgs.Delta,
                SystemInformation.MouseWheelScrollDelta,
                ScalePixel(64));
            contentScroll.Value += pixels;
        }

        internal void ScrollToBankedForCapture()
        {
            LayoutContentSurface();
            foreach (Control control in content.Controls)
            {
                if (string.Equals(
                    control.Tag as string,
                    "banked-section",
                    StringComparison.Ordinal))
                {
                    contentScroll.Value = Math.Max(0, control.Top - ScalePixel(12));
                    break;
                }
            }
        }

        internal void ShowSettingsForCapture()
        {
            ShowSettingsView();
        }

        internal string LayoutReport()
        {
            PerformLayout();
            LayoutContentSurface();
            StringBuilder report = new StringBuilder();
            int expectedCardWidth = ScalePixel(CardWidth);
            int narrowestCard = int.MaxValue;
            int widestCard = 0;
            foreach (Control control in content.Controls)
            {
                if (control.Width > 0)
                {
                    narrowestCard = Math.Min(narrowestCard, control.Width);
                    widestCard = Math.Max(widestCard, control.Width);
                }
            }
            if (narrowestCard == int.MaxValue)
            {
                narrowestCard = 0;
            }
            bool cardsFit = narrowestCard == expectedCardWidth
                && widestCard == expectedCardWidth;
            bool footerFits = usageButton.Left >= 0
                && settingsButton.Right <= ClientSize.Width;
            bool providerFits = (!settings.ShowCodexProvider
                    || (codexButton.Left >= 0
                        && codexButton.Right <= providerShell.ClientSize.Width))
                && (!settings.ShowClaudeProvider
                    || (claudeButton.Left >= 0
                        && claudeButton.Right <= providerShell.ClientSize.Width));
            bool headerFits = productTitle.Left >= 0
                && dashboardButton.Right <= ClientSize.Width
                && pinButton.Right <= ClientSize.Width
                && productTitle.Right + ScalePixel(8) <= dashboardButton.Left
                && productTitle.Top >= 0
                && productTitle.Bottom <= productTitle.Parent.ClientSize.Height
                && productSubtitle.Top >= 0
                && productSubtitle.Bottom
                    <= productSubtitle.Parent.ClientSize.Height
                && dashboardButton.Top >= 0
                && dashboardButton.Bottom
                    <= dashboardButton.Parent.ClientSize.Height
                && pinButton.Top >= 0
                && pinButton.Bottom <= pinButton.Parent.ClientSize.Height;
            report.AppendLine(
                cardsFit && footerFits && providerFits && headerFits
                    ? "status=passed"
                    : "status=failed");
            report.AppendLine("systemDpi=" + NativeDrawing.SystemDpi);
            report.AppendLine(
                "client="
                    + ClientSize.Width
                    + "x"
                    + ClientSize.Height);
            report.AppendLine(
                "viewport="
                    + contentViewport.ClientSize.Width
                    + "x"
                    + contentViewport.ClientSize.Height);
            report.AppendLine("expectedCardWidth=" + expectedCardWidth);
            report.AppendLine("narrowestContentWidth=" + narrowestCard);
            report.AppendLine("widestContentWidth=" + widestCard);
            report.AppendLine("providerRight=" + claudeButton.Right);
            report.AppendLine("footerRight=" + settingsButton.Right);
            report.AppendLine("titleRight=" + productTitle.Right);
            report.AppendLine(
                "titleBounds=" + productTitle.Bounds.ToString());
            report.AppendLine(
                "subtitleBounds=" + productSubtitle.Bounds.ToString());
            report.AppendLine(
                "headerHeight=" + productTitle.Parent.ClientSize.Height);
            report.AppendLine("dashboardLeft=" + dashboardButton.Left);
            report.AppendLine("dashboardRight=" + dashboardButton.Right);
            report.AppendLine("pinLeft=" + pinButton.Left);
            report.AppendLine("pinRight=" + pinButton.Right);
            report.AppendLine("contentHeight=" + content.Height);
            report.AppendLine("scrollMaximum=" + contentScroll.Maximum);
            return report.ToString();
        }

        protected override void OnMouseWheel(MouseEventArgs eventArgs)
        {
            Point screenPoint = PointToScreen(eventArgs.Location);
            if (contentViewport.RectangleToScreen(contentViewport.ClientRectangle).Contains(screenPoint))
            {
                ScrollContent(this, eventArgs);
                return;
            }
            base.OnMouseWheel(eventArgs);
        }

        private void SelectProvider(bool codex)
        {
            if (codex && !settings.ShowCodexProvider)
            {
                codex = false;
            }
            else if (!codex && !settings.ShowClaudeProvider)
            {
                codex = true;
            }
            showingClaudeInfo = !codex;
            codexButton.Selected = codex;
            codexButton.ForeColor = codex ? NativePalette.Primary : NativePalette.Secondary;
            codexButton.BorderColor = codex
                ? NativePalette.BorderStrong
                : Color.Transparent;
            claudeButton.Selected = !codex;
            claudeButton.ForeColor = codex ? NativePalette.Secondary : NativePalette.Primary;
            claudeButton.SelectedColor = Color.FromArgb(57, 39, 32);
            claudeButton.BorderColor = codex
                ? Color.Transparent
                : NativePalette.Claude;
            codexButton.Invalidate();
            claudeButton.Invalidate();
        }

        private void ApplyProviderVisibility()
        {
            settings.Normalize();
            bool showSwitcher = settings.ShowCodexProvider
                && settings.ShowClaudeProvider;
            providerBand.Visible = showSwitcher;
            root.RowStyles[1].Height = showSwitcher ? ScalePixel(48) : 0;
            codexButton.Visible = settings.ShowCodexProvider;
            claudeButton.Visible = settings.ShowClaudeProvider;
            if ((showingClaudeInfo && !settings.ShowClaudeProvider)
                || (!showingClaudeInfo && !settings.ShowCodexProvider))
            {
                showingClaudeInfo = settings.ShowClaudeProvider;
            }
            SelectProvider(!showingClaudeInfo);
            productSubtitle.Text = showingClaudeInfo
                ? "Claude info · native beta"
                : "Codex monitor";
            LayoutProviderButtons();
        }

        private void LayoutProviderButtons()
        {
            if (providerShell == null
                || codexButton == null
                || claudeButton == null)
            {
                return;
            }
            int inset = ScalePixel(3);
            int gap = ScalePixel(4);
            int height = Math.Max(1, providerShell.ClientSize.Height - inset * 2);
            if (settings.ShowCodexProvider && settings.ShowClaudeProvider)
            {
                int available = Math.Max(2, providerShell.ClientSize.Width - inset * 2 - gap);
                codexButton.Bounds = new Rectangle(inset, inset, available / 2, height);
                claudeButton.Bounds = new Rectangle(
                    codexButton.Right + gap,
                    inset,
                    Math.Max(1, providerShell.ClientSize.Width - inset - codexButton.Right - gap),
                    height);
            }
            else
            {
                RoundedButton visible = settings.ShowCodexProvider
                    ? codexButton
                    : claudeButton;
                visible.Bounds = new Rectangle(
                    inset,
                    inset,
                    Math.Max(1, providerShell.ClientSize.Width - inset * 2),
                    height);
            }
        }

        private void SetTransientMessage(string message, Color color)
        {
            currentMessage = message;
            currentMessageColor = color;
            if (lastSnapshot != null)
            {
                BuildContent(lastSnapshot);
            }
            else
            {
                BuildEmptyState(message);
            }
        }

        private void DragHeader(object sender, MouseEventArgs eventArgs)
        {
            if (eventArgs.Button != MouseButtons.Left)
            {
                return;
            }
            ReleaseCapture();
            SendMessage(Handle, WmNcLButtonDown, new IntPtr(HtCaption), IntPtr.Zero);
        }

        private void PaintShellBorder(object sender, PaintEventArgs eventArgs)
        {
            eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle borderBounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = NativeDrawing.RoundedRectangle(
                borderBounds,
                ScalePixel(CornerRadius)))
            using (Pen border = new Pen(NativePalette.BorderStrong, 1.0f))
            {
                eventArgs.Graphics.DrawPath(border, path);
            }
        }

        private void ApplyRoundedRegion()
        {
            IntPtr regionHandle = CreateRoundRectRgn(
                0,
                0,
                Width + 1,
                Height + 1,
                ScalePixel(CornerRadius) * 2,
                ScalePixel(CornerRadius) * 2);
            try
            {
                Region next = Region.FromHrgn(regionHandle);
                Region previous = Region;
                Region = next;
                if (previous != null)
                {
                    previous.Dispose();
                }
            }
            finally
            {
                DeleteObject(regionHandle);
            }
        }

        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                Hide();
                return true;
            }
            if (keyData == (Keys.Control | Keys.R))
            {
                refreshButton.PerformClick();
                return true;
            }
            if (keyData == Keys.PageDown)
            {
                contentScroll.Value += contentViewport.ClientSize.Height;
                return true;
            }
            if (keyData == Keys.PageUp)
            {
                contentScroll.Value -= contentViewport.ClientSize.Height;
                return true;
            }
            return base.ProcessCmdKey(ref message, keyData);
        }

        public void ShowNearTaskbar()
        {
            ShowProviderNearTaskbar(!showingClaudeInfo);
        }

        internal DialogResult ShowConfirmation(
            string message,
            string caption,
            MessageBoxIcon icon)
        {
            autoHideTimer.Stop();
            modalDialogDepth++;
            try
            {
                return MessageBox.Show(
                    this,
                    message,
                    caption,
                    MessageBoxButtons.YesNo,
                    icon,
                    MessageBoxDefaultButton.Button2);
            }
            finally
            {
                modalDialogDepth = Math.Max(0, modalDialogDepth - 1);
                if (Visible)
                {
                    Activate();
                    BringToFront();
                }
            }
        }

        public void ShowProviderNearTaskbar(bool codex)
        {
            SelectProvider(codex);
            productSubtitle.Text = showingClaudeInfo
                ? "Claude info · native beta"
                : "Codex monitor";
            ShowUsageView();
            PositionNearTaskbar();
            if (!Visible)
            {
                Show();
            }
            WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
        }

        internal bool ShowingClaudeProvider
        {
            get { return showingClaudeInfo; }
        }

        internal bool ProviderButtonsFillAvailableSpaceForTest()
        {
            int inset = ScalePixel(3);
            if (!settings.ShowCodexProvider || !settings.ShowClaudeProvider)
            {
                return !providerBand.Visible
                    && Math.Abs(root.RowStyles[1].Height) < 0.1f;
            }
            return providerBand.Visible
                && codexButton.Left == inset
                && claudeButton.Right == providerShell.ClientSize.Width - inset;
        }

        internal void PrepareNearTaskbarForLayoutProbe()
        {
            PositionNearTaskbar();
            PerformLayout();
            LayoutContentSurface();
        }

        private void PositionNearTaskbar()
        {
            Screen screen = Screen.FromPoint(Cursor.Position);
            int screenDpi = GetScreenDpi(screen);
            if (screenDpi != currentDpi)
            {
                ApplyDpiChange(screenDpi, Rectangle.Empty, false);
            }
            Rectangle working = screen.WorkingArea;
            int margin = ScalePixel(10);
            Size desiredSize = new Size(
                MaximumSize.Width,
                Math.Min(
                    MaximumSize.Height,
                    Math.Max(MinimumSize.Height, working.Height - margin * 2)));
            Bounds = CalculateFlyoutBounds(
                screen.Bounds,
                working,
                desiredSize,
                margin);
        }

        internal static Rectangle CalculateFlyoutBounds(
            Rectangle screenBounds,
            Rectangle workingArea,
            Size desiredSize,
            int margin)
        {
            Rectangle usable = workingArea.Width > 0 && workingArea.Height > 0
                ? workingArea
                : screenBounds;
            int safeMargin = Math.Max(0, margin);
            int width = Math.Max(
                1,
                Math.Min(
                    Math.Max(1, desiredSize.Width),
                    Math.Max(1, usable.Width - safeMargin * 2)));
            int height = Math.Max(
                1,
                Math.Min(
                    Math.Max(1, desiredSize.Height),
                    Math.Max(1, usable.Height - safeMargin * 2)));

            TaskbarEdge edge = DetectTaskbarEdge(screenBounds, usable);
            int x = usable.Right - width - safeMargin;
            int y = usable.Bottom - height - safeMargin;
            if (edge == TaskbarEdge.Top)
            {
                y = usable.Top + safeMargin;
            }
            else if (edge == TaskbarEdge.Left)
            {
                x = usable.Left + safeMargin;
            }

            x = Math.Max(
                usable.Left,
                Math.Min(x, usable.Right - width));
            y = Math.Max(
                usable.Top,
                Math.Min(y, usable.Bottom - height));
            return new Rectangle(x, y, width, height);
        }

        private static TaskbarEdge DetectTaskbarEdge(
            Rectangle screenBounds,
            Rectangle workingArea)
        {
            int leftInset = Math.Max(0, workingArea.Left - screenBounds.Left);
            int topInset = Math.Max(0, workingArea.Top - screenBounds.Top);
            int rightInset = Math.Max(0, screenBounds.Right - workingArea.Right);
            int bottomInset = Math.Max(0, screenBounds.Bottom - workingArea.Bottom);

            TaskbarEdge edge = TaskbarEdge.Bottom;
            int largestInset = bottomInset;
            if (topInset > largestInset)
            {
                edge = TaskbarEdge.Top;
                largestInset = topInset;
            }
            if (leftInset > largestInset)
            {
                edge = TaskbarEdge.Left;
                largestInset = leftInset;
            }
            if (rightInset > largestInset)
            {
                edge = TaskbarEdge.Right;
            }
            return edge;
        }

        private static int GetScreenDpi(Screen screen)
        {
            if (screen == null)
            {
                return NativeDrawing.SystemDpi;
            }
            Rectangle bounds = screen.Bounds;
            NativePoint center = new NativePoint(
                bounds.Left + bounds.Width / 2,
                bounds.Top + bounds.Height / 2);
            try
            {
                IntPtr monitor = MonitorFromPoint(center, MonitorDefaultToNearest);
                uint dpiX;
                uint dpiY;
                if (monitor != IntPtr.Zero
                    && GetDpiForMonitor(
                        monitor,
                        MonitorDpiType.Effective,
                        out dpiX,
                        out dpiY) == 0
                    && dpiX > 0)
                {
                    return unchecked((int)dpiX);
                }
            }
            catch (DllNotFoundException)
            {
            }
            catch (EntryPointNotFoundException)
            {
            }
            return NativeDrawing.SystemDpi;
        }

        private enum TaskbarEdge
        {
            Bottom,
            Top,
            Left,
            Right
        }

        private enum MonitorDpiType
        {
            Effective = 0
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public NativePoint(int x, int y)
            {
                X = x;
                Y = y;
            }

            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private sealed class DpiLayoutSnapshot
        {
            private DpiLayoutSnapshot(Control control)
            {
                Bounds = control.Bounds;
                Margin = control.Margin;
                Padding = control.Padding;
                MinimumSize = control.MinimumSize;
                MaximumSize = control.MaximumSize;

                TableLayoutPanel table = control as TableLayoutPanel;
                int rowCount = table == null ? 0 : table.RowStyles.Count;
                RowHeights = new float[rowCount];
                for (int index = 0; index < rowCount; index++)
                {
                    RowHeights[index] = table.RowStyles[index].Height;
                }
                int columnCount = table == null ? 0 : table.ColumnStyles.Count;
                ColumnWidths = new float[columnCount];
                for (int index = 0; index < columnCount; index++)
                {
                    ColumnWidths[index] = table.ColumnStyles[index].Width;
                }
            }

            public Rectangle Bounds { get; private set; }
            public Padding Margin { get; private set; }
            public Padding Padding { get; private set; }
            public Size MinimumSize { get; private set; }
            public Size MaximumSize { get; private set; }
            public float[] RowHeights { get; private set; }
            public float[] ColumnWidths { get; private set; }

            public static DpiLayoutSnapshot Capture(Control control)
            {
                return new DpiLayoutSnapshot(control);
            }
        }
    }
}
