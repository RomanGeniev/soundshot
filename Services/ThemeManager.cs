using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace GraphiteCompanion.Services
{
    /// <summary>Light / dark theme and accent colour, switched live (all colours are DynamicResource).</summary>
    public static class ThemeManager
    {
        public sealed record AccentInfo(string Key, string Name, Color A, Color B, Color Solid);

        public static readonly AccentInfo[] Accents =
        {
            new("Violet",  "Фиолетовый", Hex("#5B8CFF"), Hex("#9B6BFF"), Hex("#6E97FF")),
            new("Red",     "Красный",    Hex("#FF4D6D"), Hex("#FF8A3D"), Hex("#FF5C7A")),
            new("Green",   "Зелёный",    Hex("#22C55E"), Hex("#14B8A6"), Hex("#34D399")),
            new("Cyan",    "Бирюзовый",  Hex("#06B6D4"), Hex("#3B82F6"), Hex("#22D3EE")),
            new("Orange",  "Оранжевый",  Hex("#F97316"), Hex("#F43F5E"), Hex("#FB923C")),
            new("Pink",    "Розовый",    Hex("#EC4899"), Hex("#8B5CF6"), Hex("#F472B6")),
        };

        public static AccentInfo Current { get; private set; } = Accents[0];
        public static bool IsLight { get; private set; }

        public static AccentInfo FindAccent(string? key)
        {
            if (TryCustomColor(key, out var color))
                return new AccentInfo($"#{color.R:X2}{color.G:X2}{color.B:X2}", "Свой цвет", color, color, color);
            return Array.Find(Accents, a => a.Key == key) ?? Accents[0];
        }

        public static bool TryCustomColor(string? text, out Color color)
        {
            color = default;
            text = text?.Trim();
            if (text is not { Length: 7 } || text[0] != '#' ||
                !uint.TryParse(text.AsSpan(1), System.Globalization.NumberStyles.AllowHexSpecifier,
                    System.Globalization.CultureInfo.InvariantCulture, out uint rgb)) return false;
            color = Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
            return true;
        }

        private static double Luminance(Color c)
        {
            static double Channel(byte b) { double v = b / 255.0; return v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4); }
            return .2126 * Channel(c.R) + .7152 * Channel(c.G) + .0722 * Channel(c.B);
        }

        public static bool SystemUsesLight()
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return k?.GetValue("AppsUseLightTheme") is int v && v == 1;
            }
            catch { return false; }
        }

        /// <param name="theme">"Dark", "Light" or "System".</param>
        public static void Apply(string theme, string accentKey, string? backgroundColor = null)
        {
            bool customBackground = TryCustomColor(backgroundColor, out var background);
            bool light = customBackground ? Luminance(background) > .179 : theme == "Light" || (theme == "System" && SystemUsesLight());
            IsLight = light;
            var accent = FindAccent(accentKey);
            Current = accent;
            var r = Application.Current.Resources;

            void Set(string key, string hex) => r[key] = Freeze(new SolidColorBrush(Hex(hex)));

            if (light)
            {
                Set("BgBrush", "#F3F5FA"); Set("CardBrush", "#FFFFFF"); Set("CardHoverBrush", "#F2F5FB");
                Set("CardBorderBrush", "#DDE3EE"); Set("InsetBrush", "#EEF1F7"); Set("TextBrush", "#141A26");
                Set("SubTextBrush", "#5B667A"); Set("TrackBrush", "#D7DDE8"); Set("GoodBrush", "#16A34A");
                Set("WarnBrush", "#B7791F"); Set("GhostBrush", "#EEF1F7"); Set("GhostHoverBrush", "#E3E8F2");
                Set("GhostPressedBrush", "#D6DDEB"); Set("MutedBgBrush", "#FFE3E8"); Set("MutedFgBrush", "#E0355A");
                Set("SwitchOffBrush", "#C5CDDB"); Set("PopupBrush", "#F7FFFFFF"); Set("BannerBrush", "#FFF4DD");
                Set("BannerBorderBrush", "#F2D492"); Set("ZeroLineBrush", "#C9D1DF");
                r["SelectedBrush"] = Freeze(new SolidColorBrush(Mix(Hex("#FFFFFF"), accent.Solid, 0.12)));
            }
            else
            {
                Set("BgBrush", "#0B0E14"); Set("CardBrush", "#141A26"); Set("CardHoverBrush", "#1A2132");
                Set("CardBorderBrush", "#232C3E"); Set("InsetBrush", "#0F141F"); Set("TextBrush", "#E8ECF4");
                Set("SubTextBrush", "#8A94A8"); Set("TrackBrush", "#222A3B"); Set("GoodBrush", "#3DDC97");
                Set("WarnBrush", "#F5A524"); Set("GhostBrush", "#1A2132"); Set("GhostHoverBrush", "#232C42");
                Set("GhostPressedBrush", "#2B3550"); Set("MutedBgBrush", "#3A2330"); Set("MutedFgBrush", "#FF7A90");
                Set("SwitchOffBrush", "#2A3347"); Set("PopupBrush", "#F2121722"); Set("BannerBrush", "#2A200C");
                Set("BannerBorderBrush", "#5C4512"); Set("ZeroLineBrush", "#2A3347");
                r["SelectedBrush"] = Freeze(new SolidColorBrush(Mix(Hex("#141A26"), accent.Solid, 0.16)));
            }

            if(customBackground)
            {
                void Paint(string key,Color color)=>r[key]=Freeze(new SolidColorBrush(color));
                Color foreground=light?Colors.Black:Colors.White;
                Color card=Mix(background,light?Colors.White:Colors.Black,light ? .92 : .62);
                Color inset=Mix(background,light?Colors.White:Colors.Black,light ? .84 : .74);
                Color sub=Mix(foreground,background,.25);
                if(Contrast(sub,background)<4.5)sub=foreground;
                Paint("BgBrush",background);Paint("CardBrush",card);Paint("InsetBrush",inset);
                Paint("CardHoverBrush",Mix(background,light?Colors.White:Colors.Black,light ? .85 : .52));
                Paint("CardBorderBrush",Mix(background,light?Colors.Black:Colors.White,.18));
                Paint("GhostBrush",inset);Paint("GhostHoverBrush",Mix(background,light?Colors.White:Colors.Black,light ? .72 : .46));
                Paint("GhostPressedBrush",Mix(background,light?Colors.White:Colors.Black,light ? .62 : .36));
                Paint("TrackBrush",Mix(card,foreground,.18));Paint("SwitchOffBrush",Mix(card,foreground,.24));
                Paint("TextBrush",foreground);Paint("SubTextBrush",sub);
                Paint("PopupBrush",WithAlpha(card,0xF7));
                Paint("SelectedBrush",Mix(card,accent.Solid,light ? .12 : .16));
            }

            bool custom = TryCustomColor(accentKey, out _);
            Color readable = accent.Solid;
            if (custom && (light ? Luminance(readable) > .4 : Luminance(readable) < .15))
                readable = Mix(readable, light ? Colors.Black : Colors.White, .55);
            r["AccentBrush"] = Freeze(new SolidColorBrush(readable));
            r["AccentSolidTextBrush"] = Freeze(new SolidColorBrush(Luminance(readable) > .179 ? Colors.Black : Colors.White));
            r["AccentTextBrush"] = Freeze(new SolidColorBrush(custom && Luminance(accent.Solid) > .179 ? Colors.Black : Colors.White));
            r["AccentBorderBrush"] = Freeze(new SolidColorBrush(WithAlpha(accent.Solid, 0x60)));
            r["AccentGradient"] = Freeze(new LinearGradientBrush(accent.A, accent.B, new Point(0, 0), new Point(1, 1)));
            r["CurveFillBrush"] = Freeze(new LinearGradientBrush(WithAlpha(accent.A, 0x55), WithAlpha(accent.B, 0x08), 90));
            r["SpectrumBrush"] = Freeze(new LinearGradientBrush(WithAlpha(accent.Solid, light ? (byte)0x55 : (byte)0x46), WithAlpha(accent.B, 0x0C), 90));

            try { Application.Current.ThemeMode = light ? ThemeMode.Light : ThemeMode.Dark; } catch { }
        }

        private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }
        public static double Contrast(Color a,Color b) => (Math.Max(Luminance(a),Luminance(b))+.05)/(Math.Min(Luminance(a),Luminance(b))+.05);

        public static Color Hex(string hex) => (Color)ColorConverter.ConvertFromString(hex);

        private static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

        private static Color Mix(Color a, Color b, double t) => Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    }
}


