using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Command;
using Dalamud.Hooking;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using ActionDelegate = FFXIVClientStructs.FFXIV.Client.Game.ActionManager.Delegates.UseAction;
using UseActionMode = FFXIVClientStructs.FFXIV.Client.Game.ActionManager.UseActionMode;

namespace ActionVoiceJP;

public sealed unsafe class Plugin : IDalamudPlugin
{
    private readonly IDalamudPluginInterface pi;
    private readonly ICommandManager commands;
    private readonly IFramework framework;
    private readonly IPluginLog log;
    private readonly IClientState client;
    private readonly IObjectTable objects;
    private readonly ITargetManager targets;
    private readonly IDataManager dataManager;
    private readonly Hook<ActionDelegate> useActionHook;
    private readonly ConcurrentQueue<uint> observedActions = new();
    private readonly ConcurrentQueue<uint> capturedActions = new();
    private readonly List<uint> recentActions = new();
    private volatile bool isRecording;
    internal bool IsRecording => isRecording;
    internal IReadOnlyList<uint> RecentActions => recentActions;
    internal void StartRecording()
    {
        recentActions.Clear();
        while (capturedActions.TryDequeue(out _)) { }
        isRecording = true;
        Message = "測定中：自分でアクションを使用してください。";
    }
    internal void StopRecording()
    {
        isRecording = false;
        Message = "測定を停止しました。履歴からアクションを登録できます。";
    }
    internal void ClearRecording()
    {
        recentActions.Clear();
        while (capturedActions.TryDequeue(out _)) { }
    }
    private readonly List<PendingVoice> pending = new();
    private readonly Dictionary<ulong, uint> lastCast = new();
    private readonly Audio audio = new();
    private readonly Audio previewAudio = new();
    private readonly WindowSystem windows = new("ActionVoiceJP");
    private readonly MainWindow mainWindow;
    private readonly string voiceFolder;
    private bool disposed;

    internal Config Config { get; }
    internal string Message { get; private set; } = "準備完了。声を登録してください。";
    internal string VoiceFolder => voiceFolder;
    internal string PackFolder { get; }

    public Plugin(IDalamudPluginInterface pluginInterface, ICommandManager commandManager,
        IFramework framework, IGameInteropProvider interop, IPluginLog pluginLog,
        IClientState clientState, IObjectTable objectTable, ITargetManager targetManager,
        IDataManager dataManager)
    {
        pi = pluginInterface;
        commands = commandManager;
        this.framework = framework;
        log = pluginLog;
        client = clientState;
        objects = objectTable;
        targets = targetManager;
        this.dataManager = dataManager;
        Config = pi.GetPluginConfig() as Config ?? new Config();
        Config.Profiles ??= new();
        foreach (var profile in Config.Profiles)
            profile.Rules ??= new();
        voiceFolder = Path.Combine(pi.ConfigDirectory.FullName, "Voices");
        PackFolder = Path.Combine(pi.ConfigDirectory.FullName, "VoicePacks");
        Directory.CreateDirectory(voiceFolder);
        Directory.CreateDirectory(PackFolder);
        // v0.9: existing rule.Job values become editable group names.
        // No automatic job assignment: users now decide grouping themselves.
        foreach (var profile in Config.Profiles)
        {
            profile.Groups ??= new();
            foreach (var rule in profile.Rules)
            {
                var group = string.IsNullOrWhiteSpace(rule.Job) ? "共通" : rule.Job;
                if (group != "共通" && !profile.Groups.Any(g => string.Equals(g, group, StringComparison.OrdinalIgnoreCase)))
                    profile.Groups.Add(group);
            }
        }
        if (Config.Version < 6) { Config.Version = 6; Save(); }
        mainWindow = new MainWindow(this);
        windows.AddWindow(mainWindow);
        pi.UiBuilder.Draw += Draw;
        pi.UiBuilder.OpenMainUi += Open;
        pi.UiBuilder.OpenConfigUi += Open;
        commands.AddHandler("/actionvoice", new CommandInfo((_, _) => Open())
        {
            HelpMessage = "Action Voice JP の設定を開く"
        });
        // Only local player's action request is observable by this hook.
        useActionHook = interop.HookFromAddress<ActionDelegate>(
            ActionManager.MemberFunctionPointers.UseAction, OnUseAction);
        useActionHook.Enable();
        framework.Update += OnUpdate;
    }

    private bool OnUseAction(ActionManager* self, ActionType actionType, uint actionId, ulong targetId,
        uint extraParam, UseActionMode mode, uint comboRouteId, bool* areaTargeted)
    {
        var result = useActionHook.Original(self, actionType, actionId, targetId, extraParam, mode,
            comboRouteId, areaTargeted);
        try
        {
            if (!disposed && result && actionType == ActionType.Action &&
                mode != UseActionMode.Queue && client.IsLoggedIn)
            {
                if (isRecording) capturedActions.Enqueue(actionId);
                if (Config.Enabled) observedActions.Enqueue(actionId);
            }
        }
        catch (Exception ex) { log.Error(ex, "Action observer failed"); }
        return result;
    }

