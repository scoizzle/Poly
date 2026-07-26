# Poly Architecture

> **Status: historical / may be stale.** For the current short platform map (purpose, boundaries, critical support including node replacement and direct AST→VM), use **[`docs/CORE.md`](CORE.md)**. Prefer module READMEs under `Poly/*/` and ADRs under `docs/decisions/` over diagrams in this file when they conflict. Sections below that describe an intermediate **primitive IR** / separate µop-compiler path are **superseded** by direct AST→VM-ABI (`DirectVmAbiEmitter`).

This document describes the architecture of the Poly system — a neurosymbolic platform where models codify algorithms and heuristics as composable macros in a symbolic IR, validated by the VM (canonical semantics), and compiled to native backends.

## System Overview

```
┌──────────────────────────────────────────────────────────┐
│                     Syntax (AST Layer)                    │
│  Node records, NodeId, fluent construction API           │
└──────────────┬───────────────────────────────────────────┘
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
└──────────────┬───────────────────────────────────────────┘
               │
               ▼
┌──────────────────────────────────────────────────────────┐
│              Canonical Execution IR (Primitives) │
│                                                          │
│  PrimitiveNode instruction set (enhanced with ValueSlot, │
│  explicit dataflow, Phi, etc.). The canonical IR for the │
│  VM execution engine. AST remains the primary symbolic   │
│  / model-facing / serializable form.                     │
│                                                          │
│  Lowering (ToPrimitives) is the point to expand metadata │
│  rather than discard structure.                          │
│                                                          │
│  Produces: primitives + expanded metadata for execution  │
└──────────────┬───────────────────────────────────────────┘
               │
               ▼
┌──────────────────────────────────────────────────────────┐
│                   Lowering (IR → µops)                   │
│                                                          │
│  UopCompiler walks IR blocks, emits flat µop      │
│  list. Resolves CondBranch/Goto targets to absolute PCs, │
│  maps Phi → PhiMarker with ConsumedFromPcs.              │
│                                                          │
│  Consumes: ModuleMetadata (blocks, values, terminators)   │
└──────────────┬───────────────────────────────────────────┘
               │
               ▼
┌──────────────────────────────────────────────────────────┐
│              ProgramCompiler (µops → delegate)            │
│                                                          │
│  Compiles flat µop list into Action<VmState> delegate     │
│  using LINQ Expression trees. Uses CompilationContext     │
│  for stack management, tracing, breakpoint support.      │
└──────────────┬───────────────────────────────────────────┘
               │
               ▼
┌──────────────────────────────────────────────────────────┐
│                     VM Execution                          │
│                                                          │
│  Vm.Execute(state) runs the compiled delegate, handles   │
│  call/return, closures, exceptions, external calls,      │
│  debugging, and µop-level tracing.                       │
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

## 3. Canonical IR — The Neurosymbolic Pivot

**Location:** `Poly/Ir/`

The Canonical IR is the structural layer between AST/domain-level expression and all execution backends. It is the **pivot** of Poly's neurosymbolic architecture: models express intent at the domain level, the compiler lowers it deterministically through IR, the VM validates it, and every backend projects the same IR into its target form.

### The Three Levels of Expression

Poly provides three levels at which code can be expressed, each with a distinct role:

| Level | Module | Role | Who authors it |
|-------|--------|------|---------------|
| **Domain** | `DomainModeling` | Entities, actions, stages, policies — the model's primary surface | Model (default), User |
| **IR** | `Ir` | Blocks, instructions, phi nodes — semantically complete, execution-model-agnostic | Compiler (lowering), Model (escape hatch) |
| **µops** | `Interpretation/Vm` | Stack-machine instructions with PC offsets, ring allocation | Compiler only (never model-authored) |

The lowering pipeline is **deterministic and traceable** at every step:

```
Domain Modeling ──→ AST ──→ IR ──→ µops ──→ delegate
     ↑                ↑       ↑        ↑
  model authors   lowering  model     compiler
  here (default)  pass      can       only
                            inspect
                            or inject
