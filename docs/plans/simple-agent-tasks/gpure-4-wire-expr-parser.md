# gpure-4 — Wire product expression parse to Grammar

**Difficulty:** M–L  
**Status:** `[x]`  
**Prereq:** task 3  

## Objective

Product expression parse builds `DomainExpression` **from Grammar matches** (table-guided), not private kind-while loops for `+/*`.  

**F2 lock:** This task **switches the live path** to Grammar and may leave old RD methods as `Obsolete` / private dual for parity **only**. **Deleting** residual RD bodies is **gpure-7**, not this task — do not claim “RD deleted” here.

## Required reading

1. `DslExpressionParser.cs`  
2. `DslGrammar` expr rules (task 3)  
3. `MatchRule` / dual-cursor in `PolyDslParser`  
4. `DslExprParityTests` (task 3)  

## Nested-span note (feeds gpure-5) — **Option A required**

Product `MatchRule` peeks without Consume. **Must use Option A:** layer-by-layer `MatchRule` + `Consume` so nested `Rule("expr")` leaves the cursor positioned for IR fold.

**Option B forbidden for nested groups:** single outer match + rebuild IR only from flat tokens loses structure (same class as B1; F5). No span-tree API in this suite.

**F10 — perf is OK:** layered Option A re-scans from head per `MatchRule` (Unread+TryMatch+Read) → O(n²) worst case on long chains. Fine for DSL authoring size. **Do not** add a match-cache / cursor optimization in this suite unless a measured product test forces it.

## Exact steps

1. Implement Grammar-guided parse (**Option A only**):
   - Methods call `MatchRule`/`Consume` for each layer (`expr-or`, `LeftAssoc` operands, …).  
   - No `while (_c.Current.Kind == Plus)` — operator loops live in engine LeftAssoc or repeated MatchRule on op patterns.

2. **Keep** old RD implementation available **only** for parity dual-run until gpure-7:
   - e.g. `ParseExpressionRdForParity()` internal, or dual-run flag.  
   - Live product path = Grammar path.

3. Expand **`DslExprParityTests`** to **IR equality** (not only accept/reject):

   - For each corpus case that accepts: `DomainExpression` from RD-for-parity equals Grammar path (structural equality of records / ToString if records lack equality — prefer record equality).  
   - Keep B3: `not a > b` fails both paths.  
   - **Minimum corpus size after this task: ≥ 15 cases** covering: arithmetic, and/or, compare, not, paren, path-prefix, exists, quantifier smoke, multi-hop if cheap.

4. **F6 — fail-closed negatives** (add to parity or unit tests; at least 2–3):

   | Case | Expect |
   |------|--------|
   | trailing op after partial expr (e.g. `1 +`) | fail loud |
   | unclosed group `(1 + 2` | fail loud |
   | empty / missing primary where required | fail loud — no vacuous success |

5. Keep `IExpressionPrimaryForm` hook **before** primary match.

6. Grep live path: no Plus/Star while-loops on product entry (RD dual may still have them until gpure-7).

## Verification

```bash
dotnet build Poly.Tests/Poly.Tests.csproj
dotnet run --project Poly.Tests/Poly.Tests.csproj --no-build
```

- [ ] Product `ParseExpression` uses Grammar path (Option A)  
- [ ] `DslExprParityTests` ≥ 15 cases, IR equality green  
- [ ] Fail-closed negatives present (F6)  
- [ ] Full suite green  
- [ ] **RD dual still exists** for parity (gpure-7 deletes it)  

## File ownership

| Edit | Do not edit |
|------|-------------|
| `DslExpressionParser.cs`, wiring | Effect grammar (gpure-5) |
| `DslExprParityTests.cs` | gpure-7 deletion of dual |

## Status

**Status:** Done 2026-08-07 — live `DslExpressionParser` folds via `MatchRule("expr-*-op")` (Option A); old RD kept as `*Rd` dual for parity; `DslExprParityTests` upgraded to IR equality (33 cases + 5 fail-closed negatives, Id-agnostic canonical form). No span gate on the live path (gate rejected `a + not b` — see notes). Suite 1923 green.  
