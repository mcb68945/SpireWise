# SpireWise mod (Slay the Spire 2)

Labels each card on the card reward screen with how much it raises your chance to win for your current deck
(Ascension 10 model, same math as the SpireWise site), highlights the best pick and says when skipping is better.
Read-only: it never changes game state.

Also labels your deck on removal screens (shop/events) and upgrade screens (rest-site Smith) with how much removing/upgrading each card helps
compared with choosing a different card. F7 hides/shows all labels (saved in spirewise_settings.json in the game's user folder).
The game's Mods screen checkbox turns the whole mod off (takes effect after restart).

## v0.4.0 features
- Shop: win-chance gain on cards and relics for sale.
- Rest sites: what winning A10 players chose (Smith vs Rest) at your HP and act. Descriptive on purpose: the data can't give a fair
  causal Smith-vs-Rest estimate, because players only Smith at low HP when they know they're safe.
- Transform screens: rank your deck like removal (mostly basics have data).
- Ancient choices: win-chance gain per relic option, best highlighted.
- Map: estimated win chance for the current run (deck, HP, gold, relics, floor; held-out AUC about 0.71-0.77).

## Settings
- Settings > Mod Settings > SpireWise when the community ModConfig mod is installed: master switch plus one
  toggle per feature. Without ModConfig, F7 toggles everything and choices are saved in spirewise_settings.json.
- The game's own Mods screen checkbox disables the whole mod (after a restart).

## Game updates
Every hook is looked up by name at startup. If a patch renames something, that feature is skipped and logged as
"[SpireWise] ... not found, feature off" in the game log; the rest of the mod and the game keep working.

## Data
The models in `data/` are computed from [Spire Codex](https://spire-codex.com) community run exports.
Only fitted model numbers ship, not the runs themselves.

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
