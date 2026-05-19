# TonePrism

TonePrism は、大阪府立刀根山高校パソコン部の文化祭展示向けに作っている統合ランチャーシステムです。
来場者がスタッフの補助なしでもゲームを選んで起動できること、展示運営を少ない人数でも回しやすくすることを目的としています。

## このリポジトリにあるもの

- **Launcher**: 来場者向けのゲーム選択・起動アプリです。Godot Engine 4.6 で実装しています。
- **Manager**: スタッフ向けの管理ツールです。ゲーム情報、開発者情報、バージョン情報、ストア表示用データなどを管理します。C# WinForms / .NET Framework 4.8 で実装しています。
- **Monitor**: 先生PC向けの監視ソフトです。仕様は [SPECIFICATION.md](SPECIFICATION.md) にありますが、2026-04-01 時点ではこのリポジトリに実装は含まれていません。

## 現在の状態

- Launcher はゲーム選択画面、Store Browse 画面、スクリーンセーバー、ダイアログ表示、エラー表示、画面遷移などの実装が進んでいます。
- Manager は Windows Forms アプリとして実装済みで、各種データ編集フォームやストアセクション管理画面を含みます。
- Monitor は仕様策定と Issue 分解が進行中です。

詳細な仕様は [SPECIFICATION.md](SPECIFICATION.md)、変更履歴は [CHANGELOG.md](CHANGELOG.md) を参照してください。

## ディレクトリ構成

```text
TonePrism/
├── Launcher/               # 主要: Godot 製ランチャー本体
├── Manager/                # 主要: WinForms 製管理ツール
├── Monitor/                # 主要: 監視ソフト (将来)
├── Companions/             # 補助 exe 集約 (SPEC §2.4)
│   └── Updater/            # Manager 置換用 (SPEC §3.7.4、#108 Phase 3)
├── games/                  # 展示対象ゲームやサンプルデータ
├── docs/                   # 補助ドキュメント
├── toneprism.db                # SQLite データベース
├── SPECIFICATION.md        # 仕様書
└── CHANGELOG.md            # 変更履歴
```

トップレベル命名規約: dir 名は短縮 (`Launcher/`, `Manager/`, `Companions/<Name>/`)、csproj / アセンブリ / exe 名は `TonePrism_<Name>` prefix 維持 (例: `TonePrism_Launcher.exe`、process 検知 uniqueness のため)。詳細は [AGENTS.md "Naming Conventions"](AGENTS.md) を参照。

## 動作環境

### 共通

- Windows 10 / 11

### Launcher

- Godot Engine 4.6
- `godot-sqlite` プラグイン

### Manager

- Visual Studio 2026
- .NET Framework 4.8 開発環境

## 開発環境のセットアップ

このセクションは、開発者がこのリポジトリをローカルで開いて編集・実行するための手順です。
展示用 PC に配布する実行環境のセットアップ手順ではありません。

### 1. リポジトリを取得

```bash
git clone https://github.com/ken1208git/TonePrism.git
```

### 2. Launcher を開く

1. Godot Engine 4.6 をインストールします。
2. Godot エディタで [Launcher/project.godot](Launcher/project.godot) を開きます。
3. 必要に応じてそのまま実行します。

補足:
`godot-sqlite` プラグインはリポジトリ内に含まれています。

### 3. Manager を開く

1. Visual Studio 2026 で [Manager/TonePrism_Manager.csproj](Manager/TonePrism_Manager.csproj) を開きます。
2. NuGet パッケージを復元します。
3. `Debug` もしくは `Release` でビルドして起動します。

## 関連ドキュメント

- [SPECIFICATION.md](SPECIFICATION.md): 全体仕様、画面仕様、マイルストーン、バージョン方針
- [CHANGELOG.md](CHANGELOG.md): 実装変更の履歴
- [ERROR_CODES_MANUAL.txt](ERROR_CODES_MANUAL.txt): エラーコード運用メモ

## 補足

- `games/` には展示対象ゲームや確認用データが入っています。
- `toneprism.db` は SQLite データベースです。
- README は「このリポジトリの入口」として簡潔に保ち、詳細仕様は仕様書側に寄せています。

## ライセンス

MIT License

Copyright (c) 2025-2026 Kenshiro Kuroga - TonePrism Project (Osaka Prefectural Toneyama Upper Secondary School PC Club)
