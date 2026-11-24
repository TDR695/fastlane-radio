// FastLane Radio - plays internet radio for BeamNG.drive mods, outside the game.
//
// BeamNG cannot reach the internet, so a mod asks this program instead, through files in
// the game's user folder, under settings/fastlane/radio; the game itself never touches
// the network.
//
//   stations.json          written here: the player's own stations, kept in
//       { "stations": [ { "name": "...", "genre": "...", "url": "https://..." } ] }
//       %APPDATA%\FastLane\Radio and edited in this program's window - every mod offers these
//   <mod>.command.json     written by the mod
//       { "beat": n, "play": true, "url": "https://...", "volume": 0..1 }
//       beat must count up at least every 4 seconds while it plays: when it stops (the
//       game closed or crashed, the car is gone), so does the music
//   status.json            written here twice a second
//       { "beat": n, "client": "<mod>", "url": "...", "state": "idle|connecting|playing|error",
//         "title": "Artist - Song", "error": "..." }
//
// One stream plays at a time: of the mods asking, the one that started its stream last.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Playback;
using Windows.Security.Cryptography;

static class Radio
{
    public const string Name = "FastLane Radio";

    [DllImport("user32.dll")]
    static extern bool AllowSetForegroundWindow(int processId);
    const int ASFW_ANY = -1;

    public static string ExePath { get { return Assembly.GetExecutingAssembly().Location; } }

    [MTAThread]
    static void Main(string[] args)
    {
        // Run entries written by 1.0.0/1.0.1 still pass this; open straight to the tray
        bool fromStartup = args.Length > 0 && args[0] == "--startup";
        bool first;
        using (var running = new Mutex(true, @"Local\FastLaneRadio", out first))
        using (var quit = new EventWaitHandle(false, EventResetMode.ManualReset, @"Local\FastLaneRadioQuit"))
        using (var show = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\FastLaneRadioShow"))
        {
            // a second copy only brings up the running one's window, which may take the
            // foreground only because this copy, just started by the player, lets it
            if (!first)
            {
                AllowSetForegroundWindow(ASFW_ANY);
                show.Set();
                return;
            }
            quit.Reset();
            ServicePointManager.SecurityProtocol =
                SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;

            Stations.Load();
            Stations.PublishAll();
            Tray.Start(quit);
            if (!fromStartup) StationsWindow.Show(quit);
            new Player(null).Run(quit, show);
            Tray.Stop();
        }
    }
}

static class Places
{
    // where the files below sit inside a user folder
    public const string Folder = @"settings\fastlane\radio";

    public static IEnumerable<string> UserFolders()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string[] parents = {
            Path.Combine(local, @"BeamNG\BeamNG.drive"),
            Path.Combine(local, @"BeamNG\BeamNG.tech"),
            Path.Combine(local, "BeamNG.drive")
        };
        foreach (string parent in parents)
        {
            if (!Directory.Exists(parent)) continue;
            foreach (string dir in Directory.GetDirectories(parent)) yield return dir;
        }
        // for a user folder moved elsewhere: one path per line
        string extra = Path.Combine(Path.GetDirectoryName(Radio.ExePath), "userfolder.txt");
        if (File.Exists(extra))
        {
            foreach (string line in File.ReadAllLines(extra))
            {
                if (line.Trim().Length > 0) yield return line.Trim();
            }
        }
    }

    // JavaScriptSerializer writes & ' < > as & and the like, which BeamNG's JSON reader
    // does not decode - "Dave & Tems" reached the car as "Dave u0026 Tems". Plain is valid JSON.
    static readonly JavaScriptSerializer json = new JavaScriptSerializer();

    public static string Json(object value)
    {
        return json.Serialize(value).Replace("\\u0026", "&").Replace("\\u0027", "'")
                                    .Replace("\\u003c", "<").Replace("\\u003e", ">");
    }

