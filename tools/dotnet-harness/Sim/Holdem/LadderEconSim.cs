using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using AllIn.Core;
using AllIn.Holdem;
using AllIn.Holdem.Brain;

namespace AllIn.Harness.Sim.Holdem
{
    /// <summary>Ladder economics grid (report-only, rules untouched): required wins x entry fee for the Pub and the Satellite,
    /// chart bot vs gut bot, with the decided luck chance. Marks the cells where the chart bot's ROI is in [-10%, 0] and the gut bot's clearly negative.</summary>
    public static class LadderEconSim
    {
        sealed class Cell
        {
            public double Reach, Reentries, Roi;
        }

        static readonly int[] SatWins = { 4, 5, 6 };
        static readonly long[] SatFees = { 100_000, 200_000, 300_000 };
        static readonly int[] PubWins = { 3, 4 };
        static readonly long[] PubFees = { 30_000, 50_000 };
        const double GutClearlyNegative = -0.20;   // "뚜렷한 음수": at most -20%

        static string P(double v) => (v * 100).ToString("F1", CultureInfo.InvariantCulture) + "%";

        static (double reach, double reentries) Play(TournamentKind kind, string bot, int wins, long fee, int runs)
        {
            int hits = 0; long re = 0;
            for (int s = 1; s <= runs; s++)
            {
                var cfg = LadderConfig.For(kind, 4);
                cfg.Steps = new[] { new LadderStep(wins, LadderStage.Seat, new Prize(PrizeType.Seat, 1, 1), true) };
                cfg.EntryFee = fee;
                cfg.Reentry.Cost = fee;
                var l = new Ladder(cfg, new Xoshiro128((ulong)s)) { LuckRng = new Xoshiro128((ulong)s ^ 0x1AC1AC1AC1ACUL) };
                l.RunToEnd(bot == "직감" ? new LadderSim.GutBot() : (ILadderPlayer)ChartPlayer.Instance);
                if (l.Result.Stage >= LadderStage.Seat) hits++;
                re += l.Result.Reentries;
            }
            return (hits / (double)runs, re / (double)runs);
        }

        /// <summary>Main-event average after-tax prize of a bot (a Main seat's value, the existing valuation for a Satellite ticket).</summary>
        static double MainSeatValue(string bot, int runs)
        {
            double sum = 0;
            for (int s = 1; s <= runs; s++)
            {
                var cfg = LadderConfig.For(TournamentKind.Main, 4);
                var l = new Ladder(cfg, new Xoshiro128((ulong)s)) { LuckRng = new Xoshiro128((ulong)s ^ 0x1AC1AC1AC1ACUL) };
                l.RunToEnd(bot == "직감" ? new LadderSim.GutBot() : (ILadderPlayer)ChartPlayer.Instance);
                if (l.Result.Prize.Type == PrizeType.Cash) sum += l.Result.Prize.AfterTax;
            }
            return sum / runs;
        }

