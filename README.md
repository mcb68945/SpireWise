# Spire Pick

A10 card advisor for Slay the Spire 2: for your current deck, how much each card raises your chance to win if you pick it.

- Data: Spire Codex community run exports (solo, unmodded runs), live branch v0.107.1 and beta v0.111.0.
- Method: one regularized logistic model per card, comparing offers that were picked against offers that were skipped,
  adjusted for floor, deck size and every card in the deck, with separate (heavier) regularization on pairwise synergy terms.
- Static site: all scoring happens in the browser from `data/<version>/<character>.json`.

Card names, text and art © Mega Crit Games. Fan project, not affiliated with or endorsed by Mega Crit.
