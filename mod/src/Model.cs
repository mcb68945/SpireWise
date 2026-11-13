using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spirewise;

/// <summary>Per-card logistic model, same format and math as the website (data/&lt;version&gt;/&lt;character&gt;.json).</summary>
public sealed class CardModelData
{
    [JsonPropertyName("a")] public double A { get; set; }
    [JsonPropertyName("beta")] public double Beta { get; set; }
    [JsonPropertyName("k")] public double K { get; set; }
    [JsonPropertyName("bucket")] public double[] Bucket { get; set; } = new double[6];
    [JsonPropertyName("delta")] public Dictionary<string, double> Delta { get; set; } = new();
    [JsonPropertyName("gamma")] public Dictionary<string, double> Gamma { get; set; } = new();
    [JsonPropertyName("value")] public double Value { get; set; }
    [JsonPropertyName("offers")] public int Offers { get; set; }
    /// <summary>Extra pick value by act: [act 1 = 0, added from act 2, added again from act 3] (floor buckets 2+ and 4+).</summary>
    [JsonPropertyName("beta_act")] public double[]? BetaAct { get; set; }
    // co-op only: team size [2p,3p,4p] and teammates' characters, and how they change the pick's value
    [JsonPropertyName("np")] public double[]? Np { get; set; }
    [JsonPropertyName("mate")] public Dictionary<string, double>? Mate { get; set; }
    [JsonPropertyName("pnp")] public double[]? Pnp { get; set; }
    [JsonPropertyName("pmate")] public Dictionary<string, double>? Pmate { get; set; }
}

public sealed class SimpleCardData
{
    [JsonPropertyName("value")] public double Value { get; set; }
    [JsonPropertyName("se")] public double Se { get; set; }
    [JsonPropertyName("picks")] public int Picks { get; set; }
}

public sealed class CharacterData
{
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("runs")] public int Runs { get; set; }
    [JsonPropertyName("models")] public Dictionary<string, CardModelData> Models { get; set; } = new();
    [JsonPropertyName("simple")] public Dictionary<string, SimpleCardData> Simple { get; set; } = new();
    [JsonPropertyName("mode")] public string? Mode { get; set; }
}

public readonly record struct Score(double Gain, double Synergy, bool LowData, bool Known);

public static class ModelStore
{
    private static readonly Dictionary<string, CharacterData?> Cache = new();
    private static string[]? _versions;

    /// <summary>Data versions embedded in the DLL, e.g. "v0.107.1".</summary>
    public static string[] Versions => _versions ??= Assembly.GetExecutingAssembly().GetManifestResourceNames()
        .Select(n => n.Replace('\\', '/').Split('/')[0]).Where(v => v.StartsWith("v")).Distinct()
        .OrderBy(v => ParseVersion(v)).ToArray();

    /// <summary>Exact match for the game version if we have it, otherwise the newest data not newer than the game, otherwise the newest.</summary>
    public static string PickVersion(string? gameVersion)
    {
        var vs = Versions;
        if (gameVersion != null && vs.Contains(gameVersion)) return gameVersion;
        var g = ParseVersion(gameVersion);
        var older = vs.Where(v => ParseVersion(v).CompareTo(g) <= 0).ToArray();
        return older.Length > 0 ? older[^1] : vs[^1];
    }

    public static CharacterData? Get(string version, string character)
    {
        var key = $"{version}/{character}";
        if (Cache.TryGetValue(key, out var d)) return d;
        d = null;
        var asm = Assembly.GetExecutingAssembly();
        var res = asm.GetManifestResourceNames().FirstOrDefault(n => n.Replace('\\', '/') == $"{key}.json.gz");
        if (res != null)
        {
            using var s = asm.GetManifestResourceStream(res)!;
            using var gz = new GZipStream(s, CompressionMode.Decompress);
            d = JsonSerializer.Deserialize<CharacterData>(gz);
        }
        Cache[key] = d;
        return d;
    }

    private static Version ParseVersion(string? v)
    {
        if (v != null && System.Version.TryParse(v.TrimStart('v').Split('-')[0], out var p)) return p;
        return new Version(0, 0);
    }