    // replaced whole, so the game never reads half a file
    public static void WriteWhole(string path, string text)
    {
        string tmp = path + ".tmp";
        try
        {
            File.WriteAllText(tmp, text, new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

sealed class Player
{
    const int TickMs = 250;
    const string CommandSuffix = ".command.json";
    static readonly TimeSpan BeatTimeout = TimeSpan.FromSeconds(4);
    static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(5);
    static readonly TimeSpan GiveUpAfter = TimeSpan.FromSeconds(15);
    static readonly string[] GameProcesses = { "BeamNG.drive.x64", "BeamNG.tech.x64" };

    sealed class Client
    {
        public string Beat;
        public DateTime BeatAt = DateTime.MinValue;
        public bool Play;
        public string Url = "";
        public double Volume;
        public DateTime StartedAt;
    }

    readonly object gate = new object();
    // taken before gate, never inside it: the player's events take gate
    readonly object mediaGate = new object();
    readonly JavaScriptSerializer json = new JavaScriptSerializer();
    readonly Dictionary<string, Client> clients = new Dictionary<string, Client>(StringComparer.OrdinalIgnoreCase);
    readonly string fixedFolder;

    MediaPlayer media;
    StreamFeed feed;
    string folder;
    DateTime nextScan = DateTime.MinValue, nextStatus = DateTime.MinValue;
    double volume = -1;
    long statusBeat;

    // guarded by gate: the player's events and the feed arrive on their own threads
    int generation;
    string playingClient, playingUrl;
    string state = "idle", title = "", error = "";
    DateTime retryAt = DateTime.MaxValue, startedAt;
    bool everPlayed;

    // a folder given here is used as is, game running or not
    public Player(string fixedFolder)
    {
        this.fixedFolder = fixedFolder;
    }

    // show: another copy was started, which asks for the stations window
    public void Run(EventWaitHandle quit, WaitHandle show)
    {
        WaitHandle[] waits = { quit, show };
        while (true)
        {
            int woke = WaitHandle.WaitAny(waits, TickMs);
            if (woke == 0) break;
            if (woke == 1) StationsWindow.Show(quit);
            try { Tick(); }
            catch (Exception e) { lock (gate) error = e.Message; }
        }
        Stop();
    }

    void Tick()
    {
        DateTime now = DateTime.UtcNow;
        if (now >= nextScan)
        {
            nextScan = now.AddSeconds(5);
            folder = fixedFolder ?? (GameRunning() ? FindFolder() : null);
            if (folder != null) Stations.Publish(folder);
        }
        if (folder == null || !Directory.Exists(folder))
        {
            Stop();
            Report(false);
            return;
        }

        ReadCommands(now);
        string pickName = null;
        Client pick = null;
        foreach (KeyValuePair<string, Client> kv in clients)
        {
            Client c = kv.Value;
            if (!c.Play || c.Url.Length == 0 || now - c.BeatAt > BeatTimeout) continue;
            if (pick == null || c.StartedAt > pick.StartedAt)
            {
                pick = c;
                pickName = kv.Key;
            }
        }

        if (pick == null) Stop();
        else
        {
            bool failed, silent;
            lock (gate)
            {
                failed = state == "error" && now >= retryAt;
                // a dead address can leave the player opening for minutes without failing
                silent = state == "connecting" && !everPlayed && now - startedAt > GiveUpAfter;
            }
            if (silent) Fail("the station did not answer");
            if (pick.Url != playingUrl || pickName != playingClient || failed) Start(pickName, pick.Url);
            if (Math.Abs(pick.Volume - volume) > 0.001)
            {
                volume = pick.Volume;
                media.Volume = volume;
            }
        }

        if (now >= nextStatus)
        {
            nextStatus = now.AddMilliseconds(500);
            WriteStatus();
        }
        Report(true);
    }

    // what the stations window shows, read from its own thread. NowKind is "off" while
    // the game is not running, otherwise the stream's state.
    public static volatile string NowKind = "off";
    public static volatile string NowUrl = "";
    public static volatile string NowTitle = "";

    void Report(bool gameFound)
    {
        lock (gate)
        {
            NowKind = gameFound ? state : "off";
            NowUrl = playingUrl ?? "";
            NowTitle = title;
        }
    }

    static bool GameRunning()
    {
        foreach (string name in GameProcesses)
        {
            Process[] found = Process.GetProcessesByName(name);
            foreach (Process p in found) p.Dispose();
            if (found.Length > 0) return true;
        }
        return false;
    }

    // the user folder a mod wrote to last, when the game has more than one
    static string FindFolder()
    {
        string best = null;
        DateTime bestTime = DateTime.MinValue;
        foreach (string root in Places.UserFolders())
        {
            string dir = Path.Combine(root, Places.Folder);
            if (!Directory.Exists(dir)) continue;
            foreach (string file in Directory.GetFiles(dir, "*" + CommandSuffix))
            {
                DateTime t = File.GetLastWriteTimeUtc(file);
                if (t > bestTime)
                {
                    bestTime = t;
                    best = dir;
                }
            }
        }
        return best;
    }

    void ReadCommands(DateTime now)
    {
        foreach (string path in Directory.GetFiles(folder, "*" + CommandSuffix))
        {
            Dictionary<string, object> d = ReadJson(path);
            if (d == null) continue;
            string name = Path.GetFileName(path);
            name = name.Substring(0, name.Length - CommandSuffix.Length);

            Client c;
            bool seen = clients.TryGetValue(name, out c);
            if (!seen)
            {
                c = new Client();
                clients[name] = c;
            }
            // a file left by the last session is not a heartbeat: only a beat that moves is
            string beat = Convert.ToString(Value(d, "beat"), CultureInfo.InvariantCulture);
            if (beat != c.Beat)
            {
                if (seen) c.BeatAt = now;
                c.Beat = beat;
            }

            bool play = Value(d, "play") is bool && (bool)Value(d, "play");
            string url = (Value(d, "url") as string ?? "").Trim();
            if (play && (!c.Play || url != c.Url)) c.StartedAt = now;
            c.Play = play;
            c.Url = url;
            c.Volume = Math.Max(0, Math.Min(1, ToDouble(Value(d, "volume"))));
        }
    }

    static object Value(Dictionary<string, object> d, string key)
    {
        object v;
        return d.TryGetValue(key, out v) ? v : null;
    }

    static double ToDouble(object v)
    {
        try { return v == null ? 0 : Convert.ToDouble(v, CultureInfo.InvariantCulture); }
        catch (Exception) { return 0; }
    }

    // shared, because the game may be writing it at the same moment
    Dictionary<string, object> ReadJson(string path)
    {
        try
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(fs, Encoding.UTF8))
            {
                return json.DeserializeObject(reader.ReadToEnd()) as Dictionary<string, object>;
            }
        }
        catch (Exception) { return null; }
    }

    void EnsureMedia()
    {
        if (media != null) return;
        media = new MediaPlayer();
        // kept off the Windows media overlay that the keyboard's volume keys bring up
        media.CommandManager.IsEnabled = false;
        media.AudioCategory = MediaPlayerAudioCategory.Media;
        media.MediaFailed += OnFailed;
        media.MediaEnded += OnEnded;
        media.PlaybackSession.PlaybackStateChanged += OnState;
        volume = -1;
    }

    void Start(string client, string url)
    {
        EnsureMedia();
        int gen;
        lock (gate)
        {
            gen = ++generation;
            playingClient = client;
            playingUrl = url;
            state = "connecting";
            title = "";
            error = "";
            retryAt = DateTime.MaxValue;
            startedAt = DateTime.UtcNow;
            everPlayed = false;
        }
        Detach();
        try
        {
            if (new Uri(url).AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase))
            {
                PlayDirect(gen, url);
                return;
            }
            feed = new StreamFeed(url,
                source => Play(gen, MediaSource.CreateFromMediaStreamSource(source)),
                () => PlayDirect(gen, url),
                t => { lock (gate) if (gen == generation) title = t; },
                why => { if (Current(gen)) Fail(why); });
            feed.Start();
        }
        catch (Exception e)
        {
            Fail(e.Message);
        }
    }

    // HLS, and any format StreamFeed does not cut into frames, go to the player as an address
    void PlayDirect(int gen, string url)
    {
        Play(gen, MediaSource.CreateFromUri(new Uri(url)));
    }

    // called from the feed's thread too, so a stream that was stopped meanwhile stays stopped
    void Play(int gen, MediaSource source)
    {
        lock (mediaGate)
        {
            if (!Current(gen)) return;
            media.Source = source;
            media.Play();
        }
    }

    void Detach()
    {
        if (feed != null) feed.Stop();
        feed = null;
        if (media == null) return;
        lock (mediaGate)
        {
            media.Pause();
            media.Source = null;
        }
    }

    void Stop()
    {
        lock (gate)
        {
            if (playingUrl == null) return;
            generation++;
            playingClient = null;
            playingUrl = null;
            state = "idle";
            title = "";
        }
        Detach();
    }

    void Fail(string why)
    {
        lock (gate)
        {
            if (playingUrl == null) return;
            state = "error";
            error = why ?? "";
            retryAt = DateTime.UtcNow + RetryAfter;
        }
    }

    void OnFailed(MediaPlayer sender, MediaPlayerFailedEventArgs e) { Fail(e.ErrorMessage); }

    // A live stream has no end, so an end is the station dropping out. One arriving before
    // the stream now asked for has played is the last station's, closed by a change.
    void OnEnded(MediaPlayer sender, object e)
    {
        lock (gate) if (!everPlayed) return;
        Fail("the stream ended");
    }

    void OnState(MediaPlaybackSession session, object e)
    {
        MediaPlaybackState s = session.PlaybackState;
        lock (gate)
        {
            if (playingUrl == null || state == "error") return;
            if (s == MediaPlaybackState.Playing)
            {
                state = "playing";
                everPlayed = true;
            }
            // a live stream buffers for a moment now and then; that is not connecting again
            else if (!everPlayed && (s == MediaPlaybackState.Opening || s == MediaPlaybackState.Buffering))
                state = "connecting";
        }
    }

    bool Current(int gen)
    {
        lock (gate) return gen == generation;
    }

    void WriteStatus()
    {
        var d = new Dictionary<string, object>();
        lock (gate)
        {
            d["beat"] = ++statusBeat;
            d["client"] = playingClient ?? "";
            d["url"] = playingUrl ?? "";
            d["state"] = state;
            d["title"] = title;
            d["error"] = error;
        }
        Places.WriteWhole(Path.Combine(folder, "status.json"), Places.Json(d));
    }
}

sealed class Station
{
    public Station()
    {
        Name = Country = Genre = Url = "";
    }

