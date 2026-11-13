using System.Reflection;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;

namespace Spirewise;

/// <summary>
/// Rewind: every time the game saves a solo run (after each room), keep a copy. From the main menu the player can put
/// an earlier copy back as the current save and press Continue to replay from that floor.
///
/// Uses the game's own save format and save writer, so nothing about combat state is rebuilt by hand. Solo only (co-op
/// saves live on the host and every player has to rejoin). Daily runs are never snapshotted. A run that was rewound is
/// recorded as a custom run in its history file, so it stays out of community stats (Spire Codex and others drop custom
/// runs) and out of SpireWise's own Run Stats unless asked for.
/// </summary>
public static class Rewind
{
    public sealed class Snapshot
    {
        public string Path = "";
        public int Floor;
        public int Act;
        public string Room = "";
        public bool AfterRoom;
        public DateTime SavedAt;
    }

    public sealed class RunSnapshots
    {
        public long StartTime;
        public string Character = "";
        public int Ascension;
        public string Build = "";
        public List<Snapshot> Snapshots = new();
    }

    private sealed class Meta
    {
        public long StartTime { get; set; }
        public string Character { get; set; } = "";
        public int Ascension { get; set; }
        public string Build { get; set; } = "";
    }

    private const int KeepRuns = 5;
    private const int KeepPerRun = 120;
    private static readonly object Gate = new();
    private static byte[]? _lastBytes;

    public static string Root => System.IO.Path.Combine(OS.GetUserDataDir(), "SpireWise", "rewind");
    private static string RewoundListPath => System.IO.Path.Combine(OS.GetUserDataDir(), "SpireWise", "rewound_runs.json");
    private static string? GameBuild => MegaCrit.Sts2.Core.Debug.ReleaseInfoManager.Instance.ReleaseInfo?.Version;

    // ---------------- snapshotting ----------------

    /// <summary>Prefix on RunSaveManager.SaveRun(SerializableRun save, bool isMultiplayer).</summary>
    public static void SaveRunPrefix(SerializableRun save, bool isMultiplayer)
    {
        try
        {
            if (isMultiplayer || save == null || save.DailyTime != null || !Settings.Current.Rewind) return;
            var bytes = JsonSerializer.SerializeToUtf8Bytes(save, JsonSerializationUtility.GetTypeInfo<SerializableRun>());
            var floor = save.MapPointHistory?.Sum(a => a.Count) ?? 0;
            var last = save.MapPointHistory?.LastOrDefault(a => a.Count > 0)?.LastOrDefault();
            var meta = new Meta
            {
                StartTime = save.StartTime,
                Character = save.Players?.FirstOrDefault()?.CharacterId?.ToString() ?? "",
                Ascension = save.Ascension,
                Build = GameBuild ?? "",
            };
            string room = last?.MapPointType.ToString() ?? "start";
            bool after = save.PreFinishedRoom != null;
            int act = save.CurrentActIndex + 1;
            // Disk writes off the main thread; the bytes are already a complete copy.
            Task.Run(() => Write(meta, bytes, floor, act, room, after));
        }
        catch (Exception e) { Plugin.LogError($"rewind snapshot failed: {e.Message}"); }
    }

