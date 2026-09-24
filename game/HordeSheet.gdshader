shader_type canvas_item;
// One cell of a baked sheet per instance: INSTANCE_CUSTOM.x is the walk
// frame (column), .y the facing (row). The sheet is COLS x ROWS cells.
// Where in its quad a fragment is comes from the quad's own vertices (a
// QuadMesh of side QUAD, centred, y up as meshes are).
uniform float cols = 6.0;
uniform float rows = 8.0;
uniform float quad = 72.0;
varying flat vec2 cell;
varying vec2 local_uv;

void vertex() {
	cell = INSTANCE_CUSTOM.xy;
	local_uv = vec2(VERTEX.x / quad + 0.5, 0.5 - VERTEX.y / quad);
}

void fragment() {
	// Not multiplied by COLOR: in a canvas shader COLOR already holds Godot's own
	// sample of the texture at the mesh UVs, which would mask the cell.
	COLOR = texture(TEXTURE, (clamp(local_uv, 0.0, 1.0) + cell) / vec2(cols, rows));
}
