using System.Globalization;
using System.Text;
using Godot;

namespace Spirewise;

/// <summary>
/// SpireWise Run Stats: filters over the player's own run history, with overview, run list, cards (your results next
/// to the A10 data), relics and encounters, plus CSV export. Built from plain Godot controls in code, opened from the
/// main menu. Read-only: never writes to the game's saves.
/// </summary>
public sealed class StatsScreen
{
    private static readonly Color Bg = new(0.07f, 0.08f, 0.10f, 0.97f);
    private const int Font = 18;

    private readonly CanvasLayer _layer;
    private readonly Label _summary;
    private readonly OptionButton _char, _asc, _mode, _build, _result, _players;
    private readonly CheckBox _abandoned, _rewound;
    private readonly Tree _overview, _runs, _cards, _relics, _fights;
    private readonly List<RunRec> _all;
    private List<RunRec> _shown = new();
    private readonly Dictionary<Tree, (int col, bool desc)> _sort = new();
    private readonly Dictionary<Tree, List<object[]>> _rows = new();
    private SceneTree? _tree;

    public static void Open(Node host)
    {
        try { _ = new StatsScreen(host); }
        catch (Exception e) { Plugin.LogError($"run stats failed to open: {e}"); }
    }

    private StatsScreen(Node host)
    {
        _all = RunHistoryStore.LoadAll(out int failed);
        Plugin.Log($"run stats: {_all.Count} runs loaded" + (failed > 0 ? $", {failed} unreadable" : ""));

        _layer = new CanvasLayer { Layer = 90, Name = "SpireWise_RunStats" };
        host.AddChild(_layer);
        var root = FullRect(new Control { MouseFilter = Control.MouseFilterEnum.Stop });
        _layer.AddChild(root);
        root.AddChild(FullRect(new ColorRect { Color = new Color(0, 0, 0, 0.6f), MouseFilter = Control.MouseFilterEnum.Ignore }));

        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", Box(Bg, Badges.Gold));
        panel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        panel.OffsetLeft = 60; panel.OffsetTop = 50; panel.OffsetRight = -60; panel.OffsetBottom = -50;
        root.AddChild(panel);

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 10);
        panel.AddChild(col);

        var head = new HBoxContainer();
        head.AddChild(Text("SpireWise · Run Stats", 28, Badges.Gold));
        head.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        head.AddChild(Btn("Export CSV", Export));
        head.AddChild(Btn("Close  (Esc)", Close));
        col.AddChild(head);

        var filters = new HFlowContainer();
        filters.AddThemeConstantOverride("h_separation", 14);
        _char = Opt(filters, "Character", new[] { "All" }.Concat(_all.Select(r => r.Character).Distinct().OrderBy(x => x).Select(RunHistoryStore.Pretty)));
        _asc = Opt(filters, "Ascension", new[] { "All" }.Concat(_all.Select(r => r.Ascension).Distinct().OrderByDescending(x => x).Select(a => "A" + a)));
        _mode = Opt(filters, "Mode", new[] { "All" }.Concat(_all.Select(r => r.Mode).Distinct().OrderBy(x => x)));
        _build = Opt(filters, "Version", new[] { "All" }.Concat(_all.Select(r => r.Build).Where(b => b != "").Distinct().OrderByDescending(x => x)));
        _result = Opt(filters, "Result", new[] { "All", "Wins", "Losses" });
        _players = Opt(filters, "Players", new[] { "All", "Solo", "Co-op" });
        _abandoned = Check(filters, "Include abandoned", false);
        _rewound = Check(filters, "Include rewound", false);
        col.AddChild(filters);

        _summary = Text("", 20, Colors.White);
        _summary.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        col.AddChild(_summary);

        var tabs = new TabContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        tabs.AddThemeFontSizeOverride("font_size", Font);
        col.AddChild(tabs);
        _overview = Table(tabs, "By character", "Character", "Runs", "Wins", "Win %", "Highest win", "Avg floor", "Avg time");
        _runs = Table(tabs, "Runs", "Date", "Character", "Asc", "Mode", "Players", "Result", "Floor", "Killed by", "Time", "Rewound");
        _cards = Table(tabs, "Cards", "Card", "Offered", "Picked", "Pick %", "Runs ending with it", "Your win % with it", "A10 players: vs avg card");
        _relics = Table(tabs, "Relics", "Relic", "Runs", "Wins", "Your win %");
        _fights = Table(tabs, "Encounters", "Encounter", "Type", "Fights", "Avg damage", "Max damage", "Deaths");

