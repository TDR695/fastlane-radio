# FastLane Radio

Plays internet radio for BeamNG.drive mods. BeamNG can't reach the internet, so a mod asks this small program to play the stream instead. The program runs in the tray and only plays while the game is running and a mod asks it to.

Download it from [Releases](https://github.com/TDR695/fastlane-radio/releases/latest).

## For players

1. Download `radio_fastlane.zip` and unzip it to a folder you'll keep.
2. Double-click `FastLaneRadio.exe` to open its **Stations** window. Closing the window leaves it running in the notification area (the tray) by the clock, with no installer.
3. Click its tray icon, or double-click the exe again, to open the Stations window. Right-click the tray icon to see what's playing, open the window, or power it off.

In the Stations window you can:

- **Add, edit, erase or reorder presets.** Every mod that uses FastLane Radio offers the same list, and a car updates within a couple of seconds, even mid-drive.
- **Test signal** on a stream address before storing it. If the station sends its own name and genre, they fill in empty fields.
- **Choose what closing the window does**: keep the radio running in the tray (Close to tray, the default), or quit.
- **Power off** FastLane Radio.

Your stations are kept in `%APPDATA%\FastLane\Radio\stations.json`. The first run gives you 101 stations from across Europe, in alphabetical order.

**Station logos are found automatically.** When you add a preset, or change its name or address, FastLane Radio looks the station up in the free [radio-browser.info](https://www.radio-browser.info) directory and downloads its logo. This sends the stream address and name to radio-browser.info. Stations it can't find show a radio icon instead.

If you moved your BeamNG user folder, put a `userfolder.txt` next to the exe containing that folder's path.

Needs Windows 10 or 11. Nothing else to install.

## For mod makers

Everything goes through files in the game's user folder, under `settings/fastlane/radio/`.

**`stations.json`** is written by FastLane Radio. It holds the player's stations, the same for every mod:

```json
{ "stations": [ { "name": "Dance FM", "genre": "Dance, Electronic", "url": "http://...", "logo": "logos/62e6b400422ba9e4.png" } ] }
```

- Re-read it every few seconds, so a station the player adds shows up without a restart.
- `logo` is a 128×128 PNG next to the list, at `settings/fastlane/radio/logos/...`. From a vehicle screen, load it as `/settings/fastlane/radio/` plus `logo`. It's an empty string when no logo was found, and missing while the lookup hasn't run yet. Either way, show your own fallback.
- If the file is missing, FastLane Radio has never run on this PC. Say so, instead of showing an empty list.
- Your mod shouldn't ship its own stations. The list belongs to the player.

**`<yourmod>.command.json`** is written by your mod (game-engine Lua `jsonWriteFile`):

```json
{ "beat": 12, "play": true, "url": "https://example.com/stream.mp3", "volume": 0.6 }
```

- `beat` must change at least every 4 seconds while `play` is true. Rewrite the file every second. When the beat stops (the game was closed, your car is gone), the music stops.
- `volume` is 0 to 1.
- Write the file once when your mod loads, even with `play: false`. The program finds the user folder from it.

**`status.json`** is written by FastLane Radio twice a second:

```json
{ "beat": 40, "client": "yourmod", "url": "...", "state": "playing", "title": "Artist - Song", "error": "" }
```

- `state` is one of `idle`, `connecting`, `playing` or `error`. After an error it tries again every 5 seconds.
- `client` and `url` say whose stream this is. Ignore the state when they aren't yours.
- If `beat` stops changing, FastLane Radio isn't running.

One stream plays at a time. If several mods ask, the one that started its stream last is heard.

MP3 and AAC streams (`audio/mpeg`, `audio/aac`) are read ahead by FastLane Radio itself, about 3 seconds before playback starts. A dropped connection is reopened behind that buffer. HLS (`.m3u8`) and any other format go straight to Windows' own player, which can pause to rebuffer now and then. `title` comes from Shoutcast/Icecast metadata in the same connection, and stays empty for stations that don't send any.

## Building

Run `build.cmd`. It uses the C# compiler that ships with Windows (.NET Framework 4.8), so no install is needed. It compiles `Radio.cs` (the player), `RadioWindow.cs` (the Stations window), `RadioLogos.cs` (logo lookup), `RadioTray.cs` (the tray icon) and `AssemblyInfo.cs` (version details), and builds `FastLaneRadio.ico` into the exe. Release builds are made by GitHub Actions from this repository.

Players get `FastLaneRadio.exe` and `README.txt`. The icon is inside the exe, and its font, Segoe UI, comes with Windows 10/11.

## License

GPL-3.0. See [LICENSE](LICENSE). The license covers the code, not the FastLane name or logo: modified or redistributed versions must not be called FastLane Radio.