        public static bool Run(int runs, string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# SIM_LADDER_ECON — 사다리 경제 그리드 (보고 전용, 규칙 기본값 불변)");
            sb.AppendLine();
            sb.AppendLine("> 생성: `dotnet run --project tools/dotnet-harness -- sim ladder`. 손으로 고치지 말 것.");
            sb.AppendLine($"> 시드 1..{runs}, 무조건 승리 q = 홀덤 {P(HoldemRules.LuckQHoldem)} 포함, 리엔트리 허용(참가비 × (1 + 리엔트리) 가 총 비용). 차트 봇 = HUD 4 권고, 직감 봇 = 포켓페어·A 포함·브로드웨이 두 장.");
            sb.AppendLine("> ROI = (도달률 × 시트 가치 − 평균 총 참가비) / 평균 총 참가비. 시트 가치 = 기존 방식: 펍 시트 = 새틀라이트 참가비 100,000원(현행값 고정), 새틀 시트 = **같은 봇의 메인 평균 세후 상금**(메인은 이번에 건드리지 않음).");
            sb.AppendLine($"> ★ = 목표 충족(차트 봇 ROI ∈ [−10%, 0%] 이고 직감 봇 ROI ≤ {P(GutClearlyNegative)}). 판단은 사용자.");
            sb.AppendLine();

            var seat = new Dictionary<string, double> { ["차트"] = MainSeatValue("차트", runs), ["직감"] = MainSeatValue("직감", runs) };
            sb.AppendLine($"메인 평균 세후 상금(시트 가치): 차트 {Fmt.Won((long)seat["차트"])} / 직감 {Fmt.Won((long)seat["직감"])}.");
            sb.AppendLine();

            // ---------- satellite ----------
            sb.AppendLine("## 새틀라이트 (필요 승수 × 참가비) — 현행: 4승 · 100,000원");
            sb.AppendLine();
            sb.AppendLine("| 필요 승수 | 참가비 | 차트 도달률 | 차트 리엔트리 | 차트 ROI | 직감 도달률 | 직감 리엔트리 | 직감 ROI | |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
            var hitsSat = new List<string>();
            foreach (int w in SatWins)
                foreach (long fee in SatFees)
                {
                    var c = Eval(TournamentKind.Satellite, "차트", w, fee, runs, seat["차트"]);
                    var g = Eval(TournamentKind.Satellite, "직감", w, fee, runs, seat["직감"]);
                    bool ok = c.Roi >= -0.10 && c.Roi <= 0 && g.Roi <= GutClearlyNegative;
                    if (ok) hitsSat.Add($"{w}승 · {Fmt.Won(fee)}");
                    string cur = w == 4 && fee == 100_000 ? " (현행)" : "";
                    sb.AppendLine($"| {w}승{cur} | {Fmt.Won(fee)} | {P(c.Reach)} | {c.Reentries:F2} | {P(c.Roi)} | {P(g.Reach)} | {g.Reentries:F2} | {P(g.Roi)} | {(ok ? "★" : "")} |");
                }
            sb.AppendLine();
            sb.AppendLine("표의 격자점에서 조건을 만족하는 조합: " + (hitsSat.Count == 0 ? "**없음**" : string.Join(" / ", hitsSat)));
            sb.AppendLine();
            FeeBands(sb, TournamentKind.Satellite, SatWins, SatFees[0], runs, seat["차트"], seat["직감"]);

            // ---------- pub ----------
            sb.AppendLine("## 펍 (필요 승수 × 참가비) — 현행: 3승 · 30,000원 (시트 가치 100,000원 고정)");
            sb.AppendLine();
            sb.AppendLine("| 필요 승수 | 참가비 | 차트 도달률 | 차트 리엔트리 | 차트 ROI | 직감 도달률 | 직감 리엔트리 | 직감 ROI | |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
            var hitsPub = new List<string>();
            foreach (int w in PubWins)
                foreach (long fee in PubFees)
                {
                    var c = Eval(TournamentKind.Pub, "차트", w, fee, runs, HoldemRules.SatelliteFee);
                    var g = Eval(TournamentKind.Pub, "직감", w, fee, runs, HoldemRules.SatelliteFee);
                    bool ok = c.Roi >= -0.10 && c.Roi <= 0 && g.Roi <= GutClearlyNegative;
                    if (ok) hitsPub.Add($"{w}승 · {Fmt.Won(fee)}");
                    string cur = w == 3 && fee == 30_000 ? " (현행)" : "";
                    sb.AppendLine($"| {w}승{cur} | {Fmt.Won(fee)} | {P(c.Reach)} | {c.Reentries:F2} | {P(c.Roi)} | {P(g.Reach)} | {g.Reentries:F2} | {P(g.Roi)} | {(ok ? "★" : "")} |");
                }
            sb.AppendLine();
            sb.AppendLine("표의 격자점에서 조건을 만족하는 조합: " + (hitsPub.Count == 0 ? "**없음**" : string.Join(" / ", hitsPub)));
            sb.AppendLine();
            FeeBands(sb, TournamentKind.Pub, PubWins, PubFees[0], runs, HoldemRules.SatelliteFee, HoldemRules.SatelliteFee);
            sb.AppendLine("> 새틀 4승 도달 참고: 공정한 몫은 16명 필드에서 1/16 = 6.25% (도달률이 그보다 높으면 시트가 필드 크기 대비 관대하다는 뜻).");

            System.IO.File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            Console.WriteLine($"ladder econ sim: satellite hits [{string.Join(", ", hitsSat)}] pub hits [{string.Join(", ", hitsPub)}] -> {path}");
            return true;
        }

        /// <summary>Reach and re-entries do not depend on the fee (the bots always re-enter), so ROI + 1 = K / fee with K = reach x seat value / (1 + re): the fee band that puts the chart bot in [-10%, 0%] is [K, K / 0.9].</summary>
        static void FeeBands(StringBuilder sb, TournamentKind kind, int[] wins, long firstFee, int runs, double chartSeat, double gutSeat)
        {
            sb.AppendLine("### 목표 구간의 참가비 범위 (참고 — 도달률·리엔트리는 참가비와 무관하므로 정확한 역산)");
            sb.AppendLine();
            sb.AppendLine("| 필요 승수 | 차트 ROI ∈ [−10%, 0%] 가 되는 참가비 | 그 중간 참가비에서 직감 ROI | 직감 ≤ −20% 도 만족? |");
            sb.AppendLine("|---|---|---|---|");
            foreach (int w in wins)
            {
                var c = Eval(kind, "차트", w, firstFee, runs, chartSeat);
                var g = Eval(kind, "직감", w, firstFee, runs, gutSeat);
                double kc = c.Reach * chartSeat / (1 + c.Reentries), kg = g.Reach * gutSeat / (1 + g.Reentries);
                double lo = kc, hi = kc / 0.9, mid = (lo + hi) / 2;
                double gutRoi = kg / mid - 1;
                sb.AppendLine($"| {w}승 | {Fmt.Won((long)Math.Ceiling(lo / 1000) * 1000)} ~ {Fmt.Won((long)Math.Floor(hi / 1000) * 1000)} | {P(gutRoi)} (@ {Fmt.Won((long)Math.Round(mid / 1000) * 1000)}) | {(gutRoi <= GutClearlyNegative ? "O" : "X")} |");
            }
            sb.AppendLine();
        }

        static Cell Eval(TournamentKind kind, string bot, int wins, long fee, int runs, double seatValue)
        {
            var (reach, re) = Play(kind, bot, wins, fee, runs);
            double cost = fee * (1 + re);
            return new Cell { Reach = reach, Reentries = re, Roi = (reach * seatValue - cost) / cost };
        }
    }
}
