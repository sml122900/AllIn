using System.Collections.Generic;

namespace AllIn.Core
{
    public sealed partial class Loop
    {
        partial void OnNewDay()
        {
            Data.lottoBoughtToday = 0;
        }

        partial void OnSlotEnd()
        {
            if (Data.slot == Slot.Evening && Calendar.IsSaturday(Data.day)) DrawLotto();
        }

        public int TicketsForNextDraw
        {
            get
            {
                int n = 0, draw = Calendar.NextSaturday(Data.day);
                foreach (var t in Data.tickets) if (t.drawDay == draw) n++;
                return n;
            }
        }

        void DrawLotto()
        {
            Data.stats.lottoDraws++;
            int[] drawn = LottoRules.Draw(Rng);
            int crowd = Rules.LottoCrowdWinnersBase + Rng.NextInt(-Rules.LottoCrowdWinnersSpread, Rules.LottoCrowdWinnersSpread + 1);

            var mine = new List<int>();   // 6 numbers per game
            var ranks = new List<int>();  // one per game
            long won = 0;
            foreach (var t in Data.tickets)
            {
                if (t.drawDay != Data.day) continue;
                bool anyWin = false;
                for (int g = 0; g < Rules.LottoGamesPerTicket; g++)
                {
                    int off = g * Rules.LottoPicks;
                    int rank = LottoRules.Rank(t.numbers, off, drawn);
                    for (int k = 0; k < Rules.LottoPicks; k++) mine.Add(t.numbers[off + k]);
                    ranks.Add(rank);
                    if (rank == 0) continue;
                    anyWin = true;
                    Data.stats.lottoRanks[rank]++;
                    won += LottoRules.NetPrize(rank);
                    AddMental(Rules.LottoRankMental[rank]);
                }
                if (!anyWin) AddMental(-Rules.LottoMissMental);
            }
            Data.tickets.RemoveAll(t => t.drawDay == Data.day);

            if (ranks.Count == 0) return; // nothing bought for this draw: nothing to show
            if (won > 0) AddCash(won);
            Data.stats.lottoWon += won;
            Emit(new GameEvent
            {
                id = EventIds.LottoDraw,
                ints = drawn,
                ints2 = mine.ToArray(),
                amount = won,
                count = crowd,
            });
            Log(Texts.LottoDrawTitle + " " + Fmt.Won(won));
        }
    }
}
