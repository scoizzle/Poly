# AST Types as First-Class Provider + Domain Instance Ergonomics

**Date:** 2026-07-12  
**Status:** Proposal — post-M2 capability expansion  
**Depends on:** `TypeDefinitionNodeAnalyzer` (exists), `ITypeDefinitionProvider` (exists), `PolicyEvaluator` (exists), `DomainExpressionLoweringPass` (exists)  
**Anti-patterns to avoid:**
- **#005 (second-system effect):** Wire AST types through existing Introspection seams — do not build a parallel type registry or a second emitter path.  
- **#004 (interface new-hiding):** No `new` member declarations that shadow base interface members.  
- **#007 (single-point dependency):** Keep providers composable; do not hardcode AST types into the emitter or analysis framework.

---

## Problem

AST-defined types (types created from domain model metadata) exist today only as an ephemeral simulation: `TypeDefinitionNodeAnalyzer` builds `AstTypeDefinition` instances that wrap `Dictionary<string, object?>` at runtime, but they live **inside the analyzer** and are invisible to the broader Introspection / provider chain. This forces every consumer that needs to work with AST types to either:

- Rebuild the type definition each time (as `PolicyEvaluator.evaluate_policy` MCP tool does ad-hoc), or
- Work directly with `Dictionary<string, object?>` and bypass the type system entirely.

Meanwhile, the domain model has entities, properties, stages, and actions — but no way to materialize an instance, call an action, observe stage transitions, or evaluate a policy against a typed instance without jumping through hoops.

---

## Approach

Five incremental phases, each building on the last. No phase forces framework completeness — only the thinest slice that unblocks the next.

```text
Phase 1  AST types as proper Introspection provider   ── register + resolve like CLR types
Phase 2  Type-safe dictionary access + defaults       ── coercion, missing-key handling
Phase 3  Domain instance lifecycle                    ── create, read properties, evaluate
Phase 4  Policy eval as product demo                  ── MCP tool for arbitrary entity/policy eval
Phase 5  Onboarding quickstart                        ── one-file end-to-end walkthrough
```

---

## Phase 1 — AST types as proper Introspection provider

**Why first:** Until AST types are visible through the standard `ITypeDefinitionProvider` chain, no downstream consumer (analysis passes, emitter, MCP tools) can resolve them uniformly. The current ad-hoc approach (rebuild per call) doesn't scale to a product path.

### Current state

- `AstTypeDefinition` implements `ITypeDefinition` and `IClrTypeDefinition` — it *can* participate in the provider chain.
- `TypeDefinitionNodeAnalyzer` produces `AstTypeDefinition` instances and stores them in its own `_typeDefinitions` dictionary.
- `TypeDefinitionProviderCollection` already exists and layers providers LIFO.
- `AnalysisContext` accepts an `ITypeDefinitionProvider` at construction.
- Gap: `TypeDefinitionNodeAnalyzer`'s types are not registered with any shared provider. They're only resolvable while that specific analyzer's analysis context is alive.

### Approach

No new types. `TypeDefinitionNodeAnalyzer` already implements `ITypeDefinitionProvider` — it has `GetTypeDefinition(string)`, `GetTypeDefinition(Type)`, and `GetTypeDefinitions()`. It already has `Freeze()`. The only change is plumbing:

```csharp
var providers = new TypeDefinitionProviderCollection(ClrTypeDefinitionRegistry.Shared);
var context = new AnalysisContext(providers);
// ... run analyzers including TypeDefinitionNodeAnalyzer ...
analyzer.Freeze();
providers.Add(analyzer);  // analyzer itself IS the provider
```

### Tasks

| ID | Task | Exit check |
|----|------|------------|
| **P1.1** | `Interpreter.Analyze` / `Interpreter.Compile` construct `AnalysisContext` with a `TypeDefinitionProviderCollection` instead of bare `ClrTypeDefinitionRegistry.Shared`. After the standard pass list completes, freeze `TypeDefinitionNodeAnalyzer` (if present) and add it to the collection. | `context.TypeDefinitions` resolves AST types by name after analysis. |
| **P1.2** | `Interpreter.Analyze` / `Interpreter.Compile` accept optional pre-built `TypeDefinitionProviderCollection`. When provided, it replaces the default (caller manages lifetime, can pre-populate). | Caller-supplied providers are used when specified. |
| **P1.3** | The `evaluate_policy` MCP tool uses the new path instead of building its own ad-hoc `TypeDefinitionNode`→`AstTypeDefinition`→compile. | Old tests pass; MCP policy eval works identically. |

**Dependencies:** None (pure refactor).  
**Risk:** Low — no new types. The analyzer already is a provider.  
**Tests:** Existing tests pass. Add one test that resolves an AST type by name through `context.TypeDefinitions` after analysis.

---

## Phase 2 — Type-safe dictionary access + defaults

**Why:** `AstPropertyDefinition.EmitRead` currently returns `Missing.Value` for absent keys. Real entity instances need proper type coercion, default values, and structural validation at construction time.

