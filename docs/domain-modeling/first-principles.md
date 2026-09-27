# First principles for domain modeling

This document is a map of what this repository *already does*, written so you can open it cold.

It is not a plan, not a review, and not a list of things to fix. Every principle below is derived from implemented code. Hand-written core — the syntax tree, the interpreter, and the primitive types — comes first. The domain-modeling pipeline is described as it actually runs: load libraries, analyze the author’s model, lower that model into syntax trees, then simulate or print those trees.

Docs under `docs/decisions/`, `docs/CORE.md`, `docs/ARCHITECTURE.md`, and plan notes are used only to *name* something the code already does. An idea that lives only in a document is not a principle. Those leftover ideas sit at the end, one line each, without ranking.

**How to read it.** Principle 0 is the anchor: a trivial syntax tree is the unit of meaning, and the interpreter exists to simulate that tree efficiently. Principle 0.1 is the check every later layer must still pass: can a person or a model step through the running snippet and see which tree node they are on? Principle 0.2 is the hand-edit bar: the owner can change any file without an agent. The ten sections after that walk the pipeline in order. Each principle has four parts: the claim, why it has to be true, where it lives in code, and which product-roadmap phase it serves.

**A few terms, used the same way throughout.**

- A **syntax tree** (also called an AST, abstract syntax tree) is a small, typed tree of nodes such as constants, arithmetic, `if`, loops, method calls, and type definitions. Nodes live under `Poly/Ast/`. They are data. They do not execute themselves.
- The **interpreter** is the in-process simulator for those trees. It analyzes a tree, compiles it to a runnable program, and executes it. That is `Poly/Interpretation/Interpreter.cs`.
- A **domain** is the author’s model of a business: named types, properties, stages, actions, policies, and relationships. It is facts. It is not yet a running program.
- **Lowering** is the conversion of those facts into syntax trees the interpreter can run and the C# printer can print.
- **Print** means turning a syntax tree into C# source text.
- A **library** (listed on a domain with `uses`) is a named extension loaded into a compile session. It contributes language tables, type maps, extra analysis, or extra generated files. It does not invent a second language.
- A **compile session** (`DomainSession`) is the loaded libraries plus the domain they apply to. It is not a chat session.
- The **MCP harness** (Model Context Protocol tools in `Poly.Mcp/`) is an interactive conversation for authoring and simulating. It is not the customer product.

---

## Why: the product roadmap

This work exists to sell software, then to widen what that software can be, then to make learned algorithms cheap to run.

**Phase 1.** Domain modeling that produces real, business-grade working software. The fast path to revenue: something sellable. Today that means a business domain that can be simulated, then printed as working C# — including persistence and an HTTP API when those libraries are loaded.

**Phase 2.** Expand to any kind of software, not only web APIs, with agents reviewing the output and their feedback flowing directly into the product.

**Phase 3.** The neurosymbolic leap. Algorithms and heuristics embedded in a model’s weights get translated into simple code and run far more cheaply, freeing tensor compute for the parts that truly need it.

The rest of this document marks, as an observation rather than a ranking, whether a layer is **earning its place for phase 1** (sellability by simulation and generated working software) or is **only justified by phase 2/3**. Phase 1 sellability is the priority lens.

---

## Principle 0 — The trivial syntax tree is the unit of meaning

**Principle.** The trivial syntax tree is the unit of meaning, and the interpreter simulates it efficiently. The interpreter exists only so operations can be simulated efficiently from snippets of conceptual code modeled as a trivial syntax tree (AST). Everything hand-written in the core serves this.

**Why it has to be true.** If meaning lived in a host trick, a dictionary walk, or a second evaluator, you could not trust that “what we simulated” is the same thing as “what we printed” or “what the customer runs.” A small, inspectable tree is something a human or a model can read, step through, and reuse. The interpreter is not a product of its own. It is the cheap, in-process way to *run that tree*.

**Where it lives in code.**

- Base node, stable identity, child walk, and a compact trace string: `Poly/Ast/Node.cs`, `Poly/Ast/NodeId.cs`. Fluent construction: `Poly/Ast/NodeExtensions.cs`.
- Expression and statement node types (the vocabulary of a snippet): `Poly/Ast/Nodes/` — among them `Constant.cs`, `Block.cs`, `Assignment.cs`, `IfStatement.cs`, `Invoke.cs`, `Member.cs`, `Lambda.cs`, `ThisReference.cs`, `SuspendNode.cs`, plus arithmetic, comparison, and loop nodes.
- Type-shaped nodes the lowered domain becomes: `Poly/Ast/Nodes/TypeDefinitions/TypeDefinitionNode.cs`, `MethodDefinitionNode.cs`, `PrimitiveTypeReference.cs`, and `Poly/Ast/Nodes/CompilationUnitNode.cs`.
- Primitive types the interpreter and printers understand (bool, integers, floats, text, dates, guid, bytes, and so on): `Poly/Introspection/PrimitiveType.cs`, `Poly/Introspection/TypeCategory.cs`. A syntax-tree reference to one of those: `Poly/Ast/Nodes/TypeDefinitions/PrimitiveTypeReference.cs`.
- The interpreter façade — analyze, compile (errors stop compilation), execute: `Poly/Interpretation/Interpreter.cs`. Compile walks the analyzed tree and emits a delegate; there is no separate primitive-instruction flattening step: `Poly/Interpretation/Vm/DirectVmAbiEmitter.cs` (with `DirectVmAbiEmitter.Statements.cs`, `DirectVmAbiEmitter.Invoke.cs`). Runtime state (stack, heap, registers, current node): `Poly/Interpretation/Vm/VmState.cs`, `Poly/Interpretation/Vm/VmProgram.cs`, `Poly/Interpretation/Vm/Heap.cs`. Result plus inspectable state: `Poly/Interpretation/ExecutionResult.cs`.
- Types and members the analyzer can resolve, with the CLR as the first host: `Poly/Introspection/ITypeDefinition.cs`, `Poly/Introspection/CommonLanguageRuntime/ClrTypeDefinitionRegistry.cs`.

