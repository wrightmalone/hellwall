# Corrupted bear animation implementation

The bear now replaces the Brute's art in normal gameplay. It has eight facings and five six-frame clips: breathing idle, walking, bite/paw attack, hit recoil and collapse/death. Brute combat statistics and behavior are unchanged. This is a generated-art animation pass ready for in-game review, with remaining artistic polish described below.

## View and play

From the repository root, after importing/building the project:

```sh
/Applications/Godot_mono.app/Contents/MacOS/Godot --path game -- --bear-gallery
```

The gallery animates every clip in all eight directions. F2 returns to a staged fight with a defended town, trained soldiers and 19 bears plus three existing enemies. Scroll zooms, arrow keys pan, Space pauses, and Tab changes speed. F2 reopens the gallery and pauses combat until you return. The fight uses normal Brute statistics, including 700 base HP; the gallery is available with F2 during ordinary runs too.

`--bear-preview` opens directly to combat. `--legacy-brute-art` restores the previous Brute sprites for comparison. Preview runs do not count for achievements.

## Assets and provenance

- `game/art/bear/bear-animations.png`: 576 x 3840 RGBA atlas, 96-pixel square cells. Six frame columns and 40 rows: `clip * 8 + facing`. All 240 runtime cells contain the corresponding pose.
- `game/art/bear/animations.json`: exact packing, alignment and mirroring metadata.
- `game/art/bear/source/{s,sw,w,nw,n}.png`: five generated 6-column x 5-row source sheets. A `.gdignore` keeps source sheets out of runtime imports/exports. Each source row is idle, walk, attack, hit or death, in that order.
- `scripts/pack-bear-animations.py`: reproducible grid extraction, disconnected-fragment removal, baseline alignment, mirroring, resizing, packing and review GIF creation. Requires Pillow and numpy. It does not synthesize or warp poses.
- `docs/art-studies/bear-animation-prompts.json`: exact prompts used with the built-in image generation tool, from the approved transparent painted bear in `game/art/preview/bear/bear-source.png`. That design was developed from Kian's original corrupted bear and corruption manifestation drawings.
- `out/bear-animation/`: review PNGs/GIFs and runtime captures; ignored by Git.

Five source views are drawn: S, SW, W, NW and N. SE, E and NE mirror their opposite side views. The atlas contains 150 generated source poses and 90 mirrored cells. Mirroring also mirrors asymmetric wounds and lighting; this is an explicit production tradeoff rather than eight independently painted rotations.

The old static study remains in `game/art/preview/bear/` and its original packing helper remains available as historical/reference material. It is not the current Brute atlas.

## Runtime behavior

The Brute uses the existing batched MultiMesh renderer and remains the same physical on-screen quad size. The 96-pixel cells improve sampling at close zoom without enlarging its simulation collision radius. Other enemy types keep their previous 72-pixel animation sheets.

- Idle: breathing at 3.2 frames/sec, with phase offsets so groups do not all breathe together. Dormant bears also breathe.
- Walk: six-frame loop, one stride per 1.2 tiles of actual movement. A stopped bear retains its last facing.
- Attack: a 0.78-second six-frame clip triggered when the simulation resets that bear's attack cooldown. Facing is held through the strike; damage timing and target selection remain the existing simulation's. This is a visual reaction to an attack, not a new windup/damage-delay mechanic.
- Hit: a 0.36-second six-frame recoil and short warm flash following a real HP decrease. An ongoing attack keeps priority; repeated damage flashes are throttled to prevent perpetual strobing.
- Death: a 1.2-second collapse, held on the final prone frame, then faded by 5.2 seconds. Corpse count is bounded at 256. Corpses respect current fog visibility.

Animation clocks use simulation tick time, so pause and game speed apply consistently. The gallery runs on its own presentation clock while combat is paused. Presentation state is captured before each simulation tick and follows the horde's stable compaction afterward. `DeathMark.Slot` identifies the pre-compaction slot; it is transient output, neither saved nor hashed. There is no new persistent simulation state or save-format change.

## Verification

The test suite includes row indexing, attack/recoil priority, distance-based walking, death hold/fade and transient death-slot compaction checks.

```sh
dotnet test tests/Sim.Tests
/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path game -- --selftest=bear
```

The runtime self-test plays 100 seconds of staged combat, observes all five animation states, and checks state hashes against a simulation-only clone. It reports clip samples and the facing mask (`ff` means all eight facings occurred).

To record the actual in-engine gallery:

```sh
/Applications/Godot_mono.app/Contents/MacOS/Godot --path game -- --bear-gallery --bear-capture="$PWD/out/bear-animation/gallery-frames"
```

This writes 48 PNGs at a deterministic eight-frame/sec gallery clock and exits. The capture contains actual Godot rendering of the runtime atlas, not an external animation mockup.

## Remaining polish

This pass supplies usable poses, directions and state transitions, but it is not a hand-cleaned final commercial animation. Six-frame motion is deliberately coarse. Generated poses still exhibit some texture/anatomy shimmer and weight/contact variation, particularly when enlarged. Turn transitions snap between eight views. Mirroring duplicates the rib wound onto the opposite side. The attack is triggered by the existing instantaneous strike and does not move gameplay damage to the painted impact frame. More fluid locomotion, distinct attack damage windows, finer wound consistency and custom sound effects are separate production work.

At default zoom the bear's broad silhouette, pale skull/ribs and hanging hide carry identification; individual blood and sludge strands reduce to texture. Higher source resolution helps close zoom, but does not create more screen pixels at normal zoom. The brighter placeholder environment remains a different art style.

The packer validates nonempty padded cells and six distinct frames for every clip/facing. An optional renderer stress run converts the existing 20,000-enemy bench into Brutes:

```sh
/Applications/Godot_mono.app/Contents/MacOS/Godot --path game -- --bench=6 --bear-bench --skip=85 --reveal
```

This is a rendering/performance probe; it does not establish gameplay balance for a 20,000-Brute army.

## Results from this implementation pass

- `scripts/verify.sh --fast`: passed, including 246 tests, cross-process determinism, 20,000-enemy simulation performance, defended-town probes and Godot startup/control/save checks.
- Bear runtime self-test: passed. All five clips were observed during combat, the facing mask was `ff`, and simulation hashes matched the clone throughout the test.
- Atlas validation: all 240 cells are nonempty and padded, every clip/facing contains six distinct frames, and mirrored pairs match exactly.
- Actual in-engine gallery recording: `out/bear-animation/in-game-animations.gif` (48 captured frames at eight frames/sec, six seconds, resized for review).
- M1 Pro stress measurement (`--bench=6 --bear-bench --skip=85 --reveal`): 19,297 surviving enemies at the end, 52.1 average FPS, frame p95 21.67 ms, combined tick/presentation-update p95 6.72 ms. This is a short, intentionally extreme all-Brute probe on this machine, not a minimum-hardware guarantee or a benchmark for every campaign scene.
- Matched legacy-art comparison: 52.7 average FPS, frame p95 21.43 ms, tick p95 5.78 ms, same starting simulation hash and same final enemy count. These short runs put the rendering rates within about 1 FPS of each other; the new presentation tracking adds about 1 ms to the 20,000-enemy tick in this probe. Treat small FPS differences as noise rather than a precise speedup/slowdown.
