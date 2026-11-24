// The stations window: WPF, built from the XAML below at run time so the whole program
// still compiles with the csc that ships with Windows. Kept apart from Radio.cs because
// WPF's System.Windows.Media and WinRT's Windows.Media both name a MediaPlayer.
//
// Dressed as a car's head unit: the stations are logo tiles (or rows), the bar along the
// bottom is what plays, and the whole window takes its colour from that station's logo.
// The car's own controls choose the station; this only shows it.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Shell;
using System.Windows.Threading;

// On a thread of its own that ends when the window closes: the radio goes on either way.
static class StationsWindow
{
    static readonly object gate = new object();
    static StationsView open;
    static bool opening;

    public static void Show(EventWaitHandle quit)
    {
        // logos still missing are looked up only once the player opens the window, never in the background
        LogoFinder.Start();
        lock (gate)
        {
            if (opening) return;
            if (open != null)
            {
                StationsView v = open;
                v.Window.Dispatcher.BeginInvoke((Action)v.BringOut);
                return;
            }
            opening = true;
        }
        var t = new Thread(() =>
        {
            try
            {
                var v = new StationsView(quit);
                v.Window.Closed += delegate
                {
                    Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                };
                lock (gate)
                {
                    open = v;
                    opening = false;
                }
                v.Window.Show();
                Dispatcher.Run();
            }
            finally
            {
                lock (gate)
                {
                    open = null;
                    opening = false;
                }
            }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.IsBackground = true;
        t.Start();
    }
}

// The XAML names these @LIKE_THIS@; the code asks for the same ones by name. The G names are
// glyphs drawn on a 24-unit square, written out in full because WPF's path parser does not
// read SVG's run-together numbers (".8.8"). F1 fills overlaps (the + where its bars cross); WPF defaults to
// even-odd, which punches them out. The radio keeps even-odd for its cut-outs.
static class Palette
{
    static readonly Dictionary<string, string> values = new Dictionary<string, string> {
        { "TEXT", "#EDEDED" }, { "WHITE", "#F2F2F2" }, { "SOFT", "#CCFFFFFF" }, { "DIM", "#8CFFFFFF" },
        { "SONG", "#BDBDBD" }, { "GENRE", "#8C8C8C" },
        { "FILL", "#12FFFFFF" }, { "FILLHOVER", "#1FFFFFFF" }, { "FILLLIVE", "#24FFFFFF" },
        { "CHIP", "#1FFFFFFF" }, { "CHIPHOVER", "#33FFFFFF" }, { "LINE", "#1FFFFFFF" }, { "EDGE", "#24FFFFFF" },
        { "ROUND", "#8C141414" }, { "SHADE", "#8C000000" }, { "INK", "#1A1A1A" },
        { "RED", "#FF8A8A" }, { "REDSOLID", "#E5484D" }, { "REDHOVER", "#F05A5F" }, { "CLOSEHOVER", "#C42B1C" },
        { "GREEN", "#5BD47A" }, { "AMBER", "#F2B84B" },
        { "GGRID", "F1 M 5 3 H 9 A 2 2 0 0 1 11 5 V 9 A 2 2 0 0 1 9 11 H 5 A 2 2 0 0 1 3 9 V 5 A 2 2 0 0 1 5 3 Z M 15 3 H 19 A 2 2 0 0 1 21 5 V 9 A 2 2 0 0 1 19 11 H 15 A 2 2 0 0 1 13 9 V 5 A 2 2 0 0 1 15 3 Z M 5 13 H 9 A 2 2 0 0 1 11 15 V 19 A 2 2 0 0 1 9 21 H 5 A 2 2 0 0 1 3 19 V 15 A 2 2 0 0 1 5 13 Z M 15 13 H 19 A 2 2 0 0 1 21 15 V 19 A 2 2 0 0 1 19 21 H 15 A 2 2 0 0 1 13 19 V 15 A 2 2 0 0 1 15 13 Z" },
        { "GLIST", "F1 M 4.2 4 H 5.8 A 1.2 1.2 0 0 1 7 5.2 V 6.8 A 1.2 1.2 0 0 1 5.8 8 H 4.2 A 1.2 1.2 0 0 1 3 6.8 V 5.2 A 1.2 1.2 0 0 1 4.2 4 Z M 10.2 4.8 H 19.8 A 1.2 1.2 0 0 1 21 6 V 6 A 1.2 1.2 0 0 1 19.8 7.2 H 10.2 A 1.2 1.2 0 0 1 9 6 V 6 A 1.2 1.2 0 0 1 10.2 4.8 Z M 4.2 10 H 5.8 A 1.2 1.2 0 0 1 7 11.2 V 12.8 A 1.2 1.2 0 0 1 5.8 14 H 4.2 A 1.2 1.2 0 0 1 3 12.8 V 11.2 A 1.2 1.2 0 0 1 4.2 10 Z M 10.2 10.8 H 19.8 A 1.2 1.2 0 0 1 21 12 V 12 A 1.2 1.2 0 0 1 19.8 13.2 H 10.2 A 1.2 1.2 0 0 1 9 12 V 12 A 1.2 1.2 0 0 1 10.2 10.8 Z M 4.2 16 H 5.8 A 1.2 1.2 0 0 1 7 17.2 V 18.8 A 1.2 1.2 0 0 1 5.8 20 H 4.2 A 1.2 1.2 0 0 1 3 18.8 V 17.2 A 1.2 1.2 0 0 1 4.2 16 Z M 10.2 16.8 H 19.8 A 1.2 1.2 0 0 1 21 18 V 18 A 1.2 1.2 0 0 1 19.8 19.2 H 10.2 A 1.2 1.2 0 0 1 9 18 V 18 A 1.2 1.2 0 0 1 10.2 16.8 Z" },
        { "GSORT", "F1 M 4.2 4.8 H 19.8 A 1.2 1.2 0 0 1 21 6 A 1.2 1.2 0 0 1 19.8 7.2 H 4.2 A 1.2 1.2 0 0 1 3 6 A 1.2 1.2 0 0 1 4.2 4.8 Z M 4.2 10.8 H 14.8 A 1.2 1.2 0 0 1 16 12 A 1.2 1.2 0 0 1 14.8 13.2 H 4.2 A 1.2 1.2 0 0 1 3 12 A 1.2 1.2 0 0 1 4.2 10.8 Z M 4.2 16.8 H 9.8 A 1.2 1.2 0 0 1 11 18 A 1.2 1.2 0 0 1 9.8 19.2 H 4.2 A 1.2 1.2 0 0 1 3 18 A 1.2 1.2 0 0 1 4.2 16.8 Z" },
        { "GPIN", "F0 M 12 2.5 A 7 7 0 0 0 5 9.5 C 5 14.5 12 21.5 12 21.5 C 12 21.5 19 14.5 19 9.5 A 7 7 0 0 0 12 2.5 Z M 12 6.8 A 2.7 2.7 0 1 1 12 12.2 A 2.7 2.7 0 1 1 12 6.8 Z" },
        { "GALPHA", "F0 M 10.5 3.5 H 13.5 L 19.8 20.5 H 16.9 L 15.4 16.4 H 8.6 L 7.1 20.5 H 4.2 Z M 9.5 13.9 H 14.5 L 12 7 Z" },
        { "GNOTE", "F1 M 9 17.5 A 3 3 0 1 1 7 14.67 V 5.3 C 7 4.7 7.4 4.3 8 4.2 L 18 2.6 C 18.6 2.5 19 2.9 19 3.5 V 15.5 A 3 3 0 1 1 17 12.67 V 7.4 L 9 8.7 Z" },
        { "GPLUS","F1 M 12 4 H 12 A 1.2 1.2 0 0 1 13.2 5.2 V 18.8 A 1.2 1.2 0 0 1 12 20 H 12 A 1.2 1.2 0 0 1 10.8 18.8 V 5.2 A 1.2 1.2 0 0 1 12 4 Z M 5.2 10.8 H 18.8 A 1.2 1.2 0 0 1 20 12 V 12 A 1.2 1.2 0 0 1 18.8 13.2 H 5.2 A 1.2 1.2 0 0 1 4 12 V 12 A 1.2 1.2 0 0 1 5.2 10.8 Z" },
        { "GMORE", "F1 M 2.8 12 A 2.2 2.2 0 1 0 7.2 12 A 2.2 2.2 0 1 0 2.8 12 Z M 9.8 12 A 2.2 2.2 0 1 0 14.2 12 A 2.2 2.2 0 1 0 9.8 12 Z M 16.8 12 A 2.2 2.2 0 1 0 21.2 12 A 2.2 2.2 0 1 0 16.8 12 Z" },
        { "GCARET", "F1 M 6.6 9.2 h 10.8 c .9 0 1.3 1 .7 1.6 l -5.4 5.3 c -.4 .4 -1 .4 -1.4 0 L 5.9 10.8 c -.6 -.6 -.2 -1.6 .7 -1.6 z" },
        { "GLEFT", "F1 M 15.6 5.3 c .8 -.6 1.9 0 1.9 1 v 11.4 c 0 1 -1.1 1.6 -1.9 1 L 7.9 13 c -.7 -.5 -.7 -1.5 0 -2 z" },
        { "GRIGHT", "F1 M 8.4 5.3 c -.8 -.6 -1.9 0 -1.9 1 v 11.4 c 0 1 1.1 1.6 1.9 1 l 7.7 -5.7 c .7 -.5 .7 -1.5 0 -2 z" },
        { "GUP", "F1 M 5.3 15.6 c -.6 .8 0 1.9 1 1.9 h 11.4 c 1 0 1.6 -1.1 1 -1.9 L 13 7.9 c -.5 -.7 -1.5 -.7 -2 0 z" },
        { "GDOWN", "F1 M 5.3 8.4 c -.6 -.8 0 -1.9 1 -1.9 h 11.4 c 1 0 1.6 1.1 1 1.9 L 13 16.1 c -.5 .7 -1.5 .7 -2 0 z" },
        { "GEDIT", "F1 M 16.6 3.7 a 2.2 2.2 0 0 1 3.1 0 l .6 .6 a 2.2 2.2 0 0 1 0 3.1 L 9.2 18.5 l -5.2 1.5 1.5 -5.2 z" },
        { "GERASE", "F1 M 5.1 5.2 H 18.9 A 1.1 1.1 0 0 1 20 6.3 V 6.7 A 1.1 1.1 0 0 1 18.9 7.8 H 5.1 A 1.1 1.1 0 0 1 4 6.7 V 6.3 A 1.1 1.1 0 0 1 5.1 5.2 Z M 10.1 2.8 H 13.9 A 1.1 1.1 0 0 1 15 3.9 V 4.3 A 1.1 1.1 0 0 1 13.9 5.4 H 10.1 A 1.1 1.1 0 0 1 9 4.3 V 3.9 A 1.1 1.1 0 0 1 10.1 2.8 Z M 5.8 9.4 h 12.4 l -1 10 a 2.2 2.2 0 0 1 -2.2 2 H 9 a 2.2 2.2 0 0 1 -2.2 -2 z" },
        { "GSPEAKER", "F1 M 3 9.6 c 0 -.6 .4 -1 1 -1 h 3.1 l 4.7 -4 c .7 -.6 1.8 -.1 1.8 .8 v 13.2 c 0 .9 -1.1 1.4 -1.8 .8 l -4.7 -4 H 4 c -.6 0 -1 -.4 -1 -1 z" },
        { "GWAVES", "F1 M 16.3 8.6 a 4.6 4.6 0 0 1 0 6.8 M 18.9 6 a 8.4 8.4 0 0 1 0 12" },
        { "GPOWERBAR", "F1 M 12 2.5 H 12 A 1.2 1.2 0 0 1 13.2 3.7 V 10.3 A 1.2 1.2 0 0 1 12 11.5 H 12 A 1.2 1.2 0 0 1 10.8 10.3 V 3.7 A 1.2 1.2 0 0 1 12 2.5 Z" },
        { "GPOWERARC", "F1 M 7 5.6 a 8 8 0 1 0 10 0" },
        { "GANTENNA", "F1 M 440.94 177.034 l -286.69 -92.511 5.928 -18.37 286.69 92.511 -5.928 18.37 z" },
        { "GRADIO", "F1 M 80.947 155.28 h 350.126 c 24.555 0 44.401 19.876 44.401 44.431 v 201.697 c 0 24.043 -19.845 44.442 -44.401 44.442 h -350.126 c -24.546 0 -44.431 -20.398 -44.431 -44.442 v -201.708 c 0 -24.546 19.886 -44.421 44.431 -44.421 v 0 z M 79.391 208.599 c -6.277 0 -11.489 5.212 -11.489 12.022 v 0 c 0 6.267 5.212 11.489 11.489 11.489 h 172.974 c 6.789 0 12.022 -5.222 12.022 -11.489 v 0 c 0 -6.81 -5.233 -12.022 -12.022 -12.022 h -172.974 z M 79.391 248.832 c -6.277 0 -11.489 5.222 -11.489 11.49 v 0 c 0 6.8 5.212 12.042 11.489 12.042 h 172.974 c 6.789 0 12.022 -5.243 12.022 -12.042 v 0 c 0 -6.267 -5.233 -11.489 -12.022 -11.489 h -172.974 z M 79.391 288.563 c -6.277 0 -11.489 5.223 -11.489 12.002 v 0 c 0 6.277 5.212 11.5 11.489 11.5 h 172.974 c 6.789 0 12.022 -5.223 12.022 -11.5 v 0 c 0 -6.779 -5.233 -12.002 -12.022 -12.002 h -172.974 z M 79.391 328.796 c -6.277 0 -11.489 5.223 -11.489 11.479 v 0 c 0 6.779 5.212 12.002 11.489 12.002 h 172.974 c 6.789 0 12.022 -5.223 12.022 -12.002 v 0 c 0 -6.257 -5.233 -11.479 -12.022 -11.479 h -172.974 z M 79.391 368.517 c -6.277 0 -11.489 5.202 -11.489 12.002 v 0 c 0 6.277 5.212 11.5 11.489 11.5 h 172.974 c 6.789 0 12.022 -5.223 12.022 -11.5 v 0 c 0 -6.8 -5.233 -12.002 -12.022 -12.002 h -172.974 z M 371.507 220.089 c -44.411 0 -80.466 36.065 -80.466 80.476 0 44.421 36.055 80.476 80.466 80.476 44.401 0 80.476 -36.055 80.476 -80.476 0.010 -44.411 -36.075 -80.476 -80.476 -80.476 v 0 z M 371.507 235.776 c -35.553 0 -64.788 28.723 -64.788 64.788 0 35.543 29.235 64.798 64.788 64.798 35.533 0 64.809 -29.255 64.809 -64.798 0 -36.065 -29.276 -64.788 -64.809 -64.788 z" }
    };

    // <Glyph G='NAME' Size='16' .../> becomes the glyph on its 24-unit square, scaled; the
    // other attributes go on the path
    static readonly Regex glyphTag = new Regex(@"<Glyph G='(\w+)' Size='([\d.]+)'(.*?)/>");

    public static string Apply(string xaml)
    {
        xaml = glyphTag.Replace(xaml, m =>
            "<Viewbox Width='" + m.Groups[2].Value + "' Height='" + m.Groups[2].Value + "' IsHitTestVisible='False'>" +
            "<Canvas Width='24' Height='24'><Path Data='@G" + m.Groups[1].Value + "@'" + m.Groups[3].Value + "/></Canvas></Viewbox>");
        foreach (KeyValuePair<string, string> v in values) xaml = xaml.Replace("@" + v.Key + "@", v.Value);
        return xaml;
    }

    public static Brush Brush(string name)
    {
        return new SolidColorBrush(Colour(name));
    }

    public static Color Colour(string name)
    {
        return (Color)ColorConverter.ConvertFromString(values[name]);
    }

    public static Geometry Glyph(string name)
    {
        return Geometry.Parse(values["G" + name]);
    }

    // a glyph made in code, as the XAML's <Glyph> makes one
    public static FrameworkElement Icon(string name, double size, Brush fill)
    {
        var canvas = new Canvas { Width = 24, Height = 24 };
        if (name == "POWER")
        {
            canvas.Children.Add(new Path { Data = Glyph("POWERBAR"), Fill = fill });
            canvas.Children.Add(new Path { Data = Glyph("POWERARC"), Stroke = fill, StrokeThickness = 2.4,
                                           StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
        }
        else canvas.Children.Add(new Path { Data = Glyph(name), Fill = fill });
        return new Viewbox { Width = size, Height = size, Child = canvas, IsHitTestVisible = false };
    }

    public static Color Mix(Color a, Color b, double t)
    {
        return Color.FromArgb(255, (byte)(a.R * t + b.R * (1 - t)), (byte)(a.G * t + b.G * (1 - t)),
                              (byte)(a.B * t + b.B * (1 - t)));
    }
}

// The colour a station is known by, as Android Auto and many head units pick one: its logo's
// most common strong hue, brought into the lightness that reads on the dark window. Grey
// for a logo without colour - Magic FM's black M - and for no logo at all.
static class Accent
{
    public static readonly Color Grey = Color.FromRgb(0xD6, 0xD6, 0xD6);
    const int Hues = 18;
    // a hue counts only if this many fully strong pixels' worth of the logo carry it
    const double Enough = 80;
    static readonly Dictionary<string, Color> known = new Dictionary<string, Color>();

    public static Color Of(PresetRow row)
    {
        if (row == null) return Grey;
        string key = row.Station.Logo ?? "";
        Color c;
        if (known.TryGetValue(key, out c)) return c;
        var image = row.Logo as BitmapSource;
        c = image == null ? Grey : Measure(image);
        known[key] = c;
        return c;
    }

    static Color Measure(BitmapSource image)
    {
        var bgra = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        int stride = bgra.PixelWidth * 4;
        var px = new byte[stride * bgra.PixelHeight];
        bgra.CopyPixels(px, stride, 0);
        var sum = new double[Hues, 4];
        for (int i = 0; i < px.Length; i += 4)
        {
            if (px[i + 3] < 128) continue;
            double b = px[i] / 255.0, g = px[i + 1] / 255.0, r = px[i + 2] / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            double s = max == 0 ? 0 : (max - min) / max;
            if (s < 0.35 || max < 0.25) continue;
            int k = (int)(Hue(r, g, b, max, min) * Hues) % Hues;
            double w = s * max;
            sum[k, 0] += r * w;
            sum[k, 1] += g * w;
            sum[k, 2] += b * w;
            sum[k, 3] += w;
        }
        int best = 0;
        for (int k = 1; k < Hues; k++) if (sum[k, 3] > sum[best, 3]) best = k;
        double weight = sum[best, 3];
        if (weight < Enough) return Grey;

        double h, l, sat;
        ToHls(sum[best, 0] / weight, sum[best, 1] / weight, sum[best, 2] / weight, out h, out l, out sat);
        return FromHls(h, Math.Min(Math.Max(l, 0.55), 0.66), Math.Max(sat, 0.65));
    }

    // 0..1 round the colour wheel
    static double Hue(double r, double g, double b, double max, double min)
    {
        double d = max - min;
        if (d == 0) return 0;
        double h = max == r ? (g - b) / d : max == g ? 2 + (b - r) / d : 4 + (r - g) / d;
        h /= 6;
        return h < 0 ? h + 1 : h;
    }

    static void ToHls(double r, double g, double b, out double h, out double l, out double s)
    {
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        l = (max + min) / 2;
        h = Hue(r, g, b, max, min);
        s = max == min ? 0 : l <= 0.5 ? (max - min) / (max + min) : (max - min) / (2 - max - min);
    }

    static Color FromHls(double h, double l, double s)
    {
        double m2 = l <= 0.5 ? l * (1 + s) : l + s - l * s, m1 = 2 * l - m2;
        return Color.FromRgb(Channel(m1, m2, h + 1 / 3.0), Channel(m1, m2, h), Channel(m1, m2, h - 1 / 3.0));
    }

    static byte Channel(double m1, double m2, double h)
    {
        h = h < 0 ? h + 1 : h > 1 ? h - 1 : h;
        double v = h < 1 / 6.0 ? m1 + (m2 - m1) * h * 6 : h < 0.5 ? m2 : h < 2 / 3.0 ? m1 + (m2 - m1) * (2 / 3.0 - h) * 6 : m1;
        return (byte)Math.Round(v * 255);
    }
}

// one station on the grid or the list
sealed class PresetRow
{
    public Station Station { get; set; }
    public bool IsLive { get; set; }
    // what the list's live row says: Playing, Tuning in, No signal
    public string LiveText { get; set; }
    public string Name { get { return Station.Name; } }
    public string Genre { get { return Station.Genre; } }
    public ImageSource Logo { get; set; }
    // where the station sits in the saved list; the window shows them grouped by country instead
    public int Index { get; set; }

    // the group the station is shown under; one with no country goes last, under Other
    public const string NoCountry = "Other";

    public string Country
    {
        get { return string.IsNullOrEmpty(Station.Country) ? NoCountry : Station.Country; }
    }

    // the first genre named, "Pop" of "Pop, Rock": the group when sorting by genre
    public string Kind
    {
        get
        {
            string g = (Station.Genre ?? "").Split(',', '/', ';')[0].Trim();
            return g.Length > 0 ? g : NoCountry;
        }
    }

    // groups A to Z with Other last
    static int Groups(string a, string b)
    {
        bool ao = a == NoCountry, bo = b == NoCountry;
        return ao != bo ? (ao ? 1 : -1) : string.Compare(a, b, StringComparison.CurrentCultureIgnoreCase);
    }

    static int Names(PresetRow a, PresetRow b)
    {
        return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
    }

    // the window's sorts: by country, each in the saved order; by name; by genre, each by name;
    // or the saved order itself, the one the game shows
    public static Comparison<PresetRow> Sort(string by)
    {
        switch (by)
        {
            case "name": return (a, b) => { int c = Names(a, b); return c != 0 ? c : a.Index.CompareTo(b.Index); };
            case "genre": return (a, b) => { int c = Groups(a.Kind, b.Kind); if (c == 0) c = Names(a, b); return c != 0 ? c : a.Index.CompareTo(b.Index); };
            case "custom": return (a, b) => a.Index.CompareTo(b.Index);
            default: return (a, b) => { int c = Groups(a.Country, b.Country); return c != 0 ? c : a.Index.CompareTo(b.Index); };
        }
    }

    // the property a sort groups by, or null for one long list
    public static string GroupBy(string by)
    {
        return by == "genre" ? "Kind" : by == "country" ? "Country" : null;
    }

    // read whole and frozen, so the file is not held open and the image can cross threads
    public static ImageSource Load(string logo)
    {
        if (string.IsNullOrEmpty(logo)) return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(System.IO.Path.Combine(Stations.LogosDir, System.IO.Path.GetFileName(logo)));
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception) { return null; }
    }
}

sealed class StationsView
{
    static readonly Duration Fade = new Duration(TimeSpan.FromMilliseconds(500));
    static readonly Duration Quick = new Duration(TimeSpan.FromMilliseconds(220));
    static readonly Duration Slide = new Duration(TimeSpan.FromMilliseconds(320));
    static readonly Duration Away = new Duration(TimeSpan.FromMilliseconds(180));
    static readonly Duration SlideAway = new Duration(TimeSpan.FromMilliseconds(260));
    static readonly Color Floor = Color.FromRgb(0x0E, 0x0E, 0x0E);
    // the narrowest a tile's cell gets before a column goes: six across at the window's 820
    const double TileStep = 124;

    public readonly Window Window;
    readonly EventWaitHandle quit;
    readonly FrameworkElement root;
    readonly ItemsControl tiles, list;
    readonly ScrollViewer scroller;
    readonly GradientStop top, middle, bottom;
    readonly Grid menuLayer;
    readonly Canvas menuCanvas;
    readonly Border menu;
    readonly Path viewIcon, stateSpeaker, stateWaves;
    readonly FrameworkElement dockLogoBox, dockRadio, stateRow, editor, confirm;
    readonly Image dockLogo;
    readonly TextBlock stateText, dockName, dockSong, dockGenre, empty;
    readonly TextBlock editorTitle, checkResult, confirmTitle, confirmText;
    readonly Border menuBody, menuTint, editorTint, confirmTint;
    readonly FrameworkElement editorPanel, confirmCard;
    // per window: the window has a thread of its own, and an easing made on another cannot be used
    readonly IEasingFunction ease = new CubicEase { EasingMode = EasingMode.EaseOut };
    // leaving starts slowly, or most of the way is gone in the first frame and it looks cut
    readonly IEasingFunction leave = new QuadraticEase { EasingMode = EasingMode.EaseIn };
    readonly Grid main;
    readonly Ellipse checkLed;
    readonly TextBox nameBox, countryBox, genreBox, urlBox;
    readonly Button checkButton, confirmYes, closeButton, maxButton;
    readonly TextBlock maxGlyph;
    List<Station> stations = new List<Station>();
    List<PresetRow> rows = new List<PresetRow>();
    int editing = -1;
    int liveIndex = -1;
    string liveText = "";
    int seenVersion = -1;
    bool listView;
    // how the stations are laid out: country, name, genre or custom (the saved order)
    string sortBy = "country";
    Color accent = Accent.Grey;
    Action confirmed;
    Point dragFrom;
    PresetRow dragRow;

    public StationsView(EventWaitHandle quit)
    {
        this.quit = quit;
        root = (FrameworkElement)XamlReader.Parse(Palette.Apply(Xaml));

        Window = new Window
        {
            Title = Radio.Name,
            Content = root,
            Width = 820,
            Height = 700,
            MinWidth = 640,
            MinHeight = 540,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = new SolidColorBrush(Floor)
        };
        // the window draws its own title bar; snapping and resizing stay Windows'
        WindowChrome.SetWindowChrome(Window, new WindowChrome
        {
            CaptionHeight = 36,
            ResizeBorderThickness = new Thickness(6),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false
        });
        // a maximized window hangs over the screen edge by its resize border
        Window.StateChanged += delegate
        {
            root.Margin = Window.WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
            ShowMaxState();
        };
        ImageSource icon = AppIcon();
        if (icon != null) Window.Icon = icon;
        // Windows shows a program's first window the way its launcher asked - minimized,
        // when the background copy was started that way - so it is restored once shown
        Window.ContentRendered += delegate { BringOut(); };
        Window.PreviewKeyDown += OnKey;

        // stops animated in code, so the brush is built here rather than frozen in the XAML
        top = new GradientStop(Shade(accent, 0.46), 0);
        middle = new GradientStop(Shade(accent, 0.24), 0.5);
        bottom = new GradientStop(Shade(accent, 0.14), 1);
        // on Main rather than the window, so the frosted copies of Main carry the colour too
        main = Find<Grid>("Main");
        main.Background = new LinearGradientBrush(new GradientStopCollection { top, middle, bottom }, 90);
        Frost("MenuFrost", "MenuBlur", 7);
        Frost("EditorFrost", "EditorBlur", 0);
        Frost("ConfirmFrost", "ConfirmBlur", 9);
        menuBody = Find<Border>("MenuBody");
        menuTint = Find<Border>("MenuTint");
        editorTint = Find<Border>("EditorTint");
        confirmTint = Find<Border>("ConfirmTint");
        editorPanel = Find<FrameworkElement>("EditorPanel");
        confirmCard = Find<FrameworkElement>("ConfirmCard");

        tiles = Find<ItemsControl>("Tiles");
        list = Find<ItemsControl>("Rows");
        scroller = Find<ScrollViewer>("Scroller");
        menuLayer = Find<Grid>("MenuLayer");
        menuCanvas = Find<Canvas>("MenuCanvas");
        menu = Find<Border>("Menu");
        viewIcon = Find<Path>("ViewIcon");
        stateSpeaker = Find<Path>("StateSpeaker");
        stateWaves = Find<Path>("StateWaves");
        stateRow = Find<FrameworkElement>("StateRow");
        stateText = Find<TextBlock>("StateText");
        dockLogoBox = Find<FrameworkElement>("DockLogoBox");
        dockLogo = Find<Image>("DockLogo");
        dockRadio = Find<FrameworkElement>("DockRadio");
        dockName = Find<TextBlock>("DockName");
        dockSong = Find<TextBlock>("DockSong");
        dockGenre = Find<TextBlock>("DockGenre");
        empty = Find<TextBlock>("Empty");
        editor = Find<FrameworkElement>("Editor");
        editorTitle = Find<TextBlock>("EditorTitle");
        nameBox = Find<TextBox>("NameBox");
        countryBox = Find<TextBox>("CountryBox");
        genreBox = Find<TextBox>("GenreBox");
        urlBox = Find<TextBox>("UrlBox");
        // measured once the box is on screen; before that it has no size to measure in
        urlBox.IsVisibleChanged += delegate
        {
            if (urlBox.IsVisible) Window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, (Action)(() => AlignExample(urlBox)));
        };
        checkButton = Find<Button>("CheckButton");
        checkResult = Find<TextBlock>("CheckResult");
        checkLed = Find<Ellipse>("CheckLed");
        confirm = Find<FrameworkElement>("Confirm");
        confirmTitle = Find<TextBlock>("ConfirmTitle");
        confirmText = Find<TextBlock>("ConfirmText");
        confirmYes = Find<Button>("ConfirmYes");

        Find<Button>("MinButton").Click += delegate { Window.WindowState = WindowState.Minimized; };
        maxButton = Find<Button>("MaxButton");
        maxGlyph = Find<TextBlock>("MaxGlyph");
        maxButton.Click += delegate
        {
            Window.WindowState = Window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        };
        ShowMaxState();
        closeButton = Find<Button>("CloseButton");
        closeButton.Click += delegate { Window.Close(); };
        ShowCloseTip();
        // every way out: the button, Esc, Alt+F4, the taskbar
        Window.Closed += delegate { if (!Prefs.CloseToTray) quit.Set(); };
        Find<Button>("AddButton").Click += delegate { OpenEditor(-1); };
        Find<Button>("SortButton").Click += (s, e) => SortMenu((FrameworkElement)s);
        Find<Button>("ViewButton").Click += (s, e) => ViewMenu((FrameworkElement)s);
        Find<Button>("MoreButton").Click += (s, e) => MoreMenu((FrameworkElement)s);
        Find<Button>("StoreButton").Click += delegate { SaveEditor(); };
        Find<Button>("CancelEdit").Click += delegate { CloseEditor(); };
        checkButton.Click += delegate { CheckLink(); };
        Find<Button>("ConfirmNo").Click += delegate { CloseConfirm(); };
        confirmYes.Click += delegate
        {
            CloseConfirm();
            if (confirmed != null) confirmed();
        };
        menuLayer.MouseDown += (s, e) => { if (e.OriginalSource == menuLayer) CloseMenu(); };

        foreach (ItemsControl items in new[] { tiles, list })
        {
            // the row buttons live in a template, so their clicks are caught on the way up
            items.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnRowButton));
            items.PreviewMouseLeftButtonDown += OnRowPress;
            items.PreviewMouseMove += OnRowDrag;
            items.PreviewMouseRightButtonUp += OnRowRightClick;
            items.Drop += OnRowDrop;
        }
        scroller.SizeChanged += delegate { FitColumns(); };
        // the bar is as tall as its lines, so the list ends wherever it begins
        var dock = Find<FrameworkElement>("Dock");
        dock.SizeChanged += delegate
        {
            scroller.Margin = new Thickness(18, 4, 0, dock.ActualHeight);
            empty.Margin = new Thickness(0, 0, 0, dock.ActualHeight);
        };

        listView = Prefs.Get("view", "grid") == "list";
        sortBy = Prefs.Get("sort", "country");
        if (sortBy != "name" && sortBy != "genre" && sortBy != "custom") sortBy = "country";
        ShowView();

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += delegate { ShowStatus(); };
        timer.Start();
        Window.Closed += delegate { timer.Stop(); };

        // painted here too: a grey station never changes the starting grey, so Tint never runs for it
        PaintState();
        Fill();
        ShowStatus();
    }

