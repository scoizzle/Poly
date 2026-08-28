# Poly Architecture

> **Status: current.** For the short platform map (purpose, boundaries, critical machinery), use **[`docs/CORE.md`](CORE.md)**. Module READMEs under `Poly/*/` and ADRs under `docs/decisions/` provide deeper detail. This document describes the high-level architecture of the Poly system — a neurosymbolic platform where domain models and policies are authored as structured data, lowered to a symbolic AST, analyzed, then executed by the VM.

## System Overview

```
DomainModeling ──[DomainExpression lowering]──┐
                                              │
                                              ▼
┌──────────────────────────────────────────────────────────┐
│               Ast (Symbolic IR)                            │
│  Node records, NodeId, fluent construction API           │
└──────────────────────┬───────────────────────────────────┘
                       │
                       ▼
┌──────────────────────────────────────────────────────────┐
│               Analysis Pipeline (INodeAnalyzer passes)    │
│                                                          │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌─────────────┐ │
│  │ Type     │→│ Member   │→│ Scope    │→│ Constant     │ │
│  │ Resolver │ │ Resolver │ │ Validator│ │ Folding      │ │
│  └──────────┘ └──────────┘ └──────────┘ └─────────────┘ │
│                                                          │
│  ┌──────────────┐ ┌──────────────┐ ┌──────────────────┐  │
│  │ Side Effect  │ │ Control Flow │ │ Definite Assign  │  │
│  │ Analysis     │ │ Analysis     │ │ Analysis         │  │
│  └──────────────┘ └──────────────┘ └──────────────────┘  │
│                                                          │
│  Produces: AnalysisResult (metadata + diagnostics)        │
└──────────────────────┬───────────────────────────────────┘
                       │ analyzed AST + metadata
                       ▼
┌──────────────────────────────────────────────────────────┐
│               DirectVmAbiEmitter                           │
│                                                          │
│  Compiles analyzed AST to Expression trees targeting     │
│  VmState / ring / heap (no primitive IR). Honors         │
│  NodeReplacementMetadata before dispatch.                │
│                                                          │
│  Output: VmProgram (Action<VmState> Delegate)            │
└──────────────────────┬───────────────────────────────────┘
                       │ VmProgram
                       ▼
┌──────────────────────────────────────────────────────────┐
│                     VM Execution                          │
│                                                          │
│  Interpreter.Execute → program.Delegate(VmState).        │
│  Ring registers + heap. DebugHook / CompilationMode.     │
│                                                          │
│  Canonical semantics — all backends match this behavior. │
└──────────────────────────────────────────────────────────┘
```

---

## 1. Syntax — AST Layer

**Location:** `Poly/Ast/`

The AST is a forest of immutable C# record types. Nodes carry no semantic information — they are pure data. All semantic resolution is the job of analysis passes.

### Key Files

| File | Content |
|---|---|
| `Node.cs` | Base `abstract record Node` with `NodeId Id`, `Children` enumeration |
| `NodeId.cs` | Stable `readonly record struct NodeId` — created from source position, GUID, or hash. Metadata key |
| `NodeExtensions.cs` | Fluent construction API: `left.Add(right)`, `cond.Conditional(a,b)`, `x.Assign(value)` |
| `Nodes/Operator.cs` | `abstract record Operator : Node` — marker for value-producing operations |
| `Nodes/Constant.cs`, `Variable.cs`, `Parameter.cs` | Leaf value/reference nodes |
| `Nodes/Block.cs` | Sequence block with `Nodes` (body) and `Variables` (declarations) |
| `Nodes/Invoke.cs` | Invocation `fn(args)` |
| `Nodes/Lambda.cs` | Lambda `(params) => body` |
| `Nodes/Return.cs` | `return value` |

### Expression Node Types

All in `Poly/Ast/Nodes/`:

**Arithmetic:** `Add`, `Subtract`, `Multiply`, `Divide`, `Modulo`, `UnaryMinus`

