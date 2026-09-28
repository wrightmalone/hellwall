# Steam: achievements, cloud saves, and sharing maps

Hellwall ships on Steam only for now. This plan covers what each part needs, and the order to build it in. Anything that has to happen in the Steamworks website (the partner site) rather than in the code is marked **[you]**.

## What comes first

- **[you] A Steamworks partner account and an App ID.** The fee is $100 per app. Building against the App ID can start before the store page is live. For local work, a `steam_appid.txt` beside the game's executable stands in for launching through Steam.
- **The Steam library: Steamworks.NET.** It's the standard C# wrapper, and a better fit for a C# project than GodotSteam (a GDExtension aimed at GDScript). It ships native `steam_api64.dll` / `libsteam_api.dylib` files, which export.sh copies beside the build.
- **One Steam module (`game/Steam.cs`).** It starts Steam once, pumps its callbacks each frame, and does nothing when Steam isn't running. That covers dev builds, headless tests and the bot. Nothing else in the game talks to Steam directly.
- **The save folder moves before release.**
  - Today it's Godot's generic `%APPDATA%\Godot\app_userdata\Hellwall`. It becomes `%APPDATA%\Hellwall`, and `~/Library/Application Support/Hellwall` on a Mac (Godot's `use_custom_user_dir`).
  - On first launch, the old folder is copied across once.
  - It moves before any cloud syncing starts, so the synced paths never change afterwards.
- **What stays Steam-free.** The sim knows nothing of Steam. Achievements listen to the events the sim already sends (wins, kills, gates closed), so the sim stays deterministic and testable.

## Achievements

- **How they work:**
  - **[you]** Each is an API name, a display name, a description and two 64x64 icons (earned and not), set in Steamworks.
  - The game calls SetAchievement and StoreStats when one is earned.
  - Progressive ones ("slay 100,000 demons") are Steam stats with a progress bar.
  - They're earned offline too, and Steam syncs them on reconnect.
- **In the code:** an `Achievements` module on the client watches the sim's events and the run's end, with each achievement written as a rule.
  - The rules live in data (`achievements.json`), so the list can change without code.
  - A headless check plays a few bot runs and confirms the right ones fire, which keeps them honest as the game changes.
- **Guards against cheap unlocks:**
  - Nothing unlocks with debug keys used.
  - Nothing unlocks in the editor's test plays.
  - Nothing unlocks on a hand-made map, except the editor ones.

**Built in 0.33.0.** The code side is done:
- the list is `game/data/achievements.json`;
- the rules are `src/Achievements/Achievements.cs`, tested in `tests/Sim.Tests/AchievementTests.cs`;
- `game/Achievements.cs` keeps what's earned in `achievements.cfg`, shows a notice, and sends each one to Steam when it's up;
- Extras has an Achievements page.

**How they're earned:**
- **Only in fair runs:**
  - nothing counts with the bot playing, in the menu's background game, with dev options, or once a debug key (K, J, F6) is used;
  - a hand-made map earns only Cartographer, so a custom map can't farm the rest (its kills don't count towards the totals either);
  - a loaded save's kills are never counted twice.
- **Survival wins are cumulative:** a win on Hard also earns Normal and Easy.
- **At launch:** the campaign's relic achievements are checked against existing progress. Everything earned locally, including offline, is sent to Steam once it's up.
- **Not yet wired:** Author and Well Received wait for the Workshop (step 5).

**[you] In Steamworks: App Admin, then Stats & Achievements:**
1. **Add the stat first:** API name `demons_slain`, type INT, "Increment only", default 0, max 2147483647, display name "Demons slain".
2. **Add each achievement below.** The API name must match exactly. For Slayer, Butcher and Scourge, set "Progress stat" to `demons_slain`, from 0 to its count, so Steam shows a progress bar.
3. **Icons:** each needs two 64x64 images, earned and unearned (the unearned one is usually a greyed version). Placeholders are fine to start: the art pass can replace them.
4. **Publish** the changes.

