class_name ErrorCode

# エラーコード定義
# 形式: カテゴリ(1桁) + 番号(3桁)
# 1xxx: データベース関連
# 9xxx: システム関連

const OK = 0

# --- 1000番台: データベース関連 ---
const DATABASE_NOT_FOUND = 1001       # データベースファイルが見つからない
const DATABASE_CONNECTION_FAILED = 1002 # データベース接続に失敗
const DATABASE_TABLE_MISSING = 1003     # 必要なテーブルが存在しない
const DATABASE_QUERY_FAILED = 1004      # クエリ実行に失敗
const DATABASE_DATA_INVALID = 1005      # データの整合性に問題がある
const DATABASE_NO_GAMES_REGISTERED = 1006 # ゲームが1つも登録されていない

# --- 2000番台: ゲーム起動・実行関連 ---
const GAME_EXECUTION_FAILED = 2001      # ゲームの起動に失敗（プロセス生成失敗）
const GAME_EXECUTABLE_NOT_FOUND = 2002  # 実行ファイルが見つからない
const GAME_PATH_INVALID = 2003          # パス設定が不正（空など）
const GAME_PERMISSION_DENIED = 2004     # 実行権限がない

# --- 3000番台: アセット・リソース関連 ---
const RESOURCE_IMAGE_NOT_FOUND = 3001   # 画像ファイルが見つからない
const RESOURCE_FONT_NOT_FOUND = 3002    # フォントファイルが見つからない

# --- 9000番台: システム・その他 ---
const SYSTEM_CONFIG_ERROR = 9001        # 設定ファイルのロード失敗
const SYSTEM_FILE_ACCESS_ERROR = 9002   # ファイルアクセスエラー
const SYSTEM_UNKNOWN_ERROR = 9999       # 不明なエラー