**Roadmap phase.** Serves phase 1 (verify business behavior by simulation), phase 2 (agents review simulated outcomes), and phase 3 (heuristics extracted from model weights become trivial syntax trees the interpreter runs cheaply). The syntax tree and interpreter are **earning their place for phase 1**.

**Observation:** Domain facts (`Domain`, `Entity`, `Action`, `Policy`, `Effect`, `DomainExpression`) are also `Node` subclasses (`Poly/DomainModeling/Ontology/DomainObject.cs`), so they can carry analysis metadata. They are not the syntax tree the interpreter compiles. Meaning for a running operation is supposed to be the lowered syntax tree; the author’s tree is the input to lowering.

**Observation:** `Poly/Ast/Nodes/Comment.cs` is a no-op placeholder node whose own comment says it records “what was not lowered.” The emitter treats `Comment` as empty when used as a statement and rejects it as a value (`Poly/Interpretation/Vm/DirectVmAbiEmitter.cs`). A comment is not a simulated operation.

**Observation:** `LinqExpressionGenerator` (`Poly/Interpretation/LinqExpressions/LinqExpressionGenerator.cs`) compiles the same syntax tree to LINQ expression trees. The file states it is a test/reference path, not the canonical execution engine. A second compile path exists beside the interpreter.

---

## Principle 0.1 — The interpreter is a debugger

**Principle.** The interpreter is deliberately a debugger, not just an execution engine. Any agent, human or model, can step through, pause, and inspect running snippets in-process, like having gdb built in.

**Why it has to be true.** Simulation without inspection is a black box. Phase 1 needs to *see* that a business operation did what the author meant. Phase 2 needs agents to review outcomes on the same tree. Phase 3 needs extracted heuristics to remain steppable code, not a tensor. If a layer produces something you cannot step through and trace back to the snippet it came from, it has left the unit of meaning.

**The check every layer must pass:** can what it produces still be stepped through, and can you tell which source snippet (which syntax-tree node) you are on?

**Where it lives in code.**

- Compile mode: `Poly/Interpretation/Vm/CompilationMode.cs`. `Normal` (the default) includes debug-hook checks, AST-node tracking, and a loop-iteration sandbox. `NoDebug` omits that instrumentation.
- Before each statement in `Normal` mode, the emitter can invoke a hook with the current syntax-tree node, a span of local slots, and the heap: `DirectVmAbiEmitter.CompileStatement` in `Poly/Interpretation/Vm/DirectVmAbiEmitter.cs`. The compiled program also stores `StepNodes` and `DebugInfo` on `Poly/Interpretation/Vm/VmProgram.cs` so a debugger can map a step back to a node and name locals.
- Current node on the running state: `VmState.CurrentAstNode` in `Poly/Interpretation/Vm/VmState.cs`. Optional text trace of operations: `VmState.Trace` plus `Poly/Interpretation/Vm/VmTrace.cs`. Pause via `SuspendNode` (`Poly/Ast/Nodes/SuspendNode.cs`) and `InterpreterStatus.Suspended` / `ExecutionResult.IsSuspended`.
- Interactive stepper: `Poly/Interpretation/Vm/VmDebugger.cs`. Start pauses at the first statement; `StepOver` advances one statement boundary and reports the node plus named locals; `Continue` runs until completion or suspend. When nobody is stepping, the hook checks a flag and returns immediately.
- Tests of the hook and stepper: `Poly.Tests/Interpretation/VmDebuggerTests.cs`.

Secondary context only (naming, not a source of principles): `docs/decisions/2026-06-08-breakpoint-architecture.md`, `docs/interpretation/debugging-and-tracing.md`.

**Roadmap phase.** Serves phase 1 (a person can step a business operation), phase 2 (an agent can pause and inspect simulated outcomes), and phase 3 (extracted code stays steppable). The statement-level hook and `VmDebugger` are **earning their place for phase 1**. The “any reasonable host / any reasonable type system” ambition of introspection, and per-micro-operation interrupt machinery (a callback before every tiny low-level step) that is not actually emitted, are **only justified by phase 2/3** if they are justified at all.

**Observation:** `VmState.DebugInterrupt` is documented as a per-micro-operation callback. The emitter does not invoke it. `VmDebuggerTests.DebugInterrupt_IsNotInvokedByEmitter` records that. What you can actually step is statement-shaped syntax-tree nodes, not a bytecode program counter.

**Observation:** The 2026-06-08 breakpoint decision describes a bytecode PC set, an interrupt vector, and a PC-to-node source map. That execute loop is not in the code. Stepping is the debug hook on compiled syntax-tree statements, not PC-level breakpoints.

**Observation:** `CompilationMode.NoDebug` produces a program that cannot be stepped. Speed is optional; the default path keeps the mapping to the tree.

---

## Principle 0.2 — Every file can be edited by hand

**Principle.** The repo owner must be able to open any file cold and change it by hand without an agent.

**Why it has to be true.** A product that only an agent can change is a product nobody owns. Phase 1 needs the owner to fix a lowering rule, a printer, or a library on a Tuesday afternoon without a toolchain in the loop. The syntax tree and interpreter from Principle 0 only stay the unit of meaning if a person can read and change the code that builds and runs them.

**Where it lives in code.**

- The hand-written core is ordinary C# with no source generators (the only generated-code attribute in the repo is a test-only regex in `Poly.Tests/Style/AssertionStyleTests.cs`): syntax-tree nodes in `Poly/Ast/`, the interpreter in `Poly/Interpretation/Interpreter.cs` and `Poly/Interpretation/Vm/`, primitives in `Poly/Introspection/`.
- The authoring language is plain text (`.poly`) parsed by hand-written tables: `Poly/DomainModeling/Language/DslGrammar.cs`, `Poly/DomainModeling/Language/PolyDslParser.cs`.
- Project wiring is plain `.csproj` files listed in `Poly.slnx`; there are no checked-in generated `.g.cs` or designer files.

