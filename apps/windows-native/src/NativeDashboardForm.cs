using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace UsageApp.Native
{
    internal sealed class NativeDashboardForm : Form
    {
        private const int WmDpiChanged = 0x02E0;

        private readonly NativeSettings settings;
        private readonly Panel header;
        private readonly BrandLogo logo;
        private readonly RoundedPanel provider;
        private readonly Panel contentHost;
        private readonly BufferedFlowLayoutPanel content;
        private readonly DarkScrollBar contentScroll;
        private readonly RoundedButton refreshButton;
        private readonly PrecisionWheelAccumulator wheelScroll =
            new PrecisionWheelAccumulator();
        private readonly Dictionary<Panel, GridLayoutSpec> grids =
            new Dictionary<Panel, GridLayoutSpec>();
        private Label productTitle;
        private Label productSubtitle;
        private RoundedButton codexButton;
        private RoundedButton claudeButton;
        private Icon ownedIcon;
        private UsageSnapshot snapshot;
        private string stateText = "Connecting";
        private Color stateColor = NativePalette.Accent;
        private bool showingClaudeInfo;
        private bool providerSwitcherEnabled;
        private int selectedActivityDays = 30;
        private DateTime? customActivityFromDate;
        private DateTime? customActivityToDate;
        private float dpiScale;
        private int currentDpi;
        private bool handlingDpiChange;

        public event EventHandler RefreshRequested;

        public NativeDashboardForm(NativeSettings nativeSettings)
        {
            settings = nativeSettings ?? new NativeSettings();
            settings.Normalize();
            currentDpi = NativeDrawing.SystemDpi;
            dpiScale = currentDpi / 96.0f;

            Text = "UsageApp - Codex dashboard";
            SetOwnedIcon(null);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.FromArgb(7, 13, 21);
            ForeColor = NativePalette.Primary;
            ClientSize = SizePx(1080, 720);
            MinimumSize = SizePx(780, 560);
            KeyPreview = true;

            header = new Panel();
            header.Location = Point.Empty;
            header.Size = new Size(ClientSize.Width, Px(74));
            header.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            header.BackColor = Color.FromArgb(9, 16, 25);
            header.Padding = Pad(24, 13, 24, 12);
            Controls.Add(header);

            logo = new BrandLogo();
            logo.Location = PointPx(48, 4);
            logo.Size = SizePx(38, 38);
            header.Controls.Add(logo);

            productTitle = Label(
                "UsageApp",
                14.0f,
                FontStyle.Bold,
                NativePalette.Primary,
                PointPx(100, 3),
                SizePx(210, 38));
            header.Controls.Add(productTitle);
            productSubtitle = Label(
                "Codex usage dashboard",
                9.0f,
                FontStyle.Regular,
                NativePalette.Secondary,
                PointPx(100, 40),
                SizePx(240, 28));
            header.Controls.Add(productSubtitle);

            provider = new RoundedPanel();
            provider.Anchor = AnchorStyles.Top;
            provider.Location = PointPx(340, 9);
            provider.Size = SizePx(400, 42);
            provider.FillColor = NativePalette.ShellRaised;
            provider.BorderColor = NativePalette.Border;
            header.Controls.Add(provider);

            codexButton = new RoundedButton();
            codexButton.Text = "Codex";
            codexButton.Location = PointPx(3, 3);
            codexButton.Size = SizePx(194, 36);
            codexButton.Font = UiFont(9.0f, FontStyle.Bold);
            codexButton.Selected = true;
            codexButton.ForeColor = NativePalette.Primary;
            codexButton.FillColor = NativePalette.ShellRaised;
            codexButton.SelectedColor = NativePalette.CardSelected;
            codexButton.Click += delegate
            {
                SelectProvider(true);
            };
            provider.Controls.Add(codexButton);

            claudeButton = new RoundedButton();
            claudeButton.Text = "Claude beta info";
            claudeButton.Location = PointPx(200, 3);
            claudeButton.Size = SizePx(197, 36);
            claudeButton.Font = UiFont(8.2f, FontStyle.Bold);
            claudeButton.ForeColor = NativePalette.Muted;
            claudeButton.AccessibleName = "Claude native beta information";
            claudeButton.AccessibleDescription =
                "Shows why Claude usage is not yet available in the native beta.";
            claudeButton.Click += delegate
            {
                SelectProvider(false);
            };
            provider.Controls.Add(claudeButton);
            ApplyProviderVisibility();

            refreshButton = new RoundedButton();
            refreshButton.Text = "Refresh";
            refreshButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            refreshButton.Location = new Point(
                header.ClientSize.Width - Px(120),
                Px(9));
            refreshButton.Size = SizePx(96, 40);
            refreshButton.Font = UiFont(9.0f, FontStyle.Bold);
            refreshButton.ForeColor = NativePalette.Primary;
            refreshButton.FillColor = NativePalette.ShellRaised;
            refreshButton.BorderColor = NativePalette.Border;
            refreshButton.Click += delegate
            {
                EventHandler handler = RefreshRequested;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            };
            header.Controls.Add(refreshButton);
            header.Resize += delegate
            {
                LayoutHeader();
            };

            contentHost = new Panel();
            contentHost.Location = new Point(0, header.Bottom);
            contentHost.Size = new Size(
                ClientSize.Width,
                Math.Max(1, ClientSize.Height - header.Height));
            contentHost.BackColor = BackColor;
            Controls.Add(contentHost);

            content = new BufferedFlowLayoutPanel();
            content.Location = Point.Empty;
            content.Size = new Size(
                Math.Max(1, contentHost.ClientSize.Width - Px(9)),
                contentHost.ClientSize.Height);
            content.AutoScroll = false;
            content.WrapContents = false;
            content.FlowDirection = FlowDirection.TopDown;
            content.Padding = Pad(24, 28, 24, 36);
            content.BackColor = BackColor;
            contentHost.Controls.Add(content);

            contentScroll = new DarkScrollBar();
            contentScroll.Dock = DockStyle.Right;
            contentScroll.Width = Px(9);
            contentScroll.ValueChanged += delegate
            {
                content.Top = -contentScroll.Value;
            };
            contentHost.Controls.Add(contentScroll);
            contentScroll.BringToFront();

            Resize += delegate
            {
                if (handlingDpiChange)
                {
                    return;
                }
                LayoutSurface();
                ResizeCards();
                UpdateContentExtent();
            };
            FormClosing += delegate(object sender, FormClosingEventArgs eventArgs)
            {
                if (eventArgs.CloseReason == CloseReason.UserClosing)
                {
                    eventArgs.Cancel = true;
                    Hide();
                }
            };
            Build();
            LayoutSurface();
            ResizeCards();
            UpdateContentExtent();
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
                // WinForms must see the message so DeviceDpi propagates to custom
                // controls. Layout is then rebuilt from logical measurements once.
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
                if (hasSuggestedBounds && WindowState == FormWindowState.Normal)
                {
                    Bounds = suggestedBounds;
                }
                return;
            }

            int previousDpi = Math.Max(1, currentDpi);
            int previousScroll = contentScroll == null ? 0 : contentScroll.Value;
            double logicalScroll = previousScroll * 96.0 / previousDpi;
            Rectangle previousBounds = Bounds;
            currentDpi = nextDpi;
            dpiScale = currentDpi / 96.0f;

            SuspendLayout();
            try
            {
                MinimumSize = SizePx(780, 560);
                if (hasSuggestedBounds && WindowState == FormWindowState.Normal)
                {
                    Bounds = suggestedBounds;
                }
                else if (WindowState == FormWindowState.Normal)
                {
                    int width = Math.Max(
                        MinimumSize.Width,
                        (int)Math.Round(
                            previousBounds.Width * (double)currentDpi / previousDpi));
                    int height = Math.Max(
                        MinimumSize.Height,
                        (int)Math.Round(
                            previousBounds.Height * (double)currentDpi / previousDpi));
                    SetBounds(
                        previousBounds.Left + (previousBounds.Width - width) / 2,
                        previousBounds.Top + (previousBounds.Height - height) / 2,
                        width,
                        height,
                        BoundsSpecified.All);
                }
                ApplyDpiChromeLayout();
                ApplyChromeFonts();
                Build();
                LayoutSurface();
                ResizeCards();
                UpdateContentExtent();
                contentScroll.Value = (int)Math.Round(logicalScroll * dpiScale);
                Invalidate(true);
            }
            finally
            {
                ResumeLayout(true);
            }
        }

        private void ApplyDpiChromeLayout()
        {
            header.Location = Point.Empty;
            header.Size = new Size(ClientSize.Width, Px(74));
            header.Padding = Pad(24, 13, 24, 12);
            logo.Location = PointPx(48, 4);
            logo.Size = SizePx(38, 38);
            productTitle.Location = PointPx(100, 3);
            productTitle.Size = SizePx(210, 38);
            productSubtitle.Location = PointPx(100, 40);
            productSubtitle.Size = SizePx(240, 28);
            provider.Location = PointPx(340, 9);
            provider.Size = SizePx(400, 42);
            codexButton.Location = PointPx(3, 3);
            codexButton.Size = SizePx(194, 36);
            claudeButton.Location = PointPx(200, 3);
            claudeButton.Size = SizePx(197, 36);
            refreshButton.Size = SizePx(96, 40);
            contentScroll.Width = Px(9);
            LayoutHeader();
        }

        private void LayoutHeader()
        {
            if (header == null
                || provider == null
                || refreshButton == null
                || codexButton == null
                || claudeButton == null)
            {
                return;
            }
            refreshButton.Location = new Point(
                header.ClientSize.Width - refreshButton.Width - Px(24),
                Px(9));
            int providerLeftLimit = CalculateProviderLeftLimit(
                productTitle.Right,
                productSubtitle.Right,
                Px(270),
                Px(16));
            int providerRightLimit = refreshButton.Left - Px(24);
            int availableProviderWidth = Math.Max(
                Px(260),
                providerRightLimit - providerLeftLimit);
            provider.Width = Math.Min(Px(400), availableProviderWidth);
            provider.Left = Math.Max(
                providerLeftLimit,
                Math.Min(
                    (header.ClientSize.Width - provider.Width) / 2,
                    providerRightLimit - provider.Width));
            LayoutProviderButtons();
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

        public void ShowLoading()
        {
            refreshButton.Enabled = false;
            stateText = "Refreshing";
            stateColor = NativePalette.Accent;
            // Keep the currently rendered dashboard in place while the
            // background provider request runs. Rebuilding an unchanged tree
            // here is what made a full-screen dashboard visibly blink.
            if (snapshot == null) Build();
        }

        public void ShowSnapshot(UsageSnapshot value, bool cached)
        {
            snapshot = value;
            refreshButton.Enabled = !showingClaudeInfo;
            stateText = cached ? "Last known" : "Live";
            stateColor = cached ? NativePalette.Warning : NativePalette.Success;
            SetOwnedIcon(
                snapshot == null || snapshot.PreferredWindow == null
                    ? (int?)null
                    : snapshot.PreferredWindow.RemainingPercent);
            Build();
        }

        public void ShowError(string message)
        {
            refreshButton.Enabled = !showingClaudeInfo;
            stateText = snapshot == null
                ? Safe(message)
                : "Stale · " + Safe(message);
            stateColor = NativePalette.Error;
            Build();
        }

        public void ShowDashboard()
        {
            if (!Visible)
            {
                Show();
            }
            int actualDpi = IsHandleCreated ? DeviceDpi : currentDpi;
            if (actualDpi > 0 && actualDpi != currentDpi)
            {
                ApplyDpiChange(actualDpi, Rectangle.Empty, false);
            }
            WindowState = FormWindowState.Maximized;
            Activate();
            BringToFront();
        }

        public void ShowProviderDashboard(bool codex)
        {
            SelectProvider(codex);
            ShowDashboard();
        }

        public void ApplySettings()
        {
            settings.Normalize();
            ApplyProviderVisibility();
            ApplyChromeFonts();
            SetOwnedIcon(
                snapshot == null || snapshot.PreferredWindow == null
                    ? (int?)null
                    : snapshot.PreferredWindow.RemainingPercent);
            Build();
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
            UpdateProviderSelectionChrome(codex);
            contentScroll.Value = 0;
            Build();
        }

        private void UpdateProviderSelectionChrome(bool codex)
        {
            codexButton.Selected = codex;
            codexButton.ForeColor = codex
                ? NativePalette.Primary
                : NativePalette.Secondary;
            codexButton.BorderColor = codex
                ? NativePalette.BorderStrong
                : Color.Transparent;
            claudeButton.Selected = !codex;
            claudeButton.ForeColor = codex
                ? NativePalette.Secondary
                : NativePalette.Primary;
            claudeButton.SelectedColor = Color.FromArgb(57, 39, 32);
            claudeButton.BorderColor = codex
                ? Color.Transparent
                : NativePalette.Claude;
            productSubtitle.Text = codex
                ? "Codex usage dashboard"
                : "Claude info · native beta";
            Text = codex
                ? "UsageApp - Codex dashboard"
                : "UsageApp - Claude native beta info";
            if (refreshButton != null)
            {
                refreshButton.Enabled = codex
                    && !string.Equals(
                        stateText,
                        "Refreshing",
                        StringComparison.Ordinal);
            }
            codexButton.Invalidate();
            claudeButton.Invalidate();
        }

        private void ApplyProviderVisibility()
        {
            settings.Normalize();
            providerSwitcherEnabled = settings.ShowCodexProvider
                && settings.ShowClaudeProvider;
            provider.Visible = providerSwitcherEnabled;
            codexButton.Visible = settings.ShowCodexProvider;
            claudeButton.Visible = settings.ShowClaudeProvider;
            if ((showingClaudeInfo && !settings.ShowClaudeProvider)
                || (!showingClaudeInfo && !settings.ShowCodexProvider))
            {
                showingClaudeInfo = settings.ShowClaudeProvider;
            }
            UpdateProviderSelectionChrome(!showingClaudeInfo);
            LayoutProviderButtons();
        }

        private void LayoutProviderButtons()
        {
            int inset = Px(3);
            int gap = Px(3);
            int height = Math.Max(1, provider.ClientSize.Height - inset * 2);
            if (settings.ShowCodexProvider && settings.ShowClaudeProvider)
            {
                int available = Math.Max(2, provider.ClientSize.Width - inset * 2 - gap);
                codexButton.Bounds = new Rectangle(inset, inset, available / 2, height);
                claudeButton.Bounds = new Rectangle(
                    codexButton.Right + gap,
                    inset,
                    Math.Max(1, provider.ClientSize.Width - inset - codexButton.Right - gap),
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
                    Math.Max(1, provider.ClientSize.Width - inset * 2),
                    height);
            }
        }

        internal bool ProviderSwitcherMatchesVisibilityForTest()
        {
            bool shouldShow = settings.ShowCodexProvider
                && settings.ShowClaudeProvider;
            return providerSwitcherEnabled == shouldShow;
        }

        private void ApplyChromeFonts()
        {
            ReplaceUiFont(productTitle, 14.0f, FontStyle.Bold);
            ReplaceUiFont(productSubtitle, 9.0f, FontStyle.Regular);
            ReplaceUiFont(codexButton, 9.0f, FontStyle.Bold);
            ReplaceUiFont(claudeButton, 8.2f, FontStyle.Bold);
            ReplaceUiFont(refreshButton, 9.0f, FontStyle.Bold);
        }

        private void ReplaceUiFont(
            Control control,
            float size,
            FontStyle style)
        {
            if (control == null)
            {
                return;
            }
            Font previous = control.Font;
            control.Font = UiFont(size, style);
            if (previous != null)
            {
                previous.Dispose();
            }
        }

        private void SetOwnedIcon(int? percentage)
        {
            Icon next = NativeBrandIconRenderer.Create(48);
            Icon previous = ownedIcon;
            ownedIcon = next;
            Icon = next;
            if (previous != null)
            {
                previous.Dispose();
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && ownedIcon != null)
            {
                ownedIcon.Dispose();
                ownedIcon = null;
            }
        }

        internal string LayoutReport()
        {
            PerformLayout();
            ResizeCards();
            int expected = CardWidth();
            bool cardsFit = content.Controls.Count > 0;
            foreach (Control control in content.Controls)
            {
                if (string.Equals(control.Tag as string, "full", StringComparison.Ordinal)
                    && control.Width != expected)
                {
                    cardsFit = false;
                }
            }
            string clippedLabel;
            bool labelsFit = LabelsFitVertically(content, out clippedLabel);
            bool headerFits = DashboardHeaderFits();
            StringBuilder report = new StringBuilder();
            report.AppendLine(
                cardsFit && labelsFit && headerFits
                    ? "status=passed"
                    : "status=failed");
            report.AppendLine(
                "client=" + ClientSize.Width + "x" + ClientSize.Height);
            report.AppendLine(
                "content=" + content.ClientSize.Width + "x" + content.ClientSize.Height);
            report.AppendLine("cardWidth=" + expected);
            report.AppendLine("controlCount=" + content.Controls.Count);
            report.AppendLine("labelsFit=" + labelsFit);
            report.AppendLine("headerFits=" + headerFits);
            report.AppendLine("headerBounds=" + header.Bounds);
            report.AppendLine("titleBounds=" + productTitle.Bounds
                + ";textFits=" + TextFits(productTitle));
            report.AppendLine("subtitleBounds=" + productSubtitle.Bounds
                + ";textFits=" + TextFits(productSubtitle));
            report.AppendLine("providerBounds=" + provider.Bounds
                + ";visible=" + provider.Visible);
            report.AppendLine("refreshBounds=" + refreshButton.Bounds
                + ";textFits=" + TextFits(refreshButton));
            if (!labelsFit)
            {
                report.AppendLine("clippedLabel=" + clippedLabel);
            }
            return report.ToString();
        }

        private bool DashboardHeaderFits()
        {
            if (header == null
                || productTitle == null
                || productSubtitle == null
                || refreshButton == null
                || provider == null)
            {
                return false;
            }
            bool controlsFit = productTitle.Left >= 0
                && productTitle.Top >= Px(3)
                && productTitle.Bottom <= header.ClientSize.Height
                && productSubtitle.Left >= 0
                && productSubtitle.Top >= 0
                && productSubtitle.Bottom <= header.ClientSize.Height
                && refreshButton.Top >= 0
                && refreshButton.Bottom <= header.ClientSize.Height
                && (!providerSwitcherEnabled
                    || (provider.Top >= 0
                        && provider.Bottom <= header.ClientSize.Height));
            return controlsFit
                && TextFits(productTitle)
                && TextFits(productSubtitle)
                && TextFits(refreshButton)
                && (!providerSwitcherEnabled
                    || (TextFits(codexButton) && TextFits(claudeButton)));
        }

        private static bool TextFits(Control control)
        {
            if (control == null || string.IsNullOrEmpty(control.Text))
            {
                return true;
            }
            Size measured = TextRenderer.MeasureText(
                control.Text,
                control.Font,
                new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            return measured.Width <= Math.Max(1, control.ClientSize.Width - 4)
                && measured.Height <= Math.Max(1, control.ClientSize.Height - 4);
        }

        private static bool LabelsFitVertically(
            Control root,
            out string clippedLabel)
        {
            clippedLabel = null;
            foreach (Control child in root.Controls)
            {
                Label label = child as Label;
                bool scrollingParent = root is FlowLayoutPanel;
                if (label != null
                    && !scrollingParent
                    && (label.Top < 0 || label.Bottom > root.ClientSize.Height))
                {
                    clippedLabel = label.Text + " bounds=" + label.Bounds
                        + " parentHeight=" + root.ClientSize.Height;
                    return false;
                }
                if (label != null
                    && label.ClientSize.Width > 0
                    && label.ClientSize.Height > 0
                    && !string.IsNullOrEmpty(label.Text))
                {
                    Size measured = TextRenderer.MeasureText(
                        label.Text,
                        label.Font,
                        new Size(label.ClientSize.Width, int.MaxValue),
                        TextFormatFlags.WordBreak
                            | TextFormatFlags.NoPadding
                            | TextFormatFlags.NoPrefix);
                    int tolerance = Math.Max(2, label.DeviceDpi / 48);
                    if (measured.Height > label.ClientSize.Height + tolerance)
                    {
                        clippedLabel = label.Text + " measuredHeight="
                            + measured.Height + " bounds=" + label.Bounds;
                        return false;
                    }
                }
                string descendant;
                if (!(child is Label)
                    && !LabelsFitVertically(child, out descendant))
                {
                    clippedLabel = descendant;
                    return false;
                }
            }
            return true;
        }

        internal void ScrollToActivityForCapture()
        {
            if (content.Controls.Count <= 3)
            {
                return;
            }
            contentScroll.Value = Math.Max(0, content.Controls[3].Top);
            UpdateContentExtent();
        }

        private void Build()
        {
            if (content == null)
            {
                return;
            }
            int previousScroll = contentScroll.Value;
            content.SuspendLayout();
            NativeDrawing.SetRedraw(content, false);
            grids.Clear();
            while (content.Controls.Count > 0)
            {
                Control control = content.Controls[0];
                content.Controls.RemoveAt(0);
                control.Dispose();
            }

            CenterContent();
            int width = CardWidth();
            if (showingClaudeInfo)
            {
                content.Controls.Add(CreateClaudeBetaInfo(width));
                content.ResumeLayout();
                ResizeCards();
                UpdateContentExtent();
                contentScroll.Value = previousScroll;
                NativeDrawing.SetRedraw(content, true);
                return;
            }
            content.Controls.Add(CreateIntro(width));

            if (snapshot == null)
            {
                content.Controls.Add(CreateEmpty(width));
            }
            else
            {
                content.Controls.Add(CreateSectionHeading(
                    width,
                    "LIVE ALLOWANCE",
                    "Quota and resets"));
                List<UsageWindow> windows = new List<UsageWindow>(snapshot.Windows);
                windows.Sort(delegate(UsageWindow left, UsageWindow right)
                {
                    int leftDuration = left.DurationMinutes ?? int.MaxValue;
                    int rightDuration = right.DurationMinutes ?? int.MaxValue;
                    int durationOrder = leftDuration.CompareTo(rightDuration);
                    if (durationOrder != 0)
                    {
                        return durationOrder;
                    }
                    bool leftIsCodex = string.Equals(
                        left.LimitId,
                        "codex",
                        StringComparison.OrdinalIgnoreCase);
                    bool rightIsCodex = string.Equals(
                        right.LimitId,
                        "codex",
                        StringComparison.OrdinalIgnoreCase);
                    if (leftIsCodex != rightIsCodex)
                    {
                        return leftIsCodex ? -1 : 1;
                    }
                    return string.Compare(
                        left.Label,
                        right.Label,
                        StringComparison.CurrentCultureIgnoreCase);
                });
                List<Control> quotaCards = new List<Control>();
                foreach (UsageWindow window in windows)
                {
                    quotaCards.Add(CreateQuotaCard(Px(320), window));
                }
                quotaCards.Add(CreateBankedCard(Px(320), snapshot.BankedResets));
                content.Controls.Add(CreateCardGrid(
                    width,
                    quotaCards,
                    3,
                    350,
                    24));
                content.Controls.Add(CreateSectionHeading(
                    width,
                    "ACTIVITY HISTORY",
                    "Explore usage"));
                content.Controls.Add(CreateActivityFilter(width));
                List<TokenUsageDailyBucket> selectedBuckets =
                    SelectedActivityBuckets();
                TokenUsageSummary tokenUsage = snapshot.TokenUsage;
                bool dailyHistoryAvailable = tokenUsage != null
                    && tokenUsage.DailyBuckets.Count > 0;
                long selectedTokens = SumTokens(selectedBuckets);
                int activeDays = CountActiveDays(selectedBuckets);
                List<Control> metrics = new List<Control>();
                metrics.Add(CreateMetricCard(
                    "Selected tokens",
                    dailyHistoryAvailable
                        ? FormatTokenCount(selectedTokens)
                        : "\u2014",
                    dailyHistoryAvailable
                        ? selectedBuckets.Count.ToString(
                            CultureInfo.CurrentCulture)
                            + " recorded day"
                            + (selectedBuckets.Count == 1 ? string.Empty : "s")
                        : "Daily totals not supplied"));
                metrics.Add(CreateMetricCard(
                    "Average per active day",
                    dailyHistoryAvailable && activeDays > 0
                        ? FormatTokenCount(selectedTokens / activeDays)
                        : "\u2014",
                    dailyHistoryAvailable
                        ? activeDays.ToString(CultureInfo.CurrentCulture)
                            + " active day"
                            + (activeDays == 1 ? string.Empty : "s")
                        : "Daily totals not supplied"));
                metrics.Add(CreateMetricCard(
                    "Requests",
                    "\u2014",
                    "Not supplied by this feed"));
                metrics.Add(CreateMetricCard(
                    "Tokens per minute",
                    "\u2014",
                    "Not supplied by this feed"));
                content.Controls.Add(CreateCardGrid(
                    width,
                    metrics,
                    4,
                    210,
                    14));
                content.Controls.Add(CreateTokenChart(width, selectedBuckets));
                content.Controls.Add(CreateSectionHeading(
                    width,
                    "ACCOUNT PROFILE",
                    "Profile highlights"));
                List<Control> profile = new List<Control>();
                profile.Add(CreateMetricCard(
                    "Lifetime tokens",
                    tokenUsage == null
                        ? "\u2014"
                        : FormatTokenCount(tokenUsage.LifetimeTokens),
                    "Account summary"));
                profile.Add(CreateMetricCard(
                    "Peak daily tokens",
                    tokenUsage == null
                        ? "\u2014"
                        : FormatTokenCount(tokenUsage.PeakDailyTokens),
                    "Highest recorded day"));
                profile.Add(CreateMetricCard(
                    "Current streak",
                    tokenUsage == null
                        ? "\u2014"
                        : FormatDayCount(tokenUsage.CurrentStreakDays),
                    "Account summary"));
                profile.Add(CreateMetricCard(
                    "Longest running turn",
                    tokenUsage == null
                        ? "\u2014"
                        : FormatDuration(tokenUsage.LongestRunningTurnSeconds),
                    "Account summary"));
                content.Controls.Add(CreateCardGrid(
                    width,
                    profile,
                    4,
                    210,
                    14));
                content.Controls.Add(CreateScopeNote(width));
            }
            content.ResumeLayout();
            ResizeCards();
            UpdateContentExtent();
            contentScroll.Value = previousScroll;
            NativeDrawing.SetRedraw(content, true);
        }

        private Control CreateClaudeBetaInfo(int width)
        {
            RoundedPanel card = Card(width, Px(330));
            card.Margin = Pad(0, 0, 0, 30);
            card.FillColor = Color.FromArgb(27, 22, 21);
            card.BorderColor = NativePalette.Claude;

            card.Controls.Add(Label(
                "ANTHROPIC CLAUDE",
                8.0f,
                FontStyle.Bold,
                NativePalette.Claude,
                PointPx(24, 24),
                new Size(width - Px(48), Px(22))));
            Label title = Label(
                "Claude native support is coming next",
                24.0f,
                FontStyle.Bold,
                NativePalette.Primary,
                PointPx(24, 56),
                new Size(width - Px(48), Px(52)));
            title.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            card.Controls.Add(title);

            Label status = Label(
                "NATIVE BETA · NOT CONNECTED · NO LIVE CLAUDE DATA",
                9.0f,
                FontStyle.Bold,
                NativePalette.Claude,
                PointPx(24, 118),
                new Size(width - Px(48), Px(25)));
            status.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            card.Controls.Add(status);

            Label explanation = Label(
                "This native beta does not collect or display Claude quota or activity yet. This screen explains the current development status; it is not a connection indicator.",
                11.0f,
                FontStyle.Regular,
                NativePalette.Secondary,
                PointPx(24, 158),
                new Size(width - Px(48), Px(62)));
            explanation.Anchor =
                AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            card.Controls.Add(explanation);

            Label boundary = Label(
                "Claude support will remain separate from Codex and will not infer a quota percentage from token activity. Codex monitoring continues in the background.",
                9.5f,
                FontStyle.Regular,
                NativePalette.Muted,
                PointPx(24, 232),
                new Size(width - Px(48), Px(48)));
            boundary.Anchor =
                AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            card.Controls.Add(boundary);

            Label returnHint = Label(
                "Select Codex above to return to live quota and reset details.",
                10.0f,
                FontStyle.Bold,
                NativePalette.Accent,
                PointPx(24, 292),
                new Size(width - Px(48), Px(24)));
            returnHint.Anchor =
                AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            card.Controls.Add(returnHint);
            return card;
        }

        private Control CreateIntro(int width)
        {
            float textScale = settings.FlyoutTextScale / 100.0f;
            int eyebrowTop = 2;
            int eyebrowHeight = Math.Max(20, (int)Math.Ceiling(20 * textScale));
            int titleTop = eyebrowTop + eyebrowHeight + 3;
            int titleHeight = Math.Max(68, (int)Math.Ceiling(60 * textScale));
            int descriptionTop = titleTop + titleHeight + 2;
            int descriptionHeight = Math.Max(25, (int)Math.Ceiling(25 * textScale));
            int stateTop = descriptionTop + descriptionHeight;
            int stateHeight = Math.Max(21, (int)Math.Ceiling(21 * textScale));
            int copyHeight = stateTop + stateHeight;
            int infoHeight = Math.Max(88, (int)Math.Ceiling(88 * textScale));
            Panel panel = new Panel();
            panel.Size = new Size(width, Px(copyHeight));
            panel.Margin = Pad(0, 0, 0, 22);
            panel.BackColor = BackColor;
            panel.Tag = "full";

            Label eyebrow = Label(
                "OPENAI CODEX",
                8.0f,
                FontStyle.Bold,
                NativePalette.Accent,
                PointPx(2, eyebrowTop),
                SizePx(260, eyebrowHeight));
            panel.Controls.Add(eyebrow);
            Label title = Label(
                "Codex usage",
                26.0f,
                FontStyle.Bold,
                NativePalette.Primary,
                PointPx(0, titleTop),
                SizePx(430, titleHeight));
            title.TextAlign = ContentAlignment.MiddleLeft;
            panel.Controls.Add(title);
            Label description = Label(
                snapshot == null || string.IsNullOrEmpty(snapshot.PlanType)
                    ? "Live plan limits stay separate from historical activity."
                    : snapshot.PlanType
                        + " plan · live limits stay separate from historical activity.",
                10.0f,
                FontStyle.Regular,
                NativePalette.Secondary,
                PointPx(0, descriptionTop),
                SizePx(520, descriptionHeight));
            panel.Controls.Add(description);
            Label state = Label(
                "● " + stateText,
                8.5f,
                FontStyle.Bold,
                stateColor,
                PointPx(0, stateTop),
                SizePx(300, stateHeight));
            panel.Controls.Add(state);

            RoundedPanel info = new RoundedPanel();
            info.Location = new Point(0, Px(copyHeight + 12));
            info.Size = new Size(width, Px(infoHeight));
            info.FillColor = Color.FromArgb(13, 23, 35);
            info.BorderColor = NativePalette.Border;
            info.Controls.Add(Label(
                "i",
                12.0f,
                FontStyle.Bold,
                NativePalette.Accent,
                PointPx(16, Math.Max(15, (int)Math.Ceiling(15 * textScale))),
                SizePx(26, Math.Max(30, (int)Math.Ceiling(30 * textScale)))));
            int infoTitleTop = Math.Max(13, (int)Math.Ceiling(13 * textScale));
            int infoTitleHeight = Math.Max(30, (int)Math.Ceiling(30 * textScale));
            int infoFreshnessTop = infoTitleTop + infoTitleHeight + 3;
            int infoFreshnessHeight = Math.Max(27, (int)Math.Ceiling(27 * textScale));
            Label infoTitle = Label(
                "Read-only account quota from the documented local Codex app-server.",
                9.0f,
                FontStyle.Bold,
                NativePalette.Primary,
                PointPx(50, infoTitleTop),
                new Size(Math.Max(1, info.Width - Px(66)), Px(infoTitleHeight)));
            infoTitle.Anchor =
                AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            info.Controls.Add(infoTitle);
            Label infoFreshness = Label(
                snapshot == null
                    ? "Waiting for the first live update."
                    : "Last known "
                        + snapshot.ObservedAtUtc.ToLocalTime().ToString(
                            "ddd, MMM d, h:mm:ss tt",
                            CultureInfo.CurrentCulture),
                8.0f,
                FontStyle.Regular,
                NativePalette.Muted,
                PointPx(50, infoFreshnessTop),
                new Size(Math.Max(1, info.Width - Px(66)), Px(infoFreshnessHeight)));
            infoFreshness.Anchor =
                AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            info.Controls.Add(infoFreshness);
            panel.Controls.Add(info);

            Action layout = delegate
            {
                bool stackInfo = panel.Width < Px(1080);
                int infoWidth = stackInfo
                    ? panel.Width
                    : Math.Min(
                        Px(520),
                        Math.Max(Px(360), panel.Width / 2));
                int copyWidth = stackInfo
                    ? panel.Width
                    : Math.Max(Px(360), panel.Width - infoWidth - Px(32));
                int desiredHeight = stackInfo
                    ? Px(copyHeight + 12 + infoHeight)
                    : Px(Math.Max(copyHeight, 10 + infoHeight));
                if (panel.Height != desiredHeight)
                {
                    panel.Height = desiredHeight;
                }
                title.Width = copyWidth;
                description.Width = copyWidth;
                info.Location = stackInfo
                    ? new Point(0, Px(copyHeight + 12))
                    : new Point(panel.Width - infoWidth, Px(10));
                info.Size = new Size(infoWidth, Px(infoHeight));
            };
            panel.Resize += delegate
            {
                layout();
            };
            layout();
            return panel;
        }

        private Control CreateEmpty(int width)
        {
            RoundedPanel card = Card(width, Px(148));
            Label title = Label(
                "No Codex quota data yet",
                15.0f,
                FontStyle.Bold,
                NativePalette.Primary,
                PointPx(22, 24),
                new Size(Math.Max(Px(300), width - Px(44)), Px(34)));
            title.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            card.Controls.Add(title);
            Label description = Label(
                stateText,
                10.0f,
                FontStyle.Regular,
                NativePalette.Secondary,
                PointPx(22, 66),
                new Size(Math.Max(Px(300), width - Px(44)), Px(55)));
            description.Anchor =
                AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            card.Controls.Add(description);
            return card;
        }

        private Control CreateSectionHeading(int width, string eyebrow, string title)
        {
            float textScale = settings.FlyoutTextScale / 100.0f;
            int eyebrowHeight = Math.Max(20, (int)Math.Ceiling(20 * textScale));
            int titleTop = eyebrowHeight + 5;
            int titleHeight = Math.Max(37, (int)Math.Ceiling(37 * textScale));
            Panel heading = new Panel();
            heading.Size = new Size(width, Px(titleTop + titleHeight + 7));
            heading.Margin = Pad(0, 0, 0, 6);
            heading.BackColor = BackColor;
            heading.Tag = "full";
            heading.Controls.Add(Label(
                eyebrow,
                8.0f,
                FontStyle.Bold,
                NativePalette.Accent,
                PointPx(2, 0),
                SizePx(300, eyebrowHeight)));
            heading.Controls.Add(Label(
                title,
                18.0f,
                FontStyle.Bold,
                NativePalette.Primary,
                PointPx(0, titleTop),
                SizePx(500, titleHeight)));
            return heading;
        }

        private Control CreateQuotaCard(int width, UsageWindow window)
        {
            float textScale = settings.FlyoutTextScale / 100.0f;
            int limitHeight = Math.Max(20, (int)Math.Ceiling(20 * textScale));
            int nameTop = 16 + limitHeight + 3;
            int nameHeight = Math.Max(60, (int)Math.Ceiling(54 * textScale));
            int percentHeight = Math.Max(42, (int)Math.Ceiling(42 * textScale));
            int remainingTop = 18 + percentHeight - 2;
            int remainingHeight = Math.Max(18, (int)Math.Ceiling(18 * textScale));
            int progressTop = Math.Max(
                nameTop + nameHeight + 7,
                remainingTop + remainingHeight + 8);
            int resetTop = progressTop + 24;
            int resetHeight = Math.Max(36, (int)Math.Ceiling(36 * textScale));
            int cardHeight = Math.Max(164, resetTop + resetHeight + 14);
            RoundedPanel card = Card(width, Px(cardHeight));
            card.Margin = Padding.Empty;
            card.Controls.Add(Label(
                string.IsNullOrEmpty(window.LimitId)
                    ? "CODEX LIMIT"
                    : window.LimitId.ToUpperInvariant(),
                8.0f,
                FontStyle.Bold,
                NativePalette.Muted,
                PointPx(20, 16),
                SizePx(300, limitHeight)));
            int nameWidth = Math.Max(Px(120), width - Px(150));
            string displayLabel;
            using (Font measurementFont = UiFont(13.0f, FontStyle.Bold))
            {
                displayLabel = QuotaDisplayLabel(
                    window.Label,
                    nameWidth,
                    measurementFont);
            }
            Label name = Label(
                displayLabel,
                13.0f,
                FontStyle.Bold,
                NativePalette.Primary,
                PointPx(20, nameTop),
                new Size(
                    nameWidth,
                    Px(nameHeight)));
            name.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            name.AutoEllipsis = false;
            card.Controls.Add(name);

            Label percent = Label(
                window.RemainingPercent + "%",
                20.0f,
                FontStyle.Bold,
                NativePalette.Primary,
                new Point(width - Px(120), Px(18)),
                SizePx(98, percentHeight));
            percent.TextAlign = ContentAlignment.TopRight;
            percent.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            card.Controls.Add(percent);
            Label remaining = Label(
                "LEFT",
                7.5f,
                FontStyle.Bold,
                NativePalette.Muted,
                new Point(width - Px(120), Px(remainingTop)),
                SizePx(98, remainingHeight));
            remaining.TextAlign = ContentAlignment.TopRight;
            remaining.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            card.Controls.Add(remaining);

            UsageProgress progress = new UsageProgress();
            progress.Location = PointPx(20, progressTop);
            progress.Size = new Size(width - Px(40), Px(8));
            progress.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            progress.Percentage = window.RemainingPercent;
            card.Controls.Add(progress);

            Label reset = Label(
                UsageFormatting.ResetTime(window.ResetsAtUtc),
                9.0f,
                FontStyle.Regular,
                NativePalette.Secondary,
                PointPx(20, resetTop),
                new Size(width - Px(40), Px(resetHeight)));
            reset.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            card.Controls.Add(reset);
            return card;
        }

        private static string QuotaDisplayLabel(
            string label,
            int availableWidth,
            Font font)
        {
            string text = label ?? string.Empty;
            Size measured = TextRenderer.MeasureText(
                text,
                font,
                Size.Empty,
                TextFormatFlags.SingleLine
                    | TextFormatFlags.NoPadding
                    | TextFormatFlags.NoPrefix);
            if (measured.Width <= availableWidth)
            {
                return text;
            }
            string[] separators = new string[] { " · ", " - ", " – ", " — " };
            foreach (string separator in separators)
            {
                int index = text.LastIndexOf(
                    separator,
                    StringComparison.Ordinal);
                if (index > 0 && index + separator.Length < text.Length)
                {
                    return text.Substring(0, index).TrimEnd()
                        + Environment.NewLine
                        + text.Substring(index + separator.Length).TrimStart();
                }
            }
            return text;
        }

        private Control CreateBankedCard(int width, BankedResetSummary banked)
        {
            int itemCount = banked.DetailsAvailable ? banked.Items.Count : 0;
            float textScale = settings.FlyoutTextScale / 100.0f;
            int countHeight = Math.Max(42, (int)Math.Ceiling(42 * textScale));
            int availableTop = 22 + countHeight - 2;
            int availableHeight = Math.Max(20, (int)Math.Ceiling(20 * textScale));
            int freshnessHeight = Math.Max(18, (int)Math.Ceiling(18 * textScale));
            int countFreshnessTop = Math.Max(
                76,
                availableTop + availableHeight + 4);
            int detailFreshnessTop = countFreshnessTop + freshnessHeight + 1;
            int detailsTop = Math.Max(
                Math.Max(124, (int)Math.Ceiling(124 * textScale)),
                detailFreshnessTop + freshnessHeight + 8);
            int itemHeight = Math.Max(26, (int)Math.Ceiling(26 * textScale));
            int logicalHeight = Math.Max(
                (int)Math.Ceiling(174 * textScale),
                detailsTop + itemCount * itemHeight + 18);
            RoundedPanel card = Card(width, Px(logicalHeight));
            card.Tag = "banked";
            card.Margin = Padding.Empty;
            card.Controls.Add(Label(
                "CODEX",
                8.0f,
                FontStyle.Bold,
                NativePalette.Accent,
                PointPx(20, 16),
                SizePx(180, 20)));
            card.Controls.Add(Label(
                "Banked resets",
                15.0f,
                FontStyle.Bold,
                NativePalette.Primary,
                PointPx(20, 40),
                SizePx(300, 34)));
            Label count = Label(
                banked.AvailableCount.HasValue
                    ? banked.AvailableCount.Value.ToString(CultureInfo.CurrentCulture)
                    : "—",
                22.0f,
                FontStyle.Bold,
                NativePalette.Primary,
                new Point(width - Px(130), Px(22)),
                SizePx(105, countHeight));
            count.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            count.TextAlign = ContentAlignment.MiddleRight;
            card.Controls.Add(count);
            Label available = Label(
                "AVAILABLE",
                7.5f,
                FontStyle.Bold,
                NativePalette.Muted,
                new Point(width - Px(130), Px(availableTop)),
                SizePx(105, availableHeight));
            available.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            available.TextAlign = ContentAlignment.MiddleRight;
            card.Controls.Add(available);

            Label countFreshness = Label(
                "Count last known · "
                    + CompactObservationText(banked.CountObservedAtUtc),
                7.8f,
                FontStyle.Regular,
                NativePalette.Muted,
                PointPx(20, countFreshnessTop),
                new Size(width - Px(40), Px(freshnessHeight)));
            countFreshness.Anchor =
                AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            card.Controls.Add(countFreshness);
            string detailText = banked.DetailsAvailable
                ? banked.Items.Count
                    + " expiry rows last known · "
                    + CompactObservationText(banked.DetailsObservedAtUtc)
                : "Expiry dates unavailable in the latest update.";
            Label detailFreshness = Label(
                detailText,
                7.8f,
                FontStyle.Regular,
                banked.DetailsAvailable
                    ? NativePalette.Muted
                    : NativePalette.Warning,
                PointPx(20, detailFreshnessTop),
                new Size(width - Px(40), Px(freshnessHeight)));
            detailFreshness.Anchor =
                AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            card.Controls.Add(detailFreshness);

            if (!banked.DetailsAvailable)
            {
                return card;
            }

            int top = detailsTop;
            foreach (BankedReset reset in banked.Items)
            {
                bool expired = reset.ExpiresAtUtc.HasValue
                    && reset.ExpiresAtUtc.Value <= DateTime.UtcNow;
                card.Controls.Add(Label(
                    string.IsNullOrEmpty(reset.Title) ? "Full reset" : reset.Title,
                    10.0f,
                    FontStyle.Bold,
                    NativePalette.Primary,
                    PointPx(20, top),
                    SizePx(120, itemHeight)));
                Label expiry = Label(
                    reset.ExpiresAtUtc.HasValue
                        ? (expired ? "Expired " : "Expires ")
                            + reset.ExpiresAtUtc.Value.ToLocalTime().ToString(
                                "ddd, MMM d, h:mm:ss tt",
                                CultureInfo.CurrentCulture)
                            + (expired ? " · last known" : string.Empty)
                        : "Expiry not returned",
                    7.6f,
                    FontStyle.Regular,
                    expired ? NativePalette.Warning : NativePalette.Secondary,
                    new Point(Px(126), Px(top)),
                    new Size(width - Px(146), Px(itemHeight)));
                expiry.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                expiry.TextAlign = ContentAlignment.MiddleRight;
                card.Controls.Add(expiry);
                top += itemHeight;
            }
            return card;
        }

        private Control CreateActivityFilter(int width)
        {
            RoundedPanel filter = Card(width, Px(118));
            filter.Margin = Pad(0, 0, 0, 14);
            Label dateLabel = Label(
                "Date range",
                8.0f,
                FontStyle.Bold,
                NativePalette.Secondary,
                PointPx(18, 17),
                SizePx(90, 22));
            filter.Controls.Add(dateLabel);

            string[] ranges = new string[]
            {
                "Today",
                "7 days",
                "30 days",
                "90 days",
                "All",
                "Custom"
            };
            int[] rangeDays = new int[] { 1, 7, 30, 90, 0, -1 };
            bool historyAvailable = snapshot != null
                && snapshot.TokenUsage != null
                && snapshot.TokenUsage.DailyBuckets.Count > 0;
            List<RoundedButton> options = new List<RoundedButton>();
            for (int index = 0; index < ranges.Length; index++)
            {
                string range = ranges[index];
                int days = rangeDays[index];
                bool selected = days == selectedActivityDays
                    && (days >= 0
                        || (customActivityFromDate.HasValue
                            && customActivityToDate.HasValue));
                RoundedButton option = new RoundedButton();
                option.Text = range;
                option.Size = SizePx(66, 34);
                option.Font = UiFont(8.0f, FontStyle.Bold);
                option.ForeColor = selected
                    ? NativePalette.Primary
                    : NativePalette.Secondary;
                option.Selected = selected;
                option.Enabled = historyAvailable;
                option.TabStop = option.Enabled;
                option.AccessibleName = range + " activity range";
                option.AccessibleDescription = days < 0
                    ? "Opens a dialog to choose an inclusive range from the recorded Codex daily totals."
                    : "Shows daily Codex token totals for "
                        + range.ToLowerInvariant()
                        + ".";
                if (option.Enabled)
                {
                    int selectedDays = days;
                    option.Click += delegate
                    {
                        if (selectedDays < 0)
                        {
                            ShowCustomActivityRangeDialog();
                            return;
                        }
                        selectedActivityDays = selectedDays;
                        Build();
                    };
                }
                filter.Controls.Add(option);
                options.Add(option);
            }

            Label freshness = Label(
                historyAvailable
                    ? "Daily account totals last known \u00b7 "
                        + CompactObservationText(
                            snapshot.TokenUsageObservedAtUtc)
                    : "Daily token history is not available yet.",
                8.2f,
                FontStyle.Regular,
                historyAvailable
                    ? NativePalette.Secondary
                    : NativePalette.Warning,
                PointPx(18, 57),
                new Size(width - Px(36), Px(23)));
            freshness.Anchor =
                AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            filter.Controls.Add(freshness);

            Label unavailable = Label(
                "Model and reasoning filters: Not supplied by this Codex feed.",
                8.2f,
                FontStyle.Regular,
                NativePalette.Muted,
                PointPx(18, 82),
                new Size(width - Px(36), Px(25)));
            unavailable.Anchor =
                AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            filter.Controls.Add(unavailable);

            Action layout = delegate
            {
                int measuredLabelWidth = TextRenderer.MeasureText(
                    dateLabel.Text,
                    dateLabel.Font,
                    Size.Empty,
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
                dateLabel.Width = Math.Max(Px(62), measuredLabelWidth + Px(2));
                int optionGap = Px(2);
                int optionsLeft = dateLabel.Right + Px(10);
                int optionWidth = CalculateActivityFilterButtonWidth(
                    filter.ClientSize.Width,
                    optionsLeft,
                    options.Count,
                    optionGap,
                    Px(18),
                    Px(66));
                int left = optionsLeft;
                foreach (RoundedButton option in options)
                {
                    option.Location = new Point(left, Px(12));
                    option.Width = optionWidth;
                    left += optionWidth + optionGap;
                }
                freshness.Width = Math.Max(1, filter.ClientSize.Width - Px(36));
                unavailable.Width = Math.Max(1, filter.ClientSize.Width - Px(36));
            };
            filter.Resize += delegate
            {
                layout();
            };
            layout();
            return filter;
        }

        private void ShowCustomActivityRangeDialog()
        {
            DateTime earliest;
            DateTime latest;
            if (!TryGetRecordedActivityBounds(out earliest, out latest))
            {
                return;
            }

            DateTime initialFrom;
            DateTime initialTo;
            if (customActivityFromDate.HasValue
                && customActivityToDate.HasValue)
            {
                initialFrom = ClampDate(
                    customActivityFromDate.Value,
                    earliest,
                    latest);
                initialTo = ClampDate(
                    customActivityToDate.Value,
                    earliest,
                    latest);
            }
            else if (selectedActivityDays > 0)
            {
                DateTime today = DateTime.Today;
                initialFrom = ClampDate(
                    today.AddDays(-(selectedActivityDays - 1)),
                    earliest,
                    latest);
                initialTo = ClampDate(today, earliest, latest);
            }
            else
            {
                initialFrom = earliest;
                initialTo = latest;
            }
            if (!IsValidCustomActivityRange(initialFrom, initialTo))
            {
                initialFrom = earliest;
                initialTo = latest;
            }

            using (CustomActivityRangeDialog dialog =
                new CustomActivityRangeDialog(
                    settings,
                    currentDpi,
                    earliest,
                    latest,
                    initialFrom,
                    initialTo))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                customActivityFromDate = dialog.SelectedFromDate.Date;
                customActivityToDate = dialog.SelectedToDate.Date;
                selectedActivityDays = -1;
            }
            Build();
        }

        private bool TryGetRecordedActivityBounds(
            out DateTime earliest,
            out DateTime latest)
        {
            earliest = DateTime.MaxValue;
            latest = DateTime.MinValue;
            if (snapshot == null || snapshot.TokenUsage == null)
            {
                return false;
            }
            bool found = false;
            foreach (TokenUsageDailyBucket bucket in
                snapshot.TokenUsage.DailyBuckets)
            {
                DateTime date;
                if (bucket == null
                    || !DateTime.TryParseExact(
                        bucket.StartDate,
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out date))
                {
                    continue;
                }
                date = date.Date;
                earliest = date < earliest ? date : earliest;
                latest = date > latest ? date : latest;
                found = true;
            }
            return found;
        }

        private static DateTime ClampDate(
            DateTime value,
            DateTime minimum,
            DateTime maximum)
        {
            DateTime date = value.Date;
            if (date < minimum.Date)
            {
                return minimum.Date;
            }
            if (date > maximum.Date)
            {
                return maximum.Date;
            }
            return date;
        }

        internal static int CalculateActivityFilterButtonWidth(
            int filterWidth,
            int optionsLeft,
            int optionCount,
            int optionGap,
            int rightInset,
            int preferredWidth)
        {
            if (optionCount <= 0)
            {
                return 0;
            }
            int usable = Math.Max(
                optionCount,
                filterWidth
                    - Math.Max(0, optionsLeft)
                    - Math.Max(0, rightInset)
                    - Math.Max(0, optionGap) * (optionCount - 1));
            return Math.Max(
                1,
                Math.Min(Math.Max(1, preferredWidth), usable / optionCount));
        }

        internal static int CalculateProviderLeftLimit(
            int productTitleRight,
            int productSubtitleRight,
            int minimumLeft,
            int gap)
        {
            return Math.Max(
                Math.Max(0, minimumLeft),
                Math.Max(productTitleRight, productSubtitleRight)
                    + Math.Max(0, gap));
        }

        private Control CreateMetricCard(
            string title,
            string value,
            string subtitle)
        {
            float textScale = settings.FlyoutTextScale / 100.0f;
            int titleHeight = Math.Max(26, (int)Math.Ceiling(26 * textScale));
            int valueTop = 14 + titleHeight + 4;
            int valueHeight = Math.Max(42, (int)Math.Ceiling(42 * textScale));
            int subtitleTop = valueTop + valueHeight + 4;
            int subtitleHeight = Math.Max(
                32,
                (int)Math.Ceiling(32 * textScale));
            int logicalHeight = Math.Max(
                116,
                subtitleTop + subtitleHeight + 14);
            RoundedPanel card = Card(Px(230), Px(logicalHeight));
            card.Margin = Padding.Empty;
            card.Controls.Add(Label(
                title,
                8.0f,
                FontStyle.Bold,
                NativePalette.Secondary,
                PointPx(16, 14),
                SizePx(190, titleHeight)));
            card.Controls.Add(Label(
                value,
                20.0f,
                FontStyle.Bold,
                NativePalette.Primary,
                PointPx(16, valueTop),
                SizePx(190, valueHeight)));
            card.Controls.Add(Label(
                subtitle,
                7.5f,
                FontStyle.Regular,
                NativePalette.Muted,
                PointPx(16, subtitleTop),
                SizePx(190, subtitleHeight)));
            return card;
        }

        private Control CreateTokenChart(
            int width,
            IList<TokenUsageDailyBucket> buckets)
        {
            RoundedPanel card = Card(width, Px(302));
            card.Margin = Pad(0, 0, 0, 18);
            card.Controls.Add(Label(
                "Tokens by day",
                13.0f,
                FontStyle.Bold,
                NativePalette.Primary,
                PointPx(20, 16),
                SizePx(320, 30)));
            card.Controls.Add(Label(
                ActivityRangeLabel(),
                8.0f,
                FontStyle.Regular,
                NativePalette.Secondary,
                PointPx(20, 44),
                SizePx(320, 22)));

            Label total = Label(
                buckets != null && buckets.Count > 0
                    ? FormatTokenCount(SumTokens(buckets))
                    : "\u2014",
                15.0f,
                FontStyle.Bold,
                NativePalette.Accent,
                new Point(width - Px(220), Px(17)),
                SizePx(198, 34));
            total.TextAlign = ContentAlignment.TopRight;
            total.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            card.Controls.Add(total);

            TokenHistoryChart chart = new TokenHistoryChart();
            chart.Location = PointPx(16, 70);
            chart.Size = new Size(width - Px(32), Px(214));
            chart.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            chart.Font = UiFont(8.0f, FontStyle.Regular);
            chart.SetData(buckets);
            card.Controls.Add(chart);
            return card;
        }

        private List<TokenUsageDailyBucket> SelectedActivityBuckets()
        {
            List<TokenUsageDailyBucket> selected =
                new List<TokenUsageDailyBucket>();
            if (snapshot == null || snapshot.TokenUsage == null)
            {
                return selected;
            }

            DateTime today = DateTime.Today;
            foreach (TokenUsageDailyBucket bucket in
                snapshot.TokenUsage.DailyBuckets)
            {
                DateTime date;
                if (bucket == null
                    || !DateTime.TryParseExact(
                        bucket.StartDate,
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out date))
                {
                    continue;
                }
                if (ActivityDateIsSelected(
                    date,
                    selectedActivityDays,
                    customActivityFromDate,
                    customActivityToDate,
                    today))
                {
                    selected.Add(bucket);
                }
            }
            return selected;
        }

        internal static bool ActivityDateIsSelected(
            DateTime date,
            int selectedDays,
            DateTime? customFrom,
            DateTime? customTo,
            DateTime today)
        {
            DateTime candidate = date.Date;
            if (selectedDays < 0)
            {
                return customFrom.HasValue
                    && customTo.HasValue
                    && IsValidCustomActivityRange(
                        customFrom.Value,
                        customTo.Value)
                    && candidate >= customFrom.Value.Date
                    && candidate <= customTo.Value.Date;
            }
            if (selectedDays == 0)
            {
                return true;
            }
            if (selectedDays < 1)
            {
                return false;
            }
            DateTime end = today.Date;
            DateTime start = end.AddDays(-(selectedDays - 1));
            return candidate >= start && candidate <= end;
        }

        internal static bool IsValidCustomActivityRange(
            DateTime from,
            DateTime to)
        {
            return from.Date <= to.Date;
        }

        private static long SumTokens(IList<TokenUsageDailyBucket> buckets)
        {
            long total = 0;
            if (buckets == null)
            {
                return total;
            }
            foreach (TokenUsageDailyBucket bucket in buckets)
            {
                if (bucket == null || bucket.Tokens < 0)
                {
                    continue;
                }
                try
                {
                    total = checked(total + bucket.Tokens);
                }
                catch (OverflowException)
                {
                    return long.MaxValue;
                }
            }
            return total;
        }

        private static int CountActiveDays(
            IList<TokenUsageDailyBucket> buckets)
        {
            int count = 0;
            if (buckets == null)
            {
                return count;
            }
            foreach (TokenUsageDailyBucket bucket in buckets)
            {
                if (bucket != null && bucket.Tokens > 0)
                {
                    count++;
                }
            }
            return count;
        }

        private string ActivityRangeLabel()
        {
            if (selectedActivityDays < 0
                && customActivityFromDate.HasValue
                && customActivityToDate.HasValue)
            {
                return "Custom · "
                    + customActivityFromDate.Value.ToString(
                        "MMM d, yyyy",
                        CultureInfo.CurrentCulture)
                    + " – "
                    + customActivityToDate.Value.ToString(
                        "MMM d, yyyy",
                        CultureInfo.CurrentCulture);
            }
            if (selectedActivityDays == 0)
            {
                return "All recorded daily totals";
            }
            if (selectedActivityDays == 1)
            {
                return "Today";
            }
            return selectedActivityDays.ToString(CultureInfo.CurrentCulture)
                + " days";
        }

        private static string FormatTokenCount(long? value)
        {
            return value.HasValue ? FormatTokenCount(value.Value) : "\u2014";
        }

        private static string FormatTokenCount(long value)
        {
            if (value >= 1000000000)
            {
                return (value / 1000000000.0).ToString(
                    "0.#",
                    CultureInfo.CurrentCulture) + "B";
            }
            if (value >= 1000000)
            {
                return (value / 1000000.0).ToString(
                    "0.#",
                    CultureInfo.CurrentCulture) + "M";
            }
            if (value >= 1000)
            {
                return (value / 1000.0).ToString(
                    "0.#",
                    CultureInfo.CurrentCulture) + "K";
            }
            return value.ToString("N0", CultureInfo.CurrentCulture);
        }

        private static string FormatDayCount(long? value)
        {
            return value.HasValue
                ? value.Value.ToString("N0", CultureInfo.CurrentCulture) + "d"
                : "\u2014";
        }

        private static string FormatDuration(long? seconds)
        {
            if (!seconds.HasValue)
            {
                return "\u2014";
            }
            if (seconds.Value >= 3600)
            {
                return (seconds.Value / 3600.0).ToString(
                    "0.#",
                    CultureInfo.CurrentCulture) + "h";
            }
            if (seconds.Value < 60)
            {
                return "<1m";
            }
            return Math.Round(seconds.Value / 60.0)
                .ToString("N0", CultureInfo.CurrentCulture) + "m";
        }

        private Panel CreateCardGrid(
            int width,
            IList<Control> cards,
            int preferredColumns,
            int minimumCardLogicalWidth,
            int bottomMarginLogical)
        {
            Panel grid = new Panel();
            grid.Width = width;
            grid.BackColor = BackColor;
            grid.Margin = Pad(0, 0, 0, bottomMarginLogical);
            grid.Tag = "full";
            foreach (Control card in cards)
            {
                grid.Controls.Add(card);
            }
            grids[grid] = new GridLayoutSpec(
                preferredColumns,
                Px(minimumCardLogicalWidth),
                Px(12));
            LayoutCardGrid(grid);
            return grid;
        }

        private void LayoutCardGrid(Panel grid)
        {
            GridLayoutSpec spec;
            if (grid == null
                || !grids.TryGetValue(grid, out spec)
                || grid.Controls.Count == 0)
            {
                return;
            }

            int columns = Math.Min(spec.PreferredColumns, grid.Controls.Count);
            while (columns > 1
                && (grid.ClientSize.Width - spec.Gap * (columns - 1)) / columns
                    < spec.MinimumCardWidth)
            {
                columns--;
            }
            int cardWidth = Math.Max(
                Px(180),
                (grid.ClientSize.Width - spec.Gap * (columns - 1)) / columns);
            int top = 0;
            int index = 0;
            while (index < grid.Controls.Count)
            {
                int rowEnd = Math.Min(grid.Controls.Count, index + columns);
                int rowHeight = 1;
                for (int rowIndex = index; rowIndex < rowEnd; rowIndex++)
                {
                    rowHeight = Math.Max(rowHeight, grid.Controls[rowIndex].Height);
                }
                for (int rowIndex = index; rowIndex < rowEnd; rowIndex++)
                {
                    int column = rowIndex - index;
                    Control card = grid.Controls[rowIndex];
                    bool stretchBankedCard = rowEnd - index == 1
                        && columns > 1
                        && string.Equals(
                            card.Tag as string,
                            "banked",
                            StringComparison.Ordinal);
                    card.Bounds = new Rectangle(
                        stretchBankedCard
                            ? 0
                            : column * (cardWidth + spec.Gap),
                        top,
                        stretchBankedCard ? grid.ClientSize.Width : cardWidth,
                        rowHeight);
                }
                top += rowHeight + spec.Gap;
                index = rowEnd;
            }
            grid.Height = Math.Max(1, top - spec.Gap);
        }

        private Control CreateScopeNote(int width)
        {
            float textScale = settings.FlyoutTextScale / 100.0f;
            int titleHeight = Math.Max(28, (int)Math.Ceiling(28 * textScale));
            int descriptionTop = 17 + titleHeight + 4;
            int descriptionHeight = Math.Max(
                70,
                (int)Math.Ceiling(70 * textScale));
            int noteHeight = descriptionTop + descriptionHeight + 15;
            RoundedPanel note = Card(width, Px(noteHeight));
            note.Margin = Pad(0, 0, 0, 30);
            note.FillColor = Color.FromArgb(12, 22, 34);
            Label title = Label(
                "Daily account-level activity",
                11.0f,
                FontStyle.Bold,
                NativePalette.Primary,
                PointPx(20, 17),
                new Size(width - Px(40), Px(titleHeight)));
            title.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            note.Controls.Add(title);
            Label description = Label(
                snapshot != null && snapshot.TokenUsage != null
                    ? "Codex supplies daily account token totals and profile highlights. It does not attribute this history to a model or reasoning level, and it does not supply request counts or tokens per minute. Activity history is never presented as quota remaining."
                    : "The optional Codex activity feed has not supplied history yet. Quota and reset data above remain live; no missing activity values are estimated.",
                9.0f,
                FontStyle.Regular,
                NativePalette.Secondary,
                PointPx(20, descriptionTop),
                new Size(width - Px(40), Px(descriptionHeight)));
            description.Anchor =
                AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            note.Controls.Add(description);
            return note;
        }

        private sealed class CustomActivityRangeDialog : Form
        {
            private const int WmDpiChanged = 0x02E0;
            private const int WmNcLButtonDown = 0x00A1;
            private const int HtCaption = 0x0002;
            private const int DwmUseImmersiveDarkMode = 20;
            private const int DwmUseImmersiveDarkModeBefore20H1 = 19;

            private readonly NativeSettings dialogSettings;
            private readonly Panel chromeHeader;
            private readonly Label chromeTitleLabel;
            private readonly RoundedButton closeButton;
            private readonly Label eyebrowLabel;
            private readonly Label titleLabel;
            private readonly Label descriptionLabel;
            private readonly RoundedPanel fromCard;
            private readonly RoundedPanel toCard;
            private readonly Label fromLabel;
            private readonly Label toLabel;
            private readonly DateTimePicker fromPicker;
            private readonly DateTimePicker toPicker;
            private readonly Label rangeHintLabel;
            private readonly Label errorLabel;
            private readonly RoundedButton cancelButton;
            private readonly RoundedButton applyButton;
            private int dialogDpi;
            private float dialogDpiScale;
            private bool handlingDialogDpiChange;

            public CustomActivityRangeDialog(
                NativeSettings settings,
                int initialDpi,
                DateTime earliest,
                DateTime latest,
                DateTime initialFrom,
                DateTime initialTo)
            {
                dialogSettings = settings ?? new NativeSettings();
                dialogSettings.Normalize();
                dialogDpi = Math.Max(96, initialDpi);
                dialogDpiScale = dialogDpi / 96.0f;

                Text = "Custom activity date range";
                AccessibleName = "Custom activity date range";
                AccessibleDescription =
                    "Choose inclusive from and to dates for recorded Codex daily token totals.";
                StartPosition = FormStartPosition.CenterParent;
                FormBorderStyle = FormBorderStyle.None;
                MaximizeBox = false;
                MinimizeBox = false;
                ControlBox = false;
                ShowIcon = false;
                ShowInTaskbar = false;
                AutoScaleMode = AutoScaleMode.None;
                BackColor = NativePalette.Shell;
                ForeColor = NativePalette.Primary;
                ClientSize = DialogSize(560, 500);
                KeyPreview = true;
                DoubleBuffered = true;

                chromeHeader = new Panel();
                chromeHeader.BackColor = NativePalette.ShellRaised;
                chromeHeader.Cursor = Cursors.SizeAll;
                chromeHeader.MouseDown += DragDialog;
                chromeHeader.Paint += delegate(object sender, PaintEventArgs eventArgs)
                {
                    using (Pen separator = new Pen(NativePalette.Border))
                    {
                        eventArgs.Graphics.DrawLine(
                            separator,
                            0,
                            chromeHeader.Height - 1,
                            chromeHeader.Width,
                            chromeHeader.Height - 1);
                    }
                };
                Controls.Add(chromeHeader);

                chromeTitleLabel = DialogLabel(
                    "Custom activity date range",
                    10.0f,
                    FontStyle.Bold,
                    NativePalette.Primary,
                    false);
                chromeTitleLabel.Cursor = Cursors.SizeAll;
                chromeTitleLabel.MouseDown += DragDialog;
                chromeHeader.Controls.Add(chromeTitleLabel);

                closeButton = new RoundedButton();
                closeButton.Text = "\u00d7";
                closeButton.Font = DialogFont(13.0f, FontStyle.Regular);
                closeButton.ForeColor = NativePalette.Secondary;
                closeButton.FillColor = NativePalette.ShellRaised;
                closeButton.HoverColor = NativePalette.CardSelected;
                closeButton.BorderColor = Color.Transparent;
                closeButton.AccessibleName = "Close custom date range";
                closeButton.DialogResult = DialogResult.Cancel;
                closeButton.TabStop = false;
                closeButton.Click += delegate
                {
                    DialogResult = DialogResult.Cancel;
                    Close();
                };
                chromeHeader.Controls.Add(closeButton);

                eyebrowLabel = DialogLabel(
                    "ACTIVITY HISTORY",
                    8.0f,
                    FontStyle.Bold,
                    NativePalette.Accent,
                    false);
                Controls.Add(eyebrowLabel);

                titleLabel = DialogLabel(
                    "Choose a custom date range",
                    18.0f,
                    FontStyle.Bold,
                    NativePalette.Primary,
                    false);
                Controls.Add(titleLabel);

                descriptionLabel = DialogLabel(
                    "The chart and summary cards will include only daily totals returned inside these dates.",
                    9.2f,
                    FontStyle.Regular,
                    NativePalette.Secondary,
                    false);
                Controls.Add(descriptionLabel);

                fromCard = DialogCard();
                Controls.Add(fromCard);
                fromLabel = DialogLabel(
                    "&From",
                    10.0f,
                    FontStyle.Bold,
                    NativePalette.Primary,
                    true);
                fromLabel.TabIndex = 0;
                fromCard.Controls.Add(fromLabel);
                fromPicker = DatePicker(
                    "From date",
                    "First date included in the custom activity range.",
                    earliest,
                    latest,
                    initialFrom);
                fromPicker.TabIndex = 1;
                fromCard.Controls.Add(fromPicker);

                toCard = DialogCard();
                Controls.Add(toCard);
                toLabel = DialogLabel(
                    "&To",
                    10.0f,
                    FontStyle.Bold,
                    NativePalette.Primary,
                    true);
                toLabel.TabIndex = 2;
                toCard.Controls.Add(toLabel);
                toPicker = DatePicker(
                    "To date",
                    "Last date included in the custom activity range.",
                    earliest,
                    latest,
                    initialTo);
                toPicker.TabIndex = 3;
                toCard.Controls.Add(toPicker);

                rangeHintLabel = DialogLabel(
                    "Both dates are included.\r\nAvailable history: "
                        + earliest.ToString(
                            "MMM d, yyyy",
                            CultureInfo.CurrentCulture)
                        + " to "
                        + latest.ToString(
                            "MMM d, yyyy",
                            CultureInfo.CurrentCulture)
                        + ".",
                    8.5f,
                    FontStyle.Regular,
                    NativePalette.Muted,
                    false);
                rangeHintLabel.AccessibleName = "Recorded history dates";
                Controls.Add(rangeHintLabel);

                errorLabel = DialogLabel(
                    string.Empty,
                    8.7f,
                    FontStyle.Bold,
                    NativePalette.Warning,
                    false);
                errorLabel.AccessibleName = "Date range validation";
                errorLabel.AccessibleRole = AccessibleRole.Alert;
                Controls.Add(errorLabel);

                cancelButton = new RoundedButton();
                cancelButton.Text = "Cancel";
                cancelButton.DialogResult = DialogResult.Cancel;
                cancelButton.Font = DialogFont(9.0f, FontStyle.Bold);
                cancelButton.ForeColor = NativePalette.Primary;
                cancelButton.FillColor = NativePalette.ShellRaised;
                cancelButton.BorderColor = NativePalette.Border;
                cancelButton.AccessibleName = "Cancel custom date range";
                cancelButton.TabIndex = 4;
                Controls.Add(cancelButton);

                applyButton = new RoundedButton();
                applyButton.Text = "Apply range";
                applyButton.Font = DialogFont(9.0f, FontStyle.Bold);
                applyButton.ForeColor = NativePalette.Primary;
                applyButton.FillColor = NativePalette.CardSelected;
                applyButton.BorderColor = NativePalette.AccentDeep;
                applyButton.AccessibleName = "Apply custom date range";
                applyButton.AccessibleDescription =
                    "Applies the inclusive from and to dates to activity history.";
                applyButton.TabIndex = 5;
                applyButton.Click += delegate
                {
                    AcceptRange();
                };
                Controls.Add(applyButton);

                AcceptButton = applyButton;
                CancelButton = cancelButton;
                fromPicker.ValueChanged += delegate
                {
                    UpdateValidation();
                };
                toPicker.ValueChanged += delegate
                {
                    UpdateValidation();
                };
                Shown += delegate
                {
                    int actualDpi = DeviceDpi;
                    if (actualDpi > 0 && actualDpi != dialogDpi)
                    {
                        dialogDpi = actualDpi;
                        dialogDpiScale = dialogDpi / 96.0f;
                        ClientSize = DialogSize(560, 500);
                        LayoutDialog();
                    }
                    EnableDarkChrome();
                    fromPicker.Focus();
                };

                LayoutDialog();
                UpdateValidation();
            }

            public DateTime SelectedFromDate { get; private set; }
            public DateTime SelectedToDate { get; private set; }

            protected override void WndProc(ref Message message)
            {
                if (message.Msg != WmDpiChanged)
                {
                    base.WndProc(ref message);
                    return;
                }

                int nextDpi = unchecked(
                    (int)(message.WParam.ToInt64() & 0xffff));
                Rectangle suggestedBounds;
                bool hasSuggestedBounds = TryReadSuggestedBounds(
                    message.LParam,
                    out suggestedBounds);
                handlingDialogDpiChange = true;
                try
                {
                    base.WndProc(ref message);
                    if (nextDpi > 0 && nextDpi != dialogDpi)
                    {
                        dialogDpi = nextDpi;
                        dialogDpiScale = dialogDpi / 96.0f;
                    }
                    if (hasSuggestedBounds)
                    {
                        Bounds = suggestedBounds;
                    }
                    LayoutDialog();
                    Invalidate(true);
                }
                finally
                {
                    handlingDialogDpiChange = false;
                }
            }

            protected override void OnResize(EventArgs eventArgs)
            {
                base.OnResize(eventArgs);
                if (!handlingDialogDpiChange && fromCard != null)
                {
                    LayoutDialog();
                }
            }

            protected override void OnPaint(PaintEventArgs eventArgs)
            {
                base.OnPaint(eventArgs);
                eventArgs.Graphics.SmoothingMode =
                    System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                Rectangle bounds = new Rectangle(
                    0,
                    0,
                    Math.Max(1, ClientSize.Width - 1),
                    Math.Max(1, ClientSize.Height - 1));
                using (System.Drawing.Drawing2D.GraphicsPath path =
                    NativeDrawing.RoundedRectangle(bounds, DialogPx(12)))
                using (Pen border = new Pen(NativePalette.BorderStrong))
                {
                    eventArgs.Graphics.DrawPath(border, path);
                }
            }

            private RoundedPanel DialogCard()
            {
                RoundedPanel card = new RoundedPanel();
                card.FillColor = NativePalette.Card;
                card.BorderColor = NativePalette.Border;
                card.CornerRadius = 12;
                return card;
            }

            private DateTimePicker DatePicker(
                string accessibleName,
                string accessibleDescription,
                DateTime minimum,
                DateTime maximum,
                DateTime value)
            {
                DateTimePicker picker = new DateTimePicker();
                picker.Format = DateTimePickerFormat.Custom;
                picker.CustomFormat = "ddd, MMM d, yyyy";
                picker.MinDate = minimum.Date;
                picker.MaxDate = maximum.Date;
                picker.Value = ClampDate(value, minimum, maximum);
                picker.Font = DialogFont(10.0f, FontStyle.Regular);
                picker.ForeColor = NativePalette.Primary;
                picker.BackColor = NativePalette.ShellRaised;
                picker.CalendarForeColor = NativePalette.Primary;
                picker.CalendarMonthBackground = NativePalette.Card;
                picker.CalendarTitleBackColor = NativePalette.CardSelected;
                picker.CalendarTitleForeColor = NativePalette.Primary;
                picker.CalendarTrailingForeColor = NativePalette.Muted;
                picker.AccessibleName = accessibleName;
                picker.AccessibleDescription = accessibleDescription;
                return picker;
            }

            private Label DialogLabel(
                string text,
                float size,
                FontStyle style,
                Color color,
                bool useMnemonic)
            {
                Label label = new Label();
                label.Text = text;
                label.Font = DialogFont(size, style);
                label.ForeColor = color;
                label.BackColor = Color.Transparent;
                label.UseMnemonic = useMnemonic;
                label.AutoEllipsis = false;
                return label;
            }

            private Font DialogFont(float size, FontStyle style)
            {
                return new Font(
                    dialogSettings.InterfaceFontName,
                    size * (dialogSettings.FlyoutTextScale / 100.0f),
                    style,
                    GraphicsUnit.Point);
            }

            private void LayoutDialog()
            {
                if (eyebrowLabel == null)
                {
                    return;
                }
                int inset = DialogPx(28);
                int width = Math.Max(1, ClientSize.Width - inset * 2);
                chromeHeader.Bounds = new Rectangle(
                    0,
                    0,
                    ClientSize.Width,
                    DialogPx(54));
                chromeTitleLabel.Bounds = new Rectangle(
                    DialogPx(18),
                    0,
                    Math.Max(1, chromeHeader.Width - DialogPx(78)),
                    chromeHeader.Height);
                chromeTitleLabel.TextAlign = ContentAlignment.MiddleLeft;
                closeButton.Bounds = new Rectangle(
                    chromeHeader.Width - DialogPx(48),
                    DialogPx(7),
                    DialogPx(40),
                    DialogPx(40));
                eyebrowLabel.Bounds = new Rectangle(
                    inset,
                    DialogPx(70),
                    width,
                    DialogPx(20));
                titleLabel.Bounds = new Rectangle(
                    inset,
                    DialogPx(93),
                    width,
                    DialogPx(42));
                descriptionLabel.Bounds = new Rectangle(
                    inset,
                    DialogPx(135),
                    width,
                    DialogPx(46));

                fromCard.Bounds = new Rectangle(
                    inset,
                    DialogPx(190),
                    width,
                    DialogPx(70));
                toCard.Bounds = new Rectangle(
                    inset,
                    DialogPx(270),
                    width,
                    DialogPx(70));
                LayoutDateCard(fromCard, fromLabel, fromPicker);
                LayoutDateCard(toCard, toLabel, toPicker);

                rangeHintLabel.Bounds = new Rectangle(
                    inset,
                    DialogPx(354),
                    width,
                    DialogPx(44));
                errorLabel.Bounds = new Rectangle(
                    inset,
                    DialogPx(401),
                    width,
                    DialogPx(24));

                int buttonWidth = DialogPx(112);
                int buttonHeight = DialogPx(42);
                int buttonGap = DialogPx(10);
                int buttonTop = ClientSize.Height
                    - DialogPx(28)
                    - buttonHeight;
                applyButton.Bounds = new Rectangle(
                    ClientSize.Width - inset - buttonWidth,
                    buttonTop,
                    buttonWidth,
                    buttonHeight);
                cancelButton.Bounds = new Rectangle(
                    applyButton.Left - buttonGap - buttonWidth,
                    buttonTop,
                    buttonWidth,
                    buttonHeight);
                UpdateDialogRegion();
            }

            private void DragDialog(object sender, MouseEventArgs eventArgs)
            {
                if (eventArgs.Button != MouseButtons.Left)
                {
                    return;
                }
                ReleaseCapture();
                SendMessage(Handle, WmNcLButtonDown, (IntPtr)HtCaption, IntPtr.Zero);
            }

            private void UpdateDialogRegion()
            {
                Rectangle bounds = new Rectangle(
                    0,
                    0,
                    Math.Max(1, Width),
                    Math.Max(1, Height));
                using (System.Drawing.Drawing2D.GraphicsPath path =
                    NativeDrawing.RoundedRectangle(bounds, DialogPx(12)))
                {
                    Region previous = Region;
                    Region = new Region(path);
                    if (previous != null)
                    {
                        previous.Dispose();
                    }
                }
            }

            private void LayoutDateCard(
                RoundedPanel card,
                Label label,
                DateTimePicker picker)
            {
                label.Bounds = new Rectangle(
                    DialogPx(18),
                    DialogPx(23),
                    DialogPx(110),
                    DialogPx(28));
                picker.Bounds = new Rectangle(
                    DialogPx(142),
                    DialogPx(18),
                    Math.Max(1, card.ClientSize.Width - DialogPx(160)),
                    DialogPx(34));
            }

            private void UpdateValidation()
            {
                if (fromPicker == null || toPicker == null)
                {
                    return;
                }
                bool valid = IsValidCustomActivityRange(
                    fromPicker.Value,
                    toPicker.Value);
                applyButton.Enabled = valid;
                string nextMessage = valid
                    ? string.Empty
                    : "From date must be the same as or earlier than To date.";
                errorLabel.Text = nextMessage;
            }

            private void AcceptRange()
            {
                UpdateValidation();
                if (!applyButton.Enabled)
                {
                    fromPicker.Focus();
                    return;
                }
                SelectedFromDate = fromPicker.Value.Date;
                SelectedToDate = toPicker.Value.Date;
                DialogResult = DialogResult.OK;
                Close();
            }

            private Size DialogSize(int width, int height)
            {
                return new Size(DialogPx(width), DialogPx(height));
            }

            private int DialogPx(int logical)
            {
                return (int)Math.Round(logical * dialogDpiScale);
            }

            private void EnableDarkChrome()
            {
                if (SystemInformation.HighContrast)
                {
                    return;
                }
                int enabled = 1;
                try
                {
                    int result = DwmSetWindowAttribute(
                        Handle,
                        DwmUseImmersiveDarkMode,
                        ref enabled,
                        sizeof(int));
                    if (result != 0)
                    {
                        DwmSetWindowAttribute(
                            Handle,
                            DwmUseImmersiveDarkModeBefore20H1,
                            ref enabled,
                            sizeof(int));
                    }
                }
                catch (DllNotFoundException)
                {
                }
                catch (EntryPointNotFoundException)
                {
                }

                try
                {
                    SetWindowTheme(
                        fromPicker.Handle,
                        "DarkMode_Explorer",
                        null);
                    SetWindowTheme(
                        toPicker.Handle,
                        "DarkMode_Explorer",
                        null);
                }
                catch (DllNotFoundException)
                {
                }
                catch (EntryPointNotFoundException)
                {
                }
            }

            [DllImport("dwmapi.dll")]
            private static extern int DwmSetWindowAttribute(
                IntPtr window,
                int attribute,
                ref int value,
                int valueSize);

            [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
            private static extern int SetWindowTheme(
                IntPtr window,
                string subAppName,
                string subIdList);

            [DllImport("user32.dll")]
            private static extern bool ReleaseCapture();

            [DllImport("user32.dll")]
            private static extern IntPtr SendMessage(
                IntPtr window,
                int message,
                IntPtr wParam,
                IntPtr lParam);
        }

        private sealed class GridLayoutSpec
        {
            public GridLayoutSpec(
                int preferredColumns,
                int minimumCardWidth,
                int gap)
            {
                PreferredColumns = preferredColumns;
                MinimumCardWidth = minimumCardWidth;
                Gap = gap;
            }

            public int PreferredColumns { get; private set; }
            public int MinimumCardWidth { get; private set; }
            public int Gap { get; private set; }
        }

        private RoundedPanel Card(int width, int scaledHeight)
        {
            RoundedPanel card = new RoundedPanel();
            // Callers already pass a DPI-scaled height. Scaling it again made
            // cards increasingly tall on 125%, 150%, and 200% displays.
            card.Size = new Size(width, Math.Max(1, scaledHeight));
            card.FillColor = NativePalette.Card;
            card.BorderColor = NativePalette.Border;
            card.Tag = "full";
            return card;
        }

        private void ResizeCards()
        {
            if (content == null || content.ClientSize.Width <= 0)
            {
                return;
            }
            CenterContent();
            int width = CardWidth();
            foreach (Control control in content.Controls)
            {
                if (string.Equals(control.Tag as string, "full", StringComparison.Ordinal))
                {
                    control.Width = width;
                }
                Panel grid = control as Panel;
                if (grid != null && grids.ContainsKey(grid))
                {
                    LayoutCardGrid(grid);
                }
            }
        }

        private void LayoutSurface()
        {
            header.Width = ClientSize.Width;
            contentHost.Location = new Point(0, header.Bottom);
            contentHost.Size = new Size(
                ClientSize.Width,
                Math.Max(1, ClientSize.Height - header.Height));
            content.Width = Math.Max(
                1,
                contentHost.ClientSize.Width - contentScroll.Width);
            contentScroll.Height = contentHost.ClientSize.Height;
            content.Top = -contentScroll.Value;
        }

        private int CardWidth()
        {
            int available = Math.Max(Px(520), content.ClientSize.Width);
            int gutter = Math.Max(
                Px(24),
                Math.Min(Px(56), (int)Math.Round(available * 0.04f)));
            return Math.Max(
                Px(520),
                Math.Min(Px(1368), available - gutter * 2));
        }

        private void CenterContent()
        {
            if (content == null || content.ClientSize.Width <= 0)
            {
                return;
            }
            int width = CardWidth();
            int usable = content.ClientSize.Width;
            int left = Math.Max(Px(24), (usable - width) / 2);
            Padding desired = Pad(0, 28, 0, 36);
            desired.Left = left;
            desired.Right = Math.Max(Px(24), usable - width - left);
            if (content.Padding != desired)
            {
                content.Padding = desired;
            }
        }

        private void UpdateContentExtent()
        {
            if (content == null || contentHost == null || contentScroll == null)
            {
                return;
            }
            content.PerformLayout();
            int extent = content.Padding.Top + content.Padding.Bottom;
            foreach (Control control in content.Controls)
            {
                extent = Math.Max(
                    extent,
                    control.Bottom
                        + control.Margin.Bottom
                        + content.Padding.Bottom);
            }
            int viewport = Math.Max(1, contentHost.ClientSize.Height);
            content.Height = Math.Max(viewport, extent);
            contentScroll.SetMetrics(viewport, content.Height);
            content.Top = -contentScroll.Value;
        }

        protected override bool ProcessCmdKey(
            ref Message message,
            Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                Hide();
                return true;
            }
            if (keyData == (Keys.Control | Keys.R))
            {
                if (!showingClaudeInfo && refreshButton.Enabled)
                {
                    refreshButton.PerformClick();
                }
                return true;
            }
            if (keyData == Keys.PageDown)
            {
                contentScroll.Value += contentHost.ClientSize.Height;
                return true;
            }
            if (keyData == Keys.PageUp)
            {
                contentScroll.Value -= contentHost.ClientSize.Height;
                return true;
            }
            if (keyData == Keys.Home)
            {
                contentScroll.Value = 0;
                return true;
            }
            if (keyData == Keys.End)
            {
                contentScroll.Value = contentScroll.Maximum;
                return true;
            }
            return base.ProcessCmdKey(ref message, keyData);
        }

        protected override void OnMouseWheel(MouseEventArgs eventArgs)
        {
            if (contentScroll != null && contentScroll.Maximum > 0)
            {
                contentScroll.Value += wheelScroll.Consume(
                    eventArgs.Delta,
                    SystemInformation.MouseWheelScrollDelta,
                    Px(72));
                return;
            }
            base.OnMouseWheel(eventArgs);
        }

        private Label Label(
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
            label.Font = UiFont(size, style);
            label.ForeColor = color;
            label.BackColor = Color.Transparent;
            label.UseMnemonic = false;
            return label;
        }

        private Font UiFont(float size, FontStyle style)
        {
            return NativeDrawing.CreateSafeFont(
                settings.InterfaceFontName,
                size * (settings.FlyoutTextScale / 100.0f),
                style);
        }

        private int Px(int logical)
        {
            return (int)Math.Round(logical * dpiScale);
        }

        private Point PointPx(int x, int y)
        {
            return new Point(Px(x), Px(y));
        }

        private Size SizePx(int width, int height)
        {
            return new Size(Px(width), Px(height));
        }

        private Padding Pad(int left, int top, int right, int bottom)
        {
            return new Padding(Px(left), Px(top), Px(right), Px(bottom));
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private static string Safe(string message)
        {
            return string.IsNullOrEmpty(message)
                ? "Codex usage is temporarily unavailable."
                : message;
        }

        private static string ObservationText(DateTime? observedAtUtc)
        {
            return observedAtUtc.HasValue
                ? observedAtUtc.Value.ToLocalTime().ToString(
                    "ddd, MMM d, yyyy h:mm:ss tt",
                    CultureInfo.CurrentCulture)
                : "not available";
        }

        private static string CompactObservationText(DateTime? observedAtUtc)
        {
            return observedAtUtc.HasValue
                ? observedAtUtc.Value.ToLocalTime().ToString(
                    "MMM d, h:mm tt",
                    CultureInfo.CurrentCulture)
                : "not available";
        }
    }
}
