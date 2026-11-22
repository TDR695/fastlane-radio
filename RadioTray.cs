// The icon in the notification area while the radio runs: a click opens the stations
// window, a right-click a short menu. WinForms, since WPF has no tray icon of its own; kept
// apart from the window because the two toolkits name some types alike.

using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

static class Tray
{
    // a tray tooltip holds at most this many characters
    const int TipLength = 63;

    static NotifyIcon icon;
    // runs the calls from other threads on the tray's own, which owns the icon
    static Control owner;

    public static void Start(EventWaitHandle quit)
    {
        var ready = new ManualResetEvent(false);
        var t = new Thread(() =>
        {
            owner = new Control();
            owner.CreateControl();
            icon = new NotifyIcon { Icon = AppIcon(), Text = Radio.Name, Visible = true };

            var now = new MenuItem { Enabled = false };
            var open = new MenuItem("Open " + Radio.Name, delegate { StationsWindow.Show(quit); }) { DefaultItem = true };
            var menu = new ContextMenu(new[] {
                now, new MenuItem("-"), open, new MenuItem("-"),
                new MenuItem("Power off", delegate { quit.Set(); })
            });
            menu.Popup += delegate { now.Text = Playing(); };
            icon.ContextMenu = menu;
            icon.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) StationsWindow.Show(quit); };

            var timer = new System.Windows.Forms.Timer { Interval = 1000 };
            timer.Tick += delegate
            {
                string tip = Radio.Name + "\n" + Playing();
                icon.Text = tip.Length > TipLength ? tip.Substring(0, TipLength) : tip;
            };
            timer.Start();
            ready.Set();
            Application.Run();
        });
        t.SetApartmentState(ApartmentState.STA);
        t.IsBackground = true;
        t.Start();
        ready.WaitOne();
    }

    // taken down before the program ends, or Windows leaves a dead icon until the mouse passes over it
    public static void Stop()
    {
        if (owner == null) return;
        owner.Invoke((Action)(() =>
        {
            icon.Visible = false;
            icon.Dispose();
            Application.ExitThread();
        }));
    }

    static string Playing()
    {
        string kind = Player.NowKind, url = Player.NowUrl;
        Station st = string.IsNullOrEmpty(url) ? null : Stations.All().Find(s => s.Url == url);
        if (kind == "off") return "Waiting for BeamNG.drive";
        if (st == null) return "Not playing";
        if (kind == "connecting") return "Tuning in to " + st.Name;
        if (kind == "error") return st.Name + " is not answering";
        return "Playing " + st.Name;
    }

    // the small size, which the icon file draws on its own rather than shrinking the large one
    static Icon AppIcon()
    {
        using (var s = typeof(Tray).Assembly.GetManifestResourceStream("FastLaneRadio.ico"))
        {
            return new Icon(s, SystemInformation.SmallIconSize);
        }
    }
}
