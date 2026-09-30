using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using AllIn.Core;
using AllIn.Holdem;

namespace AllIn.Harness.Sim.Holdem
{
    /// <summary>Sim H (DESIGN.md C-v2, report-only): 3 kinds x 3 hero bots on the win ladder.</summary>
    public static class LadderSim
    {
        /// <summary>Pocket pairs, any ace, or two broadway cards.</summary>
        public sealed class GutBot : ILadderPlayer
        {
            public bool AllIn(LadderHand h)
            {
                int r0 = Card.Rank(h.Hole0), r1 = Card.Rank(h.Hole1);
                return r0 == r1 || r0 == 12 || r1 == 12 || (r0 >= 8 && r1 >= 8);
            }
        }

        public sealed class RandomBot : ILadderPlayer
        {
            readonly Xoshiro128 _rng;
            public RandomBot(long seed) { _rng = new Xoshiro128((ulong)seed * 104729UL + 1UL); }
            public bool AllIn(LadderHand h) => _rng.Chance(0.5);
        }

        sealed class Row
        {
            public LadderStage Stage;
            public int Wins;
            public int Hands, Pushes, Calls, CallWins, Steals, FreeBlinds, Folds, Reentries, BadBeats, BbFacing, BbCalls;
            public List<double> BustBb;
        }

        static Row Play(TournamentKind kind, string bot, long seed, bool ante = true, bool avg = true)
        {
            var cfg = LadderConfig.For(kind, 4);
            cfg.BbAnte = ante;
            cfg.AverageStacks = avg;
            var l = new Ladder(cfg, new Xoshiro128((ulong)seed)) { LuckRng = new Xoshiro128((ulong)seed ^ 0x1AC1AC1AC1ACUL) };
            ILadderPlayer p = bot == "직감" ? new GutBot() : bot == "차트" ? (ILadderPlayer)ChartPlayer.Instance : new RandomBot(seed);
            l.RunToEnd(p);
            var r = l.Result;
            int pushes = l.History.Count(h => h.ChoseAllIn && h.Kind != HandKind.FreeBlind);
            return new Row
            {
                Stage = r.Stage, Wins = r.Wins, Hands = r.HandsPlayed, Pushes = pushes, Calls = r.CalledAllIns, CallWins = r.CalledWins,
                Steals = r.Steals, FreeBlinds = r.FreeBlinds, Folds = r.Folds, Reentries = r.Reentries, BadBeats = r.BadBeats,
                BbFacing = r.BbFacing, BbCalls = r.BbCalls, BustBb = r.BustBb,
            };
        }

        static string BustMedian(List<Row> l)
        {
            var all = l.SelectMany(r => r.BustBb).OrderBy(v => v).ToList();
            return all.Count == 0 ? "—" : all[all.Count / 2].ToString("F1", CultureInfo.InvariantCulture);
        }

        static string P(double v) => (v * 100).ToString("F1", CultureInfo.InvariantCulture) + "%";
        static string F(double v) => v.ToString("F1", CultureInfo.InvariantCulture);