**Comparison:** `Equal`, `NotEqual`, `LessThan`, `LessThanOrEqual`, `GreaterThan`, `GreaterThanOrEqual`

**Logical:** `And` (&&), `Or` (||), `Not` (!)

**Bitwise:** `BitwiseAnd`, `BitwiseOr`, `BitwiseXor`, `BitwiseNot`, `ShiftLeft`, `ShiftRight`

**Access:** `Member` (dot-access), `IndexAccess` (indexer), `Invoke` (call)

**Type:** `TypeCast`, `TypeIs`, `TypeAs`

**Control:** `Conditional` (?:), `Coalesce` (??), `Assignment`, `Await`

### Statement Node Types

**Loops:** `WhileLoop`, `DoWhileLoop`, `ForLoop`, `ForEachLoop`

**Branches:** `IfStatement`, `SwitchStatement`

**Exceptions:** `ThrowStatement`, `TryCatchFinally`

**Jumps:** `BreakStatement`, `ContinueStatement`, `GotoStatement`, `LabelDeclaration`, `Return`

**Other:** `UsingStatement`

### Type Reference Nodes

All in `Poly/Ast/Nodes/` (some in `TypeDefinitions/` subdirectory):

| Node | Purpose |
|---|---|
| `TypeReference(string TypeName)` | Base type reference by name string |
| `TypeDefinitionReference(ITypeDefinition)` | Already-resolved type definition |
| `ClrTypeReference(Type RuntimeType)` | Direct CLR System.Type reference |
| `PrimitiveTypeReference(PrimitiveType)` | Primitive type by enum |
| `NamedTypeReference(string, string?)` | Named type with optional namespace + generics |
| `CollectionTypeReference(ElementType, CollectionKind)` | Collection type (Array/List/Set) |
| `OptionalTypeReference(InnerType)` | Nullable/optional wrapper |
| `UnionTypeReference(Options[])` | Union/sum type |
| `MapTypeReference(KeyType, ValueType)` | Map/dictionary type |

### Type Definition Nodes

All in `Poly/Ast/Nodes/TypeDefinitions/`:

| Node | Purpose |
|---|---|
| `TypeDefinitionNode` | Full type definition with name, namespace, constructors, properties, methods, fields |
| `MethodDefinitionNode` | Function definition |
| `PropertyDefinitionNode` | Property with getter/setter/initializer |
| `FieldDefinitionNode` | Field with type, default value |
| `ConstructorDefinitionNode` | Constructor with parameters |
| `Parameter` | Formal parameter shared by methods, lambdas, type definitions |

---

## 2. Analysis — Semantic Pipeline

**Location:** `Poly/Interpretation/Analysis/`, `Poly/Analysis/`

### Framework (`Poly/Analysis/`)

| File | Purpose |
|---|---|
| `AnalyzerBuilder.cs` | Fluent builder: `AddAnalyzer(INodeAnalyzer)`, `Build()` |
| `Analyzer.cs` | Runs ordered passes: `Analyze(Node root)` → `AnalysisResult` |
| `AnalysisContext.cs` | Per-run session state: metadata storage, diagnostics, type resolution |
| `AnalysisResult.cs` | Output: `GetMetadata<T>(Node)`, `Diagnostics`, `HasErrors` |
| `INodeAnalyzer.cs` | Pass contract: `Analyze(AnalysisContext, Node)` + `AnalyzeChildren` helper |
| `INodeMetadataProvider.cs` | Metadata query interface: `GetMetadata<T>(Node?)` |
| `IAnalysisMetadata.cs` | Empty marker for all metadata types |
| `NodeMetadataStore.cs` | Two-level store (NodeId → bucket with inline array → Dictionary) |
| `NodeReplacementMetadata.cs` | Node substitution for backends |
| `AnalysisOptions.cs` | Pipeline behavior: `Full`, `StopOnStructuralErrors`, `FailFast` |

### Typical Pipeline Order

