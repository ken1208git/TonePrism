shader_type canvas_item;
uniform float alpha_multiplier : hint_range(0.0, 1.0) = 0.5;
void fragment() {
	float alpha = smoothstep(0.0, 1.0, UV.y);
	COLOR = vec4(0.0, 0.0, 0.0, alpha * alpha_multiplier * COLOR.a);
}
