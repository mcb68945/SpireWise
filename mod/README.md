# SpireWise mod (Slay the Spire 2)

Labels each card on the card reward screen with how much it raises your chance to win for your current deck
(Ascension 10 model, same math as the SpireWise site), highlights the best pick and says when skipping is better.
Read-only: it never changes game state.

Also labels your deck on removal screens (shop/events) and upgrade screens (rest-site Smith) with how much removing/upgrading each card helps
compared with choosing a different card. F7 hides/shows all labels (saved in spirewise_settings.json in the game's user folder).
The game's Mods screen checkbox turns the whole mod off (takes effect after restart).

## v0.5.0: Run Stats and Rewind

Two buttons on the main menu (top right):

- **Run Stats** reads your own run history (the game's `.run` files) and shows win rate by character, a sortable run list, your card picks and win rate with each card next to the A10 data, relics, and damage taken per encounter. Filters: character, ascension, mode, game version, result, solo/co-op, abandoned, rewound. **Export CSV** writes the filtered tables to `SpireWise/exports/` in the game's user folder. No player IDs are exported.
- **Rewind** keeps a copy of your solo run save after every room (last 5 runs). Pick a point and press Continue to replay from that floor. Uses the game's own save format and writer. Not available for co-op or Daily runs. A rewound run is written to your history as a custom run, so it stays out of community stats sites and out of Run Stats unless you tick "Include rewound". Snapshots live in `SpireWise/rewind/`; turn snapshotting off with `"Rewind": false` in `spirewise_settings.json`.

## v0.4.0 features
- Shop: win-chance gain on cards and relics for sale.
- Rest sites: what winning A10 players chose (Smith vs Rest) at your HP and act. Descriptive on purpose: the data can't give a fair
  causal Smith-vs-Rest estimate, because players only Smith at low HP when they know they're safe.
- Transform screens: rank your deck like removal (mostly basics have data).
- Ancient choices: win-chance gain per relic option, best highlighted.
- Map: estimated win chance for the current run (deck, HP, gold, relics, floor; held-out AUC about 0.71-0.77).

## Settings
- Settings > Mod Settings > SpireWise when the community ModConfig mod is installed (same as Relic Share): master switch plus one
  toggle per feature. Without ModConfig, F7 toggles everything and choices are saved in spirewise_settings.json.
- The game's own Mods screen checkbox disables the whole mod (after a restart).

## Game updates
Every hook is looked up by name at startup. If a patch renames something, that feature is skipped and logged as
"[SpireWise] ... not found, feature off" in the game log; the rest of the mod and the game keep working.

## Build
Needs the .NET 9 SDK. GameLibs defaults to the Steam install's data_sts2_windows_x86_64 folder.

    dotnet build -c Release

## Install
Copy bin/Release/net9.0/SpireWise.dll and SpireWise.json into
`Slay the Spire 2/mods/SpireWise/`.

## Data
data/<version>/<character>.json.gz are embedded in the DLL (the game loader treats every .json under mods/ as a manifest).
The mod picks the data version matching the game's release_info version, else the newest data not newer than the game.
Regenerate from C:\claude\sts2-synergy (pipeline/export_site_v2.py) and gzip.

## Hook
Harmony postfix on NCardRewardSelectionScreen.RefreshOptions; reads RunManager state, LocalContext.GetMe(players),
player.Deck.Cards, RunState.TotalFloor / AscensionLevel, ReleaseInfoManager version.
