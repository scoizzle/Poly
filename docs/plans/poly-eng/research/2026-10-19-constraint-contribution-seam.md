# Constraint contribution seam: per-constraint codegen, dependency transport, lambda bridge

**Status:** round 5 — agreed for execution; #232 and #233 closed without merging (branches retained: `spike/contributed-methods` is slice 1's base, `docs/action-dependency-passing` is evidence). Round 1 (verifier): needs-rework, 6 `bug`/3 `suggestion`/1 `nit`; all fact-checked and addressed. Round 2: sound-with-findings, all ten `addressed`, 2 new `bug`/1 `suggestion`/1 `nit` addressed in round 3. Round 3: all four `addressed`; 1 `bug`/1 `suggestion` remained (the record/`ExistsWith`/`SaveChanges` contract). Round 4 closes the contract as **D11-B** and pins the record call in slice 1. Round 5 dispositions #232/#233 as closed. Open: **D8** (blocker for slices 2–5), **D11-B confirmation** (before slice 3a). Slice 1 (engine + test double) is not blocked and can start now.

## Goal

A constraint owns its code generation. It compiles to a **complete AST contribution** — statements at a semantic anchor, supporting methods, declared dependencies — and core places it in the lowered module and threads the dependencies along the closed call set. `unique` is the first real consumer and replaces its lowering and store enforcement arms. The interpreter runs the same body the printer prints.

Three coordinated pieces:

