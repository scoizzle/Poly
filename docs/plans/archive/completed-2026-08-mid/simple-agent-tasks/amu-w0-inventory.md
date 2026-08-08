# AMU-W0 — Publish × consume × residual scan inventory

**Wave:** 0  
**Difficulty:** M  
**Status:** `[x]`
## Objective

Produce a short live inventory of domain analysis metadata: who publishes, who consumes, and where residual `domain.Relationships` / `Types.OfType` / property scans remain. This is the suite map — no production behavior change required.

## Required reading

- [`../domainmodeling-cohesion-and-metadata-findings.md`](../domainmodeling-cohesion-and-metadata-findings.md) §5  
- `Poly/DomainModeling/Analysis/` (grep only as needed)  
- CORE §3.1 catalog note  

## Exact steps

1. List all `IAnalysisMetadata` record types under DomainModeling + publisher pass.  
2. For each bag, list known consumers (analysis / runtime / lowering / MCP / DslCompiler).  
3. Grep residual scans in Analysis + Lowering + DomainEntityInstance (high-signal only).  
4. Write results to `docs/plans/amu-inventory-YYYYMMDD.md` (or extend findings §5 with dated subsection).  
5. Mark which W1–W4 tasks own which residual rows.

## Verification

- [x] Inventory file committed — `docs/plans/amu-inventory-20260806.md` (workspace; git commit deferred to suite close unless requested)
- [x] Every W1–W4 task has at least one residual row assigned (§4 map; W4 is a projection task, mapped explicitly)
- [x] No production code required for Done

## Status

**Status:** Done — 2026-08-06. Live inventory written: 24 metadata bags, consumers per bag, 24 residual scan rows (R01–R24), task ownership map. W1.1→R01–R06, W1.2→R07–R09, W1.3→R10–R11, W2.1→R12–R14 + Dependencies audit, W3.1→R15–R19, W3.2→R20–R22, W4→projection over existing bags.
