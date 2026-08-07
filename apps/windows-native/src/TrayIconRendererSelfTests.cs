using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.Windows.Forms;

namespace UsageApp.Native
{
    internal static class TrayIconRendererSelfTests
    {
        private static readonly int[] ShellSizes =
            new int[] { 16, 20, 24, 32, 40, 48, 64 };

        private static readonly int?[] RepresentativeValues =
            new int?[] { null, 1, 48, 100 };

        internal static void TestDpiValueAndEdgeMatrix()
        {
            string[] edgeModes = NativeSettings.TrayEdgeModeOptions;
            Assert(edgeModes.Length == 4, "configured tray edge modes changed");

            foreach (string edgeMode in edgeModes)
            {
                NativeSettings edgeSettings = new NativeSettings();
                edgeSettings.TrayEdgeMode = edgeMode;
                edgeSettings.Normalize();
                Assert(
                    string.Equals(
                        edgeSettings.TrayEdgeMode,
                        edgeMode,
                        StringComparison.Ordinal),
                    "configured tray edge mode did not survive normalization: "
                        + edgeMode);
                foreach (int size in ShellSizes)
                {
                    HashSet<ulong> valueFingerprints = new HashSet<ulong>();
                    foreach (int? value in RepresentativeValues)
                    {
                        using (Bitmap bitmap = TrayIconRenderer.CreateBitmap(
                            value,
                            size,
                            "Consolas",
                            Color.FromArgb(92, 207, 255),
                            edgeMode))
                        {
                            Assert(
                                bitmap.Width == size && bitmap.Height == size,
                                "tray bitmap did not preserve " + size + "px output");
                            PixelSummary summary = Analyze(bitmap);
                            Assert(
                                summary.VisiblePixels > 0,
                                "tray bitmap was transparent for "
                                    + ValueLabel(value)
                                    + " at "
                                    + size
                                    + "px with "
                                    + edgeMode
                                    + " edge");
                            Assert(
                                summary.Left >= 0
                                    && summary.Top >= 0
                                    && summary.Right < size
                                    && summary.Bottom < size,
                                "tray glyph escaped its bitmap bounds");
                            Assert(
                                summary.Width >= Math.Max(3, size / 4)
                                    && summary.Height >= Math.Max(3, size / 3),
                                "tray glyph collapsed at "
                                    + size
                                    + "px for "
                                    + ValueLabel(value));
                            Assert(
                                valueFingerprints.Add(summary.Fingerprint),
                                "distinct tray values rendered identically at "
                                    + size
                                    + "px with "
                                    + edgeMode
                                    + " edge");
                        }
                    }

                    using (Icon icon = TrayIconRenderer.Create(
                        48,
                        "Consolas",
                        Color.FromArgb(92, 207, 255),
                        edgeMode,
                        size))
                    {
                        Assert(icon != null, "tray HICON was not created");
                        Assert(
                            icon.Width == size && icon.Height == size,
                            "tray HICON did not preserve " + size + "px output");
                        using (Bitmap roundTrip = icon.ToBitmap())
                        {
                            Assert(
                                Analyze(roundTrip).VisiblePixels > 0,
                                "tray HICON round trip lost its glyph at "
                                    + size
                                    + "px");
                        }
                    }
                }
            }

            using (Bitmap tooSmall = TrayIconRenderer.CreateBitmap(
                48,
                1,
                "Consolas",
                Color.White,
                "None"))
            using (Bitmap tooLarge = TrayIconRenderer.CreateBitmap(
                48,
                96,
                "Consolas",
                Color.White,
                "None"))
            {
                Assert(
                    tooSmall.Size == new Size(16, 16),
                    "tray renderer accepted output below the 16px shell floor");
                Assert(
                    tooLarge.Size == new Size(64, 64),
                    "tray renderer did not cap output at the 64px shell ceiling");
            }

            if (!SystemInformation.HighContrast)
            {
                PixelSummary none = RenderSummary("None");
                PixelSummary automatic = RenderSummary("Automatic");
                PixelSummary dark = RenderSummary("Dark");
                PixelSummary light = RenderSummary("Light");
                Assert(
                    automatic.VisiblePixels > none.VisiblePixels,
                    "automatic tray edge added no visible coverage");
                Assert(
                    dark.VisiblePixels > none.VisiblePixels,
                    "dark tray edge added no visible coverage");
                Assert(
                    light.VisiblePixels > none.VisiblePixels,
                    "light tray edge added no visible coverage");
                Assert(
                    dark.Fingerprint != light.Fingerprint,
                    "dark and light tray edges rendered identically");
            }
            else
            {
                PixelSummary none = RenderSummary("None");
                PixelSummary automatic = RenderSummary("Automatic");
                PixelSummary dark = RenderSummary("Dark");
                PixelSummary light = RenderSummary("Light");
                Assert(
                    none.Fingerprint == automatic.Fingerprint
                        && automatic.Fingerprint == dark.Fingerprint
                        && dark.Fingerprint == light.Fingerprint,
                    "contrast theme did not override decorative edge modes");
            }
        }