```

### Why the Model Defaults to the Domain Level

Forcing models to work at the domain level — with strong analysis and diagnostics — is more effective than allowing them to operate at the IR or µop level by default:

- **Domain errors are meaningful.** "`Order.Confirm` requires `Order` to be in `Pending` stage" is actionable. "Phi at block_3 has mismatched incoming values" is a compiler artifact the model shouldn't need to reason about.
- **Deterministic lowering means the model doesn't author execution concerns.** Ring allocation, PC offsets, `ConsumedFromPcs` arrays, and `PhiMarker` placement are the compiler's job. The model describes *what* should happen; the compiler determines *how* the stack machine executes it.
- **Iteration happens at the right level.** If a model's domain description produces incorrect behavior, the model inspects the lowered IR (not the µops) to understand *why* — and fixes the domain description, not the µop listing.

### When the Model Drops to IR (the Escape Hatch)

For performance-critical paths, custom data structures, or algorithms that don't map neatly to domain constructs, the model can inject IR directly:

```
Domain Modeling ──→ AST ──→ IR ──→ µops
                              ↑
                     model injects IR here
                     (opt-in, not default)
```

The IR is the right escape hatch because it is the **lowest level that is still semantically complete** — every IR `Module` has a deterministic execution result — but the model is not burdened with execution-model concerns like ring allocation or PC offsets.

### Projection, Not Generation

Once the IR is verified by the VM, every backend is a deterministic projection. The model (or user) can request:

- **C# source** (`CSharpCodeGenerator`) — idiomatic code for human review
- **µop listing** (`UopCompiler`) — stack-machine trace for debugging
- **CFG visualization** — Mermaid diagram with blocks, branches, and phi nodes
- **Domain-level trace** — "`Order.Confirm` merge point: result is `confirmed_total` (from authorization branch) or `rejected_total` (from rejection branch)"

Each projection is derived from the same canonical IR. The model never generates per-language code — it generates IR, and the compiler handles everything downstream.

### Traceability as the Foundation

Every `Instr` carries a `NodeId? Source` linking back to the AST node (and thus the domain construct) that produced it. This means:

- **VM traces reference domain concepts**, not µop PCs. "At `Order.Confirm` merge point, `Phi` selects `confirmed_total`" instead of "pc12: PhiMarker([pc6], pc7)."
- **The model can inspect lowering decisions.** "This `CondBranch` at block_2 corresponds to your `if (payment.Authorized)` check."
- **Errors localize to the source.** A VM exception at a given µop PC can be traced back through IR → AST → the exact domain construct the model authored.

### IR Design Summary

The IR is a **block-structured CFG with SSA values**:

- **16 instruction types**: `Const`, `BinOp`, `UnaryOp`, `LoadLocal`, `StoreLocal`, `Parameter`, `Call`, `AllocClosure`, `LoadUpvalue`, `StoreUpvalue`, `AllocHeap`, `Phi`
- **5 terminator types**: `Goto`, `CondBranch`, `Ret`, `Throw`
- **4 type kinds**: `Word`, `Boolean`, `Handle`, `Void`
- **Explicit φ nodes** replace the ring-based heuristic in `Lowering.Assemble()`
- **SSA optional**: the VM backend can work with or without SSA; optimization passes enable it as needed

Each AST node lowers to IR via a `virtual Value? Emit(EmissionContext ctx)` method on the `Node` base class, replacing the monolithic 700-line `UopGenerationPass` switch.

For the full IR design, see `docs/experiments/interpretation-compiler-framework-plan.md`.

---

## 4. Lowering — IR → µops

**Location:** `Poly/Interpretation/Ir/Backends/UopCompiler.cs`

### Architecture

The `UopCompiler` walks IR blocks (not AST nodes) and emits a flat list of `MicroOp` records. It replaces the combined `UopGenerationPass` + `Lowering.Assemble()` pipeline:

1. **Block ordering**: topologically sort blocks (dominator-tree DFS). Assign contiguous PC ranges per block.
2. **Instruction emission**: for each `Instr`, emit the corresponding µop (`Const` → `LoadConst`, `BinOp` → `BinOp`, `Phi` → `PhiMarker` + resolved `ConsumedFromPcs`, etc.)
3. **Ring analysis**: `RingAnalyzer` computes eval-stack ring depths from the IR's explicit `Phi` nodes — simpler than the current heuristic because φ is already explicit.
4. **Label resolution**: `Goto.Target` and `CondBranch.ThenTarget`/`ElseTarget` are `BasicBlock` references (not integer IDs) — they cannot dangle. Resolved to absolute PCs during emission.

### What Lowering Consumes from Analysis

| Metadata | Source Pass | Used For |
|---|---|---|
| `VariableAnalysisMetadata` | ScopeValidator | `IsAliasEligible` check, scope traversal for captures/locals |
| `DefiniteAssignmentMetadata` | DefiniteAssignmentAnalyzer | Skip zero-init for definitely-assigned lambda locals |
| `ConstantValueMetadata` | ConstantFoldingPass | `TryGetConstantLong` for immediate-bearing µops |
| `TypeResolutionMetadata` | TypeResolver | Array type detection, member type checks |
| `MemberResolutionMetadata` | MemberResolver | AstMethodDefinition → CallOp, ClrMethod → CallExternalOp |
| `NodeReplacementMetadata` | ConstantFoldingPass | Node substitution before emission |

### µop Types

**Location:** `Poly/Interpretation/VirtualMachine/MicroOperations.cs`

All µops inherit from `abstract record MicroOp(NodeId? Source)` and implement `ToExpression(CompilationContext ctx)`.

| Category | µops | Purpose |
|---|---|---|
| Stack | `PushOp`, `PopOp`, `DupOp` | Stack manipulation |
| Arithmetic | `AddOp`, `SubOp`, `MulOp`, `DivOp`, `NegOp`, `NotOp` | Binary/unary ops |
| Immediate | `AddImmOp`, `SubImmOp`, `EqImmOp`, etc. | Fused push+op for constant right operands |
| Local | `LoadLocalOp`, `StoreLocalOp`, `IncLocalOp` | Local variable access |
| Argument | `LoadArgOp`, `StoreArgOp` | Function argument access |
| Control | `JumpOp`, `JumpIfFalseOp`, `ReturnOp`, `ReturnFromCallOp` | Control flow |
| Heap | `LoadValueOp`, `StoreValueOp` | Heap reference access |
| Closure | `AllocClosureOp`, `LoadUpvalueOp`, `StoreUpvalueOp` | Closure management |
| Call | `CallOp`, `CallClosureOp`, `CallExternalOp` | Function/method calls |
| Exception | `ThrowOp`, `EndFinallyOp` | Exception handling |
| Array | `ArrayLoadOp`, `NewArrayOp`, `ArrayStoreOp` | Direct long[] array ops |
| Batch | `BatchReduceOp` (Sum, CountNonZero, Min, Max, etc.) | Compiled batch loops |
| Special | `CountBitsOp`, `StridedSetOp` | Bit-counting, composite marking |
| Marker | `CommentOp` | No-op for trace readability |

### `CompilationContext`

**Location:** `MicroOperations.cs` (nested record)

Carries `ParameterExpression`s for the compiled delegate: `State` (VmState), `Slots` (stack array), `SP`, `PC`, `FB` (FrameBase), `CAS` (CachedArgSlots), `CodeLen`. Provides helper methods:

- `Push(value)`, `Pop()`, `Top()` — stack operations
- `BinaryArith(op)`, `BinaryCmp(cmp)` — fused stack ops
- `ResyncPC()`, `ResyncSP()` — read back from state
- `WritebackSP()`, `WritebackPC()` — write local to state
- `TraceBefore(op)` — gated trace call
- `GetOrCreateAlias(name, type)` — typed alias variables

---

## 5. Code Generation — µops → Compiled Delegate

**Location:** `Poly/Interpretation/VirtualMachine/ProgramCompiler.cs`

### Compilation Flow

1. Create `ParameterExpression` for `VmState s`
2. Extract `stack` → `RawSlots` → `sp`, `pc`, `fb`, `cas` expressions
3. Build `CompilationContext` with these expressions
4. For each µop at index `i`:
   - Emit `TraceBefore(uop)` (if source present)
   - Emit `uop.ToExpression(ctx)`
   - If not a control-flow µop, emit `pc++`
   - Wrap in breakpoint check: `DebugMode && BreakpointPCs.Contains(pc) → suspend`
   - Create `SwitchCase(i)` for the dispatch loop
5. Loop body: `if (pc < codeLen) → switch(pc) → ... else break`
6. Final block: sync SP and PC back to `VmState`

### Call Site Compilation

**Location:** `CallSiteCompiler.cs`

Compiles CLR method calls into `CallSiteDelegate(VmState)` instances. Bridges the VM's `long[]` stack slot format to CLR method parameters (type conversion, heap lookup for reference types).

---

## 6. VM Execution

**Location:** `Poly/Interpretation/VirtualMachine/Vm.cs`

### Flow

```
Vm.Execute(state)
  ├─ Load program constants into heap
  ├─ prog.EnsureCompiled() → Action<VmState>
  ├─ Invoke compiled delegate (runs dispatch loop)
  ├─ Handle suspension (restore PC)
  └─ Extract result from stack
