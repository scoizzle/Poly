# HISTORICAL: Stack-Based Tree-Walking Virtual Machine — Design Plan

**Status: SUPERSEDED — June 2026. The TreeWalkingInterpreter was never fully implemented and has been removed. The VM (`Poly/Interpretation/Vm/`) is now the sole canonical execution engine. See `docs/decisions/2026-06-08-vm-as-canonical-semantics.md`.**

**Core Architectural Tenet:** The interpreter must be **suspendable, introspectable, and re-analyzable by design**. 

It is not merely an execution engine. It is an **executable symbolic medium** that can be paused at any point, have its full state (call stack, evaluation stack, heap, current node, metadata) inspected, subjected to additional analysis passes, and then resumed. This enables a rich neurosymbolic authoring loop: author → lower → execute → interrupt → analyze generated code → provide hints/warnings/suggestions back to the model/user → refine.

Suspendability, introspection, and re-analyzability are not features. They are **non-negotiable foundational requirements** of the neurosymbolic platform.

Recent work and test observations have reinforced that analysis policy must be explicit: a bare `TreeWalkingInterpreter().Evaluate(node)` auto-runs the full `AnalyzeForEvaluation` pipeline (ConstantFolding → SideEffectAnalysis → ControlFlowAnalysis + basics) internally. Some integration tests use lighter analysis for the Linq path (`BuildExpression` only runs basic semantic passes). Plans, tests, and usage should make the chosen policy (full vs minimal vs pre-supplied `AnalysisResult`) visible and intentional. This directly supports elision, insight, and the "executable symbolic medium" vision.

This document has been revised to make this principle the primary driver of the architecture from the very first design decision. See decision record `2026-06-post-lowering-insight-analysis.md` for full rationale.

**Execution engine evolution (RISC IR + stack VM):** The tree-walker remains the reference implementation during transition. A new simpler explicit stack-based VM executing a minimal bespoke RISC IR (lowered after full analysis) is being built under `Poly/Interpretation/VirtualMachine/`. See the dedicated implementation plan `docs/plans/risc-ir-stack-vm-implementation-plan.md` (Phases 0+). The RISC path must deliver identical observables (1200-cycle soak, cross-engine fuzz, suspend/insight/re-analysis, breakpoints, CLR interop, stack-ref + heap-ref-cell mutation scenarios, etc.) before cutover. Analysis unification (this design + WS8) is the prerequisite frontend for the lowering.

**Critical Module Boundary (Non-Negotiable):**

`Poly.Interpretation` **must not** directly reference `Poly.DomainModeling`. The dependency must be one-way:

- `DomainModeling` → lowers to `Syntax.Nodes` (via lowering pass)
- `Interpretation` only knows about `Poly.Syntax.Node` trees and `AnalysisResult` metadata

This follows the core module boundaries established in AGENTS.md:

> "Interpretation → Introspection"  
> "No module may depend on Synthesis except DomainModeling (evolution loop)"

**Consequences for this design:**

- Remove all `PolyObject`, `DomainType`, `Entity`, `Action`, etc. references from `Interpretation/`
- The interpreter works **only** with lowered `Syntax.Node` trees
- Type resolution and domain-specific metadata come exclusively from `AnalysisResult`
- Insight analyzers that reason about domain concepts belong in `DomainModeling.Analysis/`
- Analyzers that operate on the lowered symbolic IR belong in `Interpretation.Analysis/`

This separation is essential for maintaining the neurosymbolic architecture. The interpreter should be agnostic to the source domain model and work purely with the symbolic representation.

The insight analyzers we have built (`AuthoringSuggestionGenerator`, `SemanticCoherenceAnalyzer`, `IdempotencySafetyAnalyzer`) correctly live in `DomainModeling.Analysis/` because they analyze the original domain model. Future analyzers that inspect the lowered `Syntax.Node` trees (e.g. for performance characteristics of the generated code, symbolic optimization opportunities, control flow analysis on the lowered representation) will live in `Interpretation.Analysis/`.

There is no duplication. The layers serve different but complementary roles.

**Note on Suspension Design:** We will keep suspension relatively simple. Instead of introducing many new custom types (`SuspensionPoint`, `SuspendReason`, `SuspendedExecution`, etc.), we will primarily use the existing `Diagnostic` system with new severity levels (`Suggestion`, `Explanation`) and a simple `InterpreterState` that can be captured at natural suspension points. This avoids unnecessary complexity while still enabling rich post-execution analysis.

Fine-grained debugging features (conditional breakpoints, step-into/step-over, watch expressions, time travel, etc.) are explicitly deferred to a later phase. They can be added on top of this foundation without breaking changes.

Custom suspension types will only be introduced if they prove necessary during implementation.

---

## Core Design Principles (in priority order)