    public string Name { get; set; }
    // where the station is from; the stations window groups by it
    public string Country { get; set; }
    public string Genre { get; set; }
    public string Url { get; set; }
    // "logos/<file>.png" beside the list; "" when the directory had none, null when not yet looked up
    public string Logo { get; set; }

    public Station Copy()
    {
        return new Station { Name = Name, Country = Country, Genre = Genre, Url = Url, Logo = Logo };
    }
}

// The player's stations: kept in %APPDATA%\FastLane\Radio\stations.json, edited in the window,
// and copied into every BeamNG user folder for the mods to read, logos alongside.
static class Stations
{
    static readonly object gate = new object();
    static readonly JavaScriptSerializer json = new JavaScriptSerializer();
    static List<Station> list = new List<Station>();
    static volatile int version;

    public static string Folder
    {
        get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FastLane", "Radio"); }
    }

    static string FilePath { get { return Path.Combine(Folder, "stations.json"); } }

    public static string LogosDir { get { return Path.Combine(Folder, "logos"); } }

    // goes up on every change, so the window can tell its rows are out of date
    public static int Version { get { return version; } }

    static List<Station> Defaults()
    {
        return new List<Station> {
            new Station { Name = "100% Radio 80", Country = "France", Genre = "80s", Url = "http://100radio-80.ice.infomaniak.ch/100radio-80-128.mp3", Logo = "logos/10755670194adc46.png" },
            new Station { Name = "1LIVE", Country = "Germany", Genre = "Pop, Rock", Url = "https://wdr-1live-live.icecastssl.wdr.de/wdr/1live/live/mp3/128/stream.mp3", Logo = "logos/205aa23228070c92.png" },
            new Station { Name = "80s Forever", Country = "Switzerland", Genre = "80s", Url = "https://premium.shoutcastsolutions.com/radio/8050/256.mp3", Logo = "logos/f31e2c1bfe24dbd0.png" },
            new Station { Name = "90lar", Country = "Turkey", Genre = "90s, Turkish Pop", Url = "http://37.247.98.8/stream/166/", Logo = "logos/28532eefe9f4c1cc.png" },
            new Station { Name = "Albania 360 Radio", Country = "Albania", Genre = "Pop", Url = "https://radio.albania360.net/listen/albania_360_radio/radio.mp3", Logo = "logos/0614b39217b1e4eb.png" },
            new Station { Name = "Arrow Classic Rock", Country = "Netherlands", Genre = "Classic Rock", Url = "http://stream.gal.io/arrow", Logo = "logos/2f0ebf918bc7f5d6.png" },
            new Station { Name = "Avto Radio", Country = "Bulgaria", Genre = "Pop, Rock", Url = "https://playerservices.streamtheworld.com/api/livestream-redirect/AVTORADIOAAC_L.aac", Logo = "logos/d88ae92aaacf11ac.png" },
            new Station { Name = "Bandit Rock", Country = "Sweden", Genre = "Rock", Url = "http://fm02-ice.stream.khz.se/fm02_mp3", Logo = "logos/8273e4ed991d39b0.png" },
            new Station { Name = "Bayern 3", Country = "Germany", Genre = "Pop", Url = "https://dispatcher.rndfnk.com/br/br3/live/mp3/mid", Logo = "logos/89db4eb95cc9fa10.png" },
            new Station { Name = "BBC Radio 2", Country = "UK", Genre = "Pop, Adult Contemporary", Url = "https://as-hls-ww-live.akamaized.net/pool_74208725/live/ww/bbc_radio_two/bbc_radio_two.isml/bbc_radio_two-audio%3d128000.norewind.m3u8", Logo = "logos/939e0da2056bff90.png" },
            new Station { Name = "Bylgjan", Country = "Iceland", Genre = "Pop", Url = "http://icecast.365net.is:8000/orbbylgjan.aac", Logo = "logos/f3dc1d3f68909ef9.png" },
            new Station { Name = "Cadena 100", Country = "Spain", Genre = "Pop", Url = "http://cadena100-streamers-mp3.flumotion.com/cope/cadena100.mp3", Logo = "logos/18e1f7198d40b8f7.png" },
            new Station { Name = "Calypso Radio", Country = "Malta", Genre = "Pop", Url = "https://s4.radio.co/sf3aa4c25a/listen", Logo = "logos/5ed9451b91696fa7.png" },
            new Station { Name = "Capital FM", Country = "UK", Genre = "Pop, Top 40", Url = "https://media-ssl.musicradio.com/CapitalMP3", Logo = "logos/203afa335077d9d6.png" },
            new Station { Name = "Classic 21", Country = "Belgium", Genre = "Classic Rock", Url = "http://radios.rtbf.be/classic21-128.mp3", Logo = "logos/047ea8eabbdc2517.png" },
            new Station { Name = "Classic FM", Country = "UK", Genre = "Classical", Url = "http://ice-the.musicradio.com/ClassicFMMP3", Logo = "logos/ad4f9d3b6e0e8516.png" },
            new Station { Name = "Country Radio", Country = "Czechia", Genre = "Country", Url = "http://icecast2.play.cz:8000/country128aac", Logo = "logos/f5bfa8c34c929067.png" },
            new Station { Name = "Dance FM", Country = "Romania", Genre = "Dance, Electronic", Url = "http://edge126.rdsnet.ro:84/profm/dancefm.mp3", Logo = "logos/259ab4df2f29faf7.png" },
            new Station { Name = "De Tijdloze", Country = "Belgium", Genre = "Rock, Classics", Url = "http://icecast.vrtcdn.be/stubru_tijdloze-high.mp3", Logo = "logos/80b0e967acbf9fdf.png" },
            new Station { Name = "Diesi", Country = "Cyprus", Genre = "Greek Pop", Url = "http://r1.cloudskep.com/radio/diesi/icecast.audio", Logo = "logos/0549691ad94dae76.png" },
            new Station { Name = "Digi FM", Country = "Romania", Genre = "News, Talk, Top 40", Url = "http://edge76.rdsnet.ro:84/digifm/digifm.mp3", Logo = "logos/8291d5a08a8c327a.png" },
            new Station { Name = "DR P3", Country = "Denmark", Genre = "Pop, Rock", Url = "http://live-icy.dr.dk/A/A05H.mp3", Logo = "logos/fa7871c8767c19fa.png" },
            new Station { Name = "DR P8 Jazz", Country = "Denmark", Genre = "Jazz", Url = "http://live-icy.dr.dk/A/A22H.mp3", Logo = "logos/fa7871c8767c19fa.png" },
            new Station { Name = "Eldoradio", Country = "Luxembourg", Genre = "Pop", Url = "http://81.92.238.33/", Logo = "logos/d72394dcecee976e.png" },
            new Station { Name = "Energy NRJ Bulgaria", Country = "Bulgaria", Genre = "Pop, Dance", Url = "http://play.global.audio/nrj128", Logo = "logos/01c758c00d1ad768.png" },
            new Station { Name = "Energy Z\u00fcrich", Country = "Switzerland", Genre = "Pop, Dance", Url = "http://broadcast.infomaniak.ch/energyzuerich-high.mp3", Logo = "logos/9df917446c566ecf.png" },
            new Station { Name = "Eska Rock", Country = "Poland", Genre = "Rock", Url = "https://ic2.smcdn.pl/5380-1.mp3", Logo = "logos/7843a6691910e150.png" },
            new Station { Name = "Europa FM", Country = "Romania", Genre = "Pop, News", Url = "https://astreaming.edi.ro:8443/EuropaFM_aac", Logo = "logos/33c6393ded5947cd.png" },
            new Station { Name = "FM104", Country = "Ireland", Genre = "Pop", Url = "https://wg.cdn.tibus.net/fm104MP3128", Logo = "logos/96188fdf9f9e783e.png" },
            new Station { Name = "FM4", Country = "Austria", Genre = "Alternative, Electronic", Url = "https://orf-live.ors-shoutcast.at/fm4-q2a", Logo = "logos/064ba99691ea9bd6.png" },
            new Station { Name = "France Inter", Country = "France", Genre = "News, Talk", Url = "https://icecast.radiofrance.fr/franceinter-midfi.mp3", Logo = "logos/b3d30d4cacd4965e.png" },
            new Station { Name = "Fun Radio", Country = "France", Genre = "Dance, Electro", Url = "http://icecast.funradio.fr/fun-1-44-128", Logo = "logos/96aed1f78f74cd0e.png" },
            new Station { Name = "Fun Radio Slovakia", Country = "Slovakia", Genre = "Pop", Url = "http://stream.funradio.sk:8000/fun128.mp3", Logo = "logos/e541369d300f06bb.png" },
            new Station { Name = "Heart 80s", Country = "UK", Genre = "80s, Pop", Url = "https://media-ssl.musicradio.com/Heart80sMP3", Logo = "logos/a4f04795f53003ad.png" },
            new Station { Name = "Hiper FM", Country = "Portugal", Genre = "Pop, Top 40", Url = "http://195.23.85.126:9500/;", Logo = "logos/a7e5ca2e98e9ad5c.png" },
            new Station { Name = "Hit FM", Country = "Ukraine", Genre = "Pop, Dance", Url = "http://195.95.206.17/HitFM", Logo = "logos/4f66e5d44de84a89.png" },
            new Station { Name = "Hitr\u00e1dio Devades\u00e1tka", Country = "Czechia", Genre = "90s, Pop", Url = "http://ice.abradio.cz/hit90128.mp3", Logo = "logos/7595a61867aa4eb6.png" },
            new Station { Name = "Hitradio \u00d63", Country = "Austria", Genre = "Pop", Url = "https://orf-live.ors-shoutcast.at/oe3-q2a", Logo = "logos/c1a5297b3ee4facc.png" },
            new Station { Name = "I Love Radio", Country = "Germany", Genre = "Top 40, Hip-Hop", Url = "https://streams.ilovemusic.de/iloveradio1.mp3", Logo = "logos/96fea98b6f229eaa.png" },
            new Station { Name = "Jazz Radio", Country = "France", Genre = "Jazz, Soul", Url = "http://jazzradio.ice.infomaniak.ch/jazzradio-high.mp3", Logo = "logos/550f93eac80b6938.png" },
            new Station { Name = "Kiss 92.9", Country = "Greece", Genre = "Pop", Url = "http://netradio.live24.gr/kissfmathens", Logo = "logos/2734cbeb3dad10ba.png" },
            new Station { Name = "Kiss FM", Country = "Romania", Genre = "Top 40", Url = "https://live.kissfm.ro/kissfm.aacp", Logo = "logos/148e26b66d22eb6e.png" },
            new Station { Name = "Kiss Radio", Country = "Czechia", Genre = "Pop", Url = "https://icecast1.play.cz/kissdelta128.mp3", Logo = "logos/bf74eb9847354505.png" },
            new Station { Name = "Kronehit", Country = "Austria", Genre = "Pop, Dance", Url = "https://secureonair.krone.at/kronehit-hp.mp3", Logo = "logos/d4100575a330d443.png" },
            new Station { Name = "L'Essentiel Radio", Country = "Luxembourg", Genre = "Pop, Top 40", Url = "http://lessentielradio.ice.infomaniak.ch/lessentielradio-128.mp3", Logo = "logos/5c980ce5356fef0f.png" },
            new Station { Name = "LOS40", Country = "Spain", Genre = "Top 40, Latin", Url = "https://playerservices.streamtheworld.com/api/livestream-redirect/LOS40.mp3", Logo = "logos/8a3048e5ff0e0148.png" },
            new Station { Name = "Lux FM", Country = "Ukraine", Genre = "Pop", Url = "http://lux.radio.tvstitch.com/kyiv/lux_adv_sd", Logo = "logos/e9f89fff7ab04da5.png" },
            new Station { Name = "Mix Megapol", Country = "Sweden", Genre = "Pop", Url = "https://live-bauerse-fm.sharp-stream.com/mixmegapol_instream_se_mp3", Logo = "logos/28bbfa9254e5f1aa.png" },
            new Station { Name = "NPO Radio 2", Country = "Netherlands", Genre = "Pop, Classic Hits", Url = "https://icecast.omroep.nl/radio2-bb-mp3", Logo = "logos/62eaf3ed32cef68e.png" },
            new Station { Name = "Otvoreni Radio", Country = "Croatia", Genre = "Pop", Url = "http://stream.otvoreni.hr/otvoreni", Logo = "logos/6faaee54fa254f6e.png" },
            new Station { Name = "P4 Norge", Country = "Norway", Genre = "Pop, Hits", Url = "https://p4.p4groupaudio.com/P04_AH", Logo = "logos/2b4c690a6e964c92.png" },
            new Station { Name = "P6 Rock", Country = "Norway", Genre = "Rock, Classic Rock", Url = "https://p6.p4groupaudio.com/P06_MM", Logo = "logos/0d35824bf4833414.png" },
            new Station { Name = "R\u00e1di\u00f3 1", Country = "Hungary", Genre = "Dance, Pop", Url = "https://icast.connectmedia.hu/5201/live.mp3", Logo = "logos/e0f700ee39d419e9.png" },
            new Station { Name = "Radio 1 Slovenia", Country = "Slovenia", Genre = "Pop", Url = "http://live.radio.si/Radio1", Logo = "logos/140b0a5334cfe15c.png" },
            new Station { Name = "Radio 10", Country = "Netherlands", Genre = "Classic Hits", Url = "http://playerservices.streamtheworld.com/api/livestream-redirect/RADIO10.mp3", Logo = "logos/95927ed4ffff898a.png" },
            new Station { Name = "Radio 101", Country = "Croatia", Genre = "Pop, Rock", Url = "http://live.radio101.hr:9531/", Logo = "logos/0af3b8f93481787f.png" },
            new Station { Name = "Radio 538", Country = "Netherlands", Genre = "Dance, Top 40", Url = "https://playerservices.streamtheworld.com/api/livestream-redirect/RADIO538.mp3", Logo = "logos/d9171811cf7170f7.png" },
            new Station { Name = "Radio 80", Country = "Italy", Genre = "80s", Url = "http://str01.fluidstream.net:7020/;stream.mp3", Logo = "logos/2ee5755bc7d75f70.png" },
            new Station { Name = "Radio Antena", Country = "Slovenia", Genre = "Pop, Rock", Url = "https://live.radio.si/Antena", Logo = "logos/5974819bb9ddaafe.png" },
            new Station { Name = "Radio Bob!", Country = "Germany", Genre = "Rock", Url = "http://streams.radiobob.de/bob-national/mp3-192/mediaplayer", Logo = "logos/1b142e4f9957ed5f.png" },
            new Station { Name = "Radio Caroline", Country = "UK", Genre = "Rock, Pop", Url = "http://78.129.202.200:8040/;", Logo = "logos/d865bb3d4312da7b.png" },
            new Station { Name = "R\u00e1dio Comercial", Country = "Portugal", Genre = "Pop", Url = "https://stream-icy.bauermedia.pt/comercial.mp3", Logo = "logos/5260b2ffbde46e24.png" },
            new Station { Name = "Radio Elmag Pop Rock", Country = "Montenegro", Genre = "Pop, Rock", Url = "https://radio.elmag.me/poprock.mp3", Logo = "logos/2b845f40e46c677f.png" },
            new Station { Name = "R\u00e1dio Expres", Country = "Slovakia", Genre = "Pop", Url = "https://stream.bauermedia.sk/128.mp3", Logo = "logos/cd0bc737e92015fb.png" },
            new Station { Name = "Radio Ibiza", Country = "Italy", Genre = "Dance, Electronic", Url = "http://wma08.fluidstream.net:5010/;stream.aac", Logo = "logos/aa7ade88b73a41dc.png" },
            new Station { Name = "Radio Kiss Kiss", Country = "Italy", Genre = "Pop", Url = "http://wma08.fluidstream.net:4610/", Logo = "logos/06bb9274fd5037c2.png" },
            new Station { Name = "R\u00e1dio Ko\u0161ice", Country = "Slovakia", Genre = "Pop, Rock", Url = "http://stream.ecce.sk:8000/radiokosice-128.mp3", Logo = "logos/1f8060997fb7648f.png" },
            new Station { Name = "Radio Maximum", Country = "Ukraine", Genre = "Pop, Rock", Url = "http://icecastdc.luxnet.ua/maximum_mp3_128", Logo = "logos/ced336d54c8b027f.png" },
            new Station { Name = "Radio SWH", Country = "Latvia", Genre = "Rock", Url = "http://80.232.162.149:8000/swh96mp3", Logo = "logos/bc4da711326bc2b8.png" },
            new Station { Name = "Radio Swiss Classic", Country = "Switzerland", Genre = "Classical", Url = "https://stream.srg-ssr.ch/m/rsc_de/mp3_128", Logo = "logos/d95797e25620de42.png" },
            new Station { Name = "Radio Swiss Jazz", Country = "Switzerland", Genre = "Jazz", Url = "https://stream.srg-ssr.ch/m/rsj/mp3_128", Logo = "logos/08bd4b642c6603fb.png" },
            new Station { Name = "Radio Swiss Pop", Country = "Switzerland", Genre = "Pop", Url = "https://stream.srg-ssr.ch/m/rsp/mp3_128", Logo = "logos/284e6c64655dc505.png" },
            new Station { Name = "Radio Veronika", Country = "Bulgaria", Genre = "Pop-Folk", Url = "http://play.global.audio/veronikahi.aac", Logo = "logos/12c225f3ce293577.png" },
            new Station { Name = "Radio Zet", Country = "Poland", Genre = "Pop", Url = "http://zet-net-01.cdn.eurozet.pl:8400/", Logo = "logos/c3d1d7203a09c01b.png" },
            new Station { Name = "Radio ZU", Country = "Romania", Genre = "Top 40", Url = "http://zuicast.digitalag.ro:9420/zu", Logo = "logos/3d6d0022a6eb6c1f.png" },
            new Station { Name = "Radio ZUM", Country = "Moldova", Genre = "Dance, Hits", Url = "https://streams.radiozum.md/listen/zum1/ZUM1", Logo = "logos/3811d75accb6c902.png" },
            new Station { Name = "Radiocentras", Country = "Lithuania", Genre = "Pop, Rock", Url = "https://stream2.rc.lt/rc128.mp3", Logo = "logos/eb5206eea4c5ae3a.png" },
            new Station { Name = "Rai Radio 2", Country = "Italy", Genre = "Pop", Url = "http://icestreaming.rai.it/2.mp3", Logo = "logos/23db03afbc47a566.png" },
            new Station { Name = "R\u00e1s 2", Country = "Iceland", Genre = "Pop, Rock", Url = "http://netradio.ruv.is/ras2.aac", Logo = "logos/0680f7aa7d8e8114.png" },
            new Station { Name = "Relax FM", Country = "Lithuania", Genre = "Pop, Easy Listening", Url = "https://stream1.relaxfm.lt/relaxfm128.mp3", Logo = "logos/de23dea7c6ef327a.png" },
            new Station { Name = "Retro FM Estonia", Country = "Estonia", Genre = "80s, 90s", Url = "https://stream.skymedia.ee/live/RETRO", Logo = "logos/dd8cd3602aa31cdd.png" },
            new Station { Name = "Retro FM Latvia", Country = "Latvia", Genre = "Classic Hits", Url = "https://live.retrofm.lv/retrofmlatvia", Logo = "logos/ec3d9b048fd505bb.png" },
            new Station { Name = "Retro FM Moldova", Country = "Moldova", Genre = "Retro, Oldies", Url = "https://efir.retrofm.md:8000/retro", Logo = "logos/6513b1582231ce65.png" },
            new Station { Name = "Retro R\u00e1di\u00f3", Country = "Hungary", Genre = "Retro, 80s, 90s", Url = "https://icast.connectmedia.hu/5001/live.mp3", Logo = "logos/56ba7881589b3606.png" },
            new Station { Name = "RFM", Country = "Portugal", Genre = "Pop", Url = "https://playerservices.streamtheworld.com/api/livestream-redirect/RFMAAC.aac", Logo = "logos/74b574b8ea4e5e21.png" },
            new Station { Name = "RMF FM", Country = "Poland", Genre = "Pop", Url = "http://195.150.20.242:8000/rmf_fm", Logo = "logos/ea9ef4bb4ab83ee4.png" },
            new Station { Name = "RMF MAXX", Country = "Poland", Genre = "Pop, Dance", Url = "http://rs202-krk.rmfstream.pl/RMFMAXXX48", Logo = "logos/59eb9463588a45f3.png" },
            new Station { Name = "Rock Antenne", Country = "Germany", Genre = "Rock", Url = "http://mp3channels.webradio.rockantenne.de/rockantenne", Logo = "logos/f7ac9ff009d425b5.png" },
            new Station { Name = "Rock FM Cyprus", Country = "Cyprus", Genre = "Rock", Url = "http://live3.istoikona.net:8278/;", Logo = "logos/423e8dbf80222722.png" },
            new Station { Name = "Rock FM Estonia", Country = "Estonia", Genre = "Rock", Url = "https://edge02.cdn.bitflip.ee:8888/rck?_i=5f5ab186", Logo = "logos/dd8cd3602aa31cdd.png" },
            new Station { Name = "Sky Radio Skopje", Country = "North Macedonia", Genre = "Pop, Rock", Url = "https://s1.skyradio.com.mk/;*.mp3", Logo = "logos/ad43d5a7d7eb7854.png" },
            new Station { Name = "Super FM", Country = "Serbia", Genre = "Pop, Hits", Url = "http://onair.superfm.rs/superfm.mp3", Logo = "logos/a5638da1dc13dd30.png" },
            new Station { Name = "S\u00fcper FM", Country = "Turkey", Genre = "Pop", Url = "https://playerservices.streamtheworld.com/api/livestream-redirect/SUPER_FM_SC", Logo = "logos/de82cc8bbc0f6379.png" },
            new Station { Name = "SWR3", Country = "Germany", Genre = "Pop, Rock", Url = "https://liveradio.swr.de/sw282p3/swr3/play.mp3", Logo = "logos/443d503fc46317c1.png" },
            new Station { Name = "The Funky Channel", Country = "Belgium", Genre = "Funk, Soul, Disco", Url = "http://cast3.my-control-panel.com:8170/stream", Logo = "logos/ee8fdd504eba224d.png" },
            new Station { Name = "Today FM", Country = "Ireland", Genre = "Pop, Rock", Url = "https://stream.audioxi.com/TD", Logo = "logos/39d2bc270759ed12.png" },
            new Station { Name = "Tomorrowland One World Radio", Country = "Belgium", Genre = "Dance, Electronic", Url = "https://playerservices.streamtheworld.com/api/livestream-redirect/OWR_INTERNATIONAL_ADP.aac", Logo = "logos/4c63fb3ffbe7b9b8.png" },
            new Station { Name = "Top Albanian Radio", Country = "Albania", Genre = "Albanian Pop", Url = "https://live.top-media.al/tar", Logo = "logos/b99cea57a199708f.png" },
            new Station { Name = "Urban FM", Country = "North Macedonia", Genre = "Pop", Url = "https://securestreams4.autopo.st:1668/stream", Logo = "logos/b17532234060c353.png" },
            new Station { Name = "Virgin Radio Romania", Country = "Romania", Genre = "Pop, Dance", Url = "http://astreaming.virginradio.ro:8000/virgin_aacp_64k", Logo = "logos/1500a3b9047ca1af.png" },
            new Station { Name = "Zeppelin 106.7", Country = "Greece", Genre = "Rock", Url = "https://radiostreaming.ert.gr/ert-zeppelin", Logo = "logos/965106d3b7766276.png" }
        };
    }

