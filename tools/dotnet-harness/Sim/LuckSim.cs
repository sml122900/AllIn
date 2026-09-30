using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using AllIn.Core;
using AllIn.Holdem;
using AllIn.Holdem.Brain;

namespace AllIn.Harness.Sim
{
    /// <summary>DESIGN.md E4: picks q for "무조건 승리". Report-only; the rule default stays q = 0 until the user decides.</summary>
    public static class LuckSim
    {
        static readonly double[] HoldemQs = { 0, 0.01, 0.02, 0.03, 0.05 };
        static readonly double[] CoinQs = { 0, 0.02, 0.05, 0.10, 0.15 };

        sealed class HRow
        {
            public LadderStage Stage;
            public int Reentries, Wins, LuckWins;
            public long Prize; // after-tax cash (Main)
        }

        static HRow PlayHoldem(TournamentKind kind, string bot, long seed, double q)
        {
            var cfg = LadderConfig.For(kind, 4);
            cfg.LuckQ = q;
            var l = new Ladder(cfg, new Xoshiro128((ulong)seed)) { LuckRng = new Xoshiro128((ulong)seed ^ 0x1AC1AC1AC1ACUL) };
            ILadderPlayer p = bot == "직감" ? new Holdem.LadderSim.GutBot() : (ILadderPlayer)ChartPlayer.Instance;
            l.RunToEnd(p);
            var r = l.Result;
            return new HRow { Stage = r.Stage, Reentries = r.Reentries, Wins = r.Wins, LuckWins = r.LuckWins, Prize = r.Prize.Type == PrizeType.Cash ? r.Prize.AfterTax : 0 };
        }

        static string P(double v) => (v * 100).ToString("F1", CultureInfo.InvariantCulture) + "%";
        static string F2(double v) => v.ToString("F2", CultureInfo.InvariantCulture);

