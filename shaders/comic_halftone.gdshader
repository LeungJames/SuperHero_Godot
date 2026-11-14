shader_type canvas_item;
render_mode unshaded;

// 网点：屏幕空间外圈网点，受击 / 击杀 / 低血量时叠上来（美漫半调）。
uniform vec4 ink : source_color = vec4(0.08, 0.06, 0.10, 1.0);
uniform float spacing : hint_range(4.0, 48.0) = 14.0;
uniform float dot_radius : hint_range(0.05, 0.5) = 0.30;
uniform float strength : hint_range(0.0, 1.0) = 0.0;
uniform float edge_start : hint_range(0.0, 1.0) = 0.55;

void fragment() {
	vec2 cell = fract(FRAGCOORD.xy / spacing) - 0.5;
	float dots = smoothstep(dot_radius, dot_radius - 0.07, length(cell));

	vec2 s = SCREEN_UV - 0.5;
	float weight = smoothstep(edge_start, 1.0, max(abs(s.x), abs(s.y)) * 2.0);

	COLOR = vec4(ink.rgb, dots * weight * strength);
}
