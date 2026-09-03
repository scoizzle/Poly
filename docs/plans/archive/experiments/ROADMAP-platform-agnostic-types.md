# Platform-Agnostic Type System Roadmap

This roadmap formalizes a platform-neutral domain type system where CLR is only one adapter target.

## Scope constraints

- `Poly.Validation.*` is treated as deprecated for now.
- Core type semantics must be platform-agnostic.
- Runtime-specific mapping belongs in adapters (CLR, SQL, TypeScript, etc.).

## Phase 1 (in progress): canonical built-ins and bootstrap

### Goals

- Provide generic built-in types out of the box.
- Remove manual primitive bootstrap friction in MCP sessions.
- Keep naming semantic rather than CLR-specific.

### Implemented

- Added canonical built-in catalog in `Poly.Data.Modeling.TypeSystem`.
- Added initial set: `Boolean`, `Integer`, `Decimal`, `Text`, `Date`, `Time`, `DateTime`, `Duration`, `Uuid`, `Binary`.
- Domain session creation now seeds these built-ins and stores resulting analysis as revision `0` baseline.

### Remaining in Phase 1

- Add explicit wrapper constructors/usages for `Optional<T>` and `Collection<T>` in MCP mutation APIs.
- Add MCP/query DTOs for wrapper type composition.
- Add analyzer diagnostics for invalid wrapper composition and unknown type references.
- Add documentation for canonical type semantics and mapping intent.

## Phase 1.5 (✅ Complete): Effect Wiring Model

### Goals:

- Model how effects produce and consume values (declarative, not execution).
- Enable cross-effect data flow for realistic domain behavior.
- Provide the foundation for code generation (AST → target language).

### Implemented:

- **`EffectResult`** (new) — declares what an effect produces (named tuple; like C# function return).
- **`EffectValueRef`** (new) — reference to a specific named output from a prior effect.
- **`Effect.BindOutputTo()`** — shortcut to wire: `effectA.BindOutputTo("entity", effectB, "param")`.
- **`InvokeAction.BindParameterFrom()`** — bind param to prior effect's output.
- **`CreateEntityInstance`** — declares it produces `"entity"` output.
- **Base `Effect`** — now has `Result` property, `Produces()` method, `BindOutputTo()`.
- **Tests** — 19 new tests in `EffectWiringTests.cs` (838 total, all passing).

### Enables:

- Cross-entity behavior via action invocation with wired parameters.
- `CreateEntityInstance` → `InvokeAction` pipelines (entity ref flows through context).
- Foundation for AST generation (wiring model → `Expression` tree → C#/TypeScript).

## Phase 1.75 (✅ Complete): Constraint Propagation Analyzer

### Goals:

- Propagate constraints from downstream effect property accesses UP to Action parameters.
- Enable earlier validation and richer code generation.

### Implemented:

- **`ConstraintPropagationAnalyzer`** (new) — walks effect graph, collects constraints from property accesses, attaches to Action parameter metadata.
- **Registered** in `DomainModelAnalyzer` pipeline.
- **Tests** — 3 new tests in `ConstraintPropagationAnalyzerTests.cs` (840 total, all passing).
- **Fixed**: Now traces through `InvokeAction` into target action's effects and collects ALL property constraints.

### How It Works:

```csharp
Action CheckoutBook {
    Parameters: { book: Book }  // Book.AvailableCopies has RequiredConstraint
    
    Effects: [
        CreateEntityInstance(Loan) → produces "entity"
        InvokeAction(Target: DecrementCopies) 
            ← BindParameterFrom("book", createLoan, "entity")
    ]
    
    // Analyzer propagates: 
    // book.Parameter now has "downstreamConstraints" metadata
    // containing RequiredConstraint from AvailableCopies
}
```

### Enables:

- Code generation can validate parameters earlier based on downstream usage.
- Richer emit'd code: parameter validation reflects how it's actually used.

## Phase 1.875 (✅ Complete): Enum Constraint Subset Validation

### Goals:

- Validate that EnumConstraint on properties is a subset of parent entity's constraint.
- Prevent invalid enum values from being added in child entities.

### Implemented:

- **`EnumConstraintSubsetAnalyzer`** (new) — validates property's enum is subset of parent's.
- **Diagnostic `DMSEM004`** — `SemanticConstraintMismatch`.
- **Tests** — 2 new tests in `DomainModelDiagnosticContractTests.cs` (840 total, all passing).

### Example:

```csharp
// Parent: Entity "Task" has property "Status" with Enum { "Open", "InProgress", "Done" }
// Child: Entity "UrgentTask" inherits Task
// Child's "Status" with Enum { "Open", "InProgress" } ← ✅ Valid (subset)
// Child's "Status" with Enum { "Open", "Unknown" } ← ❌ INVALID (Unknown not in parent)
```

### Enables:

- Safe hierarchy evolution — child can't break parent's contract.
- Earlier validation — caught at modeling time, not runtime.

## Phase 2: first-class enum and value object modeling

### Goals

- Make enum/value object native constructs, not primitive categories.
- Validate enum literals and value object invariants at modeling time.

### Work items

- Introduce enum type with allowed value set.
- Introduce value object type with properties and constraints.
- Add diagnostics for invalid enum/value object usage.

## Phase 3: adapter-based platform mapping

### Goals

- Keep core model independent of any runtime.
- Add mappers translating canonical types to platform-native targets.

### Work items

- Define platform mapping contract from canonical type graph to target type graph.
- Implement CLR adapter without leaking CLR names into core schema.
- Add capability diagnostics for unsupported target mappings.

## Phase 4: migration/evolution and operability

### Goals

- Improve long-term domain evolution safety.
- Improve diagnostics and persistence UX.

### Work items

- Add compatibility analyzer for breaking model changes.
- Add export/import for domain persistence.
- Improve diagnostics to be remediation-first and expert-friendly.
