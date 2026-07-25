# Micro-Task: APM Gate — Phase A pre-ship review

**Suite:** [`apm-README.md`](apm-README.md) **#Gate**  
**Parent:** [`../analysis-pipeline-merge.md`](../analysis-pipeline-merge.md)  
**Difficulty:** Process  
**Estimated Context:** ~8k tokens  
**Status:** `[ ]` Not Started  
**Prereq:** A1–A5 green  

## Objective

Run the uncommitted-change review gate and only mark Phase A complete when structure/contract issues are clean.

## Required Reading

- [`../v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md`](../v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md)  
- Parent §9 risks  
- `AGENTS.md` pre-ship gate  

## Exact Steps

1. `git diff --stat HEAD` and full diff — scope should be bridge + pipeline + DslCompiler + tests.  
2. Categorize 🔴 Structure / 🟠 Contract / 🟡 Edge / ⚪ Hygiene.  
3. Confirm: no Storage in domain pipeline; fail-closed still loud; no new diagnostic codes unless accidental.  
4. Confirm metadata bridge still used (no `new AggregateAnalyzer(domain)` without context when structure metadata required).  
5. Run full suite or large relevant subset:
   ```bash
   dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj
   dotnet run --project Poly.Tests/Poly.Tests.csproj
   ```
6. Update parent plan agent pick + this suite statuses; optional inventory §5 note that three passes run on domain pipeline.  
7. Do **not** start Phase B in the same commit unless trivial.

## Verification

- [ ] 🔴🟠 zero  
- [ ] Build + suite green  
- [ ] Plans/apm statuses honest (A1–A5 `[x]`)  

## Output

- Review notes / plan status sync  
- Ready-to-commit tree  

## Out of Scope

- Phase B  
- Force-push / push  

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:**
