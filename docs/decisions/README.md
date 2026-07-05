# Architectural Decisions

This directory contains high-level, cross-cutting architectural decisions that affect large parts of the Poly system.

## Purpose

These documents exist so that:

- Humans and AI agents working on the codebase can quickly understand major directional choices.
- Future work respects previously agreed constraints (especially around the domain model).
- We avoid repeatedly re-litigating the same big decisions.

## Guidelines

- Only major, cross-cutting decisions should live here.
- Detailed or module-specific decisions should remain co-located with the code they govern (e.g. inside `Poly/DomainModeling/`).
- Keep entries concise and scannable. Link to more detailed records where they exist.
- Update `AGENTS.md` when adding new decisions that agents must be aware of.

**Note on Plans vs Decisions**: Execution roadmaps, phase breakdowns, and task tracking now live in `docs/plans/`. See `docs/plans/v2-to-v3-domain-modeling-port-roadmap.md` for the current V2→V3 port tracker.

## Current Decisions

- [2026-05-31: Immutable Core for Domain Modeling (V2 → V3)](2026-05-31-immutable-core-domain-modeling.md) — Strategic shift to immutable records while preserving the transactional evolution/correctness guarantees required by LLM agents.
- [2026: V2 → V3 Domain Modeling Port Plan](2026-v2-to-v3-domain-modeling-port.md) — Living plan for the port to the immutable core + thin evolution layer (includes integration with the documentation and agent instruction structure).
- [2026-05-31: Neurosymbolic Platform Vision](2026-05-31-neurosymbolic-platform-vision.md) — Architectural vision for Poly as a neurosymbolic platform: models codify discovered algorithms as composable macros in a symbolic IR, validated by a tree-walker interpreter, compiled to native backends. Reframes domain modeling as compiler frontend for program synthesis.
- [2026-06-08: VM as Canonical Semantics](2026-06-08-vm-as-canonical-semantics.md) — Tree-walker removed; VM becomes the canonical semantics reference for all backends.
- [2026-06-08: Breakpoint Architecture](2026-06-08-breakpoint-architecture.md) — PC-level breakpoints via Int/Iret interrupts, managed externally. No AST modification.
- [2026-06-08: Heap Reclamation Strategy](2026-06-08-heap-reclamation.md) — Free-list with explicit null-out. No tracing GC.
- [2026-06-08: Peephole Optimizer](2026-06-08-peephole-optimizer.md) — Post-lowering pass on bytecode array, optional. Common fold patterns.
- [2026-06-08: Bytecode Serialization](2026-06-08-bytecode-serialization.md) — Portable binary format replacing CLR references with stable identifiers.
- [2026-06-08: VM Sandboxing](2026-06-08-sandboxing-approach.md) — Permission table checked at CallExternal entry. Deny by default, allow overrides.
- [2026-06-08: Domain-Lowering Boundary](2026-06-08-domain-lowering-boundary.md) — No domain-specific VM opcodes. Domain concepts lower to existing generic ops.
- [2026-06-09: Comparison Fusion Encoding](2026-06-09-comparison-fusion-encoding.md) — Why comparison+branch fusion lives in lowering, not in the comparison opcodes. Signed overflow rules out the subtraction trick.
- [2026-07-04: Primitives as Canonical IR](2026-07-04-primitives-as-canonical-ir.md) — The PrimitiveNode instruction set IS the canonical intermediate representation, superseding the planned separate Poly/Ir/. Adds ValueSlot, Phi, BasicBlock, Module to the primitive format.