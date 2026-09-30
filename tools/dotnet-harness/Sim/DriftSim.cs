using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using AllIn.Core;

namespace AllIn.Harness.Sim
{
    /// <summary>Price-drift check (report-only): is E[P(t+1)/P(t)] = 1, and does the long side have an edge over the short side?</summary>
    public static class DriftSim
    {
        const long Samples = 10_000_000;

        static string F(double v, int d = 6) => v.ToString("F" + d, CultureInfo.InvariantCulture);

        /// <summary>The correction constant: ln E[e^r] from a fixed-seed Monte Carlo (capped tick log-returns, uncorrected).</summary>
        public static double MeasureC(out double se)
        {
            var rng = new Xoshiro128(20260930UL);
            double sum = 0, sum2 = 0;
            for (long i = 0; i < Samples; i++)
            {
                double e = Math.Exp(StudentTPriceModel.RawLogReturn(rng));
                sum += e; sum2 += e * e;
            }
            double mean = sum / Samples, var = sum2 / Samples - mean * mean;
            se = Math.Sqrt(var / Samples);
            return Math.Log(mean);
        }

        static (double mean, double median, double se) Stats(IEnumerable<long> v)
        {
            var a = v.Select(x => (double)x).OrderBy(x => x).ToArray();
            double m = a.Average();
            double sd = Math.Sqrt(a.Sum(x => (x - m) * (x - m)) / (a.Length - 1));
            return (m, a[a.Length / 2], sd / Math.Sqrt(a.Length));
        }

        static void Control(StringBuilder sb, int runs, double c)
        {
            double savedC = StudentTPriceModel.DriftC;
            bool savedStarve = LoopSim.StarveOff;
            double savedQ = LoopSim.LuckQCoin;
            StudentTPriceModel.DriftC = c;
            LoopSim.StarveOff = true;     // coin only: no job, no lotto, no hunger side effects
            LoopSim.LuckQCoin = 0;        // and no luck button — this is the market alone
            var lo = new List<LoopSim.Outcome>(); var sh = new List<LoopSim.Outcome>();
            for (int s = 1; s <= runs; s++)
            {
                lo.Add(LoopSim.Play(LoopSim.MakeChain(Direction.Long, true), s));
                sh.Add(LoopSim.Play(LoopSim.MakeChain(Direction.Short, true), s));
            }
            StudentTPriceModel.DriftC = savedC; LoopSim.StarveOff = savedStarve; LoopSim.LuckQCoin = savedQ;
            var a = Stats(lo.Select(o => o.Final)); var b = Stats(sh.Select(o => o.Final));
            var ra = Stats(lo.Select(o => o.CoinRealized)); var rb = Stats(sh.Select(o => o.CoinRealized));
            var diffs = lo.Zip(sh, (x, y) => (double)(x.Final - y.Final)).ToArray();
            double dm = diffs.Average(), dsd = Math.Sqrt(diffs.Sum(x => (x - dm) * (x - dm)) / (diffs.Length - 1)), dse = dsd / Math.Sqrt(diffs.Length);
            sb.AppendLine($"| c = {F(c, 6)} | 롱만 | {Fmt.Won((long)a.mean)} (±{Fmt.Won((long)a.se)}) | {Fmt.Won((long)a.median)} | {Fmt.SignedWon((long)ra.mean)} | {lo.Average(o => o.Liquidations):F2} |");
            sb.AppendLine($"| | 숏만 | {Fmt.Won((long)b.mean)} (±{Fmt.Won((long)b.se)}) | {Fmt.Won((long)b.median)} | {Fmt.SignedWon((long)rb.mean)} | {sh.Average(o => o.Liquidations):F2} |");
            sb.AppendLine($"| | 롱 − 숏 (같은 시드 쌍) | {Fmt.SignedWon((long)dm)} (±{Fmt.Won((long)dse)}, z = {F(dm / dse, 2)}) | | | |");
        }

