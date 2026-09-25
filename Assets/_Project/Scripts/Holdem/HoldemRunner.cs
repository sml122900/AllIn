using AllIn.Core;
using AllIn.Holdem.Brain;

namespace AllIn.Holdem
{
    /// <summary>What the caller (the 30-day loop, a sim, a test) supplies to a headless tournament.</summary>
    public interface ITournamentHooks
    {
        /// <summary>Plays the hero seat. Headless runs need one; the Holdem scene drives <see cref="Tournament"/> directly instead.</summary>
        IDecider Hero { get; }

        /// <summary>Asked when the hero busts inside the re-entry window. True = pay <paramref name="cost"/> and sit back down.</summary>
        bool OfferReentry(long cost);
    }

    /// <summary>Entry point for the loop: plays a whole tournament and returns the hero's result. Cash/seat bookkeeping is the loop's job.</summary>
    public static class HoldemRunner
    {
        public static TournamentResult RunTournament(TournamentConfig config, IRng rng, ITournamentHooks hooks)
        {
            var t = new Tournament(config, rng, BotFactory.Instance, hooks.Hero) { StopWhenHeroOut = true, HeroBotReentry = hooks.OfferReentry };
            t.RunToEnd(hooks.OfferReentry);
            return t.Result;
        }
    }
}