1. **Suspendable by default** — The interpreter can be paused at any semantically meaningful point (statement boundary, function entry/exit, stage transition, event boundary, contract call, etc.), its full state inspected, additional analysis performed, and then resumed. Fine-grained debugging (conditional breakpoints, step-into/over, watch expressions) is explicitly deferred to a later phase.
2. **Introspectable execution state** — Call stack, evaluation stack, heap, current node, metadata, and suspension reason must be first-class, queryable, and serializable.
3. **Re-analyzable lowered code** — The `Syntax.Node` tree (and future bytecode) must support multiple layers of post-lowering analysis that generate rich diagnostics (`Suggestion`, `Explanation`, etc.) back to the authoring model/user.
4. **Explicit virtual machine** — Even though it walks a tree, the execution model should be designed like a stack-based VM with explicit `EvaluationStack`, `CallStack` of `StackFrame`s, and `InterpreterState`.
5. **Evolvable to bytecode** — The design should naturally evolve into a real bytecode VM + JIT/AOT pipeline.

---

## File Structure

```
Poly/Interpretation/TreeWalking/
  InterpreterState.cs            # Central VM state (EvaluationStack + CallStack)
  StackFrame.cs                  # Explicit call frame with locals, parameters, return address
  EvaluationStack.cs             # Operand stack for values during execution
  TreeWalker.cs                  # Main suspendable interpreter / VM
  InterpreterResult.cs           # Evaluation result (Value, Void, or Signal)
  InterpreterSignal.cs           # Non-local control flow (Return, Break, Continue, Throw)
  InterpreterOptions.cs          # Configuration (max stack depth, timeout, suspension points)
  ITreeWalkerCompiler.cs         # Pluggable node handlers (for hybrid compiled paths)
  PolyObject.cs                  # Runtime representation of lowered type-definition instances
```

**Namespace:** `Poly.Interpretation.TreeWalking`

---

## Core Evaluation Model

### InterpreterResult (Revised)

```csharp
public readonly record struct InterpreterResult {
    public static InterpreterResult None => new();           // void
    public static InterpreterResult Value(object? value) => new(value, null);
    public static InterpreterResult Signal(InterpreterSignal signal) => new(null, signal);

    public object? Value { get; }
    public InterpreterSignal? Signal { get; }

    public bool IsVoid => Signal is null && Value is null;
    public bool HasValue => Signal is null && Value is not null;
    public bool IsSignal => Signal is not null;

    private InterpreterResult(object? value, InterpreterSignal? signal) {
        Value = value;
        Signal = signal;
    }
}
```

**Rationale for changes:**
- Explicit `Value()` and `Signal()` factory methods for clarity
- `IsVoid` vs `HasValue` distinction (critical for control flow)
- Private constructor prevents accidental creation with both value and signal
- Added `InterpreterOptions.cs` to support configuration (max stack depth, timeout, strict mode)

### InterpreterSignal

```csharp
public readonly record struct InterpreterSignal {
    public enum SignalKind { Return, Break, Continue, Throw }

    public SignalKind Kind { get; }
    public object? Value { get; }        // return value or exception object
    public string? Label { get; }        // for labeled break/continue

    public static InterpreterSignal Return(object? value = null) => 
        new(SignalKind.Return, value);
    public static InterpreterSignal Break(string? label = null) => 
        new(SignalKind.Break, label: label);
    public static InterpreterSignal Continue(string? label = null) => 
        new(SignalKind.Continue, label: label);
    public static InterpreterSignal Throw(Exception exception) => 
        new(SignalKind.Throw, exception);
}
```

Signal-passing replaces C# exceptions for structured control flow (return, break, continue). Only actual runtime errors (null refs, type mismatches, divide-by-zero) throw real exceptions.

## Core Data Structures (MemoryPool + Span based)

### InterpreterState — Central VM State

```csharp
public sealed class InterpreterState : IDisposable {
    private readonly MemoryPool<object?> _memoryPool;
    
    public EvaluationStack ValueStack { get; }
    public CallStack CallStack { get; } = new();
    public Dictionary<Guid, PolyObject> Heap { get; } = new();
    
    public bool IsComplete { get; private set; }
    public InterpreterResult? LastResult { get; private set; }

    // Suspension support (kept intentionally simple)
    public bool IsSuspended { get; private set; }
    public string? SuspensionReason { get; private set; }
    public Node? SuspendedAtNode { get; private set; }

    public InterpreterState(MemoryPool<object?>? pool = null);
    
    public void Suspend(string reason, Node? atNode = null);
    public void Resume();
    public void Complete(InterpreterResult result);
    
    public StackFrame CurrentFrame => CallStack.Peek();
    public void Dispose();
}
```

### EvaluationStack — Span-based pooled stack (zero allocation hot path)

```csharp
public sealed class EvaluationStack : IDisposable {
    private readonly MemoryPool<object?> _pool;
    private IMemoryOwner<object?> _memoryOwner;
    private Span<object?> _span;
    private int _count = 0;
    private int _capacity;

    public EvaluationStack(MemoryPool<object?>? pool = null, int initialCapacity = 64);
    
    public void Push(object? value);
    public object? Pop();
    public object? Peek();
    public int Count => _count;
    public Span<object?> AsSpan() => _span.Slice(0, _count);   // zero-copy view for analysis
    
    public void Clear();
    public void Dispose();
}
```

