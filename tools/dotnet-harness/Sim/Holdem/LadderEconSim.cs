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
    /// <summary>Ladder economics check (report-only): the configured Pub and Satellite (required wins, field, fee) for the chart and gut bots with the
    /// decided luck chance, plus the exact fee band that would put the chart bot in [-10%, 0%]. Target: chart -10..0%, gut clearly negative (<= -20%).</summary>
    public static class LadderEconSim
    {
        const double GutClearlyNegative = -0.20;

        static string P(double v) => (v * 100).ToString("F1", CultureInfo.InvariantCulture) + "%";

        sealed class Result
        {
            public double Reach, Reentries;
        }

        static Result Play(TournamentKind kind, string bot, int runs)
        {
            int hits = 0; long re = 0;
            for (int s = 1; s <= runs; s++)
            {
                var cfg = LadderConfig.For(kind, 4);
                var l = new Ladder(cfg, new Xoshiro128((ulong)s)) { LuckRng = new Xoshiro128((ulong)s ^ 0x1AC1AC1AC1ACUL) };
                l.RunToEnd(bot == "직감" ? new LadderSim.GutBot() : (ILadderPlayer)ChartPlayer.Instance);
                if (l.Result.Stage >= LadderStage.Seat) hits++;
                re += l.Result.Reentries;
            }
            return new Result { Reach = hits / (double)runs, Reentries = re / (double)runs };
        }

        /// <summary>Main-event average after-tax prize of a bot: a Main seat's value, the existing valuation for a Satellite ticket.</summary>
        static double MainSeatValue(string bot, int runs)
        {
            double sum = 0;
            for (int s = 1; s <= runs; s++)
            {
                var l = new Ladder(LadderConfig.For(TournamentKind.Main, 4), new Xoshiro128((ulong)s)) { LuckRng = new Xoshiro128((ulong)s ^ 0x1AC1AC1AC1ACUL) };
                l.RunToEnd(bot == "직감" ? new LadderSim.GutBot() : (ILadderPlayer)ChartPlayer.Instance);
                if (l.Result.Prize.Type == PrizeType.Cash) sum += l.Result.Prize.AfterTax;
            }
            return sum / runs;
        }

        public static bool Run(int runs, string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# SIM_LADDER_ECON — 사다리 경제 (보고 전용)");
            sb.AppendLine();
            sb.AppendLine("> 생성: `dotnet run --project tools/dotnet-harness -- sim ladder`. 손으로 고치지 말 것.");
            var pubCfg = LadderConfig.For(TournamentKind.Pub);
            var satCfg = LadderConfig.For(TournamentKind.Satellite);
            sb.AppendLine($"> 시드 1..{runs}. **현재 규칙의 실제 설정**(`LadderConfig.For`)을 그대로 돌린다: 펍 = 필드 {pubCfg.Field} · {pubCfg.Steps[0].Wins}승 · {Fmt.Won(HoldemRules.PubFee)}, 새틀라이트 = 필드 {satCfg.Field} · {satCfg.Steps[0].Wins}승 · {Fmt.Won(HoldemRules.SatelliteFee)}. 무조건 승리 q = 홀덤 {P(HoldemRules.LuckQHoldem)} 포함, 리엔트리 항상.");
            sb.AppendLine("> ROI = (도달률 × 시트 가치 − 평균 총 참가비) / 평균 총 참가비, 총 참가비 = 참가비 × (1 + 리엔트리). 시트 가치: 펍 시트 = 새틀라이트 참가비(현재 " + Fmt.Won(HoldemRules.SatelliteFee) + ", 규칙 상수에 연동), 새틀 시트 = **같은 봇의 메인 평균 세후 상금**.");
            sb.AppendLine($"> 목표(판단은 사용자): 차트 봇 ROI ∈ [−10%, 0%], 직감 봇 ROI ≤ {P(GutClearlyNegative)}.");
            sb.AppendLine();

            var seat = new Dictionary<string, double> { ["차트"] = MainSeatValue("차트", runs), ["직감"] = MainSeatValue("직감", runs) };
            sb.AppendLine($"메인 평균 세후 상금(시트 가치): 차트 {Fmt.Won((long)seat["차트"])} / 직감 {Fmt.Won((long)seat["직감"])}.");
            sb.AppendLine();

            var summary = new List<string>();
            foreach (var kind in new[] { TournamentKind.Pub, TournamentKind.Satellite })
            {
                var cfg = LadderConfig.For(kind, 4);
                int wins = cfg.Steps[0].Wins;
                long fee = cfg.EntryFee;
                double chartSeat = kind == TournamentKind.Pub ? HoldemRules.SatelliteFee : seat["차트"];
                double gutSeat = kind == TournamentKind.Pub ? HoldemRules.SatelliteFee : seat["직감"];
                var c = Play(kind, "차트", runs); var g = Play(kind, "직감", runs);
                double cCost = fee * (1 + c.Reentries), gCost = fee * (1 + g.Reentries);
                double cRoi = (c.Reach * chartSeat - cCost) / cCost, gRoi = (g.Reach * gutSeat - gCost) / gCost;
                double kc = c.Reach * chartSeat / (1 + c.Reentries), kg = g.Reach * gutSeat / (1 + g.Reentries);   // ROI + 1 = K / fee
                double lo = kc, hi = kc / 0.9;
                double gutAtMid = kg / ((lo + hi) / 2) - 1;
                bool ok = cRoi >= -0.10 && cRoi <= 0 && gRoi <= GutClearlyNegative;
                summary.Add($"{Texts.HoldemKindNames[(int)kind]}: 차트 {P(cRoi)} / 직감 {P(gRoi)} {(ok ? "OK" : "outside target")}, band {Fmt.Won((long)lo)}~{Fmt.Won((long)hi)}");

                sb.AppendLine($"## {Texts.HoldemKindNames[(int)kind]} — 필드 {cfg.Field} · {wins}승 · 참가비 {Fmt.Won(fee)}");
                sb.AppendLine();
                sb.AppendLine("| 봇 | 도달률 | 평균 리엔트리 | 총 참가비 | 시트 가치 | ROI |");
                sb.AppendLine("|---|---|---|---|---|---|");
                sb.AppendLine($"| 차트 | {P(c.Reach)} | {c.Reentries:F2} | {Fmt.Won((long)cCost)} | {Fmt.Won((long)chartSeat)} | **{P(cRoi)}** |");
                sb.AppendLine($"| 직감 | {P(g.Reach)} | {g.Reentries:F2} | {Fmt.Won((long)gCost)} | {Fmt.Won((long)gutSeat)} | **{P(gRoi)}** |");
                sb.AppendLine();
                sb.AppendLine($"공정 몫(필드 {cfg.Field}에서 1등) = {P(1.0 / cfg.Field)} — 차트 봇 도달률은 그 {c.Reach * cfg.Field:F1}배. {(ok ? "**목표 충족.**" : "**목표 밖.**")}");
                sb.AppendLine();
                sb.AppendLine($"참가비 범위(도달률·리엔트리는 참가비와 무관하므로 정확한 역산): 차트 ROI ∈ [−10%, 0%] ⇔ 참가비 **{Fmt.Won((long)Math.Ceiling(lo / 1000) * 1000)} ~ {Fmt.Won((long)Math.Floor(hi / 1000) * 1000)}**; 그 중간({Fmt.Won((long)Math.Round((lo + hi) / 2000) * 1000)})에서 직감 ROI {P(gutAtMid)}.");
                sb.AppendLine();
            }
            System.IO.File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            Console.WriteLine("ladder econ: " + string.Join(" | ", summary) + " -> " + path);
            return true;
        }
    }
}