    T Find<T>(string name) where T : class
    {
        return (T)root.FindName(name);
    }

    // FastLaneRadio.ico, embedded by build.cmd; the window picks the size it needs from it
    static ImageSource AppIcon()
    {
        using (var s = typeof(StationsView).Assembly.GetManifestResourceStream("FastLaneRadio.ico"))
        {
            if (s == null) return null;
            var decoder = new IconBitmapDecoder(s, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            return decoder.Frames[0];
        }
    }

    public void BringOut()
    {
        if (Window.WindowState == WindowState.Minimized) Window.WindowState = WindowState.Normal;
        // a moment on top, or it opens behind whatever has the focus
        Window.Topmost = true;
        Window.Topmost = false;
        Window.Activate();
    }

    static Color Shade(Color c, double amount)
    {
        return Palette.Mix(c, Floor, amount);
    }

    void ShowStatus()
    {
        string kind = Player.NowKind, url = Player.NowUrl, title = Player.NowTitle;
        int live = string.IsNullOrEmpty(url) ? -1 : stations.FindIndex(s => s.Url == url);
        Station st = live >= 0 ? stations[live] : null;

        string state = "", song = "";
        if (st != null && kind == "playing")
        {
            state = "Playing";
            song = title;
        }
        else if (st != null && kind == "connecting") state = "Tuning in";
        else if (st != null && kind == "error")
        {
            state = "No signal";
            song = "Not answering, trying again";
        }
        else
        {
            st = null;
            live = -1;
            song = kind == "off" ? "Waiting for BeamNG.drive" : "Choose a station in the game";
        }

        stateRow.Visibility = state.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        stateText.Text = state;
        dockName.Text = st != null ? st.Name : "Not playing";
        dockSong.Text = song;
        dockSong.Visibility = song.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        // where it is from, then what it plays: "France · News, Talk"
        dockGenre.Text = st == null ? "" : string.Join(" · ", new[] { st.Country, st.Genre }.Where(t => t.Length > 0));
        dockGenre.Visibility = dockGenre.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        // a logo found in the background, or the list changed some other way
        if (live != liveIndex || state != liveText || Stations.Version != seenVersion)
        {
            liveIndex = live;
            liveText = state;
            Fill();
        }
        PresetRow row = live >= 0 ? RowAt(live) : null;
        dockLogo.Source = row != null ? row.Logo : null;
        dockLogo.Visibility = dockLogo.Source != null ? Visibility.Visible : Visibility.Collapsed;
        dockRadio.Visibility = dockLogo.Source == null ? Visibility.Visible : Visibility.Collapsed;
        Tint(Accent.Of(row));
    }

    // the window fades to the playing station's colour rather than jumping to it
    void Tint(Color c)
    {
        if (c == accent) return;
        accent = c;
        top.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(Shade(c, 0.46), Fade));
        middle.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(Shade(c, 0.24), Fade));
        bottom.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(Shade(c, 0.14), Fade));
        PaintState();
    }

    // the "Playing" line in a light shade of the station's colour
    void PaintState()
    {
        var light = new SolidColorBrush(Palette.Mix(accent, Colors.White, 0.7));
        stateText.Foreground = light;
        stateSpeaker.Fill = light;
        stateWaves.Stroke = light;
    }

    void Fill()
    {
        seenVersion = Stations.Version;
        stations = Stations.All();
        rows = new List<PresetRow>();
        for (int i = 0; i < stations.Count; i++)
            rows.Add(new PresetRow { Station = stations[i], IsLive = i == liveIndex, LiveText = liveText,
                                     Logo = PresetRow.Load(stations[i].Logo), Index = i });
        // rows in the order shown, so a group is made in that order and a row's neighbours are on screen too
        rows.Sort(PresetRow.Sort(sortBy));
        tiles.ItemsSource = Grouped(rows, sortBy);
        list.ItemsSource = Grouped(rows, sortBy);
        empty.Visibility = stations.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // one view each: the grid and the list must not share a view's current item
    static ListCollectionView Grouped(List<PresetRow> rows, string by)
    {
        var view = new ListCollectionView(rows);
        string group = PresetRow.GroupBy(by);
        if (group != null) view.GroupDescriptions.Add(new PropertyGroupDescription(group));
        return view;
    }

    // by name or genre the sort places every station, so moving one would undo itself
    bool CanMove { get { return sortBy == "country" || sortBy == "custom"; } }

    // stations move past each other only inside one group
    bool SameGroup(PresetRow a, PresetRow b)
    {
        return sortBy != "country" || a.Country == b.Country;
    }

    PresetRow RowAt(int index)
    {
        return rows.Find(r => r.Index == index);
    }

    // the row shown next to this one in its group, or null at the group's edge or when it cannot move
    PresetRow Beside(PresetRow row, int step)
    {
        int i = rows.IndexOf(row) + step;
        return CanMove && i >= 0 && i < rows.Count && SameGroup(rows[i], row) ? rows[i] : null;
    }

    void Save()
    {
        Stations.Replace(stations);
        Fill();
    }

    void FitColumns()
    {
        tiles.Tag = Math.Max(2, (int)((scroller.ActualWidth - 12) / TileStep));
    }

    void ShowView()
    {
        tiles.Visibility = listView ? Visibility.Collapsed : Visibility.Visible;
        list.Visibility = listView ? Visibility.Visible : Visibility.Collapsed;
        viewIcon.Data = Palette.Glyph(listView ? "LIST" : "GRID");
    }

    static PresetRow RowOf(object source)
    {
        var e = source as FrameworkElement;
        return e != null ? e.DataContext as PresetRow : null;
    }

    static bool InButton(DependencyObject d)
    {
        for (; d != null; d = d is Visual ? VisualTreeHelper.GetParent(d) : null)
            if (d is ButtonBase) return true;
        return false;
    }

    void OnRowButton(object sender, RoutedEventArgs e)
    {
        var b = e.OriginalSource as Button;
        if (b == null || (b.CommandParameter as string) != "more") return;
        PresetRow row = RowOf(b);
        if (row != null) RowMenu(row, b, new Point(0, 0));
    }

    void OnRowPress(object sender, MouseButtonEventArgs e)
    {
        dragRow = null;
        if (InButton(e.OriginalSource as DependencyObject)) return;
        PresetRow row = RowOf(e.OriginalSource);
        if (row == null) return;
        if (e.ClickCount == 2)
        {
            OpenEditor(row.Index);
            e.Handled = true;
            return;
        }
        dragRow = row;
        dragFrom = e.GetPosition(root);
    }

    // a tile carried onto another takes its place, the ones between moving up one
    void OnRowDrag(object sender, MouseEventArgs e)
    {
        if (dragRow == null || e.LeftButton != MouseButtonState.Pressed) return;
        Vector moved = e.GetPosition(root) - dragFrom;
        if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        PresetRow row = dragRow;
        dragRow = null;
        var items = (ItemsControl)sender;
        items.AllowDrop = true;
        DragDrop.DoDragDrop(items, row, DragDropEffects.Move);
        items.AllowDrop = false;
    }

    void OnRowDrop(object sender, DragEventArgs e)
    {
        var from = e.Data.GetData(typeof(PresetRow)) as PresetRow;
        PresetRow to = RowOf(e.OriginalSource);
        // a station stays under its own country, which its Country field says
        if (from == null || to == null || from == to || !CanMove || !SameGroup(from, to)) return;
        Move(from.Index, to.Index);
    }

    void OnRowRightClick(object sender, MouseButtonEventArgs e)
    {
        PresetRow row = RowOf(e.OriginalSource);
        if (row == null) return;
        RowMenu(row, null, e.GetPosition(menuCanvas));
        e.Handled = true;
    }

    void Move(int from, int to)
    {
        if (from < 0 || to < 0 || from >= stations.Count || to >= stations.Count || from == to) return;
        Station s = stations[from];
        stations.RemoveAt(from);
        stations.Insert(to, s);
        Save();
    }

    // ---- menus: drawn inside the window, tinted like it, closed by a click anywhere else

    // anchored below a button, its right edge to the button's, or at a point
    void OpenMenu(FrameworkElement anchor, Point at, params UIElement[] items)
    {
        var panel = new StackPanel { MinWidth = 168 };
        foreach (UIElement item in items) panel.Children.Add(item);
        menuBody.Child = panel;
        menuTint.Background = Frosted();
        menuLayer.Visibility = Visibility.Visible;
        // laid out now, or the layer just shown still measures 0 wide and the menu lands in the corner
        menuLayer.UpdateLayout();
        menu.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Size size = menu.DesiredSize;
        Point p = at;
        if (anchor != null)
        {
            Point corner = anchor.TranslatePoint(new Point(anchor.ActualWidth, anchor.ActualHeight + 4), menuCanvas);
            p = new Point(corner.X - size.Width, corner.Y);
        }
        double maxX = menuCanvas.ActualWidth - size.Width - 8, maxY = menuCanvas.ActualHeight - size.Height - 8;
        Canvas.SetLeft(menu, Math.Max(8, Math.Min(p.X, maxX)));
        Canvas.SetTop(menu, Math.Max(8, Math.Min(p.Y, maxY)));
        // grows out of the corner it hangs from
        menu.RenderTransformOrigin = anchor != null ? new Point(1, 0) : new Point(0, 0);
        Reveal(menuLayer, menu, Quick, 0, -6, 0.97);
    }

    static Color WithAlpha(Color c, byte a)
    {
        return Color.FromArgb(a, c.R, c.G, c.B);
    }

    void CloseMenu()
    {
        Conceal(menuLayer, menu, Away, 0, -4, 0.98, () => menuBody.Child = null);
    }

    // ---- showing and hiding: the layer fades while its panel moves the last stretch

    static bool IsOpen(FrameworkElement layer)
    {
        return layer.Visibility == Visibility.Visible && layer.Tag == null;
    }

    void Reveal(FrameworkElement layer, FrameworkElement panel, Duration time, double x, double y, double scale)
    {
        // a close still fading out must not hide the layer once it is open again
        layer.Tag = null;
        layer.Visibility = Visibility.Visible;
        layer.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, time) { EasingFunction = ease });
        Move(panel, time, true, x, y, scale);
    }

    void Conceal(FrameworkElement layer, FrameworkElement panel, Duration time, double x, double y, double scale,
                 Action after = null)
    {
        if (!IsOpen(layer)) return;
        var closing = new object();
        layer.Tag = closing;
        var fade = new DoubleAnimation(0, time) { EasingFunction = leave };
        fade.Completed += delegate
        {
            if (layer.Tag != closing) return;
            layer.Visibility = Visibility.Collapsed;
            layer.Tag = null;
            if (after != null) after();
        };
        layer.BeginAnimation(UIElement.OpacityProperty, fade);
        Move(panel, time, false, x, y, scale);
    }

    // opening, from the offset into place; closing, from where it is to the offset
    void Move(FrameworkElement panel, Duration time, bool opening, double x, double y, double scale)
    {
        var group = panel.RenderTransform as TransformGroup;
        if (group == null)
        {
            group = new TransformGroup();
            group.Children.Add(new ScaleTransform());
            group.Children.Add(new TranslateTransform());
            panel.RenderTransform = group;
        }
        var size = (ScaleTransform)group.Children[0];
        var shift = (TranslateTransform)group.Children[1];
        Animate(shift, TranslateTransform.XProperty, opening, x, 0, time);
        Animate(shift, TranslateTransform.YProperty, opening, y, 0, time);
        Animate(size, ScaleTransform.ScaleXProperty, opening, scale, 1, time);
        Animate(size, ScaleTransform.ScaleYProperty, opening, scale, 1, time);
    }

    void Animate(Animatable target, DependencyProperty property, bool opening, double offset, double home, Duration time)
    {
        var a = opening ? new DoubleAnimation(offset, home, time) : new DoubleAnimation(offset, time);
        a.EasingFunction = opening ? ease : leave;
        target.BeginAnimation(property, a);
    }

    void CloseEditor(Action after = null)
    {
        Conceal(editor, editorPanel, SlideAway, editorPanel.ActualWidth, 0, 1, after);
    }

    void CloseConfirm()
    {
        Conceal(confirm, confirmCard, Away, 0, 4, 0.98);
    }

    Button MenuItem(string glyph, string text, Action act, bool red = false, bool lit = false, UIElement right = null)
    {
        Brush ink = red ? Palette.Brush("RED") : Palette.Brush("WHITE");
        var line = new DockPanel { LastChildFill = true };
        if (right != null)
        {
            DockPanel.SetDock(right, Dock.Right);
            line.Children.Add(right);
        }
        FrameworkElement icon = Palette.Icon(glyph, 14, ink);
        icon.Margin = new Thickness(0, 0, 10, 0);
        DockPanel.SetDock(icon, Dock.Left);
        line.Children.Add(icon);
        line.Children.Add(new TextBlock { Text = text, Foreground = ink, VerticalAlignment = VerticalAlignment.Center });
        var b = new Button { Style = (Style)root.FindResource("MenuRow"), Content = line };
        if (lit) b.Background = Palette.Brush("FILLHOVER");
        b.Click += delegate { act(); };
        return b;
    }

    static UIElement MenuLine()
    {
        return new Border { Height = 1, Background = Palette.Brush("EDGE"), Margin = new Thickness(2, 4, 2, 4) };
    }

    void ViewMenu(FrameworkElement anchor)
    {
        OpenMenu(anchor, new Point(),
                 MenuItem("GRID", "Grid", () => SetView(false), lit: !listView),
                 MenuItem("LIST", "List", () => SetView(true), lit: listView));
    }

    void SortMenu(FrameworkElement anchor)
    {
        OpenMenu(anchor, new Point(),
                 MenuItem("PIN", "Country", () => SetSort("country"), lit: sortBy == "country"),
                 MenuItem("ALPHA", "Name", () => SetSort("name"), lit: sortBy == "name"),
                 MenuItem("NOTE", "Genre", () => SetSort("genre"), lit: sortBy == "genre"),
                 MenuItem("SORT", "Your order", () => SetSort("custom"), lit: sortBy == "custom"));
    }

    void SetSort(string by)
    {
        CloseMenu();
        sortBy = by;
        Prefs.Set("sort", by);
        Fill();
    }

    void SetView(bool asList)
    {
        CloseMenu();
        listView = asList;
        Prefs.Set("view", asList ? "list" : "grid");
        ShowView();
    }

    void MoreMenu(FrameworkElement anchor)
    {
        OpenMenu(anchor, new Point(),
                 SwitchItem("Close to tray", Prefs.CloseToTray, () =>
                 {
                     Prefs.CloseToTray = !Prefs.CloseToTray;
                     ShowCloseTip();
                     return Prefs.CloseToTray;
                 }),
                 MenuLine(),
                 MenuItem("POWER", "Power off", () =>
                 {
                     CloseMenu();
                     Ask("Power off " + Radio.Name + "?", "Mods can't play internet radio until you start it again.",
                         "Power off", () => { quit.Set(); Window.Close(); });
                 }, red: true));
    }

    // flip returns the setting as it is after the click
    Button SwitchItem(string text, bool on, Func<bool> flip)
    {
        Border knob;
        Border sw = Switch(on, out knob);
        Button item = MenuItem("POWER", text, () => SetSwitch(sw, knob, flip(), true), right: sw);
        // the switch shows the state; a glyph beside it would only crowd it
        ((DockPanel)item.Content).Children[1].Visibility = Visibility.Collapsed;
        return item;
    }

    void ShowMaxState()
    {
        bool max = Window.WindowState == WindowState.Maximized;
        maxGlyph.Text = max ? "\uE923" : "\uE922";
        maxButton.ToolTip = max ? "Restore down" : "Maximize";
    }

    void ShowCloseTip()
    {
        closeButton.ToolTip = Prefs.CloseToTray ? "Close to tray" : "Quit " + Radio.Name;
    }

    // the track's 36 less the 14 knob and its 3 margin each side
    const double KnobTravel = 16;

    Border Switch(bool on, out Border knob)
    {
        knob = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(7), Margin = new Thickness(3),
                            HorizontalAlignment = HorizontalAlignment.Left, Background = new SolidColorBrush(),
                            RenderTransform = new TranslateTransform() };
        var sw = new Border { Width = 36, Height = 20, CornerRadius = new CornerRadius(10), Child = knob,
                              Background = new SolidColorBrush(), Margin = new Thickness(16, 0, 0, 0),
                              VerticalAlignment = VerticalAlignment.Center };
        SetSwitch(sw, knob, on, false);
        return sw;
    }

    // the knob slides rather than jumps, and the track takes the station's colour as it goes
    void SetSwitch(Border sw, Border knob, bool on, bool animate)
    {
        var time = animate ? Quick : new Duration(TimeSpan.Zero);
        sw.Background.BeginAnimation(SolidColorBrush.ColorProperty,
            new ColorAnimation(on ? accent : Palette.Colour("CHIPHOVER"), time) { EasingFunction = ease });
        knob.Background.BeginAnimation(SolidColorBrush.ColorProperty,
            new ColorAnimation(on ? Color.FromRgb(0x10, 0x10, 0x10) : Palette.Colour("WHITE"), time) { EasingFunction = ease });
        knob.RenderTransform.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(on ? KnobTravel : 0, time) { EasingFunction = ease });
    }

    void RowMenu(PresetRow row, FrameworkElement anchor, Point at)
    {
        int i = row.Index;
        if (i < 0 || i >= stations.Count) return;
        // moves within the station's country, past the neighbour shown beside it
        PresetRow before = Beside(row, -1), after = Beside(row, 1);
        Button back = MenuItem(listView ? "UP" : "LEFT", listView ? "Move up" : "Move left", () => { CloseMenu(); Move(i, before.Index); });
        Button on = MenuItem(listView ? "DOWN" : "RIGHT", listView ? "Move down" : "Move right", () => { CloseMenu(); Move(i, after.Index); });
        back.IsEnabled = before != null;
        on.IsEnabled = after != null;
        OpenMenu(anchor, at, back, on,
                 MenuItem("EDIT", "Edit", () => { CloseMenu(); OpenEditor(i); }),
                 MenuLine(),
                 MenuItem("ERASE", "Erase", () =>
                 {
                     CloseMenu();
                     Ask("Erase " + stations[i].Name + "?", "It disappears from the radio in every mod.", "Erase", () =>
                     {
                         stations.RemoveAt(i);
                         Save();
                     });
                 }, red: true));
    }

    // ---- the editor and the confirm card

    // the dark glaze over a frosted surface, so text stays readable on the blurred logos
    Brush Frosted()
    {
        return new SolidColorBrush(WithAlpha(Palette.Mix(accent, Color.FromRgb(0x16, 0x16, 0x16), 0.2), 0xCC));
    }

    // WPF cannot blur what lies behind an element, so a frosted surface paints a blurred copy
    // of Main under its glaze, kept lined up with wherever the surface sits
    void Frost(string area, string blur, double radius)
    {
        var surface = Find<FrameworkElement>(area);
        var copy = Find<Rectangle>(blur);
        var behind = new VisualBrush(main) { ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill };
        copy.Fill = behind;
        surface.SizeChanged += delegate
        {
            surface.Clip = new RectangleGeometry(new Rect(surface.RenderSize), radius, radius);
        };
        // every frame, not on layout: a panel sliding in moves without a layout pass
        EventHandler lineUp = delegate
        {
            if (!surface.IsVisible) return;
            var at = new Rect(copy.TranslatePoint(new Point(), main), copy.RenderSize);
            if (behind.Viewbox != at) behind.Viewbox = at;
        };
        CompositionTarget.Rendering += lineUp;
        Window.Closed += delegate { CompositionTarget.Rendering -= lineUp; };
    }

    void OpenEditor(int index)
    {
        editing = index;
        Station s = index >= 0 ? stations[index] : new Station();
        editorTitle.Text = index >= 0 ? "Edit station" : "New station";
        nameBox.Text = s.Name;
        countryBox.Text = s.Country;
        genreBox.Text = s.Genre;
        urlBox.Text = s.Url;
        Tell("DIM", "");
        checkButton.IsEnabled = true;
        editorTint.Background = Frosted();
        Reveal(editor, editorPanel, Slide, editorPanel.Width, 0, 1);
        (index >= 0 ? nameBox : urlBox).Focus();
    }

    void SaveEditor()
    {
        string n = nameBox.Text.Trim(), a = urlBox.Text.Trim();
        if (!IsWebAddress(a))
        {
            Tell("RED", NotAnAddress);
            urlBox.Focus();
            return;
        }
        if (n.Length == 0)
        {
            Tell("RED", "Give the station a name.");
            nameBox.Focus();
            return;
        }
        var s = new Station { Name = n, Country = countryBox.Text.Trim(), Genre = genreBox.Text.Trim(), Url = a };
        // the logo stays with the same station; a new address or name is looked up again
        if (editing >= 0 && stations[editing].Url == a && stations[editing].Name == n) s.Logo = stations[editing].Logo;
        if (editing >= 0) stations[editing] = s;
        else stations.Add(s);
        // the list is rebuilt once the panel is gone: rebuilding reloads every logo, which
        // would stall the window through the first frames of the slide-out
        CloseEditor(Save);
    }

    void Tell(string colour, string text)
    {
        checkLed.Fill = Palette.Brush(colour);
        checkLed.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        checkResult.Text = text;
    }

    void Ask(string title, string text, string yes, Action then)
    {
        confirmTitle.Text = title;
        confirmText.Text = text;
        confirmYes.Content = yes;
        confirmed = then;
        confirmTint.Background = Frosted();
        Reveal(confirm, confirmCard, Quick, 0, 8, 0.96);
        confirmYes.Focus();
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        if (IsOpen(menuLayer))
        {
            if (e.Key == Key.Escape) CloseMenu();
            else return;
        }
        else if (IsOpen(confirm))
        {
            if (e.Key == Key.Escape) CloseConfirm();
            else return;
        }
        else if (IsOpen(editor))
        {
            if (e.Key == Key.Escape) CloseEditor();
            else if (e.Key == Key.Enter) SaveEditor();
            else return;
        }
        else if (e.Key == Key.Escape) Window.Close();
        else return;
        e.Handled = true;
    }

    // The example sits where the caret starts. That is past the box's padding by an inset
    // of WPF's own text view, so it is asked for rather than guessed.
    static void AlignExample(TextBox box)
    {
        var example = box.Template.FindName("example", box) as TextBlock;
        Rect caret = box.GetRectFromCharacterIndex(0);
        if (example == null || caret.IsEmpty) return;
        // less the template border's 1, the example being inside it
        example.Margin = new Thickness(caret.X - 1, 0, 0, 0);
    }

    const string NotAnAddress = "The address has to start with http:// or https://";

    static bool IsWebAddress(string address)
    {
        Uri u;
        return Uri.TryCreate(address, UriKind.Absolute, out u) && (u.Scheme == "http" || u.Scheme == "https");
    }

    // Windows' own wording ("The remote name could not be resolved") put plainly
    static string Explain(Exception e)
    {
        var web = e as WebException;
        if (web == null) return "It could not be reached.";
        var answer = web.Response as HttpWebResponse;
        if (answer != null)
            return (int)answer.StatusCode == 404 ? "Nothing at that address (404)."
                                                 : "The server turned it down (" + (int)answer.StatusCode + ").";
        switch (web.Status)
        {
            case WebExceptionStatus.NameResolutionFailure: return "That server can't be found.";
            case WebExceptionStatus.Timeout: return "No answer within 10 seconds.";
            case WebExceptionStatus.ConnectFailure: return "The server isn't answering.";
            case WebExceptionStatus.TrustFailure:
            case WebExceptionStatus.SecureChannelFailure: return "Its secure connection failed.";
            default: return "It could not be reached.";
        }
    }

    // on its own thread, so the window stays usable while a slow station answers
    void CheckLink()
    {
        string address = urlBox.Text.Trim();
        if (!IsWebAddress(address))
        {
            Tell("RED", NotAnAddress);
            urlBox.Focus();
            return;
        }
        checkButton.IsEnabled = false;
        Tell("AMBER", "Testing...");
        var t = new Thread(() =>
        {
            string verdict, icyName = null, icyGenre = null;
            bool good;
            try { good = Probe(address, out verdict, out icyName, out icyGenre); }
            catch (Exception e)
            {
                good = false;
                verdict = Explain(e);
            }
            Window.Dispatcher.BeginInvoke((Action)(() =>
            {
                checkButton.IsEnabled = true;
                Tell(good ? "GREEN" : "RED", verdict);
                if (good && nameBox.Text.Trim().Length == 0 && !string.IsNullOrEmpty(icyName)) nameBox.Text = icyName.Trim();
                if (good && genreBox.Text.Trim().Length == 0 && !string.IsNullOrEmpty(icyGenre)) genreBox.Text = icyGenre.Trim();
            }));
        });
        t.IsBackground = true;
        t.Start();
    }

    // the station's own name and genre come back too, for an empty form to take
    static bool Probe(string address, out string verdict, out string icyName, out string icyGenre)
    {
        icyName = icyGenre = null;
        var req = (HttpWebRequest)WebRequest.Create(address);
        req.Headers["Icy-MetaData"] = "1";
        req.UserAgent = "FastLaneRadio/1.0";
        req.Timeout = 10000;
        req.ReadWriteTimeout = 10000;
        try
        {
            using (var resp = (HttpWebResponse)req.GetResponse())
            {
                string type = (resp.ContentType ?? "").ToLowerInvariant();
                icyName = resp.Headers["icy-name"];
                icyGenre = resp.Headers["icy-genre"];
                string kbps = resp.Headers["icy-br"];
                if (type.StartsWith("text/html"))
                {
                    verdict = "That is a web page, not a stream.";
                    return false;
                }
                if (!type.StartsWith("audio/") && !type.Contains("mpegurl") && !type.Contains("ogg"))
                {
                    verdict = "Unknown format (" + type + "). It may not play.";
                    return false;
                }
                verdict = "Stream works: " + Format(type) + (string.IsNullOrEmpty(kbps) ? "" : ", " + kbps + " kbps");
                return true;
            }
        }
        finally
        {
            req.Abort();
        }
    }

    // the format by the name players know it by, not its media type
    static string Format(string type)
    {
        if (type.Contains("mpegurl")) return "HLS";
        if (type.Contains("mpeg") || type.Contains("mp3")) return "MP3";
        if (type.Contains("aac")) return "AAC";
        if (type.Contains("ogg")) return "Ogg";
        return type;
    }

    const string Xaml = @"
