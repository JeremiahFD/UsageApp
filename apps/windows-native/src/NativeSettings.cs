using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace UsageApp.Native
{
    internal sealed class NativeSettings
    {
        internal const string DefaultCodexQuotaNotificationThresholdsCsv =
            "25,10,5";
        private static readonly int[] AllowedRefreshMinutes =
            new int[] { 1, 2, 5, 10, 15, 30, 60 };
        private static readonly int[] AllowedTextScales =
            new int[] { 100, 110, 125 };
        private static readonly string[] AllowedTrayFonts = InstalledFontOptions(
            new string[]
            {
                "Consolas",
                "Segoe UI",
                "Segoe UI Black",
                "Segoe UI Semibold",
                "Bahnschrift",
                "Bahnschrift Condensed",
                "Bahnschrift SemiCondensed",
                "Calibri",
                "Arial",
                "Arial Black",
                "Arial Narrow",
                "Verdana",
                "Tahoma",
                "Trebuchet MS",
                "Cascadia Code",
                "Cascadia Mono SemiBold",
                "Cascadia Mono",
                "Franklin Gothic Medium",
                "Century Gothic",
                "OCR A Extended",
                "Agency FB",
                "Lucida Console",
                "DejaVu Sans Mono",
                "Rockwell Extra Bold",
                "Copperplate Gothic Bold",
                "Bauhaus 93",
                "Broadway",
                "Haettenschweiler",
                "Showcard Gothic",
                "Stencil",
                "Impact"
            },
            "Consolas");
        private static readonly string[] AllowedInterfaceFonts = InstalledFontOptions(
            new string[]
            {
                "Segoe UI",
                "Calibri",
                "Arial",
                "Verdana",
                "Tahoma",
                "Trebuchet MS",
                "Bahnschrift",
                "Century Gothic",
                "Franklin Gothic Medium",
                "Lucida Sans Unicode",
                "Georgia",
                "Times New Roman",
                "Consolas",
                "Cascadia Code",
                "Cascadia Mono",
                "OCR A Extended",
                "Agency FB",
                "Lucida Console",
                "DejaVu Sans Mono",
                "Rockwell",
                "Copperplate Gothic Light",
                "Bauhaus 93",
                "Ink Free",
                "Comic Sans MS",
                "Stencil"
            },
            "Segoe UI");
        private static readonly string[] CodexQuotaNotificationPresets =
            new string[] { "25,10,5", "10,5", "50,25,10,5" };
        private static readonly string[] AllowedTrayColorPresets =
            new string[] { "Automatic", "Original", "Bright", "Dark" };
        private static readonly string[] AllowedTrayEdgeModes =
            new string[] { "Automatic", "None", "Dark", "Light" };
        private static readonly string[] AllowedTrayWindowModes =
            new string[] { "MostConstrained", "ShortestWindow", "LongestWindow" };

        public NativeSettings()
        {
            ShowCodexProvider = true;
            ShowClaudeProvider = true;
            ShowCodexTrayIcon = true;
            ShowClaudeTrayIcon = false;
            RefreshIntervalMinutes = 5;
            FlyoutTextScale = 100;
            InterfaceFontName = "Segoe UI";
            TrayFontName = "Consolas";
            TrayCodexColor = "#30718D";
            TrayColorPreset = "Automatic";
            TrayEdgeMode = "Automatic";
            TrayWindowMode = "MostConstrained";
            CodexQuotaNotificationsEnabled = false;
            CodexQuotaNotificationThresholdsCsv =
                DefaultCodexQuotaNotificationThresholdsCsv;
            StartWithWindows = false;
        }

        public bool ShowCodexProvider { get; set; }
        public bool ShowClaudeProvider { get; set; }
        public bool ShowCodexTrayIcon { get; set; }
        public bool ShowClaudeTrayIcon { get; set; }
        public int RefreshIntervalMinutes { get; set; }
        public int FlyoutTextScale { get; set; }
        public string InterfaceFontName { get; set; }
        public string TrayFontName { get; set; }
        public string TrayCodexColor { get; set; }
        public string TrayColorPreset { get; set; }
        public string TrayEdgeMode { get; set; }
        public string TrayWindowMode { get; set; }
        public bool CodexQuotaNotificationsEnabled { get; set; }
        public string CodexQuotaNotificationThresholdsCsv { get; set; }
        public bool StartWithWindows { get; set; }

        public void Normalize()
        {
            // A provider-less app and an icon-less notification-area app have
            // no usable surface. Keep the choices valid even if an older or
            // manually edited settings file contains an impossible state.
            if (!ShowCodexProvider && !ShowClaudeProvider)
            {
                ShowCodexProvider = true;
            }
            if (!ShowCodexProvider)
            {
                ShowCodexTrayIcon = false;
            }
            if (!ShowClaudeProvider)
            {
                ShowClaudeTrayIcon = false;
            }
            if (!ShowCodexTrayIcon && !ShowClaudeTrayIcon)
            {
                if (ShowCodexProvider)
                {
                    ShowCodexTrayIcon = true;
                }
                else
                {
                    ShowClaudeTrayIcon = true;
                }
            }
            RefreshIntervalMinutes = Contains(
                AllowedRefreshMinutes,
                RefreshIntervalMinutes)
                ? RefreshIntervalMinutes
                : 5;
            FlyoutTextScale = Contains(AllowedTextScales, FlyoutTextScale)
                ? FlyoutTextScale
                : 100;
            InterfaceFontName = Contains(
                AllowedInterfaceFonts,
                InterfaceFontName)
                ? InterfaceFontName
                : "Segoe UI";
            TrayFontName = Contains(AllowedTrayFonts, TrayFontName)
                ? TrayFontName
                : "Consolas";
            TrayColorPreset = Contains(AllowedTrayColorPresets, TrayColorPreset)
                ? TrayColorPreset
                : "Automatic";
            TrayEdgeMode = Contains(AllowedTrayEdgeModes, TrayEdgeMode)
                ? TrayEdgeMode
                : "Automatic";
            TrayWindowMode = Contains(AllowedTrayWindowModes, TrayWindowMode)
                ? TrayWindowMode
                : "MostConstrained";
            Color ignored;
            if (!TryParseColor(TrayCodexColor, out ignored))
            {
                TrayCodexColor = "#30718D";
            }
            else
            {
                TrayCodexColor = TrayCodexColor.ToUpperInvariant();
            }
            CodexQuotaNotificationThresholdsCsv =
                NormalizeCodexQuotaNotificationThresholdsCsv(
                    CodexQuotaNotificationThresholdsCsv);
        }

        [ScriptIgnore]
        public Color CodexTrayColor
        {
            get
            {
                if (string.Equals(TrayColorPreset, "Bright", StringComparison.Ordinal))
                {
                    return Color.FromArgb(56, 189, 248);
                }
                if (string.Equals(TrayColorPreset, "Dark", StringComparison.Ordinal))
                {
                    return Color.FromArgb(31, 111, 139);
                }
                if (string.Equals(TrayColorPreset, "Automatic", StringComparison.Ordinal))
                {
                    return NativeTaskbarTheme.UsesLightTaskbar
                        ? Color.FromArgb(0, 88, 122)
                        : Color.FromArgb(92, 207, 255);
                }
                Color color;
                return TryParseColor(TrayCodexColor, out color)
                    ? color
                    : Color.FromArgb(48, 113, 141);
            }
        }

        [ScriptIgnore]
        public Color ClaudeTrayColor
        {
            get
            {
                if (string.Equals(TrayColorPreset, "Bright", StringComparison.Ordinal))
                {
                    return Color.FromArgb(251, 146, 60);
                }
                if (string.Equals(TrayColorPreset, "Dark", StringComparison.Ordinal))
                {
                    return Color.FromArgb(169, 77, 18);
                }
                if (string.Equals(TrayColorPreset, "Automatic", StringComparison.Ordinal))
                {
                    return NativeTaskbarTheme.UsesLightTaskbar
                        ? Color.FromArgb(160, 68, 8)
                        : Color.FromArgb(255, 176, 120);
                }
                return Color.FromArgb(231, 157, 109);
            }
        }

        [ScriptIgnore]
        public int[] CodexQuotaNotificationThresholds
        {
            get
            {
                return ParseCodexQuotaNotificationThresholds(
                    CodexQuotaNotificationThresholdsCsv);
            }
        }

        public static int[] RefreshOptions
        {
            get { return (int[])AllowedRefreshMinutes.Clone(); }
        }

        public static int[] TextScaleOptions
        {
            get { return (int[])AllowedTextScales.Clone(); }
        }

        public static string[] TrayFontOptions
        {
            get { return (string[])AllowedTrayFonts.Clone(); }
        }

        public static string[] InterfaceFontOptions
        {
            get { return (string[])AllowedInterfaceFonts.Clone(); }
        }

        public static string[] CodexQuotaNotificationPresetOptions
        {
            get { return (string[])CodexQuotaNotificationPresets.Clone(); }
        }

        public static string[] TrayColorPresetOptions
        {
            get { return (string[])AllowedTrayColorPresets.Clone(); }
        }

        public static string[] TrayEdgeModeOptions
        {
            get { return (string[])AllowedTrayEdgeModes.Clone(); }
        }

        public static string[] TrayWindowModeOptions
        {
            get { return (string[])AllowedTrayWindowModes.Clone(); }
        }

        internal static string NormalizeCodexQuotaNotificationThresholdsCsv(
            string csv)
        {
            int[] thresholds = ParseCodexQuotaNotificationThresholds(csv);
            if (thresholds.Length == 0)
            {
                return DefaultCodexQuotaNotificationThresholdsCsv;
            }
            StringBuilder normalized = new StringBuilder();
            foreach (int threshold in thresholds)
            {
                if (normalized.Length > 0)
                {
                    normalized.Append(',');
                }
                normalized.Append(
                    threshold.ToString(CultureInfo.InvariantCulture));
            }
            return normalized.ToString();
        }

        internal static bool TryNormalizeCodexQuotaNotificationThresholdsCsv(
            string csv,
            out string normalized,
            out string error)
        {
            normalized = null;
            error = null;
            if (string.IsNullOrWhiteSpace(csv))
            {
                error = "Enter 1 to 5 comma-separated percentages.";
                return false;
            }

            string[] parts = csv.Split(',');
            if (parts.Length < 1 || parts.Length > 5)
            {
                error = "Enter no more than 5 warning percentages.";
                return false;
            }

            List<int> thresholds = new List<int>();
            foreach (string part in parts)
            {
                int threshold;
                if (!int.TryParse(
                        part.Trim(),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out threshold)
                    || threshold < 1
                    || threshold > 99)
                {
                    error = "Each warning must be a whole percentage from 1 to 99.";
                    return false;
                }
                if (thresholds.Contains(threshold))
                {
                    error = "Use each warning percentage only once.";
                    return false;
                }
                thresholds.Add(threshold);
            }

            thresholds.Sort(
                delegate(int left, int right)
                {
                    return right.CompareTo(left);
                });
            StringBuilder result = new StringBuilder();
            foreach (int threshold in thresholds)
            {
                if (result.Length > 0)
                {
                    result.Append(',');
                }
                result.Append(threshold.ToString(CultureInfo.InvariantCulture));
            }
            normalized = result.ToString();
            return true;
        }

        internal static int[] ParseCodexQuotaNotificationThresholds(string csv)
        {
            List<int> thresholds = new List<int>();
            if (!string.IsNullOrEmpty(csv))
            {
                string[] parts = csv.Split(',');
                foreach (string part in parts)
                {
                    int threshold;
                    if (!int.TryParse(
                            part.Trim(),
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out threshold)
                        || threshold < 1
                        || threshold > 99
                        || thresholds.Contains(threshold))
                    {
                        continue;
                    }
                    thresholds.Add(threshold);
                    if (thresholds.Count == 5)
                    {
                        break;
                    }
                }
            }
            thresholds.Sort(
                delegate(int left, int right)
                {
                    return right.CompareTo(left);
                });
            return thresholds.ToArray();
        }

        internal static bool TryParseColor(string value, out Color color)
        {
            color = Color.Empty;
            if (string.IsNullOrEmpty(value)
                || value.Length != 7
                || value[0] != '#')
            {
                return false;
            }
            int red;
            int green;
            int blue;
            if (!int.TryParse(
                    value.Substring(1, 2),
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out red)
                || !int.TryParse(
                    value.Substring(3, 2),
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out green)
                || !int.TryParse(
                    value.Substring(5, 2),
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out blue))
            {
                return false;
            }
            color = Color.FromArgb(red, green, blue);
            return true;
        }

        private static bool Contains(int[] values, int target)
        {
            foreach (int value in values)
            {
                if (value == target)
                {
                    return true;
                }
            }
            return false;
        }

        private static string[] InstalledFontOptions(
            string[] preferred,
            string fallback)
        {
            List<string> installed = new List<string>();
            try
            {
                using (InstalledFontCollection collection =
                    new InstalledFontCollection())
                {
                    HashSet<string> names = new HashSet<string>(
                        StringComparer.OrdinalIgnoreCase);
                    foreach (FontFamily family in collection.Families)
                    {
                        names.Add(family.Name);
                    }
                    foreach (string candidate in preferred)
                    {
                        if (names.Contains(candidate))
                        {
                            installed.Add(candidate);
                        }
                    }
                }
            }
            catch
            {
                // Font enumeration should never prevent settings from loading.
            }
            if (installed.Count == 0)
            {
                installed.Add(fallback);
            }
            return installed.ToArray();
        }

        private static bool Contains(string[] values, string target)
        {
            foreach (string value in values)
            {
                if (string.Equals(value, target, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
    }

    internal static class NativeTaskbarTheme
    {
        private const string PersonalizeKey =
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

        internal static bool UsesLightTaskbar
        {
            get
            {
                if (System.Windows.Forms.SystemInformation.HighContrast)
                {
                    return SystemColors.Window.GetBrightness() > 0.5f;
                }
                try
                {
                    using (Microsoft.Win32.RegistryKey key =
                        Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                            PersonalizeKey,
                            false))
                    {
                        object raw = key == null
                            ? null
                            : key.GetValue("SystemUsesLightTheme", null);
                        return raw != null
                            && Convert.ToInt32(raw, CultureInfo.InvariantCulture) != 0;
                    }
                }
                catch
                {
                    return false;
                }
            }
        }
    }

    internal sealed class NativeSettingsStore
    {
        private readonly string settingsPath;

        public NativeSettingsStore()
            : this(DefaultSettingsPath())
        {
        }

        internal NativeSettingsStore(string injectedSettingsPath)
        {
            if (string.IsNullOrEmpty(injectedSettingsPath))
            {
                throw new ArgumentException(
                    "A settings file path is required.",
                    "injectedSettingsPath");
            }
            settingsPath = injectedSettingsPath;
        }

        private static string DefaultSettingsPath()
        {
            string root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "UsageAppNative");
            return Path.Combine(root, "settings.json");
        }

        public NativeSettings Load()
        {
            try
            {
                if (!File.Exists(settingsPath))
                {
                    return new NativeSettings();
                }
                NativeSettings settings = Deserialize(
                    File.ReadAllText(settingsPath, Encoding.UTF8));
                if (settings == null)
                {
                    settings = new NativeSettings();
                }
                settings.Normalize();
                return settings;
            }
            catch
            {
                return new NativeSettings();
            }
        }

        public bool Save(NativeSettings settings)
        {
            if (settings == null)
            {
                return false;
            }
            settings.Normalize();
            try
            {
                string directory = Path.GetDirectoryName(settingsPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                string temporary = settingsPath + ".tmp";
                File.WriteAllText(
                    temporary,
                    Serialize(settings),
                    Encoding.UTF8);
                if (File.Exists(settingsPath))
                {
                    try
                    {
                        File.Replace(temporary, settingsPath, null);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        File.Copy(temporary, settingsPath, true);
                        File.Delete(temporary);
                    }
                    catch (IOException)
                    {
                        File.Copy(temporary, settingsPath, true);
                        File.Delete(temporary);
                    }
                }
                else
                {
                    File.Move(temporary, settingsPath);
                }
                return true;
            }
            catch
            {
                // Settings are optional; monitoring must continue if storage fails.
                return false;
            }
        }

        internal static string Serialize(NativeSettings settings)
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            return serializer.Serialize(settings);
        }

        internal static NativeSettings Deserialize(string text)
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            NativeSettings settings = serializer.Deserialize<NativeSettings>(text);
            if (settings != null)
            {
                settings.Normalize();
            }
            return settings;
        }
    }
}