```

### Key Handler Methods

Called from compiled µops via `Expression.Call` with `MemberHelper.MethodOf()` for compile-time-safe references:

| Handler | µop | Purpose |
|---|---|---|
| `HandleCall` | `CallOp` | Set up call frame, push return address, jump to function |
| `HandleCallClosure` | `CallClosureOp` | Resolve closure from heap, set up frame |
| `HandleCallExternal` | `CallExternalOp` | Invoke compiled call site delegate |
| `HandleAllocClosure` | `AllocClosureOp` | Pop captures, create Closure on heap |
| `HandleLoadUpvalue` | `LoadUpvalueOp` | Read from closure captures |
| `HandleStoreUpvalue` | `StoreUpvalueOp` | Write to closure captures |
| `HandleThrow` | `ThrowOp` | Find exception region, jump to catch/finally |
| `HandleEndFinally` | `EndFinallyOp` | Re-throw pending exception or continue |

### Runtime State

**Location:** `VmState.cs`, `ValueStack.cs`, `Heap.cs`, `Closure.cs`

| Component | Purpose |
|---|---|
| `VmState` | PC, FrameBase, stack, heap, debug state, trace writer |
| `ValueStack` | `long[]` backed by `ArrayPool<long>.Shared` for zero-GC push/pop |
| `Heap` | `List<object?>` with free-list for GC-free object storage |
| `Closure` | Function index + captures array |

### Debugger

**Location:** `VmDebugger.cs`

PC-level debugger managing breakpoints and stepping. Integrates with `VmState.BreakpointPCs` and `Bytecode.NodeRanges` (AST node → µop PC range mapping) for step-over.

---

## 7. Metadata Flow

### What Gets Produced Where

| Metadata Type | Produced By | Stored On | Consumed By |
|---|---|---|---|---|
| `TypeResolutionMetadata` | TypeResolver | Each node | MemberResolver, Lowering, code gen |
| `MemberResolutionMetadata` | MemberResolver | Invoke/Member/New nodes | Lowering (CallOp, CallExternalOp) |
| `VariableAnalysisMetadata` | ScopeValidator | Root node (via `context.SetMetadata`) | Lowering (alias, scope, captures) |
| `ConstantValueMetadata` | ConstantFoldingPass | Folded nodes | Lowering (immediate µops), code gen |
| `DefiniteAssignmentMetadata` | DefiniteAssignmentAnalyzer | Lambda body nodes | Lowering (zero-init skip) |
| `SideEffectMetadata` | SideEffectAnalyzer | Each node | LinqExpressionGenerator (DCE) |
| `ControlFlowMetadata` | ControlFlowAnalysisPass | Control-flow nodes | Dead code diagnostics |
| `StackDepthMetadata` | StackDepthAnalyzer | Each node | Verification, optimization |
| `NodeReplacementMetadata` | ConstantFoldingPass | Replaced nodes | All backends |

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
Lowering ──► Bytecode (µops + function table + constants + metadata)
  │
  ├── EmitContext consumes analysis metadata via Analysis field
  ├── Uses TryGetConstantLong from ConstantFolding analysis
  ├── Uses VariableAnalysisMetadata for alias eligibility
  └── Uses DefiniteAssignmentMetadata for zero-init optimization
```

