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
    /// <summary>
    /// Measurement only (no acceptance criterion): how much does "studying" pay? Puts one test player in the hero seat
    /// against the default 8-seat roster, no re-entry, and reports win rate, in-the-money rate and ROI per kind.
    /// </summary>
    public static class SkillMeasure
    {
        /// <summary>Follows the study-level-4 HUD: push/fold chart at ≤ 15bb, otherwise Pro play without the Station exploit.</summary>
        sealed class ChartBot : IDecider
        {
            readonly Bot _bot = new Bot(new Personality { Id = PersonalityId.Pro, BluffChance = 0.10, ExactPushFold = true, ExploitsStations = false });
            public PlayerAction Decide(Tournament t, int seat) => _bot.Decide(t, seat);
        }

        /// <summary>"Study level 0": plays only the top 20% of hands preflop (no chart), Station postflop.</summary>
        sealed class Study0Bot : IDecider
        {
            const double PlayTopPercent = 20, OpenBb = 2.5;
            readonly Bot _station = new Bot(Personality.For(PersonalityId.Station));

            public PlayerAction Decide(Tournament t, int seat)
            {
                var h = t.Hand;
                if (h.Street != Street.Preflop) return _station.DecidePostflop(t, seat);
                var legal = h.LegalFor(seat);
                if (HandRanks.Percentile(h.Hole0[seat], h.Hole1[seat]) > PlayTopPercent)
                    return legal.CanCheck ? PlayerAction.Check : PlayerAction.Fold;
                if (h.RaisesThisStreet > 0) return PlayerAction.Call;
                if (h.Stack[seat] <= 2 * h.PotTotal) return PlayerAction.AllIn;
                return PlayerAction.RaiseTo((long)(OpenBb * h.BigBlind));
            }
        }

        sealed class Row
        {
            public string Player;
            public TournamentKind Kind;
            public int Runs, Wins, Cashes;
            public double PrizeValue; // summed, in won
            public double Fee;        // per entry, in won (0 = entered with a seat)
        }

        public static string Run(int runs)
        {
            var players = new (string name, Func<IDecider> make)[]
            {
                ("ChartBot (HUD 4 권고 그대로)", () => new ChartBot()),
                ("StudyLevel 0 흉내 (상위 20%만, 포스트플랍 Station)", () => new Study0Bot()),
            };
            var rows = new List<Row>();
            foreach (var (name, make) in players)
            {
                var main = Measure(name, TournamentKind.Main, runs, make, 0);
                double mainSeatValue = main.PrizeValue / main.Runs; // what a Main seat is worth to this player
                rows.Add(Measure(name, TournamentKind.Pub, runs, make, HoldemRules.SatelliteFee));
                rows.Add(Measure(name, TournamentKind.Satellite, runs, make, mainSeatValue));
                rows.Add(main);
            }

            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("## 실력 측정 (측정만 — 수용 기준 아님)");
            sb.AppendLine();
            sb.AppendLine($"히어로 자리 1석 + 기본 로스터 8석, 리엔트리 없음, Kind별 {runs}회. 비교 기준선: 우승률 1/9 = 11.1%.");
            sb.AppendLine();
            sb.AppendLine("ROI = (획득 상금 가치 − 참가비) / 참가비. 상금 가치: Pub 1위 시트 = 새틀라이트 참가비 100,000원, 펍 포인트 = 0.");
            sb.AppendLine("새틀라이트 1위 시트 = 메인 참가비가 정해져 있지 않아, **같은 플레이어의 메인 평균 세후 상금**으로 환산(아래 Main 행). Main은 시트로 입장해 참가비가 없으므로 ROI 대신 평균 세후 상금.");
            sb.AppendLine();
            sb.AppendLine("| 플레이어 | Kind | 우승률 | 상금권 진입률 | 참가비 | 평균 상금 가치 | ROI |");
            sb.AppendLine("|---|---|---|---|---|---|---|");
            foreach (var r in rows)
            {
                double avg = r.PrizeValue / r.Runs;
                string fee = r.Kind == TournamentKind.Main ? "시트" : Fmt.Won((long)r.Fee);
                string roi = r.Kind == TournamentKind.Main ? "—" : Fmt.Pct((avg - r.Fee) / r.Fee);
                sb.AppendLine($"| {r.Player} | {r.Kind} | {Fmt.Pct(r.Wins / (double)r.Runs)} | {Fmt.Pct(r.Cashes / (double)r.Runs)} | {fee} | {Fmt.Won((long)Math.Round(avg))} | {roi} |");
            }
            Console.WriteLine("skill measure: " + string.Join(" | ", rows.Select(r => r.Kind + " " + (r.Player.StartsWith("Chart") ? "chart" : "s0") + " win " + Fmt.Pct(r.Wins / (double)r.Runs))));
            return sb.ToString();
        }

        /// <param name="seatValue">Pub: a satellite ticket's value. Satellite: a main-event seat's value. Main: unused.</param>
        static Row Measure(string name, TournamentKind kind, int runs, Func<IDecider> make, double seatValue)
        {
            var row = new Row { Player = name, Kind = kind, Runs = runs };
            var cfg0 = TournamentConfig.For(kind);
            row.Fee = cfg0.EntryFee;
            for (int seed = 1; seed <= runs; seed++)
            {
                var rng = new Xoshiro128((ulong)seed * 104729 + (ulong)kind * 31);
                var t = new Tournament(TournamentConfig.For(kind), rng, BotFactory.Instance, make()) { StopWhenHeroOut = true, HeroBotReentry = _ => false };
                t.RunToEnd();
                var r = t.Result;
                if (r.Finish == 1) row.Wins++;
                if (r.Prize.IsPaid) row.Cashes++;
                switch (r.Prize.Type)
                {
                    case PrizeType.Seat: row.PrizeValue += seatValue; break;
                    case PrizeType.Cash: row.PrizeValue += r.Prize.AfterTax; break;
                }
            }
            return row;
        }
    }
}
