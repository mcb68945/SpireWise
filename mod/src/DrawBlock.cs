namespace Spirewise;

/// <summary>
/// Rules the data can't learn because the combination is too rare. Fiddle (an Ancient relic) gives +2 cards at the
/// start of your turn but stops every other draw on your turn, so a card's draw effect - and an upgrade that only adds
/// draw - does nothing. Only 40 A10 runs in v0.111 had Fiddle with Burning Pact, far too few to fit.
/// </summary>
public static class DrawBlock
{
    /// <summary>Cards that draw when played or through the power they apply (from the v0.113 game code).</summary>
    private static readonly HashSet<string> DrawCards = new()
    {
        "CARD.ACROBATICS", "CARD.ADRENALINE", "CARD.BACKFLIP", "CARD.BATTLE_TRANCE", "CARD.BIG_BANG", "CARD.BRIGHTEST_FLAME",
        "CARD.BURNING_PACT", "CARD.COMPILE_DRIVER", "CARD.CONSTELLATION", "CARD.COOLHEADED", "CARD.DAGGER_THROW", "CARD.DARK_EMBRACE",
        "CARD.DECISIONS_DECISIONS", "CARD.DEEP_STRETCH", "CARD.DEPRECATED_CARD", "CARD.DRUM_OF_BATTLE", "CARD.ENCORE", "CARD.ESCAPE_PLAN",
        "CARD.EXPERTISE", "CARD.FETCH", "CARD.FINESSE", "CARD.FLASH_OF_STEEL", "CARD.FTL", "CARD.GLIMMER",
        "CARD.GLOW", "CARD.IMPATIENCE", "CARD.ITERATION", "CARD.JOY", "CARD.KI_HOP", "CARD.MAD_SCIENCE",
        "CARD.MASTER_OF_STRATEGY", "CARD.MINION_STRIKE", "CARD.MODDED", "CARD.MOTIF", "CARD.NEUROSURGE", "CARD.OFFERING",
        "CARD.OVERCLOCK", "CARD.PAGESTORM", "CARD.PARSE", "CARD.PHOTON_CUT", "CARD.PILLAGE", "CARD.POMMEL_STRIKE",
        "CARD.PREPARED", "CARD.PROPHESIZE", "CARD.REBOOT", "CARD.REFLEX", "CARD.RESTLESSNESS", "CARD.ROCKET_PUNCH",
        "CARD.RUMMAGE", "CARD.SCOURGE", "CARD.SCRAPE", "CARD.SCRAWL", "CARD.SHRUG_IT_OFF", "CARD.SKIM",
        "CARD.SLIMED", "CARD.SOUL", "CARD.SPOILS_OF_BATTLE", "CARD.STEADY_CADENCE", "CARD.SWEEPING_BEAM", "CARD.THE_HERMIT",
        "CARD.THE_HIGH_PRIESTESS", "CARD.THE_SUN", "CARD.THINKING_AHEAD", "CARD.VICIOUS", "CARD.WAH_CHO_KI", "CARD.WHEEL_OF_FORTUNE",
        "CARD.WREST"
    };

    public static bool Blocked(RunContext ctx, string cardId) => ctx.Context.Contains("RELIC.FIDDLE") && DrawCards.Contains(cardId);

    public const string Note = "Fiddle blocks its draw";
}
