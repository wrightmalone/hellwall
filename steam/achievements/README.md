# Achievement icons (placeholders)

Made by `game/tools/bake_achievement_icons.gd` from the game's own art: one per achievement in
`game/data/achievements.json`, each on a plate coloured by its group, with a corner mark for what
varies within the group (a mission's number, a difficulty's stars, a day, a count).

For each achievement id:

| File | What | Steamworks field |
|---|---|---|
| `ID.png` | 64x64, earned | Achieved icon |
| `ID_locked.png` | 64x64, not yet earned | Unachieved icon |
| `ID_256.png`, `ID_locked_256.png` | 256x256 masters | (keep for later) |

`contact-sheet.png` shows them all, earned beside locked.

**Replacing one:** draw a new icon with the same file name (64x64 PNG or JPG, plus a 256 master if
you like), and upload it in Steamworks (Stats & Achievements, the achievement, its icons). Re-running
the baker overwrites every file here, so only do that to get the placeholders back.

    Godot --path game --script res://tools/bake_achievement_icons.gd
