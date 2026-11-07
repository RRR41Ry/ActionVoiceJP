using System;
using System.Collections.Generic;
using Dalamud.Configuration;

namespace ActionVoiceJP;

public sealed class Config : IPluginConfiguration
{
    public int Version { get; set; } = 4;
    public bool Enabled { get; set; } = true;
    public float MasterVolume { get; set; } = 0.8f;
    public List<CharacterProfile> Profiles { get; set; } = new();
    public List<string> FavoritePaths { get; set; } = new();
    public string LastExportDirectory { get; set; } = "";
    public string LastImportDirectory { get; set; } = "";
}

public sealed class CharacterProfile
{
    public string Name { get; set; } = "";
    public string HomeWorld { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public List<VoiceRule> Rules { get; set; } = new();
    public List<string> Groups { get; set; } = new();
    public string DisplayName => string.IsNullOrWhiteSpace(HomeWorld) ? Name : $"{Name}@{HomeWorld}";
}

public sealed class VoiceRule
{
    // Old voice packs without a Job property default to the shared tab.
    public string Job { get; set; } = "共通";
    public bool Enabled { get; set; } = true;
    public string Label { get; set; } = "新しいアクション";
    public uint ActionId { get; set; }
    public string WavePath { get; set; } = "";
    public int DelayMs { get; set; }
    public float Volume { get; set; } = 0.8f;
}
