using Godot;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;

namespace Spirewise;

/// <summary>
/// Removal and upgrade screens (shop card removal, events, rest-site Smith): label each card in your deck with
/// how much removing / upgrading it helps compared with choosing a different card, and highlight the best one.
///
/// The deck grid creates and reuses card slots as you scroll, so instead of labelling once we watch the grid
/// every frame while the screen is open and keep each slot's label in sync with the card it currently shows.
/// </summary>
public static class DeckScreenPatch
{
    private static ulong _removalPendingUntil;

    /// <summary>Prefix on CardSelectCmd.FromDeckForRemoval: the next deck selection screen is a removal.</summary>
    public static void RemovalPrefix() => _removalPendingUntil = Time.GetTicksMsec() + 5000;

    /// <summary>Postfix on NDeckCardSelectScreen._Ready.</summary>
    public static void DeckSelectReady(object __instance)
    {
        bool removal = Time.GetTicksMsec() < _removalPendingUntil;
        _removalPendingUntil = 0;
        if (removal) Start(__instance, removal: true);
    }

    /// <summary>Postfix on NDeckUpgradeSelectScreen._Ready.</summary>
    public static void UpgradeReady(object __instance) => Start(__instance, removal: false);

    /// <summary>Postfix on NDeckTransformSelectScreen._Ready.</summary>
    public static void TransformReady(object __instance) => Start(__instance, removal: false, transform: true);

    private static void Start(object instance, bool removal, bool transform = false)
    {
        Settings.StartHotkey();
        if (!Settings.ShowRemoveUpgrade) return;
        try { new Tracker((Control)instance, removal, transform).Begin(); }
        catch (Exception e) { Plugin.LogError($"{(removal ? "removal" : "upgrade")} labels failed: {e.GetType().Name}: {e.Message}"); }
    }

    private sealed class Tracker
    {
        private readonly Control _screen;
        private readonly bool _removal, _transform;
        private readonly Dictionary<string, double?> _scores = new();
        private readonly HashSet<string> _blocked = new();
        private readonly Dictionary<string, double> _upgradeValue = new();
        private NGridCardHolder? _highlight;
        private double _best = double.NaN;
        private SceneTree? _tree;
        private int _errors;

        public Tracker(Control screen, bool removal, bool transform) { _screen = screen; _removal = removal; _transform = transform; }
        private string Verb => _removal ? "remove" : _transform ? "transform" : "upgrade";

        public void Begin()
        {
            var ctx = RunContext.Current();
            if (ctx == null) return;
            if (_transform)
            {
                var ex = ExtraStore.Get(ctx.DataVersion, ctx.CharKey);
                if (ex == null) return;
                foreach (var id in ctx.DeckSet)
                    _scores[id] = ExtraStore.Transform(ex.Transform.GetValueOrDefault(id), ctx.DeckIds.Count, ctx.Floor);
            }
            else
            {
                var data = DecisionStore.Get(ctx.DataVersion, ctx.CharKey);
                if (data == null) { Plugin.Log($"no remove/upgrade data for {ctx.CharKey} ({ctx.DataVersion})"); return; }
                var models = _removal ? data.Removal : data.Upgrade;
                foreach (var id in ctx.DeckSet)
                    _scores[id] = DecisionStore.Evaluate(models.GetValueOrDefault(id), ctx.Context, ctx.DeckIds.Count, ctx.Floor, _removal);
                if (_removal)
                    foreach (var id in ctx.DeckSet)
                        if (DecisionStore.Evaluate(data.Upgrade.GetValueOrDefault(id), ctx.Context, ctx.DeckIds.Count, ctx.Floor, false) is double up)
                            _upgradeValue[id] = up;
                // Upgrading a draw card mostly adds draw, which Fiddle blocks: don't score or recommend it.
                if (!_removal) foreach (var id in ctx.DeckSet.Where(id => DrawBlock.Blocked(ctx, id)).ToList()) { _scores[id] = null; _blocked.Add(id); }
            }
            var known = _scores.Values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
            _best = known.Count > 0 ? known.Max() : double.NaN;
            Plugin.Log($"{Verb} screen: {ctx.CharKey} floor {ctx.Floor}, " +
                       string.Join(", ", _scores.Where(kv => kv.Value.HasValue).OrderByDescending(kv => kv.Value).Take(4)
                           .Select(kv => $"{kv.Key} {kv.Value!.Value * 100:+0.0;-0.0}%")));
            _tree = _screen.GetTree();
            _tree.ProcessFrame += Tick;
        }

        private void Stop() { if (_tree != null) _tree.ProcessFrame -= Tick; _tree = null; }

        private void Tick()
        {
            try
            {
                if (!GodotObject.IsInstanceValid(_screen) || !_screen.IsInsideTree()) { Stop(); return; }
                var grid = AccessTools.Field(typeof(NCardGridSelectionScreen), "_grid")?.GetValue(_screen) as NCardGrid;
                if (grid == null) return;
                var holders = grid.CurrentlyDisplayedCardHolders.ToList();
                // Score each holder, not each card id: removing an upgraded copy also throws away its upgrade.
                double? Score(NGridCardHolder h)
                {
                    var cid = h.CardModel?.Id.ToString();
                    if (cid == null || !_scores.TryGetValue(cid, out var sv) || sv == null) return null;
                    if (_removal && h.CardModel!.IsUpgraded && _upgradeValue.TryGetValue(cid, out var up) && up > 0) return sv.Value - up;
                    return sv.Value;
                }
                // Gold border on one card only: the first copy with the best score (identical copies tie).
                if (_highlight == null || !GodotObject.IsInstanceValid(_highlight))
                {
                    double best = double.NegativeInfinity;
                    foreach (var h in holders) if (Score(h) is double x && x > best + 1e-9) { best = x; _highlight = h; }
                    if (best <= 0) _highlight = null;
                }
                foreach (var holder in holders)
                {
                    var id = holder.CardModel?.Id.ToString();
                    var existing = holder.GetNodeOrNull<Control>(Badges.NodeName);
                    string tag = id + (holder.CardModel?.IsUpgraded == true ? "+" : "") + (holder == _highlight ? "*" : "");
                    if (existing != null && existing.HasMeta("sw_id") && (string)existing.GetMeta("sw_id") == tag) continue;
                    if (id == null) { existing?.QueueFree(); continue; }
                    Control badge;
                    if (_blocked.Contains(id))
                        badge = Badges.Attach(holder, "Fiddle blocks draw", Badges.Muted, highlight: false, y: 150, fontSize: 20, width: 190, scope: _screen, cardId: id);
                    else if (Score(holder) is double v)
                        badge = Badges.Attach(holder, $"{Verb} {Badges.Pct(v)}", Badges.Tone(v), highlight: holder == _highlight, y: 150, fontSize: 22, width: 190,
                            scope: _screen, cardId: id);
                    else
                        badge = Badges.Attach(holder, "no data", Badges.Muted, highlight: false, y: 150, fontSize: 20, width: 190, scope: _screen, cardId: id);
                    badge.SetMeta("sw_id", tag);
                }
            }
            catch (Exception e)
            {
                if (++_errors == 1) Plugin.LogError($"deck screen labels: {e.GetType().Name}: {e.Message}");
                if (_errors > 30) Stop();    // give up quietly rather than spam every frame
            }
        }
    }
}
