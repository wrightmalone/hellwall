extends SceneTree
# Bakes the Kenney 3D characters into isometric sprite sheets for the game:
# 8 facings (rows) x 6 walk frames (columns), CELL px each, outlined, one PNG
# per soldier or demon type in res://art/baked/. Run with a display (not
# --headless, which can't render):
#   Godot --path game --script res://tools/bake.gd
# The camera looks down 30 degrees and round 45, which is exactly the 2:1
# isometric of the terrain tiles. Facing d points along sim angle d * 45deg
# (d = 0 is +x, screen down-right; d = 2 is +y, screen down-left).

const CELL := 72
const FRAMES := 6
const ORTHO := 1.35
const LOOK_Y := 0.45

const MD := "res://art/kenney3d/mini-dungeon/"
const KK := "res://art/kaykit/Models/Characters/gltf/"
const KW := "res://art/kaykit/Models/gltf/"
const GY := "res://art/kenney3d/graveyard-kit/"

# name, model, tint for the body (null: none), scale, [weapon right, item left]
var variants := [
	# Soldiers: KayKit's Dungeon Pack people (flat-coloured, no tint needed), half scale to match the Kenney demons.
	["unit-militia", KK + "character_rogue.gltf", null, 0.5, [KW + "dagger_common.gltf.glb", ""]],
	["unit-marksman", KK + "character_rogue.gltf", null, 0.5, [KW + "crossbow_common.gltf.glb", ""]],
	["unit-templar", KK + "character_knight.gltf", null, 0.52, [KW + "sword_common.gltf.glb", KW + "shield_common.gltf.glb"]],
	["unit-crossbowman", KK + "character_knight.gltf", null, 0.5, [KW + "crossbow_common.gltf.glb", KW + "shield_common.gltf.glb"]],
	["unit-chaplain", KK + "character_mage.gltf", null, 0.5, [KW + "staff_common.gltf.glb", ""]],
	["unit-outrider", KK + "character_barbarian.gltf", null, 0.55, [KW + "axe_common.gltf.glb", ""]],
	["demon-imp", MD + "character-orc.glb", Color(1.0, 0.35, 0.3), 0.85, ["", ""]],
	["demon-hound", GY + "character-skeleton.glb", Color(0.32, 0.26, 0.28), 0.8, ["", ""]],
	["demon-thrall", GY + "character-zombie.glb", null, 1.0, ["", ""]],
	["demon-gargoyle", GY + "character-vampire.glb", Color(0.55, 0.6, 0.75), 0.9, ["", ""]],
	["demon-bloater", GY + "character-zombie.glb", Color(0.75, 0.95, 0.35), 1.35, ["", ""]],
	["demon-brute", MD + "character-orc.glb", Color(0.6, 0.12, 0.12), 1.55, ["", ""]],
	["demon-howler", GY + "character-ghost.glb", Color(0.95, 0.5, 1.0), 1.0, ["", ""]],
	["demon-broodmother", GY + "character-keeper.glb", Color(0.45, 0.2, 0.55), 1.5, ["", ""]],
]

var viewport: SubViewport
var camera: Camera3D

func _initialize():
	viewport = SubViewport.new()
	viewport.size = Vector2i(CELL, CELL)
	viewport.transparent_bg = true
	viewport.own_world_3d = true
	viewport.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	viewport.msaa_3d = Viewport.MSAA_4X
	root.add_child(viewport)
	camera = Camera3D.new()
	camera.projection = Camera3D.PROJECTION_ORTHOGONAL
	camera.size = ORTHO
	viewport.add_child(camera)
	var pitch := deg_to_rad(30.0)
	var dir := Vector3(1, 0, 1).normalized() * cos(pitch) + Vector3(0, sin(pitch), 0)
	camera.look_at_from_position(Vector3(0, LOOK_Y, 0) + dir * 10.0, Vector3(0, LOOK_Y, 0))
	var sun := DirectionalLight3D.new()
	sun.rotation_degrees = Vector3(-55, 20, 0)
	sun.light_energy = 1.1
	viewport.add_child(sun)
	var env := WorldEnvironment.new()
	env.environment = Environment.new()
	env.environment.background_mode = Environment.BG_CLEAR_COLOR
	env.environment.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	env.environment.ambient_light_color = Color(1, 1, 1)
	env.environment.ambient_light_energy = 0.55
	viewport.add_child(env)
	bake.call_deferred()

func bake():
	# `-- unit` (or any prefix) bakes only the sheets whose names start with it.
	var only := OS.get_cmdline_user_args()[0] if OS.get_cmdline_user_args().size() > 0 else ""
	for v in variants:
		if v[0].begins_with(only):
			await bake_one(v)
	# Where the ground origin lands in a cell: the game anchors each figure's feet there.
	var feet := camera.unproject_position(Vector3.ZERO)
	var meta := FileAccess.open("res://art/baked/sheets.json", FileAccess.WRITE)
	meta.store_string(JSON.stringify({"cell": CELL, "frames": FRAMES, "directions": 8, "feet": [feet.x, feet.y]}))
	print("baked ", variants.size(), " sheets; feet at ", feet)
	quit()

