# Micro-Task: Inventory V2 tests and demos for aggressive port

**Parent**: WP7  
**Difficulty**: Medium (read-only + markdown deliverable)  
**Estimated Tokens**: ~8k  
**Status**: [ ] Not Started  
**Depends on**: WP6 freeze preferred but not required

## Objective

Produce a **prioritized inventory** of what still depends on `Poly.Data.Modeling` so port/delete can be chunked.

## Exact Steps

1. List test projects/files under `Poly.Tests` that reference `Poly.Data.Modeling` (path + rough test count if easy).
2. List demos/benchmarks under `Poly.Benchmarks` (and any other host apps) that use V2.
3. Classify each entry:
   - **Port to V3** (still teaches product value)
   - **Delete** (redundant with V3 suites already green: DomainFactory, authoring, rollback, V3McpSmoke)
   - **Blocked** (needs Actor / contract gen / feature not on V3 yet) — list the gap
4. Write deliverable: `docs/plans/v2-to-v3/spikes/v2-port-inventory.md` with tables + recommended first batch (≤5 files or ≤1 demo).
5. Propose order for micro-tasks: first port batch, second port batch, demo rewrite, delete leftovers.

## Verification

- [ ] Inventory file exists and is actionable
- [ ] First batch named explicitly for `wp7-port-v2-tests-batch1.md`
- [ ] No code ports in this task

## Out of Scope

- Actually porting or deleting code
