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
            public int Starved;
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
                    r.Starved = d.stats.starved;
                    return r;
                }
            }
            r.Starved = d.stats.starved;
            return r;
        }

        sealed class HopeRun
        {
            public long Slots, SlotsHigh, SlotsMax;   // slots sampled / mental >= 80 / mental == 100
            public List<int> RecoveryDays = new List<int>();
            public int LiquidationsBelow80, Unrecovered;
            public int HopeCircuits, HopeReadyDays;
        }

        /// <summary>LifeSense on, chosen mental variant, bankruptcy on: plays to the first declaration or 730 days, sampling mental every slot.</summary>
        static HopeRun HopeStudy(Func<Loop, Action<Loop>> makeBot, long seed, string variant)
        {
            var loop = Loop.NewRun(seed);
            loop.PopEvent(); loop.PopEvent();
            loop.Tuning = MentalTuning.ByName(variant);
            var d = loop.Data;
            var bot = makeBot(loop);
            var r = new HopeRun();
            int guard = 0, recoveryStartDay = 0;
            int lastAbs = loop.AbsSlot, lastDayCounted = 0;
            while (d.day <= 730 && guard++ < 400_000)
            {
                d.pendingEvents.Clear();
                int liq = d.stats.liquidations;
                if (loop.Finished) loop.ContinueLife(); else bot(loop);
                if (d.stats.liquidations > liq && recoveryStartDay == 0)
                {
                    if (d.mental < 80) { recoveryStartDay = d.day; r.LiquidationsBelow80++; }
                }
                if (recoveryStartDay > 0 && d.mental >= 80) { r.RecoveryDays.Add(d.day - recoveryStartDay); recoveryStartDay = 0; }
                int adv = Math.Max(1, loop.AbsSlot - lastAbs);
                lastAbs = loop.AbsSlot;
                r.Slots += adv;
                if (d.mental >= 80) r.SlotsHigh += adv;
                if (d.mental >= 100) r.SlotsMax += adv;
                if (d.slot == Slot.Dawn && d.day != lastDayCounted) { lastDayCounted = d.day; if (d.mental >= 80 && d.hasLastWin) r.HopeReadyDays++; }
                if (loop.IsBankrupt) break;
            }
            if (recoveryStartDay > 0) r.Unrecovered++;
            r.HopeCircuits = d.stats.hopeCircuits;
            return r;
        }

        public static void AppendHope(StringBuilder sb, int runs)
        {
            sb.AppendLine();
            sb.AppendLine("## I 측정 — 희망회로 구간(mental ≥ 80) 비율 (9620fe9 회신, 보고 전용·규칙 변경 없음)");
            sb.AppendLine();
            sb.AppendLine("LifeSense on + 파산 on, 첫 파산 선언 또는 730일까지, 각 1,000런. 슬롯 비율은 매 슬롯 끝의 mental로 샘플. 회복 일수 = mental < 80이 된 청산 후, 다시 ≥ 80이 될 때까지(그 사이 또 청산이 나도 처음 기준; 회복 못 한 채 끝난 런은 제외하고 개수만 표기). **E4는 헤드리스 봇이 코인 화면을 열지 않고 수익 청산도 한 적이 없어(직전 수익 기록 자체가 안 생김) 실제 발동·조건 충족 일수가 0이다. 이 봇들로는 E4 빈도를 잴 수 없고, mental ≥ 80 슬롯 비율이 사람이 노출될 상한에 가깝다.**");
            sb.AppendLine();
            sb.AppendLine("| 봇 | 변형 | mental ≥ 80 슬롯 비율 | mental = 100 슬롯 비율 | 청산 후 ≥ 80 복귀 일수 중앙 (p10 / p90) [복귀 n / 미복귀 n] | 런당 E4 발동 | 런당 E4 조건 충족 일수 |");
            sb.AppendLine("|---|---|---|---|---|---|---|");
            LifeSense.On = true;
            foreach (var (name, bot) in new[] { ("E", EBot), ("F", FBot) })
                foreach (var v in new[] { "V3", "V1", "V2" })
                {
                    var rs = Enumerable.Range(1, runs).Select(sd => HopeStudy(bot, sd, v)).ToList();
                    double slots = rs.Sum(x => (double)x.Slots);
                    var rec = rs.SelectMany(x => x.RecoveryDays).ToList();
                    string label = v == "V3" ? "V3 (채택)" : v == "V1" ? "V1 (잠 +5만)" : "V2 (연속 보너스만)";
                    sb.AppendLine($"| {name} | {label} | {LoopSim.Pct(rs.Sum(x => (double)x.SlotsHigh) / slots)} | {LoopSim.Pct(rs.Sum(x => (double)x.SlotsMax) / slots)} | {F(Pctl(rec, 0.5))}일 ({F(Pctl(rec, 0.1))} / {F(Pctl(rec, 0.9))}) [{rec.Count} / {rs.Sum(x => x.Unrecovered)}] | {rs.Average(x => (double)x.HopeCircuits).ToString("F2", CultureInfo.InvariantCulture)} | {rs.Average(x => (double)x.HopeReadyDays).ToString("F1", CultureInfo.InvariantCulture)} |");
                    Console.WriteLine($"hope study {name} {v}: >=80 {LoopSim.Pct(rs.Sum(x => (double)x.SlotsHigh) / slots)}, =100 {LoopSim.Pct(rs.Sum(x => (double)x.SlotsMax) / slots)}");
                }
            LifeSense.On = false;
        }

        static Func<Loop, Action<Loop>> EBot => _ => { var p = LoopSim.Make("E"); return l => p.PlaySlot(l); };

        /// <summary>The sim F bot (100x all-in, whole bank to the exchange, job in the evenings), one slot at a time.</summary>
        static Func<Loop, Action<Loop>> FBot => _ => l =>
        {
            var d = l.Data;
            LifeSense.Tick(l);
            if (!l.DomesticLocked && d.domesticCash > 0) l.TransferDomesticToOverseas(d.domesticCash);
            if (!d.hasPosition && d.overseasCash > 0 && l.Do(new CoinOpenAction(Direction.Long, 100, d.overseasCash)).Ok) return;
            if (!d.hasPosition && d.overseasCash <= 0 && d.domesticCash <= 0 && d.pendingTransfers.Count == 0 && d.bankCash > 0)
            {
                long amt = LifeSense.CapMove(l, d.bankCash);
                if (amt > 0) l.TransferBankToDomestic(amt);
            }
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
            sb.AppendLine("## H 측정 — 파산 선언 순간 (보고 전용·규칙 변경 없음; LifeSense 행은 742fe1d 회신)");
            sb.AppendLine();
            sb.AppendLine("파산 on + V3 기본. E 봇(청산 체인)·F 봇(100배 올인 반복)을 첫 E9 파산 선언까지 굴린 뒤(E 최대 730일, F 최대 800일), 그 상태 그대로 **알바만 하는 봇**이 14일 유예 안에 밀린 월세를 내는지 본다(열린 포지션·거래소 잔고는 손대지 않음, 굶음 적용). **LifeSense** 행: 은행에 (다음 월세 + 식비 30일분 = 810,000원)을 남기고만 거래소로 옮기고, 저녁 식비 전 은행이 모자라면 거래소에서 출금을 시작하고, 파산 선언 시 지갑 여유분으로 밀린 월세를 채울 수 있으면 즉시 출금한다(각 이체는 정상 지연 규칙). 평균 굶음은 관찰 구간(선언 전 + 버티기 14일) 전체. 청산 → 선언 일수 = 선언일 − 마지막 청산일(그 전에 청산이 있었던 런만).");
            sb.AppendLine();
            sb.AppendLine("| 봇 | 파산 선언한 런 | 선언 시점 mental p10 / p50 / p90 | 청산 → 선언 일수 중앙 (p10 / p90, n) | 선언일 중앙 | 버티기 해제 성공률 | 런당 평균 굶음 |");
            sb.AppendLine("|---|---|---|---|---|---|---|");
            foreach (var (name, bot, maxDay, sense) in new[] { ("E 청산 체인", EBot, 730, false), ("F 100배 올인 반복", FBot, 800, false), ("E + LifeSense", EBot, 730, true), ("F + LifeSense", FBot, 800, true) })
            {
                LifeSense.On = sense;
                var rs = new List<Declared>(runs);
                for (int seed = 1; seed <= runs; seed++) rs.Add(Study(bot, seed, maxDay));
                LifeSense.On = false;
                var dec = rs.Where(x => x.Happened).ToList();
                var mental = dec.Select(x => x.Mental).ToList();
                var gap = dec.Where(x => x.DaysSinceLiquidation >= 0).Select(x => x.DaysSinceLiquidation).ToList();
                var day = dec.Select(x => x.Day).ToList();
                string rel = dec.Count > 0 ? LoopSim.Pct(dec.Count(x => x.Released) / (double)dec.Count) : "—";
                double starved = rs.Average(x => (double)x.Starved);
                sb.AppendLine($"| {name} | {dec.Count} / {runs} | {F(Pctl(mental, 0.1))} / {F(Pctl(mental, 0.5))} / {F(Pctl(mental, 0.9))} | {F(Pctl(gap, 0.5))}일 ({F(Pctl(gap, 0.1))} / {F(Pctl(gap, 0.9))}, n={gap.Count}) | Day {F(Pctl(day, 0.5))} | {rel} | {starved.ToString("F1", CultureInfo.InvariantCulture)} |");
                Console.WriteLine($"bankruptcy study {name}: declared {dec.Count}/{runs}, mental p50 {F(Pctl(mental, 0.5))}, released {rel}, starved {starved:F1}");
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