<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
        xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
        xmlns:shell='clr-namespace:System.Windows.Shell;assembly=PresentationFramework'
        BorderBrush='@LINE@' BorderThickness='1'
        TextElement.FontFamily='Segoe UI' TextElement.FontSize='14' TextElement.Foreground='@TEXT@'
        UseLayoutRounding='True' SnapsToDevicePixels='True' TextOptions.TextFormattingMode='Display'>
  <Border.Resources>
    <SolidColorBrush x:Key='ChipHover' Color='@CHIPHOVER@'/>
    <SolidColorBrush x:Key='FillHover' Color='@FILLHOVER@'/>
    <SolidColorBrush x:Key='CloseHover' Color='@CLOSEHOVER@'/>
    <SolidColorBrush x:Key='RedHover' Color='@REDHOVER@'/>
    <SolidColorBrush x:Key='WhiteHover' Color='#FFFFFF'/>

    <!-- every button: a rounded face whose colour the hover swaps for the one in Tag -->
    <Style x:Key='Chip' TargetType='Button'>
      <Setter Property='Foreground' Value='@WHITE@'/>
      <Setter Property='Background' Value='@CHIP@'/>
      <Setter Property='Padding' Value='13,6'/>
      <Setter Property='MinHeight' Value='32'/>
      <Setter Property='MinWidth' Value='34'/>
      <Setter Property='FontSize' Value='14'/>
      <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
      <Setter Property='Tag' Value='{StaticResource ChipHover}'/>
      <Setter Property='shell:WindowChrome.IsHitTestVisibleInChrome' Value='True'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='Button'>
            <Border x:Name='b' Background='{TemplateBinding Background}' CornerRadius='6' Padding='{TemplateBinding Padding}'>
              <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'>
                <Setter TargetName='b' Property='Background' Value='{Binding Tag, RelativeSource={RelativeSource TemplatedParent}}'/>
              </Trigger>
              <Trigger Property='IsPressed' Value='True'>
                <Setter TargetName='b' Property='RenderTransform'>
                  <Setter.Value><TranslateTransform Y='1'/></Setter.Value>
                </Setter>
              </Trigger>
              <Trigger Property='IsEnabled' Value='False'>
                <Setter TargetName='b' Property='Opacity' Value='0.35'/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key='Caption' TargetType='Button' BasedOn='{StaticResource Chip}'>
      <Setter Property='Background' Value='Transparent'/>
      <Setter Property='Width' Value='46'/>
      <Setter Property='Height' Value='36'/>
      <Setter Property='Padding' Value='0'/>
      <Setter Property='Tag' Value='{StaticResource FillHover}'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='Button'>
            <Border x:Name='b' Background='{TemplateBinding Background}'>
              <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'>
                <Setter TargetName='b' Property='Background' Value='{Binding Tag, RelativeSource={RelativeSource TemplatedParent}}'/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <!-- Windows' own title-bar glyphs, at the size its own windows draw them -->
    <Style x:Key='CaptionGlyph' TargetType='TextBlock'>
      <Setter Property='FontFamily' Value='Segoe MDL2 Assets'/>
      <Setter Property='FontSize' Value='10'/>
      <Setter Property='Foreground' Value='#CCFFFFFF'/>
    </Style>
    <Style x:Key='Primary' TargetType='Button' BasedOn='{StaticResource Chip}'>
      <Setter Property='Foreground' Value='@INK@'/>
      <Setter Property='Background' Value='@WHITE@'/>
      <Setter Property='Tag' Value='{StaticResource WhiteHover}'/>
    </Style>
    <Style x:Key='Danger' TargetType='Button' BasedOn='{StaticResource Chip}'>
      <Setter Property='Foreground' Value='White'/>
      <Setter Property='Background' Value='@REDSOLID@'/>
      <Setter Property='Tag' Value='{StaticResource RedHover}'/>
    </Style>
    <Style x:Key='MenuRow' TargetType='Button' BasedOn='{StaticResource Chip}'>
      <Setter Property='Background' Value='Transparent'/>
      <Setter Property='Padding' Value='10,7'/>
      <Setter Property='HorizontalContentAlignment' Value='Stretch'/>
      <Setter Property='Tag' Value='{StaticResource FillHover}'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='Button'>
            <Border x:Name='b' Background='{TemplateBinding Background}' CornerRadius='5' Padding='{TemplateBinding Padding}'>
              <ContentPresenter VerticalAlignment='Center'/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'>
                <Setter TargetName='b' Property='Background' Value='{Binding Tag, RelativeSource={RelativeSource TemplatedParent}}'/>
              </Trigger>
              <Trigger Property='IsEnabled' Value='False'>
                <Setter TargetName='b' Property='Opacity' Value='0.35'/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <!-- round: the tile's menu button, previous / next, and the bigger play -->
    <Style x:Key='Round' TargetType='Button' BasedOn='{StaticResource Chip}'>
      <Setter Property='Background' Value='@ROUND@'/>
      <Setter Property='Width' Value='30'/>
      <Setter Property='Height' Value='30'/>
      <Setter Property='MinHeight' Value='0'/>
      <Setter Property='MinWidth' Value='0'/>
      <Setter Property='Padding' Value='0'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='Button'>
            <Border x:Name='b' Background='{TemplateBinding Background}' CornerRadius='99'>
              <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'>
                <Setter TargetName='b' Property='Background' Value='{Binding Tag, RelativeSource={RelativeSource TemplatedParent}}'/>
              </Trigger>
              <Trigger Property='IsPressed' Value='True'>
                <Setter TargetName='b' Property='RenderTransform'>
                  <Setter.Value><TranslateTransform Y='1'/></Setter.Value>
                </Setter>
              </Trigger>
              <Trigger Property='IsEnabled' Value='False'>
                <Setter TargetName='b' Property='Opacity' Value='0.35'/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>

    <Style TargetType='TextBox'>
      <Setter Property='Foreground' Value='@TEXT@'/>
      <Setter Property='Background' Value='@FILL@'/>
      <Setter Property='CaretBrush' Value='@WHITE@'/>
      <Setter Property='SelectionBrush' Value='#66FFFFFF'/>
      <Setter Property='FontSize' Value='15'/>
      <Setter Property='Height' Value='36'/>
      <Setter Property='Padding' Value='8,0'/>
      <Setter Property='VerticalContentAlignment' Value='Center'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='TextBox'>
            <Border x:Name='b' Background='{TemplateBinding Background}' BorderBrush='Transparent' BorderThickness='1' CornerRadius='6'>
              <Grid>
                <TextBlock x:Name='example' Text='{TemplateBinding Tag}' Foreground='#59FFFFFF' Margin='10,0,0,0'
                           VerticalAlignment='Center' IsHitTestVisible='False' Visibility='Collapsed'/>
                <ScrollViewer x:Name='PART_ContentHost' VerticalAlignment='Center' Margin='{TemplateBinding Padding}'/>
              </Grid>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='Text' Value=''>
                <Setter TargetName='example' Property='Visibility' Value='Visible'/>
              </Trigger>
              <Trigger Property='IsKeyboardFocused' Value='True'>
                <Setter TargetName='b' Property='BorderBrush' Value='#80FFFFFF'/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>

    <Style TargetType='ScrollBar'>
      <Setter Property='Width' Value='6'/>
      <Setter Property='MinWidth' Value='6'/>
      <Setter Property='Margin' Value='0,0,4,0'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='ScrollBar'>
            <Track x:Name='PART_Track' IsDirectionReversed='True'>
              <Track.Thumb>
                <Thumb>
                  <Thumb.Template>
                    <ControlTemplate TargetType='Thumb'>
                      <Border CornerRadius='3' Background='#59FFFFFF'/>
                    </ControlTemplate>
                  </Thumb.Template>
                </Thumb>
              </Track.Thumb>
            </Track>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>

    <!-- the stand-in for a station without a logo: IcoMoon's radio, on its own 512 square -->
    <DataTemplate x:Key='Radio'>
      <Viewbox>
        <Canvas Width='512' Height='512'>
          <Path Data='@GANTENNA@' Fill='@WHITE@'/>
          <Path Data='@GRADIO@' Fill='@WHITE@'/>
        </Canvas>
      </Viewbox>
    </DataTemplate>

    <DataTemplate x:Key='Tile'>
      <Grid x:Name='cell' Margin='6' Background='Transparent'>
        <Grid.RowDefinitions>
          <RowDefinition Height='118'/>
          <RowDefinition Height='Auto'/>
        </Grid.RowDefinitions>
        <Border x:Name='face' CornerRadius='8' Background='@FILL@' BorderBrush='Transparent' BorderThickness='2'>
          <Grid>
            <Image Source='{Binding Logo}' Width='86' Height='86' Stretch='Uniform' RenderOptions.BitmapScalingMode='HighQuality'/>
            <ContentControl x:Name='noLogo' ContentTemplate='{StaticResource Radio}' Width='56' Height='56'
                            Focusable='False' Visibility='Collapsed'/>
            <Button x:Name='more' Style='{StaticResource Round}' CommandParameter='more' ToolTip='More'
                    HorizontalAlignment='Right' VerticalAlignment='Top' Margin='0,4,4,0' Visibility='Hidden'>
              <Glyph G='MORE' Size='16' Fill='@WHITE@'/>
            </Button>
          </Grid>
        </Border>
        <TextBlock x:Name='name' Grid.Row='1' Text='{Binding Name}' TextAlignment='Center'
                   TextTrimming='CharacterEllipsis' Margin='4,6,4,0'/>
      </Grid>
      <DataTemplate.Triggers>
        <DataTrigger Binding='{Binding Logo}' Value='{x:Null}'>
          <Setter TargetName='noLogo' Property='Visibility' Value='Visible'/>
        </DataTrigger>
        <Trigger SourceName='cell' Property='IsMouseOver' Value='True'>
          <Setter TargetName='more' Property='Visibility' Value='Visible'/>
          <Setter TargetName='face' Property='Background' Value='@FILLHOVER@'/>
        </Trigger>
        <DataTrigger Binding='{Binding IsLive}' Value='True'>
          <Setter TargetName='face' Property='BorderBrush' Value='White'/>
          <Setter TargetName='face' Property='Background' Value='@FILLLIVE@'/>
          <Setter TargetName='name' Property='Foreground' Value='White'/>
        </DataTrigger>
      </DataTemplate.Triggers>
    </DataTemplate>

    <DataTemplate x:Key='Row'>
      <Border x:Name='face' Height='64' Margin='6,0,6,6' CornerRadius='8' Background='@FILL@'
              BorderBrush='Transparent' BorderThickness='2' Padding='8,0,14,0'>
        <DockPanel LastChildFill='True'>
          <Grid DockPanel.Dock='Left' Width='48' Height='48' Margin='0,0,14,0'>
            <Image Source='{Binding Logo}' Stretch='Uniform' RenderOptions.BitmapScalingMode='HighQuality'/>
            <ContentControl x:Name='noLogo' ContentTemplate='{StaticResource Radio}' Width='28' Height='28'
                            Focusable='False' Visibility='Collapsed'/>
          </Grid>
          <Button x:Name='more' DockPanel.Dock='Right' Style='{StaticResource Round}' CommandParameter='more'
                  ToolTip='More' Margin='14,0,0,0' Visibility='Collapsed'>
            <Glyph G='MORE' Size='16' Fill='@WHITE@'/>
          </Button>
          <StackPanel x:Name='live' DockPanel.Dock='Right' Orientation='Horizontal' VerticalAlignment='Center'
                      Visibility='Collapsed'>
            <Viewbox Width='16' Height='16'>
              <Canvas Width='24' Height='24'>
                <Path Data='@GSPEAKER@' Fill='White'/>
                <Path Data='@GWAVES@' Stroke='White' StrokeThickness='2.2' StrokeStartLineCap='Round' StrokeEndLineCap='Round'/>
              </Canvas>
            </Viewbox>
            <TextBlock Text='{Binding LiveText}' Foreground='White' FontSize='13.5' Margin='7,0,0,0'/>
          </StackPanel>
          <StackPanel VerticalAlignment='Center'>
            <TextBlock Text='{Binding Name}' FontSize='15.5' TextTrimming='CharacterEllipsis'/>
            <TextBlock Text='{Binding Genre}' FontSize='13' Foreground='@DIM@' TextTrimming='CharacterEllipsis'/>
          </StackPanel>
        </DockPanel>
      </Border>
      <DataTemplate.Triggers>
        <DataTrigger Binding='{Binding Logo}' Value='{x:Null}'>
          <Setter TargetName='noLogo' Property='Visibility' Value='Visible'/>
        </DataTrigger>
        <Trigger SourceName='face' Property='IsMouseOver' Value='True'>
          <Setter TargetName='more' Property='Visibility' Value='Visible'/>
          <Setter TargetName='face' Property='Background' Value='@FILLHOVER@'/>
        </Trigger>
        <DataTrigger Binding='{Binding IsLive}' Value='True'>
          <Setter TargetName='face' Property='BorderBrush' Value='White'/>
          <Setter TargetName='face' Property='Background' Value='@FILLLIVE@'/>
          <Setter TargetName='live' Property='Visibility' Value='Visible'/>
        </DataTrigger>
      </DataTemplate.Triggers>
    </DataTemplate>

    <Style x:Key='Label' TargetType='TextBlock'>
      <Setter Property='Foreground' Value='@DIM@'/>
      <Setter Property='FontSize' Value='13'/>
      <Setter Property='Margin' Value='0,18,0,6'/>
    </Style>

    <!-- above each country's stations: its name as a Label, with a hairline run out to the edge;
         spaced as a Label is, and in from the side by the tiles' and rows' own 6 -->
    <GroupStyle x:Key='Countries'>
      <GroupStyle.HeaderTemplate>
        <DataTemplate>
          <DockPanel Margin='6,18,6,6' LastChildFill='True'>
            <TextBlock DockPanel.Dock='Left' Style='{StaticResource Label}' Margin='0' Text='{Binding Name}'/>
            <Border Height='1' Background='@LINE@' VerticalAlignment='Center' Margin='12,1,0,0'/>
          </DockPanel>
        </DataTemplate>
      </GroupStyle.HeaderTemplate>
    </GroupStyle>
  </Border.Resources>

  <Grid>
    <Grid x:Name='Main'>
      <Grid.RowDefinitions>
        <RowDefinition Height='36'/>
        <RowDefinition Height='48'/>
        <RowDefinition Height='*'/>
      </Grid.RowDefinitions>

      <Grid>
        <StackPanel Orientation='Horizontal' HorizontalAlignment='Right' VerticalAlignment='Top'>
          <Button x:Name='MinButton' Style='{StaticResource Caption}' ToolTip='Minimize'>
            <TextBlock Style='{StaticResource CaptionGlyph}' Text='&#xE921;'/>
          </Button>
          <Button x:Name='MaxButton' Style='{StaticResource Caption}'>
            <TextBlock x:Name='MaxGlyph' Style='{StaticResource CaptionGlyph}'/>
          </Button>
          <Button x:Name='CloseButton' Style='{StaticResource Caption}'
                  Tag='{StaticResource CloseHover}'>
            <TextBlock Style='{StaticResource CaptionGlyph}' Text='&#xE8BB;'/>
          </Button>
        </StackPanel>
      </Grid>

      <Grid Grid.Row='1' Margin='24,0,24,0'>
        <TextBlock Text='Stations' FontSize='24' FontWeight='SemiBold' VerticalAlignment='Center' Foreground='@WHITE@'/>
        <StackPanel Orientation='Horizontal' HorizontalAlignment='Right' VerticalAlignment='Center'>
          <Button x:Name='SortButton' Style='{StaticResource Chip}' Padding='10,0,8,0' Margin='0,0,8,0' ToolTip='Sort by'>
            <StackPanel Orientation='Horizontal'>
              <Viewbox Width='16' Height='16'>
                <Canvas Width='24' Height='24'><Path Data='@GSORT@' Fill='@WHITE@'/></Canvas>
              </Viewbox>
              <Viewbox Width='13' Height='13' Margin='5,0,0,0'>
                <Canvas Width='24' Height='24'><Path Data='@GCARET@' Fill='#BFFFFFFF'/></Canvas>
              </Viewbox>
            </StackPanel>
          </Button>
          <Button x:Name='ViewButton' Style='{StaticResource Chip}' Padding='10,0,8,0' ToolTip='View'>
            <StackPanel Orientation='Horizontal'>
              <Viewbox Width='16' Height='16'>
                <Canvas Width='24' Height='24'><Path x:Name='ViewIcon' Fill='@WHITE@'/></Canvas>
              </Viewbox>
              <Viewbox Width='13' Height='13' Margin='5,0,0,0'>
                <Canvas Width='24' Height='24'><Path Data='@GCARET@' Fill='#BFFFFFFF'/></Canvas>
              </Viewbox>
            </StackPanel>
          </Button>
          <Button x:Name='AddButton' Style='{StaticResource Chip}' Padding='0' Margin='8,0,0,0' ToolTip='Add station'>
            <Glyph G='PLUS' Size='16' Fill='@WHITE@'/>
          </Button>
          <Button x:Name='MoreButton' Style='{StaticResource Chip}' Padding='0' Margin='8,0,0,0' ToolTip='More'>
            <Glyph G='MORE' Size='16' Fill='@WHITE@'/>
          </Button>
        </StackPanel>
      </Grid>

      <!-- ends where the bar along the bottom begins: nothing scrolls behind it -->
      <ScrollViewer x:Name='Scroller' Grid.Row='2' Margin='18,4,0,52' VerticalScrollBarVisibility='Auto'
                    HorizontalScrollBarVisibility='Disabled' Focusable='False'>
        <Grid Margin='0,0,12,12'>
          <ItemsControl x:Name='Tiles' ItemTemplate='{StaticResource Tile}' Focusable='False'>
            <ItemsControl.ItemsPanel>
              <ItemsPanelTemplate>
                <UniformGrid VerticalAlignment='Top'
                             Columns='{Binding Tag, RelativeSource={RelativeSource AncestorType=ItemsControl}}'/>
              </ItemsPanelTemplate>
            </ItemsControl.ItemsPanel>
            <ItemsControl.GroupStyle><StaticResource ResourceKey='Countries'/></ItemsControl.GroupStyle>
          </ItemsControl>
          <ItemsControl x:Name='Rows' ItemTemplate='{StaticResource Row}' Focusable='False'>
            <ItemsControl.GroupStyle><StaticResource ResourceKey='Countries'/></ItemsControl.GroupStyle>
          </ItemsControl>
        </Grid>
      </ScrollViewer>
      <TextBlock x:Name='Empty' Grid.Row='2' Foreground='@DIM@' HorizontalAlignment='Center' VerticalAlignment='Center'
                 Margin='0,0,0,52' TextAlignment='Center' Text='No stations yet. Add one with + and it shows up in the game.'/>

      <!-- what plays: see-through, so the station's colour runs on under it -->
      <!-- only as tall as its lines: the name, the state and song, the genre; the list above
           ends where it begins, kept so in code as it grows and shrinks -->
      <Border x:Name='Dock' Grid.Row='2' VerticalAlignment='Bottom' Background='#12FFFFFF'
              BorderBrush='@LINE@' BorderThickness='0,1,0,0' Padding='24,8'>
        <DockPanel LastChildFill='True' VerticalAlignment='Center'>
          <Grid x:Name='DockLogoBox' DockPanel.Dock='Left' Width='48' Height='48' Margin='0,0,16,0'>
            <Image x:Name='DockLogo' Stretch='Uniform' RenderOptions.BitmapScalingMode='HighQuality'/>
            <ContentControl x:Name='DockRadio' ContentTemplate='{StaticResource Radio}' Width='32' Height='32' Focusable='False'/>
          </Grid>
          <StackPanel VerticalAlignment='Center'>
            <TextBlock x:Name='DockName' FontSize='24' FontWeight='SemiBold' Foreground='@WHITE@' TextTrimming='CharacterEllipsis'/>
            <DockPanel LastChildFill='True'>
              <StackPanel x:Name='StateRow' DockPanel.Dock='Left' Orientation='Horizontal' Margin='0,0,10,0'>
                <Viewbox Width='14' Height='14'>
                  <Canvas Width='24' Height='24'>
                    <Path x:Name='StateSpeaker' Data='@GSPEAKER@'/>
                    <Path x:Name='StateWaves' Data='@GWAVES@' StrokeThickness='2.2' StrokeStartLineCap='Round' StrokeEndLineCap='Round'/>
                  </Canvas>
                </Viewbox>
                <TextBlock x:Name='StateText' FontSize='13' Margin='6,0,0,0' VerticalAlignment='Center'/>
              </StackPanel>
              <TextBlock x:Name='DockSong' FontSize='13.5' Foreground='@SONG@' TextTrimming='CharacterEllipsis' VerticalAlignment='Center'/>
            </DockPanel>
            <TextBlock x:Name='DockGenre' FontSize='12.5' Foreground='@GENRE@' TextTrimming='CharacterEllipsis'/>
          </StackPanel>
        </DockPanel>
      </Border>
    </Grid>

    <Grid x:Name='MenuLayer' Background='Transparent' Visibility='Collapsed'>
      <Canvas x:Name='MenuCanvas'>
        <Border x:Name='Menu' CornerRadius='8' BorderBrush='@EDGE@' BorderThickness='1'>
          <Border.Effect>
            <DropShadowEffect BlurRadius='24' ShadowDepth='6' Opacity='0.35' Color='Black'/>
          </Border.Effect>
          <Grid x:Name='MenuFrost'>
            <Rectangle x:Name='MenuBlur' Margin='-30'>
              <Rectangle.Effect><BlurEffect Radius='30'/></Rectangle.Effect>
            </Rectangle>
            <Border x:Name='MenuTint'/>
            <Border x:Name='MenuBody' Padding='5'/>
          </Grid>
        </Border>
      </Canvas>
    </Grid>

    <Grid x:Name='Editor' Visibility='Collapsed' Background='@SHADE@'>
      <Border x:Name='EditorPanel' HorizontalAlignment='Right' Width='400' BorderBrush='@EDGE@' BorderThickness='1,0,0,0'>
        <Grid x:Name='EditorFrost'>
        <Rectangle x:Name='EditorBlur' Margin='-30'>
          <Rectangle.Effect><BlurEffect Radius='30'/></Rectangle.Effect>
        </Rectangle>
        <Border x:Name='EditorTint'/>
        <DockPanel LastChildFill='False' Margin='28,24,28,22'>
          <StackPanel DockPanel.Dock='Top'>
            <TextBlock x:Name='EditorTitle' FontSize='22' FontWeight='SemiBold' Foreground='@WHITE@'/>
            <TextBlock Style='{StaticResource Label}' Text='Stream address'/>
            <TextBox x:Name='UrlBox' FontFamily='Consolas' FontSize='13' Tag='https://live.kissfm.ro/kissfm.aacp'/>
            <StackPanel Orientation='Horizontal' Margin='0,12,0,0'>
              <Button x:Name='CheckButton' Style='{StaticResource Chip}' Content='Test signal'/>
              <Ellipse x:Name='CheckLed' Width='7' Height='7' Margin='14,0,0,0' VerticalAlignment='Center' Visibility='Collapsed'/>
              <TextBlock x:Name='CheckResult' Foreground='@SOFT@' Margin='8,0,0,0' VerticalAlignment='Center'
                         TextWrapping='Wrap' MaxWidth='220' FontSize='12.5'/>
            </StackPanel>
            <TextBlock Style='{StaticResource Label}' Text='Name'/>
            <TextBox x:Name='NameBox' FontSize='16'/>
            <TextBlock Style='{StaticResource Label}' Text='Country'/>
            <TextBox x:Name='CountryBox'/>
            <TextBlock Style='{StaticResource Label}' Text='Genre'/>
            <TextBox x:Name='GenreBox'/>
          </StackPanel>
          <StackPanel DockPanel.Dock='Bottom' Orientation='Horizontal' HorizontalAlignment='Right'>
            <Button x:Name='CancelEdit' Style='{StaticResource Chip}' Content='Cancel'/>
            <Button x:Name='StoreButton' Style='{StaticResource Primary}' Content='Save' Margin='10,0,0,0' MinWidth='88'/>
          </StackPanel>
        </DockPanel>
        </Grid>
      </Border>
    </Grid>

    <Grid x:Name='Confirm' Visibility='Collapsed' Background='@SHADE@'>
      <Border x:Name='ConfirmCard' Width='380' BorderBrush='@EDGE@' BorderThickness='1' CornerRadius='10'
              HorizontalAlignment='Center' VerticalAlignment='Center' RenderTransformOrigin='0.5,0.5'>
        <Grid x:Name='ConfirmFrost'>
        <Rectangle x:Name='ConfirmBlur' Margin='-30'>
          <Rectangle.Effect><BlurEffect Radius='30'/></Rectangle.Effect>
        </Rectangle>
        <Border x:Name='ConfirmTint'/>
        <StackPanel Margin='24,20'>
          <TextBlock x:Name='ConfirmTitle' Foreground='@WHITE@' FontWeight='SemiBold' FontSize='18' TextWrapping='Wrap'/>
          <TextBlock x:Name='ConfirmText' Foreground='@SOFT@' Margin='0,6,0,0' TextWrapping='Wrap'/>
          <StackPanel Orientation='Horizontal' HorizontalAlignment='Right' Margin='0,22,0,0'>
            <Button x:Name='ConfirmNo' Style='{StaticResource Chip}' Content='Keep'/>
            <Button x:Name='ConfirmYes' Style='{StaticResource Danger}' Margin='10,0,0,0' MinWidth='88'/>
          </StackPanel>
        </StackPanel>
        </Grid>
      </Border>
    </Grid>
  </Grid>
</Border>";
}

// The window's own choices, each a small file beside the stations.
static class Prefs
{
    // closing the window leaves the radio running in the tray, or quits it
    public static bool CloseToTray
    {
        get { return Get("close", "tray") == "tray"; }
        set { Set("close", value ? "tray" : "quit"); }
    }

    static string File(string name) { return System.IO.Path.Combine(Stations.Folder, name + ".txt"); }

    public static string Get(string name, string fallback)
    {
        try { return System.IO.File.Exists(File(name)) ? System.IO.File.ReadAllText(File(name)).Trim() : fallback; }
        catch (Exception) { return fallback; }
    }

    public static void Set(string name, string value)
    {
        try { System.IO.File.WriteAllText(File(name), value); }
        catch (Exception) { }
    }
}
