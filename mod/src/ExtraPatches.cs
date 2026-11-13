using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;

namespace Spirewise;

/// <summary>
/// Shop cards and relics, rest-site buttons, Ancient options, and the map's win-chance readout.
/// Each entry point is hooked by name in Plugin and wraps its work in try/catch, so a game update that
/// changes one screen only turns off that one label.
/// </summary>
public static class ExtraPatches
{
    private static void Safe(string what, Action a)
    {
        try { a(); }
        catch (Exception e) { Plugin.LogError($"{what} labels failed: {e.GetType().Name}: {e.Message}"); }
    }

    // ---------------- #1 shop: cards and relics ----------------

    public static void ShopCardPostfix(object __instance)
    {
        Settings.StartHotkey();
        if (Settings.ShowShop) Safe("shop card", () => ShopCard(__instance));
    }

    private static void ShopCard(object instance)
    {
        var slot = (NMerchantCard)instance;
        if (AccessTools.Field(typeof(NMerchantCard), "_cardEntry")?.GetValue(slot) is not MerchantCardEntry entry) return;
        var card = entry.CreationResult?.Card;
        var ctx = RunContext.Current();
        if (card == null || ctx == null) return;
        var s = CardScorer.Score(ctx, card.Id.ToString());
        if (!s.Known) { slot.GetNodeOrNull(Badges.NodeName)?.QueueFree(); return; }
        var badge = Badges.Attach(slot, Badges.Pct(s.Gain) + " win" + (s.LowData ? " (low data)" : ""), Badges.Tone(s.Gain),
            highlight: false, y: 120, fontSize: 20, width: 170);
        // The fixed offset sat on the card text. Move it just below the price, like relics.
        Callable.From(() => PlaceBelowCost(slot, badge)).CallDeferred();
    }

    public static void ShopRelicPostfix(object __instance)
    {
        Settings.StartHotkey();
        if (Settings.ShowShop) Safe("shop relic", () => ShopRelic(__instance));
    }

    private static void ShopRelic(object instance)
    {
        var slot = (NMerchantRelic)instance;
        if (AccessTools.Field(typeof(NMerchantRelic), "_relicEntry")?.GetValue(slot) is not MerchantRelicEntry entry || entry.Model == null) return;
        var ctx = RunContext.Current();
        var ex = ctx == null ? null : ExtraStore.Get(ctx.DataVersion, ctx.CharKey);
        if (ctx == null || ex == null) return;
        var v = ExtraStore.Relic(ex.Relic.GetValueOrDefault(entry.Model.Id.ToString()), ctx.DeckIds.Count, ctx.Floor, shop: true, ancient: false, offered: 3);
        if (v == null) { slot.GetNodeOrNull(Badges.NodeName)?.QueueFree(); return; }
        var badge = Badges.Attach(slot, Badges.Pct(v.Value) + " win", Badges.Tone(v.Value), highlight: false, y: 70, fontSize: 20, width: 150);
        // A fixed offset landed on the price tag. Once the shop has laid out, move the label just below the price.
        Callable.From(() => PlaceBelowCost(slot, badge)).CallDeferred();
    }

    private static readonly System.Reflection.FieldInfo? CostLabelField = AccessTools.Field(typeof(NMerchantSlot), "_costLabel");

    private static void PlaceBelowCost(Control slot, Control badge)
    {
        try
        {
            if (!GodotObject.IsInstanceValid(slot) || !GodotObject.IsInstanceValid(badge)) return;
            if (CostLabelField?.GetValue(slot) is not Control cost || !GodotObject.IsInstanceValid(cost)) return;
            var price = cost.GetGlobalRect();
            var size = badge.GetGlobalRect().Size;
            badge.GlobalPosition = new Vector2(price.GetCenter().X - size.X / 2, price.End.Y + 6);
        }
        catch (Exception e) { Plugin.LogError($"shop label placement failed: {e.Message}"); }
    }

    // ---------------- #2 rest site: what winning players did ----------------

    public static void RestButtonPostfix(object __instance)
    {
        Settings.StartHotkey();
        if (Settings.ShowRestSite) Safe("rest site", () => RestButton(__instance));
    }