    private static double Sig(double z) => 1 / (1 + Math.Exp(-z));

    /// <summary>
    /// What picking a typical card would do right now: the offer-weighted average gain over every modeled card,
    /// for this deck, relics, floor and team. Card labels are shown relative to this. In A10 data, stronger players
    /// skip more and win more, which pushes every card's raw number down by a similar amount; comparing with a typical
    /// card cancels that shared shift and keeps what matters for the choice in front of you.
    /// </summary>
    public static double Typical(CharacterData data, ICollection<string> context, int deckSize, int floor, IReadOnlyList<string>? teammates = null)
    {
        double sum = 0, w = 0;
        foreach (var (id, m) in data.Models)
        {
            if (m.Offers <= 0) continue;
            sum += m.Offers * Evaluate(data, id, context, deckSize, floor, teammates).Gain;
            w += m.Offers;
        }
        return w > 0 ? sum / w : 0;
    }

    /// <summary>Change in win probability from picking <paramref name="card"/> given the deck (card ids), floor and deck size.</summary>
    public static Score Evaluate(CharacterData data, string card, ICollection<string> deckSet, int deckSize, int floor,
                                 IReadOnlyList<string>? teammates = null)
    {
        if (data.Models.TryGetValue(card, out var m))
        {
            int b = Math.Min((Math.Max(1, floor) - 1) / 9, 5);
            double z0 = m.A + m.Bucket[b] + m.K * deckSize / 10.0, g = 0;
            foreach (var p in deckSet)
            {
                if (m.Delta.TryGetValue(p, out var dv)) z0 += dv;
                if (m.Gamma.TryGetValue(p, out var gv)) g += gv;
            }
            if (m.Np != null && m.Pnp != null && teammates != null)
            {
                int np = Math.Clamp(teammates.Count + 1, 2, 4) - 2;
                z0 += m.Np[np]; g += m.Pnp[np];
                foreach (var t in teammates)
                {
                    if (m.Mate != null && m.Mate.TryGetValue(t, out var mv)) z0 += mv;
                    if (m.Pmate != null && m.Pmate.TryGetValue(t, out var pv)) g += pv;
                }
            }
            double beta = m.Beta;
            if (m.BetaAct is { Length: >= 3 }) beta += (b >= 2 ? m.BetaAct[1] : 0) + (b >= 4 ? m.BetaAct[2] : 0);
            double baseGain = Sig(z0 + beta) - Sig(z0);
            double gain = Sig(z0 + beta + g) - Sig(z0);
            return new Score(gain, gain - baseGain, false, true);
        }
        if (data.Simple.TryGetValue(card, out var s))
            return new Score(s.Value, 0, true, s.Picks >= 50 && s.Se <= 0.05);
        return new Score(0, 0, true, false);
    }
}

// ---------------- remove / upgrade decisions ----------------

public sealed class DecisionModel
{
    [JsonPropertyName("a")] public double A { get; set; }
    [JsonPropertyName("beta")] public double Beta { get; set; }
    [JsonPropertyName("k")] public double K { get; set; }
    [JsonPropertyName("kn")] public double Kn { get; set; }
    [JsonPropertyName("shop")] public double Shop { get; set; }
    [JsonPropertyName("ancient")] public double Ancient { get; set; }
    [JsonPropertyName("bucket")] public double[] Bucket { get; set; } = new double[6];
    [JsonPropertyName("delta")] public Dictionary<string, double> Delta { get; set; } = new();
    /// <summary>chosen x relic: how a held relic changes the value of removing/upgrading this card.</summary>
    [JsonPropertyName("gamma")] public Dictionary<string, double> Gamma { get; set; } = new();
}

public sealed class DecisionData
{
    [JsonPropertyName("removal")] public Dictionary<string, DecisionModel> Removal { get; set; } = new();
    [JsonPropertyName("upgrade")] public Dictionary<string, DecisionModel> Upgrade { get; set; } = new();
    [JsonPropertyName("doors")] public DoorsModel? Doors { get; set; }
}

