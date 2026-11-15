shader_type spatial;
render_mode specular_disabled, cull_back;

// Shared graphic-novel paint. Light is irradiance; pigment is applied only once.
uniform vec4 albedo_color : source_color = vec4(0.8, 0.8, 0.8, 1.0);
uniform vec3 shadow_tint : source_color = vec3(0.31, 0.27, 0.42);
uniform float shadow_scale = 0.72;
uniform float band_edge = 0.035;
uniform float key_threshold = 0.55;

void fragment() {
	ALBEDO = albedo_color.rgb;
	ROUGHNESS = 0.92;
	METALLIC = 0.0;
	SPECULAR = 0.0;
	EMISSION = albedo_color.rgb * shadow_tint * 0.10;
}

void light() {
	float ndl = dot(NORMAL, LIGHT);
	float lit = smoothstep(-band_edge, band_edge, ndl - 0.08);
	float key = smoothstep(key_threshold - band_edge, key_threshold + band_edge, ndl);

	float band = mix(0.12, 0.72 + 0.28 * key, lit);
	vec3 tint = mix(vec3(0.60, 0.65, 0.85), vec3(1.0), lit);
	DIFFUSE_LIGHT += tint * band * LIGHT_COLOR * ATTENUATION / PI;
}
