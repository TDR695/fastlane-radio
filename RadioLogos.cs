// Station logos, found the way a car's head unit finds them: in a station directory - here
// the community-run radio-browser.info - matched by stream address first and by exact name
// second. Each is shrunk to a 128 px PNG named for its content, kept in %APPDATA%\FastLane\Radio\logos
// and published beside the station list. Kept apart from Radio.cs because WPF's imaging
// lives in System.Windows.Media, whose MediaPlayer clashes with WinRT's.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

static class LogoFinder
{
    // "all" is whichever mirror DNS hands out, and a mirror now and then drops the connection
    // mid-handshake; the named ones are tried after it before a lookup counts as offline
    static readonly string[] Mirrors = { "all", "de1", "de2" };
    const int Size = 128;
    // a station website can serve its logo at full print size: Radio Taraf's is 4422 px, 2.4 MB
    const int MaxBytes = 8 * 1024 * 1024;

    static readonly object gate = new object();
    static bool running, again;

    // A station is looked up once: Logo null means not yet, "" means the directory had none.
    // Called again while a pass runs, it runs one more pass after it.
    public static void Start()
    {
        lock (gate)
        {
            if (running)
            {
                again = true;
                return;
            }
            running = true;
        }
        var t = new Thread(Run);
        t.IsBackground = true;
        // WPF's image decoders want an STA thread
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
    }

    static void Run()
    {
        while (true)
        {
            foreach (Station s in Stations.All())
            {
                if (s.Logo != null) continue;
                try { Stations.SetLogo(s, Find(s) ?? ""); }
                catch (Exception) { }   // offline: left unlooked-up, to try on the next start
            }
            lock (gate)
            {
                if (!again)
                {
                    running = false;
                    return;
                }
                again = false;
            }
        }
    }

    // null when the directory has nothing that opens; throws when it cannot be reached
    static string Find(Station s)
    {
        var favicons = new List<string>();
        AddFavicons(favicons, Query("byurl?url=" + Uri.EscapeDataString(s.Url)), s.Name, false);
        AddFavicons(favicons, Query("search?limit=10&order=votes&reverse=true&hidebroken=true&name=" +
                                    Uri.EscapeDataString(s.Name)), s.Name, true);
        foreach (string favicon in favicons)
        {
            byte[] png = null;
            // WebP and SVG logos do not open here, nor do dead links; the next one is tried
            try { png = Shrink(Download(favicon)); }
            catch (Exception) { }
            if (png == null) continue;
            string file = Hash(png) + ".png";
            Directory.CreateDirectory(Stations.LogosDir);
            File.WriteAllBytes(Path.Combine(Stations.LogosDir, file), png);
            return "logos/" + file;
        }
        return null;
    }

    // An entry named exactly as the preset first, then one whose picture is a file called
    // "logo", then the most voted. One stream can be listed under several stations: Dance
    // FM's address is also "Dance FM 89.5 Bucharest", whose picture is its sister station
    // Pro FM's star and which has far more votes. And a website's own icon is not always
    // the station's: Europa FM's favicon is WordPress's, its logo-230x70.png the real one.
    // Searched by name, only exact matches count, or another station's logo turns up.
    static void AddFavicons(List<string> into, object[] results, string name, bool onlyExact)
    {
        var found = new List<Dictionary<string, object>>();
        foreach (object o in results)
        {
            var d = o as Dictionary<string, object>;
            if (d == null) continue;
            if (onlyExact && !Named(d, name)) continue;
            found.Add(d);
        }
        found.Sort((a, b) =>
        {
            int byName = Named(b, name).CompareTo(Named(a, name));
            if (byName != 0) return byName;
            int byFile = IsLogoFile(b).CompareTo(IsLogoFile(a));
            return byFile != 0 ? byFile : Votes(b).CompareTo(Votes(a));
        });
        foreach (var d in found)
        {
            string favicon = Text(d, "favicon").Trim();
            if (favicon.StartsWith("http") && !into.Contains(favicon)) into.Add(favicon);
        }
    }

