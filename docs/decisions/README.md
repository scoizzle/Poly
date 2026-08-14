# Architectural Decisions

This directory contains high-level, cross-cutting architectural decisions that affect large parts of the Poly system.

## Purpose

These documents exist so that:

- Humans and AI agents working on the codebase can quickly understand major directional choices.
- Future work respects previously agreed constraints (especially around the domain model).
- We avoid repeatedly re-litigating the same big decisions.

**Start elsewhere for mechanisms:** Platform purpose, module boundaries, and “use this existing support, do not reinvent” live in **[`docs/CORE.md`](../CORE.md)**. This directory holds the *why* of major choices; CORE holds the *what to use*.

## Guidelines

- Only major, cross-cutting decisions should live here.
- Detailed or module-specific decisions should remain co-located with the code they govern (e.g. inside `Poly/DomainModeling/`).
- Keep entries concise and scannable. Link to more detailed records where they exist.
- Update `AGENTS.md` when adding new decisions that agents must be aware of.

**Note on Plans vs Decisions**: Execution roadmaps live in `docs/plans/`.  
**Active product plan:** [`docs/plans/v2-to-v3/master-roadmap.md`](../plans/v2-to-v3/master-roadmap.md).  
**Archived Interpretation IR-era plans:** [`docs/plans/archive/interpretation/`](../plans/archive/interpretation/README.md) (do not execute).

## Current Decisions

- [2026-08-14: Domain libraries, not packs](2026-08-14-domain-libraries.md) — Libraries load into a session/compile. Temporal is language default; annotations are optional; no module-initializer meaning.
- [2026-08-10: Relationships as Entity-Owned Navigations (Synthesized Domain View)](2026-08-10-relationships-as-entity-owned-navigations.md) — Relationship = source-entity-owned navigation; `Domain.Relationships` is a computed flatten; the semantic view is analysis-synthesized from entity navs; back-references are derived. Supersedes the domain-wide relationship-name uniqueness model (scoped in the 2026-08-10 slice).
- [2026-07-22: Persistence Units, Medium-Scoped Facets, and Pack Syntax Export](2026-07-22-persistence-units-medium-facets-pack-syntax-export.md) — **Analysis hub → downstream artifact consumers**; multi-DBMS persistence units + medium facets; **resulting artifacts**; C# as pack-movable target; single `--dbms` / string generators are transitional scaffolding.
- [2026-07-11: Platform Trust Bar and Dogfood Gates](2026-07-11-platform-trust-bar-and-dogfood.md) — First customer; product via domain + modules; external contracts; **product generation funds neurosymbolic work**; T1/T2/T3; trust layer 1 = honesty.
- [2026-05-31: Immutable Core for Domain Modeling (V2 → V3)](2026-05-31-immutable-core-domain-modeling.md) — Strategic shift to immutable records while preserving the transactional evolution/correctness guarantees required by LLM agents.
- [2026: V2 → V3 Domain Modeling Port Plan](2026-v2-to-v3-domain-modeling-port.md) — Living plan for the port to the immutable core + thin evolution layer (includes integration with the documentation and agent instruction structure).
- [2026-05-31: Neurosymbolic Platform Vision](2026-05-31-neurosymbolic-platform-vision.md) — Historical vision document. Core ideas remain relevant. **Many specifics superseded**: tree-walker removed; AST is primary symbolic form; execution is direct AST→VM-ABI (`DirectVmAbiEmitter`), not a separate primitive IR (see 2026-06-08-vm-as-canonical-semantics + superseded 2026-07-04 ADR).
- [2026-06-08: VM as Canonical Semantics](2026-06-08-vm-as-canonical-semantics.md) — Tree-walker removed; VM becomes the canonical semantics reference for all backends.
- [2026-06-08: Breakpoint Architecture](2026-06-08-breakpoint-architecture.md) — PC-level breakpoints via Int/Iret interrupts, managed externally. No AST modification.
- [2026-06-08: Heap Reclamation Strategy](2026-06-08-heap-reclamation.md) — Free-list with explicit null-out. No tracing GC.
- [2026-06-08: Peephole Optimizer](2026-06-08-peephole-optimizer.md) — Post-lowering pass on bytecode array, optional. Common fold patterns.
- [2026-06-08: Bytecode Serialization](2026-06-08-bytecode-serialization.md) — Portable binary format replacing CLR references with stable identifiers.
- [2026-06-08: VM Sandboxing](2026-06-08-sandboxing-approach.md) — Permission table checked at CallExternal entry. Deny by default, allow overrides.
- [2026-06-08: Domain-Lowering Boundary](2026-06-08-domain-lowering-boundary.md) — No domain-specific VM opcodes. Domain concepts lower to existing generic ops.
- [2026-06-09: Comparison Fusion Encoding](2026-06-09-comparison-fusion-encoding.md) — Why comparison+branch fusion lives in lowering, not in the comparison opcodes. Signed overflow rules out the subtraction trick.
- [2026-07-04: Primitives as Canonical IR](2026-07-04-primitives-as-canonical-ir.md) — **Superseded.** Title is historical; body documents removal of primitive IR in favor of direct AST→VM-ABI.
- [2026-07-05: VM Exception Handling — Strategy B](2026-07-05-vm-exception-handling-strategy-b.md) — Structured EH via side table (ExceptionRegionTable) with handler dispatch, aligned with LLVM/CLR/JVM practice. Replaces the earlier Strategy A (LINQ nesting) recommendation.