        public static bool Run(int runs, string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# SIM_DRIFT — 가격 모델 드리프트 점검 (보고 전용)");
            sb.AppendLine();
            sb.AppendLine("> 생성: `dotnet run --project tools/dotnet-harness -- sim drift`. 손으로 고치지 말 것. 틱 로그수익률 r = t(3) × 0.010, |r| ≤ 0.25. 마팅게일이면 E[P(t+1)/P(t)] = E[e^r] = 1.");
            sb.AppendLine();

            // 1) tick expectation
            var rng = new Xoshiro128(20260930UL);
            double sum = 0, sum2 = 0, sumR = 0; long capped = 0;
            for (long i = 0; i < Samples; i++)
            {
                double r = StudentTPriceModel.RawLogReturn(rng);
                double e = Math.Exp(r);
                sum += e; sum2 += e * e; sumR += r;
                if (Math.Abs(r) >= Rules.CoinTickCap - 1e-12) capped++;
            }
            double mean = sum / Samples, sd = Math.Sqrt(sum2 / Samples - mean * mean), se = sd / Math.Sqrt(Samples);
            sb.AppendLine("## 1. 틱당 E[P(t+1)/P(t)] (슬롯 = 1틱, 1,000만 샘플, 고정 시드)");
            sb.AppendLine();
            sb.AppendLine("| 항목 | 값 |");
            sb.AppendLine("|---|---|");
            sb.AppendLine($"| 표본 평균 E[e^r] | {F(mean, 7)} |");
            sb.AppendLine($"| 표준오차 | {F(se, 7)} |");
            sb.AppendLine($"| 95% 신뢰구간 | [{F(mean - 1.96 * se, 7)}, {F(mean + 1.96 * se, 7)}] |");
            sb.AppendLine($"| 1과의 차이 | {F(mean - 1, 7)} (z = {F((mean - 1) / se, 1)}) |");
            sb.AppendLine($"| 평균 r | {F(sumR / Samples, 7)} |");
            sb.AppendLine($"| 상한(|r| = 0.25)에 걸린 비율 | {F(100.0 * capped / Samples, 5)}% |");
            sb.AppendLine($"| 이론 근사 1 + Var(r)/2 (Var t3 = 3) | {F(1 + 3 * Rules.CoinTickScale * Rules.CoinTickScale / 2, 7)} |");
            sb.AppendLine();
            sb.AppendLine($"→ 로그 드리프트 c = ln E[e^r] = **{F(Math.Log(mean), 7)}** (틱당). 30일 = 150틱이면 가격 기대치가 약 {F((Math.Pow(mean, 150) - 1) * 100, 2)}% 오른다.");
            sb.AppendLine();
            if (Rules.CoinDriftC != 0)
            {
                var rng2 = new Xoshiro128(20260930UL);
                double s1 = 0, s2 = 0;
                for (long i = 0; i < Samples; i++)
                {
                    double e = Math.Exp(StudentTPriceModel.RawLogReturn(rng2) - Rules.CoinDriftC);
                    s1 += e; s2 += e * e;
                }
                double m2 = s1 / Samples, se2 = Math.Sqrt(s2 / Samples - m2 * m2) / Math.Sqrt(Samples);
                sb.AppendLine($"**보정 후** (`Rules.CoinDriftC` = {F(Rules.CoinDriftC, 7)} 를 뺀 틱): E[e^(r−c)] = {F(m2, 7)} (±{F(se2, 7)}, 1과의 차이 z = {F((m2 - 1) / se2, 1)}).");
                sb.AppendLine();
            }

            // 2) sub-tick paths
            var mr = new Xoshiro128(11UL); var sr = new Xoshiro128(22UL);
            double slotSum = 0, subSum = 0, slotSum2 = 0; long subN = 0;
            const int Slots = 1_000_000;
            long price = Rules.CoinStartPrice;
            for (int i = 0; i < Slots; i++)
            {
                long np = StudentTPriceModel.Instance.Next(price, mr);
                var sp = StudentTSubTickPath.Instance.Build(price, np, sr);
                double prev = price;
                foreach (long p in sp) { subSum += p / prev; prev = p; subN++; }
                double ratio = (double)np / price;
                slotSum += ratio; slotSum2 += ratio * ratio;
                price = Rules.CoinStartPrice;   // keep the level fixed: only the ratios matter
            }
            double sm = slotSum / Slots, sse = Math.Sqrt(slotSum2 / Slots - sm * sm) / Math.Sqrt(Slots);
            sb.AppendLine("## 2. 서브틱 포함 경로 (100만 슬롯 × 12 서브틱, 가격 1억 고정 출발)");
            sb.AppendLine();
            sb.AppendLine($"- 슬롯 종가 비율 E[close/open] = {F(sm, 7)} (±{F(sse, 7)}) — 서브틱 경로의 합이 슬롯 r이므로 1번 표와 같다.");
            sb.AppendLine($"- 서브틱당 평균 비율(경로 내부 12걸음) = {F(subSum / subN, 7)} — 브라운 다리형 보정 때문에 걸음 단위 기댓값은 슬롯 단위와 다를 수 있다(참고).");
            sb.AppendLine();

            // 3) coin-only control
            foreach (int n in new[] { runs, 5000 }.Distinct())
            {
                sb.AppendLine($"## 3. 코인 전용 대조 (E 봇 전략만: 알바·로또·굶주림 없음, 무조건 승리 없음, 30일, {n}런)");
                sb.AppendLine();
                sb.AppendLine("| 보정 | 방향 | 최종 총자산 평균 | 중앙값 | 코인 실현손익 평균 | 청산/런 |");
                sb.AppendLine("|---|---|---|---|---|---|");
                Control(sb, n, 0);
                if (Rules.CoinDriftC != 0) Control(sb, n, Rules.CoinDriftC);
                sb.AppendLine();
            }

            System.IO.File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            Console.WriteLine($"drift sim: E[e^r] = {F(mean, 7)} +- {F(se, 7)} (z {F((mean - 1) / se, 1)}), c = {F(Math.Log(mean), 7)} -> {path}");
            return true;
        }
    }
}
