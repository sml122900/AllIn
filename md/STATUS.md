# STATUS

_Single session-to-session context doc. Newest state at top._

## Current
- Branch: `feat/loop-proto`. Not pushed (no remote configured).
- Stage: **Part B Stage 1 done** (repo, asmdefs, harness, PRNG, docs).
- Unity compile: unverified (unity-mcp not connected yet this session).

## Next
- Part B Stage 2: Core time / state / IAction / SaveData.
- Part C can start in parallel (Stage 1 prerequisites are in place).

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

## Log
- Stage 1: git init (main baseline commit, `feat/loop-proto`), .gitignore, 5 asmdefs, `tools/dotnet-harness` (net8.0, NUnitLite), `Xoshiro128` + Box–Muller + Student-t(3) with tests, CLAUDE.md / STATUS.md / DESIGN.md.
