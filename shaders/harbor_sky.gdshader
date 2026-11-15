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
	float longitude = atan(dir.x, dir.z);
	float brush = 0.0;
	for (int i = 0; i < 15; i++) {
		float k = float(i);
		float center = k * 0.43 - 3.14;
		float altitude = 0.13 + 0.24 * (0.5 + 0.5 * sin(k * 7.31));
		float width = 0.17 + 0.11 * (0.5 + 0.5 * sin(k * 2.7));
		float dx = atan(sin(longitude - center), cos(longitude - center)) / width;
		float dy = (height - altitude) / (0.028 + 0.016 * sin(k * 1.7) * sin(k * 1.7));
		brush += exp(-(dx * dx + dy * dy) * 1.8);
	}
	float mask = smoothstep(0.12, 0.65, brush) * smoothstep(0.035, 0.10, height) * (1.0 - smoothstep(0.42, 0.62, height));
	vec3 cloud = mix(vec3(0.48, 0.23, 0.16), vec3(0.12, 0.14, 0.22), smoothstep(0.10, 0.45, height));
	// Broad, quiet brush masses keep the horizon readable behind targets.
	COLOR = mix(COLOR, cloud * (1.0 - dusk * 0.3), mask * 0.64);
	COLOR = mix(vec3(0.14, 0.22, 0.29), COLOR, smoothstep(-0.05, 0.015, dir.y));
}