        public static bool Run(int runs, string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# SIM_LUCK — \"무조건 승리\" 확률 q 밸런싱 (DESIGN.md E4)");
            sb.AppendLine();
            sb.AppendLine("> 생성: `dotnet run --project tools/dotnet-harness -- sim luck`. 손으로 고치지 말 것. 보고 전용 — **q 기본값은 0으로 유지**, 결정은 사용자.");
            sb.AppendLine($"> 시드 1..{runs}. 발동은 전용 luck 스트림에서만 뽑는다(기도·mental·잔고와 무관). 헤드리스 봇은 발동하면 자동으로 누른 것으로 친다.");
            sb.AppendLine();

            // ---------- hold'em ----------
            sb.AppendLine("## 홀덤 (사다리 v2)");
            sb.AppendLine();
            sb.AppendLine("ROI = (상금 가치 − 참가비 총액) / 참가비 총액, 참가비 총액 = 참가비 × (1 + 리엔트리). 펍 1위 시트 = 새틀라이트 참가비 100,000원, 새틀라이트 시트 = **같은 봇·같은 q**의 메인 평균 세후 상금으로 환산(기존 방식). 메인은 시트 입장이라 참가비가 없어 ROI 대신 평균 세후 상금.");
            sb.AppendLine();
            sb.AppendLine("| q | 봇 | 펍 3승 | 펍 ROI | 새틀 4승 | 새틀 ROI | 메인 5승 | 6승 | 7승 | 우승 | 메인 평균 세후 상금 | 발동/런(펍·새틀·메인) |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|");
            var chartRoi = new Dictionary<double, (double pub, double sat)>();
            foreach (double q in HoldemQs)
                foreach (string bot in new[] { "차트", "직감" })
                {
                    var pub = Enumerable.Range(1, runs).Select(s => PlayHoldem(TournamentKind.Pub, bot, s, q)).ToList();
                    var sat = Enumerable.Range(1, runs).Select(s => PlayHoldem(TournamentKind.Satellite, bot, s, q)).ToList();
                    var main = Enumerable.Range(1, runs).Select(s => PlayHoldem(TournamentKind.Main, bot, s, q)).ToList();
                    double mainAvg = main.Average(r => (double)r.Prize);
                    double pubFeeTotal = pub.Average(r => (1 + r.Reentries) * (double)HoldemRules.PubFee);
                    double pubValue = pub.Count(r => r.Stage >= LadderStage.Seat) / (double)runs * HoldemRules.SatelliteFee;
                    double pubRoi = (pubValue - pubFeeTotal) / pubFeeTotal;
                    double satFeeTotal = sat.Average(r => (1 + r.Reentries) * (double)HoldemRules.SatelliteFee);
                    double satValue = sat.Count(r => r.Stage >= LadderStage.Seat) / (double)runs * mainAvg;
                    double satRoi = (satValue - satFeeTotal) / satFeeTotal;
                    if (bot == "차트") chartRoi[q] = (pubRoi, satRoi);
                    string Rate(List<HRow> l, LadderStage s) => P(l.Count(r => r.Stage >= s) / (double)runs);
                    sb.AppendLine($"| {P(q)} | {bot} | {Rate(pub, LadderStage.Seat)} | {P(pubRoi)} | {Rate(sat, LadderStage.Seat)} | {P(satRoi)} | {Rate(main, LadderStage.MoneyIn)} | {Rate(main, LadderStage.FinalTable)} | {Rate(main, LadderStage.Podium)} | {Rate(main, LadderStage.Champion)} | {Fmt.Won((long)Math.Round(mainAvg))} | {F2(pub.Average(r => r.LuckWins))} · {F2(sat.Average(r => r.LuckWins))} · {F2(main.Average(r => r.LuckWins))} |");
                }
            // A condition that already fails at q = 0 is a property of the ladder economics, not of the button: it is reported and left out of the pick.
            bool pubBase = chartRoi[0].pub < 0, satBase = chartRoi[0].sat < 0;
            double bestH = 0;
            foreach (double q in HoldemQs)
                if ((!pubBase || chartRoi[q].pub < 0) && (!satBase || chartRoi[q].sat < 0)) bestH = q;
            sb.AppendLine();
            sb.AppendLine($"**홀덤 추천 (조건: 차트 봇 ROI가 어느 대회에서도 +가 되지 않는 최대 q): {P(bestH)}**" + (bestH == HoldemQs.Last() ? " — 표의 최댓값까지 충족" : "") + ".");
            if (!pubBase || !satBase)
                sb.AppendLine($"> 주의: q = 0에서 이미 조건이 깨진 대회가 있다({(pubBase ? "" : "펍 ")}{(satBase ? "" : "새틀라이트 ")}차트 봇 ROI {(pubBase ? "" : P(chartRoi[0].pub) + " ")}{(satBase ? "" : P(chartRoi[0].sat))}). 이는 버튼과 무관한 사다리 경제(새틀 시트 = 메인 평균 세후 상금 환산) 문제라 추천 q 계산에서 뺐다 — 별도로 판단할 것.");
            sb.AppendLine("메인은 참가비가 없어 ROI 조건에서 제외(평균 상금 증가만 참고).");
            sb.AppendLine();

            // ---------- coin ----------
            sb.AppendLine("## 코인 선물 (관전 1회당 q)");
            sb.AppendLine();
            sb.AppendLine("C = 25배 단발, D = 10배 매일 스캘프, E = 청산 체인(25배 → 100배 재진입). 30일, 파산 판정 끄기(A~E 기준선과 동일). 무조건 승리 M회 = 런당 평균 사용 횟수.");
            sb.AppendLine();
            sb.AppendLine("| q | C 청산율 | C 최종 중앙값 | C 평균 | D 청산/런 | D 중앙값 | D 평균 | E 청산/런 | E 중앙값 | E 평균 | E 무조건 승리 M/런 |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
            var coinOk = new Dictionary<double, (bool c, bool eMean, bool eMedian)>();
            double eMeanBase = 0;
            foreach (double q in CoinQs)
            {
                LoopSim.LuckQCoin = q;
                var c = Enumerable.Range(1, runs).Select(s => LoopSim.Play(LoopSim.Make("C"), s)).ToList();
                var d = Enumerable.Range(1, runs).Select(s => LoopSim.Play(LoopSim.Make("D"), s)).ToList();
                var e = Enumerable.Range(1, runs).Select(s => LoopSim.Play(LoopSim.Make("E"), s)).ToList();
                LoopSim.LuckQCoin = 0;
                double cLiq = c.Count(o => o.Liquidations > 0) / (double)runs;
                double eAvg = e.Average(o => (double)o.Final);
                if (q == 0) eMeanBase = eAvg;
                coinOk[q] = (cLiq >= 0.80, eAvg < Rules.StartingCash, LoopSim.Percentile(e.Select(o => o.Final), 0.5) < Rules.StartingCash);
                sb.AppendLine($"| {P(q)} | {P(cLiq)} | {Fmt.Won(LoopSim.Percentile(c.Select(o => o.Final), 0.5))} | {Fmt.Won((long)c.Average(o => (double)o.Final))} | {F2(d.Average(o => o.Liquidations))} | {Fmt.Won(LoopSim.Percentile(d.Select(o => o.Final), 0.5))} | {Fmt.Won((long)d.Average(o => (double)o.Final))} | {F2(e.Average(o => o.Liquidations))} | {Fmt.Won(LoopSim.Percentile(e.Select(o => o.Final), 0.5))} | {Fmt.Won((long)eAvg)} | {F2(e.Average(o => o.LuckUses))} |");
            }
            bool eMeanBaseOk = coinOk[0].eMean;
            double bestC = 0;
            foreach (double q in CoinQs) if (coinOk[q].c && (eMeanBaseOk ? coinOk[q].eMean : coinOk[q].eMedian)) bestC = q;
            sb.AppendLine();
            sb.AppendLine($"**코인 추천 (조건: C 청산율 ≥ 80% 이고 E {(eMeanBaseOk ? "평균" : "중앙값")} 최종 자산 < 시작 20,000,000): 최대 q = {P(bestC)}**" + (bestC == CoinQs.Last() ? " — 표의 최댓값까지 충족" : "") + ".");
            if (!eMeanBaseOk)
                sb.AppendLine($"> 주의: E의 **평균**은 q = 0에서 이미 {Fmt.Won((long)eMeanBase)}로 시작 20,000,000을 넘는다(소수의 큰 꼬리 승리가 평균을 끌어올림, 중앙값은 수만 원). 그래서 E 조건은 중앙값으로 대체했다. 평균은 q가 오를수록 더 커진다(표) — 판단은 사용자.");
            sb.AppendLine();
            sb.AppendLine("> 참고: 로또는 적용 제외(순수 확률 유지). 결정은 사용자 — 채택하면 `Rules.LuckQCoin` / `HoldemRules.LuckQHoldem`을 바꾸고 DESIGN.md E1을 함께 고친다.");
            System.IO.File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            Console.WriteLine($"luck sim: holdem q* = {P(bestH)}, coin q* = {P(bestC)} -> {path}");
            return true;
        }
    }
}
