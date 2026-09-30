using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using AllIn.Core;
using AllIn.Core.Actions;

namespace AllIn.Harness.Sim
{
    /// <summary>Report-only measurements (reply to e056a0e): the state at the moment E9 is declared, and how the D bot starves.</summary>
    public static class BankruptcyStudy
    {
        const int Grace = Rules.BankruptForcedAfterDays;

        sealed class Declared
        {
            public bool Happened;
            public int Mental;
            public int DaysSinceLiquidation = -1;   // -1: no liquidation before the declaration
            public int Day;
            public bool Released;
        }

        /// <summary>Plays the bot with bankruptcy on (V3 default) until the first E9 declaration or maxDay, then a job-only bot holds on.</summary>
        static Declared Study(Func<Loop, Action<Loop>> makeBot, long seed, int maxDay)
        {
            var loop = Loop.NewRun(seed);
            loop.PopEvent(); loop.PopEvent();
            loop.ForcedAfterDays = int.MaxValue / 2;   // the hold-on phase is stopped by hand at held day Grace
            var d = loop.Data;
            var bot = makeBot(loop);
            var r = new Declared();
            int lastLiq = 0, guard = 0;
            while (d.day <= maxDay && guard++ < 400_000)
            {
                d.pendingEvents.Clear();
                if (loop.Finished) { loop.ContinueLife(); }
                else
                {
                    int liq = d.stats.liquidations;
                    bot(loop);
                    if (d.stats.liquidations > liq) lastLiq = d.day;
                }
                if (loop.IsBankrupt)
                {
                    r.Happened = true;
                    r.Mental = d.mental;
                    r.Day = d.day;
                    r.DaysSinceLiquidation = lastLiq > 0 ? d.day - lastLiq : -1;
                    int g2 = 0;
                    while (loop.IsBankrupt && loop.BankruptDaysHeld < Grace && g2++ < 20_000)
                    {
                        d.pendingEvents.Clear();
                        if (loop.Finished) loop.ContinueLife(); else LoopSim.WorkOrRest(loop);
                    }
                    r.Released = !loop.IsBankrupt;
                    return r;
                }
            }
            return r;
        }

        static Func<Loop, Action<Loop>> EBot => _ => { var p = LoopSim.Make("E"); return l => p.PlaySlot(l); };

        /// <summary>The sim F bot (100x all-in, whole bank to the exchange, job in the evenings), one slot at a time.</summary>
        static Func<Loop, Action<Loop>> FBot => _ => l =>
        {
            var d = l.Data;
            if (!l.DomesticLocked && d.domesticCash > 0) l.TransferDomesticToOverseas(d.domesticCash);
            if (!d.hasPosition && d.overseasCash > 0 && l.Do(new CoinOpenAction(Direction.Long, 100, d.overseasCash)).Ok) return;
            if (!d.hasPosition && d.overseasCash <= 0 && d.domesticCash <= 0 && d.pendingTransfers.Count == 0 && d.bankCash > 0)
                l.TransferBankToDomestic(d.bankCash);
            if (d.hasPosition) l.Pass(); else LoopSim.WorkOrRest(l);
        };

        static double Pctl(List<int> xs, double p)
        {
            var a = xs.OrderBy(v => v).ToArray();
            return a.Length == 0 ? double.NaN : a[(int)Math.Round(p * (a.Length - 1))];
        }

        static string F(double v) => double.IsNaN(v) ? "—" : v.ToString("F0", CultureInfo.InvariantCulture);

        public static void Append(StringBuilder sb, int runs)
        {
            sb.AppendLine();
            sb.AppendLine("## H 측정 — 파산 선언 순간 (e056a0e 회신, 보고 전용·규칙 변경 없음)");
            sb.AppendLine();
            sb.AppendLine("파산 on + V3 기본. E 봇(청산 체인)·F 봇(100배 올인 반복)을 첫 E9 파산 선언까지 굴린 뒤(E 최대 730일, F 최대 800일), 그 상태 그대로 **알바만 하는 봇**이 14일 유예 안에 밀린 월세를 내는지 본다(열린 포지션·거래소 잔고는 손대지 않음, 굶음 적용). 청산 → 선언 일수 = 선언일 − 마지막 청산일(그 전에 청산이 있었던 런만).");
            sb.AppendLine();
            sb.AppendLine("| 봇 | 파산 선언한 런 | 선언 시점 mental p10 / p50 / p90 | 청산 → 선언 일수 중앙 (p10 / p90, n) | 선언일 중앙 | 버티기 해제 성공률 |");
            sb.AppendLine("|---|---|---|---|---|---|");
            foreach (var (name, bot, maxDay) in new[] { ("E 청산 체인", EBot, 730), ("F 100배 올인 반복", FBot, 800) })
            {
                var rs = new List<Declared>(runs);
                for (int seed = 1; seed <= runs; seed++) rs.Add(Study(bot, seed, maxDay));
                var dec = rs.Where(x => x.Happened).ToList();
                var mental = dec.Select(x => x.Mental).ToList();
                var gap = dec.Where(x => x.DaysSinceLiquidation >= 0).Select(x => x.DaysSinceLiquidation).ToList();
                var day = dec.Select(x => x.Day).ToList();
                string rel = dec.Count > 0 ? LoopSim.Pct(dec.Count(x => x.Released) / (double)dec.Count) : "—";
                sb.AppendLine($"| {name} | {dec.Count} / {runs} | {F(Pctl(mental, 0.1))} / {F(Pctl(mental, 0.5))} / {F(Pctl(mental, 0.9))} | {F(Pctl(gap, 0.5))}일 ({F(Pctl(gap, 0.1))} / {F(Pctl(gap, 0.9))}, n={gap.Count}) | Day {F(Pctl(day, 0.5))} | {rel} |");
                Console.WriteLine($"bankruptcy study {name}: declared {dec.Count}/{runs}, mental p50 {F(Pctl(mental, 0.5))}, released {rel}");
            }

            sb.AppendLine();
            sb.AppendLine("## D 중앙값 하락 원인 (굶음)");
            sb.AppendLine();
            var withStarve = Enumerable.Range(1, runs).Select(s => LoopSim.Play(LoopSim.Make("D"), s)).ToList();
            LoopSim.StarveOff = true;
            var control = Enumerable.Range(1, runs).Select(s => LoopSim.Play(LoopSim.Make("D"), s)).ToList();
            LoopSim.StarveOff = false;
            long medWith = LoopSim.Percentile(withStarve.Select(o => o.Final), 0.5);
            long medOff = LoopSim.Percentile(control.Select(o => o.Final), 0.5);
            sb.AppendLine($"D 봇 런당 평균 굶음 {withStarve.Average(o => (double)o.Starved).ToString("F1", CultureInfo.InvariantCulture)}회(중앙 {F(Pctl(withStarve.Select(o => o.Starved).ToList(), 0.5))}, p90 {F(Pctl(withStarve.Select(o => o.Starved).ToList(), 0.9))}). 최종 총자산 중앙값: 굶음 on {LoopSim.Won(medWith)} / **굶음만 끈 대조 {LoopSim.Won(medOff)}** (식비는 예전처럼 있는 만큼만 차감).");
        }
    }
}
