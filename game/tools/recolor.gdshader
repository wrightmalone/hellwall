shader_type spatial;
// Recolours a Kenney colormap model toward `tint`, keeping its shading and
// detail: the texture's brightness times the tint, mixed in by `amount`.
uniform sampler2D albedo_tex : source_color, filter_nearest;
uniform vec4 tint : source_color = vec4(1.0);
uniform float amount = 0.85;

void fragment() {
	vec3 base = texture(albedo_tex, UV).rgb;
	float lum = dot(base, vec3(0.3, 0.59, 0.11));
	ALBEDO = mix(base, tint.rgb * (0.45 + lum * 1.1), amount);
}
