extends SceneTree
# Placeholder icons for the Steam achievements (res://data/achievements.json): for each, an earned
# icon and a locked one (greyed and dimmed, as Steam shows unearned), at 64x64 (what Steamworks
# takes) and 256x256 (a master, for sharper sizes later), in steam/achievements/ at the repo root,
# outside the game (it never ships them). Each is the achievement's subject, drawn from the
# game's own art, on a plate whose colour says its group, with a corner mark for what varies
# within the group (a mission's number, a difficulty's pips, a day, a count). The art pass can
# replace any file with the same name. Run with a display:
#   Godot --path game --script res://tools/bake_achievement_icons.gd

const SIZE := 256
const B := "res://art/baked/"
const OUT := "res://../steam/achievements"

# A group's plate: fill and border.
const PLATES := {
	"campaign": [Color(0.20, 0.15, 0.07), Color(0.86, 0.66, 0.30)],
	"relic": [Color(0.16, 0.10, 0.20), Color(0.72, 0.56, 0.90)],
	"survival": [Color(0.20, 0.08, 0.06), Color(0.90, 0.42, 0.25)],
	"endless": [Color(0.08, 0.08, 0.11), Color(0.62, 0.64, 0.74)],
	"feat": [Color(0.08, 0.16, 0.10), Color(0.52, 0.80, 0.48)],
	"total": [Color(0.18, 0.04, 0.05), Color(0.86, 0.22, 0.24)],
	"editor": [Color(0.07, 0.12, 0.19), Color(0.48, 0.70, 0.95)],
}

# id: [group, art, corner mark]. Art: a path under res://art/baked, "unit:<sheet>" (its camera-facing
# frame), or a drawing: "gate" (a Hellgate), "gate-closed", "map" (a patch of terrain).
var looks := {
	"MISSION_FIRST_NIGHT": ["campaign", "buildings/Keep.png", "I"],
	"MISSION_IRON_HILLS": ["campaign", "buildings/Mine.png", "II"],
	"MISSION_GATEKEEPERS": ["campaign", "gate-closed", "III"],
	"MISSION_DROWNED": ["campaign", "buildings/Fishery.png", "IV"],
	"MISSION_CAUSEWAY": ["campaign", "buildings/Watchtower.png", "V"],
	"MISSION_THE_PASS": ["campaign", "buildings/Bombard.png", "VI"],
	"MISSION_TWO_FRONTS": ["campaign", "unit:unit-marksman", "VII"],
	"MISSION_WILDWOOD": ["campaign", "buildings/Woodcutter.png", "VIII"],
	"MISSION_RELIQUARY": ["campaign", "buildings/ruin.png", "IX"],
	"MISSION_LONG_SIEGE": ["campaign", "buildings/Cottage.png", "X"],
	"MISSION_THE_BRIDGE": ["campaign", "buildings/LanceTower.png", "XI"],
	"MISSION_HELLWALL": ["campaign", "walls/stone-gate-x-0.png", "XII"],
	"RELIC_BEARER": ["relic", "scenery/icon-holy.png", ""],
	"HALLOWED": ["relic", "buildings/Shrine.png", ""],
	"FULL_RELIQUARY": ["relic", "buildings/Wardstone.png", "ALL"],
	"THREE_CANDLES": ["relic", "buildings/Censer.png", "III"],
	"ENDURE_EASY": ["survival", "unit:unit-militia", "*"],
	"ENDURE_NORMAL": ["survival", "unit:unit-crossbowman", "**"],
	"ENDURE_HARD": ["survival", "unit:unit-templar", "***"],
	"ENDURE_NIGHTMARE": ["survival", "unit:unit-exorcist", "****"],
	"LONG_NIGHT": ["survival", "buildings/Belfry.png", "90"],
	"ENDLESS_50": ["endless", "unit:demon-brute", "50"],
	"ENDLESS_100": ["endless", "unit:demon-broodmother", "100"],
	"ENDLESS_150": ["endless", "unit:demon-bloater", "150"],
	"NOT_ONE_STONE": ["feat", "walls/stone-icon.png", ""],
	"WILDS_QUIET": ["feat", "scenery/pine-2.png", ""],
	"GATECRASHER": ["feat", "gate", "10"],
	"NO_GATE_STANDS": ["feat", "gate-closed", "ALL"],
	"HOLY_GROUND": ["feat", "scenery/icon-holy.png", "3K"],
	"THOUSAND_SOULS": ["feat", "buildings/Manor.png", "1K"],
	"RUIN_ROBBER": ["feat", "scenery/icon-gold.png", "5"],
	"AFTERMATH": ["feat", "unit:unit-chaplain", ""],
	"SLAYER": ["total", "unit:demon-imp", "10K"],
	"BUTCHER": ["total", "unit:demon-hound", "100K"],
	"SCOURGE": ["total", "unit:demon-gargoyle", "1M"],
	"CARTOGRAPHER": ["editor", "map", ""],
	"AUTHOR": ["editor", "unit:unit-town-scholar", ""],
	"WELL_RECEIVED": ["editor", "buildings/Skyspire.png", "25"],
}

