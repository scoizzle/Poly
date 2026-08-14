# e2e-0-4 — Delete leftover `delete` effect grammar

**Difficulty:** S  
**Status:** `[ ]`  
**Fleet:** P7-1  

## Objective

Authoring `delete …` does not throw an unhandled-pattern internal error. Soft-delete Effect IR is gone (2026-08-10); grammar must not pretend otherwise.

## Exact steps

1. Confirm `Poly/DomainModeling/Parsing/DslGrammar.cs` still has `.Pattern("delete")` and `DslTokenKind.Delete`.
2. Confirm `PolyDslParser.ParseEffect` has no delete arm.
3. **Preferred:** remove the delete pattern + unused token if nothing else uses `Delete`.  
   **Alt:** keep the token and fail parse with a dedicated diagnostic (“delete effect is not supported”).
4. Failing test first: a `.poly` / parse fragment containing a `delete` effect → parse error (not `Unhandled`, not success).
5. Test name: `ParseEffect_DeleteKeyword_FailsClosed`.

## Verification

```bash
dotnet run --project Poly.Tests/Poly.Tests.csproj --filter ParseEffect_DeleteKeyword_FailsClosed
```

- [ ] No internal “Unhandled effect pattern” for `delete`  
- [ ] Full suite green  

## File ownership

| Edit | Do not edit |
|------|-------------|
| `Poly/DomainModeling/Parsing/DslGrammar.cs` | `DomainDslPrinter.cs` |
| `Poly/DomainModeling/Parsing/PolyDslParser.cs` (ParseEffect only if alt) | runtime / exporter |
| `Poly.Tests/DomainModeling/Parsing/**` | |

## Status

**Status:** Done  
**Claimed by:** opencode (fleet agent, e2e-0) — 2026-08-13  
**Verified:** delete pattern + Delete token removed; failing test first; 2064/2064 green; build 0/0