        internal static void TestConfiguredColorModes()
        {
            string[] presets = NativeSettings.TrayColorPresetOptions;
            Assert(presets.Length == 4, "configured tray color presets changed");

            NativeSettings invalid = new NativeSettings();
            invalid.TrayColorPreset = "not-a-color-preset";
            invalid.TrayEdgeMode = "not-an-edge-mode";
            invalid.Normalize();
            Assert(
                string.Equals(
                    invalid.TrayColorPreset,
                    "Automatic",
                    StringComparison.Ordinal)
                    && string.Equals(
                        invalid.TrayEdgeMode,
                        "Automatic",
                        StringComparison.Ordinal),
                "invalid tray appearance modes did not normalize safely");

            HashSet<int> codexColors = new HashSet<int>();
            HashSet<int> claudeColors = new HashSet<int>();
            foreach (string preset in presets)
            {
                NativeSettings settings = new NativeSettings();
                settings.TrayColorPreset = preset;
                settings.Normalize();
                Assert(
                    string.Equals(
                        settings.TrayColorPreset,
                        preset,
                        StringComparison.Ordinal),
                    "configured tray color preset did not survive normalization: "
                        + preset);
                Color codex = settings.CodexTrayColor;
                Color claude = settings.ClaudeTrayColor;
                Assert(!codex.IsEmpty, preset + " Codex tray color was empty");
                Assert(!claude.IsEmpty, preset + " Claude tray color was empty");
                if (string.Equals(
                    preset,
                    "Automatic",
                    StringComparison.Ordinal))
                {
                    bool lightTaskbar = NativeTaskbarTheme.UsesLightTaskbar;
                    Assert(
                        codex.ToArgb() == (lightTaskbar
                            ? Color.FromArgb(0, 88, 122).ToArgb()
                            : Color.FromArgb(92, 207, 255).ToArgb())
                            && claude.ToArgb() == (lightTaskbar
                                ? Color.FromArgb(160, 68, 8).ToArgb()
                                : Color.FromArgb(255, 176, 120).ToArgb()),
                        "automatic tray colors did not follow the active taskbar theme");
                }
                codexColors.Add(codex.ToArgb());
                claudeColors.Add(claude.ToArgb());

                foreach (int size in new int[] { 16, 64 })
                {
                    using (Bitmap codexBitmap = TrayIconRenderer.CreateBitmap(
                        size == 16 ? 1 : 100,
                        size,
                        "Consolas",
                        codex,
                        settings.TrayEdgeMode))
                    using (Bitmap claudeBitmap = TrayIconRenderer.CreateBitmap(
                        size == 16 ? 1 : 100,
                        size,
                        "Consolas",
                        claude,
                        settings.TrayEdgeMode))
                    {
                        PixelSummary codexSummary = Analyze(codexBitmap);
                        PixelSummary claudeSummary = Analyze(claudeBitmap);
                        Assert(
                            codexSummary.VisiblePixels > 0,
                            preset + " Codex color produced an empty glyph");
                        Assert(
                            claudeSummary.VisiblePixels > 0,
                            preset + " Claude color produced an empty glyph");
                        if (!SystemInformation.HighContrast)
                        {
                            Assert(
                                codexSummary.Fingerprint
                                    != claudeSummary.Fingerprint,
                                preset
                                    + " provider colors rendered identically");
                        }
                        else
                        {
                            Assert(
                                codexSummary.Fingerprint
                                    == claudeSummary.Fingerprint,
                                "contrast theme did not replace provider colors");
                        }
                    }
                }
            }

            Assert(
                codexColors.Count == presets.Length,
                "configured Codex tray color presets were not distinct");
            Assert(
                claudeColors.Count == presets.Length,
                "configured Claude tray color presets were not distinct");

            // Exercise both automatic light/dark palette outputs without
            // changing the user's Windows theme or registry. Selection of the
            // active branch remains an operating-system integration check.
            Color[] automaticPalette = new Color[]
            {
                Color.FromArgb(0, 88, 122),
                Color.FromArgb(92, 207, 255),
                Color.FromArgb(160, 68, 8),
                Color.FromArgb(255, 176, 120)
            };
            foreach (Color color in automaticPalette)
            {
                using (Bitmap bitmap = TrayIconRenderer.CreateBitmap(
                    48,
                    24,
                    "Consolas",
                    color,
                    "Automatic"))
                {
                    Assert(
                        Analyze(bitmap).VisiblePixels > 0,
                        "an automatic light/dark palette color failed to render");
                }
            }
        }

