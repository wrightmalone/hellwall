# Credits

Placeholder art, all CC0 (public domain), by [Kenney](https://kenney.nl):

- [Tower Defense](https://kenney.nl/assets/tower-defense): terrain blocks, ore crystals, tower pieces
- [Isometric Tiles Landscape](https://kenney.nl/assets/isometric-tiles-landscape): water and worked ground
- [Graveyard Kit](https://kenney.nl/assets/graveyard-kit): the zombie that Thralls are baked from (`game/tools/bake.gd`)

Soldiers are baked from [RPG Characters](https://quaternius.com/packs/rpgcharacters.html) by Quaternius (CC0): the Rogue, Ranger, Warrior, Cleric, Monk and Wizard (the Exorcist). Licence in `game/art/rpg/`.

Buildings are baked from the [KayKit Medieval Hexagon Pack](https://kaylousberg.itch.io/kaykit-medieval-hexagon) by Kay Lousberg (CC0) with `game/tools/bake_buildings.gd`, including the scaffolding shown while one goes up and the ruins. Licence in `game/art/kaykit-medieval/`. (Quaternius's CC0 Medieval Village MegaKit is downloaded but not used yet.)

The gold, food and sanctity icons are baked from props in [KayKit Dungeon Pack](https://kaylousberg.itch.io/kaykit-dungeon) by Kay Lousberg (CC0). Licence in `game/art/kaykit/`.

Trees, rocks and grass tufts are baked from Quaternius's [Stylized Nature MegaKit](https://quaternius.com/packs/stylizednaturemegakit.html) (free Standard edition, CC0) with `game/tools/bake_scenery.gd`. Licence in `game/art/nature/`.

Demons are baked from [Ultimate Monsters](https://quaternius.com/packs/ultimatemonsters.html) by Quaternius (CC0), the Spitter from its Alien. Licence in `game/art/quaternius/`.

Each pack's License.txt is in `game/art/kenney/` or `game/art/kenney3d/`. Sound and music are synthesized in code (`game/Sound.cs`, `game/Music.cs`).

The in-game Credits screen (main menu) carries the same list: keep the two in step.

## Code and libraries

- [Steamworks.NET](https://github.com/rlabrecque/Steamworks.NET) 2025.164.1 by Riley Labrecque, MIT licence (`game/lib/steamworks/LICENSE-Steamworks.NET.txt`): the game's link to Steam.
- Valve's Steamworks API libraries (`steam_api64.dll`, `libsteam_api.dylib`, `libsteam_api.so`), shipped with the game under the Steamworks SDK's terms for redistribution.
