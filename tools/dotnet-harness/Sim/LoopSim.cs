using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using AllIn.Core;
using AllIn.Core.Actions;

namespace AllIn.Harness.Sim
{
    /// <summary>Headless 30-day auto-players (DESIGN.md B11). Uses exactly the Core rules the game uses.</summary>
    public static class LoopSim
    {
        public interface IPlayer
        {
            string Name { get; }
            string Description { get; }
            void PlaySlot(Loop loop); // instant actions (lotto) optional, then exactly one slot action
        }

        sealed class JobOnly : IPlayer
        {
            public string Name => "A";
            public string Description => "알바만";
            public void PlaySlot(Loop loop) => WorkOrRest(loop);
        }

        sealed class LottoAndJob : IPlayer
        {
            public string Name => "B";
            public string Description => "로또 매일 2회 + 알바";
            public void PlaySlot(Loop loop)
            {
                if (loop.Data.lottoBoughtToday < 2 && loop.Data.slot != Slot.Night)
                    while (loop.Data.lottoBoughtToday < 2 && loop.Do(LottoBuyAction.Instance).Ok) { }
                WorkOrRest(loop);
            }
        }

        sealed class OneShot25x : IPlayer
        {
            bool _entered;
            public string Name => "C";
            public string Description => "코인 25배 올인 롱 1회, 청산까지 보유, 이후 알바만";
            public void PlaySlot(Loop loop)
            {
                if (!_entered)
                {
                    _entered = true;
                    if (!loop.Do(new CoinOpenAction(Direction.Long, 25, loop.Data.cash)).Ok) loop.Pass(); // opening takes the slot
                    return;
                }
                if (loop.Data.hasPosition) loop.Pass();
                else WorkOrRest(loop);
            }
        }

        sealed class NightlyScalp10x : IPlayer
        {
            public string Name => "D";
            public string Description => "코인 10배 올인, 매일 밤 진입(밤샘) → 다음 행동 가능 슬롯에서 종료 반복";
            public void PlaySlot(Loop loop)
            {
                // Lost slots ("can't get up") are skipped by the Loop, so the first slot we see after a night open
                // is the first one we can act in: dawn normally, afternoon when mental < 15.
                if (loop.Data.hasPosition && loop.Do(CoinCloseAction.Instance).Ok) return;
                if (loop.Data.slot == Slot.Night && !loop.Data.hasPosition && loop.Data.cash > 0 &&
                    loop.Do(new CoinOpenAction(Direction.Long, 10, loop.Data.cash)).Ok) return;
                loop.Pass();
            }
        }

        static void WorkOrRest(Loop loop)
        {
            if (loop.Data.slot == Slot.Evening && loop.Do(WorkRestaurantAction.Instance).Ok) return;
            loop.Pass();
        }

        public sealed class Outcome
        {
            public long Final;
            public int Liquidations;
            public bool Bankrupt;
            public int[] LottoRanks;
            public long LottoSpent, LottoWon;
            public int WorkedSlots;
            public int FirstLiquidationDay; // 0 = never
        }

        public static Outcome Play(IPlayer player, long seed)
        {
            var loop = Loop.NewRun(seed);
            var o = new Outcome();
            int guard = 0;
            while (!loop.Finished)
            {
                if (guard++ > 10_000) throw new InvalidOperationException(player.Name + " stuck on seed " + seed);
                int liq = loop.Data.stats.liquidations;
                player.PlaySlot(loop);
                if (o.FirstLiquidationDay == 0 && loop.Data.stats.liquidations > liq) o.FirstLiquidationDay = loop.Data.day;
                loop.Data.pendingEvents.Clear(); // nobody is watching
            }
            var s = loop.Data.stats;
            o.Final = loop.TotalAssets;
            o.Liquidations = s.liquidations;
            o.Bankrupt = o.Final < Rules.BankruptBelow;
            o.LottoRanks = (int[])s.lottoRanks.Clone();
            o.LottoSpent = s.lottoSpent;
            o.LottoWon = s.lottoWon;
            o.WorkedSlots = s.workedSlots;
            return o;
        }

        static IPlayer Make(string name) => name switch
        {
            "A" => new JobOnly(),
            "B" => new LottoAndJob(),
            "C" => new OneShot25x(),
            "D" => new NightlyScalp10x(),
            _ => throw new ArgumentException(name),
        };

