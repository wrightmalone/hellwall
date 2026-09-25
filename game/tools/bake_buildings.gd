extends SceneTree
# Bakes KayKit Medieval Hexagon buildings (CC0) into building sprites: one PNG
# per building kind in res://art/baked/buildings/, drawn from the terrain's own
# isometric angle at its scale (132 px across a tile's diamond, shown at half
# size like the tiles), each model fitted to its footprint, and buildings.json
# with where each one's footprint centre lands in its image. Also a
# scaffolding sprite per footprint size, for buildings still going up.
# Run with a display:
#   Godot --path game --script res://tools/bake_buildings.gd
# `-- House` (or any prefix) bakes only the ones whose names start with it.

const K := "res://art/kaykit-medieval/"
const PX_PER_UNIT := 132.0 / sqrt(2.0) # a unit square's diagonal spans the diamond's 132 px

# name (a BuildingKind, or scaffold-N), model, footprint in tiles, share of the footprint's width it fills, yaw in degrees
var items := [
	["Keep", "red/building_castle_red", 3, 0.95, 0],
	["House", "red/building_home_A_red", 2, 0.8, 0],
	["Cottage", "red/building_home_B_red", 2, 0.85, 0],
	["Manor", "red/building_tavern_red", 2, 0.95, 0],
	["Woodcutter", "red/building_lumbermill_red", 2, 0.95, 0],
	["Quarry", "red/building_blacksmith_red", 2, 0.9, 0],
	["Mine", "red/building_mine_red", 2, 0.95, 0],
	["SilverMine", "blue/building_mine_blue", 2, 0.95, 0],
	["Hunter", "red/building_archeryrange_red", 2, 0.95, 0],
	["Fishery", "red/building_watermill_red", 2, 0.95, 0],
	["Farm", "red/building_windmill_red", 3, 0.7, 0],
	["Shrine", "red/building_church_red", 2, 0.95, 0],
	["Scriptorium", "blue/building_market_blue", 2, 0.95, 0],
	["Barracks", "red/building_barracks_red", 3, 0.95, 0],
	["Watchtower", "red/building_tower_A_red", 2, 0.6, 0],
	["LanceTower", "red/building_tower_B_red", 2, 0.66, 0],
	["Bombard", "red/building_tower_catapult_red", 2, 0.85, 0],
	["Belfry", "blue/building_church_blue", 2, 0.95, 0],
	["Skyspire", "blue/building_tower_B_blue", 1, 0.95, 0],
	["Censer", "blue/building_tower_A_blue", 1, 0.95, 0],
	["Wardstone", "blue/building_tower_base_blue", 1, 0.9, 0],
	["scaffold-1", "neutral/building_scaffolding", 1, 0.95, 0],
	["scaffold-2", "neutral/building_scaffolding", 2, 0.95, 0],
	["scaffold-3", "neutral/building_scaffolding", 3, 0.95, 0],
	["ruin", "neutral/building_destroyed", 2, 0.95, 0],
]

var viewport: SubViewport
var camera: Camera3D

func _initialize():
	viewport = SubViewport.new()
	viewport.transparent_bg = true
	viewport.own_world_3d = true
	viewport.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	viewport.msaa_3d = Viewport.MSAA_4X
	root.add_child(viewport)
	camera = Camera3D.new()
	camera.projection = Camera3D.PROJECTION_ORTHOGONAL
	camera.keep_aspect = Camera3D.KEEP_HEIGHT
	viewport.add_child(camera)
	var sun := DirectionalLight3D.new()
	sun.rotation_degrees = Vector3(-55, 20, 0)
	sun.light_energy = 1.1
	viewport.add_child(sun)
	var env := WorldEnvironment.new()
	env.environment = Environment.new()
	env.environment.background_mode = Environment.BG_CLEAR_COLOR
	env.environment.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	env.environment.ambient_light_color = Color(1, 1, 1)
	env.environment.ambient_light_energy = 0.6
	viewport.add_child(env)
	bake.call_deferred()

func bake():
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path("res://art/baked/buildings"))
	var only := OS.get_cmdline_user_args()[0] if OS.get_cmdline_user_args().size() > 0 else ""
	var path := "res://art/baked/buildings/buildings.json"
	var meta := {}
	if only != "" and FileAccess.file_exists(path):
		meta = JSON.parse_string(FileAccess.get_file_as_string(path))
	var count := 0
	for it in items:
		if not it[0].begins_with(only):
			continue
		meta[it[0]] = await bake_one(it)
		count += 1
	var f := FileAccess.open(path, FileAccess.WRITE)
	f.store_string(JSON.stringify(meta))
	print("baked ", count, " building sprites")
	quit()

func bake_one(it) -> Array:
	var tiles: int = it[2]
	var model: Node3D = load(K + it[1] + ".gltf").instantiate()
	viewport.add_child(model)
	model.rotation_degrees.y = it[4]
	var height := fit(model, tiles * it[3])
	# The canvas: the footprint's diamond across, and tall enough for the model above it.
	var width := tiles * 132 + 24
	var tall := int(tiles * 66 + height * PX_PER_UNIT * cos(deg_to_rad(30.0)) + 40)
	viewport.size = Vector2i(width, tall)
	camera.size = tall / PX_PER_UNIT
	# Aim so the footprint centre sits a half-diamond above the canvas bottom.
	var pitch := deg_to_rad(30.0)
	var dir := Vector3(1, 0, 1).normalized() * cos(pitch) + Vector3(0, sin(pitch), 0)
	var drop := (tall / 2.0 - tiles * 33 - 12) / PX_PER_UNIT # canvas units from centre to where the base should be
	var target := Vector3(0, drop / cos(pitch), 0)
	camera.look_at_from_position(target + dir * 20.0, target)
	await RenderingServer.frame_post_draw
	await RenderingServer.frame_post_draw
	var img := viewport.get_texture().get_image()
	img.convert(Image.FORMAT_RGBA8)
	img.save_png("res://art/baked/buildings/" + it[0] + ".png")
	var base := camera.unproject_position(Vector3.ZERO)
	model.queue_free()
	await process_frame
	return [base.x, base.y]

# Scale so the model's wider horizontal side spans `across` tiles (a tile is one unit), centred on the origin, base on the ground. Returns its height.
func fit(model: Node3D, across: float) -> float:
	var box := AABB()
	var first := true
	for m in model.find_children("*", "MeshInstance3D", true, false):
		var b: AABB = m.global_transform * m.get_aabb()
		box = b if first else box.merge(b)
		first = false
	if first:
		return 1.0
	var k := across / maxf(box.size.x, box.size.z)
	model.scale *= k
	model.position = Vector3(-(box.position.x + box.size.x / 2) * k, -box.position.y * k, -(box.position.z + box.size.z / 2) * k)
	return box.size.y * k