---

## 8. Execution Backends

### VM Backend (µops)

The primary execution engine. Lowest-level representation — a flat list of `MicroOp` records compiled to a single `Action<VmState>` delegate. Optimized for speed:

- Direct `long[]` stack via `ArrayPool<long>.Shared`
- Compiled dispatch loop with no interpretive overhead
- µop-level tracing gated at ~1 ns when inactive
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

Poly has two domain modeling subsystems:

### V3 — Immutable Core (`Poly/DomainModeling/`)

Immutable record types constructed via a fluent builder API. The primary model for analysis and lowering.

**Key Types:** `Domain`, `Entity`, `Stage`, `Action`, `Effect`, `Policy`, `Relationship`, `Property`, `Event`, `DomainExpression`, `Constraint`

**Builders:** `DomainBuilder` → `EntityBuilder` → `StageBuilder` / `ActionBuilder` / ... → `Build()`

**Expression System:** `DomainExpression` — unified expression type for policies and rules: `PropertyAccess`, `ParameterAccess`, `Literal`, `And`, `Or`, `RelationshipNavigation`, `Exists`, arithmetic, date operations.

### V2 — Mutable Graph (`Poly/Data/Modeling/`)

Mutable directed acyclic graph with transactional mutation support. Used for interactive model editing.

