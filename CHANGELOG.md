# Changelog

What changed in each release of UE MM, newest first: one short line per change.
The app shows this list (the **Changelog** link in the sidebar's update box), so
every release adds its section at the top, as `## <version> (<yyyy-mm-dd>)`.

## 1.6.1 (2026-11-07)
- Open mod page in each mod's row and details, for a mod whose download kept the mod site's file name; the app reads the mod's number from that name, with no network
- Mods installed with an older version get the link too; other mods show "No mod page", and the mods on the card show neither
- A second download of the same file, whose name ends in (1), now also gets its name and version
- Not possible yet: a real update check. It needs access to the mod site's API, which still waits for approval, so Open mod page is the most the app can do for now

## 1.6.0 (2026-11-07)
- Mods Menu and PBZModLoader can be installed together: Mods Menu 0.9.0 and PBZModLoader 2.1.0 or newer run side by side
- A warning when a known older version of either would cause problems, and PBZModLoader's hid entry stays out beside Mods Menu
- Show files lists the files in the mods folder that the manager did not install
- The UE4SS window shows its note on which release to take only for a download by hand

## 1.5.9 (2026-11-07)
- Filter the mod list by All, Enabled or Disabled; Enabled hides the disabled mods
- Sort the list by Status, Type and Integrity too, not only by name and date
- Tag suggestions under the Tags field: your own tags and common ones, narrowed as you type; Tab completes

## 1.5.8 (2026-11-07)
- Same file names as an installed mod: the install window asks whether to update it or keep both
- Keep both puts the new mod in a folder of its own in ~mods, with a note that only one style may show
- Donate button above the Discord button: fully optional, and its X removes it for good (the About page keeps it)

## 1.5.7 (2026-11-06)
- Tags for your mods: add them at install or with the Add tag button, then filter the list with one click
- An X in the search boxes clears the text, and so does Esc
- Mods with loose files below the game's project folder install whole, such as the images of Quest Navigation
- Mods for PBZModLoader also install from a zip without the ProjectPBZ folder on top
- A .sig file goes with its pak, and a file such as license.json or notice.ini is no longer taken for a document
- The install window warns when a file stays out that the mod may need
- The install window warns about a mod with only a main.ts: PBZModLoader 2.0 cannot use it
- A clearer message when a download has no mod files

## 1.5.6 (2026-11-05)
- PBZModLoader on the recommended card: its own fix for pak mods, no UE4SS needed (thanks, LKF)
- UE4SS C++ BYPASS stays on the card as an extra download
- Upgrade from a PBZModLoader installed by hand: old files go to a backup, mods and settings stay
- Mods for PBZModLoader install, and a zip with a copy for two loaders installs only one copy
- A script mod waits until its loader is installed
- Mods Menu and PBZModLoader: only one of them at a time
- A mod that only shares a file with another mod no longer replaces it
- Scripts named like install.js or notice.js are no longer left out
- No more note before installing Game files or Exe files mods
- Install problems always stay in view; long file notes wrap

## 1.5.5 (2026-11-04)
- Script mods: files below Content/JavaScript now install, such as Mods Menu
- Refresh button on the Mods page, to check the mods folder again without a restart
- Game files and Exe files mods have a blue row, and a new one shows a short note before it installs
- The window no longer goes below its start size

## 1.5.4 (2026-11-04)
- Blue UPDATE on the Mods entry in the sidebar when the recommended mod has a new version

## 1.5.3 (2026-11-04)
- Recommended mod in its own card at the top of the Mods page, with an update check at every start
- One click installs it, and installs or updates UE4SS when the mod needs it
- First mod: UE4SS C++ BYPASS brings script-based pak mods back after the game's new script check (experimental)
- Technical notes on the card: how the mod works and where its source code is

## 1.5.2 (2026-11-03)
- Data, app updates and UE4SS downloads from Cloudflare first, GitHub as backup
- Remote box in the sidebar: where the data came from, with a short explanation
- "Try again" in that window when no data came at start
- Help reports name where the data and the update check came from
- Traditional Chinese: "Open exe folder" names the right folder (thanks, CooperZhuang)

## 1.5.1 (2026-11-02)
- Open folder links in the mod details: each folder of a mod opens in the game

## 1.5.0 (2026-11-02)
- Traditional Chinese (thanks, CooperZhuang)
- More natural Simplified Chinese texts (thanks, CooperZhuang)
- Traditional Chinese in a Taiwan font
- UE4SS log in tabs: UE4SS, Mods and Unnamed
- Known harmless UE4SS lines no longer count as errors
- Keep my version: a mod file changed outside the manager no longer blocks the mod
- Same title size on every page; long contributor lines wrap

## 1.4.1 (2026-11-01)
- Changelog link in the update box: what changed in each version
- A file another program holds gets a clear message, not an error (thanks, CooperZhuang)

## 1.4.0 (2026-11-01)
- Key binding and config files in the game's own folders (thanks, CooperZhuang)
- ReShade, RenoDX and other loader mods next to the game's exe
- The game's own files kept and put back; saves never touched
- A settings .txt next to a pak installs in any folder
- Install window shows where files go and names what it skips

## 1.3.2 (2026-11-01)
- Rename installed mods: blue Edit box in the mod details
- "Optional" hint translated

## 1.3.1 (2026-10-31)
- UE4SS mods with Scripts/config.json get the settings editor

## 1.3.0 (2026-10-31)
- Vietnamese
- Warning for an unofficial UE4SS build
- Mod names stay visible in a narrow window
- More natural Chinese texts

## 1.2.2 (2026-10-30)
- UE4SS folders without code install as they are, for mod settings pages

## 1.2.1 (2026-10-30)
- UE4SS mods with shared Lua libraries install completely
- File tree button for each mod
- Clear deletes the UE4SS log
- Update notes shown formatted

## 1.2.0 (2026-10-30)
- Mod details with every file a mod placed
- Mod sets: turn groups of mods on with one click
- Test the game without mods
- Restore a save backup
- Own settings editor with colours; a .txt next to a pak counts as settings
- UE4SS errors: note when the game closes, red mark in the list, log search
- Simplified Chinese (thanks, Null993)
- Window opens where it was; Ctrl+F and F5

## 1.1.1 (2026-10-29)
- Settings files next to a pak (.json, .ini and more) install with it
- Cleaner names for downloaded mods

## 1.1.0 (2026-10-29)
- First full release
- UE4SS for Phantom Blade Zero with one click
- UE4SS updates keep the settings you changed
- Finds the game's exe with another name, as on other stores

## 1.0.1 (2026-10-29)
- Folders of the released game (Steam)
- Save backups
- Locate game folder accepts only the game's folder

## 1.0.0 (2026-10-28)
- First pre-release