    private static void RestButton(object instance)
    {
        var button = (NRestSiteButton)instance;
        bool smith = button.Option is SmithRestSiteOption, heal = button.Option is HealRestSiteOption;
        if (!smith && !heal) return;
        var ctx = RunContext.Current();
        var ex = ctx == null ? null : ExtraStore.Get(ctx.DataVersion, ctx.CharKey);
        if (ctx == null || ex == null) return;
        var (share, n) = ExtraStore.WinnersSmith(ex.Rest, ctx.Hp, ctx.MaxHp, ctx.Floor);
        if (share == null || n < 30) return;
        double mine = smith ? share.Value : 1 - share.Value;
        Badges.Attach(button, $"winners {(smith ? "smithed" : "rested")} {mine * 100:0}%\nat {ctx.Hp * 100 / Math.Max(1, ctx.MaxHp)}% HP here",
            mine >= 0.5 ? Badges.Good : Badges.Muted, highlight: mine >= 0.5, y: 110, fontSize: 18, width: 210);
    }

    // ---------------- #4/#5 Ancient options (relic choices) ----------------

    public static void EventOptionPostfix(object __instance)
    {
        Settings.StartHotkey();
        if (Settings.ShowAncients) Safe("ancient", () => AncientOptions(__instance));
        if (Settings.ShowRemoveUpgrade) Safe("doors", () => DoorsOptions(__instance));
    }

    // Doors of Light and Dark: label Light (upgrade) and Dark (remove) with how they compare.
    private static void DoorsOptions(object instance)
    {
        var button = (NEventOptionButton)instance;
        string key = button.Option?.TextKey ?? "";
        if (!key.StartsWith("DOORS_OF_LIGHT_AND_DARK.") || button.GetParent() == null) return;
        var ctx = RunContext.Current();
        if (ctx == null) return;
        double g;   // > 0: Dark (remove) is better than Light (upgrade)
        double? dark = null, light = null;
        if (ctx.GameVersion == ctx.DataVersion)
        {
            // Same event as the data: v0.111's Light door upgrades 2 random cards, fitted directly.
            var d = DecisionStore.Get(ctx.DataVersion, ctx.CharKey)?.Doors;
            if (d == null) return;
            int basics = ctx.DeckIds.Count(id => id.StartsWith("CARD.STRIKE_") || id.StartsWith("CARD.DEFEND_"));
            g = d.RemoveMinusUpgrade(ctx.DeckIds.Count, basics, ctx.Floor);
        }
        else
        {
            // v0.113 changed Light to "pick 1 card to upgrade", so it is the same choice as the remove/upgrade
            // screens: compare your best removal with your best upgrade, each scored per card.
            var data = DecisionStore.Get(ctx.DataVersion, ctx.CharKey);
            if (data == null) return;
            double? Best(bool removal) => ctx.DeckSet
                .Select(id => DecisionStore.Evaluate((removal ? data.Removal : data.Upgrade).GetValueOrDefault(id), ctx.DeckSet, ctx.DeckIds.Count, ctx.Floor, removal))
                .Where(v => v.HasValue).Select(v => v!.Value).DefaultIfEmpty(double.NaN).Max() is var m && !double.IsNaN(m) ? m : null;
            dark = Best(true); light = Best(false);
            if (dark == null || light == null) return;
            g = dark.Value - light.Value;
        }
        foreach (var b in button.GetParent().GetChildren().OfType<NEventOptionButton>())
        {
            string k = b.Option?.TextKey ?? "";
            bool isDark = k.EndsWith(".DARK"), isLight = k.EndsWith(".LIGHT");
            if (!isDark && !isLight) continue;
            // v0.111: each door vs the other. v0.113: each door's own value (your best removal / best upgrade),
            // matching the numbers on the remove and upgrade screens; the better door gets the gold border.
            double v = dark.HasValue ? (isDark ? dark.Value : light!.Value) : (isDark ? g : -g);
            bool better = isDark ? g > 0 : g < 0;
            var badge = Badges.Attach(b, Badges.Pct(v), Badges.Tone(v),
                highlight: better && v > 0, y: 0, fontSize: 20, width: 96);
            var target = b;
            Callable.From(() => { if (GodotObject.IsInstanceValid(badge)) badge.Position = new Vector2(target.Size.X - badge.Size.X - 16, Math.Max(0, (target.Size.Y - badge.Size.Y) / 2)); }).CallDeferred();
        }
        Plugin.Log($"doors: {ctx.CharKey} floor {ctx.Floor}, deck {ctx.DeckIds.Count}: remove vs upgrade {g * 100:+0.0;-0.0}%" +
                   (dark.HasValue ? $" (best remove {dark.Value * 100:+0.0;-0.0}%, best upgrade {light!.Value * 100:+0.0;-0.0}%)" : ""));
    }

