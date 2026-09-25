using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using AllIn.Core;
using AllIn.Holdem;
using AllIn.Holdem.Brain;

namespace AllIn.Harness.Sim.Holdem
{
    /// <summary>
    /// AI-only 9-handed tournaments (DESIGN.md C7). The hero seat is a bot with a random personality, so all nine seats are
    /// symmetric; per-personality win rate = wins / entrants of that personality (refills included).
    /// </summary>
    public static class HoldemSim
    {
        static readonly PersonalityId[] AllPersonalities = { PersonalityId.Tight, PersonalityId.Station, PersonalityId.Maniac, PersonalityId.Pro };

        sealed class KindStats
        {
            public TournamentKind Kind;
            public readonly List<int> Hands = new List<int>();
            public readonly List<int> ShortAtHand = new List<int>(); // first hand with median effective ≤ 10bb (0 = never)
            public readonly List<int> FinalTableAt = new List<int>();
            public readonly List<int> Reentries = new List<int>();
            public readonly Dictionary<PersonalityId, int> Wins = new Dictionary<PersonalityId, int>();
            public readonly Dictionary<PersonalityId, int> Entrants = new Dictionary<PersonalityId, int>();

            public double WinRate(PersonalityId p) => Entrants.TryGetValue(p, out int n) && n > 0 ? Wins.GetValueOrDefault(p) / (double)n : 0;
        }

        public static bool Run(int runs, string reportPath)
        {
            var all = new List<KindStats>();
            foreach (TournamentKind kind in new[] { TournamentKind.Pub, TournamentKind.Satellite, TournamentKind.Main })
            {
                var ks = new KindStats { Kind = kind };
                foreach (var p in AllPersonalities) { ks.Wins[p] = 0; ks.Entrants[p] = 0; }
                for (int seed = 1; seed <= runs; seed++) PlayOne(kind, seed, ks);
                all.Add(ks);
                Console.WriteLine($"holdem sim {kind}: avg hands {ks.Hands.Average():F1}");
            }

            var pub = all[0];
            double pubHands = pub.Hands.Average();
            double pubShort = pub.ShortAtHand.Where(x => x > 0).DefaultIfEmpty(0).Average();
            double pubShortMedian = Median(pub.ShortAtHand.Where(x => x > 0).Select(x => (double)x));
            bool handsOk = pubHands >= 25 && pubHands <= 45;
            bool shortOk = pubShortMedian <= 20;
            var skill = all.Select(k => (k, pro: k.WinRate(PersonalityId.Pro), station: k.WinRate(PersonalityId.Station))).ToList();
            bool skillOk = skill.Where(x => x.k.Kind != TournamentKind.Main).All(x => x.pro > x.station && x.pro < 0.40);

            var sb = new StringBuilder();
            sb.AppendLine("# SIM_HOLDEM — 홀덤 토너먼트");
            sb.AppendLine();
            sb.AppendLine("> 생성: `dotnet run --project tools/dotnet-harness -- sim holdem`. 손으로 고치지 말 것. 수치 출처는 md/DESIGN.md.");
            sb.AppendLine($"> AI 9인(히어로 자리도 무작위 성격 봇), Kind별 {runs}회, 시드 1..{runs}.");
            sb.AppendLine();
            sb.AppendLine("## 수용 기준");
            sb.AppendLine();
            sb.AppendLine("| 기준 | 결과 | 판정 |");
            sb.AppendLine("|---|---|---|");
            sb.AppendLine($"| Pub 평균 총 핸드 25~45 | {F1(pubHands)} | {Pass(handsOk)} |");
            sb.AppendLine($"| Pub 유효스택 중앙값 ≤ 10bb 도달 ≤ 20핸드 | 중앙값 {F1(pubShortMedian)}핸드 (평균 {F1(pubShort)}) | {Pass(shortOk)} |");
            foreach (var x in skill)
                sb.AppendLine($"| {x.k.Kind}: Pro 우승률 > Station, Pro < 40% | Pro {Fmt.Pct(x.pro)} / Station {Fmt.Pct(x.station)} | {(x.k.Kind == TournamentKind.Main ? "(참고)" : Pass(x.pro > x.station && x.pro < 0.40))} |");
            sb.AppendLine("| 사람 플레이 Pub 1판 8~12분 | 사람 확인 | — |");
            sb.AppendLine();
            sb.AppendLine("## Kind별");
            sb.AppendLine();
            sb.AppendLine("| Kind | 평균 총 핸드 | 핸드 p10 / 중앙값 / p90 | ≤10bb 도달 핸드(중앙값) | 리엔트리 평균(전원) | 파이널 테이블 도달 핸드 |");
            sb.AppendLine("|---|---|---|---|---|---|");
            foreach (var k in all)
            {
                var hands = k.Hands.Select(x => (double)x).ToList();
                string ft = k.FinalTableAt.Count > 0 ? F1(k.FinalTableAt.Average()) : "—";
                sb.AppendLine($"| {k.Kind} | {F1(hands.Average())} | {F1(Pct(hands, 0.1))} / {F1(Median(hands))} / {F1(Pct(hands, 0.9))} | " +
                              $"{F1(Median(k.ShortAtHand.Where(x => x > 0).Select(x => (double)x)))} | {F2(k.Reentries.Average())} | {ft} |");
            }
            sb.AppendLine();
            sb.AppendLine("## 성격별 우승률 (우승 / 참가자 수)");
            sb.AppendLine();
            sb.AppendLine("| Kind | Tight | Station | Maniac | Pro | Pro ÷ Station |");
            sb.AppendLine("|---|---|---|---|---|---|");
            foreach (var k in all)
            {
                double pro = k.WinRate(PersonalityId.Pro), st = k.WinRate(PersonalityId.Station);
                sb.AppendLine($"| {k.Kind} | {Fmt.Pct(k.WinRate(PersonalityId.Tight))} | {Fmt.Pct(st)} | {Fmt.Pct(k.WinRate(PersonalityId.Maniac))} | {Fmt.Pct(pro)} | " +
                              $"{(st > 0 ? F2(pro / st) : "∞")} |");
            }
            sb.AppendLine();
            sb.AppendLine("참고: 9인 테이블에서 무작위 기대 우승률 = 11.1%. Main은 필드 200명 기준(충원 포함 참가자 수로 나눔).");
            sb.Append(SkillMeasure.Run(runs));

            Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
            File.WriteAllText(reportPath, sb.ToString().Replace("\r\n", "\n"));
            Console.WriteLine($"holdem sim: Pub hands {F1(pubHands)} {Pass(handsOk)} | ≤10bb @ {F1(pubShortMedian)} {Pass(shortOk)} | skill {Pass(skillOk)} → {reportPath}");
            return handsOk && shortOk && skillOk;
        }

