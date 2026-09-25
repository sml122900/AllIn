using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AllIn.Core;
using AllIn.Holdem;

namespace AllIn.Harness
{
    /// <summary>
    /// One-off generator for Holdem/Brain/HandRanks.cs: equity of each of the 169 starting-hand classes against one random
    /// hand, sorted. `dotnet run --project tools/dotnet-harness -- gen169 [samples]` prints the C# array literal.
    /// </summary>
    public static class Gen169
    {
        public static int Run(string[] args)
        {
            int samples = args.Length > 0 ? int.Parse(args[0]) : 200_000;
            var rng = new Xoshiro128(169);
            var rows = new List<(string name, double eq)>();
            for (int hi = 12; hi >= 0; hi--)
            for (int lo = hi; lo >= 0; lo--)
            {
                if (hi == lo) rows.Add((Name(hi, lo, false), Eq(Card.Make(hi, 0), Card.Make(lo, 1), rng, samples)));
                else
                {
                    rows.Add((Name(hi, lo, true), Eq(Card.Make(hi, 0), Card.Make(lo, 0), rng, samples)));
                    rows.Add((Name(hi, lo, false), Eq(Card.Make(hi, 0), Card.Make(lo, 1), rng, samples)));
                }
            }
            var sorted = rows.OrderByDescending(r => r.eq).ToList();
            var sb = new StringBuilder();
            for (int i = 0; i < sorted.Count; i++)
            {
                sb.Append('"').Append(sorted[i].name).Append("\", ");
                if (i % 13 == 12) sb.AppendLine();
            }
            Console.WriteLine(sb.ToString());
            Console.WriteLine("// top: " + string.Join(", ", sorted.Take(5).Select(r => r.name + "=" + r.eq.ToString("F3"))));
            Console.WriteLine("// bottom: " + string.Join(", ", sorted.Skip(164).Select(r => r.name + "=" + r.eq.ToString("F3"))));
            return 0;
        }

        static double Eq(int a, int b, IRng rng, int samples) => Equity.VsRandom(a, b, new int[5], 0, 1, rng, samples);

        static string Name(int hi, int lo, bool suited) =>
            hi == lo ? "" + Card.Ranks[hi] + Card.Ranks[lo] : "" + Card.Ranks[hi] + Card.Ranks[lo] + (suited ? "s" : "o");
    }
}
