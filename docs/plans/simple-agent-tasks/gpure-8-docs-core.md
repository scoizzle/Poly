# gpure-8 — CORE + docs honesty

**Difficulty:** S  
**Status:** `[x]`  
**Prereq:** task 7  

## Objective

Document pure Grammar product path. No behavior change.

## Exact steps

1. Update `docs/CORE.md` placement: Grammar owns engine; DomainModeling owns **product grammar tables + handlers** (not dual RD language).  
   **F9 — printer honesty:** say explicitly that **printer table-parity is deferred** (round-trip still uses the domain-walk printer). “Pure Grammar product path” means **parse** control flow is table-driven; do not claim print is table-driven yet.  
2. Update `Poly/DomainModeling/README.md` Parsing row: pure Grammar-driven parse; printer deferred as above.  
3. Update `Poly/Grammar/README.md` if API surface changed (RuleRef / Rule, LeftAssoc).  
4. Parent [`../grammar-pure-end-state.md`](../grammar-pure-end-state.md) §8 checkboxes → tick completed items.  
5. `READY-TO-TASK.md`: mark gpure status if complete after gate.  
6. Guide (`poly-dsl-guide.md`): **required if** error messages or syntax claims changed (F8 `ExpectedTokens` drift from gpure-3) — same-change rule; do not leave guide claiming old expected-token text.

## Verification

- [ ] Docs match code  
- [ ] Suite still green  

```bash
dotnet run --project Poly.Tests/Poly.Tests.csproj --no-build
```

## File ownership

| Edit | Do not edit |
|------|-------------|
| CORE, DomainModeling README, Grammar README, parent plan checkboxes | Reintroduce RD |

## Status

**Status:** Done 2026-08-07 — CORE.md DSL row (tables+handlers, printer deferred), DomainModeling README Parsing row, Grammar README (RuleRef/LeftAssoc done in 1–2), parent §8 ticked, READY-TO-TASK marked DONE. Guide: no error-text claims changed (effect missing-name errors now surface as "Expected effect …" — no guide text asserted the old strings).  
