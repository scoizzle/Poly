# PR 221 remaining — carry trailing scalars in the HTTP create DTO

Status: planned follow-up. Base `29d3eb8b` on `slice/analysis-truth` (PR 221).
This file is not a board entry and does not change `CURRENT`; it records the one
held-out slice from PR 221 so the next branch can be cut cold.

Branch for the item: `pr221/create-dto-trailing-scalars`, cut from `29d3eb8b`
(this worktree's HEAD), **not** from `origin/master`. PR 221 is unmerged and the
follow-up consumes the requiredness / optional semantics it introduces.

## What this does not reopen

Already shipped in PR 221; do not re-plan or change it:

- `optional` is the explicit opt-out; required is the default.
- Presence and the value-null rule, including absent reference-typed optionals
  throwing and boolean path-hop guards.
- Create DTO `[Required]` for every not-`optional` property that is already a
  DTO member.
- Deliberately left alone: `RequiredPropertiesPass`, `BehaviorMetadata` (action
  parameters), `ConstraintValidation`, and the guide's known gap about
  `invoke Rel.Action(param:)` constraint derivation.

## The hold-out

`MinimalApiGenerator.GetConstructorOrder` returns
`EntityStructureMetadata.ConstructorParameters` and its comment says trailing
omittable scalars are deliberately not appended: a create contract that passed
the CLR/DTO default would override the domain's `default(...)`. `BuildDtoTypes`
therefore projects only required scalars, and the POST body
(`AppendCreateCallStatements` → `BuildCreateArgs`) passes `dto.<Prop>` straight
into `Entity.Create` for whatever `GetConstructorOrder` returned. The guide says
the same near the transport `[Required]` bullet
(`Poly.Mcp/Docs/poly-dsl-guide.md`, "Trailing `optional`/`default(...)` scalars are
not create-DTO members yet …").

The exporter is already correct: `DomainToCSharpExporter` appends the ordered
trailing list (`EntityStructureMetadata.TrailingOptionalPropertyNames`, consumed
through `DomainToCSharpExporter.TrailingOptionalProperties`) as C# optional
parameters, and `Create` applies them like this:

| DSL declaration | `Create` parameter today | Semantics today |
|---|---|---|
| `Title: Text` | `string title` | required positional; value non-null |
| `Memo: Text optional` | `string? memo = null` | omitted stays `null` (no coercion) |
| `Mark: Text default("x")` | `string mark = "x"` | omitted takes the constant |
| `Due: Date default(Today)` | `DateOnly? due = null` + body `Due = due ?? <Today>` | omitted takes the runtime default, evaluated at create |
| `Score: Number default(5)` | `long score = 5L` | omitted takes the constant |

`TrailingOptionalProperties` is the ordered list the factory already uses; it is
the single source for order. The HTTP producer must consume it, not re-derive it.

## Design decision: nullable DTO member that means "omitted", coalesced at the call

Use the comment's second option.

- Every trailing scalar becomes a nullable init-only DTO member (`T?`) with no
  initializer, in `TrailingOptionalProperties` order, after the required members.
- The POST passes one argument per trailing member, in the same order:
  - `optional`, no `default(...)` → `dto.Memo` (the `Create` parameter is
    `T? = null`, so omission stays absent);
  - `default(...)` that lowers to a **runtime** expression (`Today`, `Now`,
    `Guid`, a library default) → `dto.Due` (the `Create` parameter is the
    `T? = null` sentinel and its body already coalesces to the runtime default);
  - `default(...)` that lowers to a **constant** → `dto.Mark ?? "x"` /
    `dto.Score ?? 5L` (the `Create` parameter is non-nullable with a compile-time
    default, so the call site supplies the omission fallback);
  - a `default(...)` that lowers to neither → throw at generation (see the
    fail-closed case below).
- Trailing members never project `[Required]`, even when the DSL spells
  `required` next to `default(...)`. Create-presence is a different axis from
  value requiredness: `default(...)` makes the property omittable at create, and
  a DTO `[Required]` would reject exactly the omission the domain allows. This
  does not reopen the shipped rule: `BuildDtoTypes` already refuses to add
  `[Required]` when the property carries `default(...)`; this item only starts
  emitting those members.
- Required members keep the shipped projection unchanged: non-null, straight
  `dto.X`, with `[Required]` where the current code emits it (reference-typed
  scalars) and never added to value types just because they are required.

### Why not put the DSL default in the DTO

That shape (a non-nullable DTO member initialised with the domain default) would
move evaluation of `default(Today)` / `default(Now)` / `default(Guid)` into JSON
binding, before `Create`. The exporter already owns that rule — the `??` coalesce
in the constructor and the UTC clock choice (`Poly.Mcp/Docs/poly-dsl-guide.md`
"Create-time defaults … emit a `??` coalesce") — so the DTO would become a second
place that must stay in lockstep with it. It also erases the omitted signal: an
absent member and a member explicitly set to the default become indistinguishable,
which is the wrong shape for the `optional` axis that must stay absent.

The nullable/coalesce shape is the shape the exporter already uses for runtime
defaults (`T? = null` sentinel + `?? <runtime>`), so the transport forwards
instead of re-implementing. It is also the only shape that can represent
"present `0` / `false`" distinctly from "omitted": `int?`/`bool?` are non-null
for `0`/`false`, so a present value always wins over the fallback.

## The contract this must produce

For:

```poly
domain Notes
Note: entity {
  Title: Text
  Due: Date default(Today)
  Mark: Text default("x")
  Memo: Text optional
  Score: Number default(5)
}
```

`Create` (existing exporter; do not change):

```csharp
public static DomainResult<Note> Create(
    string title,
    DateOnly? due = null,
    string mark = "x",
    string? memo = null,
    long score = 5L)
```

DTO (after this item):

```csharp
public record NoteDto {
    [Required]
    public string Title { get; init; } = default!;
    public DateOnly? Due { get; init; }
    public string? Mark { get; init; }
    public string? Memo { get; init; }
    public long? Score { get; init; }
}
```

POST body (after this item):

```csharp
var noteResult = Note.Create(dto.Title, dto.Due, dto.Mark ?? "x", dto.Memo, dto.Score ?? 5L);
```

`Due` is deliberately the bare member: `Create` evaluates `Today` at create.
`Mark` and `Memo` are both strings and sit next to each other, so a positional
swap would still compile — the tests must assert the whole argument sequence, not
just membership.

## Invariants (each has a proving test)

1. Omitted `optional` stays absent (`null`), not `0` / `false` / `""`.
2. A present `0` or `false` is still present and wins over the fallback.
3. Omitted `default(...)` uses the domain default; `default(Today)` /
   `default(Now)` are evaluated at create, not as the CLR default.
4. An explicit JSON value wins over the domain default.
5. A not-`optional` property remains a required non-null value on the entity;
   DTO nullability is only the omitted signal.
6. Generated C# reads like hand-written production code: no stubs, no
   commented-out branches, no silent wrong fallback.

| Invariant | Proved by (done-when numbers) |
|---|---|
| 1 | 2, 4 |
| 2 | 5 |
| 3 | 4, 5, 10 |
| 4 | 3, 4, 5 — structurally: the DTO member is the coalesce's left operand, and a non-null present value is never replaced |
| 5 | 1, 6 |
| 6 | 11 plus reading the rendered `Program.cs` for the test domains |

## Item (one item — see "Why one item")

### slug

`create-dto-trailing-scalars` → branch `pr221/create-dto-trailing-scalars`

### Behavior change

The generated HTTP create DTO carries every trailing omittable scalar as a
nullable member, and the generated POST forwards each one into `Entity.Create`
with the omission semantics above. The required positional contract, the
exporter's `Create` signature, and the analyzer's published trailing list are
consumed unchanged.

Concretely, in `src/Poly.DslCompiler/MinimalApiGenerator.cs`:

- Keep `GetConstructorOrder` required-only; its comment changes from "trailing
  scalars are not carried" to "this is the required positional prefix; the
  trailing tail is carried by `TrailingOptionalProperties` and appended by the
  create-call builder".
- Add the ordered trailing list from
  `DomainToCSharpExporter.TrailingOptionalProperties(entity, _analysis)` (store
  the `AnalysisResult` the constructor already receives; `Poly.DslCompiler` has
  `InternalsVisibleTo`).
- In `BuildDtoTypes`, emit required members as today, then one nullable member
  per trailing property. Reuse the existing constraint projection (range,
  length, pattern, equality, `[EnumDataType]`) but drop `RequiredConstraint`
  from the trailing copy and pass the **underlying** CLR name (`"long"`,
  `"string"`, the enum type) into `BuildConstraintAttributes`; a `"long?"` /
  `"string?"` spelling silently loses `[Range]` / `[MinLength]` /
  `[RegularExpression]` because those gates match the underlying name.
  Keep the member `init`-only by passing an empty
  `PropertyInitializerDefinitionNode`; nullability makes `= default!` unnecessary.
  Drop the `scalarParams.Count == 0` early return: every root gets a POST that
  references `{Entity}Dto`, so every root must get a DTO (this also covers an
  entity whose only create inputs are omittable).
- Move the DTO-name reservation with the emission: in
  `ComputeActionDtoCollisions`, drop the `GetConstructorOrder(entity).All(p =>
  p.IsNavigation)` skip so every root reserves `{Entity}Dto`. Without it, an
  optional-only root emits `{Entity}Dto` while an action with the same Pascal
  name still claims the plain `{Action}Dto`, producing CS0101 in one
  Program.cs.
- In `AppendCreateCallStatements`, append one argument per trailing property
  after `BuildCreateArgs`, in `TrailingOptionalProperties` order:
  - no `DefaultValueConstraint` → `new Member(new Variable("dto"), prop.Name)`;
  - `EffectLoweringPass.LowerDefaultExpression(constraint.Expression,
    new NamedTypeReference(prop.Type.TypeName), RuntimeAnalysisCache.MeaningFor(_domain),
    RuntimeAnalysisCache.FormsFor(_domain))` non-null → the same member
    (the `Create` sentinel applies the runtime default);
  - else `DomainToCSharpExporter.LowerDefaultConstantNode(constraint, prop,
    _domain, _analysis)` non-null → `new Coalesce(member, constant)`;
  - both null → `NotSupportedException` naming `Entity.Prop`. This is a
    deliberate generation-time fail closed; it is stricter than the exporter,
    which emits a parameter with no default for the same non-literal shape.
- Leave `BuildCreateArgs` and `SeedAsync` alone: the seed path keeps passing
  required-only and therefore keeps exercising the domain defaults.
- Update `Poly.Mcp/Docs/poly-dsl-guide.md` (transport `[Required]` bullet) to
  state the new contract: trailing `optional`/`default(...)` scalars are nullable
  create-DTO members, omission is `null`, `optional` stays absent, a **constant**
  `default(...)` is coalesced at the POST call site, and a **runtime**
  `default(...)` (`Today` / `Now` / `Guid`) is applied by `Create`'s own
  coalesce. Remove the "tracked as a follow-up" sentence; do not leave a TODO.
- Update the `HttpFileGenerator.GetCreateScalarParams` doc comment: the sample
  body mirrors the **required** create arguments only, deliberately omitting the
  trailing scalars so `demo.http` exercises the defaults. No behavior change in
  that file.

### Files

- `src/Poly.DslCompiler/MinimalApiGenerator.cs`
- `src/Poly.DslCompiler/HttpFileGenerator.cs` (comment only)
- `Poly.Tests/DomainModeling/Lowering/MinimalApiGeneratorTests.cs`
- `Poly.Tests/DomainModeling/Compile/EmitGolden/crm-dslcompiler/Program.cs.golden`
  (regenerated, never hand-edited)
- `Poly.Mcp/Docs/poly-dsl-guide.md`

`demo.http.golden` must stay byte-identical. No other file. Do not
touch `EntityStructureAnalyzer`, `EntityStructureMetadata`,
`DomainToCSharpExporter`, `RequiredPropertiesPass`, `BehaviorMetadata`, or
`ConstraintValidation`.

### Done when

Tests are written first (each must be watched failing for the stated reason),
then the implementation makes them pass.

1. `CreateDto_RequiredIsDefault_ProjectsRequiredAttribute` (existing) — the
   `Title` assertion stays exactly as it is; flip the two held-out assertions
   (`Poly.Tests/DomainModeling/Lowering/MinimalApiGeneratorTests.cs`, the
   `Memo`/`Mark` `IsFalse` checks) to `IsTrue`, and assert
   `public string? Memo { get; init; }` / `public string? Mark { get; init; }`
   with no `[Required]`. Replace the "held out separately" comment.
2. `CreateDto_OptionalScalar_IsNullableAndPassedThrough` — `Memo: Text optional`
   is `string?` with no initializer and the POST argument is exactly `dto.Memo`
   (no `??`, no coercion). Add a value-type optional (`Count: Number optional`)
   and assert `long?` + bare `dto.Count`, never `?? 0`.
3. `CreateDto_ConstantDefault_CoalescesInCreateArgs` — `Mark: Text default("x")`
   is `string?` with no initializer, no `[Required]`, and the argument is
   `Coalesce(dto.Mark, "x")`. Assert the full argument order, including a
   same-typed neighbour (`Memo`), so a positional swap cannot pass.
4. `CreateDto_RuntimeDefault_IsForwardedForCreateTimeEvaluation` —
   `Due: Date default(Today)` (and `Opened: DateTime default(Now)`) are `T?`
   with **no initializer**, and the arguments are bare `dto.Due` / `dto.Opened`;
   no `DateOnly.FromDateTime` / `DateTime.UtcNow` / `DateTime.Today` appears in
   the POST lambda. This is the "evaluated at create, not the CLR default" proof.
5. `CreateDto_PresentZeroOrFalse_IsNotOmitted` — `Score: Number default(5)` and
   `Active: Boolean default(true)` are `long?` / `bool?`; the coalesce right-hand
   sides are `5L` / `true` and the left-hand sides are the DTO members. A present
   `0` / `false` is non-null and therefore wins.
6. `CreateDto_ExplicitRequiredWithDefault_IsNotRequiredOnTheDto` —
   `Mark: Text required default("x")` omits `[Required]` on the DTO member (the
   domain makes it omittable at create), and still gets no `[Required]` after the
   declared-constraint copy is filtered. This is the `[Required]` fail-closed
   case for the projection.
7. `CreateDto_TrailingRange_StaysValidWhenOmitted` — a defaulted or optional
   numeric member keeps its `[Range]` (e.g. `Score: Number default(5) range(0, 10)`
   → `[Range(0, 10)] public long? Score`); the attribute must not become a
   `[Required]` clone that rejects omission.
8. `CreateDto_UnlowerableDefault_ThrowsFailClosed` —
   `Score: Number default(1 + 1)`: `default` parses through `ParseExpression`,
   and `ExpressionTypeAnalyzer.CheckDefault` returns without a diagnostic for a
   non-literal, non-`PropertyAccess` expression, so analysis admits it. `Render`
   must throw `NotSupportedException` naming `Note.Score` rather than emit
   `dto.Score` or `0`. Record the red output; do not delete the guard.
9. `CreateDto_OptionalOnlyEntity_EmitsDto` — `Note: entity { Memo: Text optional }`
   emits `NoteDto` with `Memo`; today the POST references a DTO that
   `BuildDtoTypes` skipped. Include a second entity with a parameterized action
   whose Pascal name is `Note` and assert the action DTO is qualified
   (`{Entity}NoteDto`), proving the reservation moved with the emission. This is
   the DTO-existence and CS0101 fail-closed case.
10. `CreateDto_TrailingAfterNavigation_KeepsSignatureOrder` — a root with a
    required scalar, a collection navigation, then a trailing `default(...)`:
    `Note.Create(dto.Title, Enumerable.Empty<Child>(), dto.Score ?? 5L)`. This
    proves the tail lands after navs, which the no-nav example cannot.
11. `CreateDto_TrailingScalars_GeneratedHostCompiles` — compile one domain
    containing a required scalar, a navigation, an optional, a constant default,
    and a runtime default via
    `CompilerWithHttpHost().Compile(..., CompileMode.All, DbmsPack.Sqlite)` and
    `CompileHost(http.Files!)`; assert zero diagnostics. This is the
    signature/order gate: a shifted trailing argument is caught even when the
    types happen to match.
12. `Crm_DslCompilerHostFiles_MatchGolden` (existing) — regenerate
    `crm-dslcompiler/Program.cs.golden` and read the diff. Expected changes only:
    the Account DTO gains `long? ClosedTickets`, `long? OpenPipeline`,
    `long? WonCount`, and the Account POST passes
    `… Enumerable.Empty<Ticket>(), dto.ClosedTickets ?? 0L, dto.OpenPipeline ?? 0L, dto.WonCount ?? 0L`
    after the navs. `Opportunity.Probability` (entry-assigned) stays out of the
    create contract. `SeedAsync`'s `Account.Create(...)` line must not change
    (seed stays required-only), and `demo.http.golden` must not change.

The existing assertion that must flip: the two `IsFalse` checks in
`CreateDto_RequiredIsDefault_ProjectsRequiredAttribute` (lines ~218–219).

### Out of scope (this item)

- `SeedAsync` stays required-only; omitted trailing params use the domain
  defaults.
- `HttpFileGenerator` behavior: the sample body stays required-only. Omitting the
  new fields is valid and exercises the default path; only its doc comment
  changes, and `demo.http.golden` must stay byte-identical.
- Action DTOs and action-parameter validation; `BehaviorMetadata`;
  `RequiredPropertiesPass`; `ConstraintValidation`.
- The exporter `Create` signature, the VM factory slots, and the analyzer's
  trailing list — consumed as-is.
- Executing a generated HTTP host against JSON. There is no in-repo host
  harness; the proof is the emitted shape plus `CompileHost`.
- The guide's `invoke Rel.Action(param:)` constraint-derivation gap.
- A nav-only root that currently emits a POST with no DTO: dropping the
  `scalarParams.Count == 0` return fixes it incidentally; do not add new
  behavior or a separate test for it beyond the compile guard.

## Why one item

The obvious split ("optional first, then `default(...)`") is not independently
mergeable. `Create`'s trailing parameters are one positional tail in name order,
and C# forbids skipping a middle optional argument. A default-only change that
skipped an earlier `optional` slot would bind that value to the wrong parameter —
and with two same-typed strings (`Mark`, `Memo`) it would compile and silently
swap. The DTO member and its POST argument must also land together, or the DTO
would carry a field that is silently dropped. That makes this one seam: the HTTP
create transport contract for trailing omittable scalars.

## Implementer rules

- **Test-first per `AGENTS.md`.** Write the failing assertion, run it red, and
  record the command and output. A test never watched failing is not evidence.
  Then implement to green and keep the test.
- While iterating:
  `dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/MinimalApiGeneratorTests/*'`
  and `dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/EmitGoldenTests/*'`.
  The full suite is the pre-ship gate; run it there and record the pass count.
- Regenerate the CRM host golden with
  `POLY_UPDATE_GOLDEN=1 dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/EmitGoldenTests/*'`,
  then rerun without the variable and read the diff. Never hand-edit a golden.
- Do not weaken, skip, or delete a test to get green. If an existing expectation
  is wrong, say so and say why.
- Do not push. Do not edit the main checkout at `/Users/scoizzle/Projects/Poly/Poly`.
- The generated `Program.cs` is the product. Read the rendered file for the test
  domains end to end; every review finding on it is in scope, nits included.
- No TODO/FIXME, no commented-out branches, no silent fallback. Delete the
  hold-out comments instead of rewriting them as a promise.
- Keep the diff minimal and in the surrounding style; no drive-by refactors.
- Update `Poly.Mcp/Docs/poly-dsl-guide.md` in the same change, then run
  `scripts/restart-poly-mcp.sh` (the guide is served by the MCP server).
- Suggested item commit: `feat(dsl-compiler): carry trailing scalars in the HTTP create DTO`.