```csharp
new AnalyzerBuilder()
    .UseTypeAndMemberResolver()     // resolve types and members in one pass
    .UseVariableScopeValidator()    // validate scopes, produce VariableAnalysisMetadata
    .UseThisReferenceContext()      // validate `this` usage
    .UseControlFlowAnalysis()       // build CFG, detect dead code
    .UseConstantFolding()           // fold constant subexpressions
    .UseSideEffectAnalysis()        // classify side effects, enable DCE
    .UseDefiniteAssignmentAnalysis()// track definitely-assigned variables
    .Build();
```

### Analysis Passes (`Poly/Interpretation/Analysis/`)

#### Type & Member Resolution — `Semantics/TypeAndMemberResolutionPass.cs`
- Resolves both CLR `ITypeDefinition` and `ITypeMember` for every AST node in a single pass
- Merged from former separate TypeResolver + MemberResolver passes to eliminate duplicate tree walk
- Stores `TypeResolutionMetadata` (`GetResolvedType`) and `MemberResolutionMetadata` (`GetResolvedMember`)
- Registration: `UseTypeAndMemberResolver()`

#### Variable Scope & Alias Analysis — `Semantics/VariableLifetimePass.cs`
- Fully stateless pass — per-analysis `ScopeState` allocated on entry, threaded through recursive calls, discarded on return
- Validates scopes, detects undeclared variables and shadowing
- Produces `VariableAnalysisMetadata` (stored on root node via `context.SetMetadata`):
  - `VariableReferences` — use-site → declaration-site mapping
  - `BlockScopes` — per-block variable sets
  - `ScopeVertices` — scope hierarchy tree (parent links)
  - `VariableDeclarationScope` — each variable → its declaring node
  - `AssignmentCount` — per-declaration assignment count
  - `EscapedVariables` — variables passed to non-lambda calls, returned, or used in foreach
- Registration: `UseVariableScopeValidator()`

#### Constant Folding — `ConstantFolding/ConstantFoldingPass.cs`
- Evaluates constant subexpressions at analysis time
- Algebraic simplifications (x+0→x, x*1→x, etc.)
- Stores `ConstantValueMetadata` with `GetConstantValue(Node)` extension
- Registration: `UseConstantFolding()`

#### Definite Assignment — `Semantics/DefiniteAssignmentAnalyzer.cs`
- Tracks which variables are definitely assigned per lambda body
- Flow-sensitive: intersection at if/else joins, reset at loops
- Produces `DefiniteAssignmentMetadata` on lambda body nodes
- Registration: `UseDefiniteAssignmentAnalysis()`

#### Side Effect Analysis — `Semantics/SideEffectAnalysisPass.cs`
- Classifies each node by side-effect kind: Pure, Read, Write, Allocate, External
- Enables dead code elimination for pure nodes with unused results
- Registration: `UseSideEffectAnalysis()`

#### Control Flow Analysis — `ControlFlow/ControlFlowAnalysisPass.cs`
- Builds a `ControlFlowGraph` from AST
- Dead code detection, infinite loop detection, reachability
- Registration: `UseControlFlowAnalysis()`

#### Other Passes
- `ThisReferenceContextPass.cs` — validates `this` usage in type definitions
- `LambdaReturnTypeAnalyzer.cs` — refines lambda return types
- `StackDepthAnalyzer.cs` — static stack push/pop effect computation
- `TypeDefinitionNodeAnalyzer.cs` — extracts `ITypeDefinition` from `TypeDefinitionNode`

---

## 3. Direct AST → VM ABI

**Location:** `Poly/Interpretation/Vm/DirectVmAbiEmitter.cs`

The `DirectVmAbiEmitter` compiles analyzed AST to Expression trees targeting `VmState` / ring / heap. There is no intermediate primitive flattening, no `VmInstruction` bytecode, and no `CallExternalOp`. This is the canonical compilation path for all AST-backed programs. Contract: [`docs/CORE.md`](CORE.md) §3.3.

### Compilation Flow

`DirectVmAbiEmitter.CompileNode` returns a LINQ `Expression` (not bytecode):

