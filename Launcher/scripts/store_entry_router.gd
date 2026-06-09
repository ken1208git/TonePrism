## ストア入口のシーンルーティング (#315)。
##
## screensaver / intro_guide からストアに入るとき、表示できる店セクションがあれば store_browse、
## **無ければ store_browse を挟まず直接カルーセル (game_selection)** へ向かうシーンパスを返す。
## カルーセル直行のときは AppState に全ゲームを積んで準備する。
##
## なぜ入口で分岐するか: 空ストア (0 セクション) のとき store_browse に入ってから _fallback_to_carousel で
## カルーセルへ移ると、screensaver→store_browse の遷移アニメ中に再遷移が走り「一瞬空の store_browse が
## ちらつく」(ワンクッション挟まる) ため。入口で分岐すれば store_browse を一切経由せず直カルーセルになる
## (#253 の screensaver 事前分岐と同方針)。store_browse 側の _fallback_to_carousel + TransitionManager の
## 遷移キューは、ここをすり抜ける稀ケース (可視セクションはあるが中身が空/0タイル) の defense として残す。
class_name StoreEntryRouter
extends RefCounted

const STORE_BROWSE := "res://scenes/store_browse.tscn"
const GAME_SELECTION := "res://scenes/game_selection.tscn"
const SCREENSAVER := "res://scenes/screensaver.tscn"

## 入口のターゲットシーンを解決して返す。カルーセル直行のときは AppState を準備する。
static func resolve_and_prepare() -> String:
	var db := DatabaseManager.new()
	if not db.open():
		# DB が開けないときは store_browse に任せる (そちらの DATABASE_NOT_FOUND エラー表示に委ねる)。
		return STORE_BROWSE

	var game_repo := GameRepository.new(db)
	var section_repo := StoreSectionRepository.new(db, game_repo)

	# 軽量チェック: 可視セクションが1つでもあれば store_browse へ (中身が空/0タイルなら store_browse の
	# fallback が拾う)。1つも無ければ store_browse を挟まずカルーセルへ。
	if section_repo.has_visible_sections():
		db.close()
		return STORE_BROWSE

	var all_games := game_repo.get_all_games()
	db.close()
	if all_games.is_empty():
		# セクションもゲームも無い → store_browse の no-games エラー表示に委ねる (挙動据え置き)。
		return STORE_BROWSE

	# セクション0・ゲームあり → カルーセル直行。AppState を「全ゲーム」で準備する
	# (store_browse._fallback_to_carousel と同じ準備)。
	AppState.filtered_games = all_games
	AppState.initial_game_id = all_games[0].game_id
	AppState.return_scene = SCREENSAVER
	AppState.section_title = ""
	# (#315) 戻り先のストアが無い最上位カルーセル。game_selection は戻るボタンを出さず ESC を退出ダイアログにする。
	AppState.carousel_top_level = true
	return GAME_SELECTION
