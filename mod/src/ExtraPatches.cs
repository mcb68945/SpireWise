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
        Badges.Attach(slot, Badges.Pct(s.Gain) + " win" + (s.LowData ? " (low data)" : ""), s.Gain >= 0 ? Badges.Good : Badges.Bad,
            highlight: false, y: 120, fontSize: 20, width: 170);
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
        var badge = Badges.Attach(slot, Badges.Pct(v.Value) + " win", v.Value >= 0 ? Badges.Good : Badges.Bad, highlight: false, y: 70, fontSize: 20, width: 150);
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
        catch (Exception e) { Plugin.LogError($"shop relic label placement failed: {e.Message}"); }
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
            var badge = Badges.Attach(b, Badges.Pct(v.Value) + " win", v.Value >= 0 ? Badges.Good : Badges.Bad,
                highlight: v.Value == best && best > 0, y: 0, fontSize: 20, width: 140);
            var target = b;
            Callable.From(() => { if (GodotObject.IsInstanceValid(badge)) badge.Position = new Vector2(-150, Math.Max(0, target.Size.Y / 2 - 18)); }).CallDeferred();
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
        map.GetNodeOrNull(WinLabelName)?.QueueFree();
        var label = new Label { Name = WinLabelName, MouseFilter = Control.MouseFilterEnum.Ignore, ZIndex = 20, Visible = Settings.Current.Enabled,
            Text = $"SpireWise win chance: {p.Value * 100:0}%" + (ctx.Ascension != 10 ? $"  (A10 model, you're on A{ctx.Ascension})" : "") };
        label.AddThemeFontSizeOverride("font_size", 26);
        label.AddThemeColorOverride("font_color", Badges.Gold);
        label.AddThemeConstantOverride("outline_size", 7);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        map.AddChild(label);
        label.Position = new Vector2(40, 130);
        Badges.Track(label);
        Plugin.Log($"win chance {p.Value * 100:0.0}% (floor {ctx.Floor}, {ctx.Hp}/{ctx.MaxHp} HP, {ctx.Gold} gold, {ctx.Relics} relics, deck {ctx.DeckIds.Count})");
    }
}