### StackFrame — Explicit Call Frame

```csharp
public sealed class StackFrame {
    public Node CurrentNode { get; set; }
    public Dictionary<Variable, object?> Locals { get; } = new();
    public Dictionary<Parameter, object?> Parameters { get; } = new();
    public object? ThisInstance { get; }
    public Node? ReturnAddress { get; }           // where to resume after this frame
    public Dictionary<string, object?> Metadata { get; } = new();

    public StackFrame(Node entryPoint, object? thisInstance = null, Node? returnAddress = null);
}
```

**Design Notes:**
- `MemoryPool<object?>` + `IMemoryOwner<T>` + `Span<T>` gives excellent performance and low allocation pressure.
- `EvaluationStack.AsSpan()` allows post-lowering analyzers to inspect the current execution state efficiently.
- Suspension is deliberately simple (boolean flag + reason + current node) to avoid complexity in the MVP.
- Fine-grained debugging features (conditional breakpoints, step-into/over, etc.) are explicitly deferred.

---

### InterpreterContext (Revised)

```csharp
public sealed class InterpreterContext {
    // Immutable parent chain for lexical scoping
    private readonly InterpreterContext? _parent;
    private readonly IReadOnlyDictionary<Variable, object?> _variables;
    private readonly IReadOnlyDictionary<Parameter, object?> _parameters;
    private readonly Dictionary<NodeId, Type> _runtimeTypeCache;     // mutable cache
    private readonly Dictionary<string, Node> _functionLabels;       // mutable for goto

    public object? ThisInstance { get; }
    public AnalysisResult? AnalysisResult { get; }
    public InterpreterOptions Options { get; }

    // Factory methods for different scope kinds
    public static InterpreterContext Root(AnalysisResult? analysis = null, InterpreterOptions? options = null);
    public InterpreterContext CreateChildScope();
    public InterpreterContext CreateBlockScope(IEnumerable<Node> declaredVariables);
    public InterpreterContext CreateLoopScope();
    public InterpreterContext CreateFunctionScope(IEnumerable<Parameter> parameters, object? thisInstance);

    // Variable and parameter access (lexical lookup)
    public bool TryGetVariable(Variable variable, out object? value);
    public bool TryGetParameter(Parameter parameter, out object? value);
    public InterpreterContext WithVariable(Variable variable, object? value);
    public InterpreterContext WithParameter(Parameter parameter, object? value);

    // Analysis integration (null-safe)
    public TMetadata? GetMetadata<TMetadata>(Node node) where TMetadata : class, IAnalysisMetadata;
    public Node? GetNodeReplacement(Node node);
    public ITypeDefinition? GetResolvedType(Node node);
    public ITypeMember? GetResolvedMember(Node node);

    // Runtime type resolution with caching
    public Type GetRuntimeType(Node node);
}
```

**Key changes:**
- More functional style: `WithVariable`/`WithParameter` return new contexts (immutability where possible)
- Explicit `Root()` factory for top-level context
- `InterpreterOptions` for configuration (max stack depth, timeout, strict mode)
- `IReadOnlyDictionary` for variables/parameters (safer API surface)
- Analysis integration moved to dedicated methods with clear null-safety
- Added introspection properties (`Variables`, `Parameters`) for debugging

**Scoping rules:** Lexical scoping. Each `Block` creates a child scope. Shadowing: inner scope dictionary checked first. Variables declared in blocks are stored in that scope's dictionary.

---

## PolyObject — Runtime Type-Definition Instances

For when the interpreter encounters `New` with a Poly-defined type definition (not a CLR type):

```csharp
public sealed class PolyObject {
    public Guid InstanceId { get; }
    public ITypeDefinition TypeDefinition { get; }
    public IReadOnlyDictionary<string, object?> Fields { get; }     // immutable after construction
    public Dictionary<string, object?> Properties { get; }          // mutable computed properties

    public PolyObject(ITypeDefinition typeDef, IReadOnlyDictionary<string, object?> initialFields);

    public object? GetField(string name);
    public void SetField(string name, object? value);
    public object? GetProperty(string name, InterpreterContext context);
    public void SetProperty(string name, object? value);
    public InterpreterResult InvokeMethod(string name, object?[] args, InterpreterContext context);
}
```

**Design notes:**
- Fields are immutable after construction (set via constructor)
- Properties can be computed on demand (getter logic in `GetProperty`)
- Method dispatch checks `PolyObject` first, then falls back to CLR reflection
- `InstanceId` enables heap introspection and debugging

For CLR-backed types, create CLR objects via `Activator.CreateInstance(type)` or `ConstructorInfo.Invoke(args)`. Properties and methods accessed via standard reflection.

---

## TreeWalker — Main Interpreter