**Roadmap phase.** **Earning its place for phase 1.** It is also what lets phase 2 agent feedback land as ordinary, reviewable edits.

**Observation:** The C# that Poly prints for a customer (entity classes, `{Domain}DbContext.cs`, `Program.cs`, `demo.http`) is generated output, not source kept in the repo. Changing it by hand changes only that copy; the next compile regenerates it from the domain.

**Observation:** Some core files are large enough that editing them cold takes study, for example the emitter split across `Poly/Interpretation/Vm/DirectVmAbiEmitter.cs` and its partial files, and the lowering exporter split across `Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs` and its partial files.

---

## 1. What a domain is

**Principle.** A domain is an immutable library of named business facts — types, properties, stages, actions, policies, relationships, and the extension ids it lists — that exist so they can be lowered into syntax trees. It is not a process with a `Main`, and it is not itself the tree the interpreter runs.

**Why it has to be true.** The sellable object in phase 1 is working software for a business. The author needs a place to write “an Order has a Total, can Checkout only when Paid, and then becomes Shipped.” Those facts must be complete enough to become operations (syntax trees), or simulation and print have nothing honest to consume. If the domain *were* the running program, you would be executing a modeling graph instead of a snippet.

**Where it lives in code.**

- Top-level container: `Poly/DomainModeling/Ontology/Domain.cs` — name, types, imported contracts, contract bindings, and `Extensions` (the `uses` ids). Only types, imported contracts, and contract bindings are children of the domain node; the name and `Extensions` are plain properties.
- Named members share `DomainMember` / `DomainObject` (`Poly/DomainModeling/Ontology/DomainMember.cs`, `DomainObject.cs`). `DomainObject` extends `Node`, which is how analysis hangs metadata on authoring nodes.
- Entities, the stateful business objects: `Poly/DomainModeling/Ontology/Entity.cs` — properties, actions, policies, stages, entity-level subscriptions, and navigations (relationships owned by the source entity).
- Actions (named operations with parameters, effects, and guard policies): `Poly/DomainModeling/Ontology/Action.cs`. Policies (a named boolean expression): `Poly/DomainModeling/Ontology/Policy.cs`. Stages (lifecycle, including entry/exit effects and stage-scoped subscriptions): `Poly/DomainModeling/Ontology/Stage.cs`.
- Relationships as source-owned navigations: `Poly/DomainModeling/Ontology/Relationship.cs`.
- Built-in domain primitives seeded on a new domain: `Poly/DomainModeling/Ontology/PrimitiveType.cs` plus `Poly/DomainModeling/Ontology/Bootstrap/CanonicalBuiltInTypeCatalog.cs` (Boolean, Number, Text, Uuid, Binary). Bootstrap: `Poly/DomainModeling/Ontology/Bootstrap/DomainFactory.cs`.
- Authoring expressions (a second tree used *inside* policies and effect right-hand sides): `Poly/DomainModeling/Ontology/DomainExpression.cs`. Authoring effects (assign, create, invoke, stage transition, and so on): `Poly/DomainModeling/Ontology/Effect.cs`, `Poly/DomainModeling/Ontology/Effects/AssignEffect.cs`, `Poly/DomainModeling/Ontology/Effects/StageTransitionEffect.cs`.
- Another Poly domain used as a contract, not as an extension id: `Poly/DomainModeling/Ontology/Contract/ImportedContract.cs`. Filling that surface from a loaded domain: `Poly/DomainModeling/ContractFill/InternalDomainProducer.cs`.
- Inspection projections of the facts (not execution): `Poly/DomainModeling/Queries/DomainQueries.cs`.
- The authoring language that parses `.poly` text into evolution changes (facts), and prints facts back: `Poly/DomainModeling/Language/PolyDslParser.cs`, `Poly/DomainModeling/Language/DslGrammar.cs`, `Poly/DomainModeling/Language/DomainDslPrinter.cs`. Grammar engine the session holds: `Poly/Grammar/Language.cs`, `Poly/Grammar/Grammar.cs`, `Poly/Grammar/Matcher.cs`.

**Roadmap phase.** **Earning its place for phase 1.** A domain is how a business is written down so it can become working software.

**Observation:** Between a domain and the syntax-tree core there is non-trivial ceremony. The author writes `DomainExpression` and `Effect` nodes. Those are not interpreter nodes. Lowering (`DomainExpressionLoweringPass`, `EffectLoweringPass`) rewrites them into `Member`, `Assignment`, `Invoke`, `IfStatement`, `ForEachLoop`, and friends. Until that rewrite happens, the domain is not simulable as Principle 0 states.

**Observation:** Stage comments in `Stage.cs` state that parent/child stage hierarchy is not in the current language. Stages are flat on an entity.

---

## 2. Load

**Principle.** Load resolves the domain’s `uses` ids into libraries and freezes them into a compile session — language tables, expression meaning, type maps, extra analyzers, and artifact contributors — so later parse, analyze, and lower can produce syntax trees. Unknown or duplicate ids stop with an error. The domain record itself does not load code.

**Why it has to be true.** Dates, storage annotations, SQLite types, and HTTP are not core language. If loading were silent, optional, or done again inside lowering, the same domain text would mean different trees depending on who compiled it. Load is the moment the session knows *how* to turn listed ids into meaning that lowering can emit as ordinary syntax (for example `DateTime.UtcNow` rather than a private clock opcode).

**Where it lives in code.**

