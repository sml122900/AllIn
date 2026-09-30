using System;

namespace AllIn.Holdem.Brain
{
    public enum TablePosition { UTG = 0, MP, CO, BTN, SB, BB }

    /// <summary>
    /// Push/fold ranges for 3–35bb (columns 20/25/35 added for the v2 ladder; the v1 bots still only use ≤ 15bb), top % of hands (DESIGN.md C4). Nash-ish approximations — the only hard
    /// requirement is monotonicity: later position → wider, shorter stack → wider. Values between columns are interpolated.
    /// </summary>
    public static class PushFoldTable
    {
        public static readonly int[] StackColumns = { 3, 5, 7, 10, 15, 20, 25, 35 };

        // rows: UTG, MP, CO, BTN, SB (opening shove)
        static readonly double[,] Push =
        {
            { 40, 25, 18, 13, 9, 7, 6, 4 },
            { 48, 30, 22, 16, 11, 9, 7, 5 },
            { 60, 40, 30, 22, 16, 13, 10, 7 },
            { 75, 55, 42, 32, 24, 19, 15, 11 },
            { 90, 70, 55, 43, 33, 27, 22, 16 },
        };

        // rows: position of the shover being called
        static readonly double[,] Call =
        {
            { 30, 18, 13, 10, 7, 5.5, 4.5, 3.5 },
            { 35, 21, 15, 12, 8, 6.5, 5, 4 },
            { 42, 26, 19, 14, 10, 8, 6.5, 5 },
            { 50, 32, 24, 18, 12, 10, 8, 6 },
            { 60, 40, 30, 22, 15, 12, 10, 8 },
        };

        /// <summary>Open-shove range (top %) for this position and effective stack in big blinds.</summary>
        public static double PushPercent(TablePosition pos, double bb) => Lookup(Push, Row(pos), bb);

        /// <summary>Calling range (top %) against a shove from <paramref name="shover"/>.</summary>
        public static double CallPercent(TablePosition shover, double bb) => Lookup(Call, Row(shover), bb);

        static int Row(TablePosition p) => p == TablePosition.BB ? (int)TablePosition.SB : (int)p; // BB never opens; treat like SB

        static double Lookup(double[,] t, int row, double bb)
        {
            var cols = StackColumns;
            if (bb <= cols[0]) return t[row, 0];
            if (bb >= cols[cols.Length - 1]) return t[row, cols.Length - 1];
            for (int i = 1; i < cols.Length; i++)
            {
                if (bb > cols[i]) continue;
                double f = (bb - cols[i - 1]) / (cols[i] - cols[i - 1]);
                return t[row, i - 1] + (t[row, i] - t[row, i - 1]) * f;
            }
            return t[row, cols.Length - 1];
        }
    }

    public static class Positions
    {
        /// <summary>
        /// Labels every seat in the hand relative to the button: BTN, SB, BB, then counting back from the button
        /// CO, MP, MP, and UTG for the rest. Heads-up: the button is the SB.
        /// </summary>
        public static TablePosition[] Assign(HandState h)
        {
            var pos = new TablePosition[HandState.Seats];
            var order = new int[HandState.Seats];
            int n = 0;
            for (int k = 0; k < HandState.Seats; k++)
            {
                int s = (h.Button + k) % HandState.Seats;
                if (h.InHand[s]) order[n++] = s;
            }
            if (n == 2)
            {
                pos[order[0]] = TablePosition.SB;
                pos[order[1]] = TablePosition.BB;
                return pos;
            }
            pos[order[0]] = TablePosition.BTN;
            pos[order[1]] = TablePosition.SB;
            pos[order[2]] = TablePosition.BB;
            // order[3..n-1] act first..last before the button
            for (int i = 3; i < n; i++)
            {
                int fromButton = n - i; // 1 = CO
                pos[order[i]] = fromButton == 1 ? TablePosition.CO : fromButton <= 3 ? TablePosition.MP : TablePosition.UTG;
            }
            return pos;
        }

        public static TablePosition Of(HandState h, int seat) => Assign(h)[seat];

        public static string Label(TablePosition p) => p.ToString();
    }

    public static class RangeMath
    {
        public static double Scaled(double percent, double multiplier) => Math.Min(100, percent * multiplier);
    }
}
