using System.Text.Json;
using Godot;

namespace Spirewise;

/// <summary>
/// User settings, saved next to the game's own settings (Godot user:// folder).
/// F7 toggles all SpireWise labels on/off instantly; the game's Mods screen checkbox
/// disables the whole mod (takes effect after a restart).
/// </summary>
public static class Settings
{
    public sealed class Data
    {
        public bool Enabled { get; set; } = true;
        public bool CardRewards { get; set; } = true;
        public bool RemoveUpgrade { get; set; } = true;
        public bool Shop { get; set; } = true;
        public bool RestSite { get; set; } = true;
        public bool Ancients { get; set; } = true;
        public bool WinChance { get; set; } = true;
    }

    public static Data Current { get; private set; } = new();
    private static string PathOnDisk => System.IO.Path.Combine(OS.GetUserDataDir(), "spirewise_settings.json");

    public static void Load()
    {
        try
        {
            if (System.IO.File.Exists(PathOnDisk))
                Current = JsonSerializer.Deserialize<Data>(System.IO.File.ReadAllText(PathOnDisk)) ?? new Data();
        }
        catch (Exception e) { Plugin.LogError($"settings load failed, using defaults: {e.Message}"); Current = new Data(); }
    }

    public static void Save()
    {
        try { System.IO.File.WriteAllText(PathOnDisk, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception e) { Plugin.LogError($"settings save failed: {e.Message}"); }
    }

    public static bool ShowRewards => Current.Enabled && Current.CardRewards;
    public static bool ShowRemoveUpgrade => Current.Enabled && Current.RemoveUpgrade;
    public static bool ShowShop => Current.Enabled && Current.Shop;
    public static bool ShowRestSite => Current.Enabled && Current.RestSite;
    public static bool ShowAncients => Current.Enabled && Current.Ancients;
    public static bool ShowWinChance => Current.Enabled && Current.WinChance;

    public static void Toggle() => SetEnabled(!Current.Enabled);

    public static void SetEnabled(bool on)
    {
        Current.Enabled = on;
        Save();
        Badges.SetAllVisible(Current.Enabled);
        Plugin.Log($"labels {(Current.Enabled ? "on" : "off")}");
    }

    // ---- F7 hotkey, polled once per frame via the scene tree (no custom Node class needed) ----
    private static bool _wasDown;
    private static bool _hooked;

    public static void StartHotkey()
    {
        if (_hooked) return;
        if (Engine.GetMainLoop() is SceneTree tree)
        {
            tree.ProcessFrame += OnFrame;
            _hooked = true;
        }
    }

    private static void OnFrame()
    {
        try
        {
            bool down = Input.IsKeyPressed(Key.F7);
            if (down && !_wasDown) Toggle();
            _wasDown = down;
        }
        catch { /* never break the game loop */ }
    }
}
