<!-- Copied 2026-10-10 from the bot box (analysis-review/findings.md). Harness and raw files under /workspace/analysis-review/ were scratch on that box and are not in the repo. Line numbers are as of master 67427b72; re-check at slice start. -->

# Poly analysis-systems review (master 67427b72, 2026-10-10)

Scope: every analyzer that runs on the 16 maintained sample domains (`docs/probes/**`, `demo/live/**`, a superset of the 10 EmitGolden domains), plus the unmaintained archive (`docs/plans/archive/**`, 256 files) for signal.
Harnesses (scratch, not committed): `/workspace/analysis-review/{vmcheck,exp,emitcheck,roslyn}`. Raw output: `raw.tsv` (live), `archive.tsv`, `emit-live.txt`, `emit-archive.txt`.
Not run: the TUnit suite itself (`VmAnalyzerReportTests` etc.). My harness copies `VmAnalyzerReportTests.Measure` line for line, and it reproduces the pinned split: 15 domains with VM Errors, 1 with domain Errors, 0 clean.

## 1. Analysis systems and where they surface

| System | Codes | Where it surfaces today |
|---|---|---|
| Domain analysis (`Poly/DomainModeling/Analysis/*`, about 20 analyzers) | DM* (DMSEM, DMEFF, DMAGG, DMBEH, DMAS, DMSYS, DMSS…) | Errors block Lower/Emit (`DomainModelAnalyzer.ThrowIfHasErrors`). Warnings and hints go only into the `analysis-report` catalog artifact and MCP. No test asserts on them per sample. |
| VM analysis (`Interpreter.Analyzer`: `SyntaxTypeCompatibilityAnalyzer` → `VmTypeCompatibility`; `TypeAndMemberResolutionPass` → uncoded "does not contain a member") | VmTypeCompatibility, (none) | `DomainSession.Emit` (`DomainSession.cs:281-283`) analyzes the whole lowered module, registers a `vm-analysis-report` artifact, then passes the analysis to `CSharpGenerator`. **`ThrowIfVmHasErrors` (`:385`) is never called**, so Emit stays fail-open. Nothing reads the report: no MCP tool, and `catalog.golden` lists only the artifact id. `VmAnalyzerReportTests` **pins the 15 erroring domains as an allowlist**, and `AnalysisReportArtifactTests` pins smoke.poly at exactly 9 VM errors. H2 and K6 deliberately left it unwired ("DomainResult analyzer gaps" out of scope), and no slice owns making the samples VM-clean. |
| Catalog-reference report (H3) | Dangling / WrongType | 0 findings on every sample. |
| Runtime per-body VM compile (`DomainEntityInstance.CompileBody` → `Interpreter.Compile`) | same VM codes | Fails closed, but it uses a richer provider (`DomainResultTypeProvider` plus module stubs), so it doesn't hit the Emit-path gaps. |
| Roslyn compile of emitted C# | CS* | Only some goldens, via `ExportedCSharp` helpers. Non-golden probes are not compiled. |

**Why the VM errors are ignored:** 100% of valid samples have them, so wiring the throw would break every golden. The H2 slice froze them into an allowlist instead. About 99.8% of them are analyzer false positives (proven below), so the real ones are buried.

## 2. Counts (16 live samples)

| Source | Error | Warning | Hint |
|---|---|---|---|
| VM analysis (Emit path) | **1988** (15 domains; crm 807, hotel 482, university 217…) | 0 | 0 |
| Domain analysis | 1 (expected) | 6 | 135 |
| Catalog-reference | 0 | 0 | 0 |
| Roslyn on emitted C# (all 15 emitting samples) | 0 | 4 (2 domains not in goldens) | n/a |

71% of VM errors sit in the K0 StoreBind methods (BindProbeCreate 788, BindCreate 550, BindCreateIn 71). Archive: 139 domains emit, 28 of those emit C# that does not compile, and 72 fail domain analysis. 45 fail to parse.

