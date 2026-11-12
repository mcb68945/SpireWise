using System.Reflection;
using Godot;

namespace Spirewise;

/// <summary>
/// Adds SpireWise toggles to the community ModConfig mod's "Mod Settings" tab (inside the game's Settings),
/// the same way Relic Share does. Reflection only, no compile-time dependency: if ModConfig isn't installed,
/// nothing happens and the mod uses its own saved settings (F7 still toggles all labels).
/// Adapted from ModConfig's published template (github.com/xhyrzldf/ModConfig-STS2, examples/ModConfigBridge.cs).
/// </summary>
internal static class ModConfigBridge
{
    private const string ModId = "SpireWise";
    private static bool _available, _registered;
    private static Type? _apiType, _entryType, _configTypeEnum;

    /// <summary>Detect and register on the next frame, after other mods (ModConfig) have loaded.</summary>
    internal static void DeferredRegister()
    {
        if (Engine.GetMainLoop() is SceneTree tree) tree.ProcessFrame += OnNextFrame;
    }

    private static void OnNextFrame()
    {
        if (Engine.GetMainLoop() is SceneTree tree) tree.ProcessFrame -= OnNextFrame;
        try
        {
            var types = AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => { try { return a.GetTypes(); } catch { return Type.EmptyTypes; } }).ToArray();
            _apiType = types.FirstOrDefault(t => t.FullName == "ModConfig.ModConfigApi");
            _entryType = types.FirstOrDefault(t => t.FullName == "ModConfig.ConfigEntry");
            _configTypeEnum = types.FirstOrDefault(t => t.FullName == "ModConfig.ConfigType");
            _available = _apiType != null && _entryType != null && _configTypeEnum != null;
        }
        catch { _available = false; }
        if (_available) Register();
        else Plugin.Log("ModConfig not installed: using F7 and spirewise_settings.json for settings");
    }

    private static void Register()
    {
        if (_registered) return;
        _registered = true;
        try
        {
            var s = Settings.Current;
            var entries = BuildEntries(new (string key, string label, string desc, bool value, Action<bool> set)[]
            {
                ("enabled", "Enable SpireWise labels", "Master switch for every SpireWise label (same as F7).", s.Enabled, v => Settings.SetEnabled(v)),
                ("cardRewards", "Card rewards", "Win-chance gain on each card reward.", s.CardRewards, v => s.CardRewards = v),
                ("removeUpgrade", "Remove / upgrade / transform", "Rank your deck on removal, upgrade and transform screens.", s.RemoveUpgrade, v => s.RemoveUpgrade = v),
                ("shop", "Shop", "Win-chance gain on cards and relics for sale.", s.Shop, v => s.Shop = v),
                ("restSite", "Rest sites", "What winning A10 players chose (Smith vs Rest) at your HP.", s.RestSite, v => s.RestSite = v),
                ("ancients", "Ancient choices", "Win-chance gain for each Ancient relic option.", s.Ancients, v => s.Ancients = v),
                ("winChance", "Win chance on map", "Estimated chance this run wins, shown on the map.", s.WinChance, v => s.WinChance = v),
            });
            var names = new Dictionary<string, string> { ["en"] = "SpireWise" };
            var reg = _apiType!.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.Name == "Register")
                .OrderByDescending(m => m.GetParameters().Length).First();
            if (reg.GetParameters().Length == 4) reg.Invoke(null, new object[] { ModId, "SpireWise", names, entries });
            else reg.Invoke(null, new object[] { ModId, "SpireWise", entries });

            // ModConfig's saved values win over our own file once it's present.
            s.Enabled = GetValue("enabled", s.Enabled); s.CardRewards = GetValue("cardRewards", s.CardRewards);
            s.RemoveUpgrade = GetValue("removeUpgrade", s.RemoveUpgrade); s.Shop = GetValue("shop", s.Shop);
            s.RestSite = GetValue("restSite", s.RestSite); s.Ancients = GetValue("ancients", s.Ancients); s.WinChance = GetValue("winChance", s.WinChance);
            Settings.Save();
            Plugin.Log("registered with ModConfig (Settings > Mod Settings > SpireWise)");
        }
        catch (Exception e) { Plugin.LogError($"ModConfig registration failed, using own settings: {e.GetType().Name}: {e.Message}"); }
    }

    private static T GetValue<T>(string key, T fallback)
    {
        if (!_available) return fallback;
        try
        {
            var r = _apiType!.GetMethod("GetValue", BindingFlags.Public | BindingFlags.Static)?.MakeGenericMethod(typeof(T)).Invoke(null, new object[] { ModId, key });
            return r != null ? (T)r : fallback;
        }
        catch { return fallback; }
    }

    private static Array BuildEntries((string key, string label, string desc, bool value, Action<bool> set)[] toggles)
    {
        var arr = Array.CreateInstance(_entryType!, toggles.Length);
        for (int i = 0; i < toggles.Length; i++)
        {
            var t = toggles[i];
            var e = Activator.CreateInstance(_entryType!)!;
            Set(e, "Key", t.key); Set(e, "Label", t.label); Set(e, "Description", t.desc);
            Set(e, "Type", Enum.Parse(_configTypeEnum!, "Toggle")); Set(e, "DefaultValue", (object)t.value);
            Set(e, "OnChanged", new Action<object>(v => { t.set(Convert.ToBoolean(v)); Settings.Save(); }));
            arr.SetValue(e, i);
        }
        return arr;
    }

    private static void Set(object o, string name, object v) => o.GetType().GetProperty(name)?.SetValue(o, v);
}
