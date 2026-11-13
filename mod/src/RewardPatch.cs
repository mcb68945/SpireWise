using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Debug;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Runs;

namespace Spirewise;

/// <summary>Shared run context: who you are, your deck, floor, teammates, which data version to use.</summary>
public sealed class RunContext
{
    public string CharKey = "";
    public string DataVersion = "";
    public string? GameVersion;
    public List<string> DeckIds = new();
    public HashSet<string> DeckSet = new();
    public int Floor;
    public int Ascension;
    public int Players;
    public List<string>? Mates;
    public int Hp, MaxHp, Gold, Relics;

    private static string? _lastMode;

    /// <summary>
    /// True for an ordinary run. Read by name so it works on game versions with and without the
    /// v0.113 modes (GameMode: Standard, Daily, Dread, Blitz; RunState.IsCustomGame).
    /// </summary>
    public static bool IsStandardRun(object state)
    {
        string mode = "Standard";
        bool custom = false;
        try
        {
            var t = state.GetType();
            mode = t.GetProperty("GameMode")?.GetValue(state)?.ToString() ?? "Standard";
            custom = t.GetProperty("IsCustomGame")?.GetValue(state) is true;
        }
        catch { /* unknown shape: assume standard */ }
        bool ok = mode == "Standard" && !custom;
        string key = mode + (custom ? "/custom" : "");
        if (key != _lastMode)
        {
            _lastMode = key;
            if (!ok) Plugin.Log($"{mode}{(custom ? " (custom)" : "")} run: SpireWise's data only covers standard A10 runs, so labels are off for this run");
        }
        return ok;
    }

    public static string CharKeyOf(string id) => (id.Contains('.') ? id[(id.IndexOf('.') + 1)..] : id).ToLowerInvariant();

    public static RunContext? Current()
    {
        var state = RunManager.Instance.DebugOnlyGetState();
        if (state == null) return null;
        if (!IsStandardRun(state)) return null;   // Blitz, Dread, Daily, custom: our data is standard A10 runs only
        var me = LocalContext.GetMe(state.Players);
        if (me == null) return null;
        var ctx = new RunContext
        {
            CharKey = CharKeyOf(me.Character.Id.ToString()),
            GameVersion = ReleaseInfoManager.Instance.ReleaseInfo?.Version,
            Floor = state.TotalFloor,
            Ascension = state.AscensionLevel,
            Players = state.Players.Count,
        };
        ctx.DataVersion = ModelStore.PickVersion(ctx.GameVersion);
        ctx.DeckIds = me.Deck.Cards.Select(c => c.Id.ToString()).ToList();
        try { ctx.Hp = me.Creature.CurrentHp; ctx.MaxHp = me.Creature.MaxHp; ctx.Gold = me.Gold; ctx.Relics = me.Relics.Count; } catch { /* optional fields */ }
        ctx.DeckSet = new HashSet<string>(ctx.DeckIds);
        if (ctx.Players is >= 2 and <= 4)
            ctx.Mates = state.Players.Where(p => !LocalContext.IsMe(p)).Select(p => CharKeyOf(p.Character.Id.ToString())).ToList();
        return ctx;
    }
}

/// <summary>Card value for the current deck (solo or co-op), shared by the reward screen and the shop.</summary>
public static class CardScorer
{
    public static Score Score(RunContext ctx, string id)
    {
        var solo = ModelStore.Get(ctx.DataVersion, ctx.CharKey);
        if (solo == null) return default;
        var coopData = ctx.Mates != null ? ModelStore.Get(ctx.DataVersion, ctx.CharKey + ".coop") : null;
        if (coopData != null && coopData.Models.ContainsKey(id))
            return ModelStore.Evaluate(coopData, id, ctx.DeckSet, ctx.DeckIds.Count, ctx.Floor, ctx.Mates);
        var s = ModelStore.Evaluate(solo, id, ctx.DeckSet, ctx.DeckIds.Count, ctx.Floor);
        return coopData != null && s.Known ? s with { LowData = true } : s;
    }
}

/// <summary>
/// Card reward screen: label each offered card with its win-rate gain for the current deck,
/// highlight the best one, and say when skipping is better. Read-only.
/// Hooked by name in Plugin (postfix on NCardRewardSelectionScreen.RefreshOptions).
/// </summary>
public static class RewardPatch
{
    private const string BannerName = "SpireWise_Banner";

    public static void Postfix(object __instance)
    {
        Settings.StartHotkey();
        if (!Settings.ShowRewards) return;
        try { Annotate(__instance); }
        catch (Exception e) { Plugin.LogError($"reward labels failed: {e.GetType().Name}: {e.Message}"); }
    }

