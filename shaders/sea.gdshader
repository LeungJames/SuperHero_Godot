shader_type spatial;
render_mode unshaded, cull_disabled;

uniform vec4 deep_color : source_color = vec4(0.055,0.19,0.26,1.0);
uniform vec4 shallow_color : source_color = vec4(0.16,0.34,0.40,1.0);
uniform vec4 reflection_color : source_color = vec4(1.0,0.67,0.25,1.0);
uniform vec3 sun_direction = vec3(-0.60,0.085,-0.795);
varying vec3 world_position;

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1,311.7))) * 43758.5453); }
float noise(vec2 p) {
    vec2 i=floor(p),f=fract(p);f=f*f*(3.0-2.0*f);
    return mix(mix(hash(i),hash(i+vec2(1,0)),f.x),mix(hash(i+vec2(0,1)),hash(i+vec2(1,1)),f.x),f.y);
}
void vertex() { world_position=(MODEL_MATRIX*vec4(VERTEX,1.0)).xyz; }
void fragment() {
    vec2 p=world_position.xz;
    float wave=sin(p.x*0.54+p.y*0.83+TIME*0.68)*0.4 + sin(p.x*1.27-p.y*0.58-TIME*0.51)*0.22;
    float broad=noise(p*0.055+vec2(TIME*0.009,0.0));
    float streak=noise(vec2(p.x*0.17+wave*0.6,p.y*1.6+TIME*0.19));
    vec3 normal=normalize(vec3(wave*0.08,1.0,cos(p.y*0.77+TIME*0.6)*0.045));
    vec3 view_dir=normalize(CAMERA_POSITION_WORLD-world_position);
    vec3 half_dir=normalize(view_dir+normalize(sun_direction));
    float sun_glint=pow(max(dot(normal,half_dir),0.0),140.0);
    // Broken, graphic reflection rather than a continuous plastic highlight.
    float glint=smoothstep(0.17,0.58,sun_glint)*smoothstep(0.40,0.68,streak);
    float glint_halo=sun_glint*0.17;
    float lines=smoothstep(0.72,0.87,streak)*0.13;
    vec3 water=mix(deep_color.rgb,shallow_color.rgb,broad*0.65+lines);
    float fresnel=pow(1.0-max(view_dir.y,0.0),3.0);
    water=mix(water,vec3(0.30,0.43,0.47),fresnel*0.30);
    ALBEDO=mix(water,reflection_color.rgb,min(glint+glint_halo,0.94));
    EMISSION=reflection_color.rgb*glint*0.22;
}
