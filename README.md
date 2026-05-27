# TonePrism

TonePrism は、大阪府立刀根山高校パソコン部の文化祭展示向けに作っている統合ランチャーシステムです。
来場者がスタッフの補助なしでもゲームを選んで起動できること、展示運営を少ない人数でも回しやすくすることを目的としています。

## はじめにお読みください（入口の案内）

このリポジトリには **2 種類の読み手** に向けた情報が混在しています。目的に応じて入口を選んでください。

- **運営スタッフ・部員・顧問の方（インストール / 運用）**
  - 運用マニュアル（日々の起動・Manager の使い方・当日運用・トラブル対応）: **[部員向けマニュアル](https://ken1208git.github.io/TonePrism/)**
  - インストール: [GitHub Releases](https://github.com/ken1208git/TonePrism/releases) から最新の zip をダウンロードし、解凍して `Install.bat` を実行（詳細手順は zip 同梱の `INSTALL_README.txt`）。
  - ※ このリポジトリを `git clone` する必要はありません。
- **開発者の方（ソースの編集・ビルド）**
  - 下記「[開発環境のセットアップ](#開発環境のセットアップ)」へ。

## このリポジトリにあるもの

- **Launcher**: 来場者向けのゲーム選択・起動アプリです。Godot Engine 4.6 で実装しています。
- **Manager**: スタッフ向けの管理ツールです。ゲーム情報、開発者情報、バージョン情報、ストア表示用データなどを管理します。C# WinForms / .NET Framework 4.8 で実装しています。
- **Monitor**: 先生PC向けの監視ソフトです。仕様は [SPECIFICATION.md](SPECIFICATION.md) にありますが、現時点ではこのリポジトリに実装は含まれていません（仕様策定・Issue 分解が進行中）。
- **Companions**: 主要アプリを補助する独立 exe 群です（SPEC §2.4）。Manager の自己置換に使う **Updater** と、Launcher 補助の常駐エージェント **LauncherAgent**（ゲームの可視/前面ウィンドウ検知、HOME/Guide による中断メニュー検知、前面化制御を 1 プロセスに統合）を含みます。

## 現在の状態

- Launcher はゲーム選択画面、Store Browse 画面、スクリーンセーバー、ダイアログ表示、エラー表示、画面遷移などの実装が進んでいます。
- Manager は Windows Forms アプリとして実装済みで、ゲーム / 開発者 / ストアセクションの編集、設定（ログ保存先・バックアップ等）、ログ閲覧、Manager UI からの自動アップデート適用などを備えます。
- Companions（Updater）は Manager の自動アップデート適用フローで使用されます。LauncherAgent は Launcher のゲーム起動→プレイ中遷移の正確化・ランチャー前面化異常検知・中断メニュー（HOME/Guide）検知に使用されます。
- Monitor は仕様策定と Issue 分解が進行中です。
- リリースは Bundle 単位で配布しています（最新は [GitHub Releases](https://github.com/ken1208git/TonePrism/releases) を参照）。

詳細な仕様は [SPECIFICATION.md](SPECIFICATION.md)、変更履歴は [CHANGELOG.md](CHANGELOG.md) を参照してください。

## ディレクトリ構成

```text
TonePrism/
├── Launcher/               # 主要: Godot 製ランチャー本体
├── Manager/                # 主要: WinForms 製管理ツール
├── Monitor/                # 主要: 監視ソフト (将来)
├── Companions/             # 補助 exe 集約 (SPEC §2.4)
│   ├── Updater/            # Manager 置換用 (SPEC §3.7.4、#108 Phase 3)
│   └── LauncherAgent/      # Launcher 補助の常駐エージェント: probe(#101/#216) + sensor(HOME/Guide #30) + focus
├── games/                  # 展示対象ゲームやサンプルデータ
├── docs/                   # 部員向けマニュアル (MkDocs ソース、GitHub Pages で公開)
├── templates/              # 配布物テンプレート (Install.bat / INSTALL_README.txt 等)
├── .github/workflows/      # CI (encoding チェック / docs サイト公開)
├── mkdocs.yml              # docs サイト設定
├── requirements-docs.txt   # docs サイトのビルド依存
├── toneprism.db            # SQLite データベース
├── Release.ps1             # リリースビルド・配布スクリプト
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

- [部員向けマニュアル](https://ken1208git.github.io/TonePrism/): 運営スタッフ・部員向けの運用マニュアル（`docs/` を MkDocs で公開）
- [SPECIFICATION.md](SPECIFICATION.md): 全体仕様、画面仕様、マイルストーン、バージョン方針
- [CHANGELOG.md](CHANGELOG.md): 実装変更の履歴

## 補足

- `games/` には展示対象ゲームや確認用データが入っています。
- `toneprism.db` は SQLite データベースです。
- README は「このリポジトリの入口」として簡潔に保ち、詳細仕様は仕様書側に寄せています。

## ライセンス

MIT License

Copyright (c) 2025-2026 TonePrism Project — Lead maintainer: Kenshiro Kuroga (Osaka Prefectural Toneyama Upper Secondary School PC Club)
