# TASK C8-tc1 - Typed create in the compiled tree (1 of 3: typed lowering + interpreter map)
Status: planned. Branch slice/c8-tc1-typed-create. Lane B. Review 2. Implement mill: Grok (review on OpenCode).
## Scope
Split of C8-tc (Scot 2026-10-10 19:58 CT; see `pipeline-convergence-plan.decisions.md`). Findings G1, K0 (call-site half), C6b2 (call-site half).
Lowering names string-named store jobs: `LowerRuntimeFactoryCall` (`Poly/DomainModeling/Lowering/EffectLoweringPass.cs:1136-1198`, reached by `CreateEntityInstance` `:897`, `CreateEntityInRelationship` `:909`, `LowerCreateInProbe` `:1104`, `LowerCreateEntityInstanceProbe` `:1122`) emits `this.Create("Type", "prop", value, …)` / `this.CreateIn` / `this.ProbeCreate` with entity values cast to object (`:1160-1162` `NeedsObjectSlotCast`). Replace all three with typed calls against the factories that already exist on master: static `Entity.Create(args…)` (`Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs:849`, built from `:779`) and `this.Create{Nav}(args…)` (`Poly/DomainModeling/Lowering/DomainToCSharpExporter.Notify.cs:92`, `AddCreateSingularNavMethod` `:84`); probes become static `Entity.ProbeCreate(args…)` — a new exported method whose body delegates to today's probe semantics (construct via `Create`, rewrap as `DomainResult`) so behavior is unchanged and G2 stays open for C8-tc3. Results stay `DomainResult`/`DomainResult<T>` with no cast to object; positional args in constructor-parameter order (`GetConstructorParameters` `Notify.cs:311`), collections as `new List<T>()` before any back-ref slot (C6b2: a self-rel `create in children` must print `Node.Create(name, this, new List<Node>())`, not `…, this, this)`; `StoreBind.cs` `BuildTargetCreateArgs` tests `IsBackReference` `:225` before `IsCollection` `:232` and has the same bug — that copy dies with the file in C8-tc2).
The interpreter maps the typed calls onto its dictionary-backed instances: static `Entity.Create`/`ProbeCreate` module methods rewrite onto host jobs keyed by an ambient subject (`Poly/DomainModeling/Runtime/DomainEntityInstance.cs:866-895` module-type analysis), new `DomainInstanceHostJobs.TypedCreate`/`TypedProbeCreate` pack positional args into the existing dictionary `Create`/`ProbeCreate` jobs (`Poly/DomainModeling/Runtime/DomainInstanceHostJobs.cs:31-38`, `Values`/`With` `:41-56`), and `this.Create{Nav}` routes in `DomainEntityInstance.InvokeNamed.cs:26-27` through a `HostAbi` helper that packs `CreateNavArgumentNames` order into the `CreateIn` job (`Poly/DomainModeling/Runtime/DomainEntityInstance.HostAbi.cs:430-443`, `:502`). The old `Create`/`CreateIn`/`ProbeCreate` name routing in `InvokeNamed` stays (harmless; C8d deletes it).
Not this slice (unchanged, verified still true after): `EnsureUnique` stays the always-Success stub (`StoreBind.cs:25-35`, `WrapConstrainedAssign` `EffectLoweringPass.cs:310-313`; G3+D1 is C8-tc2); `DomainToCSharpExporter.StoreBind.cs` stays printed — its dictionary overloads back nothing the tree calls, but the file dies in C8-tc2 (rest of K0); `Create{Nav}` stays private (G9 is C8-tc2); the probe *body* still constructs and attaches (G2 is C8-tc3). Regenerate the affected goldens (14 files hold 28 string-named `Create(`/`CreateIn(`/`ProbeCreate(` call sites on master `60089ef0`, 55 calls total).
## Files
- `Poly/DomainModeling/Lowering/EffectLoweringPass.cs` (typed create / create-in / probe call sites; drop the object-cast helper)
- `Poly/DomainModeling/Lowering/DomainToCSharpExporter.Notify.cs` (share `GetConstructorParameters` `:311` internal; add `CreateNavArgumentNames` / `TrailingDefaultedProperties` / `EmptyListNode` / `TypedNull` helpers; move the collection check (`:144`) above the back-ref check (`:129`) in `AddCreateNavMethod` so a self-rel collection slot gets a list)
- `Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs` (emit static `ProbeCreate` delegating to `Create` — semantics-preserving bridge to C8-tc3)
- `Poly/DomainModeling/Runtime/DomainEntityInstance.cs` (rewrite static `Create`/`ProbeCreate` module methods onto the host jobs; ambient push around `Interpreter.Execute` `:854`)
- `Poly/DomainModeling/Runtime/DomainEntityInstance.HostAbi.cs` (ambient accessor; `TryTypedCreateNav` packing into `CreateIn` `:502`)
- `Poly/DomainModeling/Runtime/DomainEntityInstance.InvokeNamed.cs` (route `Create{Nav}` beside the existing name routing `:26-27`)
- `Poly/DomainModeling/Runtime/DomainInstanceHostJobs.cs` (`TypedCreate`/`TypedProbeCreate` + `TrimNulls` on top of `Values`/`With`)
- `Poly.Tests/DomainModeling/Lowering/StoreBindCreateTests.cs`, `Poly.Tests/DomainModeling/Lowering/DomainToCSharpExporterTests.cs` (incl. new self-rel collection row; probe-position rows now look for `Stay.ProbeCreate(`), `Poly.Tests/DomainModeling/Lowering/ParityTests.cs`, `Poly.Tests/DomainModeling/Lowering/Item4FailBeforeMutateTests.cs`, `Poly.Tests/DomainModeling/Runtime/DomainInstanceHostJobsTests.cs`, `Poly.Tests/DomainModeling/Compile/EmitGoldenTests.cs`
- Goldens: regenerate `Poly.Tests/DomainModeling/Compile/EmitGolden/**` (call sites typed; remaining StoreBind dictionary bodies and `EnsureUnique` strings are expected until C8-tc2; none hand-edited)
## Shape matrix
| Input kind | Before | After | Test that proves it |
|---|---|---|---|
| `create Fine { Amount: 5 }` body (K0/G1 family) | `var create = this.Create("Fine", "Amount", 5L)` → `DomainResult<object>` | `var create = Fine.Create(5L, …defaults)` → `DomainResult<Fine>`, no object cast | `StoreBindCreateTests`; golden `simulate-create-type/Fine.cs.golden` |
| `create in enrollments { section: offering }` (G1) | `this.CreateIn("enrollments", "section", (object)offering)` (`university/Student.cs.golden:75`) | `this.CreateEnrollments(offering)` | `StoreBindCreateTests`; `university/Student.cs.golden` regenerated |
| Entity-typed initializer value | `(object)offering` TypeCast into the object slot | typed argument, `Arguments.Last() is TypeCast` false | `StoreBindCreateTests` (Loans/Permits rows) |
| Self-rel collection create-in (C6b2) | `Node.Create(name, this, this)` — CS1503 | `Node.Create(name, this, new List<Node>())`, export compiles | new `DomainToCSharpExporterTests.Export_SelfReferencingCollection_CreateGetsEmptyListNotThis` |
| Fail-before-mutate probe call site | `this.ProbeCreate("Stay", "book", book)` | `Stay.ProbeCreate(book)`; same construct/attach semantics as today (G2 open) | `Item4FailBeforeMutateTests`; `DomainToCSharpExporterTests` probe-order rows |
| Interpreter runs the typed create | only the string-named job path | `Type.Create` / `Type.ProbeCreate` / `this.CreateX` execute on the dictionary-backed instance (ambient host job) | `ParityTests` rows; `DomainInstanceHostJobsTests` |
## Done when
- `EffectLoweringPass` emits `Entity.Create(realParams, defaults)` for create, `this.Create{Nav}(…)` for create-in, and `Entity.ProbeCreate(realParams, defaults)` for probes; no `NeedsObjectSlotCast`/`TypeCast`-to-object remains on create values; results stay `DomainResult`/`DomainResult<T>` with no cast to object.
- The interpreter maps those typed calls onto its dictionary-backed `DomainEntityInstance` via the ambient host jobs and `InvokeNamed`/`HostAbi`; simulate and print agree (`ParityTests` green), including the Item-4 probe-before-assign ordering.
- C6b2 call-site: a self-referencing collection slot gets `new List<T>()`, and the new exporter test compiles the export.
- Goldens regenerated: `git grep -n 'this\.Create("\|this\.CreateIn("\|this\.ProbeCreate("' Poly.Tests/DomainModeling/Compile/EmitGolden` returns nothing (`EnsureUnique` strings and StoreBind dictionary bodies may remain until C8-tc2). Full suite green.
- Probe *behavior* is unchanged (still constructs; G2 open), `EnsureUnique`, `StoreBind.cs`, `Create{Nav}` accessibility and `DomainResult<T>` shape are byte-for-byte the C8-tc behavior (no drive-by fixes).
## SHIP if / NOT SHIP if
SHIP if every create/create-in/probe call site in the lowered tree and goldens is typed with real parameters and defaults, the interpreter executes those typed calls (parity green), the C6b2 slot is a list, and the suite passes with regenerated goldens.

NOT SHIP if any string-named create/create-in/probe call site survives, any `(object)` cast remains on a create value, probe/`EnsureUnique` *behavior* or StoreBind/`Create{Nav}`-visibility changed (out of scope), typed calls lost defaults or reorder arguments, or a golden was hand-edited.
## Tests
```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/StoreBindCreateTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/DomainToCSharpExporterTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/ParityTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/Item4FailBeforeMutateTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/DomainInstanceHostJobsTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/EmitGoldenTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj
```
## Hand-edit
One factory call replaces one string-named call per site (the object cast drops), the interpreter adds one named-route and one host job, and the goldens show the typed call Scot already hand-writes; every touched spot is a named method on the existing path.
## Needs Scot
none
## Log
| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-10 | planner | opencode/opencode-go/mimo-v2.6-flash | 60089ef0 | planned | G1 K0(call-site) C6b2(call-site); split of C8-tc per Scot 19:58 CT |
