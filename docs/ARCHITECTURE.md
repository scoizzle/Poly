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
│  Compiles analyzed AST directly to VmProgram (no         │
│  intermediate IR). Honors NodeReplacementMetadata for    │
│  constant folding, desugaring, and other rewrites.       │
│                                                          │
│  Output: VmProgram (function table + bytecode + consts)   │
└──────────────────────┬───────────────────────────────────┘
                       │ VmProgram
                       ▼
┌──────────────────────────────────────────────────────────┐
│                     VM Execution                          │
│                                                          │
│  VmProgram.EnsureCompiled() → compiled delegate via      │
│  ring-allocated stack (long[], ArrayPool<long>),         │
│  PC-based dispatch, breakpoint support, µop-level trace. │
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
| `NodeId.cs` | Stable `readonly record struct NodeId` — created from source position, GUID, or hash. Enables incremental analysis |
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

The `DirectVmAbiEmitter` compiles analyzed AST directly into a `VmProgram` — there is no intermediate primitive IR or separate µop-generation pass. This is the canonical compilation path for all AST-backed programs (domain policies, standalone expressions, interpreted scripts).

### Compilation Flow

`DirectVmAbiEmitter.CompileNode(Node node, AnalysisResult analysis)`:

1. **Node dispatch**: each node type has a dedicated compile method (`CompileConstant`, `CompileAdd`, `CompileInvoke`, etc.) that emits `VmInstruction` records into the growing program buffer.
2. **Replacement awareness**: before dispatching, checks `analysis.GetNodeReplacement(node)` — constant folding and other rewrite passes substitute nodes transparently.
3. **Ring allocation**: the emitter performs live analysis of the eval-stack to assign ring slots, eliminating stack-overflow checks at runtime.
4. **Branch resolution**: forward jumps (`Conditional`, `WhileLoop`, etc.) use deferred label targets that are resolved to absolute PCs after all nodes are processed.
5. **Output**: a `VmProgram` containing a `FunctionEntry[]` table (one per top-level lambda/block), `VmInstruction[]` bytecode, and a constant pool (`Constant[]`).

### What the Emitter Consumes from Analysis

| Metadata | Source Pass | Used For |
|---|---|---|
| `TypeResolutionMetadata` | TypeResolver | Member type checks, array detection |
| `MemberResolutionMetadata` | MemberResolver | Resolve call targets (`CallExternalOp`) |
| `ConstantValueMetadata` | ConstantFoldingPass | Inline constant immediates |
| `VariableAnalysisMetadata` | ScopeValidator | Alias eligibility, scope size |
| `DefiniteAssignmentMetadata` | DefiniteAssignmentAnalyzer | Skip zero-init for definitely-assigned locals |
| `NodeReplacementMetadata` | Any pass | Substitute folded/desugared nodes |

### Domain Expression Lowering

**Location:** `Poly/DomainModeling/Lowering/DomainExpressionLoweringPass.cs`

Before the emitter runs, domain-level expressions (policies, rules, conditions) must be lowered to AST nodes. This pass expands `DomainExpression` trees — `PropertyAccess`, `RelationshipNavigation`, `And`, `Or`, `Exists`, `Literal`, arithmetic, date ops — into `Poly.Ast` node records. The AST is then analyzed and compiled via the standard path above.

```
DomainExpression ──→ Ast nodes ──→ Analysis ──→ DirectVmAbiEmitter ──→ VM
```

Policy compilation/eval (`DomainEntityInstance.EvaluatePolicy` → `DomainExpressionLoweringPass` → `Interpreter`) uses this path, with the VM as the primary execution engine and LINQ expressions as a dual-oracle reference (`PolicyEvaluator` is the test-only CLR-subject wrapper in `Poly.Tests/TestHelpers/`).

---

## 4. VM Program & Bytecode

**Location:** `Poly/Interpretation/Vm/VmProgram.cs`

`VmProgram` is the compiled output — a sealed record containing:

| Field | Purpose |
|---|---|
| `FunctionEntry[] Functions` | One entry per compiled function/lambda; holds function index, start PC, arity, local count |
| `VmInstruction[] Instructions` | Flat bytecode array; each instruction has an opcode, an optional immediate operand, and a `NodeId? Source` for traceability |
| `Constant[] Constants` | Constant pool (longs, doubles, strings, heap object references) |
| `HashSet<int> BreakpointPCs` | Active breakpoint PCs (populated by the debugger) |

The instruction set is a compact stack-machine encoding with ~60 opcodes covering arithmetic, comparison, control flow (conditional/unconditional jumps), call/return, closure operations, heap access, and exception handling. Each instruction is 4–12 bytes.

### Compilation to Delegate

`VmProgram.EnsureCompiled()` builds the delegate lazily:

1. Allocate `VmProgramRuntime` — an `Action<VmState>` delegate.
2. Build a dispatch loop using `Expression` trees that reads the next instruction, switches on opcode, and emits `Expression` nodes for each case.
3. The dispatch uses a local `pc` variable and a `switch` inside a `while (pc < codeLen)` loop.
4. Compile to a `Func<VmState, ValueStack, ..., int>` and wrap as `Action<VmState>`.
5. On first call, the delegate runs the dispatch loop; subsequent calls reuse the compiled delegate.