var viewport: SubViewport

func _initialize():
	viewport = SubViewport.new()
	viewport.size = Vector2i(SIZE, SIZE)
	viewport.transparent_bg = false
	viewport.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	root.add_child(viewport)
	bake.call_deferred()

func bake():
	var list: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://data/achievements.json"))
	var out := ProjectSettings.globalize_path(OUT)
	DirAccess.make_dir_recursive_absolute(out)
	var done := 0
	var missing := []
	# A contact sheet of every icon, earned beside locked, at 128 px: for reviewing them at a glance.
	const CELL := 128
	const COLS := 5
	var count: int = list["achievements"].size()
	var sheet := Image.create_empty(COLS * (CELL * 2 + 20) + 20, int(ceil(count / float(COLS))) * (CELL + 20) + 20, false, Image.FORMAT_RGBA8)
	sheet.fill(Color(0.1, 0.1, 0.12))
	for a in list["achievements"]:
		var id: String = a["id"]
		if not looks.has(id):
			missing.append(id)
			continue
		var look: Array = looks[id]
		var icon := build(look[0], look[1], look[2])
		viewport.add_child(icon)
		await RenderingServer.frame_post_draw
		await RenderingServer.frame_post_draw
		var image := viewport.get_texture().get_image()
		image.convert(Image.FORMAT_RGBA8)
		var grey := locked(image)
		save(image, out + "/" + id)
		save(grey, out + "/" + id + "_locked")
		var at := Vector2i(20 + (done % COLS) * (CELL * 2 + 20), 20 + (done / COLS) * (CELL + 20))
		for pair in [[image, 0], [grey, CELL + 4]]:
			var small: Image = pair[0].duplicate()
			small.resize(CELL, CELL, Image.INTERPOLATE_LANCZOS)
			sheet.blit_rect(small, Rect2i(0, 0, CELL, CELL), at + Vector2i(pair[1], 0))
		icon.queue_free()
		await process_frame
		done += 1
	sheet.save_png(out + "/contact-sheet.png")
	print("baked ", done, " achievement icons (", done * 2, " at 64 and 256) into ", out, "; no look for: ", missing)
	quit()

# Written twice: the 64x64 Steamworks takes, and the 256x256 master.
func save(image: Image, path: String):
	image.save_png(path + "_256.png")
	var small := image.duplicate()
	small.resize(64, 64, Image.INTERPOLATE_LANCZOS)
	small.save_png(path + ".png")

# Unearned: grey, dim and flat, so it reads as locked beside the earned one.
func locked(image: Image) -> Image:
	var grey := image.duplicate()
	for y in grey.get_height():
		for x in grey.get_width():
			var c: Color = grey.get_pixel(x, y)
			var l: float = c.r * 0.3 + c.g * 0.59 + c.b * 0.11
			l = 0.12 + l * 0.42
			grey.set_pixel(x, y, Color(l, l, l * 1.04, c.a))
	return grey

