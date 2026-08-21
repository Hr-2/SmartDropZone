using System.Windows;
using System.Windows.Media;

namespace SmartDropZone
{
    public enum AppTheme { Slate, Ocean, Forest, Ember, Violet, Light }

    /// <summary>Swaps the application's color resources so every DynamicResource picks up the new theme.</summary>
    public static class ThemeManager
    {
        private sealed class Theme
        {
            public Color SurfaceTop, SurfaceBottom, Accent, PrimaryText, SecondaryText, SubtleText,
                        Card, CardHover, Separator, Selection, Hover, Input, InputBorder, Badge, Handle;
        }

        public static void Apply(AppTheme theme)
        {
            var res = Application.Current.Resources;
            var t = Themes[(int)theme];

            res["ShelfBrush"] = Shelf(t);
            res["CardBrush"] = Brush(t.Card);
            res["CardHoverBrush"] = Brush(t.CardHover);
            res["CardSeparatorBrush"] = Brush(t.Separator);
            res["PrimaryTextBrush"] = Brush(t.PrimaryText);
            res["SecondaryTextBrush"] = Brush(t.SecondaryText);
            res["SubtleTextBrush"] = Brush(t.SubtleText);
            res["AccentBrush"] = Brush(t.Accent);
            res["SelectionBrush"] = Brush(t.Selection);
            res["HoverBrush"] = Brush(t.Hover);
            res["InputBrush"] = Brush(t.Input);
            res["InputBorderBrush"] = Brush(t.InputBorder);
            res["BadgeBrush"] = Brush(t.Badge);
            res["HandleBrush"] = Brush(t.Handle);
        }

        private static Brush Shelf(Theme t)
        {
            var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            g.GradientStops.Add(new GradientStop(t.SurfaceTop, 0.0));
            g.GradientStops.Add(new GradientStop(t.SurfaceBottom, 1.0));
            return g;
        }

        private static SolidColorBrush Brush(Color c) => new SolidColorBrush(c);

        private static readonly Theme[] Themes = { Slate(), Ocean(), Forest(), Ember(), Violet(), Light() };

        private static Theme Slate() => new Theme
        {
            SurfaceTop = Rgb(0x2B, 0x2B, 0x33), SurfaceBottom = Rgb(0x21, 0x21, 0x27),
            Accent = Rgb(0x4C, 0xC2, 0xFF),
            PrimaryText = Rgb(0xE9, 0xE9, 0xEF), SecondaryText = Rgb(0x9A, 0x9A, 0xA5), SubtleText = Rgb(0x66, 0x66, 0x70),
            Card = Argb(0x1A, 0xFF, 0xFF, 0xFF), CardHover = Argb(0x2E, 0xFF, 0xFF, 0xFF), Separator = Argb(0x26, 0xFF, 0xFF, 0xFF),
            Selection = Argb(0x3D, 0x4C, 0xC2, 0xFF), Hover = Argb(0x33, 0xFF, 0xFF, 0xFF),
            Input = Argb(0x0F, 0xFF, 0xFF, 0xFF), InputBorder = Argb(0x1F, 0xFF, 0xFF, 0xFF),
            Badge = Argb(0x33, 0xFF, 0xFF, 0xFF), Handle = Argb(0x4D, 0xFF, 0xFF, 0xFF),
        };

        private static Theme Ocean() => new Theme
        {
            SurfaceTop = Rgb(0x1C, 0x2B, 0x3A), SurfaceBottom = Rgb(0x14, 0x1F, 0x2B),
            Accent = Rgb(0x38, 0xBD, 0xF8),
            PrimaryText = Rgb(0xE8, 0xF0, 0xF8), SecondaryText = Rgb(0x94, 0xA3, 0xB8), SubtleText = Rgb(0x62, 0x70, 0x84),
            Card = Argb(0x1A, 0xFF, 0xFF, 0xFF), CardHover = Argb(0x2E, 0xFF, 0xFF, 0xFF), Separator = Argb(0x26, 0xFF, 0xFF, 0xFF),
            Selection = Argb(0x3D, 0x38, 0xBD, 0xF8), Hover = Argb(0x33, 0xFF, 0xFF, 0xFF),
            Input = Argb(0x0F, 0xFF, 0xFF, 0xFF), InputBorder = Argb(0x1F, 0xFF, 0xFF, 0xFF),
            Badge = Argb(0x33, 0xFF, 0xFF, 0xFF), Handle = Argb(0x4D, 0xFF, 0xFF, 0xFF),
        };

