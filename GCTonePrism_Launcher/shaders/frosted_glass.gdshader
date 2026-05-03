shader_type canvas_item;

uniform sampler2D SCREEN_TEXTURE : hint_screen_texture, filter_linear, repeat_disable;
uniform float blur_radius : hint_range(0.0, 60.0) = 20.0;
uniform vec4 tint_color : source_color = vec4(0.0, 0.0, 0.0, 1.0);
uniform float tint_strength : hint_range(0.0, 1.0) = 0.5;

// Vogel disk (golden-angle spiral) で 49 点を擬似一様に配置
const float SAMPLES = 49.0;
const float GOLDEN_ANGLE = 2.39996322972865332;

void fragment() {
	vec3 col = vec3(0.0);
	for (float i = 0.0; i < SAMPLES; i += 1.0) {
		float r = sqrt((i + 0.5) / SAMPLES) * blur_radius;
		float angle = i * GOLDEN_ANGLE;
		vec2 offset = vec2(cos(angle), sin(angle)) * r * SCREEN_PIXEL_SIZE;
		col += texture(SCREEN_TEXTURE, SCREEN_UV + offset).rgb;
	}
	col /= SAMPLES;

	COLOR.rgb = mix(col, tint_color.rgb, tint_strength);
}
