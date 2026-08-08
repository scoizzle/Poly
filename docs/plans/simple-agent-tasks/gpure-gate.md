# gpure — Suite gate

**Difficulty:** S  
**Status:** `[x]`  
**Prereq:** tasks 0–8 `[x]`  

## Objective

Prove pure Grammar product path. No new features.

## Exact steps

1. **Grep gates**

```bash
# No classic RD arithmetic loops in product parsing
rg -n "while \(_c\.Current\.Kind == TokenKind\.(Plus|Minus|Star|Slash)" Poly/DomainModeling/Parsing --glob '*.cs'

# Effect entry uses MatchRule
rg -n 'MatchRule\("effect"\)|TryMatch\("effect"\)' Poly/DomainModeling/Parsing --glob '*.cs'

# Engine features exist (F1: name RuleRef, not class Rule)
rg -n "RuleRef|LeftAssoc" Poly/Grammar --glob '*.cs'
# Expect: both match

# Parity / regression corpus still present
rg -n "DslExprParityTests|class DslExprParity" Poly.Tests --glob '*.cs'
```

2. **Build + full suite**

```bash
dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj
dotnet run --project Poly.Tests/Poly.Tests.csproj
```

3. Spot-check inventory notes still record F4 (longest vs first) + B1 (effect head/body).

4. **pr1** pre-ship review on dirty tree; fix 🔴🟠.

5. Mark `gpure-README.md` **DONE** + date.  
6. Tick parent plan §8 success definition fully.  

## Verification

- [ ] Greps pass (`RuleRef` + `LeftAssoc` present; no vacuous `class Rule`)  
- [ ] Parity suite exists  
- [ ] Full suite green  
- [ ] pr1 clean  
- [ ] Suite Done  

## Post-gate follow-up — P1 (2026-08-08): span-table vs live-fold parity class

**Rule (applies to any suite where a grammar table models a language the live path also implements):** every **divergence class** between the span table and the live fold must be pinned on **BOTH** sides with tests — span rejects/accepts **and** fold IR oracles — never only documented in inventory notes.

**First instance (gpure, S1):** `DslExprParityTests.SpanVsFold_NotInChain_TableRejectsFoldAccepts` pins `a + not b` / `a + not b > c` — span rejects, fold accepts with frozen-IR oracles.

**Check before claiming a divergence is "documented":** `rg -n "SpanVsFold" Poly.Tests` must hit a test that pins both sides.

## Status

**Status:** Done 2026-08-07 — all greps pass (no RD arithmetic loops exit 1; effect entry `MatchRule("effect")`; `RuleRef`/`LeftAssoc` present; parity suite present). Build + full suite green (1928). pr1: no 🔴/🟠 — rejections preserved three-layer; documented drifts: effect missing-name error text (now "Expected effect …"), span-vs-fold `not`-in-chain (inventory §A1 note). Parent §8 ticked; README marked DONE.  
