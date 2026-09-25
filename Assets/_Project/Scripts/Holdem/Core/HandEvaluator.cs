namespace AllIn.Holdem
{
    public enum HandCategory { HighCard = 0, Pair, TwoPair, Trips, Straight, Flush, FullHouse, Quads, StraightFlush }

    /// <summary>
    /// Best 5-card hand out of 5–7 cards, as a comparable int: higher is better, equal means a split.
    /// Layout: category &lt;&lt; 20 | five 4-bit rank slots (most significant first). Bitmask based — no 21-combo loop,
    /// because the AI's Monte Carlo calls this hundreds of thousands of times per tournament.
    /// </summary>
    public static class HandEvaluator
    {
        public static int Evaluate(int[] cards) => Evaluate(cards, cards.Length);

        public static int Evaluate(int[] cards, int count)
        {
            int s0 = 0, s1 = 0, s2 = 0, s3 = 0;
            for (int i = 0; i < count; i++)
            {
                int c = cards[i], bit = 1 << (c >> 2);
                switch (c & 3)
                {
                    case 0: s0 |= bit; break;
                    case 1: s1 |= bit; break;
                    case 2: s2 |= bit; break;
                    default: s3 |= bit; break;
                }
            }
            return EvaluateMasks(s0, s1, s2, s3);
        }

        /// <summary>Two hole cards + board, without allocating.</summary>
        public static int Evaluate(int h0, int h1, int[] board, int boardCount)
        {
            int s0 = 0, s1 = 0, s2 = 0, s3 = 0;
            Add(h0, ref s0, ref s1, ref s2, ref s3);
            Add(h1, ref s0, ref s1, ref s2, ref s3);
            for (int i = 0; i < boardCount; i++) Add(board[i], ref s0, ref s1, ref s2, ref s3);
            return EvaluateMasks(s0, s1, s2, s3);
        }

        static void Add(int c, ref int s0, ref int s1, ref int s2, ref int s3)
        {
            int bit = 1 << (c >> 2);
            switch (c & 3)
            {
                case 0: s0 |= bit; break;
                case 1: s1 |= bit; break;
                case 2: s2 |= bit; break;
                default: s3 |= bit; break;
            }
        }

        static int EvaluateMasks(int s0, int s1, int s2, int s3)
        {
            int flushMask = PopCount(s0) >= 5 ? s0 : PopCount(s1) >= 5 ? s1 : PopCount(s2) >= 5 ? s2 : PopCount(s3) >= 5 ? s3 : 0;
            if (flushMask != 0)
            {
                int sf = StraightTop(flushMask);
                if (sf >= 0) return Score(HandCategory.StraightFlush, sf, 0, 0, 0, 0);
            }

            // rank counts, scanned from ace down
            int quad = -1, trip1 = -1, trip2 = -1, pair1 = -1, pair2 = -1, pair3 = -1;
            int singles = 0; // bitmask of ranks seen exactly once
            for (int r = 12; r >= 0; r--)
            {
                int bit = 1 << r;
                int n = ((s0 & bit) != 0 ? 1 : 0) + ((s1 & bit) != 0 ? 1 : 0) + ((s2 & bit) != 0 ? 1 : 0) + ((s3 & bit) != 0 ? 1 : 0);
                switch (n)
                {
                    case 4: quad = r; break;
                    case 3: if (trip1 < 0) trip1 = r; else if (trip2 < 0) trip2 = r; break;
                    case 2: if (pair1 < 0) pair1 = r; else if (pair2 < 0) pair2 = r; else if (pair3 < 0) pair3 = r; break;
                    case 1: singles |= bit; break;
                }
            }
            int all = s0 | s1 | s2 | s3;

            if (quad >= 0) return Score(HandCategory.Quads, quad, TopBits(all & ~(1 << quad), 1), 0, 0, 0);
            if (trip1 >= 0 && (trip2 >= 0 || pair1 >= 0))
                return Score(HandCategory.FullHouse, trip1, trip2 > pair1 ? trip2 : pair1, 0, 0, 0);
            if (flushMask != 0) return ScoreBits(HandCategory.Flush, flushMask, 5);
            int st = StraightTop(all);
            if (st >= 0) return Score(HandCategory.Straight, st, 0, 0, 0, 0);
            if (trip1 >= 0)
            {
                int rest = all & ~(1 << trip1);
                return Score(HandCategory.Trips, trip1, NthBit(rest, 0), NthBit(rest, 1), 0, 0);
            }
            if (pair2 >= 0)
            {
                int rest = all & ~(1 << pair1) & ~(1 << pair2); // a third pair's rank can be the kicker
                return Score(HandCategory.TwoPair, pair1, pair2, NthBit(rest, 0), 0, 0);
            }
            if (pair1 >= 0)
            {
                int rest = all & ~(1 << pair1);
                return Score(HandCategory.Pair, pair1, NthBit(rest, 0), NthBit(rest, 1), NthBit(rest, 2), 0);
            }
            return ScoreBits(HandCategory.HighCard, all, 5);
        }

        public static HandCategory Category(int score) => (HandCategory)(score >> 20);

        static int Score(HandCategory c, int a, int b, int d, int e, int f) => ((int)c << 20) | (a << 16) | (b << 12) | (d << 8) | (e << 4) | f;

        static int ScoreBits(HandCategory c, int mask, int n)
        {
            int score = (int)c << 20, shift = 16;
            for (int r = 12; r >= 0 && n > 0; r--)
            {
                if ((mask & (1 << r)) == 0) continue;
                score |= r << shift;
                shift -= 4;
                n--;
            }
            return score;
        }

        /// <summary>Highest rank index of the n-th set bit from the top (0 = highest); 0 if absent.</summary>
        static int NthBit(int mask, int n)
        {
            for (int r = 12; r >= 0; r--)
            {
                if ((mask & (1 << r)) == 0) continue;
                if (n-- == 0) return r;
            }
            return 0;
        }

        static int TopBits(int mask, int n) => NthBit(mask, n - 1);

        /// <summary>Top rank index of the best straight in a 13-bit rank mask, or −1. The wheel (A-2-3-4-5) tops at 3 (the five).</summary>
        static int StraightTop(int mask)
        {
            for (int top = 12; top >= 4; top--)
            {
                int run = 0x1F << (top - 4);
                if ((mask & run) == run) return top;
            }
            const int wheel = (1 << 12) | 0xF; // A,2,3,4,5
            return (mask & wheel) == wheel ? 3 : -1;
        }

        static int PopCount(int x)
        {
            x -= (x >> 1) & 0x55555555;
            x = (x & 0x33333333) + ((x >> 2) & 0x33333333);
            return (((x + (x >> 4)) & 0x0F0F0F0F) * 0x01010101) >> 24;
        }
    }
}
