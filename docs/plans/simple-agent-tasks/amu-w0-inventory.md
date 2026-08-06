# AMU-W0 — Publish × consume × residual scan inventory

**Wave:** 0  
**Difficulty:** M  
**Status:** `[ ]`  
**Prereq:** —

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

- [ ] Inventory file committed  
- [ ] Every W1–W4 task has at least one residual row assigned  
- [ ] No production code required for Done  

## File ownership

- **Create:** inventory markdown under `docs/plans/`  
- **Do not edit:** production `.cs` (unless trivial doc comment)  

## Status

**Status:** Not Started  