### Current state

- `AstPropertyDefinition.EmitRead` emits `ContainsKey` + `get_Item` with `Missing.Value` fallback.
- No type coercion: if the property says `Number` but the dictionary holds an `int`, caller gets whatever was stored.
- No default value support: no way to say "default is 0" vs "default is `Missing.Value`."
- `AstFieldDefinition` has no `EmitRead`/`EmitWrite` at all (returns null — not readable/writable).

### Tasks

| ID | Task | Exit check |
|----|------|------------|
| **P2.1** | Define a `DictionaryBackedValue` helper (or strategy) that provides type coercion for values read from `Dictionary<string, object?>`: given a value and a target domain type (`PrimitiveType`), coerce `int` → `long`, `int` → `double`, `float` → `double`, etc. Start with the types used in `DomainTypeToClrType` mapping. | Coercion handles all primitive domain types correctly. |
| **P2.2** | Add `DefaultValue` property to `AstPropertyDefinition` (nullable `object?`). When the domain model's property has a default expression (e.g., `0`, `false`), store it here. `EmitRead` returns the default instead of `Missing.Value` when the key is absent. | `EmitRead` for missing key returns the default, not `Missing.Value`. |
| **P2.3** | Implement `EmitRead`/`EmitWrite` on `AstFieldDefinition` using the same dictionary access pattern as `AstPropertyDefinition`. Fields are stored as dictionary entries keyed by field name. | `AstFieldDefinition.EmitRead(write)` works — field reads/writes go through dictionary. |
| **P2.4** | Add structural validation: on first access (or explicit `Validate` call), verify that all properties defined on the type exist in the dictionary. Emit a diagnostic or throw if required properties are missing. | Validation catches missing required properties. |

**Dependencies:** Phase 1 (provider registration makes AST types resolvable through the standard chain).  
**Risk:** Low — additive changes to existing `AstPropertyDefinition`/`AstFieldDefinition`. No new types needed.  
**Tests:** Add tests for coercion, default values, field EmitRead/EmitWrite, and structural validation.

---

## Phase 3 — Domain instance lifecycle

**Why:** The domain model defines entities with properties, stages, and actions — but there's no way to materialize an entity instance, bind property values, call an action, or observe stage transitions at the VM level. Phase 3 creates the thinnest possible runtime for domain entity instances.

### Current state

- `CreateEntityInstance` exists as a domain-level *effect model* (design-time, never executed).
- `PolicyEvaluator` evaluates predicates against dictionaries — but the entity instance itself is not a first-class concept at runtime.
- No way to say: `CreateInstance(Person)` → set `Name = "Alice"` → evaluate `IsAdult` → get result.

### Tasks

