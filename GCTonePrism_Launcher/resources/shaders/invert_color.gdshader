shader_type canvas_item;

void fragment() {
	vec4 tex = texture(TEXTURE, UV);
	// RGBを反転、アルファは親のmodulate(COLOR.a)を乗算してフェードに追従
	COLOR = vec4(1.0 - tex.r, 1.0 - tex.g, 1.0 - tex.b, tex.a * COLOR.a);
}