    private readonly record struct PendingVoice(DateTime When, string Path, float Volume);

    private static string WorldName(IPlayerCharacter player)
    {
        try { return player.HomeWorld.Value.Name.ToString(); }
        catch { return ""; }
    }

    private static bool Matches(CharacterProfile profile, IPlayerCharacter player) =>
        profile.Enabled && !string.IsNullOrWhiteSpace(profile.Name) &&
        !string.IsNullOrWhiteSpace(profile.HomeWorld) &&
        string.Equals(profile.Name.Trim(), player.Name.TextValue, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(profile.HomeWorld.Trim(), WorldName(player), StringComparison.OrdinalIgnoreCase);

    private void QueueFor(IPlayerCharacter character, uint action, DateTime now)
    {
        foreach (var profile in Config.Profiles)
        {
            if (!Matches(profile, character)) continue;
            foreach (var rule in profile.Rules)
            {
                if (!rule.Enabled || rule.ActionId != action || string.IsNullOrWhiteSpace(rule.WavePath)) continue;
                pending.Add(new PendingVoice(now.AddMilliseconds(Math.Clamp(rule.DelayMs, 0, 3000)),
                    ResolvePath(rule.WavePath), Math.Clamp(rule.Volume * Config.MasterVolume, 0, 1)));
            }
        }
    }

    private void OnUpdate(IFramework frameworkContext)
    {
        if (!client.IsLoggedIn && isRecording) StopRecording();
        while (capturedActions.TryDequeue(out var captured))
        {
            recentActions.Remove(captured);
            recentActions.Insert(0, captured);
            if (recentActions.Count > 12) recentActions.RemoveAt(recentActions.Count - 1);
        }
        if (!Config.Enabled || !client.IsLoggedIn)
        {
            pending.Clear();
            lastCast.Clear();
            while (observedActions.TryDequeue(out var ignoredAction)) { }
            return;
        }
        var now = DateTime.UtcNow;
        var local = objects.LocalPlayer;
        while (observedActions.TryDequeue(out var action))
            if (local != null) QueueFor(local, action, now);

        // Experimental remote observer: only CASTING actions (not instant skills such as Drill).
        // Track transitions to avoid repeating on every frame.
        var visible = new HashSet<ulong>();
        foreach (var obj in objects.PlayerObjects)
        {
            if (obj is not IPlayerCharacter character || (local != null && obj.GameObjectId == local.GameObjectId)) continue;
            if (!Config.Profiles.Exists(p => Matches(p, character))) continue;
            var objectId = character.GameObjectId;
            visible.Add(objectId);
            var actionId = character.IsCasting ? character.CastActionId : 0;
            lastCast.TryGetValue(objectId, out var previous);
            if (actionId != 0 && actionId != previous) QueueFor(character, actionId, now);
            lastCast[objectId] = actionId;
        }
        foreach (var key in new List<ulong>(lastCast.Keys)) if (!visible.Contains(key)) lastCast.Remove(key);

        for (var i = pending.Count - 1; i >= 0; i--)
        {
            if (pending[i].When > now) continue;
            var job = pending[i];
            pending.RemoveAt(i);
            Message = audio.Play(job.Path, job.Volume);
        }
    }

    // Resolve action names using the game's Japanese Action sheet. Unknown IDs retain a readable fallback.
    internal string ActionName(uint actionId)
    {
        if (actionId == 0) return "未設定";
        try
        {
            var sheet = dataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>(Dalamud.Game.ClientLanguage.Japanese);
            if (sheet.TryGetRow(actionId, out var row))
            {
                var name = row.Name.ToString();
                if (!string.IsNullOrWhiteSpace(name)) return name;
            }
        }
        catch (Exception ex) { log.Debug(ex, "Unable to resolve action name {id}", actionId); }
        return $"不明なアクション ({actionId})";
    }

    // Action sheet normally has a ClassJob row reference. Reflection avoids relying
    // on differences in RowRef/row field access between generated Lumina versions.
    // Unknown/shared actions remain in the user's chosen tab instead of guessing.
    internal string? ActionJob(uint actionId)
    {
        if (actionId == 0) return null;
        // Summon Carbuncle is classified as Summoner even when the sheet lacks
        // a specific job link (verified against the registered action in this project).
        if (actionId == 25798) return "召喚士";
        try
        {
            var sheet = dataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>(Dalamud.Game.ClientLanguage.Japanese);
            if (!sheet.TryGetRow(actionId, out var row)) return null;
            object boxed = row;
            var jobRef = boxed.GetType().GetProperty("ClassJob")?.GetValue(boxed);
            if (jobRef == null) return null;
            var refType = jobRef.GetType();
            var idProperty = refType.GetProperty("RowId") ?? refType.GetProperty("Row");
            if (idProperty == null) return null;
            var value = idProperty.GetValue(jobRef);
            if (value == null) return null;
            var id = Convert.ToUInt32(value);
            return id switch
            {
                1 or 19 => "ナイト",
                2 or 20 => "モンク",
                3 or 21 => "戦士",
                4 or 22 => "竜騎士",
                5 or 23 => "吟遊詩人",
                6 or 24 => "白魔道士",
                7 or 25 => "黒魔道士",
                26 or 27 => "召喚士",
                28 => "学者",
                29 or 30 => "忍者",
                31 => "機工士",
                32 => "暗黒騎士",
                33 => "占星術師",
                34 => "侍",
                35 => "赤魔道士",
                36 => "青魔道士",
                37 => "ガンブレイカー",
                38 => "踊り子",
                39 => "リーパー",
                40 => "賢者",
                41 => "ヴァイパー",
                42 => "ピクトマンサー",
                _ => null
            };
        }
        catch (Exception ex)
        {
            log.Debug(ex, "Action job lookup failed for {id}", actionId);
            return null;
        }
    }

    internal (string Name, string World)? TargetIdentity()
    {
        if (targets.Target is not IPlayerCharacter player) return null;
        var world = WorldName(player);
        return string.IsNullOrWhiteSpace(world) ? null : (player.Name.TextValue, world);
    }

    internal string ResolvePath(string path) => Path.IsPathRooted(path) ? path : Path.Combine(voiceFolder, path);
    internal void Preview(VoiceRule rule) => Message = previewAudio.Play(ResolvePath(rule.WavePath),
        Math.Clamp(rule.Volume * Config.MasterVolume, 0, 1));
    internal void UpdatePreviewVolume(VoiceRule rule) => previewAudio.SetVolume(
        Math.Clamp(rule.Volume * Config.MasterVolume, 0, 1));
    internal void StopPreview()
    {
        previewAudio.Stop();
        Message = "試聴を停止しました。";
    }

    internal void Export(CharacterProfile profile, string? destinationDirectory = null, string? fileName = null)
    {
        try
        {
            var safe = string.Concat(profile.Name.Split(Path.GetInvalidFileNameChars()));
            if (string.IsNullOrWhiteSpace(safe)) safe = "VoicePack";
            var folder = string.IsNullOrWhiteSpace(destinationDirectory) ? PackFolder : Path.GetFullPath(destinationDirectory);
            if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("保存先フォルダが見つかりません: " + folder);
            var outputName = string.IsNullOrWhiteSpace(fileName)
                ? $"{safe}_{DateTime.Now:yyyyMMdd_HHmmss_fff}.zip"
                : Path.GetFileName(fileName.Trim());
            if (!outputName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) outputName += ".zip";
            if (outputName == ".zip" || outputName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new InvalidDataException("ファイル名が正しくありません。");
            var path = Path.Combine(folder, outputName);
            if (File.Exists(path)) throw new IOException("同名のZIPが既に存在します。");
            Message = VoicePack.Export(profile, path, voiceFolder);
        }
        catch (Exception ex) { Message = "エクスポート失敗: " + ex.Message; }
    }
    internal bool ImportInto(string path, CharacterProfile target, bool replaceMatching)
    {
        try
        {
            if (!Config.Profiles.Contains(target)) throw new InvalidDataException("登録済みキャラクターを選んでください。");
            var count = VoicePack.ImportInto(path, voiceFolder, target, replaceMatching);
            Save();
            Message = $"ボイス{count}件を {target.DisplayName} にコピーしました（キャラクター情報は変更していません）。";
            return true;
        }
        catch (Exception ex)
        {
            Message = "インポート失敗: " + ex.Message;
            return false;
        }
    }
    internal void Save() => pi.SavePluginConfig(Config);
    internal void SetMessage(string value) => Message = value;
    private void Draw() => windows.Draw();
    private void Open() => mainWindow.IsOpen = true;

    public void Dispose()
    {
        disposed = true;
        isRecording = false;
        framework.Update -= OnUpdate;
        useActionHook.Dispose();
        pi.UiBuilder.Draw -= Draw;
        pi.UiBuilder.OpenMainUi -= Open;
        pi.UiBuilder.OpenConfigUi -= Open;
        commands.RemoveHandler("/actionvoice");
        windows.RemoveAllWindows();
        previewAudio.Dispose();
        audio.Dispose();
        Save();
    }
}