        /// <summary>Runs all players over seeds 1..runs, writes md/SIM_REPORT.md, returns true if every acceptance criterion holds.</summary>
        public static bool Run(int runs, string reportPath)
        {
            var names = new[] { "A", "B", "C", "D" };
            var results = new Dictionary<string, List<Outcome>>();
            var descriptions = new Dictionary<string, string>();
            foreach (var n in names)
            {
                descriptions[n] = Make(n).Description;
                var list = new List<Outcome>(runs);
                for (int seed = 1; seed <= runs; seed++) list.Add(Play(Make(n), seed));
                results[n] = list;
            }

            long aExpected = Rules.StartingCash - Rules.Rent - Rules.Days * Rules.FoodPerDay + Rules.Days * Rules.RestaurantWage;
            bool aOk = results["A"].All(o => o.Final == aExpected);
            double cLiqRate = results["C"].Count(o => o.Liquidations > 0) / (double)runs;
            bool cOk = cLiqRate >= 0.80;
            long dMedian = Percentile(results["D"].Select(o => o.Final), 0.5);
            bool dOk = dMedian < Rules.StartingCash;

            var sb = new StringBuilder();
            sb.AppendLine("# SIM_REPORT — 30일 루프");
            sb.AppendLine();
            sb.AppendLine("> 생성: `dotnet run --project tools/dotnet-harness -- sim loop`. 손으로 고치지 말 것. 수치 출처는 md/DESIGN.md.");
            sb.AppendLine($"> 시드 1..{runs}, 플레이어 4종.");
            sb.AppendLine();
            sb.AppendLine("## 수용 기준");
            sb.AppendLine();
            sb.AppendLine("| 기준 | 결과 | 판정 |");
            sb.AppendLine("|---|---|---|");
            sb.AppendLine($"| A 결정론적: 전 런 최종 총자산 = {Won(aExpected)} (상수에서 계산) | {results["A"].Select(o => o.Final).Distinct().Count()}개 값, 첫 값 {Won(results["A"][0].Final)} | {Pass(aOk)} |");
            sb.AppendLine($"| C 30일 내 청산 ≥ 80% | {Pct(cLiqRate)} | {Pass(cOk)} |");
            sb.AppendLine($"| D 최종 총자산 중앙값 < 20,000,000 | {Won(dMedian)} | {Pass(dOk)} |");
            sb.AppendLine("| 사람 플레이 1런 10~15분 | 사람 확인 | — |");
            sb.AppendLine();
            sb.AppendLine("## 플레이어별 분포");
            sb.AppendLine();
            sb.AppendLine("| 플레이어 | 전략 | 중앙값 | p10 | p90 | 평균 청산 | 파산(< 5,000) | 평균 노동 슬롯 |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|");
            foreach (var n in names)
            {
                var r = results[n];
                var finals = r.Select(o => o.Final).ToList();
                sb.AppendLine($"| {n} | {descriptions[n]} | {Won(Percentile(finals, 0.5))} | {Won(Percentile(finals, 0.1))} | {Won(Percentile(finals, 0.9))} | " +
                              $"{r.Average(o => o.Liquidations).ToString("F2", CultureInfo.InvariantCulture)} | {Pct(r.Count(o => o.Bankrupt) / (double)runs)} | " +
                              $"{r.Average(o => o.WorkedSlots).ToString("F1", CultureInfo.InvariantCulture)} |");
            }
            sb.AppendLine();
            sb.AppendLine("## 로또 등수 분포 (B, 전 런 합계)");
            sb.AppendLine();
            var b = results["B"];
            long games = b.Sum(o => o.LottoSpent) / Rules.LottoPrice * Rules.LottoGamesPerTicket;
            sb.AppendLine($"구매 {games:N0}게임 / 지출 {Won(b.Sum(o => o.LottoSpent))} (Day 28~30 구매분은 기간 내 미추첨) / 당첨(세후) 합계 {Won(b.Sum(o => o.LottoWon))}.");
            sb.AppendLine();
            sb.AppendLine("| 등수 | 횟수 | 런당 평균 |");
            sb.AppendLine("|---|---|---|");
            for (int rank = 1; rank <= 5; rank++)
            {
                int c = b.Sum(o => o.LottoRanks[rank]);
                sb.AppendLine($"| {rank}등 | {c:N0} | {(c / (double)runs).ToString("F3", CultureInfo.InvariantCulture)} |");
            }
            sb.AppendLine();
            sb.AppendLine("## C 첫 청산 일자 분포");
            sb.AppendLine();
            sb.AppendLine("| 구간 | 비율 |");
            sb.AppendLine("|---|---|");
            foreach (var (lo, hi) in new[] { (1, 1), (2, 3), (4, 7), (8, 14), (15, 30) })
                sb.AppendLine($"| Day {lo}–{hi} | {Pct(results["C"].Count(o => o.FirstLiquidationDay >= lo && o.FirstLiquidationDay <= hi) / (double)runs)} |");
            sb.AppendLine($"| 청산 없음 | {Pct(results["C"].Count(o => o.FirstLiquidationDay == 0) / (double)runs)} |");

            Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
            File.WriteAllText(reportPath, sb.ToString().Replace("\r\n", "\n"));

            Console.WriteLine($"loop sim: A {Pass(aOk)} | C liquidated {Pct(cLiqRate)} {Pass(cOk)} | D median {Won(dMedian)} {Pass(dOk)} → {reportPath}");
            return aOk && cOk && dOk;
        }

        static long Percentile(IEnumerable<long> values, double p)
        {
            var sorted = values.OrderBy(v => v).ToArray();
            return sorted[(int)Math.Round(p * (sorted.Length - 1))];
        }

        static string Won(long v) => Fmt.Won(v);
        static string Pct(double r) => Fmt.Pct(r);
        static string Pass(bool ok) => ok ? "PASS" : "FAIL";
    }
}