```csharp
public sealed class TreeWalker {
    private readonly AnalysisResult? _analysisResult;
    private readonly InterpreterOptions _options;
    private readonly List<ITreeWalkerCompiler> _customCompilers = new();

    public TreeWalker(AnalysisResult? analysisResult = null, InterpreterOptions? options = null);

    public TreeWalker RegisterCompiler(ITreeWalkerCompiler compiler);

    public InterpreterResult Evaluate(Node node, InterpreterContext? context = null);
    public object? EvaluateAsValue(Node node, InterpreterContext? context = null);
    public T? EvaluateAs<T>(Node node, InterpreterContext? context = null);

    private InterpreterResult EvaluateNode(Node node, InterpreterContext context);
    private InterpreterResult HandleSignal(InterpreterResult result, InterpreterContext context);
}
```

### ITreeWalkerCompiler (pluggable compiler pattern)

```csharp
public interface ITreeWalkerCompiler {
    bool TryEvaluate(Node node, Func<Node, InterpreterContext, InterpreterResult> evaluateChild,
                     InterpreterContext context, out InterpreterResult result);
}
```

Mirrors `INodeCompiler` from `LinqExpressions/INodeCompiler.cs`. Custom compilers get first chance. The `evaluateChild` callback now takes both `Node` and `InterpreterContext` for maximum flexibility.

---

## Expression Evaluation — Full Node Dispatch

### Leaf nodes
| Node type | Evaluation |
|-----------|-----------|
| `Constant` | `Value(node.Value)` |
| `Default` | `Value(GetDefaultValue(targetType))` |
| `Variable` | Lookup in context (`TryGetVariable`) |
| `Parameter` | Lookup in context (`TryGetParameter`) |
| `ThisReference` | `Value(context.ThisInstance)` |
| `NullForgiving` | Unwrap operand |
| `TypeReference` / `TypeDefinitionReference` | `Value(null)` (compile-time concepts) |

### Arithmetic
| Node type | Evaluation |
|-----------|-----------|
| `Add` | Numeric promotion + `a + b`. String: `string.Concat(a, b)`. |
| `Subtract` | Numeric promotion + `a - b` |
| `Multiply` | Numeric promotion + `a * b` |
| `Divide` | Numeric promotion + `a / b` (throws on divide-by-zero) |
| `Modulo` | `a % b` |
| `UnaryMinus` | `-(operand)` |

**Numeric promotion:** Use `Convert.ChangeType` + `dynamic` dispatch. Divide-by-zero becomes `InterpreterSignal.Throw(new DivideByZeroException())`.

### Comparison
| Node type | Evaluation |
|-----------|-----------|
| `Equal` | `EqualityComparer<T>.Default.Equals(a, b)` |
| `NotEqual` | `!Equals(a, b)` |
| `LessThan` | `Comparer<T>.Default.Compare(a, b) < 0` |
| `LessThanOrEqual` | `<=` |
| `GreaterThan` | `>` |
| `GreaterThanOrEqual` | `>=` |

### Boolean (short-circuit)
| Node type | Evaluation |
|-----------|-----------|
| `And` | Evaluate LHS. If false → `Value(false)` (skip RHS). Else evaluate RHS. |
| `Or` | Evaluate LHS. If true → `Value(true)` (skip RHS). Else evaluate RHS. |
| `Not` | `!(bool)operand` |

### Conditional, Coalesce
| Node type | Evaluation |
|-----------|-----------|
| `Conditional` | Evaluate condition. Truthy → IfTrue, else → IfFalse. Return branch result. |
| `Coalesce` | If LHS non-null → LHS, else RHS. |

### Member Access & Index Access
| Node type | Evaluation |
|-----------|-----------|
| `Member` | Evaluate target. If `PolyObject` → `GetProperty(name, context)`. If CLR → reflection `GetProperty`/`GetField`. |
| `IndexAccess` | Evaluate target + indices. CLR array → `GetValue`, indexer → `PropertyInfo.GetValue`. |

### Invocation
| Node type | Evaluation |
|-----------|-----------|
| `Invoke` | If delegate is `Member` → resolve via `GetResolvedMember` (analysis) or reflection. If `Lambda` → compile to CLR `Delegate` via `Expression.Lambda.Compile()` then `DynamicInvoke`. If already a `Delegate` → `DynamicInvoke`. |

**Method dispatch algorithm:**
1. Evaluate the target object from `(Invoke.Delegate as Member).Value`
2. Use `GetResolvedMember(Invoke)` from analysis to get `ITypeMethod` if available
3. If `ClrMethod` → use `MethodInfo.Invoke(target, args)`
4. If method on `PolyObject` → use `PolyObject.InvokeMethod(name, args, context)`
5. If analysis not available → fall back to reflection: `target.GetType().GetMethod(name, argTypes)`

**Argument evaluation:** All arguments evaluated left-to-right before invoking. Argument types matched by position.

### Constructor (`New`)
| Node type | Evaluation |
|-----------|-----------|
| `New` | If CLR → `ConstructorInfo.Invoke(args)`. If Poly type definition → create `PolyObject`, evaluate constructor body with `this` bound. |

