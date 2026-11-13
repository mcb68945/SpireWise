using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace Spirewise;

/// <summary>Main-menu picker for Rewind: recent solo runs and the floors you can go back to.</summary>
public sealed class RewindScreen
{
    private readonly CanvasLayer _layer;
    private readonly Tree _list;
    private readonly Label _status;
    private readonly Button _go;
    private readonly NMainMenu? _menu;
    private readonly List<Rewind.RunSnapshots> _runs;
    private TreeItem? _armed;
    private SceneTree? _tree;
    private bool _escWasDown = true;

    public static void Open(Node host)
    {
        try { _ = new RewindScreen(host); }
        catch (Exception e) { Plugin.LogError($"rewind screen failed to open: {e}"); }
    }

    private RewindScreen(Node host)
    {
        _menu = host as NMainMenu;
        _runs = Rewind.List();
        _layer = new CanvasLayer { Layer = 90, Name = "SpireWise_Rewind" };
        host.AddChild(_layer);
        var root = StatsScreen.FullRect(new Control { MouseFilter = Control.MouseFilterEnum.Stop });
        _layer.AddChild(root);
        root.AddChild(StatsScreen.FullRect(new ColorRect { Color = new Color(0, 0, 0, 0.6f), MouseFilter = Control.MouseFilterEnum.Ignore }));

        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", StatsScreen.Box(new Color(0.07f, 0.08f, 0.10f, 0.97f), Badges.Gold));
        panel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        panel.OffsetLeft = 260; panel.OffsetTop = 80; panel.OffsetRight = -260; panel.OffsetBottom = -80;
        root.AddChild(panel);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 10);
        panel.AddChild(col);

        var head = new HBoxContainer();
        head.AddChild(StatsScreen.Text("SpireWise · Rewind", 28, Badges.Gold));
        head.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        head.AddChild(StatsScreen.Btn("Close  (Esc)", Close));
        col.AddChild(head);

        var help = StatsScreen.Text(
            "Pick a point in a recent solo run, then press Continue on the main menu to play on from there. " +
            "\"Start\" is entering that floor; \"after\" is the reward screen after the fight. " +
            "A rewound run is saved to your history as a custom run, so it stays out of community stats sites and SpireWise's Run Stats.",
            17, Badges.Muted);
        help.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        col.AddChild(help);

        _list = new Tree { Columns = 3, HideRoot = true, ColumnTitlesVisible = true, SelectMode = Tree.SelectModeEnum.Row, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _list.AddThemeFontSizeOverride("font_size", 18);
        _list.AddThemeFontSizeOverride("title_button_font_size", 18);
        _list.SetColumnTitle(0, "Run / point"); _list.SetColumnTitle(1, "Room"); _list.SetColumnTitle(2, "Saved");
        _list.SetColumnExpandRatio(0, 3);
        _list.ItemSelected += () => { _armed = null; _go!.Text = "Rewind to selected point"; _status!.Text = ""; };
        col.AddChild(_list);

        var foot = new HBoxContainer();
        _status = StatsScreen.Text(_runs.Count == 0 ? "No snapshots yet. They're saved as you play solo runs with SpireWise on." : "", 18, Colors.White);
        _status.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        foot.AddChild(_status);
        _go = StatsScreen.Btn("Rewind to selected point", () => _ = Go());
        foot.AddChild(_go);
        col.AddChild(foot);

        Fill();
        _tree = host.GetTree();
        _tree.ProcessFrame += Poll;
    }

