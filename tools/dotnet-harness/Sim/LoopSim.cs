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

        /// <summary>Day 1: sweep bank -> domestic (starts the 24h lock) -> overseas once unlocked. Shared by C/D/E
        /// per DESIGN.md B11 ("Day 1 새벽에 즉시 옮기면 Day 2 같은 슬롯부터 거래 가능"). Idempotent per slot.</summary>
        static void SweepToOverseas(Loop loop, ref bool deposited)
        {
            var d = loop.Data;
            if (!deposited)
            {
                deposited = true;
                loop.TransferBankToDomestic(d.bankCash);
                return;
            }
            if (!loop.DomesticLocked && d.domesticCash > 0) loop.TransferDomesticToOverseas(d.domesticCash);
        }

        sealed class OneShot25x : IPlayer
        {
            bool _deposited, _entered;
            public string Name => "C";
            public string Description => "코인 25배 올인 롱 1회, 청산까지 보유, 이후 알바만 (Day 1 은행→국내→해외로 자금 이동 후 진입)";
            public void PlaySlot(Loop loop)
            {
                var d = loop.Data;
                if (!_entered)
                {
                    SweepToOverseas(loop, ref _deposited);
                    if (!d.hasPosition && d.overseasCash > 0)
                    {
                        _entered = true;
                        if (!loop.Do(new CoinOpenAction(Direction.Long, 25, d.overseasCash)).Ok) loop.Pass();
                        return;
                    }
                    WorkOrRest(loop);
                    return;
                }
                if (loop.Data.hasPosition) loop.Pass();
                else WorkOrRest(loop);
            }
        }

        sealed class NightlyScalp10x : IPlayer
        {
            bool _deposited;
            public string Name => "D";
            public string Description => "코인 10배 올인, 매일 밤 진입(밤샘) → 다음 행동 가능 슬롯에서 종료 반복. Day 1 즉시 입금, 이후 수익은 해외 지갑에 유지";
            public void PlaySlot(Loop loop)
            {
                var d = loop.Data;
                SweepToOverseas(loop, ref _deposited);
                // Lost slots ("can't get up") are skipped by the Loop, so the first slot we see after a night open
                // is the first one we can act in: dawn normally, afternoon when mental < 15.
                if (d.hasPosition && loop.Do(CoinCloseAction.Instance).Ok) return;
                if (d.slot == Slot.Night && !d.hasPosition && d.overseasCash > 0 &&
                    loop.Do(new CoinOpenAction(Direction.Long, 10, d.overseasCash)).Ok) return;
                loop.Pass();
            }
        }

        /// <summary>Sim E (DESIGN.md B11/B5 §8, report-only): 25x all-in long -> liquidated -> deposit half the bank
        /// balance -> once transferred, 100x all-in -> repeat, alba in between.</summary>
        sealed class LiquidationChain : IPlayer
        {
            bool _deposited;
            int _cyclesEntered;
            public string Name => "E";
            public string Description => "청산 체인: 25배 올인 롱 → 청산되면 은행 잔고 절반 입금 → 전송 가능해지면 100배 올인 → 반복, 알바 병행";
            public void PlaySlot(Loop loop)
            {
                var d = loop.Data;
                if (!_deposited) { SweepToOverseas(loop, ref _deposited); WorkOrRest(loop); return; }
                if (!loop.DomesticLocked && d.domesticCash > 0) loop.TransferDomesticToOverseas(d.domesticCash);

                if (!d.hasPosition && d.overseasCash > 0)
                {
                    int lev = _cyclesEntered == 0 ? 25 : 100;
                    if (loop.Do(new CoinOpenAction(Direction.Long, lev, d.overseasCash)).Ok) { _cyclesEntered++; return; }
                }
                if (d.hasPosition) { loop.Pass(); return; }

                if (!loop.DomesticLocked && d.domesticCash <= 0 && d.overseasCash <= 0 && d.bankCash > 0)
                    loop.TransferBankToDomestic(d.bankCash / 2);

                WorkOrRest(loop);
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
            // B5 v0.3 liquidation chain (sim E report)
            public int TailLiquidations;
            public int WatchesShown;
            public int LiquidationsDuringLock;
            public int GhostFired;
            public int GhostFiredEnhanced;
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
            o.TailLiquidations = s.tailLiquidations;
            o.WatchesShown = s.watchesShown;
            o.LiquidationsDuringLock = s.liquidationsDuringLock;
            o.GhostFired = s.ghostFired;
            o.GhostFiredEnhanced = s.ghostFiredEnhanced;
            return o;
        }

        static IPlayer Make(string name) => name switch
        {
            "A" => new JobOnly(),
            "B" => new LottoAndJob(),
            "C" => new OneShot25x(),
            "D" => new NightlyScalp10x(),
            "E" => new LiquidationChain(),
            _ => throw new ArgumentException(name),
        };

        /// <summary>Runs all players over seeds 1..runs, writes md/SIM_REPORT.md, returns true if every acceptance criterion holds.</summary>
        public static bool Run(int runs, string reportPath)
        {
            var names = new[] { "A", "B", "C", "D", "E" };
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
            sb.AppendLine($"> 시드 1..{runs}, 플레이어 5종. E(청산 체인)는 수용 기준 없음 — 보고 전용.");
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
            sb.AppendLine();
            sb.AppendLine("## E 청산 체인 (B5 v0.3, 보고 전용 — 수용 기준 없음)");
            sb.AppendLine();
            {
                var e = results["E"];
                long liq = e.Sum(o => o.Liquidations);
                long tail = e.Sum(o => o.TailLiquidations);
                long watches = e.Sum(o => o.WatchesShown);
                long lockLiq = e.Sum(o => o.LiquidationsDuringLock);
                long ghosts = e.Sum(o => o.GhostFired);
                long ghostsEnh = e.Sum(o => o.GhostFiredEnhanced);
                double bankruptE = e.Count(o => o.Bankrupt) / (double)runs;
                double tailRatio = liq > 0 ? tail / (double)liq : 0;
                double watchSurvival = watches > 0 ? (watches - liq) / (double)watches : 0;
                double lockLiqRatio = liq > 0 ? lockLiq / (double)liq : 0;
                double ghostRate = liq > 0 ? ghosts / (double)liq : 0;
                double enhancedRate = ghosts > 0 ? ghostsEnh / (double)ghosts : 0;

                sb.AppendLine("| 항목 | 값 |");
                sb.AppendLine("|---|---|");
                sb.AppendLine($"| 런당 평균 청산 | {(liq / (double)runs).ToString("F2", CultureInfo.InvariantCulture)} |");
                sb.AppendLine($"| 꼬리 청산 비율 (청산 중) | {Pct(tailRatio)} |");
                sb.AppendLine($"| 관전 발생 수 (합계) / 생존 비율 | {watches:N0} / {Pct(watchSurvival)} |");
                sb.AppendLine($"| 잠김 기간 중 발생한 청산 비율 | {Pct(lockLiqRatio)} |");
                sb.AppendLine($"| 청산 대비 E8(본절) 발생률 | {Pct(ghostRate)} |");
                sb.AppendLine($"| E8 중 강화 변형 비율 | {Pct(enhancedRate)} |");
                sb.AppendLine($"| 30일 파산률 (총자산 < {Won(Rules.BankruptBelow)}) | {Pct(bankruptE)} |");
            }

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
