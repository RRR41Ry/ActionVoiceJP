using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;

namespace ActionVoiceJP;

internal sealed class MainWindow : Window
{
    private readonly Plugin plugin;
    private int selectedProfile = -1;
    private int selectedRule = -1;
    private string browseDirectory = "";
    private string directoryInput = "";
    private string selectedZip = "";
    private bool browserOpen;
    private bool browserForImport;
    private string browserSearch = "";
    private string exportFileName = "";
    private bool showFavoritesOnly;
    private readonly System.Collections.Generic.List<string> browserHistory = new();
    private int historyIndex = -1;
    private bool importConfirm;
    private bool importReplaceMatching;

    private string browserError = "";
    private string actionFilter = "";
    // User-created category names. A category is just an organization label,
    // not a game job lookup. Existing voice rules retain their category.
    private string selectedJob = "共通";
    private string newGroupName = "";
    private string renameGroupName = "";
    private string renameTarget = "";
    private string groupError = "";

    private static string JobOf(VoiceRule rule) => string.IsNullOrWhiteSpace(rule.Job) ? "共通" : rule.Job;
    private static int CountJob(CharacterProfile profile, string job) => profile.Rules.Count(r => JobOf(r) == job);
    private static void EnsureGroups(CharacterProfile profile)
    {
        profile.Groups ??= new();
        profile.Rules ??= new();
        // Import older profiles and older ZIP packs without losing categories.
        foreach (var rule in profile.Rules)
        {
            var group = JobOf(rule);
            if (group != "共通" && !profile.Groups.Contains(group, StringComparer.OrdinalIgnoreCase))
                profile.Groups.Add(group);
        }
    }
    private void SelectGroup(string group)
    {
        selectedJob = group;
        selectedRule = -1;
        renameTarget = "";
        groupError = "";
    }
    private void DrawJobTabs(CharacterProfile profile)
    {
        EnsureGroups(profile);
        ImGui.Text("アクショングループ（自由な名前で管理）");
        ImGui.TextDisabled("「召喚士」「戦士」「お気に入り」など好きな名前のグループを作れます。");
        var names = profile.Groups.ToList();
        if (CountJob(profile, "共通") > 0 || (names.Count == 0 && selectedJob == "共通"))
            names.Insert(0, "共通");
        if (!names.Contains(selectedJob)) selectedJob = names.FirstOrDefault() ?? "共通";

        // Only user-created categories are shown as tabs; wrap within window width.
        float usedWidth = 0;
        float lineWidth = Math.Max(160f, ImGui.GetContentRegionAvail().X);
        foreach (var group in names)
        {
            var label = $"{group} ({CountJob(profile, group)})";
            float buttonWidth = ImGui.CalcTextSize(label).X + ImGui.GetStyle().FramePadding.X * 2 + 14f;
            if (usedWidth > 0 && usedWidth + buttonWidth < lineWidth) ImGui.SameLine();
            else usedWidth = 0;
            bool current = group == selectedJob;
            if (current) ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(.40f, .26f, .36f, 1f));
            if (ImGui.Button(label + "##usergroup_" + group)) SelectGroup(group);
            if (current) ImGui.PopStyleColor();
            usedWidth += buttonWidth + ImGui.GetStyle().ItemSpacing.X;
        }
        // Compact selector also works when numerous long category names exist.
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##groupSelector", $"選択中：{selectedJob}"))
        {
            foreach (var group in names)
            {
                if (ImGui.Selectable($"{group} ({CountJob(profile, group)}件)##group", group == selectedJob))
                    SelectGroup(group);
            }
            ImGui.EndCombo();
        }
        ImGui.Text("グループを作成");
        ImGui.SetNextItemWidth(Math.Max(150f, ImGui.GetContentRegionAvail().X - 125f));
        ImGui.InputTextWithHint("##newGroup", "例：召喚士 / 戦士 / 特殊ボイス", ref newGroupName, 60);
        ImGui.SameLine();
        if (ImGui.Button("＋ 作成##group"))
        {
            var candidate = newGroupName.Trim();
            if (string.IsNullOrWhiteSpace(candidate)) groupError = "グループ名を入力してください。";
            else if (candidate.Length > 40) groupError = "40文字以内で入力してください。";
            else if (candidate == "共通" || profile.Groups.Any(g => string.Equals(g, candidate, StringComparison.OrdinalIgnoreCase)))
                groupError = "同じ名前のグループは登録できません。";
            else
            {
                profile.Groups.Add(candidate);
                SelectGroup(candidate);
                newGroupName = "";
                plugin.Save();
            }
        }
        if (selectedJob != "共通" && profile.Groups.Contains(selectedJob))
        {
            if (renameTarget != selectedJob)
            {
                renameTarget = selectedJob;
                renameGroupName = selectedJob;
            }
            ImGui.SetNextItemWidth(Math.Max(150f, ImGui.GetContentRegionAvail().X - 145f));
            ImGui.InputText("##renameGroup", ref renameGroupName, 60);
            ImGui.SameLine();
            if (ImGui.Button("名前変更##group"))
            {
                var candidate = renameGroupName.Trim();
                if (candidate.Length == 0 || candidate.Length > 40 || candidate == "共通" ||
                    profile.Groups.Any(g => g != selectedJob && string.Equals(g, candidate, StringComparison.OrdinalIgnoreCase)))
                    groupError = "別の有効な名前を入力してください（40文字以内）。";
                else
                {
                    var old = selectedJob;
                    int index = profile.Groups.IndexOf(old);
                    profile.Groups[index] = candidate;
                    foreach (var rule in profile.Rules)
                        if (JobOf(rule) == old) rule.Job = candidate;
                    SelectGroup(candidate);
                    plugin.Save();
                }
            }
            ImGui.SameLine();
            if (ImGui.Button("削除##group"))
            {
                // Never remove rules: move them to the unclassified category.
                foreach (var rule in profile.Rules)
                    if (JobOf(rule) == selectedJob) rule.Job = "共通";
                profile.Groups.Remove(selectedJob);
                SelectGroup("共通");
                plugin.Save();
                plugin.SetMessage("グループを削除しました。アクションは「共通」に移動しました。");
            }
        }
        if (!string.IsNullOrEmpty(groupError))
            ImGui.TextColored(new Vector4(1f, .55f, .35f, 1f), groupError);
        ImGui.Text($"現在のグループ：{selectedJob}　登録 {CountJob(profile, selectedJob)}件");
    }
    private string[] wavFiles = Array.Empty<string>();

    public MainWindow(Plugin plugin) : base("Action Voice JP  |  キャラクター別ボイス##avjp")
    {
        this.plugin = plugin;
        Size = new Vector2(850, 640);
        SizeCondition = ImGuiCond.FirstUseEver;
        RefreshFiles();
    }

    private void UpdateActivePreviewVolume()
    {
        var profiles = plugin.Config.Profiles;
        if (selectedProfile < 0 || selectedProfile >= profiles.Count) return;
        var rules = profiles[selectedProfile].Rules;
        if (selectedRule < 0 || selectedRule >= rules.Count) return;
        plugin.UpdatePreviewVolume(rules[selectedRule]);
    }

    private void RefreshFiles()
    {
        try
        {
            wavFiles = Directory.GetFiles(plugin.VoiceFolder, "*.wav", SearchOption.AllDirectories)
                .Select(p => Path.GetRelativePath(plugin.VoiceFolder, p)).OrderBy(s => s).ToArray();
        }
        catch { wavFiles = Array.Empty<string>(); }
    }

    public override void Draw()
    {
        var config = plugin.Config;
        ImGui.TextWrapped("既存のVFX/SE MODは変更しません。追加音声はこのPCだけで再生します。");
        ImGui.TextColored(new Vector4(1f, .75f, .35f, 1f),
            "注意：他人の技は現在『詠唱あり』のみ検知。ドリル等の無詠唱アクションは未対応です。");
        ImGui.Separator();
        var enabled = config.Enabled;
        if (ImGui.Checkbox("有効", ref enabled)) { config.Enabled = enabled; plugin.Save(); }
        ImGui.SameLine();
        var master = config.MasterVolume * 100;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderFloat("全体音量", ref master, 0, 100, "%.0f%%"))
        { config.MasterVolume = master / 100; plugin.Save(); UpdateActivePreviewVolume(); }
        ImGui.Separator();

        ImGui.Columns(2, "profile-list", true);
        ImGui.SetColumnWidth(0, 255);
        ImGui.Text("キャラクター");
        if (ImGui.Button("＋ 手動で追加"))
        {
            config.Profiles.Add(new CharacterProfile());
            selectedProfile = config.Profiles.Count - 1;
            selectedRule = -1;
            selectedJob = "共通";
            plugin.Save();
        }
        if (ImGui.Button("🎯 ターゲットから追加"))
        {
            var target = plugin.TargetIdentity();
            if (target == null) plugin.SetMessage("プレイヤーをターゲットしてください。");
            else
            {
                config.Profiles.Add(new CharacterProfile { Name = target.Value.Name, HomeWorld = target.Value.World });
                selectedProfile = config.Profiles.Count - 1;
                selectedRule = -1;
                selectedJob = "共通";
                plugin.Save();
            }
        }
        ImGui.Separator();
        for (var i = 0; i < config.Profiles.Count; i++)
        {
            var profile = config.Profiles[i];
            var title = $"{(profile.Enabled ? "●" : "○")} {profile.DisplayName}##profile{i}";
            if (ImGui.Selectable(title, selectedProfile == i)) { selectedProfile = i; selectedRule = -1; EnsureGroups(profile); selectedJob = profile.Groups.FirstOrDefault() ?? "共通"; renameTarget = ""; }
        }
        ImGui.NextColumn();
        if (selectedProfile < 0 || selectedProfile >= config.Profiles.Count)
        {
            ImGui.TextWrapped("左でキャラクターを登録してください。フレンドをターゲットして登録することもできます。");
        }
        else
        {
            DrawProfile(config.Profiles[selectedProfile]);
        }
        ImGui.Columns(1);
        ImGui.Separator();
        ImGui.Text("共有ボイスパック（ZIP）");
        ImGui.TextDisabled("ZIPにはボイスとグループ設定だけ保存します。キャラクター名やワールドは含めません。");
        ImGui.TextDisabled("読み込み時は左で選択したキャラクターに一括コピーします。お気に入りは共通です。");
        if (ImGui.Button("保存先を選んでZIPに出力")) OpenBrowser(false);
        ImGui.SameLine();
        if (ImGui.Button("選択中のキャラへZIPをコピー"))
        {
            if (selectedProfile < 0 || selectedProfile >= config.Profiles.Count)
                plugin.SetMessage("先に左側でコピー先のキャラクターを選んでください。");
            else OpenBrowser(true);
        }
        DrawPathBrowser();
        ImGui.TextWrapped("WAVフォルダー: " + plugin.VoiceFolder);
        ImGui.TextWrapped("ZIP出力先: " + plugin.PackFolder);
        ImGui.TextWrapped("状態: " + plugin.Message);
    }

    // This browser is drawn inside Dalamud via ImGui; no Windows or VFXEditor dependency.
    private void OpenBrowser(bool isImport)
    {
        browserForImport = isImport;
        browserOpen = true;
        browserError = "";
        browserSearch = "";
        showFavoritesOnly = false;
        selectedZip = "";
        importConfirm = false;
        importReplaceMatching = false;
        browserHistory.Clear();
        historyIndex = -1;
        var cfg = plugin.Config;
        var preferred = isImport ? cfg.LastImportDirectory : cfg.LastExportDirectory;
        if (string.IsNullOrWhiteSpace(preferred) || !Directory.Exists(preferred)) preferred = plugin.PackFolder;
        NavigateTo(preferred);
        if (!isImport && selectedProfile >= 0 && selectedProfile < cfg.Profiles.Count)
        {
            exportFileName = "VoicePack.zip";
        }
        ImGui.OpenPopup("ファイルを選択 / VoicePack##voiceBrowser");
    }

    private void NavigateTo(string path, bool keepHistory = false)
    {
        try
        {
            var full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')));
            if (!Directory.Exists(full)) throw new DirectoryNotFoundException("フォルダが見つかりません: " + full);
            browseDirectory = full;
            directoryInput = full;
            selectedZip = "";
            browserError = "";
            if (!keepHistory)
            {
                if (historyIndex < browserHistory.Count - 1)
                    browserHistory.RemoveRange(historyIndex + 1, browserHistory.Count - historyIndex - 1);
                if (browserHistory.Count == 0 || !string.Equals(browserHistory[^1], full, StringComparison.OrdinalIgnoreCase))
                    browserHistory.Add(full);
                if (browserHistory.Count > 50) browserHistory.RemoveAt(0);
                historyIndex = browserHistory.Count - 1;
            }
        }
        catch (Exception ex) { browserError = "開けません: " + ex.Message; }
    }

    private void GoHistory(int delta)
    {
        var pos = historyIndex + delta;
        if (pos < 0 || pos >= browserHistory.Count) return;
        historyIndex = pos;
        NavigateTo(browserHistory[pos], true);
    }

    private bool SearchMatches(string name) =>
        string.IsNullOrWhiteSpace(browserSearch) || name.Contains(browserSearch, StringComparison.OrdinalIgnoreCase);

    private void RememberBrowserDirectory(bool importing)
    {
        if (importing) plugin.Config.LastImportDirectory = browseDirectory;
        else plugin.Config.LastExportDirectory = browseDirectory;
        plugin.Save();
    }

    private void DrawPathBrowser()
    {
        if (!browserOpen) return;
        ImGui.SetNextWindowSize(new Vector2(920, 620), ImGuiCond.FirstUseEver);
        bool open = browserOpen;
        if (!ImGui.BeginPopupModal("ファイルを選択 / VoicePack##voiceBrowser", ref open, ImGuiWindowFlags.NoCollapse))
        {
            browserOpen = open;
            return;
        }
        browserOpen = open;
        var config = plugin.Config;
        config.FavoritePaths ??= new();

        ImGui.Text(browserForImport ? "ボイス設定を選択中のキャラへコピー" : "ボイス設定だけZIPに書き出す");
        if (browserForImport && selectedProfile >= 0 && selectedProfile < config.Profiles.Count)
            ImGui.TextColored(new Vector4(.5f, .9f, .6f, 1f), "コピー先: " + config.Profiles[selectedProfile].DisplayName);
        ImGui.SameLine();
        ImGui.TextDisabled("  C: / D: / お気に入り から選択できます");

        if (ImGui.Button("← 戻る") && historyIndex > 0) GoHistory(-1);
        ImGui.SameLine();
        if (ImGui.Button("進む →") && historyIndex < browserHistory.Count - 1) GoHistory(1);
        ImGui.SameLine();
        if (ImGui.Button("↑ 上へ"))
        {
            var parent = Directory.GetParent(browseDirectory);
            if (parent != null) NavigateTo(parent.FullName);
        }
        ImGui.SameLine();
        if (ImGui.Button("⌂ 既定")) NavigateTo(plugin.PackFolder);
        ImGui.SameLine();
        if (ImGui.Button("☆ お気に入りに登録"))
        {
            if (!config.FavoritePaths.Any(x => string.Equals(x, browseDirectory, StringComparison.OrdinalIgnoreCase)))
            { config.FavoritePaths.Add(browseDirectory); plugin.Save(); }
        }
        ImGui.SameLine();
        if (ImGui.Button("更新")) { selectedZip = ""; browserError = ""; }

        // Editable address bar + breadcrumb-like directory navigation.
        ImGui.SetNextItemWidth(-95);
        bool enter = ImGui.InputText("##address", ref directoryInput, 2048, ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();
        if (ImGui.Button("移動") || enter) NavigateTo(directoryInput);

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##fileSearch", "このフォルダ内を検索（名前）", ref browserSearch, 160);
        ImGui.Separator();

        var available = ImGui.GetContentRegionAvail();
        float panelHeight = Math.Max(190, available.Y - 112);
        float sidebarWidth = Math.Min(190, Math.Max(135, available.X * .26f));
        ImGui.BeginChild("##browserSidebar", new Vector2(sidebarWidth, panelHeight), true);
        ImGui.TextDisabled("ドライブ");
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                // GetDrives can list removable drives without media; NavigateTo validates them.
                if (ImGui.Selectable("▣ " + drive.Name + "##drive", browseDirectory.StartsWith(drive.Name, StringComparison.OrdinalIgnoreCase)))
                    NavigateTo(drive.RootDirectory.FullName);
            }
        }
        catch (Exception ex) { ImGui.TextDisabled("ドライブ取得不可: " + ex.Message); }
        ImGui.Separator();
        ImGui.TextDisabled("クイックアクセス");
        var quick = new (string Label, string Path)[] {
            ("Downloads", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")),
            ("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
            ("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
            ("既定VoicePacks", plugin.PackFolder)
        };
        foreach (var entry in quick)
            if (ImGui.Selectable("▸ " + entry.Label + "##quick")) NavigateTo(entry.Path);
        ImGui.Separator();
        ImGui.TextDisabled("★ お気に入り（共通）");
        for (int i = 0; i < config.FavoritePaths.Count; i++)
        {
            var path = config.FavoritePaths[i];
            ImGui.PushID(i + 88000);
            if (ImGui.SmallButton("×"))
            {
                config.FavoritePaths.RemoveAt(i);
                plugin.Save();
                ImGui.PopID();
                i--;
                continue;
            }
            ImGui.SameLine();
            if (ImGui.Selectable(Path.GetFileName(Path.TrimEndingDirectorySeparator(path)) is { Length: > 0 } n ? n : path))
                NavigateTo(path);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(path);
            ImGui.PopID();
        }
        ImGui.EndChild();
        ImGui.SameLine();
        ImGui.BeginChild("##browserItems", new Vector2(0, panelHeight), true);
        ImGui.TextDisabled("名前                                      種類");
        ImGui.Separator();
        try
        {
            var folders = Directory.EnumerateDirectories(browseDirectory)
                .Where(x => SearchMatches(Path.GetFileName(x)))
                .OrderBy(x => Path.GetFileName(x), StringComparer.OrdinalIgnoreCase)
                .Take(300).ToArray();
            foreach (var folder in folders)
            {
                var label = "📁 " + Path.GetFileName(folder);
                ImGui.PushID(folder);
                if (ImGui.Selectable(label + "    <フォルダ>", false, ImGuiSelectableFlags.AllowDoubleClick))
                    NavigateTo(folder);
                ImGui.PopID();
            }
            if (browserForImport)
            {
                var files = Directory.EnumerateFiles(browseDirectory, "*.zip")
                    .Where(x => SearchMatches(Path.GetFileName(x)))
                    .OrderBy(x => Path.GetFileName(x), StringComparer.OrdinalIgnoreCase)
                    .Take(300).ToArray();
                foreach (var zip in files)
                {
                    ImGui.PushID(zip);
                    var selected = string.Equals(selectedZip, zip, StringComparison.OrdinalIgnoreCase);
                    if (ImGui.Selectable("▣ " + Path.GetFileName(zip) + "    ZIP", selected, ImGuiSelectableFlags.AllowDoubleClick))
                    {
                        selectedZip = zip;
                        importConfirm = ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left);
                    }
                    ImGui.PopID();
                }
            }
        }
        catch (Exception ex) { browserError = "一覧を表示できません: " + ex.Message; }
        ImGui.EndChild();
        ImGui.Separator();
        if (browserForImport)
        {
            ImGui.SetNextItemWidth(-1);
            ImGui.InputText("##chosenZip", ref selectedZip, 2048, ImGuiInputTextFlags.ReadOnly);
            ImGui.Checkbox("同じアクションID・グループの設定は置き換える", ref importReplaceMatching);
            ImGui.TextDisabled(importReplaceMatching
                ? "一致したボイス設定だけ置き換えます。その他の設定は残ります。"
                : "現在のボイスを残し、ZIPの登録内容を追加します。");
            if (ImGui.Button("選択したZIPをコピー") || importConfirm)
            {
                importConfirm = false;
                if (selectedProfile < 0 || selectedProfile >= config.Profiles.Count)
                    browserError = "先にコピー先キャラクターを選んでください。";
                else if (string.IsNullOrWhiteSpace(selectedZip)) browserError = "ZIPファイルを選んでください。";
                else
                {
                    var target = config.Profiles[selectedProfile];
                    if (plugin.ImportInto(selectedZip, target, importReplaceMatching))
                    {
                        selectedRule = -1;
                        EnsureGroups(target);
                        selectedJob = target.Groups.FirstOrDefault() ?? "共通";
                        RememberBrowserDirectory(true);
                        RefreshFiles();
                        browserOpen = false;
                        ImGui.CloseCurrentPopup();
                    }
                    else browserError = plugin.Message;
                }
            }
        }
        else
        {
            ImGui.Text("保存ファイル名（.zip）");
            ImGui.SetNextItemWidth(-1);
            ImGui.InputText("##saveName", ref exportFileName, 240);
            if (ImGui.Button("この場所に保存"))
            {
                if (selectedProfile < 0 || selectedProfile >= config.Profiles.Count)
                    browserError = "先にキャラクターを選んでください。";
                else if (string.IsNullOrWhiteSpace(exportFileName)) browserError = "ファイル名を入力してください。";
                else
                {
                    plugin.Export(config.Profiles[selectedProfile], browseDirectory, exportFileName);
                    if (plugin.Message.StartsWith("共有ZIPを作成しました", StringComparison.Ordinal))
                    {
                        RememberBrowserDirectory(false);
                        browserOpen = false;
                        ImGui.CloseCurrentPopup();
                    }
                    else browserError = plugin.Message;
                }
            }
        }
        ImGui.SameLine();
        if (ImGui.Button("キャンセル")) { browserOpen = false; ImGui.CloseCurrentPopup(); }
        if (!string.IsNullOrWhiteSpace(browserError))
            ImGui.TextColored(new Vector4(1f, .55f, .35f, 1f), browserError);
        ImGui.EndPopup();
    }

    private void DrawProfile(CharacterProfile profile)
    {
        var active = profile.Enabled;
        if (ImGui.Checkbox("この人物を有効", ref active)) { profile.Enabled = active; plugin.Save(); }
        var name = profile.Name;
        if (ImGui.InputText("キャラクター名", ref name, 100)) { profile.Name = name; plugin.Save(); }
        var world = profile.HomeWorld;
        if (ImGui.InputText("ホームワールド（例: Aegis）", ref world, 100)) { profile.HomeWorld = world; plugin.Save(); }
        ImGui.TextDisabled("フレンドが別ワールドにいても、ホームワールドで照合します。");
        ImGui.Separator();
        DrawJobTabs(profile);
        ImGui.Separator();
        if (ImGui.CollapsingHeader("アクション測定（自分の技）", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.TextDisabled("測定開始 → 技を使う → 履歴から登録。名前も自動取得します。");
            if (!plugin.IsRecording)
            {
                if (ImGui.Button("● 測定開始")) plugin.StartRecording();
            }
            else
            {
                if (ImGui.Button("■ 測定停止")) plugin.StopRecording();
            }
            ImGui.SameLine();
            if (ImGui.Button("履歴を消去")) plugin.ClearRecording();
            if (plugin.IsRecording)
                ImGui.TextColored(new Vector4(1f, .8f, .35f, 1f), "測定中：自分のアクションを使ってください");
            if (plugin.RecentActions.Count == 0)
                ImGui.TextDisabled("測定履歴はまだありません。");
            foreach (var actionId in plugin.RecentActions)
            {
                ImGui.PushID((int)actionId);
                ImGui.Separator();
                var actionName = plugin.ActionName(actionId);
                ImGui.Text($"{actionName}  |  ID: {actionId}");
                if (ImGui.SmallButton("＋ 新規ボイスに登録"))
                {
                    profile.Rules.Add(new VoiceRule { ActionId = actionId, Label = actionName, Job = selectedJob });
                    selectedRule = profile.Rules.Count - 1;
                    plugin.Save();
                    plugin.SetMessage($"{actionName} (ID {actionId}) を登録しました。");
                }
                if (selectedRule >= 0 && selectedRule < profile.Rules.Count)
                {
                    ImGui.SameLine();
                    if (ImGui.SmallButton("選択中のボイスへ適用"))
                    {
                        profile.Rules[selectedRule].ActionId = actionId;
                        profile.Rules[selectedRule].Label = actionName;
                        // Keep the group the user chose, even when applying a captured ID.
                        plugin.Save();
                        plugin.SetMessage($"選択中のボイスを {actionName} に設定しました。");
                    }
                }
                ImGui.PopID();
            }
            ImGui.TextDisabled("※ 検出対象は自分の技のみ。他人の無詠唱アクションには未対応。");
        }
        ImGui.Separator();
        if (ImGui.Button("＋ ボイスを追加"))
        {
            profile.Rules.Add(new VoiceRule { Job = selectedJob });
            selectedRule = profile.Rules.Count - 1;
            plugin.Save();
        }
        ImGui.SameLine();
        if (ImGui.Button("この人物を削除"))
        {
            plugin.Config.Profiles.RemoveAt(selectedProfile);
            selectedProfile = -1;
            selectedRule = -1;
            plugin.Save();
            return;
        }
        ImGui.Separator();
        ImGui.Text($"{selectedJob} の登録済みアクション  ({CountJob(profile, selectedJob)}件) / 全{profile.Rules.Count}件");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##actionFilter", "アクション名・IDで検索", ref actionFilter, 100);
        if (CountJob(profile, selectedJob) == 0)
            ImGui.TextDisabled("このグループにはまだアクションがありません。測定履歴から登録できます。");
        for (var i = 0; i < profile.Rules.Count; i++)
        {
            var rule = profile.Rules[i];
            if (JobOf(rule) != selectedJob) continue;
            var actionName = plugin.ActionName(rule.ActionId);
            var title = string.IsNullOrWhiteSpace(rule.Label) || rule.Label.StartsWith("新しいアクション") || rule.Label.StartsWith("アクション ")
                ? actionName : rule.Label;
            if (!string.IsNullOrWhiteSpace(actionFilter) &&
                !title.Contains(actionFilter, StringComparison.OrdinalIgnoreCase) &&
                !actionName.Contains(actionFilter, StringComparison.OrdinalIgnoreCase) &&
                !rule.ActionId.ToString().Contains(actionFilter, StringComparison.OrdinalIgnoreCase)) continue;
            ImGui.PushID(i);
            var header = $"{(rule.Enabled ? "●" : "○")} {title}  |  ID: {rule.ActionId}##voiceRule";
            bool open = ImGui.CollapsingHeader(header, ImGuiTreeNodeFlags.None);
            // An open row is also the selected rule used by the capture panel.
            if (ImGui.IsItemClicked()) selectedRule = i;
            if (open)
            {
                selectedRule = i;
                ImGui.Indent();
                ImGui.TextDisabled($"ゲーム内アクション名：{actionName}");
                if (ImGui.BeginCombo("登録グループ", JobOf(rule)))
                {
                    var selectableGroups = new[] { "共通" }.Concat(profile.Groups).Distinct(StringComparer.OrdinalIgnoreCase);
                    foreach (var job in selectableGroups)
                    {
                        if (ImGui.Selectable(job, JobOf(rule) == job))
                        {
                            rule.Job = job;
                            selectedJob = job;
                            selectedRule = -1;
                            plugin.Save();
                        }
                    }
                    ImGui.EndCombo();
                }
                var on = rule.Enabled;
                if (ImGui.Checkbox("この音声を有効", ref on)) { rule.Enabled = on; plugin.Save(); }
                var label = rule.Label;
                if (ImGui.InputText("表示名（自由に変更可）", ref label, 100)) { rule.Label = label; plugin.Save(); }
                var id = (int)Math.Min(rule.ActionId, int.MaxValue);
                if (ImGui.InputInt("アクションID", ref id))
                {
                    rule.ActionId = (uint)Math.Clamp(id, 0, int.MaxValue);
                    if (rule.Label.StartsWith("新しいアクション") || rule.Label.StartsWith("アクション "))
                        rule.Label = plugin.ActionName(rule.ActionId);
                    plugin.Save();
                }
                var path = rule.WavePath;
                if (ImGui.InputText("WAVファイル", ref path, 1024)) { rule.WavePath = path; plugin.Save(); }
                if (ImGui.Button("音声一覧を更新")) RefreshFiles();
                if (ImGui.BeginCombo("音声を選ぶ", string.IsNullOrEmpty(rule.WavePath) ? "未選択" : Path.GetFileName(rule.WavePath)))
                {
                    foreach (var file in wavFiles)
                        if (ImGui.Selectable(file, string.Equals(rule.WavePath, file, StringComparison.OrdinalIgnoreCase)))
                        { rule.WavePath = file; plugin.Save(); }
                    ImGui.EndCombo();
                }
                var volume = rule.Volume * 100;
                if (ImGui.SliderFloat("音量", ref volume, 0, 100, "%.0f%%"))
                { rule.Volume = volume / 100; plugin.Save(); plugin.UpdatePreviewVolume(rule); }
                var delay = rule.DelayMs;
                if (ImGui.SliderInt("遅延 (ms)", ref delay, 0, 3000)) { rule.DelayMs = delay; plugin.Save(); }
                if (ImGui.Button("▶ 試聴")) plugin.Preview(rule);
                ImGui.SameLine();
                if (ImGui.Button("■ 試聴停止")) plugin.StopPreview();
                ImGui.SameLine();
                if (ImGui.Button("このボイスを削除"))
                {
                    profile.Rules.RemoveAt(i);
                    selectedRule = -1;
                    plugin.Save();
                    ImGui.Unindent();
                    ImGui.PopID();
                    break;
                }
                ImGui.Unindent();
            }
            ImGui.PopID();
        }

    }
}
