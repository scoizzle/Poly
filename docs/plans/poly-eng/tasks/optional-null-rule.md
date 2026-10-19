# TASK optional-null-rule — `optional` value null rule + transport requiredness

Status: in progress
Branch `slice/analysis-truth`. PR #221. Lane A. Review 1. Implement mill: OpenCode (review on the opposite mill).

Admission: Scot-directed 2026-10-19 ("full send"); not a board row. Open PRs are the live in-flight state.

## Scope

Finish the `optional` constraint PR (#221) so the shipped capability is fail closed end to end:

1. **Arithmetic null rule.** A comparison that reads an `optional` value through arithmetic or
   temporal arithmetic (`DueDate + 14 > Today`, `Qty * 2 > 10`) is false when the operand is
   absent. A value-producing use of an `optional` (assign RHS, initializer, invoke argument,
   arithmetic/library argument) unboxes to the underlying type; absent throws, because there is
   no value to produce — it never folds to a type default.
2. **Assign-side presence.** Range and enum-membership guards on an assign test only a *present*
   value, using the assigned expression's own optionality (not an untyped local), so an absent
   `optional` passes and a present value is validated (`0`/`false` included).
3. **Target-entity optionality.** Path-prefix reads and quantifier bodies (and `sum`/`foreach`)
   resolve optionality from the **target** entity, not the source. Required target properties
   need no presence guard (required ⇒ non-null): the required-target presence proof.
4. **Transport `[Required]`.** The create DTO projects `[Required]` for every not-`optional`
   property; required is the default, so the generator no longer keys on the explicit keyword.

Deliberately held out (documented, separate slice): the generated HTTP create DTO does not yet
carry trailing `optional`/`default(...)` scalars — doing so needs the domain default (or a
nullable field that coalesces) in the DTO, otherwise the CLR default overrides `default(...)`.

## Files

- `Poly/DomainModeling/Lowering/DomainExpressionLoweringPass.cs` — guard collection recurses
  through arithmetic/temporal; path-prefix target optionality; quantifier-body context.
- `Poly/DomainModeling/Lowering/EffectLoweringPass.cs` — assign guards; optional predicate flows
  into every lowering context.
- `Poly/DomainModeling/Lowering/DomainExpressionLoweringPass.cs` — guard collection, value-context
  unbox, path-prefix/binder entity scoping.
- `Poly/DomainModeling/Lowering/LoweringContext.cs` — target-entity and binder-entity wiring.
- `Poly/DomainModeling/Lowering/DomainToCSharpExporter.Notify.cs` — create guard shape (shared).
- `Poly/DomainModeling/Libraries/Temporal/TemporalLowering.cs` — path-aware date operand types.
- `src/Poly.DslCompiler/MinimalApiGenerator.cs` — `[Required]` keyed on not-`optional`.
- `Poly.Mcp/Docs/poly-dsl-guide.md` — arithmetic rule, target-entity presence, `[Required]`.
- Tests: `Poly.Tests/DomainModeling/Analysis/OptionalConstraintTests.cs`,
  `Poly.Tests/Interpretation/OptionalScalarVmTests.cs`,
  `Poly.Tests/DomainModeling/Lowering/DomainToCSharpExporterTests.cs`, plus a compiler/API test
  for `[Required]`.

## Shape matrix

| Input kind | Before | After | Test that proves it |
|---|---|---|---|
| `DueDate + 14 > Today`, `DueDate` optional | unguarded; print does not compile | presence guard on the operand; absent ⇒ false | lowering + VM + print compile |
| `Qty * 2 > 10`, `Qty` optional Number | unguarded; VM throws on absent | presence guard; absent ⇒ false; present `0` tested | lowering + VM |
| `assign Total to Qty + 1`, `Qty` optional | non-compiling print / VM throw | unboxes; absent throws | print compile + VM throw |
| assign optional Number with `range(...)`, absent RHS | VM throws; print silently skips | absent passes; present validated | lowering + VM + print |
| policy reads optional target prop through `rel` | source-entity predicate | target-entity presence guard | lowering |
| quantifier body reads optional target prop | source-entity predicate | target-entity presence guard | lowering |
| create DTO, not-`optional` prop | no `[Required]` unless keyword written | `[Required]` | compiler test |

## Done when

- Every `optional` read in a comparison (direct, arithmetic, temporal, path-prefix, quantifier,
  `sum`/`foreach`) is guarded on presence, and absent ⇒ false in both print and VM.
- A value-producing use of an `optional` unboxes; absent throws in both print and VM (no
  silent type default).
- Assign guards test only present values; print and VM agree for absent and present (`0`/`false`).
- Not-`optional` create DTO members carry `[Required]`.
- Full suite green; guide updated in the same change.

## SHIP if

Full suite green on the final SHA, CI green on that SHA, no 🔴/🟠 open, guide matches behaviour.

## NOT SHIP if

A lowered shape does not compile, print and VM diverge, or a `default(...)`/`optional` case
silently changes value.

## Tests

`dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/<Class>/*'` per class,
then the full suite at the gate.

## Hand-edit

Each rule is one guard-collection method plus one analyzer check; a reader sees the presence
test (`!= null`) and the false-on-absent shape without an agent.

## Needs Scot

none.

## Log

| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-19 | Scot | — | `224a507d` | plan: full null rule, transport `[Required]`, merge on Scot's OK | — |
| 2026-10-19 | OpenCode | — | — | implement: arithmetic/temporal/boolean guards, value-context unbox, target-entity optionality, assign presence, `sum` skip, `[Required]` | full suite 4131 passed |
| 2026-10-19 | OpenCode | — | `761d9293` | review round 1: 4 bugs (path-prefix/binder leaves), 1 suggestion, 1 nit | fixed in `44e3cd4a` + follow-up |
| 2026-10-19 | OpenCode | — | — | review round 2: for-each resolver scoping, multi-hop date type; fix + tests | full suite 4137 passed |