/// <summary>Doors of Light and Dark: remove 1 card (Dark) vs upgrade 2 random cards (Light, game v0.111 rules).</summary>
public sealed class DoorsModel
{
    [JsonPropertyName("n")] public int N { get; set; }
    [JsonPropertyName("a")] public double A { get; set; }
    [JsonPropertyName("k")] public double K { get; set; }
    [JsonPropertyName("kb")] public double Kb { get; set; }
    [JsonPropertyName("r")] public double R { get; set; }
    [JsonPropertyName("r_n")] public double RN { get; set; }
    [JsonPropertyName("r_b")] public double RB { get; set; }
    [JsonPropertyName("r_f")] public double RF { get; set; }
    [JsonPropertyName("bucket")] public double[] Bucket { get; set; } = new double[6];

    /// <summary>Win-chance gain from removing (Dark) instead of upgrading (Light). Same formula as the site.</summary>
    public double RemoveMinusUpgrade(int deckSize, int basics, int floor)
    {
        int b = Math.Min((Math.Max(1, floor) - 1) / 9, 5);
        double z = A + Bucket[b] + K * deckSize / 10.0 + Kb * basics;
        double diff = R + RN * deckSize / 10.0 + RB * basics + RF * floor / 10.0;
        return 1 / (1 + Math.Exp(-(z + diff))) - 1 / (1 + Math.Exp(-z));
    }
}

public static class DecisionStore
{
    private static readonly Dictionary<string, DecisionData?> Cache = new();

    public static DecisionData? Get(string version, string character)
    {
        var key = $"{version}/{character}";
        if (Cache.TryGetValue(key, out var d)) return d;
        d = null;
        var asm = System.Reflection.Assembly.GetExecutingAssembly();
        var res = asm.GetManifestResourceNames().FirstOrDefault(n => n.Replace('\\', '/') == $"{key}.decisions.json.gz");
        if (res != null)
        {
            using var s = asm.GetManifestResourceStream(res)!;
            using var gz = new System.IO.Compression.GZipStream(s, System.IO.Compression.CompressionMode.Decompress);
            d = JsonSerializer.Deserialize<DecisionData>(gz);
        }
        Cache[key] = d;
        return d;
    }

    /// <summary>Change in win chance from removing/upgrading this card instead of another card in the deck.
    /// Removal is scored as a one-card shop removal (the most common case).</summary>
    public static double? Evaluate(DecisionModel? m, ICollection<string> deckSet, int deckSize, int floor, bool removal)
    {
        if (m == null) return null;
        int b = Math.Min((Math.Max(1, floor) - 1) / 9, 5);
        double z0 = m.A + m.Bucket[b] + m.K * deckSize / 10.0 + (removal ? m.Kn + m.Shop : 0);
        double g = 0;
        foreach (var p in deckSet)
        {
            if (m.Delta.TryGetValue(p, out var v)) z0 += v;
            if (m.Gamma.TryGetValue(p, out var gv)) g += gv;
        }
        static double Sig(double z) => 1 / (1 + Math.Exp(-z));
        return Sig(z0 + m.Beta + g) - Sig(z0);
    }
}

// ---------------- shop relics / ancients, transforms, rest sites, win chance ----------------

public sealed class WinModel
{
    [JsonPropertyName("a")] public double A { get; set; }
    [JsonPropertyName("hp")] public double Hp { get; set; }
    [JsonPropertyName("mhp")] public double Mhp { get; set; }
    [JsonPropertyName("gold")] public double Gold { get; set; }
    [JsonPropertyName("relics")] public double Relics { get; set; }
    [JsonPropertyName("k")] public double K { get; set; }
    [JsonPropertyName("fl")] public double Fl { get; set; }
    [JsonPropertyName("bucket")] public double[] Bucket { get; set; } = new double[6];
    [JsonPropertyName("deck")] public Dictionary<string, double> Deck { get; set; } = new();
}

