extends SceneTree
# Bakes the walls and gates from KayKit Medieval Hexagon (CC0) in the pieces our walls join
# from: a post on the tile, and an arm half a tile long toward each neighbour it joins
# (north, east, south, west). A gate is three tiles long, one way or the other: wall either side
# of a doorway in the middle, its doors baked in frames from shut to open. KayKit's own
# walls sit on hexagon edges, so each straight piece is stretched to fit our square grid.
# Every piece is drawn from the terrain's isometric angle at its scale (132 px across a tile's
# diamond, shown at half size) on the same canvas, with the tile's centre at the same point
# (gates on a bigger canvas of their own): walls.json says where. Run with a display:
#   Godot --path game --script res://tools/bake_walls.gd

const K := "res://art/kaykit-medieval/neutral/"
const PX_PER_UNIT := 132.0 / sqrt(2.0)
const W := 156
const TALL := 176
const OVERLAP := 0.06 # arms reach a little into the post, so no seam shows
const GW := 280 # the gates' canvas: three tiles along one axis
const GTALL := 250
const GATE_FOOT := 80 # the doorway's centre this far above the gates' canvas bottom
const FRAMES := 5 # doors shut (0) to open (FRAMES - 1)

# set, model for arms and post, gate model, height, thickness (tile units). "proc:" models are
# built here from shapes: KayKit's wooden fence, stretched to a wall, read as salmon brick, so
# timber is a palisade of sharpened logs, as in They Are Billions.
var sets := [
	["wood", "proc:palisade", "proc:palisade-gate", 0.42, 0.2],
	["stone", "wall_straight", "wall_straight_gate", 0.62, 0.36],
]

var viewport: SubViewport
var camera: Camera3D

func _initialize():
	viewport = SubViewport.new()
	viewport.transparent_bg = true
	viewport.own_world_3d = true
	viewport.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	viewport.msaa_3d = Viewport.MSAA_4X
	viewport.size = Vector2i(W, TALL)
	root.add_child(viewport)
	camera = Camera3D.new()
	camera.projection = Camera3D.PROJECTION_ORTHOGONAL
	camera.keep_aspect = Camera3D.KEEP_HEIGHT
	viewport.add_child(camera)
	# The tile's centre a half-diamond and a margin above the canvas bottom.
	aim(TALL, 33 + 12)
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

# The camera from the terrain's angle, for a canvas `tall` high with the tile's centre `foot` above its bottom.
func aim(tall: int, foot: float):
	camera.size = tall / PX_PER_UNIT
	var pitch := deg_to_rad(30.0)
	var dir := Vector3(1, 0, 1).normalized() * cos(pitch) + Vector3(0, sin(pitch), 0)
	var drop := (tall / 2.0 - foot) / PX_PER_UNIT
	var target := Vector3(0, drop / cos(pitch), 0)
	camera.look_at_from_position(target + dir * 20.0, target)

func bake():
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path("res://art/baked/walls"))
	var count := 0
	for s in sets:
		var h: float = s[3]
		var t: float = s[4]
		var arm := 0.5 + OVERLAP
		# Arms: centred half-way out, along x (east, west) or z (south, north).
		await piece(s[0] + "-arm-e", s[1], Vector3(arm / 2, 0, 0), 0, arm, h, t)
		await piece(s[0] + "-arm-w", s[1], Vector3(-arm / 2, 0, 0), 0, arm, h, t)
		await piece(s[0] + "-arm-s", s[1], Vector3(0, 0, arm / 2), 90, arm, h, t)
		await piece(s[0] + "-arm-n", s[1], Vector3(0, 0, -arm / 2), 90, arm, h, t)
		# The post: a pillar a little taller and wider than the wall.
		await piece(s[0] + "-post", s[1], Vector3.ZERO, 0, t + 0.08, h + 0.08, t + 0.08)
		count += 5
	var base := camera.unproject_position(Vector3.ZERO)
	# The gates, on their own canvas: three tiles east to west (x) or north to south (z), centred
	# on the doorway, in frames from shut to open.
	viewport.size = Vector2i(GW, GTALL)
	aim(GTALL, GATE_FOOT)
	await RenderingServer.frame_post_draw
	for s in sets:
		for axis in [["x", 0.0], ["z", 90.0]]:
			for frame in FRAMES:
				await gate("%s-gate-%s-%d" % [s[0], axis[0], frame], s, axis[1], frame / float(FRAMES - 1))
				count += 1
	# The build menu's pictures: a short straight stretch of each wall (a gate's is its shut frame).
	for s in sets:
		await wall_icon(s[0] + "-icon", s)
		count += 1
	var gate_base := camera.unproject_position(Vector3.ZERO)
	var f := FileAccess.open("res://art/baked/walls/walls.json", FileAccess.WRITE)
	f.store_string(JSON.stringify({"base": [base.x, base.y], "size": [W, TALL], "gateBase": [gate_base.x, gate_base.y], "gateSize": [GW, GTALL], "gateFrames": FRAMES}))
	print("baked ", count, " wall pieces; tile centre at ", base, ", gates' at ", gate_base)
	quit()

