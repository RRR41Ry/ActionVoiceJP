Action Voice JP v0.4 — 音量修正試作版

更新内容:
- Windows MCI (setaudio) の音量指定が環境によって無視される問題を回避。
- NAudioでWAV再生時に各音声ストリームの音量を適用。
- 全体音量 × 個別音量 の掛け算を試聴とアクション再生に適用。
- 試聴中のスライダー変更は即反映。
- 試聴停止ボタン、アクションID測定、ZIP共有の既存機能を維持。
- Plugin.cs の UseActionMode エイリアスを含む。

注意:
- このZIPはソースコードです。ゲーム内動作・ビルドの実機検証は未実施です。
- 初回ビルド時はNuGetからNAudio 2.4.0の取得が必要です（ネット接続）。
- 変更前に既存プロジェクトのバックアップ推奨。
- 他プレイヤーの無詠唱アクション検知には依然として未対応です。
- 自分のアクションID測定機能は試験的なままです。

更新手順:
1. Dalamudで古いAction Voice JPを無効化。
2. ZIPを解凍。既存のActionVoiceJPプロジェクト内の同名ソースとcsprojを置き換え。
3. Visual StudioでActionVoiceJP.csprojを開き、Ctrl+Shift+Bでビルド。
4. Dalamudで開発プラグインを再読み込み。必要ならDLLパスを更新。
5. 試聴音量とスキル時音量を確認。

WAVの配置場所:
%APPDATA%\XIVLauncher\pluginConfigs\ActionVoiceJP\Voices
