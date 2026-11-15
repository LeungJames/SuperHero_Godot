shader_type sky;
render_mode disable_fog;

uniform vec3 zenith : source_color = vec3(0.24, 0.40, 0.56);
uniform vec3 horizon : source_color = vec3(0.94, 0.63, 0.36);
uniform vec3 sun_direction = vec3(-0.60, 0.085, -0.795);
uniform float dusk = 0.0;

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float noise(vec2 p) {
	vec2 i = floor(p), f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1, 0)), f.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1)), f.x), f.y);
}
void sky() {
	vec3 dir = normalize(EYEDIR);
	float height = max(dir.y, 0.0);
	COLOR = mix(horizon, zenith, pow(smoothstep(0.0, 0.70, height), 0.58));
	vec3 sun = normalize(sun_direction);
	float distance_to_sun = length(dir - sun);
	COLOR += vec3(0.18, 0.065, 0.012) * exp(-distance_to_sun * 5.0);
	float disk = 1.0 - smoothstep(0.025, 0.028, distance_to_sun);
	COLOR = mix(COLOR, vec3(1.50, 1.10, 0.49), disk);
	// Two simple horizontal cloud layers, painted as flat graphic shapes.
	vec2 uv = vec2(atan(dir.x, dir.z) * 4.0, height * 34.0);
	float n = noise(uv * vec2(1.6, 1.0)) * 0.65 + noise(uv * vec2(4.4, 2.2)) * 0.35;
	float mask = smoothstep(0.65, 0.68, n) * smoothstep(0.025, 0.08, height) * (1.0 - smoothstep(0.35, 0.50, height));
	vec3 cloud = mix(vec3(0.55, 0.22, 0.065), vec3(0.09, 0.11, 0.18), smoothstep(0.10, 0.40, height));
	float edge = smoothstep(0.68, 0.72, n);
	cloud = mix(vec3(0.80, 0.36, 0.10), cloud, edge);
	COLOR = mix(COLOR, cloud * (1.0 - dusk * 0.3), mask * 0.70);
	COLOR = mix(vec3(0.14, 0.22, 0.29), COLOR, smoothstep(-0.05, 0.015, dir.y));
}