1. **Contribution contract + placement engine** (branch `spike/contributed-methods` — closed PR #233; the branch is slice 1's base).
2. **Storage awareness** (branch `docs/action-dependency-passing` — closed PR #232; evidence only. Units from PR #192 — *not yet built*, see D3).
3. **Lambda bridge** — module `Lambda` nodes become real delegates or `Expression<TDelegate>` at CLR call boundaries.

## Constraints

- Poly `AGENTS.md` wins: frozen core (lower-inside-analysis is a hard no; `ConstraintCompilation.Apply` runs inside `RuntimeAnalysisCache.GetOrLower`, in lowering), no consumer-specific lowering flag, no host-escape keyword, no second interpreter, C9's bind rule (`git grep GetOrLower -- src` stays empty — the catalog is the bind surface).
- Board admission: absorbs the **narrowed** C8-tc2 row (real `EnsureUnique` only; probes stay unrestored; `StoreBind.cs` is `StoreJobObjectType` + `TypedNull` — the task file's older probe-restoring text is *not* absorbed). The lambda bridge and multi-unit work are admitted as their own rows (the bridge under the C# surface track). One board, no second CURRENT.
- `docs/CORE.md` updates in the same change as slice 3 (constraint codegen ownership moves).
- Build `dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj`; test `dotnet run --project Poly.Tests/Poly.Tests.csproj` (not `dotnet test`), `-- --treenode-filter` while iterating, full suite at the pre-ship gate in the parent session.
- Tests ship with each feature; every feature gets a fail-closed case.

## Verified facts (round-2 corrected)

- `ConstraintCompilation.Apply` runs inside `RuntimeAnalysisCache.GetOrLower`, in lowering, after `session.Lower` (spike `RuntimeAnalysisCache.cs:152`). Not lower-inside-analysis.
- The engine retargets entry/exit, policy, and subscription body maps after replacing bodies (`Retarget` in `GetOrLower`).
- **The spike engine throws when any parameter carries a default** (`ContributedMethods.cs:149-151` and `:229-231`, "Cannot append contributed parameter … a parameter already has a default"). Product `Create` signatures have trailing optional parameters — `hotel/Guest.cs.golden:149`: `Create(string email, string name, string phone, IEnumerable<EventHold>? events = null, … long stayCount = 0L)`. **Appending `db` would put a required parameter after optional ones — illegal C#.** The spike's `Widget` domain (only `Code`) hides this.
- **Assign-time unique exists and is not a method-entry guard**: `EffectLoweringPass.WrapConstrainedAssign` (`:291`) evaluates the RHS once, runs intrinsic checks (required → range → length → pattern → enum member, per the comment at `:289`), then the `EnsureUnique` invoke (`:305-311`), then assigns. `StoreBindUniqueTests` pins the "EnsureUnique then assign" shape.
- **The store unique gate is the only simulate enforcement today**: `DomainInstanceStore.cs:299`/`:325` (`TryAdd`/`EnsureUnique` path, message "Unique constraint violated: '{propertyName}' value is already used on another '{entity.Name}'." at `:335`), and the catch at `DomainEntityInstance.cs:937` turns `Unique constraint violated` into action `Failure` because the printed factory does not check unique. `DomainEntityInstance.cs:366`/`:460` produce the same text on other paths.
- **The #232 probe's dependency is not read-only** (branch `docs/action-dependency-passing`, closed PR #232): `docs/probes/action-deps/Program.cs:174-183` has `ExistsWithCode(db, code)` local-or-database, and `Create` calls `db.Widgets.Add(created)` **before returning**. The host Adds only after the action returns (`MinimalApiGenerator.cs:457-462` on master).
- **The spike calls `GetOrLower` from `src/`** (`MinimalApiGenerator.cs:785` in `AppendContributedArguments`) — C9 (`tasks/C9.md:6,26,29`) made the catalog the bind surface and its done-when is `git grep GetOrLower -- src` → empty. The same generator also builds POST `Entity.Create` (`:444` comment region) and the `SeedAsync` local function (`:320`, `:1071-1113`) — both call `Create` and stop compiling once `Create` gains `db`.
- `UniqueConstraint` also appears in the parser (`PolyDslParser.cs:1423`), printer (`DomainDslPrinter.cs:823`), `StorageAnalyzer`'s `IsUnique` projection, `HttpFileGenerator` example JSON (`:195`), and MCP `DomainTools` — authoring and projection, not enforcement.
- `EnsureUnique` appears in **13 entity goldens across 7 domains**: hotel `Room`/`Guest`, mcp-library `Book`, crm `Lead`/`Contact`/`Product`, university `Instructor`/`Student`/`Course`, orders `Customer`, warehouse `Warehouse`/`Truck`, clinic `Doctor`. Host `Program.cs.golden` also changes when create/seed/action calls gain the `db` suffix.
- `EveryConceptHasTreeTests.cs:223` oracles unique as "a method named `EnsureUnique`".
- `ClrTypeReference(Type RuntimeType)` requires an in-process CLR type (`Poly/Ast/Nodes/ClrTypeReference.cs:3`). A name-resolved `{Domain}DbContext` is **not** an in-process `Type` — D8 is a blocker, not a detail.
- `EmitLambda` emits closure handles (`object[]` + index + upvalue cells), not delegates or expression trees (`DirectVmAbiEmitter.Statements.cs:469`).
- There is no program-level constant table: `EmitConstant` materializes non-inline values into the invocation's heap per evaluation (`DirectVmAbiEmitter.cs:287`).
- The module compiles to one outer `Expression.Lambda<Action<VmState>>` (`:174`); a nested `LambdaExpression` passed as a `Call` argument takes its form from the **parameter's declared type** — a delegate-typed parameter receives a compiled delegate (the outer `Compile()` compiles delegate-positioned nested lambdas), an `Expression<D>`-typed parameter receives the tree.
- EF lambdas appear only in the host artifact today (`Program.cs.golden`: `e => e.Sku == sku`); no entity module passes a lambda into a CLR call. The spike invoked `SeenCodes.Contains` — **not** EF.
- `LinqExpressionGenerator` is the "test/reference path — NOT the canonical semantics backend" (its header).
- PR #192's `OnlyUnit()` / `AgreedIsUnique` / `store("unitId")` are **prose in an unaccepted ADR — none of it is built**. Master has one `StorageMappingMetadata(StorageModel)`; a second persistence library cannot load (`SessionBuilder.cs:63-69`, duplicate `StoragePass`).
- The VM invokes plain instance calls with plain arguments on supplied values (spike: `db.Contains(code)` on a `SeenCodes`).
- `DomainResult` precedent: printed into the module (`DomainToCSharpExporter.Actions.cs:655`), bound in the interpreter by `DomainResultTypeProvider`.

## Decisions

- **D1 (amended, round 5).** The new ADR "constraints contribute AST at semantic anchors" lands with slice 1's branch. The transport decisions are recorded in this plan; the closed #232 prototype is evidence, not a merge candidate. This plan supersedes #232's absolute "No generator change" non-goal — the route suffix is part of the mechanism.
- **D2 (amended, round 3).** Two anchor kinds, because assign-time unique is not a method-entry guard:
  - *Method anchors* (`OnCreate`, `OnCreateIn`, `OnStageEntry`, `Named`) resolve to **all** matching lowered methods. `OnCreate` has **two slots**, matching the probe's order (check → construct → record → return, `Program.cs:178-184`): the guard before the first `New`, and a **post-construct record call before return**. The record call records the pending key on `db` (the spike's `Add(code)` shape, or a context method both the generated context and the simulate binding implement). It **must not pass the constructed instance to `DbSet.Add`** — simulate's bound constructor produces a `DomainEntityInstance`, not an EF entity, so a real `DbSet.Add` throws on the VM side slice 5 runs.
  - *`OnAssign`* splices into the existing fail-before-mutate wrap (`WrapConstrainedAssign`): after the RHS local, after the intrinsic checks, before the assignment. It does not run at method entry, and it checks the assigned value, not a constructor argument.
- **D3 (rewritten, round 2).** Storage today is **one** `StorageModel` and one `{Domain}DbContext`; two persistence libraries cannot even load. Slice 2 consumes that single model; **no `OnlyUnit()` / `AgreedIsUnique` — those are #192 ADR prose and move to slice 6** after #192's bag exists. No private copy of that API in this program.
- **D4 (amended, round 3).** `unique` is vendor-neutral, owned by the persistence library. **A `unique` constraint with no registered compiler is a compile error, not a silent skip** (the spike's `ConstraintCompilation.cs:43-44` `continue` is the bug this fixes). **The product diagnostic turns on in slice 3a, in the same change that registers `UniqueCompiler`** — slice 2 proves the fail-closed on a **test double** only. Splitting them would fail every unique-bearing golden for the whole of slice 2.
- **D5 (amended, round 2).** Board edit cites the **narrowed** row 4 (real `EnsureUnique` only; probes stay unrestored; `StoreBind.cs` is already `StoreJobObjectType` + `TypedNull` — the task file's probe-restoring text is not absorbed). The lambda bridge and multi-unit work get their own rows (bridge under the C# surface track). **Row 4 closes at slice 5**, when print and sim both reject a duplicate and accept a distinct value. `EveryConceptHasTreeTests` oracle retargets to the guard, not the method name `EnsureUnique`.
- **D6 (amended, round 3).** The store unique gate and its callers delete **only once the module guard is VM-executable end to end** (slice 3b, gated on slice 5 parity). The delete set is: `DomainInstanceStore.cs:299/:325` (`TryAdd`/`EnsureUnique`), the `SetProperty` path, the host job and its callers (`DomainEntityInstance.HostAbi.cs:35`, `DomainEntityInstance.Runtime.cs:142` — the build forces these, but the set names them so 3b is not a hunt), and the `DomainEntityInstance.cs:937` catch that converts the throw into `Failure`. Deleting earlier leaves simulate with no enforcement.
- **D7′ (D7 withdrawn).** Direct EF; the domain-model storage abstraction is not built (recorded as a later option only for multi-unit host fan-out). EF predicates live in the generated `{Domain}DbContext` partial (`ExistsWith{Property}`), never in module AST — until the D9 flip.
- **D8 (open, and a blocker for slices 2–5).** The `db` parameter's static type in the module. Name-resolved `{Domain}DbContext` is not an in-process `Type`; Roslyn-compile-the-host-first makes MCP simulate depend on a generated assembly it does not have. The spike's `ClrTypeReference(typeof(SeenCodes))` stand-in must be replaced by a real answer before slice 2 lands anything that compiles against `Create`. Options: (a) the storage pack contributes a context *base type* in-process that the generated `{Domain}DbContext` extends; (b) simulate Roslyn-compiles the host; (c) the parameter is name-resolved with a VM-side type binding. This decision names the slice-2 done-when.
- **D9.** Predicate placement: context partial for slice 3; the lambda bridge enables a later flip into the module with no contract change — only the supporting method's body source moves.
- **D10 (rewritten, round 2; confirmed by Scot 2026-10-19 as the correct route regardless of the shape of the storage-injected parameters):**
  - **Trigger:** argument node is a `Lambda` and the resolved parameter CLR type is a delegate `D` (`Func<>`, `Action<>`, `Predicate<>`, `Comparison<>`, custom) or `Expression<D>`.
  - **Capture-free → route A:** precompute the delegate or `Expression<D>` at AST-build time, wrap in a `Constant` node; the existing constant path materializes it per run. **Zero new VM machinery — this claim is scoped to route A only.**
  - **Capturing → route B:** emit the nested `LambdaExpression` as the `Call` argument and let the **parameter's declared type select the form**: a delegate-typed parameter receives a compiled delegate (the outer `Compile()` compiles delegate-positioned nested lambdas — nested lambdas and spilled typed locals live inside the outer tree, which is why there is no separate `.Compile()` arm); an `Expression<D>`-typed parameter receives the tree. Route B is real emitter work.
  - **Void (`Action`) bodies:** a **value-form body against a void `D` is a signature mismatch and throws** — C# rejects `Action<int> a = x => x + 1`; no value discard. "Void-tail normalization" only describes constructing a *statement-form* body as a void block (the trailing value is never produced), not discarding one.
  - **Purity for `Expression<D>` args is an AST allow-list checked before emission** — parameters, constants, member access, comparisons, CLR calls on spilled typed locals — with spilled typed locals hoisted before the tree. A body outside the allow-list **throws**; purity is never discovered after a wrong emit.
  - Analysis checks the lambda's parameter count, parameter types, and return compatibility against `D`; any mismatch fails closed.
- **D11 (amended, round 4 — who checks and who tracks).** Contract **B**, the minimal consistent rule:
  - `ExistsWith{Property}` is **pending-key record OR database** — *not* `Local.Any || Set.Any`. The module's post-construct record call (D2) records the **key**, never the instance.
  - Both host `Add`s **stay** (the POST `db.Set.Add` at `MinimalApiGenerator.cs:457-462` and `SeedAsync`'s `db.Add` at `:1072-1073`). They persist; they are not a unique check. `SaveChanges` stays host-side.
  - A nested second create fails **before `SaveChanges`** because the first create's key is already in the pending-key record; on print and sim the record method is the same generated context method with the same name and key argument (no instance-type asymmetry).
  - *Contract A* — the probe's shape: the module calls a generated `RecordCreated(instance)` that the printed context implements as `DbSet.Add` (so `Local` and `SaveChanges` see it), both host `Add`s delete, and `ExistsWith` stays `Local.Any || Set.Any` — is recorded as the alternative. **Pick one before slice 3a**; B is the minimal change.
  - The earlier "read-only / no writable storage surface" non-goal is withdrawn and replaced by: writes through the dependency are exactly the pending-key record; `Add` and `SaveChanges` stay host-side.

## Slices (each its own branch/PR, smallest testable loop first)

1. **Contract + engine** (on a slice branch based on `spike/contributed-methods`, closed PR #233): `CompiledConstraint` → `ConstraintContribution` with the D2 anchor split; anchor resolution to all matching lowered methods; deterministic order (intrinsic before storage, stable tiebreak by library id + name); dependency dedupe with type agreement; supporting-method dedupe; provenance `(library, constraint, entity)`; body-map retarget kept. **Dependency parameters insert *before the first optional parameter*, not appended** — replacing the spike's throw-on-default (`ContributedMethods.cs:149-151`, `:229-231`) — and every positional caller is updated in the same change. Fail-closed cases: no anchor, conflicting dependency type, duplicate supporting method, unsettlable threading. *Done when:* the test-double compiler places a guard on every create path **including a `Create` with trailing defaults (the `Guest` shape, `Guest.cs.golden:149`)**, the Guest-shaped test also asserts the **post-construct key record call after `New` and before the success return**, and each fail-closed case has a test; suite green.
2. **Storage facts in the request (blocked on D8):** the request carries the entity's single `StorageModel` mapping (D3 — no `OnlyUnit`, no units); `ImpliedParameter` gains a dependency identity (library id + name + type); **contributed parameters are read from the catalog trees `Lower` already registered, never `GetOrLower` from `src/`** (C9), and POST `Entity.Create` and `SeedAsync` pass the same suffix as action routes. Fail-closed: entity with no storage mapping; the no-compiler case is proven on a **test double** only (the product `unique` diagnostic turns on in 3a with `UniqueCompiler`, per D4). *Done when:* single-`db` threading passes end to end, `git grep GetOrLower -- src` is empty, and the fail-closed cases throw named errors.
3. **3a — real `unique` goes live:** persistence library registers `UniqueCompiler` (guard at D2 anchors + thin module wrapper + `db` dependency per D11) **and turns the D4 no-compiler diagnostic on in the same change**; storage pack emits `ExistsWith{Property}` and pending-key record partials beside `{Domain}DbContext`; **both host `Add`s stay** (they persist; per D11 contract B) and are not edited here; parser/printer/storage-projection/authoring **stay** (they are not enforcement); goldens updated deliberately — the **full set: the 13 entity goldens across 7 domains plus host `Program.cs.golden`**; `docs/CORE.md` in the same change; board row updated per D5. The store gate (D6) **stays live** in 3a.
   **3b — delete the old path (gated on slice 5 parity):** delete `DomainInstanceStore` `TryAdd`/`EnsureUnique`, the `SetProperty` path, the host job, and retarget the `DomainEntityInstance.cs:937` catch. *Done when (3a+3b together):* no **lowering or store enforcement** arm remains for `unique` (the done-when is deliberately *not* "no `UniqueConstraint` switch arm anywhere" — parser, printer, storage projection, and authoring remain); printed C# and VM simulate produce the identical duplicate failure; **a duplicate assign does not mutate** (D2 `OnAssign`).
4. **Lambda bridge (slice L):**
   - L1 capture-free → precomputed `Constant` (works with today's machinery).
   - L2 capturing → nested `LambdaExpression` as the `Call` argument; the parameter's declared type selects delegate (auto-compiled by the outer `Compile()`) vs `Expression<D>`; signature checks per D10; fail-closed on count/type/return mismatch, **value-form body against void `D`**, `Action<T>` consumer (e.g. `List<T>.ForEach`).
   - L3 `Expression<D>` (`Func` and `Action`) with the AST allow-list purity check and spilled typed locals; fail-closed on a body outside the allow-list.
   - L4 parity — in-memory Sqlite **translates** the tree (`Any` on a real `IQueryable`).
5. **Parity slice (closes row 4, D5):** print-compile-run the host, run the module against the compiled context. *Done when:* **both sides fail on a duplicate with identical text, both succeed on a distinct value, and a nested second create fails before `SaveChanges`** (D11 local half). The simulate constructor-slot exception keys off the **contributed dependency identity**, not `ConstraintCompilers.Count > 0` (the spike's count flag at `DomainEntityInstance.cs:1113-1116` is replaced).
6. **Multi-unit (after #192's bag exists):** `OnlyUnit()`-style fail-closed, `AgreedIsUnique`, `store("unitId")` membership; entity in two units carries both or fails closed. No part of this API is invented earlier.
7. **Optional flip (after 4):** move `unique`'s predicate from the context partial into the module via the bridge — contract unchanged.

## Sequencing

1. Merge this plan (#234) as the program record; #232 and #233 are closed without merging.
2. **D8 decided** (Scot) — blocks slices 2–5.
3. Slice 1 off the retained `spike/contributed-methods` branch; admitted per D5.
4. Slices 2 → 3a → 4 → 5 → 3b in order: nothing ships with two *silent* unique paths, and the store gate deletes only after parity proves the module guard enforces on both sides.
5. #192 proceeds independently; slice 6 consumes it; slice 7 optional.

## Risks (ranked)

1. **D8 unresolved** — blocks the whole storage half; name-resolved contexts are not `Type`s the VM can invoke.
2. **Golden churn** — 13 entity goldens + host `Program.cs.golden`, deliberate, same-change.
3. **Parameter position** — required-after-optional is illegal C#; fixed by positioned insertion (slice 1) with a `Guest`-shape test.
4. **Double-track / late-Add** — module `Add` vs host post-action `Add` (D11); nested-create-before-`SaveChanges` is the pinned case.
5. **Assign-time unique lost** — `OnAssign` splices into `WrapConstrainedAssign` (D2); duplicate-assign-does-not-mutate is a done-when.
6. **EF translation purity** (L3) — allow-list + fail-closed.
7. **State-escaping delegates** (event registration, `Task.Run` capturing VM state) — v1 non-goal, synchronous pass-and-call only; recorded, not detected.

## Non-goals

- No second interpreter; `LinqExpressionGenerator` stays the test/reference path.
- No `SaveChanges` through the dependency; writes through the dependency are context `Add` only (D11).
- No async surface in the bridge v1.
- No unit/multi-vendor machinery before #192 builds it (D3, slice 6).
- No dormant emitter capability without a consumer; every capability lands with a test that uses it.

## Open items

- **D8 (blocker for slices 2–5)** — the `db` parameter's static type: (a) in-process context base type contributed by the pack, (b) simulate Roslyn-compiles the host, (c) name-resolved + VM-side type binding.
- **D11 confirmation** — contract B adopted (pending-key record OR database; host `Add`s stay). Contract A (generated `RecordCreated` → `DbSet.Add`; host `Add`s deleted) is the recorded alternative. Confirm before slice 3a; slice 1 is unaffected.
- **D1** — the new ADR lands with slice 1; #194 close-or-absorb under the narrowed row.
- Switch to a build agent to execute slice 1 (the engine + test double; not blocked by D8 or D11).

## Review log

- **Round 1 (2026-10-19, verifier subagent):** verdict needs-rework — 6 `bug` (parameter position after defaults; assign-time unique anchor; premature store-gate deletion + silent no-compiler skip + read-only claim; unbuilt #192 API in slice 2; D10 void/compile contradictions; `GetOrLower` in `src/` + POST/seed call sites), 3 `suggestion` (board-row narrowing + oracle; golden census + done-when scope; parity assertions + count flag), 1 `nit` (citation drift + false EF sentence). All fact-checked and addressed: D2/D3/D4/D5/D6/D10/D11 amended, D8 promoted to blocker, slices 1/2/3/5 restructured (3a/3b split), golden set and done-whens corrected.
- **Round 2 (same session, scoped to the fix):** all ten round-1 findings `addressed`; verdict sound-with-findings — 2 new `bug` (D11 put `DbSet.Add` of the constructed instance in the module body, which the simulate binding cannot run, and D2 lacked the post-construct slot; D4's diagnostic and the compiler that satisfies it were split across PRs, failing every unique-bearing golden for the whole of slice 2), 1 `suggestion` (void-`D` fail-closed belongs in L2, not slice 1), 1 `nit` (D6 host-job callers unnamed).
- **Round 3 (same session):** all four round-2 findings `addressed`; 1 new `bug` remained — the record (key-only) / `ExistsWith` (`Local.Any || Set.Any`) / deleted host `Add`s contract could not all hold. Suggestion: pin the record call in slice 1's done-when.
- **Round 4 (this document):** contract resolved as **D11-B** — `ExistsWith` = pending-key record OR database; key-only record; host `Add`s stay; nested create still fails before `SaveChanges`. Contract A recorded as the alternative for confirmation before 3a. Slice 1's done-when now asserts the post-construct record call. Review loop closed by the fix cap; no further verifier pass pending.
