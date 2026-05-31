// This file previously contained EvolutionTransaction — the explicit transaction/commit
// coordinator for the evolution layer.
//
// DECISION (June 2026): The full transaction/commit/rollback model was removed.
// See docs/decisions/2026-05-31-evolution-layer-design.md (Open Question #8 resolved).
//
// Rationale (summary):
// - V2 required compensating Apply/Rollback commands + a lock because the domain was mutable.
// - V3 uses immutable records. A proposed new root is simply discarded on analysis failure.
// - "Atomicity", rollback visibility, and rich traces are delivered by immutable snapshots +
//   a single analysis gate returning EvolutionResult. The extra transaction ceremony added
//   no additional safety or clarity.
//
// Current replacement:
// - DomainEvolution.Apply(IReadOnlyList<DomainChange>) for batch use
// - DomainEvolution.Evolve() → EvolutionBuilder (lightweight fluent accumulator)
//   .Apply(...) to execute with the analysis gate
//
// This file is intentionally left as a tombstone to prevent accidental re-introduction
// and to aid git history / agent understanding. It can be deleted after the port is stable.
//
// Do not add new code here. Update all references to use the simpler model above.

using System;

namespace Poly.DomainModeling.Evolution;

[Obsolete("EvolutionTransaction was removed. Use DomainEvolution.Apply(...) or DomainEvolution.Evolve() instead. See 2026-05-31-evolution-layer-design.md.")]
public sealed class EvolutionTransaction {
    private EvolutionTransaction() { }
}