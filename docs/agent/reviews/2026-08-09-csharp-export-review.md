# C# export review — TinyCompiler dogfood (enum notation + compile failures)

**Date:** 2026-08-09
**Mode:** product-surface dogfood — modeled a real *programmatic* domain (a compiler
pipeline) via `@poly-local` MCP, then compiled the generated C#.
**Evidence baseline:** `TinyCompiler` domain (6 entities, 8 relationships, 3 enums,
5 primitives) authored in product DSL, exported via `export_domain_to_csharp`, compiled
with `dotnet build` in a throwaway console project.
**Maps used:** [`docs/CORE.md`](../../CORE.md), [`docs/plans/grammar-revision.md`](../../plans/grammar-revision.md),
[`Poly.Mcp/Docs/poly-dsl-guide.md`](../../../Poly.Mcp/Docs/poly-dsl-guide.md).

**Verdict:** The runtime product (domain model, analysis, policies, subscriptions,
instance store) handled a genuinely programmatic domain well — but **the C# export does
not compile** for the most common action shapes. The scaffolding (enums, `DomainResult`,
validators, `IReadOnlyList` navs, stage enum, subscription wiring) is well-shaped; the
effect/enum lowering is broken in four independent ways, and **no test compiles the
export** — which is exactly why they shipped.

---

## 0. Executive summary

| Item | Assessment |
|------|-----------|
| **DSL authoring** | Solid surface; 5 friction points (below) — none blocking |
| **Runtime/analysis** | Clean model: 102 info / 5 warning / 0 error |
| **Generated C# scaffolding** | Good: `DomainResult<T>`, validators, navs, stage enum, subscription `notify`/`When…` |
| **Generated C# actions** | 🔴 **Does not compile** — 7 errors, 4 root causes (A–D) |
| **Test wall** | Exporter tests assert **AST shape only**; nothing renders + compiles the emitted C# |
| **Verification** | Exported `TinyCompiler` C# fails `dotnet build` with 7 errors; warnings include CS8618/CS0472 |

---

## 1. Enum member notation — decision

**Question:** generated C# should reference enum members as `EnumName.Member` or bare
`.Member`?

**Decision: qualified `EnumName.Member`** (e.g. `TokenKind.Numeric`, `PatronStatus.Active`).

- Generated C# is a single flat file; entity classes and enum types are siblings in the
  same namespace. An entity method referencing `Numeric` (bare) only resolves if the
  entity itself has a member of that name — wrong or ambiguous.
- `this.Numeric` is semantically false: the entity has no such property (this was the bug).
- The string-literal enum path already emits qualified (`assign Status to "Suspended"` →
  `this.Status = PatronStatus.Suspended`). Bare identifiers must match that rule.

**Implemented** in `EffectLoweringPass`:
- `LowerEnumAwareValue(expr, targetType, subject)` — used by both `CreateEntityInRelationship`
  and `BuildConstructorArgs`: a bare `PropertyAccess` whose name is a member of the target
  enum type lowers to `new Member(NamedTypeReference(enumType.Name), name)`.
- `Assign` RHS: same rule for `assign Kind to Numeric`.
- Regression tests: `Export_EnumMemberInCreateInInitializer_EmitsQualifiedMemberAccess` +
  `Export_EnumMemberInAssignRhs_EmitsQualifiedMemberAccess` (render + assert `TokenKind.Numeric`,
  reject `this.Numeric`).

---

## 2. Findings — generated C# (🔴 compile-breaking)

All reproduced by compiling the 17 KB export of `TinyCompiler`.

### A. Enum member refs in create-initializers lower to `this.<member>` — ✅ FIXED

`create in tokens { Kind: Numeric }` emitted `this.Numeric` (CS1061) instead of
`TokenKind.Numeric`. **Fixed** (see §1). Bare enum members now lower qualified everywhere
(create, create-in, assign RHS).

### B. `-> EntityType` return lowering is broken — � analysis side DONE, lowering OPEN

`Lex: action -> Token` emitted:

```csharp
var token = this.CreateTokens(0L, this.Numeric, "", 0L, null);
return DomainResult<Token>.Success(this.CurrentStage = CompilationStage.Parsing);
```

The created token is **discarded**, and the stage-transition **assignment expression** is
passed as the return value (CS1503: `CompilationStage` → `Token`). `EmitObject() -> Artifact`
is worse: `Success(this.NotifyDoneSubscribers())` — a **void** call as the value (CS1503).
This is the flagship library-demo pattern (`CheckOut: action (book: Book) -> Loan { create in loans { } }`).

