using System;
using System.Collections.Generic;
using System.IO;
using NAudio.Wave;

namespace ActionVoiceJP;

/// <summary>独立音声再生。音量は各音声ストリームに適用し、ゲームのSEには触れない。</summary>
internal sealed class Audio : IDisposable
{
    private sealed class Clip : IDisposable
    {
        public readonly AudioFileReader Reader;
        public readonly WaveOutEvent Player;
        public Clip(string path, float volume)
        {
            Reader = new AudioFileReader(path) { Volume = Math.Clamp(volume, 0f, 1f) };
            try
            {
                Player = new WaveOutEvent();
                Player.Init(Reader);
            }
            catch { Reader.Dispose(); throw; }
        }
        public void Dispose()
        {
            Player.Stop();
            Player.Dispose();
            Reader.Dispose();
        }
    }

    private readonly List<Clip> clips = new();

    public string Play(string file, float volume)
    {
        if (!File.Exists(file)) return "音声ファイルが見つかりません。";
        if (!string.Equals(Path.GetExtension(file), ".wav", StringComparison.OrdinalIgnoreCase))
            return "WAVファイルを指定してください。";
        try
        {
            Cleanup();
            var clip = new Clip(file, volume);
            clips.Add(clip);
            clip.Player.Play();
            return "再生しました。";
        }
        catch (Exception ex) { return "再生に失敗しました: " + ex.Message; }
    }

    // 試聴中にスライダーを動かしても音量をリアルタイム変更可能。
    public void SetVolume(float volume)
    {
        foreach (var clip in clips)
            if (clip.Player.PlaybackState != PlaybackState.Stopped)
                clip.Reader.Volume = Math.Clamp(volume, 0f, 1f);
    }

    private void Cleanup()
    {
        for (var i = clips.Count - 1; i >= 0; i--)
        {
            if (clips[i].Player.PlaybackState != PlaybackState.Stopped) continue;
            clips[i].Dispose();
            clips.RemoveAt(i);
        }
    }

    public void Stop()
    {
        foreach (var clip in clips) clip.Dispose();
        clips.Clear();
    }

    public void Dispose() => Stop();
}