| ID | Task | Exit check |
|----|------|------------|
| **P3.1** | Define `DomainEntityInstance` record class: holds `Entity` (domain model entity definition), backing `Dictionary<string, object?>` of property values, optional stage/state tracking, and a reference to the `ITypeDefinition` (resolved `AstTypeDefinition`) for compilation. | Simple data holder; no execution logic. |
| **P3.2** | `DomainEntityInstance.Create(Entity entity, IDictionary<string, object?> properties)` factory. Validates required properties exist (Phase 2), applies defaults, sets initial stage (if entity has stages). Returns `DomainEntityInstance` or validation error. RAII-style: factory returns only valid instances. | Factory rejects missing required properties; returns instance with defaults + initial stage. |
| **P3.3** | `DomainEntityInstance.EvaluatePolicy(Policy policy, Interpreter interpreter)` — lowers the policy expression with the instance as subject, compiles via `Interpreter.Compile` (using Phase 1's provider path), executes, returns `bool`. This is the **VM-primary path** for instance-scoped policy evaluation. | `instance.EvaluatePolicy(policy)` works end-to-end via the VM. |
| **P3.4** | `DomainEntityInstance.GetProperty<T>(string name)` / `SetProperty(string name, object? value)` — typed accessors that go through the dictionary backing. `GetProperty` uses Phase 2 coercion. | Read/write properties on an instance. |
| **P3.5** | `DomainEntityInstance.CallAction(string actionName, IDictionary<string, object?>? args, Interpreter interpreter)` — resolves the action from the entity definition, evaluates guard policies (if any), executes action effects (initially just logging/stage transitions; full effect execution is Phase 4 territory). | Action call evaluates guards and reports whether the action is allowed. |

**Dependencies:** Phase 1 + Phase 2.  
**Risk:** Medium — this is new surface, but it composes existing pieces (lowering, analysis, compile, execute). The instance itself is just a data bag + compilation orchestration.  
**Tests:** Create entity instance → set properties → evaluate policy → call action. Test success and failure paths (missing property, policy violation, unknown action).  
**Anti-pattern guard:** Phase 3 must NOT introduce a domain-specific VM opcode or a second evaluator. Everything composes through existing `DomainExpressionLoweringPass` → `Interpreter.Compile` → `Interpreter.Execute`.

---

## Phase 4 — Policy eval as MCP product demo

**Why:** The `evaluate_policy` MCP tool exists today but only works for the `Age >= 18` pattern with a flat "properties" bag. Phase 4 makes it general-purpose: any entity, any policy, real domain instances.

### Current state

- `V3EvalTool.evaluate_policy` creates an ephemeral `TypeDefinitionNode("Subject", ...)` and evaluates a single policy expression.
- Only supports flat property bags — no entity, no stages, no actions.
- The MCP tool name and description are honest but minimal.

### Tasks

| ID | Task | Exit check |
|----|------|------------|
| **P4.1** | Refactor `V3EvalTool.evaluate_policy` to use `DomainEntityInstance` (Phase 3) instead of ad-hoc `Subject` type. Accept `entityName` (must be a domain entity, not arbitrary). Accept `stageName?` (optional stage context). Accept `policyName` (must be a policy attached to that entity/stage). | MCP tool resolves entity from session, creates instance, evaluates named policy. |
| **P4.2** | Add `evaluate_action_guard` MCP tool (or extend `evaluate_policy` with `actionContext`). Given entity name + optional stage + action name + property values, evaluates all guard policies on that action and returns per-policy pass/fail. | Agent can ask "will `Confirm` on this `Order` instance pass its guards?" before calling the action. |
| **P4.3** | Add `create_entity_instance` MCP tool. Given session ID + entity name + JSON property values, returns instance ID (for subsequent eval calls) + validation errors. Instance lives in session-scoped store (in-memory for now). | Agent can create an instance, then reference it by ID in subsequent tool calls. |
| **P4.4** | Add `debug_policy` MCP tool. Given session ID + entity name + instance ID + policy name, compiles the policy expression and returns the VM step trace (via `VmDebugger`) with per-node values. | Agent can step through policy evaluation node by node. |

**Dependencies:** Phase 3.  
**Risk:** Medium — new MCP tools mean new serialization boundaries, error paths, and description maintenance.  
**Tests:** MCP tool integration tests (smoke for the agent path). Dual-oracle (VM vs LINQ) for policy eval correctness.

---

## Phase 5 — Onboarding quickstart

**Why:** A working platform with no visible entry point is indistinguishable from a broken one. (From `future-platform-capabilities.md` item 1.)

### Current state

- `FluentApiExample.cs` is commented out.
- No standalone demo project.
- No tutorial.
- `PolicyEvaluator` + MCP tools exist but require knowledge of the domain model types.

### Tasks

| ID | Task | Exit check |
|----|------|------------|
| **P5.1** | Create `demo/` project (standalone console app, `net10.0`) with a single `Program.cs`. Define a domain entity (e.g., `Person` with `Name`, `Age`, stage `Active`), create an instance, evaluate a policy, print result. One file, one `dotnet run`. | New user can run `dotnet run --project demo` and see policy evaluation. |
| **P5.2** | Add a second demo that: creates entity → creates instance → evaluates policy → calls action → shows stage transition. End-to-end vertical slice in ~30 lines. | Demo shows the full domain→lower→compile→execute path. |
| **P5.3** | Add README.md to `demo/` explaining the walkthrough and linking to relevant docs (`docs/CORE.md`, module READMEs). | README is a 2-minute orientation. |

**Dependencies:** Phase 3 (instance creation) + Phase 4 (policy eval). P5.2 needs Phase 4's action support.  
**Risk:** Low — no new platform surface, just consumption.  
**Tests:** The demo *is* the test — it must compile and run. No additional test project needed.

---

## Timeline estimate

| Phase | Effort | Can parallelize with |
|-------|--------|----------------------|
| P1 — Provider registration | 0.5d | — |
| P2 — Dictionary defaults | 0.5d | P1 (after P1.1 done) |
| P3 — Instance lifecycle | 1–2d | P2 (after P1 done) |
| P4 — MCP tools | 1–2d | P3 (after P3.1–3.3 done) |
| P5 — Quickstart | 0.5d | P4 (after P3 done) |

**Total:** ~3–6 days depending on parallelization and review depth.

---

## Non-goals (explicitly deferred)

- **Effect execution** (CreateEntityInstance, stage transitions, publish event): Phase 3's `CallAction` is a stub — executing effects requires lowering effect types to Syntax AST, which is Slice 4 from the vertical-slice plan. Do not scope-creep Phase 3 into full effect execution.
- **Persistence:** Instance store is in-memory session only. JSON serialization and database-backed storage are `future-platform-capabilities.md` items.
- **Relationship navigation:** Instance properties that reference other entities. Requires `RelationshipNavigation` lowering support, which is Slice 5.
- **Actor/macro system:** `Poly.Synthesis/` is intentionally a stub.
- **Validation rule execution:** `Poly.Validation.*` is deprecated per platform direction.
