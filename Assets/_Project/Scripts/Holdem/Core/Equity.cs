using AllIn.Core;

namespace AllIn.Holdem
{
    /// <summary>Hand equity (win share, ties split) by Monte Carlo, or exactly when few board cards are missing.</summary>
    public static class Equity
    {
        /// <summary>Equity of (h0, h1) against <paramref name="opponents"/> random hands with a random rest-of-board.</summary>
        public static double VsRandom(int h0, int h1, int[] board, int boardCount, int opponents, IRng rng, int samples)
        {
            var dead = new bool[52];
            dead[h0] = dead[h1] = true;
            for (int i = 0; i < boardCount; i++) dead[board[i]] = true;
            var live = Live(dead, out int liveCount);
            var full = new int[5];
            for (int i = 0; i < boardCount; i++) full[i] = board[i];
            int need = 5 - boardCount, draw = need + 2 * opponents;

            double total = 0;
            for (int n = 0; n < samples; n++)
            {
                for (int i = 0; i < draw; i++) // partial Fisher–Yates over the live cards
                {
                    int j = i + rng.NextInt(liveCount - i);
                    (live[i], live[j]) = (live[j], live[i]);
                }
                for (int i = 0; i < need; i++) full[boardCount + i] = live[i];
                int mine = HandEvaluator.Evaluate(h0, h1, full, 5);
                int best = int.MinValue, ties = 0;
                for (int o = 0; o < opponents; o++)
                {
                    int sc = HandEvaluator.Evaluate(live[need + 2 * o], live[need + 2 * o + 1], full, 5);
                    if (sc > best) { best = sc; ties = 0; }
                    if (sc == best) ties++;
                }
                if (mine > best) total += 1;
                else if (mine == best) total += 1.0 / (ties + 1);
            }
            return total / samples;
        }

        /// <summary>
        /// Equity of hand <paramref name="hero"/> against the other known hands, over every remaining board when at most two
        /// cards are missing, else by Monte Carlo.
        /// </summary>
        public static double VsKnown(int[][] hands, int hero, int[] board, int boardCount, IRng rng, int samples)
        {
            var dead = new bool[52];
            foreach (var h in hands) { dead[h[0]] = true; dead[h[1]] = true; }
            for (int i = 0; i < boardCount; i++) dead[board[i]] = true;
            var live = Live(dead, out int liveCount);
            var full = new int[5];
            for (int i = 0; i < boardCount; i++) full[i] = board[i];
            int need = 5 - boardCount;

            double total = 0;
            int trials = 0;
            if (need == 0)
            {
                return Share(hands, hero, full);
            }
            if (need <= 2)
            {
                for (int a = 0; a < liveCount; a++)
                {
                    full[boardCount] = live[a];
                    if (need == 1) { total += Share(hands, hero, full); trials++; continue; }
                    for (int b = a + 1; b < liveCount; b++)
                    {
                        full[boardCount + 1] = live[b];
                        total += Share(hands, hero, full);
                        trials++;
                    }
                }
                return total / trials;
            }
            for (int n = 0; n < samples; n++)
            {
                for (int i = 0; i < need; i++)
                {
                    int j = i + rng.NextInt(liveCount - i);
                    (live[i], live[j]) = (live[j], live[i]);
                    full[boardCount + i] = live[i];
                }
                total += Share(hands, hero, full);
            }
            return total / samples;
        }

        static double Share(int[][] hands, int hero, int[] full)
        {
            int best = int.MinValue, winners = 0, mine = 0;
            for (int i = 0; i < hands.Length; i++)
            {
                int sc = HandEvaluator.Evaluate(hands[i][0], hands[i][1], full, 5);
                if (i == hero) mine = sc;
                if (sc > best) { best = sc; winners = 0; }
                if (sc == best) winners++;
            }
            return mine == best ? 1.0 / winners : 0;
        }

        static int[] Live(bool[] dead, out int count)
        {
            var live = new int[52];
            count = 0;
            for (int c = 0; c < 52; c++) if (!dead[c]) live[count++] = c;
            return live;
        }
    }
}