## 3. Triage

| # | Code | Sev | Count | Example | Message | Verdict | Owning code | Codegen match |
|---|---|---|---|---|---|---|---|---|
| V1 | VmTypeCompatibility | Error | 739 | orders Order.Pay | no matching member 'Success' on 'DomainResult' (0 or 1 args) | **Analyzer FP.** `AstTypeRegistry.Add` keys by `FullName`, so `DomainResult<T>` overwrites `DomainResult`. I confirmed it: dropping the generic node removes all 0-arg errors. The 1-arg ones fail because `T` doesn't bind. | `Poly/Interpretation/Analysis/Semantics/TypeDefinitionNodeAnalyzer.cs:56` | no (G12/D7 touch the same type) |
| V2 | VmTypeCompatibility (+uncoded) | Error | 644 | orders Order field init / AttachItems | no matching constructor for 'List<X>' with 0 args; 'List<X>' has no member 'Add' | **Analyzer FP.** `AstCollectionTypeDefinition` exposes `Methods => []` and `Constructors => []`. | `TypeDefinitionNodeAnalyzer.cs:848-849` | no |
| V3 | VmTypeCompatibility | Error | 488 | orders OrderItem.BindCreate | no matching member 'Create' on 'Order' for invoke with 6 args (also CreateOrder, CreateSection…) | **Analyzer FP.** The static `Create` exists with matching arity, but the args (`new List<X>()` resolved as IList, nullable/optional params) aren't treated as assignable. Roslyn compiles it. | `SyntaxTypeCompatibilityAnalyzer.cs:229-258` (`HasPlausibleMemberOverload`) and `TypeDefinitionExtensions.IsAssignableFrom` | located in K0/G1/G2 code, but the diag doesn't flag those bugs |
| V4 | VmTypeCompatibility | Error | 60 | every Poly.Types | no matching constructor for 'DomainResult'/'DomainResult<T>' | Analyzer FP (same as V1, plus private ctor) | as V1 | no |
| V5 | VmTypeCompatibility | Error | 54 | checkout `new Order(total)` vs ctor(Int64 total) | no matching constructor for '<Entity>' with N args | **Analyzer FP.** It fails even on an exact single-arg match, and it isn't about visibility (making the ctor public changes nothing). Likely cause: `ConstructorInvocationSemanticResolver` returns null when an arg type is null, here a parameter reference in a static method, whereas member invokes tolerate null. | `TypeAndMemberResolutionPass.cs:561-581` | no |
| V6 | VmTypeCompatibility/uncoded | Error | 3 | entry-transition-in-first-stage Z.NoteAutomaticStage | 'ToString' on 'ZStage'; ctor 'InvalidOperationException' | Analyzer FP (AST enum has no inherited members; CLR exception not resolvable without a provider). The method itself is the G5 throw path. | `TypeDefinitionNodeAnalyzer` / no provider in Emit | near G5 |
| D1 | DMEFF004 | Hint | 56 | crm AddContact(name, email) | parameter declared but never referenced | **Analyzer FP for `create in {…}`.** `CollectFromEffect` skips `CreateEntityInRelationshipEffect` initializers. It fires on nearly every create-in action. | `Poly/DomainModeling/Analysis/EffectAnalyzer.cs:429-431` | new |
| D2 | DMAGG001 | Warning | 6 | orders OrderItem, warehouse Delivery, crm QuoteLine | non-root entity with no aggregate parent | **FP for grandchildren** (3 of 6): only root parents are accepted. Lead, Tr and Host are plausible (no incoming relationship). | `OwnershipAggregatePass.cs:105` | new |
| D3 | DMBEH001 | Hint | 40 | hotel Reservation.Cancel | action has no require gates | Expected (advisory) | AuthoringSuggestionAnalyzer | — |
| D4 | DMAS001 | Hint | 37 | Gadget | entity has no stages | Expected (advisory) | AuthoringSuggestionAnalyzer | — |
| D5 | DMSYS001 / DMSS002 | Hint | 1 / 1 | warehouse Truck.Dispatch / peer-tracking Tr | coverage gap / replay idempotency | Expected | RuleCoverage / Subscription | — |
| D6 | DMSEM003 | Error | 1 | nested-invoke-type-mismatch | Gadget→Widget | Expected (invalid by design) | — | — |
| R1 | CS8602 (Roslyn) | Warning | 1 | peer-tracking-transition Tr.cs:51 | `this.Tracks.RegisterTrBSubscriber(this)` with `Paper? Tracks` | **Real bug.** `Tr.Create()` with no paper throws NRE in the ctor (`InitializeSubscriptions` runs at the end of the ctor). It also never registers if Tracks is linked later. | `DomainToCSharpExporter.cs:728-733, 858` | new |
| R2 | CS8618 / CS8625 (Roslyn) | Warning | 3 | entry-transition-in-first-stage Z.cs:13, :323 | non-nullable `HashSet<string> _automaticStageChain` uninitialized, set to null | **Real (production-quality).** It should be `HashSet<string>?`. | `DomainToCSharpExporter.Actions.cs:1052-1055` | new (G5 neighbourhood) |

