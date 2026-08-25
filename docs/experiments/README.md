# Experiments & Future Plans

This directory holds speculative documents — specifications, design plans, and roadmaps — for capabilities that **may or may not ever be built**.

## Purpose

These files capture exploratory thinking, alternative architectures, and ambitious future directions. They are retained for:

- Historical context
- Inspiration for later work
- Understanding the design space that was considered

They do **not** represent current priorities, committed roadmap items, or active work.

## What belongs here

- High-level system or DSL specifications in draft form
- Alternative implementation strategies or lowering approaches
- Long-term platform or type system roadmaps that are not yet active
- Other significant speculative design artifacts

### Notable experiments

| Doc | Role |
|-----|------|
| [**domain-plugin-extension-platform.md**](domain-plugin-extension-platform.md) | Future research: C# extension packs for DSL facets (e.g. `column("…")`), analysis, target export — **not** active work |
| [**isa-decompiler-agent-experiment.md**](isa-decompiler-agent-experiment.md) | Speculative: grammar-driven ISA lifting → Poly.Ast → analysis structuring → VM-executable decompilation for agents — **not** active work |

## Related directories

- `docs/decisions/` — Decisions that *have* been formally made
- `docs/plans/` — Active planning, workstreams, and implementation roadmaps
- `docs/plans/v2-to-v3/spikes/` — Smaller technical spikes and explorations

## Note

Files may be promoted into `docs/decisions/` or `docs/plans/` if/when the work becomes active, or archived further if they become obsolete.

**2026-07-06 cleanup note**: Several files here (e.g. `interpretation-compiler-framework-plan.md`) assumed a separate `Poly/Ir/` and/or tree-walker as canonical. They are marked superseded. The current direction keeps the AST as the symbolic/serializable model IR and uses primitives (with metadata *expanded* during lowering, not discarded) as the VM execution IR. See updated `decisions/2026-05-31-neurosymbolic-platform-vision.md` and `2026-07-04-primitives-as-canonical-ir.md`.
