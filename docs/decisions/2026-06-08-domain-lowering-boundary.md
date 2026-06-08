# ADR: Domain-Lowering Boundary — No Domain-Specific VM Opcodes

**Date:** 2026-06-08  
**Status:** Accepted  

## Context

The Poly domain model defines high-level concepts: entities with properties and actions, lifecycle stages, actors with identity and roles, policies with rules, event subscriptions with correlation routing. These concepts are defined in `Poly/Data/Modeling` (and eventually `Poly/DomainModeling`).

The VM executes lowered IR. A question arose: should these domain concepts lower to **new VM opcodes** (e.g. `CheckPolicy`, `DispatchEvent`, `ResolveActor`), or should they lower to **existing generic opcodes** (calls, conditionals, members)?

## Decision

Domain-level concepts **must lower to existing generic opcodes** (or compositions thereof). No domain-specific opcodes will be added to the VM.

### Contract

1. **Policy evaluation** lowers to a tree of `And`/`Or`/`CallExternal`/`Equal`/`Member` on the entity's properties. The domain model knows the rule structure; it expands it into the IR at lowering time.

2. **Event dispatch** lowers to `CallExternal` against a runtime event bus. The subscription routing (broadcast vs correlated) becomes conditional logic in the lowered form.

3. **Actor identity resolution** lowers to `Member` access on an actor handle + `CallExternal` for claim resolution.

4. **Stage transitions and lifecycle checks** lower to `Equal`/`CallExternal` on the entity's stage property.

5. **Relationship traversal** lowers to `CallExternal` (navigating foreign keys) or to `Member` on synthetic properties injected by the domain lowering generator.

### What does NOT happen

- `OpCode.CheckPolicy` (no — domain rules must be visible in the generic IR)
- `OpCode.DispatchEvent` (no — event routing is a library concern)
- `OpCode.ResolveActor` (no — actor identity is a property access)
- `OpCode.StageTransition` (no — stages are enum-like properties)

## Rationale

- **Keep the VM generic.** A generic VM is testable against a stable conformance suite. Domain-specific opcodes create an ever-growing opcode surface that must be maintained, tested, and implemented in every backend.
- **Domain = macro.** The neurosymbolic vision explicitly says domains are macros that compile into the base IR. Adding domain opcodes blurs this boundary.
- **Composability.** Lowering policies to `And`/`Or`/`Equal` means they benefit from the existing peephole optimizer, constant folder, and conformance suite. A policy expressed as a custom opcode would need its own optimization and testing.
- **Backend portability.** A WASM backend doesn't need to implement `CheckPolicy` — it just needs `CallExternal`, `And`, `Or`, `Equal`, `Member`. This dramatically lowers the cost of targeting a new backend.

## Consequences

- The domain lowering generator (`DomainImplementationLoweringPass`, `DomainLoweringGenerator`) becomes the place where domain concepts expand into generic IR.
- The VM conformance suite stays focused on language-level semantics, not domain-level behavior.
- Domain-level correctness is verified by domain-level tests, not by VM opcode unit tests.
- `CallExternal` will be the primary bridge for operations that genuinely require CLR runtime services (file I/O, networking, reflection, identity resolution). The number of `CallExternal` sites may be high for domain-heavy programs — the permission system becomes correspondingly more important.