| API name | Display name | Description | Progress |
|---|---|---|---|
| `MISSION_FIRST_NIGHT` | The First Night | Win The First Night. |  |
| `MISSION_IRON_HILLS` | Iron in the Hills | Win Iron in the Hills. |  |
| `MISSION_GATEKEEPERS` | The Gatekeepers | Win The Gatekeepers. |  |
| `MISSION_DROWNED` | Drowned Country | Win Drowned Country. |  |
| `MISSION_CAUSEWAY` | The Causeway | Win The Causeway. |  |
| `MISSION_THE_PASS` | The Pass | Win The Pass. |  |
| `MISSION_TWO_FRONTS` | Two Fronts | Win Two Fronts. |  |
| `MISSION_WILDWOOD` | Wildwood Watch | Win Wildwood Watch. |  |
| `MISSION_RELIQUARY` | The Reliquary | Win The Reliquary. |  |
| `MISSION_LONG_SIEGE` | The Long Siege | Win The Long Siege. |  |
| `MISSION_THE_BRIDGE` | The Bridge | Win The Bridge. |  |
| `MISSION_HELLWALL` | The Hellwall | Win The Hellwall. |  |
| `RELIC_BEARER` | Relic-bearer | Win a relic in the campaign. |  |
| `HALLOWED` | Hallowed | Hallow a relic: win a mission with its bonus goal met. |  |
| `FULL_RELIQUARY` | The Full Reliquary | Hallow every relic. |  |
| `THREE_CANDLES` | Three Candles | Win The Hellwall with three hallowed relics. |  |
| `ENDURE_EASY` | Endure (Easy) | Win a survival run on Easy or harder. |  |
| `ENDURE_NORMAL` | Endure (Normal) | Win a survival run on Normal or harder. |  |
| `ENDURE_HARD` | Endure (Hard) | Win a survival run on Hard or harder. |  |
| `ENDURE_NIGHTMARE` | Endure (Nightmare) | Win a survival run on Nightmare or harder. |  |
| `LONG_NIGHT` | The Long Night | Win a 90-day survival run. |  |
| `ENDLESS_50` | Day 50 | Reach day 50 in Endless. |  |
| `ENDLESS_100` | Day 100 | Reach day 100 in Endless. |  |
| `ENDLESS_150` | Day 150 | Reach day 150 in Endless. |  |
| `NOT_ONE_STONE` | Not One Stone | Win without losing a building (walls and gates aside). |  |
| `WILDS_QUIET` | The Wilds Are Quiet | Clear every sleeping demon on the map before the Convergence lands. |  |
| `GATECRASHER` | Gatecrasher | Close a Hellgate before day 10. |  |
| `NO_GATE_STANDS` | No Gate Stands | Close every Hellgate on the map. |  |
| `HOLY_GROUND` | Holy Ground | Have 3,000 tiles of holy ground at once. |  |
| `THOUSAND_SOULS` | A Thousand Souls | Have 1,000 colonists at once. |  |
| `RUIN_ROBBER` | Ruin-Robber | Loot 5 ruins in one run. |  |
| `AFTERMATH` | Aftermath | Play on after a win and clear the map: not a demon left, awake or asleep. |  |
| `SLAYER` | Slayer | Slay 10,000 demons, over all your runs. | stat `demons_slain`, 0 to 10,000 |
| `BUTCHER` | Butcher | Slay 100,000 demons, over all your runs. | stat `demons_slain`, 0 to 100,000 |
| `SCOURGE` | Scourge | Slay 1,000,000 demons, over all your runs. | stat `demons_slain`, 0 to 1,000,000 |
| `CARTOGRAPHER` | Cartographer | Make a map of your own in the editor, and play it. |  |
| `AUTHOR` | Author | Publish a mission to the Steam Workshop. |  |
| `WELL_RECEIVED` | Well Received | A mission of yours reaches 25 thumbs up on the Workshop. |  |

## Cloud saves

The code side is done in 0.32.0. What's left is **[you]**: Steamworks settings, with no code.

**How the save folder is laid out now:**

