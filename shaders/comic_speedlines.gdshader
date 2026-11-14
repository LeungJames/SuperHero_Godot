shader_type canvas_item;
render_mode unshaded;

// 放射状速度线：蹬墙跑 / 滑铲 / 高速移动时从屏幕边缘向中心聚集。
uniform vec4 ink : source_color = vec4(0.05, 0.04, 0.07, 1.0);
uniform float strength : hint_range(0.0, 1.0) = 0.0;
uniform float line_count : hint_range(16.0, 200.0) = 84.0;
uniform float inner : hint_range(0.1, 0.95) = 0.42;
uniform float wobble : hint_range(0.0, 2.0) = 1.0;

float hash(float n) {
	return fract(sin(n * 91.733) * 43758.5453);
}

void fragment() {
	vec2 center = 0.5 / SCREEN_PIXEL_SIZE;
	vec2 p = FRAGCOORD.xy - center;
	float r = length(p) / center.y;
	float a = atan(p.y, p.x);

	float lane = (a / 6.2831853 + 0.5) * line_count;
	float id = floor(lane);
	float f = fract(lane);
	float h = hash(id);
	float width = 0.08 + 0.42 * h;

	float on = step(abs(f - 0.5), width * 0.5);
	float fade = smoothstep(inner, inner + 0.55, r);
	float flicker = 0.6 + 0.4 * sin(TIME * (7.0 + h * 12.0) + h * 31.0);

	COLOR = vec4(ink.rgb, on * fade * strength * mix(1.0, flicker, wobble));
}
