shader_type spatial;
render_mode unshaded, cull_front, depth_draw_opaque;

// 反向壳描边：把网格沿法线放大一圈、剔除正面，只留下剪影（设计文档 §6.12 美漫描边）。
// 作为 StandardMaterial3D 的 next_pass 使用。
uniform vec4 ink_color : source_color = vec4(0.102, 0.078, 0.063, 1.0);
uniform float width = 0.05;

void vertex() {
	float dist = length((MODELVIEW_MATRIX * vec4(VERTEX, 1.0)).xyz);
	VERTEX += NORMAL * width * clamp(dist * 0.13, 0.6, 8.0);
}

void fragment() {
	ALBEDO = ink_color.rgb;
}
