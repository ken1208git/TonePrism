shader_type canvas_item;
// 割り当てられた TEXTURE (= companion がキャプチャしたゲーム画面) をぼかして tint を重ねる。
// 中断オーバーレイ #30 のすりガラス背景用。既存 frosted_glass.gdshader は SCREEN_TEXTURE
// (Godot 自身のフレームバッファ) を読むため、別プロセスのゲーム画面はぼかせない。こちらは
// 渡された静止フレームを blur するので透明窓越しのゲームでも使える。

// ぼかし半径を UV 単位 (画像サイズ非依存) で持つ。テクセル単位だと高解像度キャプチャで
// 相対的にぼかしが極小になり frost が効かないため。0.035 ≒ 画像の 3.5% 半径。
uniform float blur_amount : hint_range(0.0, 0.1) = 0.025;
uniform vec4 tint_color : source_color = vec4(0.941, 0.933, 0.914, 1.0); // #f0eee9 ライト
// メニュー部 (レール) は背景 veil より濃い白にして、すりガラスのパネル感・可読性を上げる。
uniform float tint_strength : hint_range(0.0, 1.0) = 0.68;
// UV.x がこの値未満の領域は描画しない (= レール領域だけぼかしを出す)。0 で全面。
uniform float mask_left : hint_range(0.0, 1.0) = 0.0;

const float SAMPLES = 64.0;
const float GOLDEN_ANGLE = 2.39996322972865332;

void fragment() {
	vec3 col = vec3(0.0);
	for (float i = 0.0; i < SAMPLES; i += 1.0) {
		float r = sqrt((i + 0.5) / SAMPLES) * blur_amount;
		float angle = i * GOLDEN_ANGLE;
		vec2 offset = vec2(cos(angle), sin(angle)) * r;
		col += texture(TEXTURE, UV + offset).rgb;
	}
	col /= SAMPLES;
	COLOR.rgb = mix(col, tint_color.rgb, tint_strength);
	COLOR.a = step(mask_left, UV.x); // レール領域 (右側) だけ不透明
}