    // true on the very first run, when there was no file yet
    public static bool Load()
    {
        lock (gate)
        {
            bool first = !File.Exists(FilePath);
            list = first ? Defaults() : Parse(File.ReadAllText(FilePath)) ?? new List<Station>();
            UnpackBundledLogos();
            if (first) Save();
            return first;
        }
    }

    // The default stations' logos ship inside the exe, so a first run looks nothing up: a new
    // program that reaches a hundred servers on its own the moment it starts reads as malware.
    static void UnpackBundledLogos()
    {
        Assembly exe = Assembly.GetExecutingAssembly();
        foreach (Station s in list)
        {
            if (string.IsNullOrEmpty(s.Logo)) continue;
            string file = Path.GetFileName(s.Logo), path = Path.Combine(LogosDir, file);
            if (File.Exists(path)) continue;
            using (Stream res = exe.GetManifestResourceStream("logos/" + file))
            {
                if (res == null) continue;
                Directory.CreateDirectory(LogosDir);
                using (FileStream f = File.Create(path)) res.CopyTo(f);
            }
        }
    }

    static List<Station> Parse(string text)
    {
        try
        {
            var root = json.DeserializeObject(text) as Dictionary<string, object>;
            object items;
            if (root == null || !root.TryGetValue("stations", out items) || !(items is object[])) return null;
            var result = new List<Station>();
            foreach (object o in (object[])items)
            {
                var d = o as Dictionary<string, object>;
                if (d == null) continue;
                var s = new Station { Name = Field(d, "name"), Country = Field(d, "country"), Genre = Field(d, "genre"), Url = Field(d, "url") };
                // lists from before the country had a field of its own kept it in the genre: "France \u00B7 Pop"
                int k = s.Genre.IndexOf(" \u00B7 ", StringComparison.Ordinal);
                if (s.Country.Length == 0 && k > 0)
                {
                    s.Country = s.Genre.Substring(0, k).Trim();
                    s.Genre = s.Genre.Substring(k + 3).Trim();
                }
                if (d.ContainsKey("logo")) s.Logo = Field(d, "logo");
                if (s.Name.Length > 0 && s.Url.Length > 0) result.Add(s);
            }
            return result;
        }
        catch (Exception) { return null; }
    }

