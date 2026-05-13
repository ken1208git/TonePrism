shader_type canvas_item;

uniform vec2 node_size = vec2(400.0, 8.0);
uniform float base_brightness : hint_range(0.0, 1.0) = 0.3;
uniform float shimmer_peak : hint_range(0.5, 1.5) = 1.0;
uniform float shimmer_width : hint_range(0.05, 0.5) = 0.18;
uniform float shimmer_speed : hint_range(0.5, 5.0) = 1.2;

varying vec2 v_local_pos;

void vertex() {
	// 頂点のノード内ローカル位置を 0..1 に正規化して渡す
	v_local_pos = VERTEX / max(node_size, vec2(1.0));
}

void fragment() {
	vec4 tex = texture(TEXTURE, UV);

	// シマー帯の位置（左→右にループ）
	float shimmer_pos = fract(TIME * shimmer_speed / 2.0) * 1.6 - 0.3;

	// ノード内の水平位置に対するフォールオフ
	float dist = abs(v_local_pos.x - shimmer_pos);
	float shimmer = smoothstep(shimmer_width, 0.0, dist);

	float brightness = mix(base_brightness, shimmer_peak, shimmer);

	COLOR = vec4(vec3(brightness), tex.a);
}
