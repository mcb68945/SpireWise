using System.Text.Json;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Saves;

namespace Spirewise;

/// <summary>One finished run from the player's history, reduced to what Run Stats shows. Holds no player IDs.</summary>
public sealed class RunRec
{
    public long StartTime;
    public DateTime Date;
    public string Character = "";      // e.g. "ironclad"
    public int Ascension;
    public string Mode = "Standard";   // Standard, Daily, Blitz, Dread, Custom
    public string Build = "";
    public bool Win, Abandoned, Rewound;
    public int Floors, Players, RunSeconds;
    public string KilledBy = "";
    public List<string> Deck = new();
    public List<string> Relics = new();
    public List<(string Encounter, string Type, int Damage)> Fights = new();
    public List<(string Card, bool Picked)> Offers = new();
}

/// <summary>Loads the player's run history (the game's .run files) through the game's own loader, on both v0.111 and v0.113.</summary>
public static class RunHistoryStore
{
    private static readonly Dictionary<string, RunRec?> Cache = new();

    public static List<RunRec> LoadAll(out int failed)
    {
        failed = 0;
        var list = new List<RunRec>();
        var sm = SaveManager.Instance;
        foreach (var name in sm.GetAllRunHistoryNames())
        {
            if (!Cache.TryGetValue(name, out var rec))
            {
                rec = null;
                try
                {
                    var res = sm.LoadRunHistory(name);
                    if (res.Success && res.SaveData != null)
                    {
                        var h = res.SaveData;
                        ulong local = 0;
                        try { local = PlatformUtil.GetLocalPlayerId(h.PlatformType); } catch { /* offline: fall back to first player */ }
                        rec = Parse(JsonSerializationUtility.ToJson(h), local);
                    }
                }
                catch (Exception e) { Plugin.LogError($"run stats: couldn't read {name}: {e.Message}"); }
                Cache[name] = rec;
            }
            if (rec == null) failed++;
            else { rec.Rewound = rec.Rewound || Rewind.Rewound.Contains(rec.StartTime); list.Add(rec); }
        }
        return list;
    }

    public static RunRec? Parse(string json, ulong localId)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        var rec = new RunRec
        {
            StartTime = Long(r, "start_time"),
            Ascension = (int)Long(r, "ascension"),
            Build = Str(r, "build_id"),
            Win = Bool(r, "win"),
            Abandoned = Bool(r, "was_abandoned"),
            RunSeconds = (int)Long(r, "run_time"),
        };
        rec.Date = DateTimeOffset.FromUnixTimeSeconds(rec.StartTime).LocalDateTime;
        string mode = Str(r, "game_mode").ToLowerInvariant();
        bool custom = Bool(r, "is_custom_game") || mode == "custom";
        rec.Mode = custom ? "Custom" : mode switch { "daily" => "Daily", "blitz" => "Blitz", "dread" => "Dread", _ => "Standard" };
        var killer = Str(r, "killed_by_encounter");
        if (killer == "" || killer.EndsWith("NONE")) killer = Str(r, "killed_by_event");
        rec.KilledBy = rec.Win || killer.EndsWith("NONE") ? "" : Pretty(killer);

        // which player is us: match the platform's local id, else the first player
        if (!r.TryGetProperty("players", out var players) || players.GetArrayLength() == 0) return null;
        rec.Players = players.GetArrayLength();
        JsonElement me = players[0];
        foreach (var p in players.EnumerateArray())
            if (localId != 0 && p.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.GetUInt64() == localId) { me = p; break; }
        ulong myId = me.TryGetProperty("id", out var mid) && mid.ValueKind == JsonValueKind.Number ? mid.GetUInt64() : 0;
        rec.Character = CharKey(Str(me, "character"));
        if (me.TryGetProperty("deck", out var deck)) foreach (var c in deck.EnumerateArray()) rec.Deck.Add(Str(c, "id"));
        if (me.TryGetProperty("relics", out var rel)) foreach (var x in rel.EnumerateArray()) rec.Relics.Add(Str(x, "id"));

        if (r.TryGetProperty("map_point_history", out var acts))
            foreach (var act in acts.EnumerateArray())
                foreach (var node in act.EnumerateArray())
                {
                    rec.Floors++;
                    JsonElement? ps = null;
                    if (node.TryGetProperty("player_stats", out var all))
                        foreach (var s in all.EnumerateArray())
                        {
                            ps ??= s;
                            if (myId != 0 && s.TryGetProperty("player_id", out var pid) && pid.ValueKind == JsonValueKind.Number && pid.GetUInt64() == myId) { ps = s; break; }
                        }
                    string type = Str(node, "map_point_type");
                    if (node.TryGetProperty("rooms", out var rooms))
                        foreach (var room in rooms.EnumerateArray())
                        {
                            var rt = Str(room, "room_type");
                            if (rt is "monster" or "elite" or "boss")
                                rec.Fights.Add((Str(room, "model_id"), rt, ps is { } p0 ? (int)Long(p0, "damage_taken") : 0));
                        }
                    if (ps is { } p1 && p1.TryGetProperty("card_choices", out var cc))
                        foreach (var ch in cc.EnumerateArray())
                        {
                            string card = ch.TryGetProperty("card", out var cd) ? (cd.ValueKind == JsonValueKind.Object ? Str(cd, "id") : cd.GetString() ?? "") : "";
                            if (card != "") rec.Offers.Add((card, Bool(ch, "was_picked")));
                        }
                }
        return rec;
    }

    public static string CharKey(string id) => (id.Contains('.') ? id[(id.IndexOf('.') + 1)..] : id).ToLowerInvariant();

    public static string Pretty(string id)
    {
        if (string.IsNullOrEmpty(id)) return "";
        var s = id.Contains('.') ? id[(id.IndexOf('.') + 1)..] : id;
        return string.Join(" ", s.Split('_', StringSplitOptions.RemoveEmptyEntries).Select(w => w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));
    }

    private static string Str(JsonElement e, string k) => e.TryGetProperty(k, out var v) ? v.ValueKind switch
    {
        JsonValueKind.String => v.GetString() ?? "",
        JsonValueKind.Number => v.ToString(),
        _ => "",
    } : "";

    private static long Long(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var x) ? x : 0;
    private static bool Bool(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.True;
}