**Analysis side DONE 2026-08-09 (DMEFF010):** new validation in `EffectAnalyzer`
(`ValidateActionReturnFinalStatement`) — an action declaring `-> T` must have its
**final statement** produce a `T`: a `create`/`create in` of T, or a final conditional
whose **every branch** ends in a producer (a conditional without a final `else` is
rejected — can produce nothing). `create T; transition to Done` / `create T; assign …`
now fail analysis, so the exporter's broken "return the last effect" can never see a
non-producer last statement. This also pins runtime/export consistency (the runtime's
last-created-of-type reverse scan agrees with last-statement semantics under the
contract). Guide updated (§6, DMEFF009 + DMEFF010); 5 tests in
`ActionEntityReturnTests` (create-not-last ✗, assign-after-create ✗, conditional-both-
branches ✓, conditional-no-else ✗, conditional-bad-branch ✗).

**Lowering side DONE 2026-08-10:** with the create now guaranteed to be the final
statement, the exporter's return lowering is correct — `Lex` emits
`return DomainResult<Token>.Success(this.CreateTokens(...))` (the created instance),
not the last effect's value. Verified by compiling the full TinyCompiler export
(0 errors).

### C. Nav-name casing not mapped — ✅ FIXED (shared member-name resolver)

Policy `HasSource: policy { source exists }` emitted `this.source != null` (CS1061) — the
camelCase DSL nav name wasn't mapped to the generated `Source` property.

**Fixed 2026-08-09:** added a **shared navigation name resolver** — the single source of
truth for DSL nav name → generated C# member name:
- `LoweringContext.NavigationNameResolver` (new): `Func<string,string>` mapping a DSL
  relationship/nav name to the generated pascal-cased member (`compilations` → `Compilations`).
- `EffectLoweringPass.BuildNavigationNameResolver(entity, domain, analysis)` builds it from
  `RelationshipLookupMetadata` (primary) or the domain relationship list (fallback).
- `DomainExpressionLoweringPass` applies it in `PropertyAccess` (`Rel exists`), `OwnedAccess`,
  and `RelationshipNavigation` (path-prefix); binder/action parameters are resolved first and
  are exempt.
- `DomainToCSharpExporter.LowerExpressionToMethodBody` (policy bodies) now supplies it too.

Every lowering site that references a nav member now agrees with the exporter's naming — the
“multiple members reference the same field” problem is centralized in one resolver.

### D. `create in Rel` helpers call `Create` with wrong arity — � analysis side DONE, lowering OPEN

`CreateCompilations`/`CreateBuilds` call `Compilation.Create(entryPath, finishedAt, hadErrors,
startedAt, source, pipeline)` — 6 args — but the signature needs 9 (CS7036 ×2): the three
collection params (`tokens`, `diagnostics`, `artifacts`) are required in the ctor but never
passed. Only `create in Rel` (nav factory) hits this; `create Type` passes all ctor params
via `BuildConstructorArgs`.

**Analysis side DONE 2026-08-09 (DMEFF011):** new validation — every `required` property of
the created entity must be provided in `create`/`create in` initializers (unless it has a
`default`; the auto-wired back-reference nav is exempt). This catches the partial-initializer
shape that would throw at runtime (`create in diagnostics { Severity: Hint }` with required
`Code`/`Message`). Implemented in `EffectAnalyzer.ValidateRequiredInitializerCoverage`
(both create + create-in); 5 tests; guide updated.

**Lowering side DONE 2026-08-10:** `AddCreateNavMethod` now builds its `Target.Create(...)`
arg list in the **same order as the generated entity constructor** — entity props (ESM order)
first, then navs in relationship order, with collection navs emitted as **empty `List<T>`**
(ESM omits collections but the ctor includes them as `IEnumerable<T>` params).
`SourceFile.CreateCompilations` now calls `Compilation.Create(entryPath, finishedAt,
hadErrors, startedAt, source, pipeline, new List<Token>(), new List<Diagnostic>(),
new List<Artifact>())` — CS7036 gone. Regression test
`Export_CreateNavMethod_EmitsEmptyCollectionArgsForCtorArity`.

---

## 3. Findings — generated C# (🟠 warnings / behavior gaps)

Even after A–D compile, the export has correctness gaps:

1. **Dead validation:** `if (kind == null)` on non-nullable `TokenKind`/`ArtifactKind`
   (CS0472, always false) — required-enum validation is theater.
2. **Nullability leaks:** `Source`, `Pipeline` (and `Token.Compilation`) are non-nullable
   `{ get; private set; }` (CS8618) but routinely receive `null` — optional navs become lies.
3. **Auto-wire missing (guide §0.3):** `create in builds { EntryPath: "queued" }` emits
   `Compilation.Create(..., null, null)` — `pipeline` should be `this` but gets `null`.
4. **Partial initializers on required props → runtime throw:** `create in diagnostics
   { Severity: Hint }` → `Diagnostic.Create("", 0, 0, "")` → `Code`/`Message` required →
   the helper **throws** `InvalidOperationException` instead of returning a `Failure`.
   `Lex()` would throw the same way (`Lexeme` required).
