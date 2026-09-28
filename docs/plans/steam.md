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

**Draft list** (about 36, for you to cut down; names are placeholders for the writing pass):

| Group | Achievement | When |
|---|---|---|
| Campaign | one per mission (12) | win it |
| Campaign | Relic-bearer | win a relic |
| Campaign | Hallowed | hallow a relic |
| Campaign | The Full Reliquary | hallow every relic |
| Campaign | The Last Wall | win The Hellwall |
| Campaign | Three Candles | win The Hellwall on its bonus goal |
| Survival | Endure (Easy / Normal / Hard / Nightmare) | win a survival run at each difficulty (4) |
| Survival | The Long Night | win on the longest day setting |
| Endless | Day 50 / Day 100 / Day 150 | reach it in Endless (3) |
| Feats | Not One Stone | win without losing a building (walls aside) |
| Feats | The Wilds Are Quiet | clear every sleeping demon before the Convergence |
| Feats | Gatecrasher | close a Hellgate before day 10 |
| Feats | No Gate Stands | close every Hellgate in a run |
| Feats | Holy Ground | consecrate 3,000 tiles in one run |
| Feats | A Thousand Souls | 1,000 colonists at once |
| Feats | Ruin-Robber | loot 5 ruins in one run |
| Feats | Aftermath | play on after a win and clear the map |
| Totals | Slayer / Butcher / Scourge | 10,000 / 100,000 / 1,000,000 demons slain across all runs (stats) |
| Editor | Cartographer | make and play a map of your own |
| Editor | Author | publish a mission to the Workshop |
| Editor | Well Received | a mission of yours reaches 25 thumbs up |

## Cloud saves

- **Steam Auto-Cloud: no code.** **[you]** In Steamworks, list the folders to sync under the save folder.
  - `saves/`: the four save slots, and the latest autosave only. Five full autosaves would eat the quota for little gain.
  - `campaign.cfg`: missions won, relics hallowed, the last loadout.
  - `settings.cfg` and `scores.cfg`.
  - `maps/`: hand-made maps and missions.
  - Not synced: `logs/`, `crashes/`, `diagnostics.log`, the shader cache.
- **Autosaves:** they rotate through five slots today. They'd write to a folder of their own, so only the newest can be synced.
- **Conflicts:** two machines played offline are Steam's to settle, and it asks the player which to keep. Nothing is needed from us.
- **Quota:** a save is a few hundred KB, so ask for 50 MB and 500 files. That's plenty.
- **Later, if needed:** Steam Remote Storage (the API) gives more control, but Auto-Cloud covers this game.

## Sharing maps, the way Creeper World 3 did

CW3 worked because sharing lived inside the game: browse, search, sort by rating or plays, one click to play, a score to beat, and marks for what you've played and beaten. We'd build that same experience on the Steam Workshop, which already handles hosting, downloads, voting, tags and moderation.

- **Publishing** ("Publish to the Workshop", in the editor):
  - it sends the map file (a mission or a plain map);
  - a preview image, made from the map by the existing `mapimage` code with the Keep, gates and packs drawn on;
  - a title and description (the briefing);
  - tags: mission or map, map kind, difficulty, size;
  - a change note when you update it.
  - It's checked first, so a map the game would refuse can't be published.
  - Publishing needs **[you]** to turn the Workshop on in Steamworks and accept its legal agreement.
- **The in-game browser** (a Workshop page off the main menu):
  - It uses Steam's UGC queries: most popular, newest, top rated, most played, and search by name or tag.
  - Each map shows its preview, rating, play count and author.
  - "Play" subscribes and downloads, and the map appears at once. There's no trip to the Steam overlay.
  - Maps you've played or won are marked, as in CW3.
  - Rating uses Steam's votes, open to anyone who has played it. Steam can't restrict votes to people who've won; we could show a "won by N players" count beside it.
- **Scores:** each shared map gets a Steam leaderboard, created the first time anyone finishes it.
  - It's ranked by the run's score, which the game already computes, with days survived as the tie-break.
  - The end screen shows your place and the friends above you.
  - A hand-made mission counts only if it was played unchanged: the leaderboard is keyed to the published version.
- **Updates:** a map updated by its maker gets a fresh leaderboard. Old scores were made on a different map.
- **Without Steam:** file export and import stay, for dev builds and for sending a file to a friend.

## Build order

1. **The save folder moves, with migration.** It's needed before anything syncs, so it goes first even though it's small.
2. **The Steam module and Steamworks.NET in the build.** Steam starts when present and does nothing when absent. export.sh copies the native libraries.
3. **Cloud saves:** autosaves in a folder of their own, then **[you]** set up Auto-Cloud.
4. **Achievements:** the module, data and headless check first. Then **[you]** add the list, icons and stats in Steamworks. Icons need art: placeholders first.
5. **Workshop publishing** from the editor, with previews and tags.
6. **The in-game browser.**
7. **Leaderboards per map.**

Steps 1 to 4 are small and independent of each other. Steps 5 to 7 are the bulk, and step 6 is the part players will judge.

## Still open

- **Leaderboards:** score (assumed) or days, and whether to have them at all.
- **Ratings:** anyone can vote (assumed; it's how the Workshop works), with a "won by N" count shown beside it.
- **The achievement list:** cut it down, and name them in the writing pass.
