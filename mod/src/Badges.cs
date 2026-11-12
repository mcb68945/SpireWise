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

    public static string Pct(double x) => (x >= 0 ? "+" : "−") + Math.Abs(x * 100).ToString("0.0") + "%";

    /// <summary>Create (or replace) a badge under <paramref name="parent"/>, centred horizontally at <paramref name="y"/>.</summary>
    public static PanelContainer Attach(Node parent, string text, Color color, bool highlight, float y, int fontSize = 26, float width = 200)
    {
        parent.GetNodeOrNull(NodeName)?.QueueFree();
        var panel = new PanelContainer { Name = NodeName, MouseFilter = Control.MouseFilterEnum.Ignore, ZIndex = 5 };
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
        panel.AddChild(label);
        panel.CustomMinimumSize = new Vector2(width, 0);
        panel.Visible = Settings.Current.Enabled;
        parent.AddChild(panel);
        panel.Position = new Vector2(-width / 2, y);
        lock (Live) { Live.RemoveAll(w => !w.TryGetTarget(out var c) || !GodotObject.IsInstanceValid(c)); Live.Add(new(panel)); }
        return panel;
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