        internal static void TestInstalledFontFallback()
        {
            string[] fontOptions = NativeSettings.TrayFontOptions;
            Assert(fontOptions.Length > 0, "tray font picker had no usable fonts");

            HashSet<string> installed = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            using (InstalledFontCollection collection =
                new InstalledFontCollection())
            {
                foreach (FontFamily family in collection.Families)
                {
                    installed.Add(family.Name);
                }
            }

            foreach (string fontName in fontOptions)
            {
                Assert(
                    installed.Contains(fontName),
                    "tray font option was not installed: " + fontName);
                using (Bitmap one = TrayIconRenderer.CreateBitmap(
                    1,
                    16,
                    fontName,
                    Color.White,
                    "Dark"))
                using (Bitmap hundred = TrayIconRenderer.CreateBitmap(
                    100,
                    64,
                    fontName,
                    Color.White,
                    "Dark"))
                {
                    Assert(
                        Analyze(one).VisiblePixels > 0,
                        fontName + " failed to render a one-digit tray glyph");
                    Assert(
                        Analyze(hundred).VisiblePixels > 0,
                        fontName + " failed to render a three-digit tray glyph");
                }
            }

            NativeSettings settings = new NativeSettings();
            settings.TrayFontName = "__UsageApp font is not installed__";
            settings.Normalize();
            Assert(
                string.Equals(
                    settings.TrayFontName,
                    "Consolas",
                    StringComparison.Ordinal),
                "missing configured tray font did not normalize to Consolas");

            PixelSummary fallback;
            PixelSummary expected;
            using (Bitmap missing = TrayIconRenderer.CreateBitmap(
                100,
                24,
                "__UsageApp font is not installed__",
                Color.White,
                "Dark"))
            using (Bitmap consolas = TrayIconRenderer.CreateBitmap(
                100,
                24,
                "Consolas",
                Color.White,
                "Dark"))
            {
                fallback = Analyze(missing);
                expected = Analyze(consolas);
            }
            Assert(
                fallback.Fingerprint == expected.Fingerprint,
                "renderer missing-font fallback did not match Consolas");
        }

        private static PixelSummary RenderSummary(string edgeMode)
        {
            using (Bitmap bitmap = TrayIconRenderer.CreateBitmap(
                48,
                24,
                "Consolas",
                Color.FromArgb(92, 207, 255),
                edgeMode))
            {
                return Analyze(bitmap);
            }
        }

        private static PixelSummary Analyze(Bitmap bitmap)
        {
            PixelSummary summary = new PixelSummary();
            summary.Left = bitmap.Width;
            summary.Top = bitmap.Height;
            summary.Right = -1;
            summary.Bottom = -1;
            ulong fingerprint = 1469598103934665603UL;
            unchecked
            {
                for (int y = 0; y < bitmap.Height; y++)
                {
                    for (int x = 0; x < bitmap.Width; x++)
                    {
                        Color pixel = bitmap.GetPixel(x, y);
                        fingerprint ^= (uint)pixel.ToArgb();
                        fingerprint *= 1099511628211UL;
                        if (pixel.A < 12)
                        {
                            continue;
                        }
                        summary.VisiblePixels++;
                        summary.Left = Math.Min(summary.Left, x);
                        summary.Top = Math.Min(summary.Top, y);
                        summary.Right = Math.Max(summary.Right, x);
                        summary.Bottom = Math.Max(summary.Bottom, y);
                    }
                }
            }
            summary.Fingerprint = fingerprint;
            return summary;
        }

        private static string ValueLabel(int? value)
        {
            return value.HasValue ? value.Value.ToString() : "?";
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private struct PixelSummary
        {
            public int VisiblePixels;
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
            public ulong Fingerprint;

            public int Width
            {
                get { return Right >= Left ? Right - Left + 1 : 0; }
            }

            public int Height
            {
                get { return Bottom >= Top ? Bottom - Top + 1 : 0; }
            }
        }
    }
}
