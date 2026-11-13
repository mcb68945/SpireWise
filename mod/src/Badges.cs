using Godot;

namespace Spirewise;

/// <summary>Shared look for every SpireWise label (same colours as the website).</summary>
public static class Badges
{
    public const string NodeName = "SpireWise_Badge";
    public static readonly Color Good = new(0.44f, 0.81f, 0.54f);   // #6FCF8A
    public static readonly Color Bad = new(0.89f, 0.45f, 0.37f);    // #E2735F
    public static readonly Color Gold = new(0.85f, 0.70f, 0.37f);   // #D9B25F
    public static readonly Color Muted = new(0.75f, 0.73f, 0.68f);

    private static readonly List<WeakReference<Control>> Live = new();

    /// <summary>Green for a real gain, red for a real loss, grey when it rounds to about even (under half a point).</summary>
    public static Color Tone(double x) => x >= 0.005 ? Good : x <= -0.005 ? Bad : Muted;

    public static string Pct(double x) => (x >= 0 ? "+" : "−") + Math.Abs(x * 100).ToString("0.0") + "%";

    /// <summary>Create (or replace) a badge under <paramref name="parent"/>, centred horizontally at <paramref name="y"/>.</summary>
    public static PanelContainer Attach(Node parent, string text, Color color, bool highlight, float y, int fontSize = 26, float width = 200,
        Node? scope = null, string? cardId = null, string? detail = null, Color? detailColor = null)
    {
        Remove(parent);
        // No ZIndex lift: screens that open on top (e.g. Smith over the rest site) must be able to cover our labels.
        var panel = new PanelContainer { Name = NodeName, MouseFilter = Control.MouseFilterEnum.Ignore };
        var box = new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.09f, 0.11f, 0.88f),
            BorderColor = highlight ? Gold : new Color(0.3f, 0.32f, 0.38f),
            ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 3, ContentMarginBottom = 5,
        };
        box.SetBorderWidthAll(highlight ? 3 : 1);
        box.SetCornerRadiusAll(10);
        panel.AddThemeStyleboxOverride("panel", box);
        var label = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeConstantOverride("outline_size", 6);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        if (detail == null) panel.AddChild(label);
        else
        {
            // Main number on top; the smaller second line explains part of it and gets its own colour.
            var stack = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            stack.AddThemeConstantOverride("separation", 0);
            stack.AddChild(label);
            var sub = new Label { Text = detail, HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
            sub.AddThemeFontSizeOverride("font_size", Math.Max(14, (int)(fontSize * 0.75f)));
            sub.AddThemeColorOverride("font_color", detailColor ?? Muted);
            sub.AddThemeConstantOverride("outline_size", 5);
            sub.AddThemeColorOverride("font_outline_color", Colors.Black);
            stack.AddChild(sub);
            panel.AddChild(stack);
        }
        panel.CustomMinimumSize = new Vector2(width, 0);
        panel.Visible = Settings.Current.Enabled;
        parent.AddChild(panel);
        panel.Position = new Vector2(-width / 2, y);
        // The game reuses card holders between screens. A label dies with the screen it was made for, and a card label
        // also dies as soon as its holder shows a different card (checked every frame by Sweep).
        if (scope != null)
        {
            var weak = new WeakReference<PanelContainer>(panel);
            scope.TreeExiting += () => { if (weak.TryGetTarget(out var p) && GodotObject.IsInstanceValid(p)) p.QueueFree(); };
        }
        if (cardId != null) panel.SetMeta(CardMeta, cardId);
        EnsureSweep();
        lock (Live) { Live.RemoveAll(w => !w.TryGetTarget(out var c) || !GodotObject.IsInstanceValid(c)); Live.Add(new(panel)); }
        return panel;
    }

    /// <summary>Take the old badge out of the tree right away. QueueFree alone leaves it in place until the end of the
    /// frame, so the new badge gets renamed and the next refresh can't find it, leaving duplicates stacked up.</summary>
    public static void Remove(Node parent, string name = NodeName)
    {
        var old = parent.GetNodeOrNull(name);
        if (old == null) return;
        parent.RemoveChild(old);
        old.QueueFree();
    }

    private const string CardMeta = "sw_card";
    private static bool _sweeping;

    private static void EnsureSweep()
    {
        if (_sweeping || Engine.GetMainLoop() is not SceneTree tree) return;
        _sweeping = true;
        tree.ProcessFrame += Sweep;
    }

    /// <summary>Free card labels whose holder now shows a different card (or none).</summary>
    private static void Sweep()
    {
        List<Control> stale = new();
        lock (Live)
        {
            foreach (var w in Live)
            {
                if (!w.TryGetTarget(out var c) || !GodotObject.IsInstanceValid(c) || !c.HasMeta(CardMeta)) continue;
                var holder = c.GetParent();
                var model = holder?.GetType().GetProperty("CardModel")?.GetValue(holder);
                var id = model?.GetType().GetProperty("Id")?.GetValue(model)?.ToString();
                if (id != (string)c.GetMeta(CardMeta)) stale.Add(c);
            }
        }
        foreach (var c in stale) c.QueueFree();
    }

    /// <summary>Register any SpireWise control so F7 / the settings switch shows or hides it.</summary>
    public static void Track(Control c)
    {
        lock (Live) { Live.RemoveAll(w => !w.TryGetTarget(out var x) || !GodotObject.IsInstanceValid(x)); Live.Add(new(c)); }
    }

    public static void SetAllVisible(bool visible)
    {
        lock (Live)
            foreach (var w in Live)
                if (w.TryGetTarget(out var c) && GodotObject.IsInstanceValid(c)) c.Visible = visible;
    }
}
