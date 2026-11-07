using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ActionVoiceJP;

internal sealed class PackDocument
{
    public int Version { get; set; } = 2;
    // v2 only: No name, home world, or character identifier is serialized.
    public List<string> Groups { get; set; } = new();
    public List<VoiceRule> Rules { get; set; } = new();
    // v1 compatibility for older ZIP packs; never copied into the target identity.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterProfile? Profile { get; set; }
}

internal static class VoicePack
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public static string Export(CharacterProfile profile, string destination, string voicesFolder)
    {
        if (profile.Rules == null) throw new InvalidDataException("アクション設定が見つかりません。");
        if (profile.Rules.Count > 200) throw new InvalidDataException("1人あたり200件までです。");

        // Resolve and check ALL voice files before creating any ZIP.
        // Old versions could leave an incomplete ZIP without pack.json if a WAV was missing.
        var prepared = new List<(VoiceRule Rule, string FullPath)>();
        foreach (var rule in profile.Rules)
        {
            if (string.IsNullOrWhiteSpace(rule.WavePath))
                throw new InvalidDataException($"音声未設定のアクションがあります: {rule.Label} (ID:{rule.ActionId})");
            var path = Path.IsPathRooted(rule.WavePath) ? rule.WavePath : Path.Combine(voicesFolder, rule.WavePath);
            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException($"音声ファイルが見つかりません: {rule.Label} (ID:{rule.ActionId})", fullPath);
            if (!string.Equals(Path.GetExtension(fullPath), ".wav", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("WAVファイルのみ共有できます: " + fullPath);
            prepared.Add((rule, fullPath));
        }

        var outputPath = Path.GetFullPath(destination);
        if (File.Exists(outputPath)) throw new IOException("同名のZIPが既に存在します。");
        var tempPath = Path.Combine(Path.GetDirectoryName(outputPath)!, ".actionvoice-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var archive = ZipFile.Open(tempPath, ZipArchiveMode.Create))
            {
                var groups = (profile.Groups ?? new()).ToList();
                var rules = new List<VoiceRule>();
                var included = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (rule, fullPath) in prepared)
                {
                    if (!included.TryGetValue(fullPath, out var entryName))
                    {
                        entryName = $"voices/{included.Count + 1:D3}.wav";
                        archive.CreateEntryFromFile(fullPath, entryName, CompressionLevel.Optimal);
                        included[fullPath] = entryName;
                    }
                    rules.Add(new VoiceRule
                    {
                        Job = rule.Job, Enabled = rule.Enabled, Label = rule.Label, ActionId = rule.ActionId,
                        WavePath = entryName, DelayMs = rule.DelayMs, Volume = rule.Volume
                    });
                }
                var manifest = archive.CreateEntry("pack.json");
                using (var stream = manifest.Open())
                    JsonSerializer.Serialize(stream, new PackDocument { Version = 2, Groups = groups, Rules = rules }, Options);
            }

            // Verify the completed temp archive before it becomes visible as the exported ZIP.
            using (var verify = ZipFile.OpenRead(tempPath))
            {
                var manifest = verify.GetEntry("pack.json") ?? throw new InvalidDataException("ZIP内のpack.json作成に失敗しました。");
                using var manifestStream = manifest.Open();
                var doc = JsonSerializer.Deserialize<PackDocument>(manifestStream, Options);
                if (doc?.Version != 2 || doc.Profile != null || doc.Rules == null || doc.Rules.Count != prepared.Count)
                    throw new InvalidDataException("ZIPの設定検証に失敗しました。");
                foreach (var rule in doc.Rules)
                    if (verify.GetEntry(rule.WavePath) == null)
                        throw new InvalidDataException("ZIP内のWAVが不足しています: " + rule.WavePath);
            }
            // On failure, only the temporary file is removed; do not leave a misleading partial ZIP.
            File.Move(tempPath, outputPath);
            return $"共有ZIPを作成しました: {outputPath}";
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    // Import voice settings into an EXISTING character. Never import a ZIP character identity.
    // Existing v1 ZIPs are accepted, but their personal fields are ignored.
    // replaceMatching=true replaces only rules with the same (action ID, group) in the target.
    public static int ImportInto(string zipPath, string voicesFolder, CharacterProfile target, bool replaceMatching)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        using var archive = ZipFile.OpenRead(zipPath);
        if (archive.Entries.Count > 205) throw new InvalidDataException("ZIP内のファイル数が多すぎます。");
        var manifest = archive.GetEntry("pack.json") ?? throw new InvalidDataException("pack.jsonがありません。");
        if (manifest.Length > 512_000) throw new InvalidDataException("設定が大きすぎます。");
        PackDocument pack;
        using (var stream = manifest.Open())
            pack = JsonSerializer.Deserialize<PackDocument>(stream, Options) ?? throw new InvalidDataException("設定を読めません。");
        if (pack.Version != 1 && pack.Version != 2) throw new InvalidDataException("未対応のZIP形式です。");
        // v1 contains identity; deliberately ignore Name, HomeWorld, Enabled.
        var rules = pack.Version == 1 ? pack.Profile?.Rules : pack.Rules;
        var groups = pack.Version == 1 ? pack.Profile?.Groups : pack.Groups;
        if (rules == null || rules.Count > 200) throw new InvalidDataException("アクション設定が不正です。");
        groups ??= new();
        if (groups.Count > 100 || groups.Any(g => string.IsNullOrWhiteSpace(g) || g.Length > 40))
            throw new InvalidDataException("グループ設定が不正です。");
        if (target.Rules.Count + rules.Count > 200 && !replaceMatching)
            throw new InvalidDataException("登録可能なアクション数（200件）を超えます。");
        long totalBytes = 0;
        foreach (var rule in rules)
        {
            if (rule == null || rule.WavePath == null || !rule.WavePath.StartsWith("voices/", StringComparison.Ordinal) ||
                !rule.WavePath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
                rule.WavePath.Contains("..", StringComparison.Ordinal) || rule.WavePath.Contains('\\') ||
                rule.WavePath.Length > 160 || (rule.Job?.Length ?? 0) > 40 || (rule.Label?.Length ?? 0) > 128)
                throw new InvalidDataException("ボイス設定または音声ファイルのパスが不正です。");
            var entry = archive.GetEntry(rule.WavePath) ?? throw new InvalidDataException("ZIPに音声がありません。");
            if (entry.Length > 30_000_000) throw new InvalidDataException("音声が大きすぎます（30MB上限）。");
            totalBytes += entry.Length;
            if (totalBytes > 200_000_000) throw new InvalidDataException("音声の合計サイズが大きすぎます。");
        }
        var folderName = "Imported_" + Guid.NewGuid().ToString("N")[..12];
        var destination = Path.Combine(voicesFolder, folderName);
        var imported = new List<VoiceRule>();
        try
        {
            Directory.CreateDirectory(destination);
            var extracted = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var rule in rules)
            {
                if (!extracted.TryGetValue(rule.WavePath, out var safeName))
                {
                    safeName = $"{extracted.Count + 1:D3}.wav";
                    var entry = archive.GetEntry(rule.WavePath)!;
                    using var from = entry.Open();
                    using var to = File.Create(Path.Combine(destination, safeName));
                    from.CopyTo(to);
                    if (to.Length > 30_000_000) throw new InvalidDataException("音声が大きすぎます。");
                    extracted[rule.WavePath] = safeName;
                }
                imported.Add(new VoiceRule
                {
                    Job = string.IsNullOrWhiteSpace(rule.Job) ? "共通" : rule.Job,
                    Enabled = rule.Enabled, Label = rule.Label ?? "アクション",
                    ActionId = rule.ActionId, WavePath = Path.Combine(folderName, safeName),
                    Volume = Math.Clamp(rule.Volume, 0, 1), DelayMs = Math.Clamp(rule.DelayMs, 0, 3000)
                });
            }
            // Stage updates and only commit after extraction and validation have succeeded.
            var updated = new List<VoiceRule>(target.Rules);
            foreach (var rule in imported)
            {
                if (replaceMatching)
                    updated.RemoveAll(old => old.ActionId == rule.ActionId &&
                        string.Equals(string.IsNullOrWhiteSpace(old.Job) ? "共通" : old.Job,
                                      rule.Job, StringComparison.OrdinalIgnoreCase));
                updated.Add(rule);
            }
            if (updated.Count > 200) throw new InvalidDataException("登録可能なアクション数（200件）を超えます。");
            var updatedGroups = new List<string>(target.Groups ?? new());
            foreach (var group in groups.Concat(imported.Select(r => r.Job)))
                if (group != "共通" && !updatedGroups.Any(g => string.Equals(g, group, StringComparison.OrdinalIgnoreCase)))
                    updatedGroups.Add(group);
            target.Rules = updated;
            target.Groups = updatedGroups;
            return imported.Count;
        }
        catch
        {
            if (Directory.Exists(destination))
                try { Directory.Delete(destination, true); } catch { /* preserve original exception */ }
            throw;
        }
    }
}