- Catalog of known ids: `Poly/DomainModeling/Compile/ExtensionCatalog.cs`. Core in-assembly libraries: `temporal`, `storage`, `persistence`. Product language seed is `temporal`; authoring seed adds `storage`.
- Library contract: `Poly/DomainModeling/Compile/IDomainLibrary.cs` (`Id`, `Register`, optional primitive seeds).
- Mutable assembly, then freeze: `Poly/DomainModeling/Compile/SessionBuilder.cs` (`Load`, `AddAnalyzer`, `AddArtifactContributor`, `Build`). Duplicate library id or duplicate analyzer pass name throws.
- Session construction from ids: `DomainSession.ForExtensions`, `DomainSession.Open` (ids taken from an existing domain), `DomainSession.ForSource` (peek `uses` from text, else a seed) in `Poly/DomainModeling/Compile/DomainSession.cs`. Peek: `Poly/DomainModeling/Compile/DomainCompilation.cs`.
- In-assembly libraries that register at load: `Poly/DomainModeling/Libraries/Temporal/TemporalLibrary.cs`, `Poly/DomainModeling/Libraries/Storage/StorageFacetLibrary.cs`, `Poly/DomainModeling/Libraries/Storage/PersistenceEmitLibrary.cs`. Vendor libraries: `src/Poly.Packs.Sqlite/SqlitePack.cs` (`SqliteLibrary`, id `sqlite`), `src/Poly.Packs.SqlServer/SqlServerPack.cs` (`SqlServerLibrary`), `src/Poly.Packs.MySql/MySqlPack.cs` (`MySqlLibrary`). HTTP host library: `src/Poly.DslCompiler/HttpLibrary.cs`.

**Roadmap phase.** **Earning its place for phase 1.** Temporal meaning, storage, and HTTP are how a business domain becomes sellable software rather than a sketch.

**Observation:** Load happens *before* a full parse when compiling from text (`ForSource` / `DslCompiler.OpenCompileSession` peeks `uses`, loads, then parses). The logical story is “know ids, then parse.” The code peeks just enough of the header to load, then parses with those tables.

**Observation:** The compiler catalog (`DslCompiler`) adds sqlite, sqlserver, and http on top of `ExtensionCatalog.Core`. A core-only session cannot resolve `uses http` or `uses sqlite`.

---

## 3. Analyze

**Principle.** Analyze walks the domain (as a node tree) and hangs proven facts on those nodes — a catalog of types and operations, capabilities, subscription dispatch plans, storage mapping when a persistence library is loaded, an HTTP flag when `uses http` is loaded. Lowering and host files *read* that metadata. The syntax tree that will be simulated still has no metadata types in it.

**Why it has to be true.** Lowering needs to know which action is legal in which stage, which navigation is a collection, which property is unique, and which operations an HTTP host may name. If each consumer re-derived that from the raw domain, simulate and print would drift. Analysis is the one proof pass. It does not replace lowering: it does not produce the running snippet.

There is a second analyze, on the *syntax* tree, immediately before the interpreter compiles (types, control flow, definite assignment, constant folding, and so on). That one is for the interpreter, not for the domain catalog.

**Where it lives in code.**

- Framework (any `Node`, including domain nodes): `Poly/Analysis/Analyzer.cs`, `Poly/Analysis/AnalyzerBuilder.cs`, `Poly/Analysis/AnalysisContext.cs`, `Poly/Analysis/AnalysisResult.cs`, `Poly/Analysis/INodeAnalyzer.cs`, `Poly/Analysis/IAnalysisMetadata.cs`. Passes do not mutate the tree; they can register a replacement node: `Poly/Analysis/NodeReplacementMetadata.cs`. Constant folding is an example on the interpreter pipeline: `Poly/Interpretation/Analysis/ConstantFolding/ConstantFoldingPass.cs`.
- Domain pipeline construction and pass order: `Poly/DomainModeling/Analysis/DomainModelAnalyzer.cs` (`UseDomainModelAnalysisPipeline`). Product door: `DomainSession.Analyze` in `Poly/DomainModeling/Compile/DomainSession.cs`, which binds the session onto the domain via `Poly/DomainModeling/Analysis/RuntimeAnalysisCache.cs`.
- Structural checks, then the name catalog: `Poly/DomainModeling/Analysis/StructuralDomainAnalyzer.cs`, `Poly/DomainModeling/Analysis/DomainCatalogPass.cs`. Effective actions/policies: `Poly/DomainModeling/Analysis/CapabilityAnalyzer.cs`. Subscription dispatch plans the runtime and the printer both read: `Poly/DomainModeling/Analysis/RuntimeContractAnalyzer.cs`.
- Persistence overlay (not in the core list; a library appends it): `Poly/DomainModeling/Analysis/StoragePass.cs`, `Poly/DomainModeling/Analysis/PersistenceSurfaceMetadata.cs`. HTTP flag: `Poly/DomainModeling/Analysis/HttpSurfaceMetadata.cs`.
- Interpreter analyze of a syntax tree (the compile door): `Interpreter.Analyze` / the fourteen-pass builder at the top of `Poly/Interpretation/Interpreter.cs`. `Interpreter.Compile` refuses to emit if that analysis reported errors.

**Roadmap phase.** **Earning its place for phase 1.** Without a catalog and the later analysis metadata (the facts analysis attaches to nodes), lowering cannot produce complete operation trees, and HTTP/persistence cannot know what to emit.

**Observation:** Two analysis pipelines share one framework and two different trees. Domain analysis walks `Domain` / `Entity` / `Effect`. Interpreter analysis walks `TypeDefinitionNode` / `Block` / `Invoke`. The check from Principle 0.1 applies to the *syntax* tree. Domain analysis does not by itself give you a steppable snippet.

**Observation:** `DomainModelAnalyzer.Analyze` is a compatibility door that forwards to the cache (the bound session if `DomainSession.Analyze` already ran, otherwise a core-catalog fallback). Product analyze is the session method.

**Observation:** `DomainSession.Lower` does not itself refuse a domain analysis that has errors. Callers such as `DomainEvolution.Apply` and `DslCompiler` stop before lower when analysis failed. The interpreter compile door *does* refuse a dirty syntax-tree analysis.

---

## 4. Lower

