using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace UsageApp.Native
{
    internal static class NativePalette
    {
        private static Color ThemeColor(Color standard, Color highContrast)
        {
            return SystemInformation.HighContrast ? highContrast : standard;
        }

        public static readonly Color Shell = ThemeColor(
            Color.FromArgb(8, 15, 25),
            SystemColors.Window);
        public static readonly Color ShellRaised = ThemeColor(
            Color.FromArgb(11, 20, 32),
            SystemColors.Window);
        public static readonly Color Card = ThemeColor(
            Color.FromArgb(16, 26, 40),
            SystemColors.Window);
        public static readonly Color CardSelected = ThemeColor(
            Color.FromArgb(29, 51, 72),
            SystemColors.Window);
        public static readonly Color Border = ThemeColor(
            Color.FromArgb(38, 55, 76),
            SystemColors.WindowText);
        public static readonly Color BorderStrong = ThemeColor(
            Color.FromArgb(55, 94, 124),
            SystemColors.Highlight);
        public static readonly Color Primary = ThemeColor(
            Color.FromArgb(240, 246, 255),
            SystemColors.WindowText);
        public static readonly Color Secondary = ThemeColor(
            Color.FromArgb(147, 166, 193),
            SystemColors.WindowText);
        public static readonly Color Muted = ThemeColor(
            Color.FromArgb(113, 134, 163),
            SystemColors.WindowText);
        public static readonly Color Accent = ThemeColor(
            Color.FromArgb(101, 201, 240),
            SystemColors.Highlight);
        public static readonly Color AccentDeep = ThemeColor(
            Color.FromArgb(82, 143, 188),
            SystemColors.Highlight);
        public static readonly Color AccentPurple = ThemeColor(
            Color.FromArgb(139, 145, 248),
            SystemColors.Highlight);
        public static readonly Color Claude = ThemeColor(
            Color.FromArgb(231, 157, 109),
            SystemColors.Highlight);
        public static readonly Color Success = ThemeColor(
            Color.FromArgb(109, 218, 139),
            SystemColors.Highlight);
        public static readonly Color Warning = ThemeColor(
            Color.FromArgb(245, 183, 84),
            SystemColors.Highlight);
        public static readonly Color Error = ThemeColor(
            Color.FromArgb(245, 126, 134),
            SystemColors.Highlight);
    }

    internal static class NativeDrawing
    {
        [DllImport("user32.dll")]
        private static extern int GetDpiForSystem();

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(
            IntPtr handle,
            int message,
            IntPtr wParam,
            IntPtr lParam);

        private const int WmSetRedraw = 0x000B;

        public static int SystemDpi
        {
            get
            {
                try
                {
                    int dpi = GetDpiForSystem();
                    if (dpi > 0)
                    {
                        return dpi;
                    }
                }
                catch
                {
                    // Older Windows versions fall through to the GDI reading.
                }

                using (Graphics graphics = Graphics.FromHwnd(IntPtr.Zero))
                {
                    return Math.Max(96, (int)Math.Round(graphics.DpiX));
                }
            }
        }

        internal static void SetRedraw(Control control, bool enabled)
        {
            if (control == null || !control.IsHandleCreated) return;
            SendMessage(control.Handle, WmSetRedraw,
                enabled ? new IntPtr(1) : IntPtr.Zero, IntPtr.Zero);
            if (enabled) control.Invalidate(true);
        }

        public static float SystemScale
        {
            get { return SystemDpi / 96.0f; }
        }

        public static int Dpi(Control control, int logicalPixels)
        {
            int dpi = control != null && control.IsHandleCreated
                ? control.DeviceDpi
                : SystemDpi;
            return Math.Max(
                logicalPixels == 0 ? 0 : 1,
                (int)Math.Round(logicalPixels * (dpi / 96.0f)));
        }

        public static Font CreateSafeFont(
            string familyName,
            float size,
            FontStyle requestedStyle)
        {
            string requested = string.IsNullOrWhiteSpace(familyName)
                ? "Segoe UI"
                : familyName;
            try
            {
                using (FontFamily family = new FontFamily(requested))
                {
                    FontStyle available = AvailableFontStyle(
                        family,
                        requestedStyle);
                    return new Font(
                        family,
                        Math.Max(1.0f, size),
                        available,
                        GraphicsUnit.Point);
                }
            }
            catch
            {
                return new Font(
                    "Segoe UI",
                    Math.Max(1.0f, size),
                    FontStyle.Regular,
                    GraphicsUnit.Point);
            }
        }

        private static FontStyle AvailableFontStyle(
            FontFamily family,
            FontStyle requested)
        {
            if (family.IsStyleAvailable(requested))
            {
                return requested;
            }
            FontStyle[] fallbacks = new FontStyle[]
            {
                FontStyle.Regular,
                FontStyle.Bold,
                FontStyle.Italic,
                FontStyle.Bold | FontStyle.Italic
            };
            foreach (FontStyle fallback in fallbacks)
            {
                if (family.IsStyleAvailable(fallback))
                {
                    return fallback;
                }
            }
            return FontStyle.Regular;
        }

        public static Color ParentSurfaceColor(Control control)
        {
            Control parent = control == null ? null : control.Parent;
            while (parent != null)
            {
                RoundedPanel rounded = parent as RoundedPanel;
                if (rounded != null)
                {
                    return rounded.FillColor;
                }
                if (parent.BackColor.A == 255)
                {
                    return parent.BackColor;
                }
                parent = parent.Parent;
            }
            return NativePalette.Shell;
        }

        public static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int diameter = Math.Max(2, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
            Rectangle arc = new Rectangle(bounds.Location, new Size(diameter, diameter));
            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class BufferedFlowLayoutPanel : FlowLayoutPanel
    {
        public BufferedFlowLayoutPanel()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.UserPaint,
                true);
            ResizeRedraw = true;
        }
    }

    internal class RoundedPanel : Panel
    {
        public RoundedPanel()
        {
            DoubleBuffered = true;
            CornerRadius = 14;
            FillColor = NativePalette.Card;
            BorderColor = NativePalette.Border;
            BorderThickness = 1.0f;
            BackColor = Color.Transparent;
        }

        public int CornerRadius { get; set; }
        public Color FillColor { get; set; }
        public Color BorderColor { get; set; }
        public float BorderThickness { get; set; }

        protected override void OnPaintBackground(PaintEventArgs eventArgs)
        {
            eventArgs.Graphics.Clear(NativeDrawing.ParentSurfaceColor(this));
            if (Width < 3 || Height < 3)
            {
                return;
            }
            eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = NativeDrawing.RoundedRectangle(
                bounds,
                NativeDrawing.Dpi(this, CornerRadius)))
            using (SolidBrush fill = new SolidBrush(FillColor))
            {
                eventArgs.Graphics.FillPath(fill, path);
            }
        }

        protected override void OnPaint(PaintEventArgs eventArgs)
        {
            base.OnPaint(eventArgs);
            if (Width < 4
                || Height < 4
                || BorderThickness <= 0
                || BorderColor == Color.Transparent)
            {
                return;
            }
            eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float borderWidth = Math.Max(
                1.0f,
                BorderThickness * DeviceDpi / 96.0f);
            int inset = Math.Max(1, (int)Math.Ceiling(borderWidth / 2.0f));
            Rectangle bounds = new Rectangle(
                inset,
                inset,
                Math.Max(1, Width - inset * 2 - 1),
                Math.Max(1, Height - inset * 2 - 1));
            using (GraphicsPath path = NativeDrawing.RoundedRectangle(
                bounds,
                NativeDrawing.Dpi(this, CornerRadius)))
            using (Pen border = new Pen(BorderColor, borderWidth))
            {
                eventArgs.Graphics.DrawPath(border, path);
            }
        }
    }

    internal class RoundedButton : Button
    {
        private bool hovered;

        public RoundedButton()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            DoubleBuffered = true;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            CornerRadius = 11;
            FillColor = NativePalette.ShellRaised;
            HoverColor = Color.FromArgb(24, 39, 58);
            SelectedColor = NativePalette.CardSelected;
            BorderColor = NativePalette.Border;
            Selected = false;
            UseVisualStyleBackColor = false;
        }

        public int CornerRadius { get; set; }
        public Color FillColor { get; set; }
        public Color HoverColor { get; set; }
        public Color SelectedColor { get; set; }
        public Color BorderColor { get; set; }
        public bool Selected { get; set; }

        protected override void OnMouseEnter(EventArgs eventArgs)
        {
            hovered = true;
            Invalidate();
            base.OnMouseEnter(eventArgs);
        }

        protected override void OnMouseLeave(EventArgs eventArgs)
        {
            hovered = false;
            Invalidate();
            base.OnMouseLeave(eventArgs);
        }

        protected override void OnPaint(PaintEventArgs eventArgs)
        {
            if (Width < 4 || Height < 4)
            {
                return;
            }
            eventArgs.Graphics.Clear(NativeDrawing.ParentSurfaceColor(this));
            eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            bool highlighted = Selected || hovered;
            Color fill = SystemInformation.HighContrast
                ? (highlighted ? SystemColors.Highlight : SystemColors.Window)
                : (Selected ? SelectedColor : (hovered ? HoverColor : FillColor));
            Color textColor = SystemInformation.HighContrast && highlighted
                ? SystemColors.HighlightText
                : ForeColor;
            Color borderColor = Selected ? NativePalette.AccentDeep : BorderColor;
            float borderWidth =
                (Selected ? 1.5f : 1.0f) * DeviceDpi / 96.0f;
            int inset = Math.Max(1, (int)Math.Ceiling(borderWidth / 2.0f));
            Rectangle bounds = new Rectangle(
                inset,
                inset,
                Math.Max(1, Width - inset * 2 - 1),
                Math.Max(1, Height - inset * 2 - 1));
            using (GraphicsPath path = NativeDrawing.RoundedRectangle(
                bounds,
                NativeDrawing.Dpi(this, CornerRadius)))
            using (SolidBrush brush = new SolidBrush(fill))
            using (Pen border = new Pen(borderColor, borderWidth))
            {
                eventArgs.Graphics.FillPath(brush, path);
                eventArgs.Graphics.DrawPath(border, path);
            }

            TextRenderer.DrawText(
                eventArgs.Graphics,
                Text,
                Font,
                ClientRectangle,
                textColor,
                TextFormatFlags.HorizontalCenter
                    | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.EndEllipsis
                    | TextFormatFlags.NoPadding);

            if (Focused && ShowFocusCues)
            {
                int focusInset = NativeDrawing.Dpi(this, 4);
                Rectangle focus = Rectangle.Inflate(
                    ClientRectangle,
                    -focusInset,
                    -focusInset);
                ControlPaint.DrawFocusRectangle(eventArgs.Graphics, focus, textColor, fill);
            }
        }
    }

    internal sealed class ChoicePicker : Control
    {
        private string[] options = new string[0];
        private int selectedIndex;
        private bool hovered;
        private ContextMenuStrip activeMenu;
        private int pendingIndex = -1;

        public ChoicePicker()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.ResizeRedraw
                    | ControlStyles.SupportsTransparentBackColor
                    | ControlStyles.UserPaint,
                true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            TabStop = true;
            Size = new Size(120, 34);
            AccessibleRole = AccessibleRole.ComboBox;
        }

        public event EventHandler SelectedIndexChanged;

        public bool IsDropDownOpen
        {
            get { return activeMenu != null; }
        }

        public string[] Options
        {
            get { return (string[])options.Clone(); }
            set
            {
                string previousValue = SelectedValueText;
                options = value == null
                    ? new string[0]
                    : (string[])value.Clone();
                selectedIndex = options.Length == 0
                    ? -1
                    : Math.Max(0, Math.Min(options.Length - 1, selectedIndex));
                Invalidate();
                if (!string.Equals(previousValue, SelectedValueText, StringComparison.Ordinal))
                {
                    NotifyAccessibleValueChanged();
                }
            }
        }

        public int SelectedIndex
        {
            get { return selectedIndex; }
            set
            {
                int next = options.Length == 0
                    ? -1
                    : Math.Max(0, Math.Min(options.Length - 1, value));
                if (next == selectedIndex)
                {
                    return;
                }
                selectedIndex = next;
                Invalidate();
                NotifyAccessibleValueChanged();
                EventHandler handler = SelectedIndexChanged;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            }
        }

        private string SelectedValueText
        {
            get
            {
                return selectedIndex >= 0 && selectedIndex < options.Length
                    ? options[selectedIndex] ?? string.Empty
                    : string.Empty;
            }
        }

        protected override AccessibleObject CreateAccessibilityInstance()
        {
            return new ChoicePickerAccessibleObject(this);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            Keys keyCode = keyData & Keys.KeyCode;
            if (keyCode == Keys.Up
                || keyCode == Keys.Down
                || keyCode == Keys.Home
                || keyCode == Keys.End
                || keyCode == Keys.F4)
            {
                return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnMouseDown(MouseEventArgs eventArgs)
        {
            if (CanFocus)
            {
                Focus();
            }
            base.OnMouseDown(eventArgs);
        }

        protected override void OnMouseEnter(EventArgs eventArgs)
        {
            hovered = true;
            Invalidate();
            base.OnMouseEnter(eventArgs);
        }

        protected override void OnMouseLeave(EventArgs eventArgs)
        {
            hovered = false;
            Invalidate();
            base.OnMouseLeave(eventArgs);
        }

        protected override void OnGotFocus(EventArgs eventArgs)
        {
            base.OnGotFocus(eventArgs);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs eventArgs)
        {
            base.OnLostFocus(eventArgs);
            Invalidate();
        }

        protected override void OnClick(EventArgs eventArgs)
        {
            base.OnClick(eventArgs);
            ShowChoices();
        }

        protected override void OnKeyDown(KeyEventArgs eventArgs)
        {
            if (eventArgs.KeyCode == Keys.Enter
                || eventArgs.KeyCode == Keys.Space
                || eventArgs.KeyCode == Keys.F4
                || (eventArgs.Alt && eventArgs.KeyCode == Keys.Down))
            {
                ShowChoices();
                eventArgs.Handled = true;
                eventArgs.SuppressKeyPress = true;
                return;
            }
            if (eventArgs.KeyCode == Keys.Up && selectedIndex > 0)
            {
                SelectedIndex = selectedIndex - 1;
                eventArgs.Handled = true;
                eventArgs.SuppressKeyPress = true;
                return;
            }
            if (eventArgs.KeyCode == Keys.Down
                && selectedIndex >= 0
                && selectedIndex < options.Length - 1)
            {
                SelectedIndex = selectedIndex + 1;
                eventArgs.Handled = true;
                eventArgs.SuppressKeyPress = true;
                return;
            }
            if (eventArgs.KeyCode == Keys.Home && options.Length > 0)
            {
                SelectedIndex = 0;
                eventArgs.Handled = true;
                eventArgs.SuppressKeyPress = true;
                return;
            }
            if (eventArgs.KeyCode == Keys.End && options.Length > 0)
            {
                SelectedIndex = options.Length - 1;
                eventArgs.Handled = true;
                eventArgs.SuppressKeyPress = true;
                return;
            }
            base.OnKeyDown(eventArgs);
        }

        protected override void OnPaint(PaintEventArgs eventArgs)
        {
            if (Width < 4 || Height < 4)
            {
                return;
            }
            eventArgs.Graphics.Clear(NativeDrawing.ParentSurfaceColor(this));
            eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float borderWidth = DeviceDpi / 96.0f;
            int inset = Math.Max(1, (int)Math.Ceiling(borderWidth / 2.0f));
            Rectangle bounds = new Rectangle(
                inset,
                inset,
                Math.Max(1, Width - inset * 2 - 1),
                Math.Max(1, Height - inset * 2 - 1));
            using (GraphicsPath path = NativeDrawing.RoundedRectangle(
                bounds,
                NativeDrawing.Dpi(this, 8)))
            using (SolidBrush fill = new SolidBrush(
                SystemInformation.HighContrast
                    ? SystemColors.Window
                    : (hovered || Focused
                        ? Color.FromArgb(21, 35, 52)
                        : Color.FromArgb(11, 20, 31))))
            using (Pen border = new Pen(
                Focused ? NativePalette.AccentDeep : NativePalette.Border,
                borderWidth))
            {
                eventArgs.Graphics.FillPath(fill, path);
                eventArgs.Graphics.DrawPath(border, path);
            }

            string text = selectedIndex >= 0 && selectedIndex < options.Length
                ? options[selectedIndex] ?? string.Empty
                : "Choose";
            int horizontal = NativeDrawing.Dpi(this, 11);
            int arrowWidth = NativeDrawing.Dpi(this, 25);
            Rectangle textBounds = new Rectangle(
                horizontal,
                0,
                Math.Max(1, Width - horizontal - arrowWidth),
                Height);
            TextRenderer.DrawText(
                eventArgs.Graphics,
                text,
                Font,
                textBounds,
                ForeColor,
                TextFormatFlags.Left
                    | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.EndEllipsis
                    | TextFormatFlags.NoPadding);

            int centerX = Width - NativeDrawing.Dpi(this, 14);
            int centerY = Height / 2;
            int spread = NativeDrawing.Dpi(this, 4);
            using (Pen arrow = new Pen(NativePalette.Secondary, Math.Max(1.0f, borderWidth)))
            {
                arrow.StartCap = LineCap.Round;
                arrow.EndCap = LineCap.Round;
                eventArgs.Graphics.DrawLine(
                    arrow,
                    centerX - spread,
                    centerY - NativeDrawing.Dpi(this, 2),
                    centerX,
                    centerY + NativeDrawing.Dpi(this, 2));
                eventArgs.Graphics.DrawLine(
                    arrow,
                    centerX,
                    centerY + NativeDrawing.Dpi(this, 2),
                    centerX + spread,
                    centerY - NativeDrawing.Dpi(this, 2));
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && activeMenu != null)
            {
                ContextMenuStrip menu = activeMenu;
                activeMenu = null;
                pendingIndex = -1;
                menu.Close();
                menu.Dispose();
            }
            base.Dispose(disposing);
        }

        private void ShowChoices()
        {
            if (options.Length == 0 || activeMenu != null)
            {
                return;
            }

            activeMenu = new ContextMenuStrip();
            activeMenu.ShowImageMargin = false;
            activeMenu.ShowCheckMargin = true;
            activeMenu.BackColor = NativePalette.ShellRaised;
            activeMenu.ForeColor = NativePalette.Primary;
            activeMenu.Font = Font;
            activeMenu.Padding = new Padding(4);
            activeMenu.Renderer = new ToolStripProfessionalRenderer(
                new NativeMenuColorTable());

            for (int index = 0; index < options.Length; index++)
            {
                int optionIndex = index;
                ToolStripMenuItem item = new ToolStripMenuItem(options[index]);
                item.Tag = optionIndex;
                item.Checked = index == selectedIndex;
                item.CheckOnClick = false;
                item.ForeColor = NativePalette.Primary;
                item.Padding = new Padding(6, 4, 8, 4);
                item.Click += delegate
                {
                    pendingIndex = optionIndex;
                    // Commit on the menu item's real click event. Waiting for
                    // ContextMenuStrip.Closed proved unreliable on the live
                    // taskbar flyout even though the synthetic close-path test
                    // passed, leaving the first selection unchanged.
                    SelectedIndex = optionIndex;
                };
                activeMenu.Items.Add(item);
            }
            activeMenu.Closed += delegate
            {
                ContextMenuStrip closed = activeMenu;
                activeMenu = null;
                int next = pendingIndex;
                pendingIndex = -1;
                if (closed != null && IsHandleCreated && !IsDisposed)
                {
                    try
                    {
                        BeginInvoke((MethodInvoker)delegate
                        {
                            if (!closed.IsDisposed)
                            {
                                closed.Dispose();
                            }
                        });
                    }
                    catch (InvalidOperationException)
                    {
                        // The owning picker is already closing. Its normal
                        // disposal path owns the menu in that case.
                    }
                }
                if (next >= 0)
                {
                    SelectedIndex = next;
                }
                NotifyAccessibleStateChanged();
            };
            activeMenu.Show(this, new Point(0, Height - 1));
            if (selectedIndex >= 0 && selectedIndex < activeMenu.Items.Count)
            {
                activeMenu.Items[selectedIndex].Select();
            }
            NotifyAccessibleStateChanged();
        }

        private void ToggleChoices()
        {
            if (activeMenu == null)
            {
                ShowChoices();
                return;
            }
            activeMenu.Close();
        }

        private void SelectAccessibleValue(string value)
        {
            for (int index = 0; index < options.Length; index++)
            {
                if (string.Equals(options[index], value, StringComparison.CurrentCultureIgnoreCase))
                {
                    SelectedIndex = index;
                    return;
                }
            }
        }

        private void NotifyAccessibleValueChanged()
        {
            if (IsHandleCreated)
            {
                AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
            }
        }

        private void NotifyAccessibleStateChanged()
        {
            if (IsHandleCreated)
            {
                AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
            }
        }

        private sealed class ChoicePickerAccessibleObject : Control.ControlAccessibleObject
        {
            public ChoicePickerAccessibleObject(ChoicePicker owner)
                : base(owner)
            {
            }

            private ChoicePicker Picker
            {
                get { return (ChoicePicker)Owner; }
            }

            public override AccessibleRole Role
            {
                get { return AccessibleRole.ComboBox; }
            }

            public override string Value
            {
                get { return Picker.SelectedValueText; }
                set { Picker.SelectAccessibleValue(value); }
            }

            public override string DefaultAction
            {
                get
                {
                    return Picker.options.Length == 0
                        ? string.Empty
                        : (Picker.IsDropDownOpen ? "Close" : "Open");
                }
            }

            public override AccessibleStates State
            {
                get
                {
                    AccessibleStates state = base.State;
                    state &= ~(AccessibleStates.Expanded | AccessibleStates.Collapsed);
                    state |= Picker.IsDropDownOpen
                        ? AccessibleStates.Expanded
                        : AccessibleStates.Collapsed;
                    return state;
                }
            }

            public override void DoDefaultAction()
            {
                Picker.ToggleChoices();
            }
        }

        internal void ShowChoicesForTest()
        {
            ShowChoices();
        }

        internal void ChooseForTest(int index)
        {
            if (activeMenu == null || index < 0 || index >= options.Length)
            {
                return;
            }
            pendingIndex = index;
            activeMenu.Close();
        }

        internal void ClickChoiceForTest(int index)
        {
            if (activeMenu == null || index < 0 || index >= activeMenu.Items.Count)
            {
                return;
            }
            ToolStripMenuItem item = activeMenu.Items[index] as ToolStripMenuItem;
            if (item == null)
            {
                return;
            }
            item.PerformClick();
            if (activeMenu != null)
            {
                activeMenu.Close();
            }
        }
    }

    internal sealed class NativeMenuColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground
        {
            get { return NativePalette.ShellRaised; }
        }

        public override Color ImageMarginGradientBegin
        {
            get { return NativePalette.ShellRaised; }
        }

        public override Color ImageMarginGradientMiddle
        {
            get { return NativePalette.ShellRaised; }
        }

        public override Color ImageMarginGradientEnd
        {
            get { return NativePalette.ShellRaised; }
        }

        public override Color MenuBorder
        {
            get { return NativePalette.Border; }
        }

        public override Color MenuItemBorder
        {
            get { return NativePalette.BorderStrong; }
        }

        public override Color MenuItemSelected
        {
            get { return NativePalette.CardSelected; }
        }

        public override Color CheckBackground
        {
            get { return NativePalette.CardSelected; }
        }

        public override Color CheckSelectedBackground
        {
            get { return NativePalette.CardSelected; }
        }
    }

    internal sealed class BrandLogo : Control
    {
        public BrandLogo()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            DoubleBuffered = true;
            BackColor = Color.Transparent;
            Size = new Size(44, 44);
            AccessibleName = "UsageApp";
            AccessibleRole = AccessibleRole.Graphic;
        }

        protected override void OnPaint(PaintEventArgs eventArgs)
        {
            eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = NativeDrawing.RoundedRectangle(
                bounds,
                NativeDrawing.Dpi(this, 12)))
            using (LinearGradientBrush fill = new LinearGradientBrush(
                bounds,
                Color.FromArgb(112, 221, 245),
                Color.FromArgb(79, 151, 235),
                45.0f))
            {
                eventArgs.Graphics.FillPath(fill, path);
            }
            using (Font font = new Font(
                "Segoe UI",
                Math.Max(18.0f, Height * 0.50f),
                FontStyle.Bold,
                GraphicsUnit.Pixel))
            {
                TextRenderer.DrawText(
                    eventArgs.Graphics,
                    "U",
                    font,
                    ClientRectangle,
                    Color.FromArgb(4, 20, 31),
                    TextFormatFlags.HorizontalCenter
                        | TextFormatFlags.VerticalCenter
                        | TextFormatFlags.NoPadding);
            }
        }
    }

    internal static class NativeBrandIconRenderer
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr handle);

        public static Icon Create(int size)
        {
            size = Math.Max(24, Math.Min(64, size));
            using (Bitmap bitmap = new Bitmap(
                size,
                size,
                PixelFormat.Format32bppPArgb))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle bounds = new Rectangle(1, 1, size - 3, size - 3);
                using (GraphicsPath path = NativeDrawing.RoundedRectangle(
                    bounds,
                    Math.Max(5, size / 4)))
                using (LinearGradientBrush fill = new LinearGradientBrush(
                    bounds,
                    Color.FromArgb(112, 221, 245),
                    Color.FromArgb(79, 151, 235),
                    45.0f))
                using (Font font = new Font(
                    "Segoe UI",
                    size * 0.48f,
                    FontStyle.Bold,
                    GraphicsUnit.Pixel))
                {
                    graphics.FillPath(fill, path);
                    TextRenderer.DrawText(
                        graphics,
                        "U",
                        font,
                        bounds,
                        Color.FromArgb(4, 20, 31),
                        TextFormatFlags.HorizontalCenter
                            | TextFormatFlags.VerticalCenter
                            | TextFormatFlags.NoPadding);
                }
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
    }

    internal sealed class ProviderMark : Control
    {
        public ProviderMark()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            DoubleBuffered = true;
            BackColor = Color.Transparent;
            ProviderColor = NativePalette.Accent;
            Mark = "C";
            Size = new Size(24, 24);
        }

        public Color ProviderColor { get; set; }
        public string Mark { get; set; }

        protected override void OnPaint(PaintEventArgs eventArgs)
        {
            eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = NativeDrawing.RoundedRectangle(
                bounds,
                NativeDrawing.Dpi(this, 7)))
            using (SolidBrush fill = new SolidBrush(ProviderColor))
            {
                eventArgs.Graphics.FillPath(fill, path);
            }
            using (Font font = new Font("Segoe UI", 10.0f, FontStyle.Bold))
            {
                TextRenderer.DrawText(
                    eventArgs.Graphics,
                    Mark,
                    font,
                    ClientRectangle,
                    Color.FromArgb(4, 20, 31),
                    TextFormatFlags.HorizontalCenter
                        | TextFormatFlags.VerticalCenter
                        | TextFormatFlags.NoPadding);
            }
        }
    }

    internal sealed class RingGauge : Control
    {
        private int? percentage;

        public RingGauge()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            DoubleBuffered = true;
            BackColor = Color.Transparent;
            Size = new Size(116, 116);
            percentage = null;
        }

        public int? Percentage
        {
            get { return percentage; }
            set
            {
                percentage = value.HasValue
                    ? (int?)Math.Max(0, Math.Min(100, value.Value))
                    : null;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs eventArgs)
        {
            eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float scale = Math.Min(Width / 116.0f, Height / 116.0f);
            int inset = Math.Max(1, (int)Math.Round(9 * scale));
            int trailingInset = Math.Max(2, (int)Math.Round(19 * scale));
            Rectangle ring = new Rectangle(
                inset,
                inset,
                Math.Max(1, Width - trailingInset),
                Math.Max(1, Height - trailingInset));
            float stroke = Math.Max(2.0f, 11.0f * scale);
            using (Pen track = new Pen(Color.FromArgb(35, 51, 74), stroke))
            using (Pen value = new Pen(NativePalette.Accent, stroke))
            {
                track.StartCap = LineCap.Round;
                track.EndCap = LineCap.Round;
                value.StartCap = LineCap.Round;
                value.EndCap = LineCap.Round;
                eventArgs.Graphics.DrawArc(track, ring, -90, 360);
                if (percentage.HasValue && percentage.Value > 0)
                {
                    eventArgs.Graphics.DrawArc(
                        value,
                        ring,
                        -90,
                        percentage.Value * 3.6f);
                }
            }

            Rectangle numberBounds = new Rectangle(
                (int)Math.Round(8 * scale),
                (int)Math.Round(24 * scale),
                Math.Max(1, Width - (int)Math.Round(16 * scale)),
                Math.Max(1, (int)Math.Round(38 * scale)));
            float interfaceScale = Font == null
                ? 1.0f
                : Math.Max(0.75f, Font.SizeInPoints / 10.0f);
            FontFamily family = Font == null
                ? SystemFonts.MessageBoxFont.FontFamily
                : Font.FontFamily;
            using (Font number = new Font(
                family,
                20.0f * interfaceScale,
                FontStyle.Bold))
            {
                TextRenderer.DrawText(
                    eventArgs.Graphics,
                    percentage.HasValue ? percentage.Value + "%" : "?",
                    number,
                    numberBounds,
                    NativePalette.Primary,
                    TextFormatFlags.HorizontalCenter
                        | TextFormatFlags.VerticalCenter
                        | TextFormatFlags.NoPadding);
            }
            Rectangle captionBounds = new Rectangle(
                (int)Math.Round(8 * scale),
                (int)Math.Round(66 * scale),
                Math.Max(1, Width - (int)Math.Round(16 * scale)),
                Math.Max(1, (int)Math.Round(21 * scale)));
            using (Font caption = new Font(
                family,
                7.8f * interfaceScale,
                FontStyle.Bold))
            {
                TextRenderer.DrawText(
                    eventArgs.Graphics,
                    percentage.HasValue ? "REMAINING" : "UNAVAILABLE",
                    caption,
                    captionBounds,
                    NativePalette.Secondary,
                    TextFormatFlags.HorizontalCenter
                        | TextFormatFlags.VerticalCenter
                        | TextFormatFlags.NoPadding);
            }
        }
    }

    internal sealed class UsageProgress : Control
    {
        private int percentage;

        public UsageProgress()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            DoubleBuffered = true;
            BackColor = Color.Transparent;
            Height = 8;
        }

        public int Percentage
        {
            get { return percentage; }
            set
            {
                percentage = Math.Max(0, Math.Min(100, value));
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs eventArgs)
        {
            eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = NativeDrawing.RoundedRectangle(bounds, Height / 2))
            using (SolidBrush track = new SolidBrush(Color.FromArgb(42, 57, 80)))
            {
                eventArgs.Graphics.FillPath(track, path);
            }
            int filledWidth = (int)Math.Round((Width - 1) * (percentage / 100.0));
            if (filledWidth <= 0)
            {
                return;
            }
            Rectangle fillBounds = new Rectangle(0, 0, Math.Max(Height, filledWidth), Height - 1);
            using (GraphicsPath fillPath = NativeDrawing.RoundedRectangle(fillBounds, Height / 2))
            using (LinearGradientBrush fill = new LinearGradientBrush(
                fillBounds,
                Color.FromArgb(126, 224, 239),
                NativePalette.AccentPurple,
                0.0f))
            {
                eventArgs.Graphics.FillPath(fill, fillPath);
            }
        }
    }

    internal sealed class PrecisionWheelAccumulator
    {
        private long remainderUnits;
        private int remainderDenominator;

        public int Consume(
            int delta,
            int wheelDeltaPerDetent,
            int pixelsPerDetent)
        {
            int detent = wheelDeltaPerDetent > 0
                ? wheelDeltaPerDetent
                : 120;
            int distance = Math.Max(1, pixelsPerDetent);
            if (remainderDenominator != 0
                && remainderDenominator != detent)
            {
                remainderUnits = 0;
            }
            remainderDenominator = detent;
            long movementUnits = remainderUnits - ((long)delta * distance);
            int wholePixels = (int)(movementUnits / detent);
            remainderUnits = movementUnits % detent;
            return wholePixels;
        }

        public void Reset()
        {
            remainderUnits = 0;
            remainderDenominator = 0;
        }
    }

    internal sealed class DarkScrollBar : Control
    {
        private int maximum;
        private int currentValue;
        private int viewportSize = 1;
        private int contentSize = 1;
        private bool dragging;
        private int dragOffset;

        public DarkScrollBar()
        {
            SetStyle(ControlStyles.Selectable, true);
            DoubleBuffered = true;
            Width = 9;
            Cursor = Cursors.Hand;
            BackColor = NativePalette.Shell;
            TabStop = true;
            AccessibleName = "Content scroll bar";
            AccessibleRole = AccessibleRole.ScrollBar;
        }

        public event EventHandler ValueChanged;

        public int Maximum
        {
            get { return maximum; }
            set
            {
                maximum = Math.Max(0, value);
                Value = currentValue;
                Invalidate();
            }
        }

        public int Value
        {
            get { return currentValue; }
            set
            {
                int next = Math.Max(0, Math.Min(maximum, value));
                if (next == currentValue)
                {
                    return;
                }
                currentValue = next;
                Invalidate();
                if (IsHandleCreated)
                {
                    AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
                }
                EventHandler handler = ValueChanged;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            }
        }

        public void SetMetrics(int viewport, int content)
        {
            viewportSize = Math.Max(1, viewport);
            contentSize = Math.Max(viewportSize, content);
            Maximum = Math.Max(0, contentSize - viewportSize);
            Visible = Maximum > 0;
            Invalidate();
        }

        private Rectangle ThumbBounds()
        {
            int outerPadding = NativeDrawing.Dpi(this, 4);
            int trackHeight = Math.Max(1, Height - outerPadding * 2);
            int thumbHeight = Math.Max(
                NativeDrawing.Dpi(this, 42),
                (int)Math.Round(trackHeight * (viewportSize / (double)contentSize)));
            thumbHeight = Math.Min(trackHeight, thumbHeight);
            int travel = Math.Max(0, trackHeight - thumbHeight);
            int top = outerPadding;
            if (maximum > 0 && travel > 0)
            {
                top += (int)Math.Round(travel * (currentValue / (double)maximum));
            }
            int sidePadding = NativeDrawing.Dpi(this, 2);
            return new Rectangle(
                sidePadding,
                top,
                Math.Max(NativeDrawing.Dpi(this, 4), Width - sidePadding * 2),
                thumbHeight);
        }

        protected override AccessibleObject CreateAccessibilityInstance()
        {
            return new DarkScrollBarAccessibleObject(this);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            Keys keyCode = keyData & Keys.KeyCode;
            if (keyCode == Keys.Up
                || keyCode == Keys.Down
                || keyCode == Keys.PageUp
                || keyCode == Keys.PageDown
                || keyCode == Keys.Home
                || keyCode == Keys.End)
            {
                return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs eventArgs)
        {
            int lineChange = Math.Max(
                1,
                Math.Min(viewportSize, NativeDrawing.Dpi(this, 40)));
            if (eventArgs.KeyCode == Keys.Up)
            {
                Value -= lineChange;
            }
            else if (eventArgs.KeyCode == Keys.Down)
            {
                Value += lineChange;
            }
            else if (eventArgs.KeyCode == Keys.PageUp)
            {
                Value -= viewportSize;
            }
            else if (eventArgs.KeyCode == Keys.PageDown)
            {
                Value += viewportSize;
            }
            else if (eventArgs.KeyCode == Keys.Home)
            {
                Value = 0;
            }
            else if (eventArgs.KeyCode == Keys.End)
            {
                Value = maximum;
            }
            else
            {
                base.OnKeyDown(eventArgs);
                return;
            }
            eventArgs.Handled = true;
            eventArgs.SuppressKeyPress = true;
        }

        protected override void OnPaint(PaintEventArgs eventArgs)
        {
            eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle thumb = ThumbBounds();
            using (GraphicsPath path = NativeDrawing.RoundedRectangle(thumb, thumb.Width / 2))
            using (SolidBrush fill = new SolidBrush(
                SystemInformation.HighContrast
                    ? SystemColors.Highlight
                    : Color.FromArgb(67, 86, 113)))
            {
                eventArgs.Graphics.FillPath(fill, path);
            }
            if (Focused && ShowFocusCues)
            {
                Rectangle focus = Rectangle.Inflate(ClientRectangle, -1, -1);
                ControlPaint.DrawFocusRectangle(
                    eventArgs.Graphics,
                    focus,
                    NativePalette.Primary,
                    BackColor);
            }
        }

        protected override void OnMouseDown(MouseEventArgs eventArgs)
        {
            if (CanFocus)
            {
                Focus();
            }
            base.OnMouseDown(eventArgs);
            Rectangle thumb = ThumbBounds();
            if (thumb.Contains(eventArgs.Location))
            {
                dragging = true;
                dragOffset = eventArgs.Y - thumb.Top;
                Capture = true;
                return;
            }
            Value += eventArgs.Y < thumb.Top ? -viewportSize : viewportSize;
        }

        protected override void OnMouseMove(MouseEventArgs eventArgs)
        {
            base.OnMouseMove(eventArgs);
            if (!dragging || maximum <= 0)
            {
                return;
            }
            Rectangle thumb = ThumbBounds();
            int outerPadding = NativeDrawing.Dpi(this, 4);
            int trackHeight = Math.Max(1, Height - outerPadding * 2);
            int travel = Math.Max(1, trackHeight - thumb.Height);
            int desiredTop = Math.Max(
                outerPadding,
                Math.Min(outerPadding + travel, eventArgs.Y - dragOffset));
            Value = (int)Math.Round(
                maximum * ((desiredTop - outerPadding) / (double)travel));
        }

        protected override void OnMouseUp(MouseEventArgs eventArgs)
        {
            dragging = false;
            Capture = false;
            base.OnMouseUp(eventArgs);
        }

        protected override void OnMouseCaptureChanged(EventArgs eventArgs)
        {
            if (!Capture)
            {
                dragging = false;
                dragOffset = 0;
            }
            base.OnMouseCaptureChanged(eventArgs);
        }

        protected override void OnGotFocus(EventArgs eventArgs)
        {
            base.OnGotFocus(eventArgs);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs eventArgs)
        {
            dragging = false;
            base.OnLostFocus(eventArgs);
            Invalidate();
        }

        private sealed class DarkScrollBarAccessibleObject : Control.ControlAccessibleObject
        {
            public DarkScrollBarAccessibleObject(DarkScrollBar owner)
                : base(owner)
            {
            }

            private DarkScrollBar ScrollBar
            {
                get { return (DarkScrollBar)Owner; }
            }

            public override AccessibleRole Role
            {
                get { return AccessibleRole.ScrollBar; }
            }

            public override string Value
            {
                get { return ScrollBar.Value.ToString(); }
                set
                {
                    int next;
                    if (int.TryParse(value, out next))
                    {
                        ScrollBar.Value = next;
                    }
                }
            }

            public override string Description
            {
                get
                {
                    string description = base.Description;
                    return string.IsNullOrEmpty(description)
                        ? "Vertical scroll position from 0 to " + ScrollBar.Maximum + "."
                        : description;
                }
            }

            public override AccessibleStates State
            {
                get { return base.State | AccessibleStates.Focusable; }
            }
        }
    }
}