func bake_one(v):
	var model: Node3D = load(v[1]).instantiate()
	model.scale = Vector3.ONE * v[3]
	viewport.add_child(model)
	if v[2] != null:
		tint(model, v[2], v[0].begins_with("demon"))
	attach(model, v[4][0], "arm-right")
	attach(model, v[4][1], "arm-left")
	# Kenney's characters come with a walk; KayKit's don't, so theirs is made from their parts below.
	var players = model.find_children("*", "AnimationPlayer", true, false)
	var player: AnimationPlayer = players[0] if players.size() > 0 else null
	var length := 1.0
	if player:
		player.play("walk")
		length = player.current_animation_length
	var sheet := Image.create(CELL * FRAMES, CELL * 8, false, Image.FORMAT_RGBA8)
	for d in 8:
		var a := d * PI / 4.0
		# Sim +x is 3D +X, sim +y is 3D +Z; the models face +Z.
		model.rotation.y = atan2(cos(a), sin(a))
		for f in FRAMES:
			if player:
				player.seek(length * f / FRAMES, true)
			else:
				stride(model, v[3], TAU * f / FRAMES)
			await RenderingServer.frame_post_draw
			await RenderingServer.frame_post_draw
			var img := viewport.get_texture().get_image()
			img.convert(Image.FORMAT_RGBA8)
			sheet.blit_rect(outline(img), Rect2i(0, 0, CELL, CELL), Vector2i(f * CELL, d * CELL))
	sheet.save_png("res://art/baked/" + v[0] + ".png")
	print("baked ", v[0])
	model.queue_free()
	await process_frame

# A walk for models with no animation (KayKit's): arms swing from the
# shoulder in opposition, the whole figure bobs twice a stride and sways.
func stride(model: Node3D, size: float, phase: float):
	var swing := sin(phase) * 0.7
	for n in model.find_children("*", "Node3D", true, false):
		var lower := String(n.name).to_lower()
		if lower.ends_with("armleft") or lower.ends_with("arnleft"):
			n.rotation.x = swing
		elif lower.ends_with("armright"):
			n.rotation.x = -swing
	model.position.y = absf(sin(phase)) * 0.06 * size
	model.rotation.z = sin(phase) * 0.05

# Recolour the model toward a tint: soldiers keep their faces (the head is
# left alone), demons are recoloured head to foot.
func tint(model: Node3D, colour: Color, whole: bool):
	var shader := load("res://tools/recolor.gdshader")
	for m in model.find_children("*", "MeshInstance3D", true, false):
		if not whole and m.name.begins_with("head"):
			continue
		for s in m.mesh.get_surface_count():
			var mat = m.get_active_material(s)
			if mat is BaseMaterial3D:
				var copy := ShaderMaterial.new()
				copy.shader = shader
				copy.set_shader_parameter("albedo_tex", mat.albedo_texture)
				copy.set_shader_parameter("tint", colour)
				copy.set_shader_parameter("amount", 0.8 if whole else 0.9)
				m.set_surface_override_material(s, copy)

# Hang a prop on a bone (rigged models) or on the limb node of the same name (the graveyard's part-animated ones).
func attach(model: Node3D, path: String, limb: String):
	if path == "":
		return
	var prop: Node3D = load(path).instantiate()
	var skeletons = model.find_children("*", "Skeleton3D", true, false)
	if skeletons.size() > 0 and skeletons[0].find_bone(limb) >= 0:
		var hold := BoneAttachment3D.new()
		hold.bone_name = limb
		skeletons[0].add_child(hold)
		hold.add_child(prop)
	else:
		# Kenney graveyard limbs are named like the bones; KayKit's are character_<who>ArmLeft/Right.
		var wanted := limb.replace("-", "").to_lower()
		var found: Node3D = null
		for n in model.find_children("*", "Node3D", true, false):
			var lower := String(n.name).to_lower()
			if lower == limb or lower.ends_with(wanted) or (wanted == "armleft" and lower.ends_with("arnleft")):
				found = n
				break
		if found == null:
			prop.free()
			return
		found.add_child(prop)
		if path.begins_with(KW):
			# KayKit props are made for these hands: at the end of the arm, pointing forward.
			if path.contains("shield"):
				# Strapped to the forearm, facing out from the body.
				prop.position = Vector3(0.12, -0.3, 0.02)
				prop.rotation_degrees = Vector3(0, 90, 0)
			else:
				# Gripped at the end of the arm and held up in front, clear of the body.
				prop.position = Vector3(0, -0.42, 0.1)
				prop.rotation_degrees = Vector3(115, 0, 0)
			return
	prop.scale = Vector3.ONE * 0.7
	prop.position = Vector3(0.25 if limb == "arm-left" else -0.25, -0.05, 0.05)

# A one-pixel dark edge round the figure: it's what makes a small figure stand off busy ground.
func outline(img: Image) -> Image:
	var out := img.duplicate()
	var w := img.get_width()
	var h := img.get_height()
	for y in h:
		for x in w:
			if img.get_pixel(x, y).a > 0.3:
				continue
			var near := false
			for o in [Vector2i(1, 0), Vector2i(-1, 0), Vector2i(0, 1), Vector2i(0, -1)]:
				var p: Vector2i = Vector2i(x, y) + o
				if p.x >= 0 and p.y >= 0 and p.x < w and p.y < h and img.get_pixel(p.x, p.y).a > 0.3:
					near = true
					break
			if near:
				out.set_pixel(x, y, Color(0.08, 0.05, 0.06, 0.95))
	return out