# A gate three tiles long along x, turned `yaw` about the up axis: a stretch of wall either side,
# a post each side of the doorway, a beam over it, and two doors `open` (0 to 1) swung back.
func gate(name: String, s: Array, yaw: float, open: float):
	var outer := Node3D.new()
	outer.rotation_degrees.y = yaw
	var h: float = s[3]
	var t: float = s[4]
	var r := t * 0.5
	var high := h * 1.3 # the posts stand above the wall
	if s[0] == "wood":
		var bark := mat(Color(0.36, 0.23, 0.13))
		var cut := mat(Color(0.62, 0.47, 0.3))
		for side in [-1, 1]:
			var run := palisade(1.0 + OVERLAP, h, t, false, false)
			run.position.x = side * 1.0
			outer.add_child(run)
			log_at(outer, side * 0.5, r * 1.4, high, bark, cut)
		var beam := CylinderMesh.new()
		beam.top_radius = r * 0.55
		beam.bottom_radius = r * 0.55
		beam.height = 1.1
		add_mesh(outer, beam, bark, Vector3(0, high * 0.92, 0), Vector3(0, 0, 90))
		doors(outer, 0.5 - r * 1.2, h * 0.9, t * 0.5, open)
	else:
		var stone := mat(Color(0.66, 0.72, 0.78)) # KayKit's pale blue-grey
		for side in [-1, 1]:
			var run := await fitted(s[1], 1.0 + OVERLAP, h, t)
			run.position.x = side * 1.0
			outer.add_child(run)
			var tower := await fitted(s[1], t + 0.12, high + 0.06, t + 0.12)
			tower.position.x = side * 0.5
			outer.add_child(tower)
		var lintel := BoxMesh.new()
		lintel.size = Vector3(1.0, h * 0.16, t * 0.9)
		add_mesh(outer, lintel, stone, Vector3(0, high - h * 0.1, 0), Vector3.ZERO)
		doors(outer, 0.5 - t * 0.55, high - h * 0.18, t * 0.4, open)
	viewport.add_child(outer)
	await RenderingServer.frame_post_draw
	await RenderingServer.frame_post_draw
	save(name)
	outer.queue_free()
	await process_frame

# Two tiles of straight wall along x with a post at each end, for the build menu.
func wall_icon(name: String, s: Array):
	var outer := Node3D.new()
	var h: float = s[3]
	var t: float = s[4]
	if s[0] == "wood":
		outer.add_child(palisade(2.0, h, t, false, false))
		for side in [-1, 1]:
			var post := palisade(0.0, h, t, false, true)
			post.position.x = side * 1.0
			outer.add_child(post)
	else:
		outer.add_child(await fitted(s[1], 2.0, h, t))
		for side in [-1, 1]:
			var post := await fitted(s[1], t + 0.08, h + 0.08, t + 0.08)
			post.position.x = side * 1.0
			outer.add_child(post)
	viewport.add_child(outer)
	await RenderingServer.frame_post_draw
	await RenderingServer.frame_post_draw
	save(name)
	outer.queue_free()
	await process_frame