**Principle.** Lower turns an analyzed domain into syntax trees: type definitions with method bodies for actions, policies, constructors, entry/exit, subscriptions, and store jobs (`Create`, `CreateIn`, `EnsureUnique`, and the like). That set of trees is what simulate and print are supposed to consume. Authoring expressions and effects are parse output, not execute input.

**Why it has to be true.** Principle 0 says the syntax tree is the unit of meaning. If execute still walked `Effect` objects, or print built a different tree than simulate, “the domain works” would mean two different programs. Lowering is the one translation. It may *read* analysis metadata; the tree it emits is generic syntax (assignments, calls, `this`, loops), not domain types.

**Where it lives in code.**

- Product door: `DomainSession.Lower` → `RuntimeAnalysisCache.GetOrLower` → `DomainProgramProjection.ToSyntax` (`Poly/DomainModeling/Compile/DomainSession.cs`, `Poly/DomainModeling/Analysis/RuntimeAnalysisCache.cs`, `Poly/DomainModeling/Lowering/DomainProgramProjection.cs`).
- Projection builds entity types, stage enums, value types, `DomainResult` scaffolding, contract adapters, and operation methods. The heavy lifting of members and bodies is `Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs` (including `DomainToCSharpExporter.Actions.cs` and `DomainToCSharpExporter.StoreBind.cs`). Despite the exporter name, the output of this step is still syntax-tree `TypeDefinitionNode`s, not C# text.
- Expression lowering (authoring `DomainExpression` → syntax `Node`): `Poly/DomainModeling/Lowering/DomainExpressionLoweringPass.cs`. Effect lowering (authoring `Effect` → syntax `Node`): `Poly/DomainModeling/Lowering/EffectLoweringPass.cs`. Context flags such as whether the body uses `this` or a parameter named `entity`: `Poly/DomainModeling/Lowering/LoweringContext.cs`.
- Library-owned expression lowering (clocks, date arithmetic) registered at load, not hard-coded in the core switch: `Poly/DomainModeling/Libraries/Temporal/TemporalLowering.cs`, tables on `Poly/DomainModeling/Meaning/ExpressionMeaning.cs`.
- Cache at `GetOrLower` also stores subscription effect bodies, entry/exit bodies and segments, and policy bodies so named execute can bind a tree instead of lowering again at invoke time (`RuntimeAnalysisCache.cs`).

**Roadmap phase.** **Earning its place for phase 1.** This is the step that makes a business domain into snippets the interpreter can run and the printer can sell as C#.

**Observation:** Lowering still has a twin. Export-shaped bodies use `this` (`UseThisReference: true`) so C# prints `this.Checkout()`. Some cached simulate bodies (policies, subscription effect-only trees) are rooted on a parameter named `entity` (`UseThisReference: false`). `RuntimeAnalysisCache` comments call the policy and subscription split a residual twin: module bool methods stay `this` for print; the VM path for quantifiers uses the parameter-rooted tree.

**Observation:** Named-action execute is written to bind `MethodDefinitionNode.Body` from the cached module and not to call `LowerActionBody` at invoke time (`DomainEntityInstance.ExecuteEffectList` in `Poly/DomainModeling/Runtime/DomainEntityInstance.cs`). `LowerActionBody` still exists on `EffectLoweringPass` and is used *while* `GetOrLower` builds those cached trees. Execute-time re-lower of named actions is what the runtime comments refuse; the authoring effect list is still the input to that compile step.

**Observation:** Before the interpreter runs an export-shaped body against a dictionary-backed instance, `BindThis` / `BindModuleMethodBody` rewrite the tree (`DomainEntityInstance.cs`). Stepping then happens on the *rewritten* tree, not on the node identities the C# printer used. The mapping back to the author’s `.poly` snippet is therefore: `.poly` → domain facts → lowered syntax → (optional rewrite) → debug hook node.

---

## 5. Interpret and print

**Principle.** Interpret compiles a lowered syntax tree and runs it in the interpreter. Print walks the same kind of tree and emits C# text. Both are consumers. Neither is allowed to be a second meaning of the domain.

**Why it has to be true.** Phase 1 sellability is “we simulated Checkout, and here is the C# that is that operation.” If print invented behavior the tree does not contain, or simulate ran a host prelude the tree does not name, the customer program and the simulation would be different products.

**Where it lives in code.**

- Interpret: `Interpreter.Compile` then `Interpreter.Execute` (`Poly/Interpretation/Interpreter.cs`). Domain-bound simulate of a named action or policy: `DomainEntityInstance.InvokeAction` / `EvaluatePolicy` in `Poly/DomainModeling/Runtime/DomainEntityInstance.cs`, which compile cached trees with a type-definition provider and pass the instance as `This` (`SetArgs`). Named calls that have no CLR `MethodInfo` (actions, store jobs) go through `InvokeNamed` (`DomainEntityInstance.InvokeNamed.cs`). The instance is also `IDictionary<string, object?>` so reading or writing a member on `This` reads or writes that dictionary of property values (`DomainEntityInstance.Dictionary.cs`).
- Scratch directory bound into those trees: `Poly/DomainModeling/Runtime/DomainInstanceStore.cs` (`Create`, `CreateIn`, `EnsureUnique`, `Link`, subscription fan-out on `NotifyTransition`). Success/failure object the trees return: `Poly/DomainModeling/Runtime/DomainResult.cs`. Host-shaped methods the dictionary instance actually implements (`Notify`, which calls the store’s `NotifyTransition`; `EnsureUnique`; related-set probes): `DomainEntityInstance.HostAbi.cs` and the empty method slots on the runtime type-def in `DomainEntityInstance.Runtime.cs`.
- Print of the lowered types: `DomainSession.Emit` runs `Lower`, optionally runs interpreter analysis on a `CompilationUnitNode` of those types, then `Poly/Interpretation/CSharp/CSharpGenerator.cs` per entity (`Entity.cs`, `Poly.Types.cs`).
- MCP harness tools that ask interpret to run a named operation on a store instance: `Poly.Mcp/Tools/RuntimeTool.cs` (`invoke_action`, `create_instance`, `link_instances`), `Poly.Mcp/Tools/DomainTools.cs` (`evaluate_policy`). A DSL-fragment probe that is *not* named-policy simulate: `Poly.Mcp/Tools/OracleTool.cs`.

