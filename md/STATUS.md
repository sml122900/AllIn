# STATUS

_Single session-to-session context doc. Newest state at top._

## Current
- Branch: `feat/loop-proto`. Not pushed (no remote configured).
- Stage: **Part B Stages 1–7 done** (slot model + lotto tax revised 2026-09-25). Part B Stage 8 (human play) pending — observations come from the user separately.
- Unity compile: **verified** 2026-09-25 via unity-mcp (editor was open): 0 errors, **140/140 EditMode tests** (71 Core + 67 Holdem + 2 Game). Win64 build succeeds; loop and hold'em dev captures run with 0 exceptions.

## Next
- **Part B Stage 8 (user):** open `Assets/_Project/Scenes/Loop.unity`, press Play (or run `Builds/Win64/AllIn.exe`), play one 30-day run. Record observations below — no number changes (tuning is the user's call). Target 10–15 min.
- Part C (hold'em): **C1–C7 done; stopped at C7 human play.** To play: open `Assets/_Project/Scenes/Holdem.unity` → Play (or run the build and load the Holdem scene — the build starts in Loop; see note), pick Kind / study level / seed → 시작. Target Pub 8–12 min. Observations go below; numbers stay as they are.
- Remote: none configured and `gh` is not installed → nothing pushed. Needs a repository URL from the user.

## Decisions (confirmed by the user 2026-09-25)
- **1 action = 1 slot.** Job, rest/sleep, coin open, coin close each consume the slot. Free: looking at the coin screen, lotto purchase (not at night), popups. Opening/closing at night = all-nighter (no recovery, mental −5). The "can't open and close in the same slot" rule is a consequence, not a separate rule. E4 still fires on entering the coin screen (viewing is free). Sim D opens at night and closes in the first slot it can act in.
- **Lotto tax is progressive:** 22% up to 300M, 33% only on the part above. 1st prize net 1,373,000,000.
- **Fired day** still pays that day's wage (kept). **mental < 15 lost slots** count as rest, body +5 (kept).
- **Pixel Perfect:** URP built-in component (kept; the handoff's package name was outdated).
- **Margin math** (unchanged): committed C, notional N = C×lev, entry fee out of C → margin M = C − fee; liquidation at loss ≥ 0.9M.
- **E3 timing:** start of the slot after the one where E1/E2 appeared.
- **Hold'em numbers not in the handoff** (DESIGN.md C3/C4, still provisional): Pro p_bluff 0.10, pub points 2/1, off-table elimination 0.08 × (remaining − 9) per hand, Main refill Pro 60%.
- **Determinism:** PRNG bit-exact everywhere; saved numbers are all integral. Price uses Math.Exp etc., so harness vs editor could differ in the last bit of a tick before rounding to whole won — resume from a save is exact.

## Hold'em sim observations (C5, 1,000 × 3 kinds — observations only)
- Pub: 32.8 hands avg (p10 27 / p90 39), median effective stack ≤ 10bb by hand 13. Re-entries 2.3 per tournament (all seats).
- Skill signal is **thin in the Pub**: Pro 11.9% vs Station 11.5% (≈ 1 SE apart; passes the letter of the criterion only). Maniac wins most (12.3%) — the hyper-turbo rewards aggression. Satellite: Pro 12.9% vs Station 10.6%. Main (reference only): Pro 3.7% vs Station 3.9% — Pro does not beat Station there.
- Main: final table at hand 60.5 after recalibrating my own provisional off-table rate 0.08 → 0.05 (0.08 gave 47; the handoff asks for ~60). Main runs 123 hands on average (p90 182) — long for a human sitting.

## Hold'em UI notes (C6–C7)
- Holdem.unity is empty; `HoldemApp` builds a boot menu (Kind · study level 0–4 · seed ±/random) and a 9-seat table in code, then replays the engine's event stream with delays (actions 0.5 s, flop/turn 0.55 s, river 0.9 s, all-in river 1.5 s + board shake, bad beat 0.5 s blackout + line, bubble / level / table-merge banners, re-entry popup on bust).
- HUD: 0 = own chips, others as chip piles (≥1.3× avg 많음 / ≥0.7× 보통 / else 적음), plain dealer button; 1 = all stacks with bb; 2 = position labels + highlighted button; 3 = pot odds on the call button; 4 = push/fold panel (range for this bb/position, or the call range facing a shove, and whether my hand is in).
- All-in hands are revealed at showdown in show order (DESIGN C1), not at the moment of the all-in — decide in play whether earlier reveal feels better.
- Build note: the build's first scene is Loop; the Holdem scene is reachable in the build only via the dev flag. For human play use the editor.

## Open tuning (do NOT change until the user's Stage 8 play notes arrive)
- Post-liquidation mental spiral: −40 on liquidation can hit 0 within ~3 days with no recovery path (can't get up → job mistakes → fired).
- Lotto miss −1 per purchase: 2 purchases/day alone erodes mental enough to cost the job (sim B works 23/30 shifts).

## Things to look at during human play
- Mental falls fast: the dev-capture bot (50x every other night) hit mental 0 by day 3 → "can't get up" and 30% job mistakes most of the run. Real players may spiral the same way — intended or too harsh?
- E3 blocks input with a near-invisible scrim and darkens only the room area; the HUD stays readable. Check it reads as "forced".
- Log panel (right) overlaps the placeholder room art. Fine for placeholders.
- E5 shows at most 8 games; the rest are summarized ("외 N게임").

## Sim observations (Stage 5, seeds 1..1000 — observations only, no numbers changed)
- A exact 20,738,000 every run. C liquidated 81.8% (margin over the 80% bar is thin — any volatility tweak could flip it). D median 10.1M (PASS).
- B (lotto 2/day + job) works only 23/30 shifts on average: weekly draw misses (−1 per losing purchase, ~14/week) plus job −4/sleep +3 push mental under 30 → 30% mistakes → fired. Mental erosion from lotto alone is enough to cost the job. Worth a look in human play.
- C when not liquidated has p90 ≈ 110M (25x long + slight upward drift of exp(t) prices).

## Log
- C6–C7: Holdem.unity code-built table, action bar (fold / check-call / presets ½ ⅔ pot all-in + slider), HUD 0–4, effects, boot menu, result screen; `-allinHoldemShots` dev capture. Found via capture: result overlay stayed up when a new tournament started without the menu (fixed).
- C1–C5 (hold'em): bitmask 7-card evaluator (cross-checked vs brute force on 20k hands), HandState betting engine, Tournament step machine (re-entry, Main field/refills, bubble, bad beats), Brain (169 ranks generated by `gen169`, push/fold table, MC postflop, 4 personalities), `sim holdem` → md/SIM_HOLDEM.md. 138 harness tests.
- 2026-09-25 revision: coin open/close now consume a slot (night = all-nighter), progressive lotto tax, sim D rewritten. 71 harness tests. Sim: A exact, C 81.8%, D median 7,184,768 (was 10.1M; the all-nighter mental cost now bites).
- Stages 6–7 (one commit — the screens and the event presenter are wired to each other, so neither compiles alone): code-built HUD (day/slot, cash/assets, body/mental, room, position, log, action bar), coin screen (price, 60-tick chart, long/short, x5–x100, commit slider honoring the 25% floor under mental 30, fee/liq preview, close), lotto screen; events OPENING/FIRST/E1–E7/FIRED/CANT_GET_UP; autosave after every action (temp file → replace); "새 인생" with confirm. Galmuri11 (OFL) → dynamic TMP fonts at boot; TMP Essential Resources extracted into Assets; empty Loop/Holdem scenes + build settings created by `ProjectSetup`. Found and fixed via Unity tests: JsonUtility doesn't round-trip doubles → price is whole won, quantity derived, event payloads long. Found in the build: a new TMP font asset has a null fallback list (NRE at boot).
- Stage 5: `sim loop` (A/B/C/D × 1000) → md/SIM_REPORT.md; all acceptance criteria pass. Exit code 1 on any FAIL.
- Stage 4: lotto (real draws, tax brackets), restaurant job (mistakes/firing), gauge thresholds. 68 tests.
- Stage 3: coin futures — fees, liquidation (E1/E2/E3), close mental, hope circuit E4.
- Stage 2: Loop engine (slots, calendar, rent/food, rest/sleep, lost slots, event queue, SaveData, two RNG streams).
- Stage 1: git init (main baseline commit, `feat/loop-proto`), .gitignore, 5 asmdefs, `tools/dotnet-harness` (net8.0, NUnitLite), `Xoshiro128` + Box–Muller + Student-t(3) with tests, CLAUDE.md / STATUS.md / DESIGN.md.
