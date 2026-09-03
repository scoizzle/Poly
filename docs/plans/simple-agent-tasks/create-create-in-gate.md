# create-create-in-gate — Simulate is the lowered program

**Difficulty:** S  
**Status:** `[ ]`  
**Prereq:** tasks 1–5 `[x]`

## Objective

Prove CURRENT create/create-in is done: simulate = Interpreter + bound Store + dictionary-backed `This`. Mark PIPELINE-STATUS DONE in this change.

## Exact steps

1. Run the [uncommitted-change review gate](../v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md). All 🔴🟠 resolved.
2. Grep fail-closed:
   - `ExecuteStructured` — gone
   - `CreateByType` / `CreateInNav` / `ProbeCreateByType` — not shipped meaning
   - `PreprocessQuantifiers` / `PreprocessEffectExpressions` — not on invoke/simulate path
3. Docs match code in the same change: `docs/CORE.md` §3.6 residual sentence, `docs/interpretation/domain-execution-model.md`, ADR consequences, parent **Done** checklist.
4. Full suite:

```bash
dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj
dotnet run --project Poly.Tests/Poly.Tests.csproj -p:NuGetAudit=false
```

5. Update Agent pick in **one** change: `PIPELINE-STATUS.md`, `READY-TO-TASK.md`, `docs/plans/README.md`, `docs/plans/v2-to-v3/master-roadmap.md`. `CURRENT` becomes the next admitted line from PIPELINE-STATUS (`THEN`), or `(none)` if nothing is admitted. Do not invent a new CURRENT here.

## Verification

- [ ] Parent Done checklist complete
- [ ] pr1 clean
- [ ] Suite green
- [ ] Agent pick updated in the same change

## File ownership

| Edit | Do not edit |
|------|-------------|
| PIPELINE-STATUS + mirrors | New suite admission |
| CORE / execution-model residuals | Unrelated parked plans |

## Status

**Status:** Not Started