public sealed class RestTable
{
    /// <summary>[act 0..2][hp band 0..9]: share of WINNING runs that chose Smith over Rest.</summary>
    [JsonPropertyName("winners_smith")] public double?[][] WinnersSmith { get; set; } = Array.Empty<double?[]>();
    [JsonPropertyName("n")] public int[][] N { get; set; } = Array.Empty<int[]>();
}

public sealed class ExtraData
{
    [JsonPropertyName("relic")] public Dictionary<string, DecisionModel> Relic { get; set; } = new();
    [JsonPropertyName("transform")] public Dictionary<string, DecisionModel> Transform { get; set; } = new();
    [JsonPropertyName("rest")] public RestTable? Rest { get; set; }
    [JsonPropertyName("win")] public WinModel? Win { get; set; }
}

public static class ExtraStore
{
    private static readonly Dictionary<string, ExtraData?> Cache = new();
    private static double Sig(double z) => 1 / (1 + Math.Exp(-z));

    public static ExtraData? Get(string version, string character)
    {
        var key = $"{version}/{character}";
        if (Cache.TryGetValue(key, out var d)) return d;
        d = null;
        var asm = System.Reflection.Assembly.GetExecutingAssembly();
        var res = asm.GetManifestResourceNames().FirstOrDefault(n => n.Replace('\\', '/') == $"{key}.extra.json.gz");
        if (res != null)
        {
            using var s = asm.GetManifestResourceStream(res)!;
            using var gz = new System.IO.Compression.GZipStream(s, System.IO.Compression.CompressionMode.Decompress);
            d = JsonSerializer.Deserialize<ExtraData>(gz);
        }
        Cache[key] = d;
        return d;
    }

    /// <summary>Picked-vs-passed value of a relic offer (shop relic or ancient option).</summary>
    public static double? Relic(DecisionModel? m, int deckSize, int floor, bool shop, bool ancient, int offered)
    {
        if (m == null) return null;
        int b = Math.Min((Math.Max(1, floor) - 1) / 9, 5);
        double z0 = m.A + m.Bucket[b] + m.K * deckSize / 10.0 + (shop ? m.Shop : 0) + (ancient ? m.Ancient : 0) + m.Kn * offered;
        return Sig(z0 + m.Beta) - Sig(z0);
    }

    /// <summary>Transforming this card instead of another (one card, event screen).</summary>
    public static double? Transform(DecisionModel? m, int deckSize, int floor)
    {
        if (m == null) return null;
        int b = Math.Min((Math.Max(1, floor) - 1) / 9, 5);
        double z0 = m.A + m.Bucket[b] + m.K * deckSize / 10.0 + m.Kn;
        return Sig(z0 + m.Beta) - Sig(z0);
    }

    /// <summary>Share of winning A10 runs that chose Smith at this HP and act, with sample size.</summary>
    public static (double? share, int n) WinnersSmith(RestTable? t, int hp, int maxHp, int floor)
    {
        if (t == null || maxHp <= 0 || t.WinnersSmith.Length < 3) return (null, 0);
        int act = floor <= 17 ? 0 : floor <= 34 ? 1 : 2;
        int hb = Math.Clamp(hp * 10 / maxHp, 0, 9);
        return (t.WinnersSmith[act][hb], t.N[act][hb]);
    }

    /// <summary>Estimated chance this run wins, from deck, HP, gold, relics and floor.</summary>
    public static double? WinChance(WinModel? w, ICollection<string> deckSet, int deckSize, int hp, int maxHp, int gold, int relics, int floor)
    {
        if (w == null || maxHp <= 0) return null;
        int b = Math.Min((Math.Max(1, floor) - 1) / 9, 5);
        double z = w.A + w.Hp * Math.Clamp((double)hp / maxHp, 0, 1) + w.Mhp * maxHp / 100.0 + w.Gold * gold / 100.0 + w.Relics * relics
                   + w.K * deckSize / 10.0 + w.Fl * floor / 10.0 + w.Bucket[b];
        foreach (var c in deckSet) if (w.Deck.TryGetValue(c, out var v)) z += v;
        return Sig(z);
    }
}