5. **Initializer silently dropped:** defaulted props (e.g. `Severity` with `default(Warning)`)
   are excluded from the ctor, and an explicit binding (`Severity: Hint`) is dropped entirely —
   the override can't be expressed.
6. **`StartedAt` set twice:** ctor parameter, then `UtcNow` overwrite from the entry effect.

**Follow-up:** required-prop defaults on partial initializers, optional-nav nullability,
auto-wire back-reference, and the defaulted-prop override are a single "create-initializer
completeness" pass.

---

## 4. Findings — DSL authoring surface (🟡)

1. **`Number` enum member is a parse error** — collides with the `NumberType` keyword
   (`Expected RBrace, got 'Number'`). Renamed to `Numeric`. Enum members named after type
   keywords are impossible; undocumented.
2. **Create-initializer ambiguity:** `create in tokens { Kind: Identifier Lexeme: "id" }`
   fails — two space-separated identifiers parse as a **path-prefix read**, so the second
   binding breaks (`Expected property name`). Enum values in initializers are only safe as
   the *last* binding. The library demo dodges this by using literals (`Amount: 5`).
3. **Relationship names are globally unique per domain** — a back-ref `compilation` on three
   entities errors ("defined more than once"). Reasonable, but forces renames when several
   children reference a parent.
4. **`or` inside a quantifier body requires parens** (`any diagnostics where (A or B)`) —
   without them it resolves against the *source* entity with a misleading error
   ("property 'Severity' does not exist on entity 'Compilation'").
5. **Analyzer blind spot:** 5 warnings claim transitions require `EntryPath` with no
   AssignEffect producing it — but `EntryPath` is satisfied at *create* time
   (`create in builds { EntryPath: "queued" }`). The analyzer can't see create-initializers
   as satisfying `required`. False positives.

---

## 5. Root cause

`DomainToCSharpExporterTests` assert **AST tree shape only** — no test renders + compiles
the emitted C#. That is why four independent compile-breaking lowering bugs (A–D) shipped.
The runtime path (VM, instance store) handles these constructs; the exporter is a second
lowering that has drifted.

---

## 6. Checkable follow-up tasks

- [ ] **A (DONE 2026-08-09):** enum members in create/create-in initializers + assign RHS
      lower to qualified `EnumName.Member` (`LowerEnumAwareValue` + `Assign` branch; two
      regression tests).
- [ ] **B-analysis (DONE 2026-08-09):** DMEFF010 — final statement of a `-> T` action must
      produce T (create/create-in last, or every branch of a final conditional); 5 tests +
      guide update.
- [ ] **C (DONE 2026-08-09):** shared `NavigationNameResolver` (entity/analysis-backed) —
      nav names pascal-case consistently across `Rel exists`, path-prefix, owned access,
      policy bodies, and the exporter; 2 regression tests.
- [ ] **D-analysis (DONE 2026-08-09):** DMEFF011 — create/create-in must provide every
      `required` property (defaults + auto-wired back-ref exempt); 5 tests + guide update.
- [ ] **E-tests (DONE 2026-08-09):** 8 tests covering C + D (render/compile-assert + analysis).
- [ ] **B-lowering (DONE 2026-08-10):** create-is-last contract (DMEFF010) + verified return
      of the created instance (`Success(this.CreateTokens(...))`); full export compiles.
- [ ] **D-lowering (DONE 2026-08-10):** nav-factory `create in Rel` appends empty-collection
      ctor args (CS7036 fixed); regression test added.
- [ ] **E-guard (OPEN):** full render + `dotnet build` compile-smoke test in the suite
      (verified manually 2026-08-10: TinyCompiler export compiles, 0 errors; render-based
      guards for A/C/D exist as tests but no in-suite compile gate yet).
- [ ] **F (OPEN):** create-initializer completeness — optional-nav nullability (CS8618),
      defaulted-prop overrides, `StartedAt` double-set, dead `kind == null` checks (CS0472),
      auto-wire verify.
- [ ] **G (DSL):** enum-member collisions with type keywords — parse error should be a clear
      message; reserved-word check.
- [ ] **H (DSL):** create-initializer path-prefix ambiguity — “bare enum member must be last
      binding” or grammar fix.
- [ ] **I (analysis):** required-prop satisfaction from create-initializers (kill the 5 false
      transition warnings).

**Not done here:** E-guard (in-suite compile smoke), F–I are filed, not fixed.

### Additional analysis cases identified (F)

Beyond the fixed DMEFF010/011, the dogfood surfaced three more authoring/analysis gaps,
filed above: **G** (enum member names colliding with type keywords parse with a confusing
error — should be a clear reserved-word message), **H** (two space-separated bare identifiers
in create-initializers parse as a path-prefix read, so a bare enum member must be the last
binding), and **I** (RequiredPropertiesPass treats create-initializers as satisfying
`required`, killing the five “transition requires EntryPath” false warnings).