**Roadmap phase.** **Earning its place for phase 1.** Simulation is how you verify business behavior before you sell the printed C#. Print is how that behavior becomes working software.

**Observation:** `DomainEntityInstance` plus `DomainInstanceStore` is a scratch bind: dictionary `This`, string `CurrentStage`, and store links. It is the path MCP uses. It is not the generated CLR type a customer would construct. Default-value evaluation for `Create` can resolve clocks and literals through session meaning *without* going through `Interpreter` (`EvaluateDefaultValue` in `DomainEntityInstance.cs`). That path is not steppable as a syntax snippet.

**Observation:** C# store-job methods the exporter adds include `EnsureUnique` as a body that always returns `DomainResult.Success` (`DomainToCSharpExporter.StoreBind.cs`). Simulate’s `EnsureUnique` talks to `DomainInstanceStore`. Unique indexes on the printed side are treated as a persistence-schema concern. Create factories on the printed side may still call `Stay.Create` as the host bind of a job the tree names. Simulate and print therefore do not always execute the same host implementation of a named job.

**Observation:** Policy evaluate uses a cached parameter-rooted tree (`TryGetPolicyBody`). Printed policy methods are `this`-shaped bool methods on the type definition. Same rule, two trees.

**Observation:** Generated C# that you then run under an ordinary CLR debugger is steppable as C#, not as `VmDebugger` nodes. The Principle 0.1 mapping “hook fires with the syntax-tree node” applies to interpreter simulate, not to the compiled customer process, unless you keep running the interpreter.

---

## 6. Libraries and extensions

**Principle.** A library is a named bundle that registers into a compile session: extra primitives, expression meaning (so lowering emits ordinary syntax for clocks and dates), type maps, optional analysis passes appended after the core list, and optional extra files. The domain only stores ids. Spell stays one `.poly` language.

**Why it has to be true.** Phase 1 needs dates, columns, SQLite, and HTTP without forking the interpreter or adding domain opcodes. If each of those grew a dialect or a private evaluator, Principle 0 would die. Libraries must produce or consume syntax trees (or metadata that lowering reads), not a parallel language.

**Where it lives in code.**

- Contract and session tables: `IDomainLibrary`, `SessionBuilder`, `ExpressionMeaning` (`Poly/DomainModeling/Meaning/ExpressionMeaning.cs`) — rewrite, lowering, inference, checks, defaults, assign conversions.
- Temporal: seeds Date/Time/DateTime/Duration, folds, maps, and lowering to BCL members such as `DateTime.UtcNow` (`TemporalLibrary.cs`, `TemporalLowering.cs`).
- Storage facets (`column` / `table` annotations): `StorageFacetLibrary.cs`. Generic persistence flag plus `StoragePass`: `PersistenceEmitLibrary.cs`.
- Vendor maps: `SqliteLibrary`, `SqlServerLibrary`, `MySqlLibrary` as above. HTTP flag pass: `HttpLibrary.cs`.
- Grammar the session holds after load (parse/print tables, not a second IR): `Poly/Grammar/Language.cs`. Product table: `Poly/DomainModeling/Language/DslGrammar.cs`.

**Composition.** Libraries may depend on other libraries, but every dependency chain ends at core. Here “core” means the `Poly` project (`Poly/Poly.csproj`), which holds the syntax tree, the interpreter, and the domain pipeline.

- Project references in code: `Poly/Poly.csproj` references no other project. `src/Poly.Packs.Sqlite/Poly.Packs.Sqlite.csproj`, `src/Poly.Packs.SqlServer/Poly.Packs.SqlServer.csproj`, `src/Poly.Packs.MySql/Poly.Packs.MySql.csproj`, and `Poly.Mcp/Poly.Mcp.csproj` each reference only `Poly`. `src/Poly.DslCompiler/Poly.DslCompiler.csproj` references `Poly`, the SQLite pack, and the SQL Server pack. `Poly.Tests/Poly.Tests.csproj` references all of the above. `Poly.Benchmarks/Poly.Benchmarks.csproj` references only `Poly`. Every project chain ends at `Poly`, and there are no cycles.
- Library registration in code: `IDomainLibrary` (`Poly/DomainModeling/Compile/IDomainLibrary.cs`) has `Id`, `Register`, and `PrimitiveSeeds`. It has no way to declare that one library needs another.

**Observation:** Library-to-library dependencies exist only implicitly. The HTTP library (`src/Poly.DslCompiler/HttpLibrary.cs`) registers only an HTTP flag, but `DslCompiler` refuses to emit `Program.cs` unless storage mapping metadata is present, which only a persistence or vendor library (`persistence`, `sqlite`, `sqlserver`, `mysql`) produces. Nothing in the library record states that.

**Observation:** `SqliteLibrary` and `PersistenceEmitLibrary` both register `StoragePass` and `PersistenceSurfacePass` themselves instead of depending on a shared library. Because `SessionBuilder` rejects a duplicate analyzer pass name, loading both ids into one session stops at load. `DslCompiler.OpenCompileSession` adds `persistence` only when no vendor id is already listed, which keeps the two apart.

**Observation:** The MySQL pack is referenced by tests but not by `Poly.DslCompiler`. The compiler’s built-in catalog resolves `sqlite`, `sqlserver`, and `http` on top of core; `mysql` resolves only when a caller passes that library in as an extra library.

**Roadmap phase.** Temporal, storage, vendor persistence, and HTTP are **earning their place for phase 1**. The generic grammar engine as a pattern-table machine for *any* language-shaped token stream, and introspection’s dormant “any type system on any platform” surface, are **only justified by phase 2/3** beyond the `.poly` + CLR work phase 1 actually ships.

