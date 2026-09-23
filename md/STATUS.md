# STATUS

_Single session-to-session context doc. Newest state at top._

## Current
- Branch: `feat/loop-proto`. Not pushed (no remote configured).
- Stage: **Part B Stages 1–7 done.** Stopped at Stage 8 (human play) — waiting for the user.
- Unity compile: **verified** via batchmode (0 errors), 70/70 EditMode tests pass in Unity (68 Core + 2 Game save round-trip). Windows build succeeds; scripted 30-day dev capture runs with 0 exceptions. unity-mcp was not usable (no editor open), batchmode used instead.

## Next
- **Part B Stage 8 (user):** open `Assets/_Project/Scenes/Loop.unity`, press Play (or run `Builds/Win64/AllIn.exe`), play one 30-day run. Record observations below — no number changes (tuning is the user's call). Target 10–15 min.
- Part C (hold'em) Stages 1–7: not started. Can begin any time (independent of the loop).

## Decisions & interpretations (review these)
- **Slot vs instant actions.** Handoff says "1 action = 1 slot" but also "can't open and close in the same slot", "liquidation fires while sleeping", and sim D "enter at night → close at dawn". These only work if coin/lotto don't end the slot. So: *slot actions* (job, rest/sleep) end the slot; *instant actions* (coin open/close, lotto buy) don't. Night instant actions don't break sleep; all-nighter (−5 mental) only applies to night *slot* actions (none exist yet, e.g. future night loading job). DESIGN.md B1.
- **Margin math.** Committed C, notional N = C×lev, entry fee N×0.05% comes out of C → margin M = C − fee. Liquidation when unrealized loss ≥ 0.9M. DESIGN.md B5.
- **Lotto tax.** Flat bracket rate on the whole prize (matches the handoff's 1st-prize net 1,340,000,000), not marginal.
- **Fired day.** Mistake still pays that day's wage, then 3-day ban + mental −10.
- **mental < 15 lost slots** are treated as rest (body +5).
- **E3 timing.** Shown at the start of the slot *after* the one where E1/E2 appeared.
- **Pixel Perfect.** Unity 6 URP ships its own `UnityEngine.Rendering.Universal.PixelPerfectCamera`; the standalone `com.unity.2d.pixel-perfect` package is the legacy built-in-pipeline version and is not added. Using the URP component.
- **Hold'em unstated numbers** (marked in DESIGN.md C3/C4): Pro p_bluff 0.10, pub points 2/1, off-table elimination 0.08 × (remaining − 9) per hand, Main refill weights Pro 60%.
- **Determinism caveat.** PRNG is bit-exact everywhere. Price uses `Math.Exp/Log/Sqrt/Cos`; Mono vs .NET could differ in the last bit, so harness vs editor runs of the same seed may drift slightly. Saves store the price itself, so resuming is exact.

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
- Stages 6–7 (one commit — the screens and the event presenter are wired to each other, so neither compiles alone): code-built HUD (day/slot, cash/assets, body/mental, room, position, log, action bar), coin screen (price, 60-tick chart, long/short, x5–x100, commit slider honoring the 25% floor under mental 30, fee/liq preview, close), lotto screen; events OPENING/FIRST/E1–E7/FIRED/CANT_GET_UP; autosave after every action (temp file → replace); "새 인생" with confirm. Galmuri11 (OFL) → dynamic TMP fonts at boot; TMP Essential Resources extracted into Assets; empty Loop/Holdem scenes + build settings created by `ProjectSetup`. Found and fixed via Unity tests: JsonUtility doesn't round-trip doubles → price is whole won, quantity derived, event payloads long. Found in the build: a new TMP font asset has a null fallback list (NRE at boot).
- Stage 5: `sim loop` (A/B/C/D × 1000) → md/SIM_REPORT.md; all acceptance criteria pass. Exit code 1 on any FAIL.
- Stage 4: lotto (real draws, tax brackets), restaurant job (mistakes/firing), gauge thresholds. 68 tests.
- Stage 3: coin futures — fees, liquidation (E1/E2/E3), close mental, hope circuit E4.
- Stage 2: Loop engine (slots, calendar, rent/food, rest/sleep, lost slots, event queue, SaveData, two RNG streams).
- Stage 1: git init (main baseline commit, `feat/loop-proto`), .gitignore, 5 asmdefs, `tools/dotnet-harness` (net8.0, NUnitLite), `Xoshiro128` + Box–Muller + Student-t(3) with tests, CLAUDE.md / STATUS.md / DESIGN.md.