1. **Replacement awareness**: before dispatch, honors `analysis.GetNodeReplacement(node)` — folding and other rewrite passes substitute nodes in analysis, not in the emitter.
2. **Node dispatch**: each node type has a dedicated emit method (`EmitConstant`, `EmitInvoke`, `EmitForEachLoop`, …) that builds Expression nodes against ring locals and heap.
3. **Ring allocation**: ring slots (`_r0.._rN`) are assigned during the AST walk — no global `RingAllocator` pre-pass.
4. **Control flow**: loops, branches, and try/catch/finally use native CLR Expression nodes (`Loop`, `Condition`, `TryCatchFinally`), not PC-resolved jumps.
5. **Output**: `VmProgram` whose `Delegate` is `Action<VmState>`. `Interpreter.Execute` invokes that delegate.

Invoke: resolved CLR `MethodInfo` is `Expression.Call`; unresolved `Invoke(Member(This, name))` (domain actions) dispatches through `InvokeNamed`. `ForEachLoop` walks a heap `IList` (non-`IList` / null fails loud).

### What the Emitter Consumes from Analysis

| Metadata | Source Pass | Used For |
|---|---|---|
| `TypeResolutionMetadata` | TypeAndMemberResolutionPass | Member/array/ctor type checks |
| `MemberResolutionMetadata` | TypeAndMemberResolutionPass | CLR `MethodInfo` / property / ctor when present; otherwise `InvokeNamed` |
| `ValueRepresentationMetadata` | ValueRepresentationAnalyzer | Stack scalar vs heap ref (invoke args, root result) |
| `NodeReplacementMetadata` | Any pass | Substitute folded/desugared nodes |

Do not patch the ABI for one scenario — fix upstream (lower / analyze / replace). Known-member `MethodInfo` / `PropertyInfo` / `ConstructorInfo`: `Ref` / `Ref<T>` (`Poly/Interpretation/Vm/Ref.cs`).

### Domain Expression Lowering

**Location:** `Poly/DomainModeling/Lowering/DomainExpressionLoweringPass.cs`

Before the emitter runs, domain-level expressions (policies, rules, conditions) must be lowered to AST nodes. This pass expands `DomainExpression` trees — `PropertyAccess`, `RelationshipNavigation`, `And`, `Or`, `Exists`, `Literal`, arithmetic, date ops — into `Poly.Ast` node records. The AST is then analyzed and compiled via the standard path above.

```
DomainExpression ──→ Ast nodes ──→ Analysis ──→ DirectVmAbiEmitter ──→ VM
```

Policy compilation/eval (`DomainEntityInstance.EvaluatePolicy` → `DomainExpressionLoweringPass` → `Interpreter`) uses this path, with the VM as the primary execution engine and LINQ expressions as a dual-oracle reference (`PolicyEvaluator` is the test-only CLR-subject wrapper in `Poly.Tests/TestHelpers/`).

**StageTransition** is Assignment of `CurrentStage` plus `Invoke(Member(This, "Notify"))` — not a VM opcode. Full lowering contract: [`docs/CORE.md`](CORE.md) §3.4.

---

## 4. VM Program

**Location:** `Poly/Interpretation/Vm/VmProgram.cs`

Product path is **not** a `VmInstruction` bytecode interpreter and not a primitive-IR flatten. `DirectVmAbiEmitter.Emit` compiles the analyzed AST to Expression trees targeting `VmState` / ring / heap and returns a `VmProgram` whose `Delegate` is `Action<VmState>`. `Interpreter.Execute` runs that delegate; there is no `EnsureCompiled` opcode dispatch loop.

Mechanisms: [`docs/CORE.md`](CORE.md) §3.3. Runtime types: `Poly/Interpretation/Vm/README.md`.

---

## 5. VM Execution

**Location:** `Poly/Interpretation/Vm/`

### Execution Flow

```
Interpreter.Compile(node) → DirectVmAbiEmitter.Emit → VmProgram
Interpreter.Execute(program) → program.Delegate(VmState)
```