# Two plank doors with iron straps, hung at x = -half and +half, meeting in the middle when shut,
# swung back (away from the camera, toward -z) by `open` of the way to 85 degrees.
func doors(root3: Node3D, half: float, height: float, thick: float, open: float):
	var wood := mat(Color(0.46, 0.3, 0.16))
	var iron := mat(Color(0.22, 0.22, 0.24))
	for side in [-1, 1]:
		var hinge := Node3D.new()
		hinge.position = Vector3(side * half, 0, 0)
		# Left door (side -1) reaches +x from its hinge and turns +; right, -x and -: both swing to -z.
		hinge.rotation_degrees.y = -side * open * 85.0
		root3.add_child(hinge)
		var leaf := BoxMesh.new()
		leaf.size = Vector3(half, height, thick)
		add_mesh(hinge, leaf, wood, Vector3(-side * half / 2, height / 2, 0), Vector3.ZERO)
		for strap in [0.22, 0.7]:
			var band := BoxMesh.new()
			band.size = Vector3(half * 0.96, height * 0.06, thick * 1.2)
			add_mesh(hinge, band, iron, Vector3(-side * half / 2, height * strap, 0), Vector3.ZERO)

# A KayKit model stretched to length (along x) x height x thickness, standing on the ground at the origin.
func fitted(model_name: String, length: float, height: float, thick: float) -> Node3D:
	var holder := Node3D.new()
	var mid := Node3D.new()
	holder.add_child(mid)
	var model: Node3D = load(K + model_name + ".gltf").instantiate()
	mid.add_child(model)
	viewport.add_child(holder)
	await process_frame
	var box := bounds(model)
	if box.size.z > box.size.x:
		model.rotation_degrees.y = 90
		await process_frame
		box = bounds(model)
	var sc := Vector3(length / max(box.size.x, 0.001), height / max(box.size.y, 0.001), thick / max(box.size.z, 0.001))
	mid.scale = sc
	var c := box.position + box.size / 2
	mid.position = Vector3(-c.x * sc.x, -box.position.y * sc.y, -c.z * sc.z)
	viewport.remove_child(holder)
	return holder

# One piece: `model` turned so its length runs along x, stretched to length x height x thickness,
# then turned `yaw` degrees about the up axis and centred on `at`.
func piece(name: String, model_name: String, at: Vector3, yaw: float, length: float, height: float, thick: float):
	var outer := Node3D.new()
	var mid := Node3D.new()
	outer.add_child(mid)
	if model_name.begins_with("proc:"):
		# Built to size along x, standing on the ground: nothing to fit.
		mid.add_child(palisade(length, height, thick, model_name.ends_with("gate"), name.ends_with("post")))
		outer.rotation_degrees.y = yaw
		outer.position = at
		viewport.add_child(outer)
		await RenderingServer.frame_post_draw
		await RenderingServer.frame_post_draw
		save(name)
		outer.queue_free()
		await process_frame
		return
	var model: Node3D = load(K + model_name + ".gltf").instantiate()
	mid.add_child(model)
	viewport.add_child(outer)
	await process_frame
	var box := bounds(model)
	# KayKit's pieces lie along x or z; make x the long way.
	if box.size.z > box.size.x:
		model.rotation_degrees.y = 90
		await process_frame
		box = bounds(model)
	var s := Vector3(length / max(box.size.x, 0.001), height / max(box.size.y, 0.001), thick / max(box.size.z, 0.001))
	mid.scale = s
	var c := box.position + box.size / 2
	mid.position = Vector3(-c.x * s.x, -box.position.y * s.y, -c.z * s.z)
	outer.rotation_degrees.y = yaw
	outer.position = at
	await RenderingServer.frame_post_draw
	await RenderingServer.frame_post_draw
	save(name)
	outer.queue_free()
	await process_frame