**Observation:** Libraries append analyzers after the core domain list. They do not splice the middle. Storage mapping is that overlay. Missing storage metadata later causes persistence/HTTP emit to throw, rather than inventing a mapping in the printer.

---

## 7. Sessions

**Principle.** A compile session is the loaded libraries plus the domain they apply to: one place to parse, analyze, lower, and print. A conversation session (MCP) *holds* a compile session, a revision, and scratch instances. They are different objects. Simulate in a conversation still means the interpreter on a lowered tree with a caller-supplied instance, not a private evaluator.

**Why it has to be true.** If “session” meant both “which libraries this domain loaded” and “which chat we are in,” load and authoring would collapse. Phase 1 needs a stable compile session so the same `.poly` becomes the same trees. The harness exists so a person or model can evolve facts and ask the interpreter to run a named operation. The harness is not the customer API.

**Where it lives in code.**

- Compile session: `Poly/DomainModeling/Compile/DomainSession.cs` — `Language`, `Meaning`, `TypeMaps`, `Artifacts`, `Analyze`, `Lower`, `Emit`, `WithDomain` (reload if `uses` changed).
- Conversation session: `Poly.Mcp/Sessions/McpSessionStore.cs` — `Domain`, latest analysis, revision, the `DomainSession` named `Modeling`, plus `InstanceStore` / `InstanceMap` created on first `create_instance`. Evolve holds a lock, runs `DomainEvolution`, and on success rebinds `Modeling.WithDomain`.
- Tools: `Poly.Mcp/Tools/DomainTools.cs` (authoring: `apply_dsl`, overview, export), `RuntimeTool.cs` (instances and invoke), `OracleTool.cs` (read-only probes).

**Roadmap phase.** The compile session is **earning its place for phase 1**. The MCP conversation session is **earning its place for phase 1** as the authoring and simulation harness, not as the sold product. Agent-review loops that feed outcomes back into the product automatically are **only justified by phase 2** and are not a coded product loop today.

**Observation:** MCP simulate is `DomainEntityInstance` on the scratch store, which compiles lowered bodies in-process. That *is* the interpreter, with a dictionary `This`. It is also extra machinery (instance map, JSON args, entity-typed args rebound from instance ids in `RuntimeTool.BindEntityTypedActionArgs`) between the agent and the syntax tree.

---

## 8. Artifacts