### VM true positives that exist but are drowned (archive)
In 13 of the 28 archive domains that emit non-compiling C#, the VM report *did* carry a non-FP error that predicts the break. Examples:
- `assign CheckIn to now` (lowercase) passes domain analysis and lowers to `this.now`, giving CS1061. The VM says "'Booking' does not contain a member named 'now'". Files: `agent-a/assign-now.poly`, `multiinit*.poly`, `loans2.poly`, `discovery-dates/date-edges.poly`, `fleet-eval/03-expression-type/expr-f2-keyword-assign.poly`, `expr-f9-unknown-value.poly` (`this.Bogus`).
  **Real domain-analyzer gap:** unknown bare identifiers in assign values aren't rejected. New finding.
- `expr-f10-invoke-enum-literal.poly`: `SetStatus(Bogus)` gives CS1503 (VM flagged it too).

The other 15 break with the VM silent on the real cause. These are codegen bugs on edge shapes, all new except where noted:
- Self-referencing many-relation passes `T` where `IEnumerable<T>?` is expected (CS1503: `deg-selfref`, `deg-duplicates`, `selfrel-createin`). Near G6.
- An entity named `namespace` isn't escaped (CS1001: `isolated-keyword-ident`, `export-collisions`, `kw-export`, `fileops`).
- A decimal literal goes into `long` (CS0266: `expr-f7-decimal-literal`, `doc-14b-decimal-assign`). This is D8, owned by C8-num.
- A string default on an enum parameter (CS1750: `isolated-enum-default`).
- A user action named `Create` collides with the factory (CS1501/CS0120: `isolated-two-creates`, `selfnav`, `deg-empty`). Related to K0.
- `return expr` in a void `When…` handler (CS0127: `agent-c/subinvoke`). Near D15.

The archive is unmaintained, so these need re-probing on current syntax before they're slotted.

## 4. Recommendations
1. Fix V1 (key the registry by name plus generic arity) and V2 (give `AstCollectionTypeDefinition` List ctor/Add/Count). Together they clear about 70% of the noise. V3 and V5 clear most of the rest. Then wire `ThrowIfVmHasErrors` into Emit and turn `VmAnalyzerReportTests` into a VM-clean ratchet.
2. When K0 removes the StoreBind overloads (C8-tc), about 1400 of the 1988 errors go away with them.
3. Fix the D1 and D2 analyzer FPs. Add the domain check for unknown bare identifiers.
4. Add a Roslyn compile, warnings as errors, over every `docs/probes` domain, not just the goldens. That is how R1 and R2 were found.
