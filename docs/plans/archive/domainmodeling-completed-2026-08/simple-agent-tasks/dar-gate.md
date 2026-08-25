# Micro-Task: DAR.GATE - Pre-Ship Review for DomainAuthoringContext Removal

Parent: ../domain-authoring-context-removal-plan.md
Queue: ./dar-README.md
Difficulty: Small (process)
Status: [x] Completed 2026-07-28
Prereq: DAR.A1 through DAR.G1 complete

## Objective

Verify the full removal suite is honest, fail-closed, and ship-ready under the
uncommitted-change review gate.

## Checklist

1. Run dirty-tree review:
   - `git diff --stat HEAD`
   - `git diff HEAD`
2. Categorize findings by severity:
   - Structure (red)
   - Contract (orange)
   - Edge case (yellow)
   - Hygiene (white)
3. For each structure/contract finding, verify three-layer defense:
   - parse-time reject
   - analyze-time catch
   - runtime fail loud
4. Confirm fail-closed behavior:
   - missing inputs fail loud
   - invalid extension ordering fails loud
   - no vacuous success in empty-match/invalid-config flows
5. Run full verification:

```bash
dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj
dotnet run --project Poly.Tests/Poly.Tests.csproj
rg -n "DomainAuthoringContext" Poly Poly.Mcp src/Poly.DslCompiler src/Poly.Packs.*
```

## Definition of Done

- [x] All DAR phase tasks marked complete with notes.
- [x] Build + full suite green.
- [x] Remaining findings are either fixed or explicitly documented as accepted.
- [x] No hidden analyzer system branch or mutable context dependency remains.

## Review Notes

- Ran `git diff --stat HEAD` and `git diff HEAD` for the uncommitted-change gate.
- Structure/contract review: no remaining context-based analyzer branch; explicit inputs enforced.
- Verification completed:
   - `dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj`
   - `dotnet run --project Poly.Tests/Poly.Tests.csproj`
   - `rg -n "DomainAuthoringContext" Poly Poly.Mcp src/Poly.DslCompiler src/Poly.Packs.*`