**Principle.** Artifacts are extra generated files contributed after a successful analysis — host files, HTTP `Program.cs`, a `demo.http` script, or anything a library registered — not a second simulate path. The privileged artifact for operation meaning is still the lowered syntax trees (printed as entity C# by `session.Emit`). Other artifacts are supposed to *call* those operations, not re-walk effects.

**Why it has to be true.** Phase 1 sellable software is more than entity classes: a process has to start, a database context has to exist. Those files are delivery. If they secretly reimplemented Checkout by walking the domain graph, print and simulate would diverge.

**Where it lives in code.**

- Contributor contract: `Poly/DomainModeling/Compile/IArtifactContributor.cs`. Registration: `SessionBuilder.AddArtifactContributor`. Session list: `DomainSession.Artifacts`.
- Entity C# from the lowered types: `DomainSession.Emit` (not an `IArtifactContributor`; it *is* print of the syntax trees).
- Compiler consume step after `session.Lower` / `session.Emit`: `src/Poly.DslCompiler/DslCompiler.cs` concatenates session contributors and extra contributors. HTTP host contributor: `MinimalApiHostArtifactContributor` in `src/Poly.DslCompiler/MinimalApiGenerator.cs` (`Program.cs` via `CSharpGenerator` on a compilation unit, plus `demo.http` from `src/Poly.DslCompiler/HttpFileGenerator.cs`).
- Persistence file today is built inside `DslCompiler` when the persistence metadata is present: `src/Poly.DslCompiler/DbContextGenerator.cs` produces a syntax-tree compilation unit, then `CSharpGenerator` prints it.

**Roadmap phase.** **Earning its place for phase 1.** Extra files are how a lowered domain becomes a runnable service.

**Observation:** `session.Lower` returns `IReadOnlyList<TypeDefinitionNode>`. It does not return, or catalog, the full set of files. Artifact contributors run later in `DslCompiler`. There is no “artifact-set catalog on the session after Lower that errors if empty.” Contributors may return an empty list.

**Observation:** `DbContextGenerator` and `MinimalApiGenerator` take the `Domain` plus analysis metadata (storage, behavior, aggregate). They build *new* syntax trees for `DbContext` and `Program.cs`. HTTP then checks that every named action already exists as a method on the lowered module (`DslCompiler.RequireHttpActionsInModule`). The host is gated on the module for *names*; the host source itself is generated from domain facts and analysis metadata, not by printing the operation bodies again.

**Observation:** `demo.http` is ordinary text (`HttpFileGenerator`), not a syntax tree, and is not steppable in the interpreter.

---

## 9. Persistence and HTTP delivery

**Principle.** Persistence and HTTP are opt-in libraries. They publish analysis metadata (“this unit should emit a DbContext”; “this unit should emit a process door”). Delivery files bind to operations the lowered syntax trees already name. They are not a second interpreter, and core compile does not emit `Program.cs` unless `uses http` (or an honest load of that id) published the HTTP metadata.

**Why it has to be true.** Phase 1 revenue is working business software, which for this repo currently means persisted entities and an HTTP API. Those must remain *delivery* of the same operations you simulated. A compiler flag that invents a host without the domain listing it would make “what the domain is” depend on the invoke, not on the facts.

**Where it lives in code.**

- Flags: `PersistenceSurfacePass` / `PersistenceSurfaceMetadata`, `HttpSurfacePass` / `HttpSurfaceMetadata`. Storage structure: `StoragePass` producing `StorageMappingMetadata`.
- `DslCompiler.CompileCore`: if persistence metadata is present, require storage mapping and add `{Domain}DbContext.cs`. If HTTP metadata is present, require storage, behavior, and aggregate metadata, require module methods, then add `Program.cs` and `demo.http`. `CompileMode` seeds language and optionally a vendor id; it does not seed `http`.
- HTTP library: `src/Poly.DslCompiler/HttpLibrary.cs`. Minimal API generator: `src/Poly.DslCompiler/MinimalApiGenerator.cs`.
- Runtime persistence of *instances* during simulate is the in-memory `DomainInstanceStore`, not Entity Framework. Unique collision and create-in go through that store when the interpreter invokes the named jobs.

**Roadmap phase.** **Earning its place for phase 1.** This is the sellable web-API shape. Other process shapes (CLI, gRPC, OpenAPI as further libraries) are not implemented; they would be phase 2 expansion.

**Observation:** The HTTP and DbContext generators are opaque relative to Principle 0.1. You can step `Program.cs` only after print, as C#. You cannot `VmDebugger.StepOver` a generated route. The operations those routes *call* are steppable in the interpreter when you simulate the named action on a bound instance.

**Observation:** On simulate, the instance’s `Notify` calls `DomainInstanceStore.NotifyTransition`, which fans out by walking links and dispatch plans (`RuntimeContractAnalyzer` metadata). Generated C# emits subscriber lists and `When…` handlers from the same dispatch plan (`DomainToCSharpExporter`). The plan is shared; the bind (in-memory links vs EF navigations) is not the same object.

---

## 10. Evolution

**Principle.** Evolution is how a domain changes: a batch of immutable facts is applied to a copy, the proposed domain is analyzed with the compile session, and on error the original snapshot is kept. Evolution produces a new domain (new input to lower), not a new interpreter. You do not edit the graph in place.

**Why it has to be true.** Phase 1 authoring is iterative — add a property, add Checkout, fix a policy. If mutation skipped analysis, you could lower an illegal domain. If mutation edited nodes in place, analysis metadata and the lowered cache would lie. Immutability plus one analysis gate is what makes “reload the session and lower again” well-defined.

**Where it lives in code.**

- Entry: `Poly/DomainModeling/Evolution/DomainEvolution.cs` — `Apply(changes, priorAnalysis, session)`, fluent `Evolve()` builder. Session defaults to `DomainSession.ForExtensions(domain.Extensions)` when omitted.
- Change types: `Poly/DomainModeling/Evolution/DomainChange.cs` (and the concrete changes in that file). Mutable working copy used only inside a batch: `DomainMutationContext` (same folder).
- Result: `Poly/DomainModeling/Evolution/EvolutionResult.cs` — new root or original root, analysis, trace, mutation errors. “Rollback” is discarding the proposal; the old tree is unchanged.
- Parser output is a list of changes, not a `Domain` directly: `PolyDslParser.Parse`, then `DomainEvolution.Apply` (see `DslCompiler.CompileCore` and MCP `apply_dsl`). Seed extension facts: `DomainCompilation.WithSeed`.
- After a successful MCP evolve, `McpSessionStore` stores the new root and `modeling.WithDomain`. The runtime cache keys off the `Domain` instance, so a new root is a new analyze/lower.

**Roadmap phase.** **Earning its place for phase 1.** This is the authoring loop that feeds lowering.

**Observation:** Evolution analyzes the *domain* tree. It does not call `session.Lower`. Lower happens when simulate, print, or the compiler asks. Until then there is no new steppable snippet.

**Observation:** `DomainFactory.Create` itself goes through evolution to add built-in primitives (and, when the language session is loaded, temporal primitive seeds). Bootstrap is not a side channel around the gate; if configure-pass analysis fails, the factory keeps the builtins-only root and drops the failed extras.

---

## Ideas in docs, not in code

These are named in documents (or in stale document claims) and have no matching implementation in the tree that was read for this file. One line each.

- Bytecode program-counter breakpoints: a `BreakpointPCs` set, interrupt vector 1, and a PC-to-node source map (`docs/decisions/2026-06-08-breakpoint-architecture.md`).
- `DebugInterrupt` actually invoked before every micro-operation of the compiled delegate (`docs/interpretation/debugging-and-tracing.md`; `docs/CORE.md` §3.7).
- `TryBeginAnalyzerVisit` as part of the analysis pass contract (`docs/CORE.md` §3.1; `INodeAnalyzer` in code has `Analyze` and `PassName` only).
- Portable bytecode serialization so compiled programs can be cached, shipped, or resumed across processes (`docs/decisions/2026-06-08-bytecode-serialization.md`).
- A peephole optimizer over a bytecode instruction stream (`docs/decisions/2026-06-08-peephole-optimizer.md`).
- A permission table that sandboxes host method calls from untrusted macros (`docs/decisions/2026-06-08-sandboxing-approach.md`).
- After Lower, a catalog of every produced artifact held on the session, which errors if empty when required (`docs/plans/session-lower-plan-2026-09-21.md` on `origin/ontology/talk-alignment-2026-09-21`).
- Lower itself running every library producer into one artifact set, with “compilation unit” meaning a library (`docs/plans/session-lower-abstractions-2026-09-21.md` on that same branch).
- `uses cli`, OpenAPI, and gRPC as further host libraries (`docs/plans/pipeline-transformation-2026-09-04.md`).
- Transport analysis (`TransportPass` / transport metadata) as an always-on domain pipeline surface (`docs/domainmodeling-capability-inventory.md`).
- Link and Unlink as effect types that lower into the operation tree (the capability inventory records them as store operations only).
- Virtual-actor lowering: grains instead of type-definition syntax trees (`docs/plans/archive/experiments/DOMAIN_ACTOR_LOWERING_PLAN.md`).
- A knowledge pipeline where a model discovers a pattern once, it becomes a reusable macro, then native code (`docs/decisions/2026-05-31-neurosymbolic-platform-vision.md`).
- A Synthesis / macros module that the VM validates (`docs/CORE.md` §2; no `Synthesis` code in the tree).
- Parent/child stage hierarchy (`Stage.cs` states it is not on the current language surface).
