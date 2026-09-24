extends SceneTree
func _init():
	for p in ["res://art/kenney3d/mini-dungeon/character-human.glb", "res://art/kenney3d/graveyard-kit/character-zombie.glb"]:
		var scene = load(p).instantiate()
		root.add_child(scene)
		print("== ", p)
		for ap in scene.find_children("*", "AnimationPlayer", true, false):
			print("anims: ", ap.get_animation_list())
		for sk in scene.find_children("*", "Skeleton3D", true, false):
			var names = []
			for i in sk.get_bone_count(): names.append(sk.get_bone_name(i))
			print("bones: ", names)
		for m in scene.find_children("*", "MeshInstance3D", true, false):
			print("mesh: ", m.name, " aabb ", m.get_aabb())
	quit()
