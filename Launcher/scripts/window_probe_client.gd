class_name WindowProbeClient
extends RefCounted
## WindowProbe Companion (TonePrism_WindowProbe.exe) を呼び出して、指定 PID の
## プロセスツリーが「可視ウィンドウ」「前面ウィンドウ」を持つかを取得する。
##
## 用途:
##   - 起動中 → プレイ中の遷移検知 (#101)
##   - ゲーム実行中のランチャー前面化異常検知 (#216)
##
## 注意:
##   - probe() は OS.execute を使うブロッキング呼び出し。**メインスレッドから直接呼ばず、
##     必ず別スレッドで呼ぶこと** (起動演出中のカクつき回避)。
##   - exe 不在 (Godot エディタ実行 / 配布物に未同梱 等) や実行失敗時は UNAVAILABLE を返す。
##     呼び出し元は probe 無効として従来挙動 (即 PLAYING / 異常検知なし) にフォールバックする。

enum Result {
	UNAVAILABLE,        ## exe 不在 or 実行失敗 (probe 使用不可)
	NOT_FOUND,          ## プロセス不在
	NOT_VISIBLE,        ## 稼働中だが可視ウィンドウなし
	VISIBLE_BACKGROUND, ## 可視ウィンドウあり、前面でない
	VISIBLE_FOREGROUND, ## 可視ウィンドウあり、かつ前面
}

# exe 探索候補（先に見つかったものを使う）:
#   1. 本番/インストール配置 (Release.ps1 が files/Companions/WindowProbe/ に置く)
#   2. dev ビルド出力 (Godot エディタ実行時。msbuild Release/Debug の成果物)
const _EXE_CANDIDATES: Array[String] = [
	"Companions/WindowProbe/TonePrism_WindowProbe.exe",
	"Companions/WindowProbe/bin/Release/TonePrism_WindowProbe.exe",
	"Companions/WindowProbe/bin/Debug/TonePrism_WindowProbe.exe",
]

static func get_exe_path() -> String:
	var base := PathManager.get_base_directory()
	for rel in _EXE_CANDIDATES:
		var p := base.path_join(rel)
		if FileAccess.file_exists(p):
			return p
	# 見つからない場合は本番 path を返す（呼び出し側の is_available / probe が file_exists で false 判定）
	return base.path_join(_EXE_CANDIDATES[0])

static func is_available() -> bool:
	return FileAccess.file_exists(get_exe_path())

## 指定 PID のプロセスツリーのウィンドウ状態を 1 回問い合わせる (ブロッキング)。
static func probe(pid: int) -> Result:
	var exe := get_exe_path()
	if not FileAccess.file_exists(exe):
		return Result.UNAVAILABLE

	var output: Array = []
	var exit_code := OS.execute(exe, [str(pid)], output, false, false)
	if exit_code != 0:
		# exit 2 (引数エラー) / 1 (実行時例外) / -1 (起動失敗) はいずれも probe 不能扱い
		return Result.UNAVAILABLE

	var text := ""
	if not output.is_empty():
		text = String(output[0]).strip_edges()

	# stdout は成功時 1 語のみ。念のため語の包含でも判定する。
	if "visible_foreground" in text:
		return Result.VISIBLE_FOREGROUND
	if "visible_background" in text:
		return Result.VISIBLE_BACKGROUND
	if "not_visible" in text:
		return Result.NOT_VISIBLE
	if "not_found" in text:
		return Result.NOT_FOUND
	return Result.UNAVAILABLE