    static string Field(Dictionary<string, object> d, string key)
    {
        object v;
        return d.TryGetValue(key, out v) && v is string ? ((string)v).Trim() : "";
    }

    static string Text()
    {
        var items = new List<object>();
        foreach (Station s in list)
        {
            var d = new Dictionary<string, object> { { "name", s.Name }, { "genre", s.Genre }, { "url", s.Url } };
            if (s.Country.Length > 0) d["country"] = s.Country;
            if (s.Logo != null) d["logo"] = s.Logo;
            items.Add(d);
        }
        return Places.Json(new Dictionary<string, object> { { "stations", items } });
    }

    public static List<Station> All()
    {
        lock (gate) return list.ConvertAll(s => s.Copy());
    }

    public static void Replace(List<Station> stations)
    {
        lock (gate)
        {
            list = stations.ConvertAll(s => s.Copy());
            Save();
        }
        PublishAll();
        LogoFinder.Start();
    }

    // matched by address and name, so an edit made while the logo was being found wins
    public static void SetLogo(Station s, string logo)
    {
        bool changed = false;
        lock (gate)
        {
            foreach (Station x in list)
            {
                if (x.Url != s.Url || x.Name != s.Name || x.Logo != null) continue;
                x.Logo = logo;
                changed = true;
            }
            if (changed) Save();
        }
        if (changed) PublishAll();
    }

