using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace Spirewise;

/// <summary>
/// Entry point. Every hook is looked up by name and installed separately: if a game update renames or removes
/// something, that one feature is skipped (and logged) and the rest of the mod and the game keep working.
/// </summary>
[ModInitializer("Initialize")]
public static class Plugin
{
    public const string Id = "SpireWise";
    private static Harmony? _harmony;
    private static readonly List<string> Installed = new(), Skipped = new();

    public static void Initialize()
    {
        try
        {
            Settings.Load();
            _harmony = new Harmony("spirewise");

            Hook("MegaCrit.Sts2.Core.Nodes.Screens.CardSelection.NCardRewardSelectionScreen", "RefreshOptions",
                 owner: typeof(RewardPatch), feature: "card reward labels", postfix: nameof(RewardPatch.Postfix));
            Hook("MegaCrit.Sts2.Core.Commands.CardSelectCmd", "FromDeckForRemoval",
                 owner: typeof(DeckScreenPatch), feature: "removal detection", prefix: nameof(DeckScreenPatch.RemovalPrefix));
            Hook("MegaCrit.Sts2.Core.Nodes.Screens.CardSelection.NDeckCardSelectScreen", "_Ready",
                 owner: typeof(DeckScreenPatch), feature: "removal labels", postfix: nameof(DeckScreenPatch.DeckSelectReady));
            Hook("MegaCrit.Sts2.Core.Nodes.Screens.CardSelection.NDeckUpgradeSelectScreen", "_Ready",
                 owner: typeof(DeckScreenPatch), feature: "upgrade labels", postfix: nameof(DeckScreenPatch.UpgradeReady));

            const string N = "MegaCrit.Sts2.Core.Nodes.";
            Hook(N + "Screens.CardSelection.NDeckTransformSelectScreen", "_Ready",
                 owner: typeof(DeckScreenPatch), feature: "transform labels", postfix: nameof(DeckScreenPatch.TransformReady));
            Hook(N + "Screens.Shops.NMerchantCard", "FillSlot", owner: typeof(ExtraPatches), feature: "shop cards", postfix: nameof(ExtraPatches.ShopCardPostfix));
            Hook(N + "Screens.Shops.NMerchantRelic", "FillSlot", owner: typeof(ExtraPatches), feature: "shop relics", postfix: nameof(ExtraPatches.ShopRelicPostfix));
            Hook(N + "RestSite.NRestSiteButton", "_Ready", owner: typeof(ExtraPatches), feature: "rest site", postfix: nameof(ExtraPatches.RestButtonPostfix));
            Hook(N + "Events.NEventOptionButton", "_Ready", owner: typeof(ExtraPatches), feature: "ancient choices", postfix: nameof(ExtraPatches.EventOptionPostfix));
            Hook(N + "Screens.Map.NMapScreen", "Open", owner: typeof(ExtraPatches), feature: "win chance", postfix: nameof(ExtraPatches.MapOpenPostfix));

            Hook(N + "Screens.MainMenu.NMainMenu", "_Ready", owner: typeof(MenuButtons), feature: "menu buttons", postfix: nameof(MenuButtons.ReadyPostfix));
            Hook("MegaCrit.Sts2.Core.Saves.Managers.RunSaveManager", "SaveRun", owner: typeof(Rewind), feature: "rewind snapshots",
                 prefix: nameof(Rewind.SaveRunPrefix), args: new[] { typeof(MegaCrit.Sts2.Core.Saves.SerializableRun), typeof(bool) });
            Hook("MegaCrit.Sts2.Core.Saves.SaveManager", "SaveRunHistory", owner: typeof(Rewind), feature: "rewind marking",
                 prefix: nameof(Rewind.SaveRunHistoryPrefix));

            Settings.StartHotkey();
            ModConfigBridge.DeferredRegister();
            Log($"loaded {Version} · data {string.Join(", ", ModelStore.Versions)} · on: {string.Join(", ", Installed)}" +
                (Skipped.Count > 0 ? $" · SKIPPED (game changed?): {string.Join(", ", Skipped)}" : "") +
                $" · labels {(Settings.Current.Enabled ? "on" : "off")} (F7 toggles)");
        }
        catch (Exception e)
        {
            LogError($"failed to initialize, mod inactive this session: {e}");
        }
    }

    private static string Version => typeof(Plugin).Assembly.GetName().Version?.ToString(3) ?? "?";

    private static void Hook(string typeName, string method, Type owner, string feature, string? prefix = null, string? postfix = null, Type[]? args = null)
    {
        try
        {
            var type = AccessTools.TypeByName(typeName);
            var target = type == null ? null : AccessTools.Method(type, method, args);
            if (target == null) { Skipped.Add(feature); LogError($"{feature}: {typeName}.{method} not found, feature off"); return; }
            _harmony!.Patch(target,
                prefix: prefix == null ? null : new HarmonyMethod(AccessTools.Method(owner, prefix)),
                postfix: postfix == null ? null : new HarmonyMethod(AccessTools.Method(owner, postfix)));
            Installed.Add(feature);
        }
        catch (Exception e)
        {
            Skipped.Add(feature);
            LogError($"{feature}: could not hook {typeName}.{method}: {e.GetType().Name}: {e.Message}");
        }
    }

    public static void Log(string msg) => GD.Print($"[{Id}] {msg}");
    public static void LogError(string msg) => GD.PrintErr($"[{Id}] {msg}");
}
