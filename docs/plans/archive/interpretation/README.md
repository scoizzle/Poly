# Archived Interpretation Plans

**Archived:** 2026-07-10  
**Reason:** These plans describe a **superseded** Interpretation architecture and must not drive new work.

## Current architecture (authoritative)

| Layer | Reality |
|-------|---------|
| Symbolic IR | `Poly.Syntax.Nodes` AST |
| Execution | Direct AST → VM ABI via `DirectVmAbiEmitter` → `Action<VmState>` |
| Engine | `Poly/Interpretation/Vm/` only (no tree-walker) |
| Domain boundary | Domain constructs lower to **generic** AST/opcodes (`2026-06-08-domain-lowering-boundary.md`) |
| Decisions | `docs/decisions/2026-06-08-vm-as-canonical-semantics.md`, `2026-07-04-primitives-as-canonical-ir.md` (historical title; body is direct AST path), `Poly/Interpretation/README.md` |

**Active product planning** is DomainModeling V2→V3: `docs/plans/v2-to-v3/master-roadmap.md`.

## What was archived

Plans that assumed one or more of:

- Intermediate **primitive / µop / Instruction** IR as the execution target
- **`ProgramCompiler` / bytecode** dispatch or stack-depth validation of bytecode
- **Tree-walking interpreter** as reference or co-equal engine
- **`Poly/Interpretation/VirtualMachine/`** RISC bytecode path as unfinished future work
- **Completed campaigns** whose live task lists would mislead agents (direct-lowering finish, pruning primitives tracker, INT/ANA resolution plan framed around primitive IR)

| File | Why archived |
|------|----------------|
| `risc-ir-stack-vm-implementation-plan.md` | Bytecode / VirtualMachine skeleton era; VM shipped as direct ABI under `Interpretation/Vm/` |
| `tree-walking-interpreter-design.md` | Tree-walker removed |
| `block-aware-writer.md` | µop `ProgramCompiler` rewrite — no µop list |
| `ir-lowering-redesign.md` | InstructionSequence / metadata on µops |
| `vm-jit-plan.md` | Bytecode jitterpreter |
| `vm-instruction-consolidation-and-register-allocation.md` | µop inventory + linear-scan on µops |
| `compilation-context.md` | Old compilation model |
| `worklist-lowering.md` | Recursive Emit replacement for old lowering |
| `stack-depth-definite-assignment-analyzers.md` | Bytecode stack balance |
| `future-analyzers.md` | Bytecode stack depth analyzer |
| `abstract-interpretation-and-ssa.md` | SSA over bytecode/µops |
| `pruning-primitives-plan.md` | Campaign complete; path pruned |
| `finish-direct-lowering-and-abi-refactor.md` | Campaign largely complete; residual work only if first consumer appears |
| `direct-lowering-audit-2026-07-07.md` | Point-in-time audit snapshot |
| `direct-lowering-simple-agent-prompt.md` | Campaign agent prompt |
| `interpretation-system-resolution-plan.md` | Task list framed around primitive IR + dual engines |
| `interpretation-system-issues.md` | INT/ANA tracker tied to that plan — open items must be re-filed against current ABI if still real |
| `vm-test-gap-closure.md` | Gap list vs old VM feature matrix |
| `vm-debugger-architecture.md` | Draft coupling model; shipped approach is `VmDebugger` + `DebugHook` |
| `vm-interpretation-complexity-reduction-plan.md` | Implemented against prior structure |
| `perf-improvement-task-list.md` | Frame/ABI tasks from mid-pivot; re-evaluate only with measured gap |
| `anti-pattern-002-speculative-capture.md` | Bytecode payload fields |
| `anti-pattern-006-completeness-uop-inventory.md` | µop inventory completeness |
| `v2-to-v3/spikes/lowering-as-analysis-passes.md` | µop/assembly-centric lowering passes |
| `v2-to-v3/spikes/lowering-analysis-passes-phase2.md` | Same era |

## Policy for agents

1. **Do not implement tasks from this archive** unless an orchestrator explicitly reopens an item after validating it against `DirectVmAbiEmitter` / current decisions.
2. If an archived INT-/ANA- item is still valid, create a **new** task under an active plan (or a short issue note in a decision), referencing current files — do not “resume” the archived tracker as-is.
3. Historical context is fine for reading; **execution truth** is AGENTS.md + `docs/decisions/` + `Poly/Interpretation/README.md` + V2→V3 master roadmap.

## Still-active Interpretation-adjacent plans (not archived)

Kept under `docs/plans/` when still useful against the direct ABI:

- `array-specialization-plan.md` — compile-time array elem type (emitter still has TypeIs fallback)
- `analyzer-improvements.md` — analysis quality (review before implementing; may need light refresh)
- Anti-patterns **001, 003, 004, 005, 007** — general engineering lessons
- `future-platform-capabilities.md` — product ideas, not IR
- `neurosymbolic-platform-from-first-principles.md` — vision; prefer decisions for IR truth