    private static void AncientOptions(object instance)
    {
        var button = (NEventOptionButton)instance;
        if (button.Event is not AncientEventModel || button.Option?.Relic == null || button.GetParent() == null) return;
        var ctx = RunContext.Current();
        var ex = ctx == null ? null : ExtraStore.Get(ctx.DataVersion, ctx.CharKey);
        if (ctx == null || ex == null) return;
        // Re-score every Ancient option on this screen each time one appears, so the best one can be highlighted.
        var options = button.GetParent().GetChildren().OfType<NEventOptionButton>().Where(b => b.Option?.Relic != null).ToList();
        var scores = options.Select(b => (b, v: ExtraStore.Relic(ex.Relic.GetValueOrDefault(b.Option!.Relic!.Id.ToString()),
            ctx.DeckIds.Count, ctx.Floor, shop: false, ancient: true, offered: options.Count))).ToList();
        double best = scores.Where(x => x.v.HasValue).Select(x => x.v!.Value).DefaultIfEmpty(double.NaN).Max();
        foreach (var (b, v) in scores)
        {
            if (v == null) { b.GetNodeOrNull(Badges.NodeName)?.QueueFree(); continue; }
            var badge = Badges.Attach(b, Badges.Pct(v.Value), Badges.Tone(v.Value),
                highlight: v.Value == best && best > 0, y: 0, fontSize: 20, width: 96);
            var target = b;
            Callable.From(() => { if (GodotObject.IsInstanceValid(badge)) badge.Position = new Vector2(target.Size.X - badge.Size.X - 16, Math.Max(0, (target.Size.Y - badge.Size.Y) / 2)); }).CallDeferred();
        }
    }

    // ---------------- #6 win chance on the map ----------------

    private const string WinLabelName = "SpireWise_WinChance";

    public static void MapOpenPostfix(object __instance)
    {
        Settings.StartHotkey();
        if (Settings.ShowWinChance) Safe("win chance", () => MapWinChance(__instance));
    }

    private static void MapWinChance(object instance)
    {
        var map = (NMapScreen)instance;
        var ctx = RunContext.Current();
        var ex = ctx == null ? null : ExtraStore.Get(ctx.DataVersion, ctx.CharKey);
        if (ctx == null || ex?.Win == null) return;
        var p = ExtraStore.WinChance(ex.Win, ctx.DeckSet, ctx.DeckIds.Count, ctx.Hp, ctx.MaxHp, ctx.Gold, ctx.Relics, ctx.Floor);
        if (p == null) return;
        // Clear every copy, including ones an older build left behind under a renamed node.
        foreach (var n in map.GetChildren().Where(c => c.Name.ToString().Contains(WinLabelName) || (c is Control ctl && ctl.HasMeta("sw_win"))).ToList())
        { map.RemoveChild(n); n.QueueFree(); }
        // A small badge in the empty margin left of the map, between the back button and the drawing tools,
        // instead of loose text over the relic bar.
        var badge = Badges.Attach(map, $"Win chance {p.Value * 100:0}%", Badges.Gold, highlight: false, y: 0, fontSize: 22, width: 190,
            detail: ctx.Ascension != 10 ? $"A10 model · you're on A{ctx.Ascension}" : "A10 model", detailColor: Badges.Muted);
        badge.Name = WinLabelName;
        badge.SetMeta("sw_win", true);
        var vp = map.GetViewportRect().Size;
        badge.Position = new Vector2(vp.X * 0.012f, vp.Y * 0.76f);
        Plugin.Log($"win chance {p.Value * 100:0.0}% (floor {ctx.Floor}, {ctx.Hp}/{ctx.MaxHp} HP, {ctx.Gold} gold, {ctx.Relics} relics, deck {ctx.DeckIds.Count})");
    }
}