    private static void Annotate(object instance)
    {
        var screen = (NCardRewardSelectionScreen)instance;
        var ctx = RunContext.Current();
        if (ctx == null) return;
        var solo = ModelStore.Get(ctx.DataVersion, ctx.CharKey);
        if (solo == null) { Plugin.Log($"no data for {ctx.CharKey} ({ctx.DataVersion})"); return; }
        bool coop = ctx.Mates != null;
        var coopData = coop ? ModelStore.Get(ctx.DataVersion, ctx.CharKey + ".coop") : null;

        Score Eval(string id)
        {
            if (coopData != null && coopData.Models.ContainsKey(id))
                return ModelStore.Evaluate(coopData, id, ctx.DeckSet, ctx.DeckIds.Count, ctx.Floor, ctx.Mates);
            var s = ModelStore.Evaluate(solo, id, ctx.DeckSet, ctx.DeckIds.Count, ctx.Floor);
            return coopData != null && s.Known ? s with { LowData = true } : s;
        }

        var rowField = AccessTools.Field(typeof(NCardRewardSelectionScreen), "_cardRow");
        if (rowField?.GetValue(screen) is not Control row) { Plugin.Log("card row not found (game changed?)"); return; }
        var scored = row.GetChildren().OfType<NGridCardHolder>().Where(h => !h.IsQueuedForDeletion() && h.CardModel != null)
            .Select(h => (holder: h, id: h.CardModel!.Id.ToString(), score: Eval(h.CardModel!.Id.ToString()))).ToList();
        var known = scored.Where(s => s.score.Known).ToList();
        double best = known.Count > 0 ? known.Max(s => s.score.Gain) : double.NaN;

        foreach (var (holder, cardId, s) in scored)
        {
            string text = !s.Known ? "no data" : Badges.Pct(s.Gain) + " win";
            string? detail = !s.Known ? null : s.LowData ? "low data"
                : Math.Abs(s.Synergy) >= 0.003 ? Badges.Pct(s.Synergy) + (coop ? " from team+deck" : " from your deck") : null;
            Badges.Attach(holder, text, !s.Known ? Badges.Muted : Badges.Tone(s.Gain),
                detail: detail, detailColor: s.LowData ? Badges.Muted : Badges.Tone(s.Synergy),
                highlight: s.Known && known.Count > 1 && s.Gain == best, y: 235, width: 230, scope: screen, cardId: cardId);
        }

        var notes = new List<string> { $"SpireWise · A10 data · {ctx.DataVersion}" };
        if (ctx.GameVersion != null && ctx.GameVersion != ctx.DataVersion) notes.Add($"(game {ctx.GameVersion})");
        if (ctx.Ascension != 10) notes.Add($"· you're on A{ctx.Ascension}");
        if (coop && coopData != null) notes.Add($"· co-op model ({ctx.Players}p with {string.Join(", ", ctx.Mates!)})");
        else if (ctx.Players > 1) notes.Add("· solo model (no co-op data for this team size)");
        notes.Add("· F7 hides");
        // No "skip" advice for now: in A10 data, stronger players skip more and also win more, which pushes every
        // card's number down. Until the model controls for player skill, trust the ranking more than the sign.
        string headline = "";
        AttachBanner(screen, headline, string.Join(" ", notes));

        Plugin.Log($"{ctx.CharKey} floor {ctx.Floor} deck {ctx.DeckIds.Count}: " +
                   string.Join(", ", scored.Select(s => $"{s.id} {s.score.Gain * 100:+0.0;-0.0}%")));
    }

    private static void AttachBanner(Control screen, string headline, string note)
    {
        screen.GetNodeOrNull(BannerName)?.QueueFree();
        var label = new Label
        {
            Name = BannerName, HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore,
            Text = string.IsNullOrEmpty(headline) ? note : headline + "\n" + note, Visible = Settings.Current.Enabled,
        };
        label.AddThemeFontSizeOverride("font_size", 22);
        label.AddThemeColorOverride("font_color", string.IsNullOrEmpty(headline) ? Badges.Muted : Badges.Gold);
        label.AddThemeConstantOverride("outline_size", 6);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        screen.AddChild(label);
        var vp = screen.GetViewportRect().Size;
        label.Size = new Vector2(1000, 60);
        label.Position = new Vector2(vp.X / 2 - 500, vp.Y * 0.905f);   // below the Skip button, not on it
    }
}
