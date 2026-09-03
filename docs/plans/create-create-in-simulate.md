# Simulate the lowered program (create/create-in remaining)

**Date:** 2026-09-03  
**Status:** DONE — simulate = Interpreter + bound Store. Authority: [`simple-agent-tasks/PIPELINE-STATUS.md`](simple-agent-tasks/PIPELINE-STATUS.md) (`CURRENT: (none)`).  
**Suite README:** [`simple-agent-tasks/create-create-in-README.md`](simple-agent-tasks/create-create-in-README.md)  
**Language lock:** [`decisions/2026-09-03-facts-concerns-bags-store-bind.md`](../decisions/2026-09-03-facts-concerns-bags-store-bind.md)  
**Pre-ship:** [`v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md`](v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md)

Do not invent a second CURRENT. Unique Store bind already shipped on this stream. This plan is the rest of create/create-in: simulation must run the implementation lowering already produced.

---

## Success

Simulate = `Interpreter` on the **lowered operation AST** + bound Store + dictionary-backed `This`.

Same tree as project (C# print). No `ExecuteStructured`. No Domain / Effect-IR walk as shipped meaning. MCP `create_instance` then `invoke_action` / `evaluate_policy` is that path with caller-supplied context.

---

## Why simulation is still fragile

Lowering already transforms facts + bags into a viable implementation. Unique assign now invokes Store (`EnsureUnique`) inside that tree. Create / create-in and store-aware expressions still assume the **running program knows the domain**:

| Residue | Where | What it does |
|---------|--------|----------------|
| `ExecuteStructured` / `ExecuteEffect` | `DomainEntityInstance.cs` | Walks Effect IR when `RequiresDirectExecution` (`CreateEntityInstance` with `RelationshipName`) or `HasEffectDependentConditionalCreate` |
| `CreateByType` / `CreateInNav` / `ProbeCreateByType` | `HostAbi` `InvokeNamed` | Instance factories that call `CreateChildInstance` / `ExecuteCreateInRelationship` — still Domain + store auto-link beside the tree |
| `PrevalidateUnconditionalCreates` | `HostAbi` | Evals `if` conditions against the bag **before** the tree |
| `PreprocessQuantifiers` / `PreprocessEffectExpressions` | execute time | Store-aware `Rel exists` / any/all/none/count / path-prefix rewritten to **literals** before lower — domain/store knowledge at simulate, not Store calls in the tree |
| `LowerStageTransitions` | `EffectLoweringPass` | C# `Stay.Create` / `this.CreateNav` vs runtime factories. Not a new flag to grow |

Crossing duties is how unique became a host prelude and create became a second interpreter: VM ran Syntax while `DomainEntityInstance` still consulted `Domain` beside it.

---

## Locks (do not reopen)

| ID | Rule |
|----|------|
| L1 | Words: facts / concern / bag / surface / Store / bind / lower / project. Collaborator is **Store**, not `IStorage`. |
| L2 | Lowering **process** reads bags. Operation AST **product** has no bag types and does not re-scan Domain. |
| L3 | Simulate runs the lowered program. It does not execute Domain / Effect IR. |
| L4 | Dictionary-backed `This` is Interpretation’s type-def path: `AstTypeDefinition.RuntimeType` is `IDictionary<string, object>`; member read/write is the indexer (`TypeDefinitionNodeAnalyzer`). `DomainEntityInstance` already implements `IDictionary<string, object?>`. |
| L5 | Do **not** invent ExpandoObject as the sim subject. Do **not** invent a third instance type. `PolicySubject` rejecting raw `Dictionary` / `ExpandoObject` is the **test-only** CLR-subject wrapper — not the VM dictionary-backed type-def path. |
| L6 | Create / CreateIn follow unique: **Notify-shaped** on the instance (`this.Create` / `this.CreateIn` delegate to bound Store). Dictionary `This` cannot Member-read `Store`. |
| L7 | If project needs a bag to print an action body, the collaborator was not bound. If emit of a Store job is hard, bind the host Store (later EF) — do not add a consumer-specific lowering flag. |
| L8 | Constraint checks (required, pattern, range) may stay on the entity factory. Graph wiring and uniqueness belong on Store. |
| L9 | Fail-closed: empty matches, missing Store, missing nav/FK, invalid config fail loud. Failure without prior mutate (pin `ActionEntityReturnTests`). |
| L10 | Shipped ⊆ lowerable. Do not restore `Comment`, a second interpreter, or `ExecuteStructured` as shipped meaning. |

---

## Interpretation already simulates dynamic objects

Product path (do not re-derive):

1. Entity schema → `TypeDefinitionNode` (`BuildTypeDefNode`).
2. `TypeDefinitionNodeAnalyzer` publishes `AstTypeDefinition` with `RuntimeType = typeof(IDictionary<string, object>)`.
3. `AstPropertyDefinition.EmitRead` / `EmitWrite` index that dictionary by property name (`DictionaryBackedValue`).
4. `DomainEntityInstance` **is** that dictionary (`DomainEntityInstance.Dictionary.cs`). `EnsureUnique` / `Notify` are listed on the type def like methods the VM can `Invoke`.

So the sim subject is already a dictionary-backed AST type. The bug is not “Interpretation cannot hold an entity.” The bug is DomainModeling still walking Effect IR / Domain beside that subject.

---

## Slices (one failing TUnit check each)

Copy unique: tests grow more specific; production gets more generic. CORE + `docs/interpretation/domain-execution-model.md` update in the same change as the mechanism.

| # | Slice | Smallest coherent path |
|---|--------|-------------------------|
| **1** | `Store.Create` / `Store.CreateIn` | Public Store jobs. Notify-shaped on the instance. Lowering reads `StorageMappingMetadata` (nav / FK / table) and falls back to relationship facts when the bag is absent. Runtime tree invokes `Create` / `CreateIn`, not `CreateChildInstance`. C# may still print `Stay.Create` / `CreateNav` (persistence surface until EF Store) — same split as unique indexes. |
| **2** | One tree | Always `LowerActionBody`. Delete `ExecuteStructured`, `RequiresDirectExecution`, `HasEffectDependentConditionalCreate` as runtime gates. Move fail-before-mutate of illegal create into Store / probe prefix in the tree — do not keep a bag eval before the program. |
| **3** | Unify factories | Delete `CreateByType` / `CreateInNav` / `ProbeCreateByType` as shipped instance factories. One Store job family. Do not add a third create shape. `LowerStageTransitions` remaining use is C# print vs runtime bind — not two interpreters. |
| **4** | Store reads | `Rel exists` / quantifiers / path-prefix lower to Store reads in the tree (Notify-shaped). Execute-time `PreprocessQuantifiers` → literals is Domain walk; if a rewrite to literals is kept, it belongs in **lowering**, and it is wrong when the same action creates then queries — prefer Store reads. Same for `EvaluatePolicy`. |
| **5** | MCP harness | `create_instance` allocates the dict instance and binds the session Store. `invoke_action` / `evaluate_policy` run the cached lowered program. No Expando. No private evaluator. |
| **G** | Gate | No Effect-IR walk at simulate. pr1. Suite green. Docs match. |

---

## Out of scope (do not pull in)

- EF Store / DbContext as the bound implementation (PULL: EF codegen). Scratch `DomainInstanceStore` is the first Store.
- Mut-safety, dict-sqlite, Grammar wrap-up, V3 naming, pack-2.
- Inventing `Main`. Growing `Comment`. A second interpreter.
- Replacing `DomainEntityInstance` with a new dynamic type.

---

## Done

- [x] Runtime create / create-in (including inside `if`, relationship-coupled, unique-on-create) compile as one operation AST and run through `Interpreter`.
- [x] `ExecuteStructured` / `ExecuteEffect` walker gone.
- [x] No `CreateByType` / `CreateInNav` / `ProbeCreateByType` as shipped meaning.
- [x] Store-aware expressions in actions and named policies are Store calls in the tree (or documented lowering-time rewrite with a cited reason — not execute-time preprocess).
- [x] MCP simulate = bind + Interpreter. Tool descriptions match.
- [x] CORE, ADR consequences, `domain-execution-model.md` match the code.
- [x] `PIPELINE-STATUS` marked DONE in the same change as the gate.
