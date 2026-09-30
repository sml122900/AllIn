using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AllIn.Core;
using AllIn.Core.Actions;

namespace AllIn.Harness.Sim
{
    /// <summary>Report-only comparison of the mental-recovery candidates V0~V3 (DESIGN.md D9): sim G "버티기" + side effects on A~E.</summary>
    public static class MentalSim
    {
        static readonly string[] Variants = { "V0", "V1", "V2", "V3" };
        const int Grace = Rules.BankruptForcedAfterDays;

        public sealed class HoldResult { public bool Success; public int Mental; }

        /// <summary>Starts on the dawn of the Day 31 rent with an empty bank (nothing anywhere): the arrears are declared, E9 shows.
        /// Mental is set at the declaration. Bot: evening shift every day (rest when refused), no coin, no lotto.
        /// Success = the arrears are paid within Grace days (the release check runs first each dawn).</summary>
        public static HoldResult HoldOn(long seed, int startMental, string variant)
        {
            var loop = Loop.NewRun(seed);
            loop.PopEvent(); loop.PopEvent();
            loop.Tuning = MentalTuning.ByName(variant);
            loop.ForcedAfterDays = int.MaxValue / 2;     // we stop by hand at held day Grace
            var d = loop.Data;
            loop.BankruptcyEnabled = false;
            int guard = 0;
            while (d.day < 30 && guard++ < 5000) { Zero(d); Step(loop, d); }
            loop.BankruptcyEnabled = true;
            Zero(d);
            while (!loop.IsBankrupt && guard++ < 5000) { Zero(d); Step(loop, d); }
            d.mental = startMental;
            while (loop.IsBankrupt && loop.BankruptDaysHeld < Grace && guard++ < 20_000) Step(loop, d);
            return new HoldResult { Success = !loop.IsBankrupt, Mental = d.mental };
        }

        static void Zero(SaveData d)
        {
            d.bankCash = 0; d.domesticCash = 0; d.overseasCash = 0; d.pendingTransfers.Clear();
        }

        static void Step(Loop loop, SaveData d)
        {
            d.pendingEvents.Clear();
            if (loop.Finished) loop.ContinueLife();
            else LoopSim.WorkOrRest(loop);
        }

        static double Median(IEnumerable<int> xs)
        {
            var a = xs.OrderBy(v => v).ToArray();
            return a.Length == 0 ? double.NaN : a[a.Length / 2];
        }

        public static bool Run(int runs, string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# SIM_MENTAL — 멘탈 회복 경로 후보 비교 (보고 전용)");
            sb.AppendLine();
            sb.AppendLine("> 생성: `dotnet run --project tools/dotnet-harness -- sim mental`. 손으로 고치지 말 것. **규칙 기본값은 2026-09-30부터 V3**이고 변형은 비교용 sim 파라미터(`MentalTuning`)로 주입한다.");
            sb.AppendLine("> V0 옛 규칙 / V1 잠 +3→+5 / V2 알바 실수 없이 5일 연속마다 mental +8 (실수·잘림·결근이면 카운트 리셋) / V3 = V1 + V2.");
            sb.AppendLine();
            sb.AppendLine("## G 버티기 — 월세 미납 파산(Day 31, 은행 0원) · 유예 14일 · 각 셀 " + runs + "런");
            sb.AppendLine();
            sb.AppendLine("시작: Day 31 새벽에 은행이 비어 밀린 월세 450,000원으로 파산 선언. 시작 mental만 지정. 봇: 매일 저녁 알바(거절되면 쉬기), 코인·로또 없음, 식비를 못 내면 굶음(body −10, mental −5). 성공 = 14일 안에 밀린 월세 전액 납부. 해제 mental = 해제된 새벽의 mental 중앙값, 실패 mental = 14일째 새벽 mental 중앙값. V0은 참고용(옛 잠 +3·연속 보너스 없음).");
            sb.AppendLine();
            sb.AppendLine("| 시작 mental | 변형 | 성공률 | 해제 시점 mental 중앙 | 실패 런 14일째 mental 중앙 |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (int m in new[] { 20, 30, 40 })
                foreach (var v in new[] { "V3", "V0" }) Row(sb, runs, m, v);

            sb.AppendLine();
            sb.AppendLine("## 부작용 — A~E를 변형별로 (30일 봇, 파산 판정 off)");
            sb.AppendLine();
            sb.AppendLine("| 변형 | A 최종 총자산 (고유값 수) | A 기준 | C 청산율 (≥80%) | D 중앙값 (<20M) | D 희망회로 발동 평균/런 | D 평균 노동 슬롯 | B 평균 노동 슬롯 | E 희망회로 평균/런 |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
            long aExpected = Rules.StartingCash - Rules.Rent - Rules.Days * Rules.FoodPerDay + Rules.Days * Rules.RestaurantWage;
            foreach (var v in Variants)
            {
                string vv = v;
                LoopSim.TuningFactory = () => MentalTuning.ByName(vv);
                var res = new Dictionary<string, List<LoopSim.Outcome>>();
                foreach (var n in new[] { "A", "B", "C", "D", "E" })
                {
                    var l = new List<LoopSim.Outcome>(runs);
                    for (int seed = 1; seed <= runs; seed++) l.Add(LoopSim.Play(LoopSim.Make(n), seed));
                    res[n] = l;
                }
                LoopSim.TuningFactory = null;
                bool aOk = res["A"].All(o => o.Final == aExpected);
                double c = res["C"].Count(o => o.Liquidations > 0) / (double)runs;
                long dMed = LoopSim.Percentile(res["D"].Select(o => o.Final), 0.5);
                sb.AppendLine($"| {v} | {LoopSim.Won(res["A"][0].Final)} ({res["A"].Select(o => o.Final).Distinct().Count()}) | {(aOk ? "PASS" : "FAIL")} | {LoopSim.Pct(c)} {(c >= 0.80 ? "PASS" : "FAIL")} | {LoopSim.Won(dMed)} {(dMed < Rules.StartingCash ? "PASS" : "FAIL")} | " +
                              $"{res["D"].Average(o => o.HopeCircuits):F2} | {res["D"].Average(o => o.WorkedSlots):F1} | {res["B"].Average(o => o.WorkedSlots):F1} | {res["E"].Average(o => o.HopeCircuits):F2} |");
                Console.WriteLine($"mental sim {v}: A {(aOk ? "PASS" : "FAIL")} C {LoopSim.Pct(c)} D {LoopSim.Won(dMed)}");
            }
            System.IO.File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            Console.WriteLine("mental sim → " + path);
            return true;
        }

        static void Row(StringBuilder sb, int runs, int mental, string variant)
        {
            var rs = Enumerable.Range(1, runs).Select(sd => HoldOn(sd, mental, variant)).ToList();
            var ok = rs.Where(r => r.Success).ToList();
            var bad = rs.Where(r => !r.Success).ToList();
            string F(double v) => double.IsNaN(v) ? "—" : v.ToString("F0");
            sb.AppendLine($"| {mental} | {variant} | {LoopSim.Pct(ok.Count / (double)runs)} | {F(Median(ok.Select(r => r.Mental)))} | {F(Median(bad.Select(r => r.Mental)))} |");
        }
    }
}