### Lambda
| Node type | Evaluation |
|-----------|-----------|
| `Lambda` | Does **not** evaluate body immediately. Creates closure: captures current `InterpreterContext` and wraps body + parameters into a `Delegate` via `Expression.Lambda` + `Compile()`. Alternative pure-interpreter approach (see "Open Questions") would store `(Lambda, capturedContext)` and evaluate on demand. |

**Trade-off documented below.**

### Type Casts
| Node type | Evaluation |
|-----------|-----------|
| `TypeCast` | `Convert.ChangeType(operand, targetType)` if checked, else direct CLR cast. |
| `TypeIs` | `targetType.IsInstanceOfType(operand)` |
| `TypeAs` | If compatible → operand, else null. |

### Await
| Node type | Evaluation |
|-----------|-----------|
| `Await` | Evaluate operand (expects `Task<T>`). `GetAwaiter().GetResult()` (synchronous extraction, consistent with existing `LinqExpressionGenerator.CompileAwait`). |

---

## Statement Execution

### Block
1. Create child scope (`context.CreateBlockScope(block.Variables)`)
2. Declare variables (set to uninitialized sentinel or `default(T)`)
3. For each node in `block.Nodes`:
   a. Evaluate node
   b. If result is a `Signal` (return/break/continue/throw), propagate immediately
4. Return last non-signal result (or `None` if all void)

**Block scoping:** Variables listed in `block.Variables` are declared in the new scope. Variables declared with `Variable` nodes get storage. Parameters in block variables get declared too (for lambda body blocks).

