FastLane Radio

Plays internet radio for BeamNG.drive mods. BeamNG can't reach the internet,
so a mod asks this small program to play the stream instead. The program runs
in the tray and only plays while the game is running and a mod asks it to.


FOR PLAYERS

1. Download FastLaneRadio.exe and put it anywhere.
2. Double-click it to open its Stations window. Closing the window leaves it
   running in the notification area (the tray) by the clock, with no installer.
3. Click its tray icon, or double-click the exe again, to open the Stations
   window. Right-click the tray icon to see what's playing, open the window,
   or power it off.

In the Stations window you can:

- Add, edit, erase or reorder presets. All mods that use FastLane Radio share
  the same list, and changes show up in the car within a few seconds, even
  while you're driving.
- Test signal on a stream address before storing it. If the station sends its
  own name and genre, they fill in empty fields.
- Choose what closing the window does: keep the radio running in the tray
  (Close to tray, the default), or quit.
- Power off FastLane Radio.

Your stations are kept in %APPDATA%\FastLane\Radio\stations.json. The first
run gives you 101 stations from across Europe, in alphabetical order.

Station logos are found automatically. When you add a preset, or change its
name or address, FastLane Radio looks the station up in the free
radio-browser.info directory (https://www.radio-browser.info) and downloads
its logo. This sends the stream address and name to radio-browser.info.
Stations it can't find show a radio icon instead.

On the first run Windows may say "Windows protected your PC". Click More info,
then Run anyway. Some antivirus programs may also flag or remove the exe
because it's new and not widely downloaded yet. That's a false positive. If
it happens, restore it from your antivirus and add FastLaneRadio.exe as an
exclusion so it doesn't get flagged again.

If the mod says it needs FastLane Radio even though the radio is running,
your BeamNG user folder is probably not in the usual place. The radio looks
for it in %LOCALAPPDATA%\BeamNG\BeamNG.drive\current. If yours is somewhere
else (for example you moved it to another drive in the BeamNG launcher):

1. Find your user folder. It's the folder that contains the "mods" and
   "settings" folders, for example D:\BeamNG\current.
2. Next to FastLaneRadio.exe, create a text file called userfolder.txt.
   Make sure Windows doesn't save it as userfolder.txt.txt.
3. Paste the full path of your user folder into it on one line and save.
4. Restart the radio: right-click its tray icon, choose Power off, then open
   FastLaneRadio.exe again.

Requires Windows 10 or 11.
