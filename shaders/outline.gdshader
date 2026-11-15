shader_type spatial;
render_mode unshaded, cull_front, depth_draw_opaque;

// 反向壳描边：把网格沿法线放大一圈、剔除正面，只留下剪影（设计文档 §6.12 美漫描边）。
// 作为 StandardMaterial3D 的 next_pass 使用。
uniform vec4 ink_color : source_color = vec4(0.065, 0.055, 0.080, 1.0);
uniform float width = 0.05;
uniform float pixel_width = 0.0;

void vertex() {
	vec4 view_position = MODELVIEW_MATRIX * vec4(VERTEX, 1.0);
	vec3 view_normal = normalize(MODELVIEW_NORMAL_MATRIX * NORMAL);
	vec4 clip = PROJECTION_MATRIX * view_position;
	vec2 direction = (PROJECTION_MATRIX * vec4(view_normal, 0.0)).xy;
	float weight = pixel_width > 0.0 ? pixel_width : clamp(width * 65.0, 0.70, 1.65);
	float distant_fade = mix(1.0, 0.55, smoothstep(22.0, 90.0, -view_position.z));
	clip.xy += normalize(direction + vec2(0.00001)) * weight * distant_fade * 2.0 / VIEWPORT_SIZE * clip.w;
	POSITION = clip;
}

void fragment() {
	ALBEDO = ink_color.rgb;
}