        if (_all.Count == 0) _summary.Text = "No finished runs found in this profile's history yet.";
        Refresh();

        _tree = host.GetTree();
        _tree.ProcessFrame += Poll;
    }

    // ---------------- data ----------------

    private void Refresh()
    {
        string ch = Sel(_char), asc = Sel(_asc), mode = Sel(_mode), build = Sel(_build), res = Sel(_result), pl = Sel(_players);
        _shown = _all.Where(r =>
            (ch == "All" || RunHistoryStore.Pretty(r.Character) == ch) &&
            (asc == "All" || "A" + r.Ascension == asc) &&
            (mode == "All" || r.Mode == mode) &&
            (build == "All" || r.Build == build) &&
            (res == "All" || (res == "Wins") == r.Win) &&
            (pl == "All" || (pl == "Solo") == (r.Players == 1)) &&
            (_abandoned.ButtonPressed || !r.Abandoned) &&
            (_rewound.ButtonPressed || !r.Rewound)).OrderByDescending(r => r.StartTime).ToList();

        int n = _shown.Count, w = _shown.Count(r => r.Win);
        _summary.Text = n == 0 ? (_all.Count == 0 ? _summary.Text : "No runs match these filters.")
            : $"{n} runs · {w} wins · {Pct(w, n)} win rate · average floor {_shown.Average(r => r.Floors):0.0} · " +
              $"total time {Time(_shown.Sum(r => (long)r.RunSeconds))}" +
              (_all.Count(r => r.Rewound) is int rw && rw > 0 && !_rewound.ButtonPressed ? $" · {rw} rewound run(s) hidden" : "");

        _rows[_overview] = _shown.GroupBy(r => r.Character).Select(g => new object[]
        {
            RunHistoryStore.Pretty(g.Key), g.Count(), g.Count(r => r.Win), Rt(g.Count(r => r.Win), g.Count()),
            g.Where(r => r.Win).Select(r => r.Ascension).DefaultIfEmpty(-1).Max() is var hw && hw >= 0 ? "A" + hw : "-",
            Math.Round(g.Average(r => r.Floors), 1), new Dur(g.Average(r => (double)r.RunSeconds)),
        }).ToList();

        _rows[_runs] = _shown.Select(r => new object[]
        {
            r.Date, RunHistoryStore.Pretty(r.Character), r.Ascension, r.Mode, r.Players, r.Win ? "Win" : r.Abandoned ? "Abandoned" : "Loss",
            r.Floors, r.KilledBy, new Dur(r.RunSeconds), r.Rewound ? "yes" : "",
        }).ToList();

        var cardRows = new List<object[]>();
        foreach (var g in _shown.SelectMany(r => r.Offers.Select(o => (r, o))).GroupBy(x => x.o.Card))
        {
            if (IsBasic(g.Key)) continue;
            int offered = g.Count(), picked = g.Count(x => x.o.Picked);
            var ending = _shown.Where(r => r.Deck.Contains(g.Key)).ToList();
            cardRows.Add(new object[]
            {
                RunHistoryStore.Pretty(g.Key), offered, picked, Rt(picked, offered), ending.Count,
                ending.Count > 0 ? Rt(ending.Count(r => r.Win), ending.Count) : new Ratio(0, 0),
                A10VsTypical(g.Key, g.First().r),
            });
        }
        _rows[_cards] = cardRows;

        _rows[_relics] = _shown.SelectMany(r => r.Relics.Distinct().Select(x => (r, x))).GroupBy(t => t.x).Select(g => new object[]
        {
            RunHistoryStore.Pretty(g.Key), g.Count(), g.Count(t => t.r.Win), Rt(g.Count(t => t.r.Win), g.Count()),
        }).ToList();

        _rows[_fights] = _shown.SelectMany(r => r.Fights.Select(f => (r, f))).GroupBy(t => t.f.Encounter).Select(g => new object[]
        {
            RunHistoryStore.Pretty(g.Key), Cap(g.First().f.Type), g.Count(), Math.Round(g.Average(t => t.f.Damage), 1), g.Max(t => t.f.Damage),
            g.Count(t => !t.r.Win && !t.r.Abandoned && RunHistoryStore.Pretty(g.Key) == t.r.KilledBy),
        }).ToList();

        foreach (var t in new[] { _overview, _runs, _cards, _relics, _fights }) Fill(t);
    }

    private static bool IsBasic(string id) => id.StartsWith("CARD.STRIKE_") || id.StartsWith("CARD.DEFEND_");

    /// <summary>The card's A10 pick value from SpireWise data, relative to an offer-weighted average card, for that run's version and character.</summary>
    private static object A10VsTypical(string card, RunRec r)
    {
        try
        {
            var data = ModelStore.Get(ModelStore.PickVersion(r.Build), r.Character);
            if (data == null || !data.Models.TryGetValue(card, out var m)) return "";
            double w = data.Models.Values.Sum(x => (double)x.Offers);
            double avg = w > 0 ? data.Models.Values.Sum(x => x.Offers * x.Value) / w : 0;
            return new Signed(m.Value - avg);
        }
        catch { return ""; }
    }

    // ---------------- tables ----------------

    private Tree Table(TabContainer tabs, string title, params string[] cols)
    {
        var t = new Tree { Name = title, Columns = cols.Length, ColumnTitlesVisible = true, HideRoot = true, SelectMode = Tree.SelectModeEnum.Row };
        t.AddThemeFontSizeOverride("font_size", Font);
        t.AddThemeFontSizeOverride("title_button_font_size", Font);
        for (int i = 0; i < cols.Length; i++)
        {
            t.SetColumnTitle(i, cols[i]);
            t.SetColumnExpand(i, true);
            t.SetColumnExpandRatio(i, i == 0 ? 3 : 1);
            t.SetColumnClipContent(i, true);
        }
        _sort[t] = (title == "Runs" ? 0 : 1, true);
        t.ColumnTitleClicked += (column, _) =>
        {
            var (c, d) = _sort[t];
            _sort[t] = ((int)column, c == column ? !d : true);
            Fill(t);
        };
        tabs.AddChild(t);
        return t;
    }

    private void Fill(Tree t)
    {
        t.Clear();
        var root = t.CreateItem();
        if (!_rows.TryGetValue(t, out var rows)) return;
        var (c, desc) = _sort[t];
        var ordered = desc ? rows.OrderByDescending(r => SortKey(r[c])) : rows.OrderBy(r => SortKey(r[c]));
        foreach (var r in ordered)
        {
            var it = t.CreateItem(root);
            for (int i = 0; i < r.Length; i++)
            {
                it.SetText(i, Show(r[i]));
                if (r[i] is Signed s) it.SetCustomColor(i, Badges.Tone(s.V));
                if (r[i] is string str && str == "Win") it.SetCustomColor(i, Badges.Good);
            }
        }
    }

    private static IComparable SortKey(object o) => o switch
    {
        Ratio r => r.N == 0 ? -1.0 : r.Value,
        Signed s => s.V,
        Dur d => d.Seconds,
        DateTime dt => dt,
        int i => (double)i,
        long l => (double)l,
        double d => d,
        _ => o?.ToString() ?? "",
    };

    private static string Show(object o) => o switch
    {
        Ratio r => r.N == 0 ? "-" : $"{r.Value * 100:0}%",
        Signed s => Badges.Pct(s.V),
        Dur d => Time((long)d.Seconds),
        DateTime dt => dt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        double d => d.ToString("0.#", CultureInfo.InvariantCulture),
        _ => o?.ToString() ?? "",
    };

    private sealed record Ratio(int K, int N) { public double Value => N == 0 ? 0 : (double)K / N; }
    private sealed record Signed(double V);
    private sealed record Dur(double Seconds);
    private static Ratio Rt(int k, int n) => new(k, n);
    private static string Pct(int k, int n) => n == 0 ? "-" : $"{100.0 * k / n:0}%";
    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
    private static string Time(long sec) => sec >= 3600 ? $"{sec / 3600}h {sec % 3600 / 60:00}m" : $"{sec / 60}m {sec % 60:00}s";

    // ---------------- export ----------------

    private void Export()
    {
        try
        {
            var dir = System.IO.Path.Combine(OS.GetUserDataDir(), "SpireWise", "exports", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            System.IO.Directory.CreateDirectory(dir);
            var heads = new Dictionary<Tree, string>
            {
                [_runs] = "runs.csv", [_cards] = "cards.csv", [_relics] = "relics.csv", [_fights] = "encounters.csv", [_overview] = "by_character.csv",
            };
            foreach (var (t, file) in heads)
            {
                var sb = new StringBuilder();
                sb.AppendLine(string.Join(",", Enumerable.Range(0, t.Columns).Select(i => Csv(t.GetColumnTitle(i)))));
                foreach (var r in _rows[t]) sb.AppendLine(string.Join(",", r.Select(v => Csv(CsvValue(v)))));
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, file), sb.ToString());
            }
            Plugin.Log($"run stats: exported {_shown.Count} runs to {dir}");
            OS.ShellOpen(dir);
        }
        catch (Exception e) { Plugin.LogError($"run stats export failed: {e.Message}"); _summary.Text = "Export failed: " + e.Message; }
    }

    private static string CsvValue(object o) => o switch
    {
        Ratio r => r.N == 0 ? "" : r.Value.ToString("0.###", CultureInfo.InvariantCulture),
        Signed s => s.V.ToString("0.####", CultureInfo.InvariantCulture),
        Dur d => ((long)d.Seconds).ToString(CultureInfo.InvariantCulture),
        DateTime dt => dt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        double d => d.ToString(CultureInfo.InvariantCulture),
        _ => o?.ToString() ?? "",
    };

    private static string Csv(string s) => s.IndexOfAny(new[] { ',', '"', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

    // ---------------- lifetime ----------------

    private bool _escWasDown = true;

    private void Poll()
    {
        if (!GodotObject.IsInstanceValid(_layer)) { Stop(); return; }
        bool down = Input.IsKeyPressed(Godot.Key.Escape);
        if (down && !_escWasDown) Close();
        _escWasDown = down;
    }

    private void Close()
    {
        Stop();
        if (GodotObject.IsInstanceValid(_layer)) _layer.QueueFree();
    }

    private void Stop() { if (_tree != null) _tree.ProcessFrame -= Poll; _tree = null; }

    // ---------------- small UI helpers (shared with RewindScreen) ----------------

    internal static T FullRect<T>(T c) where T : Control { c.SetAnchorsPreset(Control.LayoutPreset.FullRect); return c; }

    internal static StyleBoxFlat Box(Color bg, Color border)
    {
        var b = new StyleBoxFlat { BgColor = bg, BorderColor = border, ContentMarginLeft = 22, ContentMarginRight = 22, ContentMarginTop = 16, ContentMarginBottom = 16 };
        b.SetBorderWidthAll(2); b.SetCornerRadiusAll(12);
        return b;
    }

    internal static Label Text(string s, int size, Color color)
    {
        var l = new Label { Text = s };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        return l;
    }

    internal static Button Btn(string s, Action onPress)
    {
        var b = new Button { Text = s, FocusMode = Control.FocusModeEnum.None };
        b.AddThemeFontSizeOverride("font_size", Font);
        b.Pressed += onPress;
        return b;
    }

    private OptionButton Opt(Container parent, string label, IEnumerable<string> items)
    {
        var box = new HBoxContainer();
        box.AddChild(Text(label, Font, Badges.Muted));
        var o = new OptionButton { FocusMode = Control.FocusModeEnum.None };
        o.AddThemeFontSizeOverride("font_size", Font);
        foreach (var i in items) o.AddItem(i);
        o.Selected = 0;
        o.ItemSelected += _ => Refresh();
        box.AddChild(o);
        parent.AddChild(box);
        return o;
    }

    private CheckBox Check(Container parent, string label, bool on)
    {
        var c = new CheckBox { Text = label, ButtonPressed = on, FocusMode = Control.FocusModeEnum.None };
        c.AddThemeFontSizeOverride("font_size", Font);
        c.Toggled += _ => Refresh();
        parent.AddChild(c);
        return c;
    }

    private static string Sel(OptionButton o) => o.Selected < 0 ? "All" : o.GetItemText(o.Selected);
}
