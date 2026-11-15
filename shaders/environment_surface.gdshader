shader_type spatial;
render_mode specular_disabled;

// Meter-scaled detail stays consistent across the existing modular level kit.
uniform vec4 base_color : source_color = vec4(0.32, 0.39, 0.42, 1.0);
uniform int surface_kind = 0; // 0 paint, 1 concrete, 2 cargo ribs, 3 tread plate, 4 hazard, 5 siding
uniform float detail_strength : hint_range(0.0, 1.0) = 0.65;
varying vec3 world_pos;
varying vec3 world_normal;
varying vec3 local_pos;
varying vec3 local_normal;

float hash21(vec2 p) { return fract(sin(dot(p, vec2(127.1,311.7))) * 43758.5453); }
float line(float p, float size, float width) {
    float d = abs(fract(p / size + 0.5) - 0.5) * size;
    return 1.0 - smoothstep(width, width + max(fwidth(p), 0.003), d);
}

void vertex() {
    world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    world_normal = normalize(MODEL_NORMAL_MATRIX * NORMAL);
    local_pos = VERTEX;
    local_normal = NORMAL;
}

void fragment() {
    vec3 n = abs(world_normal);
    vec2 p = n.y > 0.6 ? world_pos.xz : (n.x > n.z ? world_pos.zy : world_pos.xy);
    vec3 c = base_color.rgb;
    float seed = hash21(floor(p * 4.0));
    float grain = mix(0.96, 1.035, seed);
    float seams = max(line(p.x, 2.0, 0.012), line(p.y, 2.0, 0.012));
    float wear = 0.0;
    if (surface_kind == 1) {
        seams = max(line(p.x, 5.0, 0.014), line(p.y, 5.0, 0.014));
        c *= mix(0.94, 1.06, hash21(floor(p / 5.0)));
        float chip = step(0.988, hash21(floor(p * 15.0)));
        wear = chip * 0.10;
    } else if (surface_kind == 2 || surface_kind == 5) {
        vec3 an = abs(local_normal);
        vec2 lp = an.y > 0.6 ? local_pos.xz : (an.x > an.z ? local_pos.zy : local_pos.xy);
        float rib = smoothstep(0.20, 0.24, fract(lp.x * 3.4)) - smoothstep(0.36, 0.40, fract(lp.x * 3.4));
        float edge = line(lp.x + 0.063, 0.294, 0.007);
        c *= 0.94 + rib * 0.12;
        seams = edge * 0.42;
    } else if (surface_kind == 3) {
        vec2 tile = floor(p * 5.0);
        vec2 q = fract(p * 5.0) - 0.5;
        float flip = mod(tile.x + tile.y, 2.0) * 2.0 - 1.0;
        float diamond = abs(q.x + q.y * flip);
        float tread = (1.0 - smoothstep(0.045, 0.095, diamond)) * (1.0 - smoothstep(0.28, 0.39, abs(q.x)));
        float fade = 1.0 - smoothstep(0.08, 0.22, max(length(dFdx(p)),length(dFdy(p))));
        c += tread * 0.055 * fade;
        c *= mix(0.96, 1.035, hash21(floor(p/2.0)));
    } else if (surface_kind == 4) {
        float stripe = step(0.5, fract((p.x + p.y) * 1.4));
        c = mix(vec3(0.075,0.09,0.10), vec3(0.93,0.58,0.13), stripe);
        seams = 0.0;
    }
    c *= grain;
    c = mix(c, c * 0.48, seams * detail_strength);
    ALBEDO = max(c - wear, vec3(0.015));
    ROUGHNESS = 0.84;
}

void light() {
    float ndl = dot(NORMAL, LIGHT);
    float band = smoothstep(-0.08, 0.08, ndl);
    float sun = smoothstep(0.52, 0.58, ndl);
    vec3 cool = vec3(0.57,0.66,0.83);
    // Godot applies ALBEDO after the custom light contribution.
    vec3 tone = mix(cool * 0.64, vec3(0.86 + sun * 0.14), band);
    DIFFUSE_LIGHT += tone * LIGHT_COLOR * ATTENUATION / PI;
}
