# Action Voice JP

**FFXIV Dalamud Plugin | Custom Action Voice Add-on**

日本語 | English

---

## 🇯🇵 日本語

### 📖 概要

**Action Voice JP** は、FINAL FANTASY XIV（FF14）の特定のアクションに、好きなボイスや効果音を追加できるDalamudプラグインです。

既存のVFX・SE MODを変更することなく、指定したアクションに追加音声を再生できます。

キャラクターごとに異なる音声設定を作成でき、ボイスパックのエクスポート・インポートにも対応しています。

### ✨ 主な機能

**🎤 アクション別ボイス設定**
- アクションごとに好きなWAV音声を登録
- アクションID・日本語名の自動取得
- 音量・再生タイミングの個別調整
- ボイスの試聴・停止

**👤 キャラクター別管理**
- キャラクター名とホームワールドで登録
- ゲーム内でターゲットしたキャラクターを登録可能
- キャラクターごとに異なるボイス設定を保存
- 自分のキャラクターのアクションを測定して登録

**📂 カスタムグループ**
- 「召喚士」「戦士」など自由にグループを作成
- グループごとにアクションを管理
- アクション一覧の検索・折りたたみ表示

**📦 ボイスパック共有**
- アクション設定とWAV音声をZIP形式でエクスポート
- ZIPファイルを読み込んでボイス設定を一括登録
- エクスポート時にキャラクター情報を除外
- 好きなキャラクターにボイスパックを適用可能
- ファイルブラウザから保存先・読み込み元を選択
- よく使うフォルダをお気に入り登録

### ⚠️ 現在の制限事項

- 他のプレイヤーの無詠唱アクションの検知には未対応です。
- アクション検知の一部は試験的な実装です。
- 追加音声はゲーム本体の音声データとは別に、PC上で再生されます。
- 本プラグインは開発中のため、不具合が発生する可能性があります。

### 🔧 使用方法

1. DalamudにAction Voice JPを導入します。
2. ゲーム内チャットで `/actionvoice` を入力します。
3. ボイスを設定したいキャラクターを登録します。
4. アクショングループを作成します。
5. アクションIDを登録するか、測定機能を利用します。
6. WAVファイルを選択します。
7. 音量や再生タイミングを調整します。

### 🎵 音声ファイルについて

対応形式：**WAV**

音声ファイルは以下のフォルダに配置してください。

`%APPDATA%\XIVLauncher\pluginConfigs\ActionVoiceJP\Voices`

音声ファイルを追加した後は、プラグイン内の「音声一覧を更新」を押してください。

### 📤 ボイスパックの共有

ボイスパックはZIP形式で保存できます。

ZIPにはアクション設定・グループ情報・音声ファイルが含まれます。

キャラクター名やホームワールドは含まれないため、受け取ったボイスパックを任意のキャラクターに適用できます。

---

## 🇬🇧 English

### 📖 Overview

**Action Voice JP** is a custom Dalamud plugin for FINAL FANTASY XIV that allows players to add custom voice lines and sound effects to specific actions.

It plays additional audio without replacing existing VFX or sound effect mods.

The plugin supports character-specific voice configurations, customizable action groups, and voice pack sharing.

### ✨ Features

**🎤 Custom Action Voices**
- Assign custom WAV audio files to individual actions
- Automatically detect action IDs and retrieve Japanese action names
- Adjust volume and playback delay for each action
- Preview and stop audio playback

**👤 Character Profiles**
- Register characters using their names and home worlds
- Add characters directly from your in-game target
- Manage separate voice configurations for different characters
- Capture action IDs by using actions on your own character

**📂 Custom Action Groups**
- Create custom groups such as Warrior, Summoner, or Favorites
- Organize actions into separate groups
- Search and expand or collapse registered actions

**📦 Voice Pack Sharing**
- Export action configurations and WAV files as ZIP archives
- Import voice packs and apply them to a selected character
- Export voice packs without character identity information
- Transfer multiple action voice settings at once
- Browse folders and select files through an in-game file browser
- Save frequently used folders as favorites

### ⚠️ Current Limitations

- Detection of other players' instant-cast actions is not yet supported.
- Some action detection features are experimental.
- Custom voices are played locally on your PC, separately from the game's audio system.
- This plugin is still under development and may contain bugs.

### 🔧 How to Use

1. Install Action Voice JP through Dalamud.
2. Enter `/actionvoice` in the in-game chat.
3. Register the character you want to configure.
4. Create a custom action group.
5. Enter an action ID or use the action capture feature.
6. Select a WAV audio file.
7. Adjust the volume and playback delay.

### 🎵 Audio Files

Supported format: **WAV**

Place your audio files in:

`%APPDATA%\XIVLauncher\pluginConfigs\ActionVoiceJP\Voices`

After adding files, click the refresh audio list button in the plugin.

### 📤 Sharing Voice Packs

Voice packs can be exported as ZIP archives containing action configurations, custom groups, and audio files.

Character names and home-world information are excluded from newly exported voice packs.

Imported voice packs can be applied to any selected character.

---

## ⚠️ Disclaimer / 免責事項

This is an unofficial third-party plugin and is not affiliated with, endorsed by, or supported by Square Enix.

FINAL FANTASY XIV and related trademarks belong to Square Enix.

Use of third-party plugins may violate the FINAL FANTASY XIV Terms of Service.

本プラグインは非公式のサードパーティ製ツールです。スクウェア・エニックスとは一切関係ありません。

FINAL FANTASY XIVおよび関連する商標は、スクウェア・エニックスに帰属します。

サードパーティ製ツールの使用は、FF14の利用規約に抵触する可能性があります。
