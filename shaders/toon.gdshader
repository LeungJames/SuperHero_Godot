shader_type spatial;
render_mode specular_disabled, cull_back;

// 美漫卡通着色（设计文档 §8.3）：NdotL 两阶硬切，阴影 = 基色 × 0.72 并偏冷紫。
// 作为关卡 / 道具材质的 next_pass 之前的主材质使用，描边由 outline.gdshader 叠加。
uniform vec4 albedo_color : source_color = vec4(0.8, 0.8, 0.8, 1.0);
uniform vec3 shadow_tint : source_color = vec3(0.42, 0.35, 0.47);
uniform float shadow_scale = 0.72;
uniform float band_edge = 0.025;
uniform float key_threshold = 0.55;

void fragment() {
	ALBEDO = albedo_color.rgb;
	ROUGHNESS = 1.0;
	METALLIC = 0.0;
}

void light() {
	float ndl = dot(NORMAL, LIGHT);
	float lit = smoothstep(-band_edge, band_edge, ndl);
	float key = smoothstep(key_threshold - band_edge, key_threshold + band_edge, ndl);

	// 阴影：基色压暗后向冷紫偏移；亮面：基色，高光阶再提一点
	vec3 base = albedo_color.rgb;
	vec3 shadow = mix(base * shadow_scale, shadow_tint * dot(base, vec3(0.333)), 0.35);
	vec3 tone = mix(shadow, base * (0.9 + 0.1 * key), lit);

	DIFFUSE_LIGHT += tone * LIGHT_COLOR * ATTENUATION / PI;
}