### Assignment
1. Evaluate `assignment.Value`
2. If Destination is `Variable` → `context.WithVariable(...)`
3. If Destination is `Parameter` → `context.WithParameter(...)`
4. If Destination is `Member` → evaluate target, set via reflection or `PolyObject.SetProperty`
5. If Destination is `IndexAccess` → evaluate target and indices, set via reflection
6. Return the assigned value (matching C# assignment expression semantics)

### IfStatement
Evaluate condition. Truthy → evaluate `ThenBranch`, else → evaluate `ElseBranch` (if present). Return branch result (or `None` if void). Since `IfStatement` extends `Operator`, it can be used in expression position — branch result is returned.

### SwitchStatement
1. Evaluate switch value
2. For each case: evaluate pattern (expected to be constant), compare with value using `EqualityComparer<T>.Default`
3. Match → evaluate case body, propagate signal, return
4. No match and DefaultCase exists → evaluate DefaultCase
5. Return `None` if no case matched

### WhileLoop / DoWhileLoop
1. Create loop scope (for break/continue binding)
2. Loop: evaluate condition (body-first for DoWhile). False → break.
3. Evaluate body:
   - `BreakSignal` → break and **do not** propagate
   - `ContinueSignal` → continue to next iteration
   - `ReturnSignal` or `ThrowSignal` → propagate upward
4. Return `None`

### ForLoop
1. Evaluate initializer (if present) once
2. Create loop scope
3. Loop: evaluate condition; if false → break. Evaluate body. On normal end or `ContinueSignal` → evaluate increment.
4. Signal handling same as WhileLoop.

### ForEachLoop
1. Evaluate collection to get `IEnumerable`
2. Get enumerator: `collection.GetEnumerator()`
3. Create loop scope, declare loop variable in scope
4. Loop: while `(enumerator.MoveNext())`:
   a. Assign `enumerator.Current` to loop variable
   b. Evaluate body, handle signals same as other loops
5. Dispose enumerator if `IDisposable` (try/finally pattern)
6. Return `None`

### Break / Continue
`BreakStatement` → `Signal(InterpreterSignal.Break(node.Label))`
`ContinueStatement` → `Signal(InterpreterSignal.Continue(node.Label))`

These are handled by the nearest enclosing loop. Labeled break/continue (`break labelname`) is not yet supported by the loop constructs — this is a future enhancement.

### Goto / LabelDeclaration
**Initial implementation:** throw `NotSupportedException("Goto not implemented")`.

**Future:** compile function bodies into a switch-based state machine or use a continuation-passing approach. This is complex and deferred until needed.

### Return
Evaluate node.Value (if present) → `Signal(InterpreterSignal.Return(value))`

### Throw
Evaluate node.Exception (expects an `Exception` object or string):
- If string → `new Exception(string)`
- Return `Signal(InterpreterSignal.Throw(exception))`

### TryCatchFinally
1. Evaluate `TryBlock` inside real `try { } catch (Exception ex) { }`
2. On CLR exception → convert to `ThrowSignal`, check catch clauses (first match by type)
3. If catch clause matches:
   - Create child scope, bind exception variable
   - Evaluate catch body
4. `Finally` block always executes (even on return/break/continue)
5. If finally throws, it replaces the original exception
6. Return `None` (try/catch is a statement)

**Important:** Real exceptions (from CLR operations inside the interpreter, like null refs) must be caught by wrapping the TryBlock evaluation in a real `try { } catch (Exception ex) { }` to convert them into `ThrowSignal`.

### UsingStatement
1. Evaluate resource
2. `try { evaluate body } finally { if (resource is IDisposable d) d.Dispose(); }`

---

## Runtime Error Model

### CLR Exceptions vs Interpreter Signals

| Aspect | Analysis errors | Runtime errors | Interpreter signals |
|--------|-----------------|----------------|---------------------|
| When | Before interpretation | During interpretation | During interpretation |
| Examples | Type not found, member not found | Null ref, divide by zero, cast failure | Return, Break, Continue, Throw |
| How reported | `AnalysisResult.Diagnostics` | `InterpreterSignal.Throw(new Exception(...))` | `InterpreterResult.Signal(...)` |
| Can resume? | No (must fix AST) | Yes (via try/catch) | Yes (handled by enclosing statements) |

The interpreter wraps each operation that could fail (member access, method call, numeric operation, array access) in a `try { } catch (Exception ex) { }` that converts CLR exceptions to `InterpreterSignal.Throw(ex)`.

**Fatal errors:** Stack overflow, assertion failure, or interpreter bugs propagate as real CLR exceptions (not caught as signals).

**Error messages:** Should include `NodeId` information from analysis when available (`analysis.GetNodeLocation(node)` or similar). This gives users source location for runtime errors.

**Custom exception type:** Consider `InterpreterException` that wraps both the signal and the original exception with source location.

---

## Integration Strategy

The tree-walking interpreter should integrate with the lowering pass:

```csharp
// In V3DomainLoweringPass or a new V3DomainInterpreter
public InterpreterResult Evaluate(Domain domain, Node entryPoint) {
    var analysis = _analyzer.Analyze(domain);
    var lowered = _loweringPass.LowerToTypeDefinitions(domain, analysis);
    var walker = new TreeWalker(analysis);
    return walker.Evaluate(entryPoint);
}
```

**Public API recommendation:**
```csharp
public static class DomainExtensions {
    public static InterpreterResult Evaluate(this Domain domain, string entryPointName, params object?[] args);
    public static T? Evaluate<T>(this Domain domain, string entryPointName, params object?[] args);
}
```

This would:
1. Run analysis
2. Lower domain to `TypeDefinitionNode[]`
3. Find the entry point method
4. Create appropriate context with arguments
5. Run the tree walker

The interpreter should be registered alongside `LinqExpressionGenerator` and `CSharpGenerator` in the interpretation layer.

---

## Bytecode Alternative

**Tree-Walking Interpreter (Current Plan)** vs **Bytecode-Based Interpreter**

### Core Approach

**Tree-Walking:**
- Directly walks the `Syntax.Node` AST recursively
- Dispatches on each node type with a big switch in `EvaluateNode()`
- Uses `InterpreterContext` for scoping, variables, and state
- Signals (`InterpreterSignal`) for control flow (return, break, continue, throw)

**Bytecode-Based:**
- First lowers `Syntax.Node` AST to a linear bytecode format (opcodes + operands)
- Virtual machine executes the bytecode sequentially with a program counter (PC)
- Bytecode is a simpler, flatter representation (e.g., `LOAD_VAR 5`, `ADD`, `JUMP_IF_FALSE 23`)
- VM has an explicit stack, heap, call stack, and instruction pointer

### Complexity & Implementation Effort

**Tree-Walking Advantages:**
- **Simpler to implement**: Direct mapping from AST nodes to evaluation logic. The plan already has a clear 16-phase implementation order.
- **Fewer moving parts**: No separate bytecode format, no bytecode emitter, no VM instruction decoder.
- **Easier debugging**: Stack traces map directly to AST nodes. Can easily add breakpoints on specific node types.
- **Leverages existing analysis**: Can use `AnalysisResult.GetNodeReplacement()` and metadata directly during evaluation.

**Bytecode Advantages:**
- **Cleaner separation**: Between "frontend" (AST) and "backend" (VM)
- **Easier to optimize**: Peephole optimization, JIT, ahead-of-time compilation to native code
- **Better long-term**: Multiple backends (interpreter, JIT, AOT compiler) become possible
- **More familiar pattern**: Python, Lua, JVM, .NET IL all use bytecode VMs

**Recommendation**: Start with the tree-walking interpreter as currently designed. It's the right choice for getting something working quickly, validating the domain model, and providing immediate utility for REPL/debugging/policy evaluation. Design the bytecode format in parallel as a separate workstream. The tree-walker can inform what operations the bytecode needs to support.

The current design is structured to evolve toward bytecode later — the `InterpreterResult` and signal model would map cleanly to a VM's stack-based execution model.

---

## Analysis Integration

The `TreeWalker` accepts optional `AnalysisResult`:

1. **Node replacements**: `analysisResult.GetNodeReplacement(node)` — allows analysis passes to lower high-level nodes before interpretation
2. **Type resolution**: `analysisResult.GetResolvedType(node)` — provides `ITypeDefinition` for member access resolution
3. **Member resolution**: `analysisResult.GetResolvedMember(node)` — provides `ITypeMember` for method/constructor/property dispatch
4. **Diagnostics**: Not used during interpretation (analysis-phase errors should be checked before running the interpreter)

The interpreter **can** work without an `AnalysisResult` (falling back to pure CLR reflection), but it will be less capable with Poly-defined types and cannot run lowered domain-model nodes.

**Recommended usage:**
```csharp
var analysis = analyzer.Analyze(ast);
if (analysis.HasErrors) { /* show diagnostics */ }

var walker = new TreeWalker(analysis);
var result = walker.Evaluate(ast);
```

---

## Key Differences from LinqExpressionGenerator

| Aspect | LinqExpressionGenerator | TreeWalker |
|--------|------------------------|------------|
| Output | `Expression` tree | `InterpreterResult` (value or control-flow signal) |
| Control flow | `LabelTarget`, `GotoExpression`, `Return` | `InterpreterSignal` discriminated union |
| Type system | CLR types only (`Type`, `ClrTypeDefinition`) | CLR types + `PolyObject` for Poly-defined types |
| Scope | `CompilationContext` (variables as `ParameterExpression`) | `InterpreterContext` (variables as `object?` in dictionaries) |
| Member binding | Static at expression-tree construction | Dynamic at runtime via reflection |
| String concat | `string.Concat` method call in expression tree | Direct `string.Concat(a, b)` |
| Lambda | Build expression tree + `Expression.Lambda` | Compile to CLR `Delegate` (pragmatic) or pure interpreter (future) |
| Short-circuit | `AndAlso` / `OrElse` expression nodes | Control flow in evaluator (`if` statements) |
| Performance | Fast after JIT, slow to compile | Moderate (no JIT overhead, but per-node dispatch) |
| Dependencies | `System.Linq.Expressions` | Minimal (base class library only) |
| Error messages | CLR exceptions at compile time | Descriptive messages with node position info |

### What the interpreter does simpler
- No expression tree construction — direct value computation
- No type-promotion concern for expression tree compatibility
- No `LabelTarget` management
- Natural short-circuit evaluation
- Natural try/catch via throw/catch

### What the interpreter does differently
- Reflection-based member dispatch instead of static `Expression.Property`/`Expression.Call`
- Dynamic numeric promotion at runtime instead of compile-time `Expression.Convert`

### What the interpreter cannot do
- Cannot skip semantic analysis (LinqExpressionGenerator can infer types from the expression tree structure)
- Cannot be composed into larger expression trees (output is a value, not an expression)
- Cannot be serialized or inspected as a data structure
- Slower for hot-path repeated evaluation

---

## Lambda/Closure Strategy — Trade-off

**Current pragmatic approach:** Compile `Lambda` nodes to CLR `Delegate` via `Expression.Lambda` + `Compile()`. This is simple and leverages existing infrastructure.

**Pure interpreter approach (recommended for future):**
- Store `(Lambda lambda, InterpreterContext capturedContext)` in a `PolyFunction` record
- When invoked, create function scope from captured context + arguments, evaluate body
- No dependency on `System.Linq.Expressions`

**Recommendation:** Start with the pragmatic `Expression.Compile()` approach for speed of implementation. Add pure interpreter path later when cross-platform or AOT constraints demand it.

---

## Performance Considerations

### Caching Strategy

| Cache | What | Why |
|-------|------|-----|
| Type cache | Maps `NodeId` → `Type` | Avoid repeating reflection-based type resolution |
| Member cache | Maps `(NodeId, memberName)` → `MemberInfo` | Avoid repeated `GetMethod`/`GetProperty` lookups |
| Delegate cache | Maps `MethodInfo` → `Func<object?, object?[], object?>` | Avoid repeated `MethodInfo.Invoke` overhead |

The cache lives in `InterpreterContext` and is populated lazily.

### Performance Characteristics

- **Cold start:** Slower than `LinqExpressionGenerator` due to per-node dispatch
- **Hot paths:** 10-50x slower than compiled expressions (no JIT)
- **REPL use:** Excellent — no compilation overhead, immediate feedback
- **Policy evaluation:** Good for low-frequency evaluation, poor for high-frequency loops

**When to prefer interpreter vs compiled path:**
- Interpreter: REPL, debugging, one-off policy evaluation, cross-platform/AOT
- Compiled: Production policy evaluation, hot paths, loops

**Stack safety:** Deeply nested recursion in the tree walker can overflow the .NET call stack. A trampoline or explicit evaluation stack may be needed for the general case. Add `maxStackDepth` to `InterpreterOptions`.

---

## Testing Strategy

### Phase 0: Smoke test
- Simple constant/literal evaluation
- Basic arithmetic and comparison

### Phase 1: Leaf nodes and basic values
- `Constant`, `Default`, `Variable`, `Parameter`, `ThisReference`, `NullForgiving`

### Phase 2: Arithmetic and comparison
- Each arithmetic operator with various numeric types (int, long, float, double, decimal)
- String concatenation via `Add`
- Division by zero → `ThrowSignal`
- All six comparison operators
- Type promotion across numeric types

### Phase 3: Boolean and conditional
- `And` short-circuit (false && (throw) → no throw)
- `Or` short-circuit (true || (throw) → no throw)
- `Not` negation
- `Conditional` (ternary)
- `Coalesce` (??)

### Phase 4: Member access and invocation
- CLR property get/set via `Member` + `Assignment`
- CLR method invocation via `Invoke` with `Member`
- Index access on arrays and lists
- Constructor invocation via `New`

### Phase 5: Statements and control flow
- `Block` scoping and sequential execution
- `Assignment` to variables, parameters, members, indexers
- `IfStatement` with/without else
- `SwitchStatement` with multiple cases and default

### Phase 6: Loops
- `WhileLoop` — basic loop, break, continue, return inside
- `DoWhileLoop` — same but body-first
- `ForLoop` — with/without initializer, condition, increment
- `ForEachLoop` — on arrays, `List<T>`, `IEnumerable`
- Nested loops with correct break/continue targeting

### Phase 7: Functions and return
- `Lambda` creation and invocation
- `Return` inside lambda correctly exits lambda, not outer scope
- Multiple return points in same function
- Recursive function calls

### Phase 8: Exception handling
- `Throw` with `Exception` and with string
- `TryCatchFinally` catching specific exception types
- Catch-all handlers
- Finally always executes
- Nested try/catch
- Exceptions from CLR operations caught by Poly try/catch

### Phase 9: Type definitions (PolyObject)
- Creating a `New` with a Poly-defined type
- Accessing properties on `PolyObject` instances
- Calling methods on `PolyObject` instances
- Constructor body evaluation

### Phase 10: Integration
- Full pipeline: Syntax.Node → Analyzer → TreeWalker
- Node replacement integration
- `ITreeWalkerCompiler` custom compiler registration
- Fallback without `AnalysisResult`

### Phase 11: Performance comparison
- Compare interpreter vs `LinqExpressionGenerator` on hot loops
- Measure stack depth limits

### Phase 12: Error messages
- Test that runtime errors include `NodeId` location information when available

---

## Open Questions / Future Considerations

1. **Stack safety:** Deeply nested recursion in the tree walker can overflow the .NET call stack. A trampoline or explicit evaluation stack may be needed for the general case.

2. **Tail calls:** Not supported initially — add when function-heavy evaluation patterns demand it.

3. **Debugger support:** `BreakStatement` with a label could trigger debugger hooks in the future.

4. **State machine compilation of `goto`:** If `goto` is needed, compile function bodies into a switch-based state machine.

5. **Async interpretation:** Cross-entity policies will require real async — the interpreter should eventually support `await` by returning `Task<InterpreterResult>` from each evaluation. This is deferred until the first policy test demands it.

6. **Pure interpreter for Lambda:** The pragmatic `Expression.Compile()` approach for lambdas creates a dependency on `System.Linq.Expressions`. A pure interpreter approach (`PolyFunction` holding `Lambda` + captured context) would make the interpreter completely standalone.

7. **Performance vs compiled path:** Should there be automatic fallback to `LinqExpressionGenerator` for hot functions? Or should this be explicit in the API?

8. **Memory model:** How should the "heap" of `PolyObject` instances be managed? Should there be weak references or explicit disposal?

---

## Integration Strategy

The tree-walking interpreter should integrate with the lowering pass:

```csharp
// In V3DomainLoweringPass or a new V3DomainInterpreter
public InterpreterResult Evaluate(Domain domain, Node entryPoint) {
    var analysis = _analyzer.Analyze(domain);
    var lowered = _loweringPass.LowerToTypeDefinitions(domain, analysis);
    var walker = new TreeWalker(analysis);
    return walker.Evaluate(entryPoint);
}
```

**Public API recommendation:**
```csharp
public static class DomainExtensions {
    public static InterpreterResult Evaluate(this Domain domain, string entryPointName, params object?[] args);
    public static T? Evaluate<T>(this Domain domain, string entryPointName, params object?[] args);
}
```

This would:
1. Run analysis
2. Lower domain to `TypeDefinitionNode[]`
3. Find the entry point method
4. Create appropriate context with arguments
5. Run the tree walker

The interpreter should be registered alongside `LinqExpressionGenerator` and `CSharpGenerator` in the interpretation layer.

---

## Conclusion

This design provides a clean, maintainable tree-walking interpreter that complements the existing `LinqExpressionGenerator` and `CSharpGenerator`. The signal-passing model for control flow, clear separation between CLR and PolyObject, comprehensive node dispatch table, and phased implementation order make this a solid foundation.

The pragmatic use of `Expression.Compile()` for lambdas accelerates implementation while the pure interpreter path remains a viable future direction. The design is flexible enough to support both REPL-style evaluation and production policy evaluation.

**Recommended next step:** Implement Phases 0-3 (leaf nodes, arithmetic, comparison, boolean) first. These will validate the core `InterpreterResult` + `InterpreterContext` model before tackling the more complex statement and control flow nodes.
