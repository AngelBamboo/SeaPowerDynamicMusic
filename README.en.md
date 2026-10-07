# Sea Power Dynamic Music

[简体中文](README.md) | English

A dynamic background music mod for
[Sea Power: Naval Combat in the Missile Age](https://store.steampowered.com/app/1636790/Sea_Power/).

Switches music automatically between the main menu, strategic map, in-mission, and
mission results. Inside a mission it picks the official tracks for the side you are
playing, or you can replace them all with your own.

## Features

- **Follows game state**. Listens to the game's own scene-switch signals; the music
  changes when the screen does, no manual switching.
- **Official tracks included**. The panel lists the game's own tracks marked
  `[Official]`, reusing audio objects the game has already loaded. Nothing is copied
  and no extra memory is used.
- **One track, many categories**. The same track can belong to both "NATO" and
  "Night" — just tick the boxes in the panel.
- **Weight and priority**. Weight decides the chance of being picked; priority
  decides playback order. Each tier plays through once before dropping to the next.
- **No immediate repeats**. The currently playing track is excluded from the next
  pick, and each track plays once per tier.
- **Graceful fallback**. No Warsaw tracks? NATO covers it. No interface tracks?
  Mission tracks do. Silence never happens.
- **Official music as backup**. If a category has none of your own tracks, the
  matching official music plays instead.
- **One-click vanilla mode**. Hand playback back to the game entirely, and switch
  back whenever you like.
- **In-game panel**. Press F7. Browse, audition, adjust, pause, scrub, rescan.
- **Chinese and English UI**. Toggle in the top-left corner; follows the game
  language the first time it opens.
- **Crossfade between categories**. Two AudioSources cross-fade, so tracks never cut
  off abruptly.

## Installation

### Steam Workshop

1. Install [Anchor Chain](https://steamcommunity.com/sharedfiles/filedetails/?id=3380210757)
   first — it is the community mod loader this mod depends on. Anchor Chain itself
   requires downloading `ACPreloader.zip` from
   [GitHub Releases](https://github.com/SeaPower-Modders/AnchorChain/releases) and
   extracting it into the game root, then subscribing to Anchor Chain on the Workshop.
2. Subscribe to this mod.

### Manual installation

Copy the whole `SeaPowerDynamicMusic` folder into
`Sea Power_Data\StreamingAssets\`, then install Anchor Chain.
Dropping just `SeaPowerDynamicMusic.dll` into `BepInEx\plugins\` also works — it has
its own BepInEx entry point and does not rely on Anchor Chain's config handling.

## Usage

### Music packs

Music can be shipped separately as a pack instead of bundled with the mod.
A pack is just a folder of category directories; the mod scans the Steam
Workshop directory on startup and adds whatever it finds to the library:

```
SeaPowerMusicPack\
  MusicLibrary\
    MainMenu\    Nato\      WP\
    Night\       StrategicMap\  Victory\  Defeat\
```

Keep the `.gitkeep` files in those folders. Workshop zips do not store empty
directories, so removing them loses the category.


Press **F7** in game to open the panel.

### Music library folders

Put your audio files (mp3 / ogg / wav / aiff) into per-category subfolders.
The categories map one-to-one onto the game's `MusicClipData._side`:

```
Sea Power\
  MusicLibrary\
    Nato\          NATO
    WP\            Warsaw Pact
    Night\         Night
    MainMenu\      Main menu
    StrategicMap\  Strategic map
    Victory\       Victory
    Defeat\        Defeat
```

Chinese folder names work too: `北约`, `华约`, `夜间`, `夜晚`.
Tracks that sit directly in the root land in "Unassigned" — they do not play, but
you can still see and re-categorise them in the panel.

Click "Rescan" in the panel afterwards. No restart needed.

If you would rather not move files, list paths in the config file directly:

```ini
[WP]
Track01=D:/Music/wp_battle.mp3
Track02=D:/Music/wp_tension.ogg
```

The `Track01=path` form with the equals sign is required; a bare path on its own
line is dropped when the config is rewritten.

### Config file

Located at
`Sea Power_Data\StreamingAssets\ACConfigs\io.github.angelbamboo.dynamicmusic_user.ini`,
managed by Anchor Chain. Your changes survive mod updates.

The `[Tracks]` section is maintained automatically and records each track's
categories, weight, and priority:

```
T1A2B3C4D=WP,Nato|1.5|2|0|D:/Music/battle.mp3
```

In order: key, category list (comma separated), weight, priority, disabled flag,
file path. Editing this by hand is not recommended — panel changes overwrite it.

`io.github.angelbamboo.dynamicmusic.ini` in the mod folder holds reference defaults
and usually needs no changes.

### Music control

The mod takes over all music playback and blocks the game's native playback, so the
two never overlap. "Vanilla mode" in the panel hands control back at any time.

Official tracks (marked `[Official]`) reuse audio objects the game already loaded.
Nothing is copied and no extra memory is used — **do not** copy official audio files
into the music library yourself.

With "Include official" unticked, official tracks stay out of the shuffle. If a
category has none of your own playable tracks, the matching official music plays
instead.

## Panel

The left side has three columns, narrowing from left to right:

| Level | Contents |
|---|---|
| 1 | Interface / Mission / Result / Unassigned |
| 2 | Main menu, Strategic map / NATO, Warsaw, Night / Victory, Defeat / Unassigned |
| 3 | The tracks in that category |

To the right of the "Scene" heading you can see the current side
(NATO / Warsaw / none); it reads "none" on the strategic map or main menu.

Counts are deduplicated — a track filed under several categories is counted once.

Per track:

| Action | Effect |
|---|---|
| Click the name | Audition; click again to pause or resume |
| Enabled box | Must be ticked to play; unticking resets the weight to zero |
| Weight 0~3 | Chance of being picked within its tier; 0 means excluded |
| Priority 0~5 | Higher plays first; the tier drops only after a full pass |
| Categories | Tick every category the track should appear under |

The status bar at the top shows the current track, its category, and playback
progress. The progress bar can be clicked or dragged; the seek applies on release.

Bottom buttons:

| Button | Effect |
|---|---|
| Save | Write track and global settings to the config file |
| Vanilla mode | Hand playback to the game; press again to take it back |
| Pause / Resume | Pause or resume the current track, keeping its position |
| Rescan | Save first, then reload the music library |
| Close | Hide the panel; the hotkey still works |

With "Include official" unticked, official tracks stay out of the shuffle. If a
category has none of your own playable tracks, the matching official music plays
instead.

## Advanced

For how the mod works and how to build it from source, see
[the development notes](docs/开发文档.md).

---

## Troubleshooting

The log is at `BepInEx\LogOutput.log`; search for `DynamicMusic` to find this mod's
entries, including how many tracks were scanned, how many official tracks imported,
and which files failed to load.

On entering a mission the official track metadata is logged once, which is handy for
confirming categories:

```
官方曲目 Nato 1 | side=nato | mode=Game
```

Common cases:

| Symptom | Cause and fix |
|---|---|
| A category shows 0 tracks | Check the files are in the matching `MusicLibrary` subfolder, not the root |
| No `[Official]` tracks visible | Official music appears once the game finishes loading; wait a moment or hit "Rescan" |
| A file shows as failed | The encoding may be unsupported; converting to ogg or mp3 usually fixes it |
| No sound at all | First confirm the game's own music works, then check the log for load records |
| Two tracks playing at once | Press "Vanilla mode" and back to force a clean takeover |
| Changes do not take effect | Panel changes are only written to the config on "Save" |
| The mod is missing from the list | Anchor Chain is probably not installed or not subscribed |

## Disclaimer

This is a community project and is not affiliated with Triassic Games.

All music bundled with the game is owned by Triassic Games. This mod only references
audio objects the game has already loaded; it copies and distributes no official
audio files.

Anchor Chain is shared infrastructure of the Sea Power modding community, MIT
licensed, and independent of this mod.

## License

Code is MIT licensed. See [LICENSE](LICENSE).

## 💖 Sponsor

This mod is still actively maintained.

**Thanks for being here. Every bit of support keeps it going.**
<table>
<tr>
<td align="center"><b>WeChat</b></td>
<td align="center"><b>Alipay</b></td>
</tr>
<tr>
<td align="center"><img src="docs/images/sponsor-wechat.png" width="230" alt="WeChat"></td>
<td align="center"><img src="docs/images/sponsor-alipay.png" width="230" alt="Alipay"></td>
</tr>
</table>

Thank you to everyone supporting this project ❤️