        private static Theme Forest() => new Theme
        {
            SurfaceTop = Rgb(0x1E, 0x2B, 0x22), SurfaceBottom = Rgb(0x15, 0x1E, 0x18),
            Accent = Rgb(0x4A, 0xDE, 0x80),
            PrimaryText = Rgb(0xE9, 0xF2, 0xEC), SecondaryText = Rgb(0x9C, 0xAD, 0xA4), SubtleText = Rgb(0x68, 0x7A, 0x70),
            Card = Argb(0x1A, 0xFF, 0xFF, 0xFF), CardHover = Argb(0x2E, 0xFF, 0xFF, 0xFF), Separator = Argb(0x26, 0xFF, 0xFF, 0xFF),
            Selection = Argb(0x3D, 0x4A, 0xDE, 0x80), Hover = Argb(0x33, 0xFF, 0xFF, 0xFF),
            Input = Argb(0x0F, 0xFF, 0xFF, 0xFF), InputBorder = Argb(0x1F, 0xFF, 0xFF, 0xFF),
            Badge = Argb(0x33, 0xFF, 0xFF, 0xFF), Handle = Argb(0x4D, 0xFF, 0xFF, 0xFF),
        };

        private static Theme Ember() => new Theme
        {
            SurfaceTop = Rgb(0x2E, 0x22, 0x1D), SurfaceBottom = Rgb(0x22, 0x18, 0x14),
            Accent = Rgb(0xFB, 0x92, 0x3C),
            PrimaryText = Rgb(0xF3, 0xED, 0xE7), SecondaryText = Rgb(0xB0, 0x9E, 0x92), SubtleText = Rgb(0x78, 0x6A, 0x60),
            Card = Argb(0x1A, 0xFF, 0xFF, 0xFF), CardHover = Argb(0x2E, 0xFF, 0xFF, 0xFF), Separator = Argb(0x26, 0xFF, 0xFF, 0xFF),
            Selection = Argb(0x3D, 0xFB, 0x92, 0x3C), Hover = Argb(0x33, 0xFF, 0xFF, 0xFF),
            Input = Argb(0x0F, 0xFF, 0xFF, 0xFF), InputBorder = Argb(0x1F, 0xFF, 0xFF, 0xFF),
            Badge = Argb(0x33, 0xFF, 0xFF, 0xFF), Handle = Argb(0x4D, 0xFF, 0xFF, 0xFF),
        };

        private static Theme Violet() => new Theme
        {
            SurfaceTop = Rgb(0x24, 0x1E, 0x33), SurfaceBottom = Rgb(0x1A, 0x16, 0x26),
            Accent = Rgb(0xA7, 0x8B, 0xFA),
            PrimaryText = Rgb(0xED, 0xEA, 0xF6), SecondaryText = Rgb(0xA3, 0x98, 0xC2), SubtleText = Rgb(0x6E, 0x64, 0x8C),
            Card = Argb(0x1A, 0xFF, 0xFF, 0xFF), CardHover = Argb(0x2E, 0xFF, 0xFF, 0xFF), Separator = Argb(0x26, 0xFF, 0xFF, 0xFF),
            Selection = Argb(0x3D, 0xA7, 0x8B, 0xFA), Hover = Argb(0x33, 0xFF, 0xFF, 0xFF),
            Input = Argb(0x0F, 0xFF, 0xFF, 0xFF), InputBorder = Argb(0x1F, 0xFF, 0xFF, 0xFF),
            Badge = Argb(0x33, 0xFF, 0xFF, 0xFF), Handle = Argb(0x4D, 0xFF, 0xFF, 0xFF),
        };

        private static Theme Light() => new Theme
        {
            SurfaceTop = Rgb(0xFF, 0xFF, 0xFF), SurfaceBottom = Rgb(0xE9, 0xEB, 0xEF),
            Accent = Rgb(0x25, 0x63, 0xEB),
            PrimaryText = Rgb(0x1F, 0x29, 0x37), SecondaryText = Rgb(0x6B, 0x72, 0x80), SubtleText = Rgb(0x9C, 0xA3, 0xAF),
            Card = Rgb(0xEF, 0xF1, 0xF4), CardHover = Rgb(0xE5, 0xE7, 0xEB), Separator = Rgb(0xD1, 0xD5, 0xDB),
            Selection = Rgb(0xD6, 0xE4, 0xFF), Hover = Rgb(0xE5, 0xE7, 0xEB),
            Input = Rgb(0xF3, 0xF4, 0xF6), InputBorder = Rgb(0xD1, 0xD5, 0xDB),
            Badge = Rgb(0xE5, 0xE7, 0xEB), Handle = Rgb(0x9C, 0xA3, 0xAF),
        };

        private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
        private static Color Argb(byte a, byte r, byte g, byte b) => Color.FromArgb(a, r, g, b);
    }
}
