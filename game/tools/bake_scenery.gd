extends SceneTree
# Bakes Quaternius Stylized Nature MegaKit models (CC0) into terrain
# decorations: one PNG per variant in res://art/baked/scenery/, drawn from the
# terrain's own isometric angle at its scale (132 px across a tile's diamond,
# shown at half size like the tiles), and scenery.json with where each one's
# base point lands in its image. Run with a display:
#   Godot --path game --script res://tools/bake_scenery.gd

const N := "res://art/nature/"
const PX_PER_UNIT := 132.0 / sqrt(2.0) # a unit square's diagonal spans the diamond's 132 px

# name, model, height in tiles, canvas size, yaw
var items := [
	["tree-1", "CommonTree_1", 1.9, Vector2i(200, 240), 0], ["tree-2", "CommonTree_2", 1.8, Vector2i(200, 240), 60],
	["tree-3", "CommonTree_3", 2.0, Vector2i(200, 240), 120], ["tree-4", "CommonTree_4", 1.8, Vector2i(200, 240), 200],
	["tree-5", "CommonTree_5", 1.9, Vector2i(200, 240), 280],
	["pine-1", "Pine_1", 2.1, Vector2i(180, 250), 0], ["pine-2", "Pine_2", 2.0, Vector2i(180, 250), 90],
	["pine-3", "Pine_3", 2.2, Vector2i(180, 250), 180], ["pine-4", "Pine_4", 1.9, Vector2i(180, 250), 45],
	["pine-5", "Pine_5", 2.0, Vector2i(180, 250), 270],
	["rock-1", "Rock_Medium_1", 0.55, Vector2i(170, 140), 0], ["rock-2", "Rock_Medium_2", 0.6, Vector2i(170, 140), 70],
	["rock-3", "Rock_Medium_3", 0.5, Vector2i(170, 140), 140], ["rock-4", "Rock_Medium_1", 0.5, Vector2i(170, 140), 220],
	["tuft-1", "Grass_Common_Short", 0.22, Vector2i(110, 80), 0], ["tuft-2", "Flower_3_Group", 0.22, Vector2i(110, 80), 0],
	["tuft-3", "Flower_4_Group", 0.22, Vector2i(110, 80), 90], ["tuft-4", "Grass_Common_Short", 0.28, Vector2i(110, 80), 120],
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
	var meta := {}
	for it in items:
		meta[it[0]] = await bake_one(it)
	var f := FileAccess.open("res://art/baked/scenery/scenery.json", FileAccess.WRITE)
	f.store_string(JSON.stringify(meta))
	print("baked ", items.size(), " scenery sprites")
	quit()

func bake_one(it) -> Array:
	var size: Vector2i = it[3]
	viewport.size = size
	camera.size = size.y / PX_PER_UNIT
	# Look at a point above the base so the model fills the canvas; its base then lands low in the image.
	var target := Vector3(0, it[2] * 0.42, 0)
	var pitch := deg_to_rad(30.0)
	var dir := Vector3(1, 0, 1).normalized() * cos(pitch) + Vector3(0, sin(pitch), 0)
	camera.look_at_from_position(target + dir * 20.0, target)
	var model: Node3D = load(N + it[1] + ".gltf").instantiate()
	viewport.add_child(model)
	model.rotation_degrees.y = it[4]
	fit(model, it[2])
	await RenderingServer.frame_post_draw
	await RenderingServer.frame_post_draw
	var img := viewport.get_texture().get_image()
	img.convert(Image.FORMAT_RGBA8)
	img.save_png("res://art/baked/scenery/" + it[0] + ".png")
	var base := camera.unproject_position(Vector3.ZERO)
	model.queue_free()
	await process_frame
	return [base.x, base.y]

# Scale to `height` tiles tall (a tile is one unit), base on the ground.
func fit(model: Node3D, height: float):
	var box := AABB()
	var first := true
	for m in model.find_children("*", "MeshInstance3D", true, false):
		var b: AABB = m.global_transform * m.get_aabb()
		box = b if first else box.merge(b)
		first = false
	if first:
		return
	var k := height / box.size.y
	model.scale *= k
	model.position = Vector3(-(box.position.x + box.size.x / 2) * k, -box.position.y * k, -(box.position.z + box.size.z / 2) * k)