    private static void Write(Meta meta, byte[] bytes, int floor, int act, string room, bool after)
    {
        try
        {
            lock (Gate)
            {
                if (_lastBytes != null && _lastBytes.AsSpan().SequenceEqual(bytes)) return;
                _lastBytes = bytes;
                var dir = System.IO.Path.Combine(Root, meta.StartTime.ToString());
                System.IO.Directory.CreateDirectory(dir);
                var metaPath = System.IO.Path.Combine(dir, "run.json");
                if (!System.IO.File.Exists(metaPath)) System.IO.File.WriteAllText(metaPath, JsonSerializer.Serialize(meta));
                int seq = System.IO.Directory.GetFiles(dir, "*.save").Length + 1;
                // name carries what the list shows, so listing never has to open the saves
                var name = $"{seq:D4}_a{act}_f{floor:D2}_{Clean(room)}_{(after ? "after" : "start")}.save";
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, name), bytes);
                Prune(dir);
            }
        }
        catch (Exception e) { Plugin.LogError($"rewind snapshot write failed: {e.Message}"); }
    }

    private static string Clean(string s) => new string(s.Where(char.IsLetterOrDigit).ToArray());

    private static void Prune(string currentDir)
    {
        var saves = System.IO.Directory.GetFiles(currentDir, "*.save").OrderBy(f => f).ToList();
        foreach (var f in saves.Take(Math.Max(0, saves.Count - KeepPerRun))) System.IO.File.Delete(f);
        var runs = System.IO.Directory.GetDirectories(Root).OrderByDescending(d => long.TryParse(System.IO.Path.GetFileName(d), out var t) ? t : 0).ToList();
        foreach (var d in runs.Skip(KeepRuns)) System.IO.Directory.Delete(d, recursive: true);
    }

    // ---------------- listing ----------------

    public static List<RunSnapshots> List()
    {
        var result = new List<RunSnapshots>();
        if (!System.IO.Directory.Exists(Root)) return result;
        foreach (var dir in System.IO.Directory.GetDirectories(Root))
        {
            try
            {
                var meta = JsonSerializer.Deserialize<Meta>(System.IO.File.ReadAllText(System.IO.Path.Combine(dir, "run.json")));
                if (meta == null) continue;
                var run = new RunSnapshots { StartTime = meta.StartTime, Character = meta.Character, Ascension = meta.Ascension, Build = meta.Build };
                foreach (var f in System.IO.Directory.GetFiles(dir, "*.save").OrderByDescending(f => f))
                {
                    var p = System.IO.Path.GetFileNameWithoutExtension(f).Split('_');
                    if (p.Length < 5) continue;
                    run.Snapshots.Add(new Snapshot
                    {
                        Path = f, Act = int.TryParse(p[1].TrimStart('a'), out var a) ? a : 0, Floor = int.TryParse(p[2].TrimStart('f'), out var fl) ? fl : 0,
                        Room = p[3], AfterRoom = p[4] == "after", SavedAt = System.IO.File.GetLastWriteTime(f),
                    });
                }
                if (run.Snapshots.Count > 0) result.Add(run);
            }
            catch (Exception e) { Plugin.LogError($"rewind: skipping {dir}: {e.Message}"); }
        }
        return result.OrderByDescending(r => r.StartTime).ToList();
    }

    // ---------------- restoring ----------------

    private static readonly FieldInfo? RunSaveManagerField = AccessTools.Field(typeof(SaveManager), "_runSaveManager");

    /// <summary>Put a snapshot back as the current solo save. Returns null on success, or a reason it was refused.</summary>
    public static async Task<string?> Restore(RunSnapshots run, Snapshot snap)
    {
        try
        {
            if (!string.IsNullOrEmpty(run.Build) && GameBuild != null && run.Build != GameBuild)
                return $"This snapshot is from game {run.Build}; you're on {GameBuild}. Saves don't carry across versions.";
            var sm = SaveManager.Instance;
            if (sm.HasRunSave)
            {
                var cur = sm.LoadRunSave();
                if (cur.Success && cur.SaveData != null && cur.SaveData.StartTime != run.StartTime)
                    return "You have a different run in progress. Finish or abandon it first, so it isn't overwritten.";
            }
            var save = JsonSerializer.Deserialize(System.IO.File.ReadAllBytes(snap.Path), JsonSerializationUtility.GetTypeInfo<SerializableRun>());
            if (save == null) return "That snapshot couldn't be read.";
            save.NumReloads += 1;
            MarkRewound(run.StartTime);
            if (RunSaveManagerField?.GetValue(sm) is not RunSaveManager rsm) return "The game's save system changed; Rewind can't write saves.";
            lock (Gate) _lastBytes = null;
            await rsm.SaveRun(save, isMultiplayer: false);
            Plugin.Log($"rewind: restored run {run.StartTime} to act {snap.Act} floor {snap.Floor} ({snap.Room}, {(snap.AfterRoom ? "after" : "start")})");
            return null;
        }
        catch (Exception e)
        {
            Plugin.LogError($"rewind restore failed: {e}");
            return "Restore failed: " + e.Message;
        }
    }

    // ---------------- marking rewound runs ----------------

    private static HashSet<long>? _rewound;

    public static HashSet<long> Rewound
    {
        get
        {
            if (_rewound != null) return _rewound;
            try { _rewound = System.IO.File.Exists(RewoundListPath) ? JsonSerializer.Deserialize<HashSet<long>>(System.IO.File.ReadAllText(RewoundListPath)) : null; }
            catch { _rewound = null; }
            return _rewound ??= new HashSet<long>();
        }
    }

    private static void MarkRewound(long startTime)
    {
        if (!Rewound.Add(startTime)) return;
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(RewoundListPath)!);
        System.IO.File.WriteAllText(RewoundListPath, JsonSerializer.Serialize(Rewound));
    }

    /// <summary>
    /// Prefix on SaveManager.SaveRunHistory: a rewound run is written as a custom run. v0.113+ has is_custom_game;
    /// v0.111 marks custom runs with GameMode.Custom.
    /// </summary>
    public static void SaveRunHistoryPrefix(RunHistory history)
    {
        try
        {
            if (history == null || !Rewound.Contains(history.StartTime)) return;
            var t = history.GetType();
            var custom = t.GetProperty("IsCustomGame");
            if (custom != null && custom.CanWrite) custom.SetValue(history, true);
            else if (t.GetProperty("GameMode") is { CanWrite: true } gm && Enum.TryParse(gm.PropertyType, "Custom", out var v)) gm.SetValue(history, v);
            Plugin.Log($"rewind: run {history.StartTime} was rewound, saved to history as a custom run");
        }
        catch (Exception e) { Plugin.LogError($"rewind: couldn't mark history: {e.Message}"); }
    }
}