    private void Fill()
    {
        var root = _list.CreateItem();
        bool first = true;
        foreach (var run in _runs)
        {
            var r = _list.CreateItem(root);
            var when = DateTimeOffset.FromUnixTimeSeconds(run.StartTime).LocalDateTime;
            r.SetText(0, $"{RunHistoryStore.Pretty(run.Character)} · A{run.Ascension} · started {when:MMM d, HH:mm}" + (Rewind.Rewound.Contains(run.StartTime) ? " · rewound" : ""));
            r.SetText(2, run.Build);
            r.SetCustomColor(0, Badges.Gold);
            r.SetSelectable(0, false); r.SetSelectable(1, false); r.SetSelectable(2, false);
            r.Collapsed = !first;
            first = false;
            foreach (var s in run.Snapshots)
            {
                var it = _list.CreateItem(r);
                it.SetText(0, $"Act {s.Act} · floor {s.Floor} · {(s.AfterRoom ? "after" : "start")}");
                it.SetText(1, RunHistoryStore.Pretty(s.Room));
                it.SetText(2, s.SavedAt.ToString("MMM d, HH:mm:ss"));
                it.SetMetadata(0, $"{run.StartTime}|{s.Path}");
            }
        }
    }

    private async Task Go()
    {
        var it = _list.GetSelected();
        if (it == null || it.GetMetadata(0).VariantType == Variant.Type.Nil) { _status.Text = "Select a point in a run first."; return; }
        if (_armed != it)
        {
            _armed = it;
            _go.Text = "Click again to confirm";
            _status.Text = "This replaces the save for that run with the selected point. Progress after it in that run is lost.";
            return;
        }
        var parts = it.GetMetadata(0).AsString().Split('|', 2);
        var run = _runs.First(r => r.StartTime.ToString() == parts[0]);
        var snap = run.Snapshots.First(s => s.Path == parts[1]);
        _go.Disabled = true;
        var err = await Rewind.Restore(run, snap);
        _go.Disabled = false;
        _armed = null;
        _go.Text = "Rewind to selected point";
        if (err != null) { _status.Text = err; return; }
        try { _menu?.RefreshButtons(); } catch (Exception e) { Plugin.LogError($"rewind: menu refresh failed: {e.Message}"); }
        Close();
    }

    private void Poll()
    {
        if (!GodotObject.IsInstanceValid(_layer)) { Stop(); return; }
        bool down = Input.IsKeyPressed(Key.Escape);
        if (down && !_escWasDown) Close();
        _escWasDown = down;
    }

    private void Close()
    {
        Stop();
        if (GodotObject.IsInstanceValid(_layer)) _layer.QueueFree();
    }

    private void Stop() { if (_tree != null) _tree.ProcessFrame -= Poll; _tree = null; }
}

/// <summary>Adds "Run Stats" and "Rewind" buttons to the main menu (postfix on NMainMenu._Ready).</summary>
public static class MenuButtons
{
    public static void ReadyPostfix(object __instance)
    {
        try
        {
            if (__instance is not Control menu) return;
            var box = new VBoxContainer { Name = "SpireWise_MenuButtons" };
            box.AddThemeConstantOverride("separation", 8);
            box.SetAnchorsPreset(Control.LayoutPreset.TopRight);
            box.OffsetLeft = -250; box.OffsetRight = -24; box.OffsetTop = 24;
            box.AddChild(Styled(StatsScreen.Btn("SpireWise · Run Stats", () => StatsScreen.Open(menu))));
            box.AddChild(Styled(StatsScreen.Btn("SpireWise · Rewind", () => RewindScreen.Open(menu))));
            menu.AddChild(box);
        }
        catch (Exception e) { Plugin.LogError($"menu buttons failed: {e.Message}"); }
    }

    private static Button Styled(Button b)
    {
        b.AddThemeStyleboxOverride("normal", StatsScreen.Box(new Color(0.08f, 0.09f, 0.11f, 0.88f), new Color(0.3f, 0.32f, 0.38f)));
        b.AddThemeStyleboxOverride("hover", StatsScreen.Box(new Color(0.12f, 0.13f, 0.16f, 0.95f), Badges.Gold));
        b.AddThemeStyleboxOverride("pressed", StatsScreen.Box(new Color(0.12f, 0.13f, 0.16f, 0.95f), Badges.Gold));
        b.AddThemeColorOverride("font_color", Badges.Gold);
        b.AddThemeColorOverride("font_hover_color", Colors.White);
        return b;
    }
}