    static void Save()
    {
        Directory.CreateDirectory(Folder);
        Places.WriteWhole(FilePath, Text());
        Prune(LogosDir);
        version++;
    }

    static HashSet<string> LogoFiles()
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Station s in list)
            if (!string.IsNullOrEmpty(s.Logo)) files.Add(Path.GetFileName(s.Logo));
        return files;
    }

    // a logo no station points at any more goes
    static void Prune(string logos)
    {
        if (!Directory.Exists(logos)) return;
        HashSet<string> keep = LogoFiles();
        foreach (string f in Directory.GetFiles(logos, "*.png"))
        {
            if (keep.Contains(Path.GetFileName(f))) continue;
            try { File.Delete(f); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    // every BeamNG user folder, so a mod finds the list in whichever one the game runs from
    public static void PublishAll()
    {
        foreach (string root in Places.UserFolders())
        {
            if (Directory.Exists(Path.Combine(root, "settings"))) Publish(Path.Combine(root, Places.Folder));
        }
    }

    // Runs every few seconds while the game does, so the list is written only when it
    // differs. Logos are copied first: the list must never name one that is not there yet.
    public static void Publish(string dir)
    {
        string text;
        HashSet<string> logos;
        lock (gate)
        {
            text = Text();
            logos = LogoFiles();
        }
        try
        {
            string logoDir = Path.Combine(dir, "logos");
            if (logos.Count > 0) Directory.CreateDirectory(logoDir);
            foreach (string f in logos)
            {
                string from = Path.Combine(LogosDir, f), to = Path.Combine(logoDir, f);
                if (File.Exists(from) && !File.Exists(to)) File.Copy(from, to);
            }
            string path = Path.Combine(dir, "stations.json");
            if (!File.Exists(path) || File.ReadAllText(path) != text)
            {
                Directory.CreateDirectory(dir);
                Places.WriteWhole(path, text);
            }
            lock (gate) Prune(logoDir);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

// One connection to the station, read ahead into a queue of whole audio frames that the
// player draws from. Handed the address instead, the Windows player reads only as fast as
// it plays, so when a station's opening burst ran out, about ten seconds in, it stalled.
sealed class StreamFeed
{
    static readonly long StartAfter = TimeSpan.FromSeconds(3).Ticks;
    static readonly long KeepAtMost = TimeSpan.FromSeconds(60).Ticks;
    static readonly TimeSpan GiveUpAfter = TimeSpan.FromSeconds(15);

    sealed class Frame
    {
        public byte[] Data;
        public long Ticks;
    }

    readonly string url;
    readonly Action<MediaStreamSource> ready;
    readonly Action unsupported;
    readonly Action<string> titled;
    readonly Action<string> failed;

    readonly object gate = new object();
    readonly Queue<Frame> queue = new Queue<Frame>();
    long queuedTicks, deliveredTicks;
    bool stopped, started;
    MediaStreamSourceSampleRequest pending;
    MediaStreamSourceSampleRequestDeferral pendingDeferral;
    HttpWebRequest request;
    FrameSplitter splitter;

    public StreamFeed(string url, Action<MediaStreamSource> ready, Action unsupported,
                      Action<string> titled, Action<string> failed)
    {
        this.url = url;
        this.ready = ready;
        this.unsupported = unsupported;
        this.titled = titled;
        this.failed = failed;
    }

    public void Start()
    {
        var t = new Thread(Read);
        t.IsBackground = true;
        t.Start();
    }

    public void Stop()
    {
        HttpWebRequest req;
        lock (gate)
        {
            stopped = true;
            req = request;
            Answer(null);
        }
        if (req != null) req.Abort();
    }

    bool Stopped()
    {
        lock (gate) return stopped;
    }

    // a dropped connection is opened again behind the queue, so a short drop is not heard
    void Read()
    {
        DateTime heard = DateTime.UtcNow;
        string why = "the station did not answer";
        while (!Stopped())
        {
            try
            {
                if (!ReadConnection(ref heard))
                {
                    unsupported();
                    return;
                }
                why = "the station stopped sending";
            }
            catch (Exception e)
            {
                why = e.Message;
            }
            if (Stopped()) return;
            if (DateTime.UtcNow - heard > GiveUpAfter)
            {
                failed(why);
                return;
            }
            Thread.Sleep(1000);
        }
    }

    // false: not a format this cuts into frames
    bool ReadConnection(ref DateTime heard)
    {
        var req = (HttpWebRequest)WebRequest.Create(url);
        req.Headers["Icy-MetaData"] = "1";
        req.UserAgent = "FastLaneRadio/1.0";
        req.Timeout = 10000;
        req.ReadWriteTimeout = 10000;
        lock (gate)
        {
            if (stopped) return true;
            request = req;
        }
        try
        {
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (Stream s = resp.GetResponseStream())
            {
                FrameKind kind = FrameSplitter.KindFor(resp.ContentType);
                if (kind == FrameKind.None) return false;
                if (splitter == null || splitter.Kind != kind) splitter = new FrameSplitter(kind, Add);
                splitter.Restart();

                // Shoutcast/Icecast put the song title in the stream every icy-metaint bytes
                int every;
                if (!int.TryParse(resp.Headers["icy-metaint"], out every) || every < 0) every = 0;
                int toTitle = every;
                var buf = new byte[16384];
                while (!Stopped())
                {
                    int n = s.Read(buf, 0, every > 0 ? Math.Min(buf.Length, toTitle) : buf.Length);
                    if (n <= 0) return true;
                    heard = DateTime.UtcNow;
                    splitter.Push(buf, n);
                    if (every == 0) continue;
                    toTitle -= n;
                    if (toTitle > 0) continue;
                    toTitle = every;
                    int blocks = s.ReadByte();
                    if (blocks < 0) return true;
                    if (blocks == 0) continue;
                    Match m = Regex.Match(Decode(ReadBytes(s, blocks * 16)), "StreamTitle='(.*?)';",
                                          RegexOptions.Singleline);
                    if (m.Success) titled(m.Groups[1].Value.Trim());
                }
                return true;
            }
        }
        finally
        {
            // the stream never ends, so closing it politely would wait for ever
            req.Abort();
        }
    }

    static byte[] ReadBytes(Stream s, int count)
    {
        var buf = new byte[count];
        int got = 0;
        while (got < count)
        {
            int n = s.Read(buf, got, count - got);
            if (n <= 0) throw new EndOfStreamException();
            got += n;
        }
        return buf;
    }

    // stations send UTF-8 or Latin-1 and say neither
    static string Decode(byte[] b)
    {
        int n = Array.IndexOf(b, (byte)0);
        if (n < 0) n = b.Length;
        try { return new UTF8Encoding(false, true).GetString(b, 0, n); }
        catch (DecoderFallbackException) { return Encoding.GetEncoding(28591).GetString(b, 0, n); }
    }

    void Add(byte[] data, long ticks)
    {
        bool startNow = false;
        lock (gate)
        {
            if (stopped) return;
            var f = new Frame { Data = data, Ticks = ticks };
            if (pending != null) Answer(f);
            else
            {
                queue.Enqueue(f);
                queuedTicks += ticks;
                while (queuedTicks > KeepAtMost) Take();
            }
            if (!started && queuedTicks >= StartAfter)
            {
                started = true;
                startNow = true;
            }
        }
        if (startNow) ready(NewSource());
    }

    MediaStreamSource NewSource()
    {
        var source = new MediaStreamSource(new AudioStreamDescriptor(splitter.Properties()));
        source.CanSeek = false;
        // the queue is the buffer
        source.BufferTime = TimeSpan.Zero;
        source.SampleRequested += OnSampleRequested;
        return source;
    }

    void OnSampleRequested(MediaStreamSource source, MediaStreamSourceSampleRequestedEventArgs e)
    {
        lock (gate)
        {
            if (stopped) return;
            if (queue.Count > 0)
            {
                e.Request.Sample = Sample(Take());
                return;
            }
            // the queue ran dry: answered as soon as the next frame arrives
            pending = e.Request;
            pendingDeferral = e.Request.GetDeferral();
        }
    }

    Frame Take()
    {
        Frame f = queue.Dequeue();
        queuedTicks -= f.Ticks;
        return f;
    }

    // null ends the stream
    void Answer(Frame f)
    {
        if (pending == null) return;
        pending.Sample = f == null ? null : Sample(f);
        pendingDeferral.Complete();
        pending = null;
        pendingDeferral = null;
    }

    // Stamped as handed over rather than as received, so a reconnect or a frame dropped
    // from a full queue leaves no hole in the player's timeline.
    MediaStreamSample Sample(Frame f)
    {
        MediaStreamSample s = MediaStreamSample.CreateFromBuffer(CryptographicBuffer.CreateFromByteArray(f.Data),
                                                              TimeSpan.FromTicks(deliveredTicks));
        s.Duration = TimeSpan.FromTicks(f.Ticks);
        deliveredTicks += f.Ticks;
        return s;
    }
}

enum FrameKind { None, Mp3, Adts }

// Cuts the byte stream into whole MP3 or ADTS AAC frames, one per sample handed to the
// player, and keeps the first frame's format to describe the stream to it.
sealed class FrameSplitter
{
    // MPEG audio header tables, indexed by the header's 4-bit bitrate field
    static readonly int[] Mp3V1L3 = { 0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320 };
    static readonly int[] Mp3V1L2 = { 0, 32, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384 };
    static readonly int[] Mp3V2 = { 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160 };
    static readonly int[] Mp3Rates = { 44100, 48000, 32000 };
    static readonly int[] AdtsRates = { 96000, 88200, 64000, 48000, 44100, 32000, 24000, 22050,
                                        16000, 12000, 11025, 8000, 7350 };
    const int HeaderBytes = 8;

    public readonly FrameKind Kind;
    readonly Action<byte[], long> emit;
    byte[] held = new byte[65536];
    int count;
    int rate, channels, kbps;

    public FrameSplitter(FrameKind kind, Action<byte[], long> emit)
    {
        Kind = kind;
        this.emit = emit;
    }

    // "mpegurl" is a playlist, not audio
    public static FrameKind KindFor(string contentType)
    {
        string t = (contentType ?? "").ToLowerInvariant();
        if (t.Contains("mpegurl")) return FrameKind.None;
        if (t.Contains("audio/mpeg") || t.Contains("audio/mp3")) return FrameKind.Mp3;
        if (t.Contains("aac")) return FrameKind.Adts;
        return FrameKind.None;
    }

    // what a dropped connection left half-read is not the start of the next one's frame
    public void Restart()
    {
        count = 0;
    }

    public AudioEncodingProperties Properties()
    {
        if (Kind == FrameKind.Mp3)
            return AudioEncodingProperties.CreateMp3((uint)rate, (uint)channels, (uint)(kbps * 1000));
        return AudioEncodingProperties.CreateAacAdts((uint)rate, (uint)channels, (uint)Math.Max(kbps * 1000, 32000));
    }

    public void Push(byte[] data, int n)
    {
        if (count + n > held.Length) Array.Resize(ref held, Math.Max(held.Length * 2, count + n));
        Buffer.BlockCopy(data, 0, held, count, n);
        count += n;

        int pos = 0;
        while (count - pos >= HeaderBytes)
        {
            int len, samples, r, ch, kb;
            if (!Header(pos, out len, out samples, out r, out ch, out kb))
            {
                pos++;
                continue;
            }
            // a header is only believed when the next frame starts where this one ends
            if (count - pos < len + HeaderBytes) break;
            int len2, samples2, r2, ch2, kb2;
            if (!Header(pos + len, out len2, out samples2, out r2, out ch2, out kb2) || r2 != r)
            {
                pos++;
                continue;
            }
            var frame = new byte[len];
            Buffer.BlockCopy(held, pos, frame, 0, len);
            if (rate == 0)
            {
                rate = r;
                channels = ch;
                kbps = kb;
            }
            emit(frame, samples * TimeSpan.TicksPerSecond / r);
            pos += len;
        }
        Buffer.BlockCopy(held, pos, held, 0, count - pos);
        count -= pos;
    }

    bool Header(int p, out int len, out int samples, out int r, out int ch, out int kb)
    {
        len = samples = r = ch = kb = 0;
        if (held[p] != 0xFF) return false;
        return Kind == FrameKind.Mp3 ? Mp3Header(p, out len, out samples, out r, out ch, out kb)
                                     : AdtsHeader(p, out len, out samples, out r, out ch, out kb);
    }

    // layers II and III; version 3 is MPEG-1, 2 MPEG-2, 0 MPEG-2.5
    bool Mp3Header(int p, out int len, out int samples, out int r, out int ch, out int kb)
    {
        len = samples = r = ch = kb = 0;
        int b1 = held[p + 1], b2 = held[p + 2], b3 = held[p + 3];
        if ((b1 & 0xE0) != 0xE0) return false;
        int ver = (b1 >> 3) & 3, layer = (b1 >> 1) & 3;
        int bi = (b2 >> 4) & 15, si = (b2 >> 2) & 3, pad = (b2 >> 1) & 1;
        if (ver == 1 || (layer != 1 && layer != 2) || bi == 0 || bi == 15 || si == 3) return false;
        bool mpeg1 = ver == 3, layer3 = layer == 1;
        r = Mp3Rates[si] >> (mpeg1 ? 0 : ver == 2 ? 1 : 2);
        kb = (mpeg1 ? (layer3 ? Mp3V1L3 : Mp3V1L2) : Mp3V2)[bi];
        samples = layer3 && !mpeg1 ? 576 : 1152;
        len = (layer3 && !mpeg1 ? 72000 : 144000) * kb / r + pad;
        ch = ((b3 >> 6) & 3) == 3 ? 1 : 2;
        return len > HeaderBytes;
    }

    bool AdtsHeader(int p, out int len, out int samples, out int r, out int ch, out int kb)
    {
        len = samples = r = ch = kb = 0;
        int b1 = held[p + 1], b2 = held[p + 2], b3 = held[p + 3];
        int b4 = held[p + 4], b5 = held[p + 5], b6 = held[p + 6];
        if ((b1 & 0xF6) != 0xF0) return false;
        int si = (b2 >> 2) & 15;
        if (si >= AdtsRates.Length) return false;
        r = AdtsRates[si];
        ch = ((b2 & 1) << 2) | (b3 >> 6);
        if (ch == 0) ch = 2;
        len = ((b3 & 3) << 11) | (b4 << 3) | (b5 >> 5);
        samples = 1024 * ((b6 & 3) + 1);
        kb = len * 8 * r / samples / 1000;
        return len > HeaderBytes;
    }
}