| Path (under the save folder) | What | Synced |
|---|---|---|
| `saves/quicksave.hwsave`, `saves/slot1-3.hwsave` (+ `.txt`) | the quicksave and the three named slots | yes |
| `saves/autosave-latest.hwsave` (+ `.txt`) | a copy of the newest autosave, rewritten at every autosave | yes |
| `autosaves/autosave1-5.hwsave` (+ `.txt`) | the five-slot rotation | no: it stays on each machine |
| `campaign.cfg` | missions won, relics hallowed, the last loadout | yes |
| `scores.cfg` | best scores | yes |
| `settings.cfg` | settings | yes |
| `display.cfg` | UI scale and fullscreen: this machine's screen | no |
| `maps/*.json` | hand-made maps and missions | yes |
| `achievements.cfg` | achievements earned, and demons slain (so a build without Steam shows them too) | yes |
| `workshop.cfg` | Workshop maps played and won (the browser's marks) | yes |
| `logs/`, `crashes/`, `diagnostics.log`, `session.txt`, caches | for debugging | no |

- **Existing saves:** saves loose in the folder, from 0.31 and earlier, are moved into `saves/` and `autosaves/` on first launch.
- **The Load page:** it lists the synced autosave as "Autosave (another computer's)" only when it's newer than all of this machine's own. That's how an autosave made on the other PC shows up. "Continue" takes whichever save is newest.
- **Settings:** UI scale and fullscreen are kept per machine, in `display.cfg` (your call), so a laptop and a big monitor each keep their own. Everything else in `settings.cfg` travels.

**[you] In Steamworks: App Admin, then Steam Cloud:**

1. **Byte quota per user:** 50 MB. **Number of files allowed per user:** 500. A save is about 220 KB, so this is generous.
2. **Enable Auto-Cloud**, and add these **Root paths** (seven), all with **OS: Windows**:

| Root | Subdirectory | Pattern | Recursive |
|---|---|---|---|
| WinAppDataRoaming | `Hellwall/saves` | `*` | no |
| WinAppDataRoaming | `Hellwall` | `campaign.cfg` | no |
| WinAppDataRoaming | `Hellwall` | `scores.cfg` | no |
| WinAppDataRoaming | `Hellwall` | `settings.cfg` | no |
| WinAppDataRoaming | `Hellwall/maps` | `*.json` | no |
| WinAppDataRoaming | `Hellwall` | `achievements.cfg` | no |
| WinAppDataRoaming | `Hellwall` | `workshop.cfg` | no |

3. **Root overrides**, so Macs and Linux sync to the same cloud files:
   - Original root WinAppDataRoaming, OS **macOS**, new root **MacAppSupport**.
   - Original root WinAppDataRoaming, OS **Linux**, new root **LinuxXdgDataHome**.

   Leave "add/replace path" empty; the subdirectories are the same on every OS.
4. **Save, then publish** the change in Steamworks.
5. **Test:** play a few minutes on one PC through Steam, quit, then launch on another PC. The campaign progress should be there, and the Load page should offer "Autosave (another computer's)".

**Conflicts:** two machines played offline are Steam's to settle. It asks the player which to keep, so nothing is needed from us.

## Sharing maps, the way Creeper World 3 did

CW3 worked because sharing lived inside the game: browse, search, sort by rating or plays, one click to play, a score to beat, and marks for what you've played and beaten. We'd build that same experience on the Steam Workshop, which already handles hosting, downloads, voting, tags and moderation.

**Publishing is built (0.34.0):** `game/Workshop.cs`, and **Publish...** in the editor.
- **What it sends:**
  - the map, staged as `map.json` in `user://workshop/<id>/content`;
  - a 512 px preview drawn from its tiles, with the Keep, Hellgates, packs and standing buildings marked;
  - its name, its briefing as the description, a change note, and its tags.
- **Updates:** the Workshop item's id is written into the map file, so publishing again updates the same item.
- **Author:** a published mission earns it.
- **Without Steam:** the button says Steam is needed. Export... still makes a file to send.
- **Not yet tested against Steam:** no Steam client can run here, and it needs the App ID. Staging and the preview are tested in the editor's self-test.
- **[you] In Steamworks:**
  - turn on the Workshop (App Admin, then Workshop, then "Enable ISteamUGC for file transfer");
  - set the item visibility you want;
  - add the tags `Mission`, `Map`, each map kind, `128`, `192`, `256`, `320` and each difficulty, so the browser can filter;
  - publish the change;
  - you'll also accept the Workshop legal agreement once, and the game opens it if Steam asks.

The rest of this section is the design, including the parts still to build.

- **Publishing** ("Publish to the Workshop", in the editor):
  - it sends the map file (a mission or a plain map);
  - a preview image, made from the map by the existing `mapimage` code with the Keep, gates and packs drawn on;
  - a title and description (the briefing);
  - tags: mission or map, map kind, difficulty, size;
  - a change note when you update it.
  - It's checked first, so a map the game would refuse can't be published.
  - Publishing needs **[you]** to turn the Workshop on in Steamworks and accept its legal agreement.
**The browser is built (0.35.0):** `game/WorkshopBrowser.cs`, **Workshop** on the main menu.
- **Browsing:**
  - sort by Popular (trending over 30 days), Newest, Top rated or Most played;
  - search by name, and show missions, maps or both;
  - each row has the map's preview, author, thumbs up and down, player count, tags, and a "played" or "won" mark (kept in `workshop.cfg`).
- **Play:** it subscribes, downloads (polled until it's installed), reads `map.json`, checks it as an import would, and starts it as its maker set it.
- **After the run:**
  - the end screen offers a thumbs up or down;
  - winning marks the map "won".
- **Fairness:** Workshop maps count as hand-made for achievements, so they can't farm them.
- **Well Received:** checked each time the page opens, against your own published missions' thumbs up.
- **Tested without Steam:** the browser talks to a source. The Steam one uses ISteamUGC queries; a made-up one (`FakeWorkshopSource`, `--workshop-demo`) lists generated maps. `--selftest=workshop`, now part of verify, lists them, searches, presses Play, and checks the map it gets plays and the marks keep.
- **Not yet tested:** the Steam source itself, which needs a Steam client and the App ID.

The design notes follow.

- **The in-game browser** (a Workshop page off the main menu):
  - It uses Steam's UGC queries: most popular, newest, top rated, most played, and search by name or tag.
  - Each map shows its preview, rating, play count and author.
  - "Play" subscribes and downloads, and the map appears at once. There's no trip to the Steam overlay.
  - Maps you've played or won are marked, as in CW3.
  - Rating uses Steam's votes, open to anyone who has played it. Steam can't restrict votes to people who've won; we could show a "won by N players" count beside it.
**Leaderboards are built (0.36.0):** `game/Leaderboards.cs`.
- **The board:** each Workshop map's is named for its item id and a fingerprint of the file as published, e.g. `map_123456789_9f3c...`. It's created the first time anyone finishes it.
- **Versions:** a map changed by its maker has a new fingerprint, and so a new board. A save of the map carries its fingerprint, so a loaded game still counts on the right board.
- **What goes up:** at the end of a fair run (the same rule as achievements), the game's score, keeping your best, with the days held and demons slain beside it.
- **The end screen** shows your rank and best, the two players above and below you, and your friends.
- **[you] Nothing to do in Steamworks:** the game makes the boards. If you ever want only the game's own uploads trusted, Steamworks can set a board to "trusted writes", but that needs a server, so leave it off.
- **Not yet tested against Steam** (it needs a client and the App ID). Board names and the end-screen line are tested in `--selftest=workshop`.

The design notes follow.

- **Scores:** each shared map gets a Steam leaderboard, created the first time anyone finishes it.
  - It's ranked by the run's score, which the game already computes, with days survived as the tie-break.
  - The end screen shows your place and the friends above you.
  - A hand-made mission counts only if it was played unchanged: the leaderboard is keyed to the published version.
- **Updates:** a map updated by its maker gets a fresh leaderboard. Old scores were made on a different map.
- **Without Steam:** file export and import stay, for dev builds and for sending a file to a friend.

## Build order

1. **The save folder moves, with migration.** Done in 0.30.0 (`game/SaveFolder.cs`).
2. **The Steam module and Steamworks.NET in the build.** Done in 0.31.0 (`game/Steam.cs`, `game/lib/steamworks/`). Apple-silicon Macs go without Steam for now: Steamworks.NET's standalone build is x64 only, so it needs building from its source as any-CPU to reach them.
3. **Cloud saves:** the code side done in 0.32.0 (the save folder laid out for syncing). **[you]** Set up Auto-Cloud, as above.
4. **Achievements:** the code done in 0.33.0 (tested with scripted runs in the unit tests, not bot runs). **[you]** Add the stat, the list and icons in Steamworks, as above.
5. **Workshop publishing** from the editor, with previews and tags: done in 0.34.0; **[you]** turn the Workshop on in Steamworks.
6. **The in-game browser:** done in 0.35.0.
7. **Leaderboards per map:** done in 0.36.0.

Steps 1 to 4 are small and independent of each other. Steps 5 to 7 are the bulk, and step 6 is the part players will judge.

## Still open

- **Leaderboards:** built on score (days as detail). Say if you'd rather rank by days, or drop them.
- **Ratings:** anyone can vote (assumed; it's how the Workshop works), with a "won by N" count shown beside it.
- **The achievement list:** cut it down, and name them in the writing pass.
