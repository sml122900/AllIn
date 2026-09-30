# STATUS

_Single session-to-session context doc. Newest state at top._

## Current
- Branch: `feat/loop-proto`. Remote `origin` = https://github.com/sml122900/AllIn (private). `main` and `feat/loop-proto` pushed 2026-09-25.
- Stage: **v0.4 stage ① "원양어선·연속 인생" done** (2026-09-30) — see Log. The "one life 21→70" direction is in `md/VISION.md`; stages ②~⑥ are NOT started (do not pre-build). Rules: DESIGN.md Part D. Human play of v0.3 (Stage 8) and hold'em (Part C) is still pending — observations come from the user.
- Unity compile: **verified** 2026-09-30 via batchmode (editor closed; unity-mcp had no tools this session): 0 errors, **204/204 EditMode tests** (202 Core+Holdem via the harness + 2 Game). Win64 build succeeds. Dev capture (`-allinShots`) was NOT re-run after the reply changes below.

## Next
- **Hold'em v2 human play (user):** `Holdem.unity` → Play (or the build with the dev flag), pick Kind / study / seed. Judge: is all-in-or-fold fun, does the ladder feel fair, is a Pub run ~5 min. Sim H says the chart bot does not beat the gut bot — see "Hold'em v2" section; numbers untouched, the user decides.
- **Human play (user), v0.4:** play a run, press [계속 살기] at Day 30, then go broke (e.g. 100x all-in) and watch E9 → 원양어선 → 귀항. See "v0.4 observations" below for what to judge. Numbers stay as they are — tuning is the user's call.
- Stage ② (per-phase time resolution + ending skeleton) is next on the roadmap — only when the user asks.
- **Human play (user), v0.3 on top of Stage 8 + C7:** open `Assets/_Project/Scenes/Loop.unity`, press Play (or run `Builds/Win64/AllIn.exe`), play one 30-day run. On Day 1, move money 은행 → 국내 거래소 → 해외 지갑 (새 "거래소" 버튼) before the first coin trade — otherwise coin is unfunded. Watch for: the 관전 scene at the liquidation edge, 분노 tint + recovery calculator after a liquidation, the 24시간 잠김 countdown, and 본절(E8) if price recovers within 10 days. Record observations below — no number changes (tuning is the user's call). Target 10–15 min.
- Part C (hold'em): **C1–C7 done + C7-reply decisions A–E applied (2026-09-25); still stopped for human play.** To play: open `Assets/_Project/Scenes/Holdem.unity` → Play (or run the build and load the Holdem scene — the build starts in Loop; see note), pick Kind / study level / seed → 시작. Target Pub 8–12 min. Observations go below; numbers stay as they are.

## Decisions (confirmed by the user 2026-09-25)
- **1 action = 1 slot.** Job, rest/sleep, coin open, coin close each consume the slot. Free: looking at the coin screen, lotto purchase (not at night), popups. Opening/closing at night = all-nighter (no recovery, mental −5). The "can't open and close in the same slot" rule is a consequence, not a separate rule. E4 still fires on entering the coin screen (viewing is free). Sim D opens at night and closes in the first slot it can act in.
- **Lotto tax is progressive:** 22% up to 300M, 33% only on the part above. 1st prize net 1,373,000,000.
- **Fired day** still pays that day's wage (kept). **mental < 15 lost slots** count as rest, body +5 (kept).
- **Pixel Perfect:** URP built-in component (kept; the handoff's package name was outdated).
- **Margin math** (unchanged): committed C, notional N = C×lev, entry fee out of C → margin M = C − fee; liquidation at loss ≥ 0.9M.
- **E3 timing:** start of the slot after the one where E1/E2 appeared.
- **Hold'em provisional numbers approved as provisional** (2026-09-25, DESIGN.md keeps the 임시 labels): AI re-entry 50%, Pro p_bluff 0.10, pub points 2/1, Main refill Pro 60%. Off-table elimination 0.05 (calibrated to the ~60-hand final table).
- **All-in reveal:** when an all-in closes the betting, every live hand is turned up at once (left of the button) before the board; the aggressor order applies only to non-all-in river showdowns.
- **Tempo:** hands the hero folded out of play instantly with a one-line log; all-in hands still show (unless the 빠르게 toggle is on).
- **Editor-changed settings:** `UnityConnectSettings` reverted (services stay off). `ProjectSettings.preloadedAssets` += `Assets/InputSystem_Actions.inputactions` (the project-wide Input System actions asset, auto-registered by the Input System package) → committed.
- **Determinism:** PRNG bit-exact everywhere; saved numbers are all integral. Price uses Math.Exp etc., so harness vs editor could differ in the last bit of a tick before rounding to whole won — resume from a save is exact.

## Hold'em sim observations (C5, 1,000 × 3 kinds — observations only)
- Pub: 32.8 hands avg (p10 27 / p90 39), median effective stack ≤ 10bb by hand 13. Re-entries 2.3 per tournament (all seats).
- Skill signal is **thin in the Pub**: Pro 11.9% vs Station 11.5% (≈ 1 SE apart; passes the letter of the criterion only). Maniac wins most (12.3%) — the hyper-turbo rewards aggression. Satellite: Pro 12.9% vs Station 10.6%. Main (reference only): Pro 3.7% vs Station 3.9% — Pro does not beat Station there.
- Main: final table at hand 60.5 after recalibrating my own provisional off-table rate 0.08 → 0.05 (0.08 gave 47; the handoff asks for ~60). Main runs 123 hands on average (p90 182) — long for a human sitting.

## Skill measurement (decision B, measurement only — SIM_HOLDEM.md "실력 측정")
- ChartBot (follows the HUD-4 advice) vs a "study level 0" imitation (top 20% preflop, Station postflop); hero seat, default roster, no re-entry, 1,000 runs per kind.
- Pub: ChartBot wins 9.7% / cashes 28.2% / ROI −67.7%; Study-0 wins 3.2% / cashes 26.0% / ROI −89.3%. Satellite: 9.4% vs 1.8% (ROI −9.4% vs −86.4%, seat valued at the same player's Main average after-tax prize). Main: 0.6% vs 0.5% win, avg after-tax prize 963,300 vs 756,600.
- Studying clearly helps (3× the Pub win rate), but ChartBot is still **below the 1/9 baseline** in the Pub and Satellite. Caveat: the measured player never re-enters while the eight opponents re-enter 50% of the time, so it faces more chips than an average seat.
- Pub ROI is negative even for ChartBot mostly because 2nd/3rd pay points valued at 0.

## Findings
- Study effect is real: ChartBot vs Study-0 win rate 3x (Pub), 5x (Satellite), both significant.
- Chart play reaches a fair seat, not an edge (Nash push/fold is unexploitable, not exploitative). Human edge must come from exploiting opponents.
- Ladder economics as designed: Pub ROI ≈ −68% (points worth 0 → pub is a money sink), Satellite ≈ breakeven, Main = fair-seat lottery (0.6% win). Keep.
- Caveats: opponents re-enter 50% while measured player never does; Study-0 imitation is an arbitrary proxy for a novice human.

## Hold'em UI notes (C6–C7)
- Holdem.unity is empty; `HoldemApp` builds a boot menu (Kind · study level 0–4 · seed ±/random) and a 9-seat table in code, then replays the engine's event stream with delays (actions 0.5 s, flop/turn 0.55 s, river 0.9 s, all-in river 1.5 s + board shake, bad beat 0.5 s blackout + line, bubble / level / table-merge banners, re-entry popup on bust).
- HUD: 0 = own chips, others as chip piles (≥1.3× avg 많음 / ≥0.7× 보통 / else 적음), plain dealer button; 1 = all stacks with bb; 2 = position labels + highlighted button; 3 = pot odds on the call button; 4 = push/fold panel (range for this bb/position, or the call range facing a shove, and whether my hand is in).
- All-in hands are revealed the moment betting closes (decision A); with study level ≥ 3 each hand's equity shows after the reveal and after the flop/turn.
- "빠르게" toggle (top-right) and a 6-line hand log (bottom-left).
- Build note: the build's first scene is Loop; the Holdem scene is reachable in the build only via the dev flag. For human play use the editor.

## Open tuning (do NOT change until the user's Stage 8 / C7 play notes arrive)
- Hold'em skill signal in the Pub **passes on paper only**: Pro 11.9% vs Station 11.5% is inside statistical noise. (Maniac winning most is not considered a defect.)
- ~~Post-liquidation mental spiral~~ — **closed: resolved by V3** (sleep +5, 5-day streak +8; sim G, 2026-09-30).
- ~~Lotto miss −1 per purchase eroding mental / sim B 23/30 shifts~~ — **closed: resolved by V3** (sim B now works 30/30 shifts).

## Open design
- Candidate lever (undecided, after playtest): opponent-tendency notes unlocked by study/observation, so human exploitation (not HUD numbers) becomes the edge beyond fair seat.

## Things to look at during human play
- Mental falls fast: the dev-capture bot (50x every other night) hit mental 0 by day 3 → "can't get up" and 30% job mistakes most of the run. Real players may spiral the same way — intended or too harsh?
- E3 blocks input with a near-invisible scrim and darkens only the room area; the HUD stays readable. Check it reads as "forced".
- Log panel (right) overlaps the placeholder room art. Fine for placeholders.
- E5 shows at most 8 games; the rest are summarized ("외 N게임").
- v0.3: the watch scene always animates the full 12 sub-ticks from the top (not just from the trigger point) — an intentional read of the spec's "실시간 약 8초" timing, but check it doesn't feel slow once every liquidation-edge slot pays the full 8s.
- v0.3: recovery-calculator/margin-add UI is fixed-fraction buttons (25%/50%/전액, or a flat 25%-of-wallet margin add), not a slider — simplest thing that works within the time budget; a slider would read closer to the coin screen's existing commit slider if picked up later.
- v0.3: the exchange screen adds a 5th bottom-bar button ("은행") next to 코인 — check the 5-way split still reads fine at 1280×720 on a real screen (the dev-capture screenshots look fine but that's not a substitute for eyes on the actual build).
- v0.3 known rough edge: in the CoinScreen open form, when tilted **and** the overseas wallet is empty **and** mental < 30, the "입금하러 가기" button crowds the leverage row (x50/x100 partly covered) — both still clickable, just visually cramped. Low priority (placeholder-tier UI, narrow edge case) but worth a real layout pass if it bothers you in play.

## Sim E observations (청산 체인, 1,000 runs — report-only, no acceptance criteria, no numbers changed)
- **2026-09-29 수정**: 꼬리 청산 플래그가 원래 "12번째 서브틱 이전에 청산됨"으로 정의되어 있어 94.9%로 과대 집계됐다(사용자 지적, 반사 원리상 기대치는 ~50%). 올바른 정의(종가/최신 청산가 기준으로도 청산이었는지)로 고치니 **45.4%**로 정정 — 이론 기대치에 훨씬 가깝다. 새 정의: 청산된 슬롯의 종가(서브틱 12, 증거금 추가가 있었으면 최신 청산가 기준)가 그 자체로는 청산을 유발하지 않았을 때만 "꼬리 청산". 테스트 3건 추가(꼬리 / 종가도 청산가 너머 / 12번째 서브틱만 청산) — `SubTickTests.cs`.
- 레버리지별: 25배(첫 진입) 41.6% (849건 청산), 100배(재진입) 45.9% (6,709건 청산) — 둘 다 이론 기대치 근방. C(25배 단발)도 같은 41.6%로 교차 검증됨.
- 런당 평균 청산 7.56회, 30일 파산률 3.8%. 중앙값 163,082원 — 체인을 반복할수록 수수료(왕복 10%@100배)가 누적되어 대부분 잃는다.
- 관전 발생 9,727회(런당 p50 9 / p90 19), 생존율 22.3%: 관전까지 간 슬롯 중 약 4/5는 그대로 청산으로 이어진다(70%→90% 손실까지 거리가 짧음, 특히 고배율). 관전 중 봇 행동은 없음(헤드리스 sim은 `BeginWatchableAction`을 직접 구동하지 않고 항상 `Do()`/`Pass()`로 자동 완결 — 기도 0회 기준선).
- 청산의 31.2%가 잠김(24시간 룰) 기간 중 발생 — "돈이 묶인 채 청산 알림만 보는" 체인의 핵심 장면이 자연 발생함을 확인.
- 청산 대비 E8(본절) 78.8%, 그중 강화 변형 24.8%, 청산→E8까지 슬롯 수 p50 3 / p90 20 (n=5,957) — 가격이 평균 회귀하는 시뮬레이션 특성상 대부분 빠르게(중앙값 3슬롯≈0.6일) 회복한다.
- **A~D 재확인** (v0.3 직전 2026-09-25 대비): A 20,738,000원 그대로(정확히 동일, 코인 미사용). C 청산율 81.8%→84.9%(상승, 꼬리 청산 추가와 일치). D 중앙값 7,184,768→7,712,281원(상승했지만 "청산이 약해져서"가 아니라 구조적 이유 둘 — 지갑 이동 때문에 첫 거래가 하루 늦어져 도박 판수가 줄고, 10배는 청산에 필요한 단일 슬롯 변동폭(~9%)이 커서 애초에 잘 안 터짐(평균 청산 0.12/런). D도 서브틱 꼬리 청산을 정상적으로 타는 것은 "그중 꼬리 청산" 19.4%로 직접 확인. 상세 표는 md/SIM_REPORT.md "A~D 재보고" 참고.

## Sim observations (Stage 5, seeds 1..1000 — observations only, no numbers changed)
- A exact 20,738,000 every run. C liquidated 81.8% (margin over the 80% bar is thin — any volatility tweak could flip it). D median 10.1M (PASS).
- B (lotto 2/day + job) works only 23/30 shifts on average: weekly draw misses (−1 per losing purchase, ~14/week) plus job −4/sleep +3 push mental under 30 → 30% mistakes → fired. Mental erosion from lotto alone is enough to cost the job. Worth a look in human play.
- C when not liquidated has p90 ≈ 110M (25x long + slight upward drift of exp(t) prices).

## v0.4 observations (2026-09-30, no numbers changed)
- Sim F (bot: 100x all-in repeat, boards at the first E9): a cycle is very fast — median age at the 3rd return is **22.51** (~3 days of play per cycle + 3 × 180 days at sea). Because each cycle ends within days, the 30-day illness roll (first one is 30 days after a return) is almost never reached: **92.8% of runs saw 0 illnesses**, mean hospital bill 153,405원. Health debt only bites a player who survives 30+ days after a voyage. Open question for the user: intended?
- 3/1000 F runs never went bankrupt (bot holds a 100x long deep in profit and never closes it) — bot artifact, guard-capped.
- Bankruptcy line 534,000 is hit the next dawn after liquidating a whole-bank stake (assets ≈ 0). [버틴다] + the job (~40k/day net) needs ~13 days to climb back over 534k, longer than the 7-day forced-ship limit, so holding on from ~0 is nearly always futile.
- The voyage pays 15M (임시) for a 6-month absence; the old reset gave a fresh 20M. Money-wise bankruptcy is now a 15M payout; the price is time (0.5 yr), body max (−15) and health debt.

## v0.4 reply changes (2026-09-30, user-approved; DESIGN.md updated first)
- Ship has a price: illness clock is calendar-based (first return + 30, then never reset; checks that fall due at sea collapse into the one roll on return) **plus an immediate roll on every return**; body max floor 40 → 20; voyage pay −1.5M per voyage from the 3rd (13.5M, 12M, 10.5M, floor 9M).
- [버틴다] is alive: grace 7 → 14 days; E9 popup only on the declaration day and day 7, forced on day 14; HUD "파산 D+N / 14"; recovering over the line clears the state with a one-line log (`Texts.BankruptCleared`, placeholder).
- Interpretation: the "immediate roll on return" and "checks collapsed from the voyage" are ONE roll, not two. The clear notice is a log line, not a popup.
- Job-only recovery from ~0 assets clears 534,000 on exactly the forced dawn (held day 14): 554,400 vs 534,000, one evening shift every single day, no slack. Test `JobOnly_CanClearTheBankruptcyLineWithin14Days`. A missed shift or a mistake-fired day means the ship. Open for the user: intended tightness?
- Sim F now runs to 5 voyages (report-only): median age at 5th return 23.53; 0-illness share by voyage 90.4 / 69.3 / 44.5 / 24.3 / 11.2%; body max 85/70/55/40/25. A, C, D unchanged (20,738,000 / 84.9% / 7,712,281).
- Working tree: `Assets/_Recovery/` held only a default one-camera scene (0.unity, 2026-09-28) → deleted and gitignored. `CLAUDE.md` / `md/DEV.md` split adopted by the user and committed (4 removed items restored, VISION.md added to read order).

## Mental-recovery candidates (2026-09-30, report-only; defaults stay V0 until the user picks) — `md/SIM_MENTAL.md`, DESIGN.md D9
- `MentalTuning` (Core, per-Loop, unsaved): V0 current / V1 sleep +5 / V2 5-day streak +8 / V3 both. Streak state `workStreak`/`lastWorkDay` added to SaveData (maintained always, only acts under V2/V3). 7 new tests; harness 209, Unity 211/211, 0 errors. V0 results identical (A 20,738,000 / C 84.9% / D 7,712,281). `sim mental` writes SIM_MENTAL.md; the old Day-2-start sim G in SIM_REPORT was replaced by it.
- Grace 14, start day random 1–30, 1,000 runs. **Rent dominates, not mental, at mental ≥ 30:** the ceiling 53.3% = 16/30 start days (days 1–16); starting later puts the Day-31 rent (450,000) inside the 14 days and nothing can clear it. Rent-free control: V1/V3 100% at mental 30 and 40, V2 100% at 40 / 54.8% at 30.
- Mental 20: V0 0.4% → V1 1.7% → V2 0.7% → V3 18.7% (36.1% rent-free). Mental 30 (with rent): V0 1.0%, V1 53.3%, V2 29.4%, V3 53.3%. Mental 40: V0 27.1%, V1/V2/V3 53.3%.
- Side effects: A/C/D pass for all variants; A 20,738,000 exact for V1–V3. D hope circuits per run 0.39 (V0/V2) → 0.46 (V1/V3); E 0.00. B works 23.0 → 30.0 shifts (V1/V3), 29.8 (V2).

## Sim G 버티기 — first version (2026-09-30, report-only; grace stays 14 — the user decides)
- Start: assets 0 right after the declaration, bot = evening shift daily, rest when refused, no coin/lotto; success = state cleared inside the grace. 1,000 runs per cell. Table in SIM_REPORT.md.
- mental 20: 14d 0.6% / 17d 0.6% / 21d 16.2% / 28d 30.3% / 42d 30.6% / **56d 53.8%** → ~50% needs roughly 50+ days. mental 40: 49.4% at 14–17d, 97.4% at 21d. mental 60: 100% everywhere.
- Steps in the curve (14=17, 28=35=42) are consistent with the Day-31 rent (450,000, D8 item 1) wiping out about 11 shifts of progress for runs that start early in the month; unverified hypothesis — the rent timing depends on when in the month bankruptcy hits.
- Bug found in my own bot while measuring: it stopped at the Day 30 settlement until it called ContinueLife (fixed; numbers above are after the fix).
- Added `Loop.ForcedAfterDays` (default `Rules.BankruptForcedAfterDays`, not saved) so the sim can vary the grace; no rule change.

## Hold'em v2 — all-in or fold + win ladder (2026-09-30, DESIGN.md C-v2; stopped for human play)
- Core `Ladder` (Holdem/Core/Ladder.cs): `LadderConfig.For(kind)`, `BeginHand()` → `Resolve(allIn)`, `LadderHand` records (deal fixed before the choice, so folding vs pushing never changes the next deals), `ChartPlayer`. `HoldemRunner.RunTournament` keeps its signature and now runs the ladder (`ILadderHooks` optional, default chart bot). `TournamentResult` gained Stage/Wins/Steals/FreeBlinds/Folds/CalledAllIns/CalledWins. Old 9-seat `Tournament`/`HandState`/`Bot` + tests untouched (not on the game path).
- Game: `LadderApp` owns Holdem.unity (boot menu Kind/Study/Seed kept, table for show, [올인]/[폴드], win dots, remaining field, blind countdown, bubble/stage banners, bad-beat blackout, re-entry prompt). Old `HoldemApp` is no longer hooked. `-allinHoldemShots` captures deal / called_reveal / call_won / call_survived / call_out / steal / reentry / bubble / stage / result — all shown, 0 exceptions. Unity 234/234 (harness 232).
- My interpretations where the spec was silent: the BB hand is a free +0.5bb with no choice (1 in 9 hands); SB fold −0.5bb, other folds free; SB steal nets +1.0bb; blinds already posted are folded into a called all-in (no separate settlement); positions cycle UTG·UTG·UTG·MP·MP·CO·BTN·SB·BB with the opponents behind = seats 1..n in order; opponent stack ratio drawn per hand, cards/board pre-dealt at BeginHand; seat replaced (fresh personality; Main refill Pro 60%) only when the hero beats them; re-entry = 15bb at the current level, wins reset, window = bust during levels 1–3, unlimited; level clock runs on total hands across re-entries; fields Pub 8 / Sat 16 / Main 200; bubble = the win before the first paying step; Podium (7 wins) draws 2nd/3rd prize; Pub's 2/3rd "points" are gone.
- Sim H (SIM_HOLDEM.md, 1,000 runs each; v1 record moved to SIM_HOLDEM_V1.md): reference checks all **fail**, reported as-is, numbers not touched. Chart bot is NOT better than the gut bot at any stage (Pub 3 wins 37.7% vs 38.1%, Sat 22.5% vs 24.7%, Main champion 2.1% vs 3.3%); random bot is worse (34.5%/19.6%/0.3%). Main champion for the chart bot is 2.1%, above the ~1% target. Pub run ≈ 28 hands ≈ 2 min at my per-hand time guess (target ~5). Cause (hypothesis, unverified): PushFoldTable clamps to its 15bb column above 15bb, so the chart advice is tight exactly when the stack is big, while callers' ranges stay wide-ish; and losing while covering keeps the wins, so the ladder rewards surviving stacks more than the chart's fold equity. Called all-ins are won 43–44% overall (callers hold stronger hands).

## Hold'em v2 rev.2 (2026-09-30, user judgment on sim H) — DESIGN.md C-v2
- Added: BB ante 1bb + the BB hand faces a steal shove from the 8 seats ahead (call/fold; `HandKind.Facing`, fold −2bb), steal pot 2.5bb, opponent stacks = starting chips × field ÷ remaining × lognormal(1, σ 0.5), re-entry restarts at the starting chips (1,500), `PushFoldTable` 20/25/35bb columns (HUD 4 uses them, plus a call-advice line on Facing hands). `LadderConfig.BbAnte` / `AverageStacks` flags (default on) exist so sim H can separate the two changes. Tests 242 (harness) / 244 (Unity), 0 errors; build + capture OK incl. the new `bb_facing_shove` screenshot.
- **One deviation from the brief:** "nobody shoves at BB → net +1.5bb" does not balance the pot (my BB + ante come back; only the SB's 0.5 is new money), so I implemented **+0.5bb**. Consequence: an orbit of folds costs 2.5bb only when the BB hand is shoved at; otherwise 0. Say so if you meant something else.
- Sim H (1,000 runs): chart vs gut — Pub 3 wins 35.4% vs 31.5% (z 1.8, just short), Sat 21.2% vs 17.3% (z 2.2 ✓), Main money-in/FT/podium ✓ (z 2.8/2.4/2.1), Main champion 1.3% vs 0.8% (z 1.1). Chart Main champion 1.3% (target ≈1%). Pub ≈ 24 hands ≈ 1.9 min by my per-hand guess (target ~5).
- Separation: neither change alone opens a gap (ante only −0.9 / −0.7 / −1.0 z; average stacks only −1.3 / −0.9 / −1.0 z; rev.1 −0.2 / −1.1 / −1.8); together +1.8 / +2.2 / +1.1. They interact: ante makes folding cost, average stacks make stealing pay.
- BB defence call rate: chart 22–23%, gut 30%, random 52%. Median bust stack ≈ 2.4–3.1bb (the hero gets busted while short).

## V3 adopted + bankruptcy = unpaid rent + hunger (2026-09-30, user decisions; DESIGN.md D3/D9/B)
- **V3 is the rule** (`Rules.SleepMental = 5`, streak 5 days → +8; `MentalTuning` default = V3, V0–V2 kept only for comparison sims). Tests that pinned +3 updated.
- **Bankruptcy** now triggers on a rent day when the bank is below the rent: the whole bank goes in, the shortfall is `rentOwed`, E9 shows it plus "거래소에 N원 있음" (exchange money / positions never auto-sold). Released at the first dawn the bank covers the arrears in full; the rent calendar is kept; 14th-day dawn pays first, else forced ship; 7th-day re-confirm popup; HUD "월세 미납 D+N / 14". The old "assets < 534,000" test is deleted. `Loop.BankruptcyEnabled = false` (sims A~E) keeps the old rent behaviour (pays what is there).
- **Hunger** (all modes): evening food when the bank < 12,000 → no payment, body −10, mental −5, log line.
- Sim G (Day 31, bank 0, 1,000 runs, V3): start mental 20 → 7.6% released in 14 days, 30 → 21.7%, 40 → 82.7% (old V0: 2.6 / 2.6 / 10.0%). Release-time mental medians 43 / 53 / 63.
- Sim F (5 voyages, all 1,000 complete now): median age at return 21.58 / 22.15 / 22.73 / 23.31 / 23.88 (previous trigger 21.50 / 22.00 / 22.51 / 23.02 / 23.53).
- Sim A~E: A 20,738,000 exact, C 84.9% (both unchanged), **D median 7,712,281 → 5,980,032** (hunger: the D bot moves the whole bank to the exchange, so it starves), B 30/30 shifts. All acceptance criteria still PASS.
- Job-only recovery from an empty bank clears the arrears on held day 12 (test), so working alone works with a small margin if mental holds.
- Holdem rev.2 numbers approved; the BB +0.5bb deviation is now "confirmed" in DESIGN.md C-v2.
- Tests 247/247 (harness 245 + Unity 2), 0 compile errors, Win64 build + `-allinShots` OK (E9 with exchange money, arrears countdown, hunger, release, ship).

## Measurements after e056a0e (2026-09-30, report-only; SIM_REPORT.md "H 측정" + D line)
- E9 declaration state, V3 + bankruptcy on, 1,000 runs each: both bots declare on the first rent day (median Day 31; 1000/1000). Mental at declaration p10/p50/p90: **E 0 / 5 / 66, F 0 / 5 / 25**. Liquidation → declaration median **E 6 days (p10 1, p90 23; n=849), F 3 days (1 / 24; n=964)**. Hold-on (job-only bot, 14 days, positions/exchange untouched): released **E 19.1%, F 13.5%**.
- Why mental is ~5: both bots move the bank to the exchange early, so from Day 2 the evening meal can't be paid — hunger (−5 mental a day) has usually driven mental to ~0 before Day 31, not the liquidations themselves. That is also why the F-bot hold-on number matches sim G's mental-20 row more than its mental-40 row.
- D drop explained: the D bot starves **30.0 times per run** (every evening; its bank is empty from Day 1). Median final assets: hunger on 5,980,032 / **hunger off 7,746,430** (the pre-change figure). `Loop.StarvationEnabled` (sim-only, default on, unsaved) exists only for this control.

## LifeSense + hunger notices (2026-09-30, after 742fe1d; no rule change)
- Sim `LifeSense` (Sim/LifeSense.cs, off by default; C/D/A acceptance numbers unchanged with it off — A 20,738,000, C 84.9%, D 5,980,032): bank buffer (rent + 30 days of food = 810,000), pre-evening withdrawal when the bank can't cover the meal, immediate withdrawal at E9 when the wallet's free cash covers the arrears (open positions untouched). Applied to the E and F bots.
- Measurement with LifeSense on (1,000 runs): declared **E 993/1000, F 997/1000**; mental at declaration p10/p50/p90 **E 36 / 100 / 100, F 62 / 100 / 100**; hold-on release **E 97.6%, F 100%**; average starved evenings **E 9.5, F 7.0** (off: 24.6 / 21.5). Declaration day median E Day 91, F Day 121 (off: Day 31). Liquidation → declaration median 35 / 37 days.
- Reading: with a buffer the bots reach a much later, healthy-mental declaration, and the E9 wallet withdrawal (not the job) is probably what releases most of them — I did not separate the two. The remaining starvation is from transfer delays / the domestic 24h lock.
- Game: the hunger log line now appends "거래소엔 N원 있음." when domestic + overseas + in transit ≥ one meal (no auto-withdrawal); the third hungry day in a row raises one popup (`EventIds.Hunger`, confirm only) instead of the log line; later days of the streak add nothing. `SaveData.starveStreak/lastStarveDay`. 3 tests; harness 248, Unity 250, 0 errors, build + capture OK (`hunger_popup` screenshot).

## Log
- 2026-09-30 v0.4 stage ① 원양어선·연속 인생: `md/VISION.md` (user quote verbatim + direction + roadmap), DESIGN.md Part D. Core `LoopLife.cs`: age = 21 + (day−1)/365; [계속 살기] (`BeginContinueLife`/`ContinueLife`), settlement every 30 days; dawn bankruptcy judgment (assets < 534,000 → E9); forced ship on the 7th day after the state began; `Depart(closePosition)` simulates all 900 slots silently; return = bank +15M, mental 50, body max −15 (floor 40), health debt +1, goshiwon; `CheckupAction` (day slot, 50,000, every 10th cancels 1 debt only while debt>0); illness roll every 30 days when debt>0 (no RNG draw at debt 0 → A~E streams untouched), hospital 1M~5M from the bank as available + 3 lost days. `Loop.BankruptcyEnabled` (headless switch, default on): sim A~E turn it off since a forced ship would stretch a 30-day bot to ~200 days; A is also tested with rules on. Sim A~E report numbers unchanged; new report-only sim F. 33 new tests. Game: age + health debt in the status row, body gauge shows the shrunken maximum, 6th button [검진·운동], E9 popup (choice / forced), position confirm before sailing, boarding + skip text, return scene, SICK popup, monthly settlement with [계속 살기] and new rows. **My interpretations where the spec was silent:** rent recurs every 30 days from Day 31 (needed for continuing life; month one unchanged); the forced ship still asks about the open position; "7 days held" = forced on the 7th day after the first declaration; check-up has no recovery effect; at sea lotto still draws (money to bank) with no popups, and E1/E2/E3/E8 are suppressed in favor of one return report; return charges no rent (next in 30 days) and the room stays goshiwon for 30 days; the ship also skips the settlements it sails past.
- 2026-09-29 코인 선물 개편 v0.3 "청산 체인": DESIGN.md B5 rewrite (돈의 세 칸, 서브틱 경로, 관전/분노/24시간 룰/본절) + intent quote. Core: `bankCash`/`domesticCash`/`overseasCash` + `PendingTransfer`/lock (`LoopWallet.cs`), 3rd RNG stream (`_subTick`) + `StudentTSubTickPath`/`LinearSubTickPath`, `SubTickWalk` (`SlotAdvance.cs`) — `Loop.BeginWatchableAction`/`FinishWatch` let the watch scene pause mid-slot and call `AddMargin`/`StopLoss`/`Pray` for real, while `Do()` stays byte-identical for every headless caller (sims/tests always auto-run to completion — "기도 0회" baseline verified by test). Tilt (`Data.tilted`, clears on next sleep), ghost tracking (50-slot window, E8, enhanced variant), "잠 못 듦" half-sleep. 23 new Core tests (161 total, was 141) + sim C/D rewritten to fund via the wallet chain + new report-only sim E ("청산 체인"). Game: `ExchangeScreen` (3 tabs), `WatchScreen` (live sub-tick animation + 기도/증거금추가/손절), `CoinScreen` tilt tint + recovery calculator + margin-add, E8 popup + settlement rows (기도/분노 베팅/놓친 돈), `DevCapture` scripts all four new scenes. Found via capture: `WatchScreen`'s margin/stop-loss buttons and `ExchangeScreen`'s 25%/50%/전액 buttons both had inverted `Place()` point-anchor offsets (negative/oversized rects — the exchange one was 190px tall and silently covered the lock-countdown text sitting above it) — both fixed with `Box()`; tilt recovery calculator showed "Infinity%" at 0 해외 잔고 — now shows "—"; the domestic-lock screenshot was capturing the wrong tab — added `ExchangeScreen.SelectTab`; `SaveIO.Save()`'s `File.Replace` threw a transient `IOException` twice independently (not caused by tooling — Windows AV/indexer lock contention) and silently killed the coroutine — added a 3-retry with backoff. Known rough edge left as-is: tilted + broke + mental<30 crowds the "입금하러 가기" button against the leverage row. Unity: 163/163 EditMode tests, 0 compile errors, Win64 build clean, `-allinShots` full 30-day run with 0 exceptions after fixes.
- 2026-09-25 C7-reply: A all-in reveal (engine `Reveal` events + 3 tests), C tempo (instant replay of hands the hero folded, hand log, 빠르게 toggle), B `SkillMeasure` in `sim holdem`, E settings. Harness 141 tests.
- C6–C7: Holdem.unity code-built table, action bar (fold / check-call / presets ½ ⅔ pot all-in + slider), HUD 0–4, effects, boot menu, result screen; `-allinHoldemShots` dev capture. Found via capture: result overlay stayed up when a new tournament started without the menu (fixed).
- C1–C5 (hold'em): bitmask 7-card evaluator (cross-checked vs brute force on 20k hands), HandState betting engine, Tournament step machine (re-entry, Main field/refills, bubble, bad beats), Brain (169 ranks generated by `gen169`, push/fold table, MC postflop, 4 personalities), `sim holdem` → md/SIM_HOLDEM.md. 138 harness tests.
- 2026-09-25 revision: coin open/close now consume a slot (night = all-nighter), progressive lotto tax, sim D rewritten. 71 harness tests. Sim: A exact, C 81.8%, D median 7,184,768 (was 10.1M; the all-nighter mental cost now bites).
- Stages 6–7 (one commit — the screens and the event presenter are wired to each other, so neither compiles alone): code-built HUD (day/slot, cash/assets, body/mental, room, position, log, action bar), coin screen (price, 60-tick chart, long/short, x5–x100, commit slider honoring the 25% floor under mental 30, fee/liq preview, close), lotto screen; events OPENING/FIRST/E1–E7/FIRED/CANT_GET_UP; autosave after every action (temp file → replace); "새 인생" with confirm. Galmuri11 (OFL) → dynamic TMP fonts at boot; TMP Essential Resources extracted into Assets; empty Loop/Holdem scenes + build settings created by `ProjectSetup`. Found and fixed via Unity tests: JsonUtility doesn't round-trip doubles → price is whole won, quantity derived, event payloads long. Found in the build: a new TMP font asset has a null fallback list (NRE at boot).
- Stage 5: `sim loop` (A/B/C/D × 1000) → md/SIM_REPORT.md; all acceptance criteria pass. Exit code 1 on any FAIL.
- Stage 4: lotto (real draws, tax brackets), restaurant job (mistakes/firing), gauge thresholds. 68 tests.
- Stage 3: coin futures — fees, liquidation (E1/E2/E3), close mental, hope circuit E4.
- Stage 2: Loop engine (slots, calendar, rent/food, rest/sleep, lost slots, event queue, SaveData, two RNG streams).
- Stage 1: git init (main baseline commit, `feat/loop-proto`), .gitignore, 5 asmdefs, `tools/dotnet-harness` (net8.0, NUnitLite), `Xoshiro128` + Box–Muller + Student-t(3) with tests, CLAUDE.md / STATUS.md / DESIGN.md.