func build(group: String, art: String, mark: String) -> Control:
	var plate: Array = PLATES[group]
	var box := Panel.new()
	box.size = Vector2(SIZE, SIZE)
	var style := StyleBoxFlat.new()
	style.bg_color = plate[0]
	style.border_color = plate[1]
	style.set_border_width_all(12)
	style.set_corner_radius_all(26)
	box.add_theme_stylebox_override("panel", style)
	# A glow behind the subject, in the plate's colour.
	var glow := TextureRect.new()
	var gradient := Gradient.new()
	gradient.set_color(0, Color(plate[1], 0.45))
	gradient.set_color(1, Color(plate[1], 0.0))
	var fill := GradientTexture2D.new()
	fill.gradient = gradient
	fill.fill = GradientTexture2D.FILL_RADIAL
	fill.fill_from = Vector2(0.5, 0.5)
	fill.fill_to = Vector2(0.5, 0.0)
	fill.width = SIZE
	fill.height = SIZE
	glow.texture = fill
	glow.position = Vector2(12, 12)
	glow.size = Vector2(SIZE - 24, SIZE - 24)
	box.add_child(glow)
	box.add_child(subject(art))
	if mark != "":
		var label := Label.new()
		label.text = mark
		label.add_theme_font_size_override("font_size", 46 if mark.length() <= 3 else 36)
		label.add_theme_color_override("font_color", plate[1].lightened(0.25))
		label.add_theme_color_override("font_outline_color", Color(0, 0, 0, 0.9))
		label.add_theme_constant_override("outline_size", 12)
		label.position = Vector2(24, SIZE - 80)
		label.size = Vector2(SIZE - 48, 60)
		label.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
		box.add_child(label)
	return box

# The picture in the middle: a sprite fitted to the plate, or a drawing.
func subject(art: String) -> Control:
	var area := Rect2(34, 26, SIZE - 68, SIZE - 76)
	if art == "gate" or art == "gate-closed" or art == "map":
		var drawing := Drawing.new()
		drawing.kind = art
		drawing.position = area.position
		drawing.size = area.size
		return drawing
	var tex: Texture2D
	if art.begins_with("unit:"):
		var atlas := AtlasTexture.new()
		atlas.atlas = load(B + art.substr(5) + ".png")
		atlas.region = Rect2(0, 72, 72, 72) # facing the camera, first frame
		tex = atlas
	else:
		tex = load(B + art)
	var rect := TextureRect.new()
	rect.texture = trimmed(tex)
	rect.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	rect.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	rect.position = area.position
	rect.size = area.size
	return rect

# A texture cut to its visible pixels, so it fills the plate however much empty canvas its bake left.
func trimmed(tex: Texture2D) -> Texture2D:
	var image := tex.get_image()
	if image.is_compressed():
		image.decompress()
	var used := image.get_used_rect()
	if used.size.x == 0:
		return tex
	return ImageTexture.create_from_image(image.get_region(used))

# The drawn subjects: a Hellgate (open, or closed with a bar across it) and a patch of map.
class Drawing extends Control:
	var kind := ""
	func _draw():
		var c: Vector2 = size / 2
		if kind == "map":
			var colours := [Color(0.33, 0.52, 0.28), Color(0.20, 0.38, 0.20), Color(0.36, 0.55, 0.78), Color(0.52, 0.50, 0.48), Color(0.33, 0.52, 0.28), Color(0.78, 0.64, 0.30)]
			var n: int = 5
			var t: float = size.x / (n + 1.0)
			for y in n:
				for x in n:
					var at: Vector2 = c + Vector2((x - y) * t / 2, (x + y - n + 1) * t / 4)
					var diamond := PackedVector2Array([at + Vector2(0, -t / 4), at + Vector2(t / 2, 0), at + Vector2(0, t / 4), at + Vector2(-t / 2, 0)])
					draw_colored_polygon(diamond, colours[(x * 7 + y * 3) % colours.size()])
			draw_circle(c + Vector2(0, 4), t * 0.28, Color(0.94, 0.76, 0.36))
			return
		var r: float = minf(size.x, size.y) * 0.42
		draw_circle(c, r, Color(0.22, 0.0, 0.05))
		draw_circle(c, r * 0.82, Color(0.62, 0.05, 0.16))
		draw_circle(c, r * 0.58, Color(0.95, 0.30, 0.25))
		draw_circle(c, r * 0.30, Color(1.0, 0.78, 0.45))
		if kind == "gate-closed":
			draw_line(c + Vector2(-r, -r) * 0.8, c + Vector2(r, r) * 0.8, Color(0.08, 0.05, 0.04), 26)
			draw_line(c + Vector2(-r, -r) * 0.8, c + Vector2(r, r) * 0.8, Color(0.86, 0.66, 0.30), 12)