**Key Difference:** V2 objects carry a `Domain` reference and mutate in-place via `DomainMutationCommand` steps. V3 objects are immutable records constructed once.

---

## 11. Key Design Principles

1. **Nodes are pure data records** — no semantics, no type info. All semantic resolution is the job of analysis passes (`INodeAnalyzer`).

2. **`NodeId` provides stable identity** — enables incremental analysis by reusing metadata across parses.

3. **The VM is the canonical semantics** — the compiled µop delegate defines the authoritative meaning of a program. All other backends should produce equivalent behavior.

4. **Metadata flows unidirectionally** — Analysis produces metadata, lowering consumes it. Lowering does not run its own analysis. The `ScopeValidator` produces `VariableAnalysisMetadata` (assignment counts, escape info) that replaced lowering's previous hand-rolled `CollectEscapeInfo` pre-scan.

5. **Module boundaries are enforced**:
   - `Interpretation` → `Introspection`
   - `Validation` → `Interpretation`
   - `Synthesis` → `Syntax`, `Interpretation`
   - No module depends on `Synthesis` except `DomainModeling`

6. **Domain concepts lower to generic VM opcodes** — no domain-specific opcodes.

7. **Compile-time-safe member references** — `MemberHelper.MethodOf(() => ...)` and `MemberHelper.PropertyOf(() => ...)` create IL-level metadata references that survive dead-member analysis, replacing string-based `typeof(T).GetMethod("Name")` patterns.

---

## File Index

### Syntax
- Base: `Poly/Ast/Node.cs`, `NodeId.cs`, `NodeExtensions.cs`
- Nodes: `Poly/Ast/Nodes/` (all expression and statement types)
- Type defs: `Poly/Ast/Nodes/TypeDefinitions/`
- Analysis framework: `Poly/Analysis/`

### Interpretation
- Analysis passes: `Poly/Interpretation/Analysis/Semantics/`
- Constant folding: `Poly/Interpretation/Analysis/ConstantFolding/`
- Control flow: `Poly/Interpretation/Analysis/ControlFlow/`
- VM: `Poly/Interpretation/VirtualMachine/` (all µops, lowering, compiler, runtime)
- LINQ backend: `Poly/Interpretation/LinqExpressions/`
- C# backend: `Poly/Interpretation/CSharp/`

### Introspection
- Interfaces: `Poly/Introspection/` (ITypeDefinition, ITypeMember, etc.)
- CLR backing: `Poly/Introspection/CommonLanguageRuntime/`

### Domain Modeling
- V3 (immutable): `Poly/DomainModeling/` (core types + `Builders/`)
- V2 (mutable): `Poly/Data/Modeling/` (core, `Analysis/`, `CodeGeneration/`, `Effects/`, `Validation/`)
