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

    /// <summary>Entry point for the loop: plays a whole tournament and returns the hero's result. Cash/seat bookkeeping is the loop's job.
    /// v2 (DESIGN.md C-v2): the tournament is the all-in-or-fold win ladder. Kind and StudyLevel come from <paramref name="config"/>;
    /// the hero is <see cref="ILadderHooks.LadderHero"/> when the hooks provide one, else the chart bot.</summary>
    public static class HoldemRunner
    {
        public static TournamentResult RunTournament(TournamentConfig config, IRng rng, ITournamentHooks hooks)
        {
            var ladder = new Ladder(LadderConfig.For(config.Kind, config.StudyLevel), rng);
            var hero = (hooks as ILadderHooks)?.LadderHero ?? ChartPlayer.Instance;
            ladder.RunToEnd(hero, hooks.OfferReentry);
            return ladder.Result;
        }
    }

    /// <summary>The HUD-4 advice followed to the letter: push when the hand is inside the pusher chart for this stack and position.</summary>
    public sealed class ChartPlayer : ILadderPlayer
    {
        public static readonly ChartPlayer Instance = new ChartPlayer();
        public bool AllIn(LadderHand h) => Ladder.HandPercentile(h) <= Ladder.PushAdvicePercent(h);
    }
}
