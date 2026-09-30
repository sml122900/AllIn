using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using AllIn.Core;

namespace AllIn.Harness.Sim
{
    /// <summary>Net effect of coin trading on the E bot (report-only): final assets of the E chain minus the same bot with the coin actions turned off
    /// (same job, transfers, rent, hunger), paired by seed. Condition proposed by the user: the 95% CI upper bound of the mean net effect stays below 0.</summary>
    public static class CoinNetSim
    {
        static readonly double[] Qs = { 0, 0.01, 0.02, 0.03 };

        static string P(double v) => (v * 100).ToString("F1", CultureInfo.InvariantCulture) + "%";

        sealed class Row
        {
            public double Q, EMean, ControlMean, Diff, Se, Lo, Hi;
        }

        static long[] Finals(bool noTrade, double q, int runs)
        {
            double savedQ = LoopSim.LuckQCoin;
            LoopSim.LuckQCoin = q;
            var r = new long[runs];
            for (int s = 1; s <= runs; s++) r[s - 1] = LoopSim.Play(LoopSim.MakeChain(Direction.Long, false, noTrade), s).Final;
            LoopSim.LuckQCoin = savedQ;
            return r;
        }

        static Row Measure(double q, long[] control, int runs)
        {
            var e = Finals(false, q, runs);
            var d = e.Zip(control, (x, y) => (double)(x - y)).ToArray();
            double m = d.Average(), sd = Math.Sqrt(d.Sum(x => (x - m) * (x - m)) / (runs - 1)), se = sd / Math.Sqrt(runs);
            return new Row { Q = q, EMean = e.Average(), ControlMean = control.Average(), Diff = m, Se = se, Lo = m - 1.96 * se, Hi = m + 1.96 * se };
        }

        static void Table(StringBuilder sb, string title, int runs, out double best)
        {
            var control = Finals(true, 0, runs);
            sb.AppendLine(title);
            sb.AppendLine();
            sb.AppendLine("| q | E 최종 총자산 평균 | 대조(코인 행동 끔) 평균 | 순효과 평균 | 표준오차 | 95% 신뢰구간 | 상한 < 0 |");
            sb.AppendLine("|---|---|---|---|---|---|---|");
            best = -1;
            foreach (double q in Qs)
            {
                var r = Measure(q, control, runs);
                bool ok = r.Hi < 0;
                if (ok) best = q;
                sb.AppendLine($"| {P(q)} | {Fmt.Won((long)r.EMean)} | {Fmt.Won((long)r.ControlMean)} | {Fmt.SignedWon((long)r.Diff)} | {Fmt.Won((long)r.Se)} | [{Fmt.SignedWon((long)r.Lo)}, {Fmt.SignedWon((long)r.Hi)}] | {(ok ? "O" : "X")} |");
            }
            sb.AppendLine();
        }

        public static bool Run(int runs, string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# SIM_COIN_NET — 코인의 순효과 (보고 전용)");
            sb.AppendLine();
            sb.AppendLine("> 생성: `dotnet run --project tools/dotnet-harness -- sim coinnet`. 손으로 고치지 말 것.");
            sb.AppendLine($"> 순효과 = E 봇(청산 체인, 알바 병행) 최종 총자산 − **같은 봇에서 코인 행동(포지션 진입)만 끈 대조**(알바·월세·식비·굶음·자금 이동은 동일) — 같은 시드끼리 짝지은 차이. 30일, {runs}런, 파산 판정 끔(A~E 기준선과 동일). 조건(사용자 제안): 순효과 평균의 95% 신뢰구간 상한 < 0 인 최대 q.");
            sb.AppendLine($"> 가격 모델은 드리프트 보정 후(`Rules.CoinDriftC` = {Rules.CoinDriftC.ToString("F7", CultureInfo.InvariantCulture)}). 참고로 보정 전 모델 결과를 아래에 함께 둔다.");
            sb.AppendLine();

            Table(sb, "## 보정 후 모델 (규칙)", runs, out double best);
            double saved = StudentTPriceModel.DriftC;
            StudentTPriceModel.DriftC = 0;
            Table(sb, "## 참고: 보정 전 모델 (c = 0)", runs, out double bestOld);
            StudentTPriceModel.DriftC = saved;

            sb.AppendLine($"**추천(보정 후, 조건 = 순효과 95% 상한 < 0): 최대 q = {(best < 0 ? "표의 모든 q에서 미충족 (q = 0 포함)" : P(best))}.** 규칙 q = {P(Rules.LuckQCoin)}는 그대로다 — 결정은 사용자.");
            System.IO.File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            Console.WriteLine($"coin net sim: q* (corrected) = {(best < 0 ? "none" : P(best))}, (uncorrected) = {(bestOld < 0 ? "none" : P(bestOld))} -> {path}");
            return true;
        }
    }
}