        public static bool Run(int runs, string path)
        {
            var kinds = new[] { TournamentKind.Pub, TournamentKind.Satellite, TournamentKind.Main };
            var bots = new[] { "직감", "차트", "랜덤" };
            var sb = new StringBuilder();
            sb.AppendLine("# SIM_HOLDEM — 홀덤 v2 (올인 or 폴드 + 승수 사다리) — sim H");
            sb.AppendLine();
            sb.AppendLine("> 생성: `dotnet run --project tools/dotnet-harness -- sim holdem`. 손으로 고치지 말 것. 수치 출처는 md/DESIGN.md C-v2. 보고 전용 — 수용 기준·수치 변경 없음.");
            sb.AppendLine($"> 대회 종류별 × 봇 3종 × {runs}회(시드 1..{runs}). 리엔트리는 가능한 한 항상 한다. 직감 봇 = 포켓페어·A 포함·브로드웨이 두 장만 올인 / 차트 봇 = HUD 4 권고(푸시 상위 N%) 그대로 / 랜덤 봇 = 매 핸드 50% 올인.");
            sb.AppendLine("> **이 문서는 9인 풀 테이블 v1 시뮬레이션을 대체한다.** v1 결과는 `md/SIM_HOLDEM_V1.md`에 기록으로 남아 있다(게임 경로에서 쓰이지 않음).");
            sb.AppendLine();
            Dictionary<(TournamentKind, string), List<Row>> Build(bool ante, bool avg)
            {
                var d = new Dictionary<(TournamentKind, string), List<Row>>();
                foreach (var k in kinds)
                    foreach (var b in bots)
                    {
                        var l = new List<Row>(runs);
                        for (int s = 1; s <= runs; s++) l.Add(Play(k, b, s, ante, avg));
                        d[(k, b)] = l;
                    }
                return d;
            }
            var results = Build(true, true);

            sb.AppendLine("## 단계 도달률 (대회 1회 = 리엔트리 포함 전체)");
            sb.AppendLine();
            sb.AppendLine("| 대회 | 봇 | 단계 도달률 |");
            sb.AppendLine("|---|---|---|");
            foreach (var k in kinds)
                foreach (var b in bots)
                {
                    var l = results[(k, b)];
                    string cell;
                    if (k == TournamentKind.Pub) cell = $"3승 참가권 {P(l.Count(r => r.Stage >= LadderStage.Seat) / (double)runs)}";
                    else if (k == TournamentKind.Satellite) cell = $"4승 메인 시트 {P(l.Count(r => r.Stage >= LadderStage.Seat) / (double)runs)}";
                    else cell = $"5승 머니인 {P(l.Count(r => r.Stage >= LadderStage.MoneyIn) / (double)runs)} / 6승 FT {P(l.Count(r => r.Stage >= LadderStage.FinalTable) / (double)runs)} / 7승 입상 {P(l.Count(r => r.Stage >= LadderStage.Podium) / (double)runs)} / 8승 우승 {P(l.Count(r => r.Stage >= LadderStage.Champion) / (double)runs)}";
                    sb.AppendLine($"| {Texts.HoldemKindNames[(int)k]} | {b} | {cell} |");
                }

            sb.AppendLine();
            sb.AppendLine("## 핸드·올인 통계 (런당 평균)");
            sb.AppendLine();
            sb.AppendLine("| 대회 | 봇 | 총 핸드 | 올인 시도 | 스틸 성공 | 콜 받은 올인 | 콜 올인 승률 | 스틸 비율(올인 중) | 탈락 시 스택 중앙값(bb) | BB 방어 콜 비율 | 폴드 | 리엔트리 | 배드빗 |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (var k in kinds)
                foreach (var b in bots)
                {
                    var l = results[(k, b)];
                    double pushes = l.Sum(r => r.Pushes), calls = l.Sum(r => r.Calls), cw = l.Sum(r => r.CallWins), st = l.Sum(r => r.Steals);
                    sb.AppendLine($"| {Texts.HoldemKindNames[(int)k]} | {b} | {F(l.Average(r => r.Hands))} | {F(pushes / runs)} | {F(st / runs)} | {F(calls / runs)} | {(calls > 0 ? P(cw / calls) : "—")} | {(pushes > 0 ? P(st / pushes) : "—")} | {BustMedian(l)} | {(l.Sum(r => r.BbFacing) > 0 ? P(l.Sum(r => r.BbCalls) / (double)l.Sum(r => r.BbFacing)) : "—")} | {F(l.Average(r => r.Folds))} | {F(l.Average(r => r.Reentries))} | {F(l.Average(r => r.BadBeats))} |");
                }

            sb.AppendLine();
            sb.AppendLine("## 참고 기준 대비 (판단은 사용자)");
            sb.AppendLine();
            bool chartBeatsGut = true;
            var stageRate = new Func<TournamentKind, string, LadderStage, double>((k, b, s) => results[(k, b)].Count(r => r.Stage >= s) / (double)runs);
            var checks = new List<(TournamentKind, LadderStage)> { (TournamentKind.Pub, LadderStage.Seat), (TournamentKind.Satellite, LadderStage.Seat), (TournamentKind.Main, LadderStage.MoneyIn), (TournamentKind.Main, LadderStage.FinalTable), (TournamentKind.Main, LadderStage.Podium), (TournamentKind.Main, LadderStage.Champion) };
            sb.AppendLine("| 기준 | 결과 |");
            sb.AppendLine("|---|---|");
            foreach (var (k, s) in checks)
            {
                double c = stageRate(k, "차트", s), g = stageRate(k, "직감", s);
                // two-proportion z
                double p = (c + g) / 2, se = Math.Sqrt(2 * p * (1 - p) / runs), z = se > 0 ? (c - g) / se : 0;
                if (!(c > g && z > 1.96)) chartBeatsGut = false;
                sb.AppendLine($"| 차트 봇 > 직감 봇 유의({Texts.HoldemKindNames[(int)k]} {s}) | 차트 {P(c)} vs 직감 {P(g)} (z = {z:F1}) {(c > g && z > 1.96 ? "O" : "X")} |");
            }
            sb.AppendLine($"| 모든 단계에서 차트 > 직감 (유의) | {(chartBeatsGut ? "O" : "X")} |");
            sb.AppendLine($"| 메인 우승 도달(차트 봇) ≈ 1% 안팎 | {P(stageRate(TournamentKind.Main, "차트", LadderStage.Champion))} |");
            var pubChart = results[(TournamentKind.Pub, "차트")];
            var pubGut = results[(TournamentKind.Pub, "직감")];
            sb.AppendLine($"| 펍 1판 ≈ 5분 (평균 핸드 수로 추정: 핸드당 약 3초 폴드/스틸, 콜 받은 올인 약 12초) | 차트 봇 {F(pubChart.Average(r => r.Hands))}핸드·콜 {F(pubChart.Average(r => r.Calls))}회 → 약 {F((pubChart.Average(r => r.Hands - r.Calls) * 3 + pubChart.Average(r => r.Calls) * 12) / 60)}분 / 직감 봇 {F(pubGut.Average(r => r.Hands))}핸드 → 약 {F((pubGut.Average(r => r.Hands - r.Calls) * 3 + pubGut.Average(r => r.Calls) * 12) / 60)}분 |");

            sb.AppendLine();
            sb.AppendLine("## 격차 분리 (BB 앤티·BB 스틸 대응 / 평균 스택 기준 상대 스택 — 각각 넣고 뺀 결과)");
            sb.AppendLine();
            sb.AppendLine("| 설정 | 펍 3승 차트 / 직감 (z) | 새틀 4승 차트 / 직감 (z) | 메인 우승 차트 / 직감 (z) | 펍 평균 핸드 (차트) |");
            sb.AppendLine("|---|---|---|---|---|");
            var configs = new (string name, bool ante, bool avg)[] { ("rev.1 (둘 다 없음)", false, false), ("1만 (앤티)", true, false), ("2만 (평균 스택)", false, true), ("1+2 (현재)", true, true) };
            foreach (var (name, ante, avg) in configs)
            {
                var r = ante && avg ? results : Build(ante, avg);
                string Cell(TournamentKind k, LadderStage st)
                {
                    double c = r[(k, "차트")].Count(x => x.Stage >= st) / (double)runs, g = r[(k, "직감")].Count(x => x.Stage >= st) / (double)runs;
                    double pp = (c + g) / 2, se = Math.Sqrt(2 * pp * (1 - pp) / runs), z = se > 0 ? (c - g) / se : 0;
                    return $"{P(c)} / {P(g)} ({z:F1})";
                }
                sb.AppendLine($"| {name} | {Cell(TournamentKind.Pub, LadderStage.Seat)} | {Cell(TournamentKind.Satellite, LadderStage.Seat)} | {Cell(TournamentKind.Main, LadderStage.Champion)} | {F(r[(TournamentKind.Pub, "차트")].Average(x => x.Hands))} |");
            }
            System.IO.File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            Console.WriteLine("holdem v2 sim (H): Pub chart " + P(stageRate(TournamentKind.Pub, "차트", LadderStage.Seat)) + " gut " + P(stageRate(TournamentKind.Pub, "직감", LadderStage.Seat)) +
                              " | Main champion chart " + P(stageRate(TournamentKind.Main, "차트", LadderStage.Champion)) + " → " + path);
            return true;
        }
    }
}