---

## 5. VM Execution

**Location:** `Poly/Interpretation/Vm/`

### Execution Flow

```
VmProgram.Execute(state)
  ├─ EnsureCompiled() → compiled delegate (lazy)
  ├─ Invoke delegate(VmState)
  │    └─ Dispatch loop: while(pc < codeLen) switch(instruction[pc])
  ├─ Handle suspension (restore PC on debugger break)
  └─ Extract result from stack
```

### Key Files

| File | Purpose |
|---|---|
| `DirectVmAbiEmitter.cs` | Compiles analyzed AST → `VmProgram` |
| `VmProgram.cs` | Sealed record holding bytecode, functions, constants |
| `VmState.cs` | Execution state: PC, FrameBase, stack, heap, debug flags, trace writer |
| `ValueStack.cs` | `long[]` backed by `ArrayPool<long>.Shared` — zero-GC push/pop |
| `Heap.cs` | `List<object?>` with free-list — GC-free object storage |
| `Closure.cs` | Function index + captures array |
| `VmDebugger.cs` | PC-level debugger: breakpoints, stepping, `VmState.DebugInterrupt` |
| `VmTrace.cs` | µop-level trace output, gated by trace flag (~1 ns when inactive) |
| `VmValueMarshaller.cs` | Converts between VM `long[]` slots and CLR types for external calls |

### Runtime State Model

| Component | Purpose |
|---|---|
| `VmState` | PC, FrameBase, stack pointer, heap reference, debug state, active trace writer |
| `ValueStack` | `long[]` slots with ring allocation — single array shared across call frames |
| `Heap` | Flat `List<object?>` — values stored by index; free-list recycles dead entries |
| `FunctionEntry` | Per-function metadata: start PC, arity, local count, name |
| `Closure` | Function index + captured variable values |
| `Ref<T>` | Heap-allocated reference cell for mutable captures |

The VM is the **canonical semantics** — all backends (C# code gen, LINQ expressions) should produce behavior equivalent to the VM execution. Debugging and tracing always reference domain-level concepts via `NodeId? Source` on instructions.

---

## 6. Metadata Flow

### What Gets Produced Where

| Metadata Type | Produced By | Stored On | Consumed By |
|---|---|---|---|---|
| `TypeResolutionMetadata` | TypeResolver | Each node | MemberResolver, emitter, code gen |
| `MemberResolutionMetadata` | MemberResolver | Invoke/Member/New nodes | Emitter (call targets) |
| `VariableAnalysisMetadata` | ScopeValidator | Root node (via `context.SetMetadata`) | Emitter (alias, scope, captures) |
| `ConstantValueMetadata` | ConstantFoldingPass | Folded nodes | Emitter (constant immediates) |
| `DefiniteAssignmentMetadata` | DefiniteAssignmentAnalyzer | Lambda body nodes | Emitter (zero-init skip) |
| `SideEffectMetadata` | SideEffectAnalyzer | Each node | LinqExpressionGenerator (DCE) |
| `ControlFlowMetadata` | ControlFlowAnalysisPass | Control-flow nodes | Dead code diagnostics |
| `StackDepthMetadata` | StackDepthAnalyzer | Each node | Ring allocation, optimization |
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
DirectVmAbiEmitter ──► VmProgram (bytecode + functions + constants)
  │
  ├── CompileNode dispatches by node type
  ├── Honors NodeReplacementMetadata before dispatch
  ├── Consumes VariableAnalysisMetadata for alias eligibility
  └── Consumes DefiniteAssignmentMetadata for zero-init optimization
```

---

## 7. Execution Backends

### VM Backend (primary)

The primary execution engine. Analyzed AST is compiled by `DirectVmAbiEmitter` into a `VmProgram` with a flat bytecode array. `VmProgram.EnsureCompiled()` builds an `Action<VmState>` delegate via Expression trees. Optimized for speed:

- Direct `long[]` stack via `ArrayPool<long>.Shared`
- Compiled dispatch loop with no interpretive overhead
- Instruction-level tracing gated at ~1 ns when inactive
- Breakpoints via `HashSet<int>` PC check gated by `DebugMode` flag

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

2. **`NodeId` provides stable identity** — enables incremental analysis by reusing metadata across parses.

3. **The VM is the canonical semantics** — the compiled µop delegate defines the authoritative meaning of a program. All other backends should produce equivalent behavior.

4. **Metadata flows unidirectionally** — Analysis produces metadata, lowering consumes it. Lowering does not run its own analysis. The `ScopeValidator` produces `VariableAnalysisMetadata` (assignment counts, escape info) that replaced lowering's previous hand-rolled `CollectEscapeInfo` pre-scan.

5. **Module boundaries are enforced**:
   - `Interpretation` → `Ast`, `Analysis`, `Introspection`
   - `DomainModeling` → `Ast` (pure lowering) + `Interpretation` (policy evaluation bridge)
   - `Analysis` → `Ast` (framework references nodes only)
   - `Introspection` ↛ `Interpretation`
   - `MCP` → `DomainModeling`, `Ast`, `Analysis` (thin adapter, no domain logic)

6. **Domain concepts lower to generic VM opcodes** — no domain-specific opcodes.

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