        static void PlayOne(TournamentKind kind, int seed, KindStats ks)
        {
            var rng = new Xoshiro128((ulong)seed * 7919 + (ulong)kind);
            var heroPersonality = AllPersonalities[rng.NextInt(AllPersonalities.Length)];
            var t = new Tournament(TournamentConfig.For(kind), rng, BotFactory.Instance, BotFactory.Instance.Create(heroPersonality), heroPersonality);
            t.HeroBotReentry = _ => t.Rng.Chance(HoldemRules.AiReentryChance);
            t.RunToEnd();

            ks.Hands.Add(t.HandNumber);
            int shortAt = t.MedianEffectiveBb.FindIndex(x => x <= 10);
            ks.ShortAtHand.Add(shortAt >= 0 ? shortAt + 1 : 0);
            if (t.HandsToFinalTable > 0) ks.FinalTableAt.Add(t.HandsToFinalTable);
            ks.Reentries.Add(t.AllEntrants.Sum(e => e.Reentries));
            foreach (var e in t.AllEntrants) ks.Entrants[e.Personality]++;
            ks.Wins[t.Winner.Personality]++;
        }

        static double Median(IEnumerable<double> xs) => Pct(xs.ToList(), 0.5);

        static double Pct(List<double> xs, double p)
        {
            if (xs.Count == 0) return 0;
            var s = xs.OrderBy(x => x).ToList();
            return s[(int)Math.Round(p * (s.Count - 1))];
        }

        static string F1(double v) => v.ToString("F1", CultureInfo.InvariantCulture);
        static string F2(double v) => v.ToString("F2", CultureInfo.InvariantCulture);
        static string Pass(bool ok) => ok ? "PASS" : "FAIL";
    }
}