func save(name: String):
	var img := viewport.get_texture().get_image()
	img.convert(Image.FORMAT_RGBA8)
	img.save_png("res://art/baked/walls/" + name + ".png")

func mat(c: Color) -> StandardMaterial3D:
	var m := StandardMaterial3D.new()
	m.albedo_color = c
	m.roughness = 0.9
	return m

# A timber palisade along x, `length` long, centred on the origin, standing on the ground:
# sharpened logs side by side (a little uneven, so it reads as timber, not a fence), bound
# with a dark band. A gate has logs at each end and plank doors with iron straps between,
# under a log lintel. A post is one thicker log.
func palisade(length: float, height: float, thick: float, gate: bool, post: bool) -> Node3D:
	var root3 := Node3D.new()
	var bark := mat(Color(0.36, 0.23, 0.13))
	var cut := mat(Color(0.62, 0.47, 0.3))
	var band := mat(Color(0.2, 0.13, 0.07))
	var r := thick * 0.5
	if post:
		log_at(root3, 0.0, r * 1.25, height * 1.12, bark, cut)
		return root3
	var spacing := r * 1.85
	var n := int(ceil(length / spacing))
	var start := -length / 2 + spacing / 2
	for i in n:
		var x := start + i * spacing
		if gate and abs(x) < length * 0.26:
			continue # the doorway
		var h := height * (0.9 + 0.14 * fmod(abs(sin(i * 12.9898)) * 43758.5453, 1.0))
		log_at(root3, x, r, h, bark, cut)
	if not gate:
		var tie := BoxMesh.new()
		tie.size = Vector3(length, height * 0.08, thick * 1.02)
		add_mesh(root3, tie, band, Vector3(0, height * 0.35, 0), Vector3.ZERO)
		return root3
	# The gate: two plank doors with iron straps, and a log across the top.
	var wood := mat(Color(0.46, 0.3, 0.16))
	var iron := mat(Color(0.22, 0.22, 0.24))
	for side in [-1, 1]:
		var leaf := BoxMesh.new()
		leaf.size = Vector3(length * 0.25, height * 0.82, thick * 0.55)
		add_mesh(root3, leaf, wood, Vector3(side * length * 0.13, height * 0.41, 0), Vector3.ZERO)
		for strap in [0.25, 0.62]:
			var s3 := BoxMesh.new()
			s3.size = Vector3(length * 0.25, height * 0.05, thick * 0.6)
			add_mesh(root3, s3, iron, Vector3(side * length * 0.13, height * strap, 0), Vector3.ZERO)
	var lintel := CylinderMesh.new()
	lintel.top_radius = r * 0.45
	lintel.bottom_radius = r * 0.45
	lintel.height = length * 0.62
	add_mesh(root3, lintel, bark, Vector3(0, height * 0.9, 0), Vector3(0, 0, 90))
	return root3

func log_at(root3: Node3D, x: float, r: float, h: float, bark: Material, cut: Material):
	var body := CylinderMesh.new()
	body.top_radius = r
	body.bottom_radius = r * 1.05
	body.height = h
	body.radial_segments = 8
	add_mesh(root3, body, bark, Vector3(x, h / 2, 0), Vector3.ZERO)
	var tip := CylinderMesh.new() # sharpened to a point
	tip.top_radius = 0.0
	tip.bottom_radius = r
	tip.height = r * 1.7
	tip.radial_segments = 8
	add_mesh(root3, tip, cut, Vector3(x, h + r * 0.85, 0), Vector3.ZERO)

func add_mesh(root3: Node3D, mesh: Mesh, m: Material, at: Vector3, rot: Vector3):
	var mi := MeshInstance3D.new()
	mi.mesh = mesh
	mi.material_override = m
	mi.position = at
	mi.rotation_degrees = rot
	root3.add_child(mi)

func bounds(model: Node3D) -> AABB:
	var box := AABB()
	var first := true
	for m in model.find_children("*", "MeshInstance3D", true, false):
		var b: AABB = m.global_transform * m.get_aabb()
		box = b if first else box.merge(b)
		first = false
	return box
