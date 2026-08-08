# p1-1 — `Now` / `today` expression nodes + form

**Difficulty:** M  
**Status:** `[ ]`  
**Prereq:** task 0  

## Objective

Product can parse identifier `Now` (and `today` if lock requires) as a **DomainExpression** node when temporal forms are registered — not as a bare property when pack on.

## Exact steps

1. If missing, add `DomainExpression` node(s) for clock now/today per design lock (names: prefer `Now` / `Today` records or single `ClockNow` with kind — **name for what it is**).  
2. Wire dispatch: `DomainExpressionDispatch`, rewrite base, lowering → CLR `TimeProvider` / `DateTime.UtcNow` as locked (prefer TimeProvider if already used; else UtcNow with TODO only if TimeProvider needs package — repo is net10.0, `TimeProvider` is in BCL).  
3. Implement `IExpressionPrimaryForm` that:
   - On identifier `Now` (exact product spelling — document case: prefer exact `Now`) consumes token and returns Now node.  
   - Leaves cursor unchanged on non-match.  
4. Unit test: with form registered, fragment `Now` parses to Now node; without form, `Now` remains PropertyAccess **or** fails if you choose reserved — **prefer**: without pack = PropertyAccess for back-compat unless design lock says reject; design lock says pack-absent **rejects temporal authoring** — so without pack, `Now` should **not** silently mean property if we reserve the name.  
   - **Locked for this suite:** without temporal forms registered, parsing `Now` as expression in policy fails closed **or** PropertyAccess — pick **fail closed at analysis** if parse allows PropertyAccess. Simpler parse: form only when pack registered; without pack PropertyAccess; analysis later flags if needed.  
   - **Task 3** owns pack-on/off. This task: form + IR + lowering test with form forced in test.

5. Test: `Now_Form_ParsesAndLowers` (lowering does not throw).

## Verification

```bash
dotnet build Poly.Tests/Poly.Tests.csproj
dotnet run --project Poly.Tests/Poly.Tests.csproj --no-build
```

- [ ] Now IR + form + test green  
- [ ] Full suite green  

## File ownership

| Edit | Do not edit |
|------|-------------|
| `DomainExpression*.cs`, dispatch/rewrite/lowering | MCP tools |
| `ExpressionFormRegistry` consumers in tests | schedule/P9 |
| New form type under DomainModeling or Packs | |

## Status

**Status:** Not Started  
