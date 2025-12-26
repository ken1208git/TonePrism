# ゲームセンターTONE 統合ランチャーシステム「Prism」

大阪府立刀根山高校パソコン部が文化祭で展示する部員制作ゲームを、スタッフのサポートなしでも誰でも簡単に選択・起動できるようにする統合ランチャーシステムです。

## 概要

- **ランチャーアプリケーション**: 来客向けのゲーム選択・起動UI（Godot）
- **管理ソフトウェア**: スタッフ向けのゲーム管理ツール（C# WinForms）

## 主な機能

### ランチャー機能

- ゲーム選択・起動機能
- ゲーム情報表示機能
- ゲームフィルター機能
- オーバーレイメニュー機能
- コントローラー・キーボード両対応
- その他多数の機能（詳細は[仕様書](SPECIFICATION.md)を参照）

### 管理機能

- ゲーム追加・編集・削除
- データ閲覧・エクスポート
- 設定管理（カラーテーマ設定含む）

## 技術スタック

- **ランチャー**: Godot Engine 4.5
- **管理ソフト**: C# (Windows Forms)
- **データ形式**: SQLite

## 開発環境

### ランチャー開発

- **Godot Engine**: 4.5以降
- **SQLiteプラグイン**: [godot-sqlite](https://github.com/2shady4u/godot-sqlite)
- **OS**: Windows 10/11

### 管理ソフト開発

- **IDE**: Visual Studio 2022（推奨）
- **.NET Framework**: （実装時に確定）
- **OS**: Windows 10/11

## セットアップ

### Launcherのセットアップ

1. Godot Engine 4.5以降をインストール
2. このリポジトリをクローン

   ```bash
   git clone https://github.com/your-username/GCTonePrism.git
   ```

3. Godotエディタで `GCTonePrism_Launcher/project.godot` を開く
4. SQLiteプラグインは既に含まれています

### Managerのセットアップ

（実装後に追加予定）

## ドキュメント

- [仕様書](SPECIFICATION.md) - 詳細な機能仕様
- [変更履歴](CHANGELOG.md) - バージョン履歴

## ライセンス

MIT License

Copyright (c) 2025 Kenshiro Kuroga (Osaka Prefectural Toneyama High School PC Club)