    static bool IsLogoFile(Dictionary<string, object> d)
    {
        string favicon = Text(d, "favicon");
        int slash = favicon.LastIndexOf('/');
        return favicon.Substring(slash + 1).IndexOf("logo", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static bool Named(Dictionary<string, object> d, string name)
    {
        return string.Equals(Text(d, "name").Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    static string Text(Dictionary<string, object> d, string key)
    {
        object v;
        return d.TryGetValue(key, out v) && v is string ? (string)v : "";
    }

    static long Votes(Dictionary<string, object> d)
    {
        object v;
        if (!d.TryGetValue("votes", out v) || v == null) return 0;
        try { return Convert.ToInt64(v, CultureInfo.InvariantCulture); }
        catch (Exception) { return 0; }
    }

    static object[] Query(string path)
    {
        Exception last = null;
        foreach (string mirror in Mirrors)
        {
            try
            {
                string body = Encoding.UTF8.GetString(
                    Get("https://" + mirror + ".api.radio-browser.info/json/stations/" + path, 15000));
                return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(body) as object[]
                       ?? new object[0];
            }
            catch (WebException e) { last = e; }
        }
        throw last;
    }

    static byte[] Download(string url)
    {
        return Get(url, 10000);
    }

    static byte[] Get(string url, int timeout)
    {
        var req = (HttpWebRequest)WebRequest.Create(url);
        req.UserAgent = "FastLaneRadio/1.0";
        req.Timeout = timeout;
        req.ReadWriteTimeout = timeout;
        req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
        using (var resp = req.GetResponse())
        using (Stream s = resp.GetResponseStream())
        using (var ms = new MemoryStream())
        {
            var buf = new byte[16384];
            int n;
            while ((n = s.Read(buf, 0, buf.Length)) > 0)
            {
                if (ms.Length + n > MaxBytes) throw new InvalidDataException("too large for a logo");
                ms.Write(buf, 0, n);
            }
            return ms.ToArray();
        }
    }

    // the largest picture in the file (an .ico holds several), fitted into the square
    static byte[] Shrink(byte[] data)
    {
        var decoder = BitmapDecoder.Create(new MemoryStream(data), BitmapCreateOptions.PreservePixelFormat,
                                           BitmapCacheOption.OnLoad);
        BitmapFrame best = null;
        foreach (BitmapFrame f in decoder.Frames)
            if (best == null || f.PixelWidth > best.PixelWidth) best = f;
        if (best == null || Math.Min(best.PixelWidth, best.PixelHeight) < 32) return null;

        double scale = Math.Min((double)Size / best.PixelWidth, (double)Size / best.PixelHeight);
        double w = best.PixelWidth * scale, h = best.PixelHeight * scale;
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (DrawingContext dc = visual.RenderOpen())
        {
            if (DarkOnClear(best)) dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, Size, Size));
            dc.DrawImage(best, new Rect((Size - w) / 2, (Size - h) / 2, w, h));
        }
        var bitmap = new RenderTargetBitmap(Size, Size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var ms = new MemoryStream())
        {
            encoder.Save(ms);
            return ms.ToArray();
        }
    }

    // A dark logo on a see-through background - Magic FM's black M - would vanish on a car's
    // dark screen, so it gets the white tile real head units put such logos on. A light or
    // an opaque logo is left as it is.
    static bool DarkOnClear(BitmapSource image)
    {
        var bgra = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        int stride = bgra.PixelWidth * 4;
        var px = new byte[stride * bgra.PixelHeight];
        bgra.CopyPixels(px, stride, 0);
        long clear = 0, solid = 0;
        double light = 0;
        for (int i = 0; i < px.Length; i += 4)
        {
            byte a = px[i + 3];
            if (a < 16) clear++;
            else if (a >= 128)
            {
                solid++;
                light += (0.0722 * px[i] + 0.7152 * px[i + 1] + 0.2126 * px[i + 2]) / 255;
            }
        }
        long all = px.Length / 4;
        return solid > 0 && clear > all / 5 && light / solid < 0.3;
    }

    static string Hash(byte[] data)
    {
        using (var sha = SHA1.Create())
        {
            var sb = new StringBuilder();
            foreach (byte b in sha.ComputeHash(data)) sb.Append(b.ToString("x2"));
            return sb.ToString(0, 16);
        }
    }
}