### Key Files

| File | Purpose |
|---|---|
| `DirectVmAbiEmitter.cs` | Compiles analyzed AST → Expression trees → `VmProgram` |
| `VmProgram.cs` | Compiled program: `Action<VmState> Delegate` (+ debug/function metadata) |
| `VmState.cs` | Execution state: stack, heap, registers, `DebugHook`, status |
| `ValueStack.cs` | `long[]` backed by `ArrayPool<long>.Shared` |
| `Heap.cs` | Object heap with free-list recycling |
| `Closure.cs` | Function index + captures array |
| `Ref.cs` | Compile-time `MemberInfo` lookups (`Ref.Method`, `Ref<T>.Property`, …) |
| `VmValueMarshaller.cs` | Converts between VM `long[]` slots and CLR types |

### Runtime State Model

| Component | Purpose |
|---|---|
| `VmState` | Stack, heap, registers, `DebugHook`, status |
| `ValueStack` | Pooled `long[]` slots |
| `Heap` | Object store by handle; free-list recycles dead entries |
| `Closure` | Function index + captured variable values |
| `Ref<T>` | Compile-time known-member `MemberInfo` (not a heap cell) |

The VM is the **canonical semantics** — all backends (C# code gen, LINQ expressions) should produce behavior equivalent to the VM execution. Debug uses `VmState.DebugHook` (AST node + frame locals), not `NodeId` on bytecode.

---

## 6. Metadata Flow

### What Gets Produced Where

| Metadata Type | Produced By | Stored On | Consumed By |
|---|---|---|---|---|
| `TypeResolutionMetadata` | TypeAndMemberResolutionPass | Each node | Emitter, code gen |
| `MemberResolutionMetadata` | TypeAndMemberResolutionPass | Invoke/Member/New nodes | Emitter (CLR members; else `InvokeNamed`) |
| `ValueRepresentationMetadata` | ValueRepresentationAnalyzer | Each node | Emitter (scalar vs heap) |
| `VariableAnalysisMetadata` | ScopeValidator | Root node (via `context.SetMetadata`) | Analysis / diagnostics |
| `ConstantValueMetadata` | ConstantFoldingPass | Folded nodes | Replacement + diagnostics |
| `DefiniteAssignmentMetadata` | DefiniteAssignmentAnalyzer | Lambda body nodes | Diagnostics |
| `SideEffectMetadata` | SideEffectAnalyzer | Each node | LinqExpressionGenerator (DCE) |
| `ControlFlowMetadata` | ControlFlowAnalysisPass | Control-flow nodes | Dead code diagnostics |
| `StackDepthMetadata` | StackDepthAnalyzer | Each node | Diagnostics (emitter assigns ring slots during the AST walk) |
| `NodeReplacementMetadata` | Any analysis pass | Replaced nodes | All backends |

### How Semantic Information Flows

```
AST Node (pure data record)
  │
  ▼
Analysis Pipeline ──► AnalysisResult
  │                     │
  │                     ├── GetMetadata<T>(node) → metadata
  │                     ├── GetResolvedType(node) → ITypeDefinition
  │                     ├── GetResolvedMember(node) → ITypeMember
  │                     ├── GetConstantValue(node) → object?
  │                     ├── GetNodeReplacement(node) → Node?
  │                     └── Diagnostics
  │
  ▼
DirectVmAbiEmitter ──► VmProgram (Action<VmState>)
  │
  ├── CompileNode returns Expression; honors NodeReplacementMetadata
  ├── Consumes Type/Member resolution + ValueRepresentationMetadata
  └── InvokeNamed for unresolved instance members; foreach is IList walk
```

---

## 7. Execution Backends

### VM Backend (primary)

The primary execution engine. Analyzed AST is compiled by `DirectVmAbiEmitter` into a `VmProgram` whose `Delegate` is `Action<VmState>` (Expression trees targeting ring/heap — no bytecode flatten). `Interpreter.Execute` invokes that delegate. Optimized for speed:

- Direct `long[]` stack via `ArrayPool<long>.Shared`
- Native CLR Expression control flow (no opcode dispatch loop)
- `DebugHook` omitted in `CompilationMode.NoDebug`

### LINQ Expressions Backend

**Location:** `Poly/Interpretation/LinqExpressions/LinqExpressionGenerator.cs`

Generates `System.Linq.Expressions.Expression` trees from analyzed AST. Supports all major node types. Used for:
- Compiling policies and rules to executable delegates
- Situations where standard .NET expression trees are needed

### C# Code Generation

**Location:** `Poly/Interpretation/CSharp/CSharpGenerator.cs`

Generates C# source code as `string`. Supports:
- Type definitions (classes, records, enums, interfaces)
- Methods, constructors, properties, fields
- Statements and expressions

---

## 9. Introspection — Type System Abstraction

**Location:** `Poly/Introspection/`

A provider-based type abstraction decoupled from CLR reflection. Consumers work against interfaces, allowing composition of multiple type sources.

### Core Interfaces

| Interface | Purpose |
|---|---|
| `ITypeDefinition` | A type with introspectable members |
| `ITypeDefinitionProvider` | Resolves types by name, CLR Type, or PrimitiveType |
| `ITypeMember` | Base for all type members |
| `ITypeMethod` | A method with parameters |
| `ITypeConstructor` | A constructor |
| `ITypeProperty` | A property with read/write/init delegates |
| `ITypeField` | A field with read/write delegates |
| `IParameter` | A parameter with name, type, optional default |

### CLR Backing (`CommonLanguageRuntime/`)

| File | Purpose |
|---|---|
| `ClrTypeDefinitionRegistry.cs` | Thread-safe singleton: resolves `System.Type` → `ClrTypeDefinition` |
| `ClrTypeDefinition.cs` | CLR-backed `ITypeDefinition` using `System.Reflection` |
| `ClrMethod.cs`, `ClrConstructor.cs` | Reflection-backed method/constructor wrappers |
| `ClrTypeProperty.cs`, `ClrTypeField.cs` | Property/field with expression-tree delegates for fast access |
| `ClrTypeSyntheticProperty.cs` | Synthetic property for array indexers |
| `IClrType.cs` | Extension interface exposing `RuntimeType` |

---

## 10. Domain Modeling

**Location:** `Poly/DomainModeling/`

The domain model is an **immutable record graph** — the primary model for analysis, evolution, and lowering to the AST. All mutation happens through the analysis-gated evolution pipeline (`DomainEvolution` + `Apply`), which returns a new `Domain` root on success or rolls back with diagnostics on failure.

### Key Types

- `Domain` — root container; entities, relationships, policies, types
- `Entity` — named entity with properties, stages, actions
- `Stage` — lifecycle stage within an entity
- `Action` — named action with optional require gates, effects
- `Effect` — effect types: create, transition, assign, conditional, composite, link/unlink, invoke-action
- `Policy` — named policy with a `DomainExpression` condition
- `Relationship` — named relationship connecting two entities
- `Property` — typed property with constraints
- `DomainExpression` — unified expression model for policies, rules, and guards
- `Constraint` — validation constraint (Range, Length, Pattern, Required, Unique)

### Builders

`DomainBuilder` → `EntityBuilder` → `StageBuilder` / `ActionBuilder` / ... → `Build()`

### Expression System

`DomainExpression` is a unified expression type covering: `PropertyAccess`, `ParameterAccess`, `Literal` (all primitive types), `And`, `Or`, `Not`, `RelationshipNavigation`, `Exists`, comparison/arithmetic/date operations, and collection queries.

### Sub-modules

| Directory | Purpose | Lines |
|-----------|---------|-------|
| `Analysis/` | Domain-model analyzers (entity structure, effects, topology, storage, transport, ownership, subscriptions, constraints) | ~5,260 |
| `Lowering/` | DomainExpression→AST lowering, C# code export, policy evaluation via VM | ~2,830 |
| `Parsing/` | Poly DSL tokenizer, parser, and printer (`.poly` text format) | ~2,340 |
| `Evolution/` | `DomainEvolution`, `DomainChange`, `EvolutionTransaction` — atomic changes with analysis gate + rollback | ~1,970 |
| `Bootstrap/` | `DomainFactory`, `CanonicalBuiltInTypeCatalog` — domain creation with built-in types | ~360 |
| `Queries/` | `DomainQueries` — model-optimized query projections | ~290 |
| `Builders/` | Fluent construction API (alternative to DSL/evolution) | ~580 |
| `Effects/` | Effect type records (Create, Transition, Assign, Conditional, Composite, Link, etc.) | ~140 |
| `Constraints/` | Constraint type records (Range, Length, Pattern, Required, Unique, etc.) | ~30 |

**Note:** The V2 mutable subsystem (`Poly/Data/Modeling/`) has been **deleted**. Only the immutable V3 core remains.

---

## 11. Key Design Principles

1. **Nodes are pure data records** — no semantics, no type info. All semantic resolution is the job of analysis passes (`INodeAnalyzer`).

2. **`NodeId` provides stable identity** — metadata is keyed by node id.

3. **The VM is the canonical semantics** — the compiled `Action<VmState>` delegate defines the authoritative meaning of a program. All other backends should produce equivalent behavior.

4. **Metadata flows unidirectionally** — Analysis produces metadata, lowering consumes it. Lowering does not run its own analysis. The `ScopeValidator` produces `VariableAnalysisMetadata` (assignment counts, escape info) that replaced lowering's previous hand-rolled `CollectEscapeInfo` pre-scan.

5. **Module boundaries are enforced**:
   - `Interpretation` → `Ast`, `Analysis`, `Introspection`
   - `DomainModeling` → `Ast` (pure lowering) + `Interpretation` (policy evaluation bridge)
   - `Analysis` → `Ast` (framework references nodes only)
   - `Introspection` ↛ `Interpretation`
   - `MCP` → `DomainModeling`, `Ast`, `Analysis` (thin adapter, no domain logic)

6. **Domain concepts lower to generic Syntax nodes** — no domain-specific VM opcodes. StageTransition is Assignment + `Invoke Notify`; invoke is `InvokeNamed`; foreach is an `IList` walk.

7. **Compile-time-safe member references** — `MemberHelper.MethodOf(() => ...)` and `MemberHelper.PropertyOf(() => ...)` create IL-level metadata references that survive dead-member analysis, replacing string-based `typeof(T).GetMethod("Name")` patterns.

---

## File Index

### Ast (Symbolic IR)
- Base: `Poly/Ast/Node.cs`, `NodeId.cs`, `NodeExtensions.cs`
- Nodes: `Poly/Ast/Nodes/` (all expression and statement types)
- Type defs: `Poly/Ast/Nodes/TypeDefinitions/`

### Analysis Framework
- `Poly/Analysis/` — Analyzer, AnalysisContext, metadata store, node replacement, diagnostics

### Interpretation
- Analysis passes: `Poly/Interpretation/Analysis/` (semantics, CFG, constant folding)
- VM/ABI: `Poly/Interpretation/Vm/` (DirectVmAbiEmitter, VmProgram, VmState, runtime)
- LINQ backend: `Poly/Interpretation/LinqExpressions/`
- C# backend: `Poly/Interpretation/CSharp/`
- Mermaid visualization: `Poly/Interpretation/Mermaid/`

### Introspection
- Interfaces: `Poly/Introspection/` (ITypeDefinition, ITypeMember, etc.)
- CLR backing: `Poly/Introspection/CommonLanguageRuntime/`

### Domain Modeling
- Core: `Poly/DomainModeling/` (Domain, Entity, Action, Effect, Policy, Relationship, etc.)
- Sub-modules: `Analysis/`, `Builders/`, `Bootstrap/`, `Constraints/`, `Effects/`, `Evolution/`, `Lowering/`, `Parsing/`, `Queries/`
