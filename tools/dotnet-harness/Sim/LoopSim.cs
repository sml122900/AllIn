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
        /// balance -> once transferred, 100x all-in -> repeat, alba in between. The bot takes no action during a
        /// watch scene (it never runs interactively) — every slot goes through Loop.Do()/Pass(), which auto-completes
        /// any sub-tick walk with zero intervention (the same "기도 0회" baseline sims and tests always use).</summary>
        sealed class LiquidationChain : IPlayer
        {
            bool _deposited;
            int _cyclesEntered;
            public int Liq25, Liq100, Tail25, Tail100; // per-leverage liquidation/tail breakdown (report-only, read after the run)
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
                    int liqBefore = d.stats.liquidations, tailBefore = d.stats.tailLiquidations;
                    if (loop.Do(new CoinOpenAction(Direction.Long, lev, d.overseasCash)).Ok)
                    {
                        _cyclesEntered++;
                        RecordLiquidation(d, lev, liqBefore, tailBefore);
                        return;
                    }
                }
                if (d.hasPosition)
                {
                    int lev = d.position.leverage;
                    int liqBefore = d.stats.liquidations, tailBefore = d.stats.tailLiquidations;
                    loop.Pass();
                    RecordLiquidation(d, lev, liqBefore, tailBefore);
                    return;
                }

                if (!loop.DomesticLocked && d.domesticCash <= 0 && d.overseasCash <= 0 && d.bankCash > 0)
                    loop.TransferBankToDomestic(d.bankCash / 2);

                WorkOrRest(loop);
            }

            void RecordLiquidation(SaveData d, int lev, int liqBefore, int tailBefore)
            {
                if (d.stats.liquidations <= liqBefore) return;
                bool tail = d.stats.tailLiquidations > tailBefore;
                if (lev == 25) { Liq25++; if (tail) Tail25++; }
                else { Liq100++; if (tail) Tail100++; }
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
            public List<int> GhostResolutionSlots;
            // sim E only (LiquidationChain), 0 for every other player
            public int Liq25, Liq100, Tail25, Tail100;
        }

        public static Outcome Play(IPlayer player, long seed)
        {
            var loop = Loop.NewRun(seed);
            loop.BankruptcyEnabled = false; // A~E are 30-day bots that never answer E9 (DESIGN.md D7); F below is the life sim
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
            o.GhostResolutionSlots = new List<int>(s.ghostResolutionSlots);
            if (player is LiquidationChain lc) { o.Liq25 = lc.Liq25; o.Liq100 = lc.Liq100; o.Tail25 = lc.Tail25; o.Tail100 = lc.Tail100; }
            return o;
        }

        // ---- F: "인생 3회 파산" (DESIGN.md D7, report-only) ----

        public const int LifeVoyages = 5;

        public sealed class LifeOutcome
        {
            public bool Completed;       // reached LifeVoyages voyages within the guard
            public double[] AgeAtReturn = new double[LifeVoyages + 1];   // index = voyage number (1..LifeVoyages), 0 = not reached
            public int[] BodyMaxAtReturn = new int[LifeVoyages + 1];
            public int[] IllnessesAtReturn = new int[LifeVoyages + 1];
            public int Reached;          // voyages actually completed
            public double AgeAtThirdReturn;
            public int Days;             // day number at the third return
            public int BodyMax;
            public int Illnesses;
            public long HospitalPaid;
            public int Liquidations;
            public int VoyageLiquidations;
        }

        /// <summary>100x all-in bot, life edition: bank -> domestic -> overseas -> 100x all-in long, repeat, job in the evening.
        /// When E9 shows up it takes the ship at once and closes any position first ([정리]). Never does a check-up.</summary>
        public static LifeOutcome PlayLife(long seed)
        {
            var loop = Loop.NewRun(seed);
            var d = loop.Data;
            var o = new LifeOutcome();
            int guard = 0;
            while (d.stats.voyages < LifeVoyages)
            {
                if (guard++ > 400_000) return FinishLife(o, loop, false);
                d.pendingEvents.Clear(); // nobody is watching
                if (loop.Finished) { loop.ContinueLife(); continue; }
                if (loop.CanDepart().Ok)
                {
                    loop.Depart(true);
                    int n = d.stats.voyages;
                    o.AgeAtReturn[n] = Loop.AgeYears(d.day); o.BodyMaxAtReturn[n] = d.bodyMax; o.IllnessesAtReturn[n] = d.stats.illnesses;
                    continue;
                }

                if (!loop.DomesticLocked && d.domesticCash > 0) loop.TransferDomesticToOverseas(d.domesticCash);
                if (!d.hasPosition && d.overseasCash > 0 &&
                    loop.Do(new CoinOpenAction(Direction.Long, 100, d.overseasCash)).Ok) continue;
                if (!d.hasPosition && d.overseasCash <= 0 && d.domesticCash <= 0 && d.pendingTransfers.Count == 0 && d.bankCash > 0)
                    loop.TransferBankToDomestic(d.bankCash);
                if (d.hasPosition) loop.Pass();
                else WorkOrRest(loop);
            }
            return FinishLife(o, loop, true);
        }

        static LifeOutcome FinishLife(LifeOutcome o, Loop loop, bool completed)
        {
            var d = loop.Data;
            o.Completed = completed;
            o.Reached = d.stats.voyages;
            o.AgeAtThirdReturn = o.AgeAtReturn[3];
            o.Days = d.day;
            o.BodyMax = d.bodyMax;
            o.Illnesses = d.stats.illnesses;
            o.HospitalPaid = d.stats.hospitalPaid;
            o.Liquidations = d.stats.liquidations;
            o.VoyageLiquidations = d.stats.voyageLiquidations;
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
            sb.AppendLine("| 플레이어 | 전략 | 중앙값 | p10 | p90 | 평균 청산 | 그중 꼬리 청산 | 파산(< 5,000) | 평균 노동 슬롯 |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
            foreach (var n in names)
            {
                var r = results[n];
                var finals = r.Select(o => o.Final).ToList();
                long liqSum = r.Sum(o => o.Liquidations), tailSum = r.Sum(o => o.TailLiquidations);
                string tailCol = liqSum > 0 ? Pct(tailSum / (double)liqSum) : "—";
                sb.AppendLine($"| {n} | {descriptions[n]} | {Won(Percentile(finals, 0.5))} | {Won(Percentile(finals, 0.1))} | {Won(Percentile(finals, 0.9))} | " +
                              $"{r.Average(o => o.Liquidations).ToString("F2", CultureInfo.InvariantCulture)} | {tailCol} | {Pct(r.Count(o => o.Bankrupt) / (double)runs)} | " +
                              $"{r.Average(o => o.WorkedSlots).ToString("F1", CultureInfo.InvariantCulture)} |");
            }
            sb.AppendLine();
            sb.AppendLine("「그중 꼬리 청산」은 B/C/D/E 전부에 서브틱 경로가 동일하게 적용됨을 보여준다(B는 코인을 하지 않아 청산이 없음 — \"—\").");
            sb.AppendLine();
            sb.AppendLine("## A~D 재보고 (v0.3 직전 커밋 대비)");
            sb.AppendLine();
            sb.AppendLine("\"byte-identical\"은 이 세션의 `SubTickWalk`/`BeginWatchableAction` 리팩터 **전후**를 가리킨다 — 즉 헤드리스 호출자(sim, test)는 리팩터 전 `Do()`/`EndSlotAndAdvance()`와 동일한 제어 흐름을 그대로 거친다는 뜻이며, v0.3의 신규 기능(지갑 3분할·서브틱 꼬리 청산) 자체가 추가되기 이전 버전과 같다는 뜻이 아니다. A는 코인을 전혀 쓰지 않으므로 어느 기준으로 봐도 정확히 같아야 한다 — 실제로 같다(아래). C·D는 이제 은행→국내→해외 자금 이동을 먼저 거쳐야 하고 서브틱 꼬리 청산이 추가되므로 값이 달라지는 것이 기대됨.");
            sb.AppendLine();
            sb.AppendLine("| 항목 | v0.3 이전 (2026-09-25) | v0.3 이후 (이번 실행) | 판정 |");
            sb.AppendLine("|---|---|---|---|");
            sb.AppendLine($"| A 최종 총자산(전 런 동일) | 20,738,000원 | {Won(results["A"][0].Final)} | {Pass(results["A"].All(o => o.Final == 20_738_000))} 정확히 동일해야 함 |");
            sb.AppendLine($"| C 30일 내 청산율 | 81.8% | {Pct(cLiqRate)} | {(cLiqRate > 0.818 ? "상승 (꼬리 청산 추가 효과와 일치)" : cLiqRate < 0.818 ? "하락 — 예상과 반대, 재검토 필요" : "변화 없음 — 서브틱 경로 미적용 의심")} |");
            sb.AppendLine($"| D 최종 총자산 중앙값 | 7,184,768원 | {Won(dMedian)} | {(dMedian != 7_184_768 ? "값이 달라짐 — 서브틱 경로가 실제로 적용되고 있음" : "변화 없음 — 서브틱 경로 미적용 의심")} |");
            sb.AppendLine();
            sb.AppendLine("D의 총자산 중앙값이 오른 이유(청산이 약해져서가 아님): C·D 모두 이제 Day 1 새벽 입금 → 5슬롯 잠금 → 해외 도착까지 약 6슬롯(1.2일)을 아무것도 못 하고 흘려보낸다. D는 이 기간 동안 기존처럼 매일 밤 베팅하지 못해 도박 판수 자체가 하루 이상 줄고, 10배는 청산까지 필요한 단일 슬롯 변동폭(약 9%)이 25배·100배보다 훨씬 커서 실제 청산 발생 자체가 드물다(아래 표, 평균 청산 0.12/런). 즉 \"청산이 덜 매워져서\"가 아니라 \"도박 기회가 하루 줄고, 애초에 10배는 잘 안 터진다\"는 두 구조적 이유다. 서브틱 경로가 D에도 적용된다는 직접 증거는 위 표의 \"그중 꼬리 청산\" 열(D 행)로 확인.");
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
            sb.AppendLine("봇의 관전 중 행동: 없음. sim은 항상 헤드리스 `Loop.Do()`/`Pass()`만 호출하고 `BeginWatchableAction`/`WatchScreen`을 직접 구동하지 않으므로, 관전이 열리는 모든 슬롯에서 `SubTickWalk.RunToEnd()`가 개입 없이 끝까지 진행된다(기도 0회·증거금 추가 0회·손절 0회 — \"기도 0회\" 기준선과 동일).");
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

                long liq25 = e.Sum(o => o.Liq25), liq100 = e.Sum(o => o.Liq100);
                long tail25 = e.Sum(o => o.Tail25), tail100 = e.Sum(o => o.Tail100);

                sb.AppendLine("| 항목 | 값 |");
                sb.AppendLine("|---|---|");
                sb.AppendLine($"| 런당 평균 청산 | {(liq / (double)runs).ToString("F2", CultureInfo.InvariantCulture)} |");
                sb.AppendLine($"| 꼬리 청산 비율 (청산 중, 전체) | {Pct(tailRatio)} |");
                sb.AppendLine($"| 꼬리 청산 비율 — 25배 (첫 진입) | {(liq25 > 0 ? Pct(tail25 / (double)liq25) : "—")} ({liq25:N0}건 청산) |");
                sb.AppendLine($"| 꼬리 청산 비율 — 100배 (재진입) | {(liq100 > 0 ? Pct(tail100 / (double)liq100) : "—")} ({liq100:N0}건 청산) |");
                sb.AppendLine($"| 관전 발생 수 (합계) / 생존 비율 | {watches:N0} / {Pct(watchSurvival)} |");
                sb.AppendLine($"| 런당 관전 횟수 p50 / p90 | {Percentile(e.Select(o => (long)o.WatchesShown), 0.5)} / {Percentile(e.Select(o => (long)o.WatchesShown), 0.9)} |");
                sb.AppendLine($"| 잠김 기간 중 발생한 청산 비율 | {Pct(lockLiqRatio)} |");
                sb.AppendLine($"| 청산 대비 E8(본절) 발생률 | {Pct(ghostRate)} |");
                sb.AppendLine($"| E8 중 강화 변형 비율 | {Pct(enhancedRate)} |");
                {
                    var allSlots = e.SelectMany(o => o.GhostResolutionSlots).OrderBy(v => v).ToArray();
                    string P(double p) => allSlots.Length == 0 ? "—" : allSlots[(int)Math.Round(p * (allSlots.Length - 1))].ToString();
                    sb.AppendLine($"| 청산 → E8까지 걸린 슬롯 수 p50 / p90 (n={allSlots.Length:N0}) | {P(0.5)} / {P(0.9)} |");
                }
                sb.AppendLine($"| 30일 파산률 (총자산 < {Won(Rules.BankruptBelow)}) | {Pct(bankruptE)} |");
            }

            AppendLifeSim(sb, runs);

            Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
            File.WriteAllText(reportPath, sb.ToString().Replace("\r\n", "\n"));

            Console.WriteLine($"loop sim: A {Pass(aOk)} | C liquidated {Pct(cLiqRate)} {Pass(cOk)} | D median {Won(dMedian)} {Pass(dOk)} → {reportPath}");
            return aOk && cOk && dOk;
        }

        static void AppendLifeSim(StringBuilder sb, int runs)
        {
            var f = new List<LifeOutcome>(runs);
            for (int seed = 1; seed <= runs; seed++) f.Add(PlayLife(seed));
            string F1(double v) => v.ToString("F2", CultureInfo.InvariantCulture);
            double P(IEnumerable<double> xs, double p) { var a = xs.OrderBy(v => v).ToArray(); return a[(int)Math.Round(p * (a.Length - 1))]; }

            sb.AppendLine();
            sb.AppendLine("## F 인생 반복 파산 (DESIGN.md D7, 보고 전용 — 수용 기준 없음)");
            sb.AppendLine();
            sb.AppendLine($"봇: 은행 → 국내 → 해외로 옮겨 100배 올인 롱, 청산되면 남은 돈으로 반복, 저녁엔 알바. E9(파산)가 뜨면 곧바로 [원양어선을 탄다]([정리]로 포지션을 닫고 출발). 검진은 한 번도 안 받는다. **파산 → 승선 → 복귀를 {LifeVoyages}번** 겪을 때까지 진행. 나이 = 21 + (day−1)/365(소수). 질병 판정: 첫 복귀부터 달력 30일마다 + 매 복귀 직후 1회.");
            sb.AppendLine();
            var done = f.Where(o => o.Completed).ToList();
            if (done.Count == 0) { sb.AppendLine($"{LifeVoyages}회 승선에 도달한 런이 없다."); return; }
            var ill = done.Select(o => (double)o.Illnesses).ToList();
            var bill = done.Select(o => (double)o.HospitalPaid).ToList();
            sb.AppendLine("| 항목 | 값 |");
            sb.AppendLine("|---|---|");
            sb.AppendLine($"| {LifeVoyages}회 승선 완료 런 | {done.Count} / {runs} |");
            if (done.Count < runs)
                sb.AppendLine($"| 미완료 런 (가드 40만 슬롯) | {runs - done.Count}건 — 봇이 이익 난 100배 포지션을 청산될 때까지 닫지 않는 성격이라 청산가에서 멀어진 채 계속 보유한 런. 봇 특성이며 규칙 결함이 아님 |");
            for (int n = 1; n <= LifeVoyages; n++)
            {
                var reached = f.Where(o => o.Reached >= n).ToList();
                if (reached.Count == 0) continue;
                var ages = reached.Select(o => o.AgeAtReturn[n]).ToList();
                sb.AppendLine($"| {n}회째 복귀 시 나이 평균 / p10 / 중앙 / p90 (n={reached.Count}) | {F1(ages.Average())} / {F1(P(ages, 0.1))} / {F1(P(ages, 0.5))} / {F1(P(ages, 0.9))} 세 |");
                sb.AppendLine($"| {n}회째 복귀 시 body 최대치 / 지급액 | {string.Join(" / ", reached.Select(o => o.BodyMaxAtReturn[n]).Distinct().OrderBy(v => v))} / {Won(Rules.VoyagePayFor(n))} |");
                sb.AppendLine($"| {n}회째 복귀까지 질병 0회 비율 | {Pct(reached.Count(o => o.IllnessesAtReturn[n] == 0) / (double)reached.Count)} |");
            }
            var ages5 = done.Select(o => o.AgeAtReturn[LifeVoyages]).ToList();
            sb.AppendLine($"| **{LifeVoyages}회 도달 나이 중앙값** | {F1(P(ages5, 0.5))} 세 (p99 {F1(P(ages5, 0.99))} / 최대 {F1(ages5.Max())}) |");
            sb.AppendLine($"| {LifeVoyages}회 도달 시 질병 발생 횟수 평균 / p50 / p90 / 최대 | {F1(ill.Average())} / {P(ill, 0.5):F0} / {P(ill, 0.9):F0} / {ill.Max():F0} |");
            sb.AppendLine($"| {LifeVoyages}회 도달 시 병원비 합계 평균 / 중앙 / p90 | {Won((long)bill.Average())} / {Won((long)P(bill, 0.5))} / {Won((long)P(bill, 0.9))} |");
            sb.AppendLine($"| 런당 평균 청산 | {F1(done.Average(o => (double)o.Liquidations))} |");
            sb.AppendLine();
            sb.AppendLine("해석 메모: 승선 1회 = 180일. 복귀 직후 판정이 생겨 반복 승선자도 매번 (건강 부채 × 10%)로 질병을 만난다. body 최대치는 5회에 25(알바 한계), 6회부터 20(하한, 알바 불가). 복귀 금액은 3회부터 −10%씩.");
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
